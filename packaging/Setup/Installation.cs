using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Maintenance_app.DatabContext;
using MaintenancePackaging;
using Microsoft.Data.SqlClient;

namespace MaintenanceSetup;

internal static class Installation
{
    #region إعداد قاعدة البيانات

    public const string ProductName = "منظومة الخدمات والصيانة";
    public const string DestinationDatabase = "Maintenance_Dabase";
    private static readonly Assembly Resources = typeof(Installation).Assembly;
    private static string InstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SSF", "MaintenanceServices");
    private static string ApplicationDirectory => Path.Combine(InstallRoot, "Application");

    // يتحقق الاختبار من وجود الجداول الأساسية قبل تثبيت نسخة لا يمكنها الاتصال.
    public static async Task VerifyConnectionAsync(SqlConnection connection)
    {
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN COL_LENGTH(N'dbo.T_Emergency_MaintenanceCosts', N'Serv_num') IS NOT NULL
                         AND COL_LENGTH(N'dbo.T_Observer', N'User_id') IS NOT NULL
                         AND OBJECT_ID(N'dbo.T_SystemUser', N'U') IS NOT NULL
                         AND OBJECT_ID(N'dbo.T_Branch', N'U') IS NOT NULL
                        THEN 1 ELSE 0 END;
            """;
        if ((int)(await command.ExecuteScalarAsync())! != 1)
            throw new InvalidOperationException("الوجهة لا تحتوي على مخطط المنظومة الحديثة أو أن الحساب لا يملك صلاحية قراءته.");
    }

    private static async Task<string> MigrateAsync(SqlConnection connection)
    {
        // يُنفَّذ النص دفعة واحدة على الاتصال نفسه حتى تبقى المعاملة وجداولها المؤقتة متماسكة.
        using var script = new StreamReader(OpenResource("Migrate.sql"));
        using var command = connection.CreateCommand();
        command.CommandText = await script.ReadToEndAsync();
        command.CommandTimeout = 900;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("لم يُرجع SQL Server نتيجة الترحيل.");

        if (reader.GetBoolean(0))
            return "سبق ترحيل قاعدة الوجهة بنجاح.";

        return $"نُقل {reader.GetInt64(1):N0} تكليفًا و{reader.GetInt64(2):N0} حركة تدقيق. "
             + $"حُفظت {reader.GetInt64(3):N0} حسابات أرشيفية معطّلة، "
             + $"وأنواع {reader.GetInt64(4):N0} تكليفًا متعدد الأنواع.";
    }

    #endregion

    #region تثبيت الملفات وإنشاء الاختصار

    // يُفك التطبيق داخل مجلد مؤقت أولًا، ثم تُستبدل النسخة السابقة بعد نجاح الاتصال والترحيل.
    public static async Task<(string Executable, string Summary)> InstallAsync(
        string connectionString, bool migrateLegacyData, IProgress<string> progress)
    {
        string root = InstallRoot;
        Directory.CreateDirectory(root);
        using var installationLock = AcquireInstallationLock(root);
        string stage = Path.Combine(root, "stage-" + Guid.NewGuid().ToString("N"));
        string previous = Path.Combine(root, "previous-" + Guid.NewGuid().ToString("N"));
        string application = ApplicationDirectory;
        string executable = Path.Combine(application, ProductName + ".exe");

        CheckInstalledApplicationClosed(executable);

        try
        {
            progress.Report("جارٍ اختبار الاتصال بقاعدة الوجهة…");
            await using var connection = new SqlConnection(connectionString);
            await VerifyConnectionAsync(connection);

            progress.Report("جارٍ تجهيز ملفات المنظومة ومكونات التشغيل…");
            await Task.Run(() =>
            {
                using var archive = new ZipArchive(OpenResource("Application.zip"), ZipArchiveMode.Read);
                archive.ExtractToDirectory(stage);
                if (!File.Exists(Path.Combine(stage, ProductName + ".exe")))
                    throw new InvalidOperationException("ملف تشغيل المنظومة مفقود من الحزمة.");
                InstalledConnectionSettings.Write(stage, connectionString);
            });

            // تحديث الملفات يكتفي بفحص المخطط؛ الترحيل الاختياري يفحص سجل الاكتمال قبل أي كتابة.
            string summary = "تم تحديث ملفات المنظومة. لم تُعدّل قاعدة البيانات.";
            if (migrateLegacyData)
            {
                progress.Report("جارٍ التحقق من الترحيل ونقل البيانات عند الحاجة…");
                summary = await MigrateAsync(connection);
            }

            progress.Report("جارٍ اعتماد التثبيت وإنشاء اختصاري ابدأ وسطح المكتب…");
            CheckInstalledApplicationClosed(executable);
            try
            {
                if (Directory.Exists(application))
                    Directory.Move(application, previous);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                throw new InvalidOperationException("ملفات المنظومة قيد الاستخدام. أغلق نافذة المنظومة ثم أعد محاولة التثبيت.", ex);
            }

            try
            {
                Directory.Move(stage, application);
                InstallerShortcuts.Create(executable, ProductName);
            }
            catch
            {
                if (Directory.Exists(application))
                    Directory.Move(application, stage);
                if (Directory.Exists(previous))
                    Directory.Move(previous, application);
                throw;
            }

            CleanStaging(previous);
            return (executable, summary);
        }
        finally
        {
            CleanStaging(stage);
        }
    }

    private static FileStream AcquireInstallationLock(string root)
    {
        // يُميّز القفل بين وجود نافذة تثبيت أخرى ووجود نسخة مفتوحة من المنظومة.
        try
        {
            return new FileStream(Path.Combine(root, "setup.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            throw new InvalidOperationException("توجد عملية تثبيت أخرى قيد التنفيذ. انتظر اكتمالها ثم أعد المحاولة.", ex);
        }
    }

    private static void CheckInstalledApplicationClosed(string executable)
    {
        // نقارن المسار حتى لا نمنع التثبيت بسبب نسخة تعمل من مجلد مختلف.
        var processes = Process.GetProcessesByName(ProductName);
        try
        {
            foreach (var process in processes)
            {
                string? runningExecutable;
                try
                {
                    runningExecutable = process.MainModule?.FileName;
                }
                catch (Win32Exception) { continue; }
                catch (InvalidOperationException) { continue; }

                if (string.Equals(runningExecutable, executable, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("المنظومة مفتوحة الآن. أغلق نافذة «منظومة الخدمات والصيانة» ثم أعد محاولة التثبيت.");
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static bool IsSharingViolation(IOException error) =>
        (error.HResult & 0xFFFF) is 32 or 33;

    private static Stream OpenResource(string name) =>
        Resources.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException("مورد تثبيت مطلوب مفقود من الحزمة.");

    private static void CleanStaging(string path)
    {
        // قد يحتفظ Windows مؤقتًا بمقبض ملف من النسخة السابقة بعد تشغيلها.
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    #endregion
}
