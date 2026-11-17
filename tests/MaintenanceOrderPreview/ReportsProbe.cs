using DevExpress.Xpf.Core;
using DevExpress.Xpf.Grid;
using Maintenance_app.dbContext;
using Maintenance_app.Accounts;
using System.Reflection;
using System.Windows.Documents;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Maintenance_app.reports;
using Maintenance_app.support_Data;
using Maintenance_app.Models;
using DevExpress.Xpf.Editors;
using MahApps.Metro.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Reporting.WinForms;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using ListView = System.Windows.Controls.ListView;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;
using Application = System.Windows.Application;

internal static class ReportsProbe
{
    #region أدوات فحص الربط المرئي
    // يبحث داخل النافذة الفعلية عن زر الوصول السريع باسمه، كما يراه المستخدم في الرئيسية.
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void Pump() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

    // ينتظر اكتمال استعلامات EF غير المتزامنة مع استمرار معالجة رسائل واجهة WPF.
    private static void PumpUntil(Func<bool> condition, int timeoutMilliseconds = 3000)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var started = Environment.TickCount64;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            if (condition() || Environment.TickCount64 - started >= timeoutMilliseconds) frame.Continue = false;
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static object? Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    // يضغط بطاقة الإعدادات الظاهرة عبر واجهة أتمتة الزر ويفحص النافذة التي تفتح فعليًا.
    private static void CheckAccountWindow<T>(Window owner, string title, string field, Key? shortcut = null) where T : Window
    {
        ((System.Windows.Controls.RadioButton)owner.FindName("SettingsTab")).IsChecked = true;
        owner.UpdateLayout();
        Pump();
        var commands = (ItemsControl)owner.FindName("SectionCommands");
        var button = Descendants(commands).OfType<SimpleButton>().Single(item => AutomationProperties.GetName(item) == title);
        if (!button.IsEnabled || !button.IsVisible)
            throw new InvalidOperationException($"Settings button unavailable: {title}, enabled={button.IsEnabled}, visible={button.IsVisible}.");
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        var invoke = (IInvokeProvider?)peer?.GetPattern(PatternInterface.Invoke)
            ?? throw new InvalidOperationException("Settings button has no invoke pattern: " + title);
        bool opened = false;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<T>().SingleOrDefault(window => window.IsVisible);
            if (dialog is null) return;
            timer.Stop();
            opened = dialog.WindowState == WindowState.Maximized && dialog.Owner == owner && dialog.FindName(field) is not null;
            dialog.Close();
        };
        timer.Start();
        try
        {
            if (shortcut is Key key)
            {
                // يبدأ الاختصار من زر له التركيز كما يحدث عند ضغط المفتاح في الرئيسية.
                button.Focus();
                var input = new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button),
                    Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent };
                button.RaiseEvent(input);
            }
            else invoke.Invoke();
            PumpUntil(() => opened);
        }
        finally { timer.Stop(); }
        if (!opened) throw new InvalidOperationException("Visible settings button did not open " + typeof(T).Name);
    }

    // يغلق رسالة النجاح فقط ويترك النافذة قيد الفحص لتأكيد تغير البيانات الفعلي.
    private static void WithMessageDismissal(Window owner, Action action)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) =>
        {
            foreach (Window window in Application.Current.Windows)
                if (window.Owner == owner && window.IsVisible)
                {
                    Console.WriteLine("PROBE: " + string.Join(" | ", Descendants(window).OfType<TextBlock>().Select(text => text.Text).Where(text => !string.IsNullOrWhiteSpace(text))));
                    window.Close();
                    break;
                }
        };
        timer.Start();
        try { action(); }
        finally { timer.Stop(); }
    }

    // يمنع اختبار الواجهة من إظهار تأكيد الإغلاق التفاعلي عند انتهاء الفحص المعزول.
    private static void CloseMain(Maintenance_app.MainWindow window)
    {
        typeof(Maintenance_app.MainWindow).GetField("confirmedClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
        window.Close();
    }

    // يفحص قوالب Report_Maint الأصلية بعد نسخها إلى مخرجات التطبيق؛ لا نكتفي بوجود الملف لأن خطأ الحقول لا يظهر إلا عند التصدير.
    private static void VerifyLegacySummaryTemplates()
    {
        var directory = Path.Combine(Path.GetDirectoryName(typeof(MaintenanceReportsWindow).Assembly.Location)!, "Reports");
        foreach (var name in new[] { "Maint_grop.rdlc", "Maint_grop_date.rdlc", "Maint_grop_Build.rdlc" })
        {
            var reportPath = Path.Combine(directory, name);
            if (!File.Exists(reportPath)) throw new FileNotFoundException("قالب كشف الصيانة غير موجود ضمن مخرجات التطبيق.", reportPath);

            var table = new DataTable("DataSet1");
            for (var index = 1; index <= 54; index++) table.Columns.Add("elm" + index, typeof(string));
            var row = table.NewRow();
            row["elm1"] = "1202600001"; row["elm2"] = "مبنى الاختبار"; row["elm3"] = "05/11/2026";
            row["elm4"] = "الأجهزة"; row["elm5"] = "فني الاختبار"; row["elm6"] = "طارئة"; row["elm7"] = "منجزة";
            row["elm14"] = "كشف خاص بتكاليف الصيانة"; row["elm17"] = "إجمالي سنة: 2026"; row["elm24"] = "الفرع";
            table.Rows.Add(row);
            using var report = new LocalReport { ReportPath = reportPath };
            report.DataSources.Add(new ReportDataSource("DataSet1", table.DefaultView));
            if (report.Render("PDF").Length == 0) throw new InvalidOperationException("تصدير قالب كشف الصيانة أعاد ملفًا فارغًا: " + name);
        }
    }
    #endregion

    #region فتح واجهات التقارير من الرئيسية نفسها
    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new Maintenance_app.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            // يمنع مسار اختبار النوافذ من تشغيل تسجيل الدخول الإنتاجي عند أول Window.Show.
            var startup = typeof(Maintenance_app.App).GetMethod("Application_Startup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            VerifyLegacySummaryTemplates();
            Console.WriteLine("PROBE: legacy report templates rendered");
            // قاعدة SQLite معزولة تمنع نوافذ الاختبار من انتظار اتصال قاعدة العمل الحقيقي.
            using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
            var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
            Maintenance_DabaseContext Create() => new(options);
            using (var db = Create())
            {
                db.Database.EnsureCreated();
                // بيانات معزولة تؤكد أن قوائم الفلاتر وواجهة البيانات المساعدة تقرأ مصادرها المباشرة.
                db.T_Branches.Add(new T_Branch { Branch_ID = 1, Branch = "فرع الاختبار", st = true });
                db.T_Locations.Add(new T_Location { Location = "الموقع", Location_ID = 1, Branch_ID = 1 });
                db.T_Builds.Add(new T_Build { Location = "الموقع", Location_ID = 1, Branch_ID = 1, Build_ID = 1, Build = "مبنى الاختبار", address = "العنوان" });
                db.T_Services.Add(new T_Service { Location = "الموقع", Location_ID = 1, Branch_ID = 1, Serv_num = 1, Serv_type = "الأجهزة" });
                db.T_Employes.Add(new T_Employe { Location = "الموقع", Location_ID = 1, Branch_ID = 1, Employe_ID = 1, Emp_Name = "فني الاختبار" });
                db.T_Manages.Add(new T_Manage { Location = "الموقع", Location_ID = 1, Branch_ID = 1, Manag_ID = 1, Manag = "إدارة الاختبار" });
                db.T_Sections.Add(new T_Section { Location = "الموقع", Location_ID = 1, Branch_ID = 1, Secti_ID = 1, Manag_ID = 1, Secti = "قسم الاختبار", Date_Modify = DateOnly.FromDateTime(DateTime.Today) });
                db.T_Emergency_MaintenanceCosts.Add(new T_Emergency_MaintenanceCost { Location = "الموقع", Location_ID = 1, Branch_ID = 1,
                    Build_ID = 1, Serv_num = 1, Emergency_MaintenanceCosts_No = 1, Date_com = DateOnly.FromDateTime(DateTime.Today) });
                db.T_SystemUsers.Add(new T_SystemUser { User_ID = 1, User_Name = "مدير الاختبار", Location = "الموقع",
                    PassWord = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("السابقة")), foruser = true });
                db.SaveChanges();
            }
            Console.WriteLine("PROBE: direct windows");
            // نتحقق من إنشاء جميع عناصر XAML قبل اتصال التحميل الفعلي.
            var reports = new MaintenanceReportsWindow(Create) { WindowState = WindowState.Normal, Width = 1280, Height = 800, ShowActivated = false };
            reports.Show();
            PumpUntil(() => ((ComboBoxEdit)reports.FindName("BuildingFilter")).ItemsSource is System.Collections.ICollection collection && collection.Count > 0);
            Console.WriteLine("PROBE: report direct shown");
            // يتحقق من البطاقة الجديدة: حقول التاريخ والفلترة وشكل الطباعة ولوحة المعاينة المعرفة في XAML.
            if (reports.FindName("ReportsGrid") is null || reports.FindName("FromDate") is null
                || reports.FindName("StatusFilter") is null || reports.FindName("PrintMode") is null
                || reports.FindName("MaintenancePrintFlyout") is null)
                throw new InvalidOperationException("Report filters or print preview did not load.");
            if (((GridControl)reports.FindName("ReportsGrid")).ItemsSource is System.Collections.ICollection initialRows && initialRows.Count != 0)
                throw new InvalidOperationException("Reports must remain empty until the user presses Show.");
            // تحقّق مرئي من أن قالب الخلية يعرض Value بدل إنشاء صفوف مخططة فارغة.
            var reportGrid = (GridControl)reports.FindName("ReportsGrid");
            reportGrid.ItemsSource = new[] { new { Number = "1202600001", Date = "05/11/2026", Building = "مبنى الاختبار", Service = "الأجهزة", MaintenanceType = "طارئة", Status = "منجزة", Details = "تفصيل الاختبار" } };
            Pump();
            if (!Descendants(reports).OfType<TextBlock>().Any(text => text.Text == "1202600001"))
                throw new InvalidOperationException("Report grid cell template did not display the row value.");
            var buildingsSource = ((ComboBoxEdit)reports.FindName("BuildingFilter")).ItemsSource as System.Collections.ICollection;
            var servicesSource = ((ComboBoxEdit)reports.FindName("ServiceFilter")).ItemsSource as System.Collections.ICollection;
            var techniciansSource = ((ComboBoxEdit)reports.FindName("TechnicianFilter")).ItemsSource as System.Collections.ICollection;
            if (buildingsSource?.Count is not > 0 || servicesSource?.Count is not > 0 || techniciansSource?.Count is not > 0)
                throw new InvalidOperationException("Report filter sources did not load from support-data tables. Status: " + ((TextBlock)reports.FindName("ReportsStatus")).Text);
            reports.Close();
            Console.WriteLine("PROBE: report direct closed");
            var statistics = new MaintenanceStatisticsWindow(Create) { WindowState = WindowState.Normal, Width = 1280, Height = 800, ShowActivated = false };
            statistics.Show();
            Console.WriteLine("PROBE: statistics direct shown");
            if (statistics.FindName("StatisticsYear") is null || statistics.FindName("CompletedProgress") is null || statistics.FindName("NeedsReportBar") is null
                || statistics.FindName("PrintStatisticsButton") is null)
                throw new InvalidOperationException("Statistics charts did not load.");
            // فحص فتح قالب الإحصائية الفعلي في show_report موجود في DailyInspectionProbe.
            statistics.Close();
            Console.WriteLine("PROBE: statistics direct closed");
            // كل مصدر بيانات يفتح في نافذته المستقلة وتبقى خانات الإدخال ومصدر الصفوف صالحين.
            for (var supportKind = 0; supportKind < 5; supportKind++)
            {
                var support = new MaintenanceSupportWindow(Create, supportKind) { ShowActivated = false };
                support.Show();
                PumpUntil(() => ((GridControl)support.FindName("RowsGrid")).ItemsSource is System.Collections.ICollection collection && collection.Count > 0);
                if (support.WindowState != WindowState.Normal || support.ActualWidth > 730 ||
                    support.FindName("AddNameInput") is not TextEdit addInput || !addInput.IsVisible ||
                    support.FindName("ScopeInput") is not null)
                    throw new InvalidOperationException("Compact support-data window did not expose direct entry.");
                if (((GridControl)support.FindName("RowsGrid")).ItemsSource is not System.Collections.ICollection supportRows || supportRows.Count == 0)
                    throw new InvalidOperationException("Maintenance support-data tables did not load.");
                if (support.FindName("SupportEditFlyout") is not Flyout supportFlyout || supportFlyout.IsOpen)
                    throw new InvalidOperationException("Support-data edit Flyout must be closed during entry.");
                addInput.Text = "مسودة مستقلة";
                ((GridControl)support.FindName("RowsGrid")).SelectedItem = supportRows.Cast<object>().First();
                typeof(MaintenanceSupportWindow).GetMethod("OpenSelectedRowForEdit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(support, null);
                Pump();
                if (!supportFlyout.IsOpen || supportFlyout.Header?.ToString()?.StartsWith("تعديل") != true)
                    throw new InvalidOperationException("Support-data edit Flyout did not open.");
                supportFlyout.IsOpen = false;
                if (addInput.Text != "مسودة مستقلة") throw new InvalidOperationException("Closing edit erased direct entry.");
                if (supportKind == 1)
                {
                    addInput.Text = "";
                    ((Task)Invoke(support, "SaveAsync", true)!).GetAwaiter().GetResult();
                    if (((TextBlock)support.FindName("AddValidationText")).Visibility != Visibility.Visible)
                        throw new InvalidOperationException("Missing support name did not show inline error.");
                    addInput.Text = "خدمة مضافة";
                    if (((TextBlock)support.FindName("AddValidationText")).Visibility != Visibility.Collapsed)
                        throw new InvalidOperationException("Typing did not clear the support validation.");
                    WithMessageDismissal(support, () => ((Task)Invoke(support, "SaveAsync", true)!).GetAwaiter().GetResult());
                    using var db = Create();
                    var added = db.T_Services.AsNoTracking().Single(item => item.Serv_type == "خدمة مضافة");
                    if (added.Location != "الموقع" || added.Location_ID != 1 || added.Branch_ID != 1 || supportFlyout.IsOpen)
                        throw new InvalidOperationException("Direct service entry did not preserve the automatic scope.");
                }
                support.Close();
            }

            // هذا الجزء يختبر النقر الحقيقي على بطاقتي «تقارير الصيانة» و«إحصائية الصيانة» في الرئيسية.
            Console.WriteLine("PROBE: main window");
            using (var db = Create()) Maintenance_app.App.appuser = db.T_SystemUsers.AsNoTracking().Single();
            var main = new Maintenance_app.MainWindow(Create) { WindowState = WindowState.Normal, Width = 1280, Height = 800, ShowActivated = true };
            main.Show(); Pump();
            Console.WriteLine("PROBE: main shown");

            var quickAccess = (ItemsControl)main.FindName("QuickAccessCommands");
            var reportAction = quickAccess.Items.OfType<Maintenance_app.MainWindow.WorkspaceAction>().Single(action => action.Title == "تقارير الصيانة");
            var statisticsAction = quickAccess.Items.OfType<Maintenance_app.MainWindow.WorkspaceAction>().Single(action => action.Title == "إحصائية الصيانة");
            var quickAccessClick = typeof(Maintenance_app.MainWindow).GetMethod("QuickAccess_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            // نستدعي الحدث نفسه الذي يتلقاه زر البطاقة، مع DataContext المطابق لعنصر القائمة الفعلي.
            quickAccessClick.Invoke(main, [new System.Windows.Controls.Button { DataContext = reportAction }, new RoutedEventArgs()]); Pump();
            Console.WriteLine("PROBE: report click raised");
            var openedReport = Application.Current.Windows.OfType<MaintenanceReportsWindow>().SingleOrDefault();
            if (openedReport is null || !openedReport.IsVisible) throw new InvalidOperationException("Reports button did not open its window.");

            quickAccessClick.Invoke(main, [new System.Windows.Controls.Button { DataContext = statisticsAction }, new RoutedEventArgs()]); Pump();
            Console.WriteLine("PROBE: statistics click raised");
            var openedStatistics = Application.Current.Windows.OfType<MaintenanceStatisticsWindow>().SingleOrDefault();
            if (openedStatistics is null || !openedStatistics.IsVisible) throw new InvalidOperationException("Statistics button did not open its window.");

            openedReport.Close(); openedStatistics.Close();
            Invoke(main, "OpenMaintenanceSupportData", 1);
            var openedSupport = Application.Current.Windows.OfType<MaintenanceSupportWindow>().Single();
            if (openedSupport.WindowState != WindowState.Normal) throw new InvalidOperationException("Opening from main maximized support data.");
            Invoke(main, "OpenMaintenanceSupportData", 1);
            if (openedSupport.WindowState != WindowState.Normal) throw new InvalidOperationException("Reopening maximized support data.");
            openedSupport.Close();

            // يمر الاختبار عبر أمري الإعدادات الفعليين ويغلق النافذتين من حلقة الحوار.
            CheckAccountWindow<UsersWindow>(main, "المستخدمون والصلاحيات", "AccountList");
            CheckAccountWindow<ChangePasswordWindow>(main, "تغيير كلمة المرور", "CurrentPassword");
            var users = (UsersWindow)Activator.CreateInstance(typeof(UsersWindow), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [1, (Func<Maintenance_DabaseContext>)Create], null)!;
            users.Show();
            PumpUntil(() => ((ListView)users.FindName("AccountList")).Items.Count == 1);
            ((Task)Invoke(users, "OpenEditorAsync", new object[] { null! })!).GetAwaiter().GetResult();
            ((TextBox)users.FindName("AccountName")).Text = "حساب اختبار جديد";
            ((PasswordBox)users.FindName("AccountPassword")).Password = "كلمة أولية";
            ((PasswordBox)users.FindName("ConfirmPassword")).Password = "كلمة أولية";
            Invoke(users, "Save_Click", users, new RoutedEventArgs());
            PumpUntil(() => ((ListView)users.FindName("AccountList")).Items.Count == 2);
            using (var db = Create())
            {
                var created = db.T_SystemUsers.AsNoTracking().Single(user => user.User_Name == "حساب اختبار جديد");
                if (created.foruser || created.Location != "الموقع" || Maintenance_app.App.decrypt(created.PassWord) != "كلمة أولية")
                    throw new InvalidOperationException("Account creation stored an invalid scope, permission, or password.");
            }
            users.Close();

            // فحص الرفض والحفظ الفعلي من واجهة كلمة المرور ومطابقة ترميز تسجيل الدخول.
            var password = (ChangePasswordWindow)Activator.CreateInstance(typeof(ChangePasswordWindow), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [Maintenance_app.App.appuser, (Func<Maintenance_DabaseContext>)Create], null)!;
            password.Show();
            var current = (PasswordBox)password.FindName("CurrentPassword");
            current.Password = "غير صحيحة";
            ((PasswordBox)password.FindName("NewPassword")).Password = "الجديدة";
            ((PasswordBox)password.FindName("ConfirmPassword")).Password = "الجديدة";
            Invoke(password, "Save_Click", password, new RoutedEventArgs());
            PumpUntil(() => Validation.GetHasError(current));
            if (!Validation.GetHasError(current)) throw new InvalidOperationException("Incorrect current password was not rejected inline.");
            current.Password = "السابقة";
            if (Validation.GetHasError(current)) throw new InvalidOperationException("Editing did not clear the password error.");
            WithMessageDismissal(password, () => Invoke(password, "Save_Click", password, new RoutedEventArgs()));
            using (var db = Create())
                if (Maintenance_app.App.decrypt(db.T_SystemUsers.AsNoTracking().Single(user => user.User_ID == 1).PassWord) != "الجديدة")
                    throw new InvalidOperationException("Password was not saved using the existing login format.");
            CloseMain(main);

            // التشغيل المعتاد يعرض الرئيسية من حوار الدخول؛ نختبر الاختصارات من رئيسية مشروطة كذلك.
            var modalMain = new Maintenance_app.MainWindow(Create);
            Exception? shortcutError = null;
            modalMain.ContentRendered += (_, _) =>
            {
                try
                {
                    CheckAccountWindow<UsersWindow>(modalMain, "المستخدمون والصلاحيات", "AccountList", Key.F10);
                    // كلمة المرور متاحة أيضًا للحساب المسجل من دون صلاحية إدارة المستخدمين.
                    Maintenance_app.App.appuser = new T_SystemUser { User_ID = 2, foruser = false };
                    CheckAccountWindow<ChangePasswordWindow>(modalMain, "تغيير كلمة المرور", "CurrentPassword", Key.F11);
                    WithMessageDismissal(modalMain, () => Maintenance_app.MainWindow.OpenUsersCommand.Execute(null, modalMain));
                    if (Application.Current.Windows.OfType<UsersWindow>().Any(window => window.IsVisible))
                        throw new InvalidOperationException("User administration opened without permission.");
                }
                catch (Exception ex) { shortcutError = ex; }
                finally { CloseMain(modalMain); }
            };
            modalMain.ShowDialog();
            if (shortcutError is not null) throw shortcutError;
            app.Shutdown();
            Console.WriteLine("PASS: reports, A4/XPS statistics, compact support entry, account creation and password change.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion
}
