using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Maintenance_app;
using Maintenance_app.Dashboard;
using MahApps.Metro.Controls;
using Microsoft.Data.SqlClient;

namespace MaintenanceSetup;

public partial class SetupWindow : MetroWindow
{
    #region إدخال اتصال SQL والتحقق منه

    private bool busy;
    private string? executable;

    public SetupWindow()
    {
        InitializeComponent();
        // يميّز اسم النسخة نافذة تثبيت الفروع قبل إدخال بيانات الاتصال.
        if (!DeploymentEdition.IncludesBranchReports)
            SetupHeading.Text = Title += " – نسخة الفروع";
        ServerName.Focus();
    }

    private string? ReadConnection()
    {
        string server = ServerName.Text.Trim();
        string user = SqlUser.Text.Trim();
        string password = SqlPassword.Password;
        // يدخل المستخدم اعتماد SQL مباشرة، ويستخدمه المثبّت لاختبار الاتصال والترحيل.
        bool invalidServer = server.Length == 0;
        bool invalidUser = user.Length == 0;
        bool invalidPassword = password.Length == 0;
        MarkError(ServerName, ServerError, invalidServer);
        MarkError(SqlUser, UserError, invalidUser);
        MarkError(SqlPassword, PasswordError, invalidPassword);

        // تظهر أخطاء الحقول بعد طلب التنفيذ، ويُنقل التركيز بعد إعادة تفعيلها.
        if (invalidServer || invalidUser || invalidPassword)
            return null;

        var settings = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = Installation.DestinationDatabase,
            UserID = user,
            Password = password,
            IntegratedSecurity = false,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            // يُقبل اعتماد شهادة الخادم ضمن إعداد الاتصال مع إبقاء التشفير مفروضًا.
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            ApplicationName = Installation.ProductName
        };
        return settings.ConnectionString;
    }

    private static void MarkError(Control field, TextBlock hint, bool error)
    {
        hint.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        if (error)
            field.BorderBrush = Brushes.Firebrick;
        else
            field.ClearValue(BorderBrushProperty);
    }

    private void Connection_Changed(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, ServerName) && ServerError is not null)
            MarkError(ServerName, ServerError, false);
        if (ReferenceEquals(sender, SqlUser) && UserError is not null)
            MarkError(SqlUser, UserError, false);
        if (ReferenceEquals(sender, SqlPassword) && PasswordError is not null)
            MarkError(SqlPassword, PasswordError, false);
    }

    #endregion

    #region تنفيذ التثبيت وعرض نتيجته

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async connectionString =>
        {
            StatusText.Text = "جارٍ اختبار اتصال SQL…";
            await using var connection = new SqlConnection(connectionString);
            await Installation.VerifyConnectionAsync(connection);
            StatusText.Text = "نجح الاتصال بقاعدة Maintenance_Dabase.";
        });
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        // لا يبدأ ترحيل القاعدة القديمة إلا إذا فُعّل الخيار في هذه الجلسة.
        bool migrateLegacyData = MigrateLegacyData.IsChecked == true;
        await RunAsync(async connectionString =>
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            (executable, string summary) = await Installation.InstallAsync(connectionString, migrateLegacyData, progress);
            MigrateLegacyData.IsChecked = false;
            SqlPassword.Clear();
            StatusText.Text = "اكتمل التثبيت لهذا الحساب. " + summary;
            LaunchButton.Visibility = Visibility.Visible;
            LaunchButton.Focus();
        });
    }

    private async Task RunAsync(Func<string, Task> action)
    {
        if (busy)
            return;
        SetBusy(true);
        try
        {
            string? connectionString = ReadConnection();
            if (connectionString is not null)
                await action(connectionString);
        }
        catch (Exception ex)
        {
            StatusText.Text = "تعذّر إكمال العملية.";
            // تعرض رسائل SQL التفاصيل اللازمة لتشخيص الاتصال أو الترحيل دون طباعة سلسلة الاتصال.
            UiMessages.Show(this, Title, ex.Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            if (ServerError.Visibility == Visibility.Visible)
                ServerName.Focus();
            else if (UserError.Visibility == Visibility.Visible)
                SqlUser.Focus();
            else if (PasswordError.Visibility == Visibility.Visible)
                SqlPassword.Focus();
            else if (LaunchButton.Visibility == Visibility.Visible)
                LaunchButton.Focus();
        }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        ConnectionFields.IsEnabled = !value;
        TestButton.IsEnabled = !value;
        InstallButton.IsEnabled = !value;
        CloseButton.IsEnabled = !value;
        LaunchButton.IsEnabled = !value;
        BusyProgress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (executable is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable)
            });
            Close();
        }
        catch (Exception ex)
        {
            UiMessages.Show(this, Title, ex.Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // يمنع الإغلاق أثناء اعتماد المعاملة أو نقل الملفات.
    private void Window_Closing(object? sender, CancelEventArgs e) => e.Cancel = busy;

    #endregion
}
