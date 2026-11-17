using System.IO;

namespace MaintenancePackaging;

internal static class InstallerShortcuts
{
    #region اختصارات قائمة ابدأ وسطح المكتب

    public static void Create(string executable, string name)
    {
        // نستخدم مجلدات حساب Windows الفعلية، بما فيها سطح المكتب عند إعادة توجيهه.
        string desktop = GetShortcutPath(Environment.SpecialFolder.DesktopDirectory, name);
        string programs = GetShortcutPath(Environment.SpecialFolder.Programs, name);
        Type shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
        object shell = Activator.CreateInstance(shellType)!;

        byte[]? previousDesktop = File.Exists(desktop) ? File.ReadAllBytes(desktop) : null;
        WriteShortcut(shell, desktop, executable);
        try
        {
            WriteShortcut(shell, programs, executable);
        }
        catch
        {
            // إذا تعذر اختصار ابدأ، نعيد سطح المكتب إلى حالته السابقة قبل تراجع التثبيت.
            if (previousDesktop is null)
                File.Delete(desktop);
            else
                File.WriteAllBytes(desktop, previousDesktop);
            throw;
        }
    }

    private static string GetShortcutPath(Environment.SpecialFolder folder, string name)
    {
        string directory = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("تعذّر الوصول إلى مجلد اختصارات Windows لهذا الحساب.");
        return Path.Combine(directory, name + ".lnk");
    }

    private static void WriteShortcut(object shell, string path, string executable)
    {
        // نحفظ الاختصار المؤقت في نفس المجلد، ثم نعتمده بعد كتابة الهدف والأيقونة.
        string temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + ".lnk");
        try
        {
            dynamic shortcut = ((dynamic)shell).CreateShortcut(temporaryPath);
            shortcut.TargetPath = executable;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
            shortcut.IconLocation = executable + ",0";
            shortcut.Save();
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    #endregion
}
