using DevExpress.Xpf.Editors;
using MahApps.Metro.Controls;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using Maintenance_app.reports;
using Maintenance_app.Reports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MenuItem = System.Windows.Controls.MenuItem;
using Size = System.Windows.Size;
using RadioButton = System.Windows.Controls.RadioButton;
using System.Windows.Automation.Peers;

internal static class DailyInspectionProbe
{
    #region فحص نموذج الفحص اليومي بقاعدة مستقلة
    private static int checks;
    // نحتفظ بالمراجع القديمة لمحاكاة استعلام أدوات الوصول بعد إغلاق قائمة أو نافذة.
    private static readonly HashSet<AutomationPeer> automationPeers = [];
    private static void CapturePeers(AutomationPeer peer)
    {
        // نعيد اجتياز الأب عند فتح القائمة حتى تُلتقط العناصر التي أُنشئت بعد اللقطة السابقة.
        var visited = new HashSet<AutomationPeer>();
        void Visit(AutomationPeer current)
        {
            if (!visited.Add(current)) return;
            automationPeers.Add(current);
            foreach (var child in current.GetChildren() ?? []) Visit(child);
        }
        Visit(peer);
    }
    private static void QueryPeers(string phase)
    {
        foreach (var peer in automationPeers)
        {
            try { _ = peer.GetAutomationId(); _ = peer.GetName(); }
            catch (System.Windows.Automation.ElementNotAvailableException) { }
            catch (Exception ex)
            {
                var item = peer as ItemAutomationPeer;
                for (DependencyObject? owner = item?.ItemsControlAutomationPeer?.Owner; owner is not null;
                     owner = System.Windows.Media.VisualTreeHelper.GetParent(owner))
                    Console.WriteLine($"Automation ancestor: {owner.GetType().FullName} / {(owner as FrameworkElement)?.Name}");
                throw new Exception($"Automation failure {phase}: peer={peer.GetType().FullName}, owner={item?.ItemsControlAutomationPeer?.Owner?.GetType().FullName}, name={(item?.ItemsControlAutomationPeer?.Owner as FrameworkElement)?.Name}", ex);
            }
        }
        Check(true, "Automation query completed.");
    }
    private static void CaptureVisualPeers(DependencyObject root)
    {
        // يشمل الأقران المسجلة على العناصر الفعلية، حتى العناصر المخفية في قالب معاينة الطباعة.
        foreach (var element in Descendants(root).OfType<UIElement>())
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(element);
            if (peer is not null) CapturePeers(peer);
        }
    }
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException(message);
    }
    private static T Field<T>(Window window, string name) where T : class => (T)window.FindName(name);
    private static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
        Maintenance_DabaseContext Create() => new(options);
        using (var db = Create())
        {
            db.Database.EnsureCreated();
            db.T_Branches.Add(new() { Branch_ID = 1, Branch = "فرع الفحص" });
            db.T_Locations.Add(new() { Branch_ID = 1, Location_ID = 1, Location = "موقع الفحص" });
            db.T_Builds.Add(new() { Branch_ID = 1, Location_ID = 1, Location = "موقع الفحص", Build_ID = 1, Build = "مبنى الفحص", address = "" });
            db.T_Services.AddRange(
                new T_Service { Branch_ID = 1, Location_ID = 1, Location = "موقع الفحص", Serv_num = 1, Serv_type = "كهرباء" },
                new T_Service { Branch_ID = 1, Location_ID = 1, Location = "موقع الفحص", Serv_num = 2, Serv_type = "تكييف" });
            db.T_Emergency_MaintenanceCosts.Add(new() { Branch_ID = 1, Location_ID = 1, Location = "موقع الفحص",
                Build_ID = 1, Serv_num = 1, Emergency_MaintenanceCosts_No = 1, Date_com = DateOnly.FromDateTime(DateTime.Today) });
            db.T_FinishedMaintenances.Add(new() { ID = 1, Branch_ID = 1, Location_ID = 1, Location = "موقع الفحص",
                Build_ID = 1, MaintenanceCosts_No = 1, State = 0, observer = "موظف الاختبار", notes = "",
                DateM = DateOnly.FromDateTime(DateTime.Today), Date_Modify = DateOnly.FromDateTime(DateTime.Today) });
            for (byte role = 0; role <= 2; role++)
                db.T_Employes.Add(new() { Branch_ID = 1, Location_ID = 1, Location = "موقع الفحص", Employe_ID = role + 1, Emp_Name = "موظف " + role, Adje = role });
            db.SaveChanges();
        }
        var window = new Daly_Print_Windowxaml(Create);
        await window.LoadChoicesAsync();
        window.Show();
        await Task.Delay(300);
        CapturePeers(UIElementAutomationPeer.CreatePeerForElement(window));
        CaptureVisualPeers(window);
        QueryPeers("daily window opened");
        var building = Field<ComboBoxEdit>(window, "Combo_Buildings");
        var date = Field<DateEdit>(window, "date_br_begin");
        var inspector = Field<ComboBoxEdit>(window, "Combo_Check_Man");
        Check(date.EditValue is DateTime initialDate && initialDate.Date == DateTime.Today,
            "The visible default date changed.");
        Check(!Validation.GetHasError(building) && !Validation.GetHasError(inspector), "Required errors appeared before printing.");
        Check(((IEnumerable<T_Employe>)inspector.ItemsSource).Select(item => item.Adje).Order().SequenceEqual(new byte?[] { 0, 2 }), "Inspector roles do not match the legacy form.");
        Check(await window.CreateReportAsync() is null && Validation.GetHasError(building), "Missing building accepted.");
        building.SelectedIndex = 0;
        Check(!Validation.GetHasError(building), "Selecting building retained error.");
        date.EditValue = null;
        Check(await window.CreateReportAsync() is null && Validation.GetHasError(date), "Missing date accepted.");
        date.EditValue = new DateTime(2026, 11, 11);
        Check(!Validation.GetHasError(date), "Changing date retained error.");
        Check(await window.CreateReportAsync() is null && Validation.GetHasError(inspector), "Missing inspector accepted.");
        inspector.SelectedIndex = 0;
        Check(!Validation.GetHasError(inspector), "Selecting inspector retained error.");
        Check(building.Text == "مبنى الفحص" && inspector.Text == ((T_Employe)inspector.SelectedItem).Emp_Name,
            "The editor template lost the displayed selection.");
        foreach (string name in new[] { "Combo_Buildings", "Combo_Check_Man", "date_br_begin" })
        {
            var field = Field<PopupBaseEdit>(window, name);
            // نشغل زر القالب الحقيقي كي نتحقق من بقاء ربط أزرار DevExpress بعد تغيير الحاوية.
            var button = Descendants(field).OfType<System.Windows.Controls.Primitives.ButtonBase>()
                .First(item => item.Name == "PART_Item" && item.IsVisible && item.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(150);
            Check(field.IsPopupOpen, name + " did not open from its actual button.");
            var peer = UIElementAutomationPeer.CreatePeerForElement(field);
            Check(peer.GetPattern(PatternInterface.ExpandCollapse) is System.Windows.Automation.Provider.IExpandCollapseProvider,
                name + " lost its expand-collapse pattern.");
            CapturePeers(peer);
            QueryPeers(name + " opened");
            field.IsPopupOpen = false;
            await Task.Delay(150);
            QueryPeers(name + " closed");
        }
        Maintenance_app.App.Branch_Name = "فرع آخر";
        using var report = await window.CreateReportAsync() ?? throw new Exception("Valid form was rejected.");
        var data = ((DataView)report.DataSources.Single().Value).Table!;
        Check(data.Rows.Count == 2 && data.Rows.Cast<DataRow>().All(row =>
            (string)row["elm14"] == "فرع الفحص" && (string)row["elm7"] == "موقع الفحص" &&
            (string)row["elm8"] == "مبنى الفحص" && (string)row["elm20"] == "11/11/2026"), "Legacy fields or branch scope changed.");
        var pdf = report.Render("PDF");
        Check(pdf.Length > 1000 && System.Text.Encoding.ASCII.GetString(pdf, 0, 4) == "%PDF", "RDLC did not produce a PDF.");
        File.WriteAllBytes(Path.Combine(output, "daily-inspection.pdf"), pdf);
        Console.WriteLine("Daily template rendered to PDF.");
        // نفتح المعاينة نفسها عبر زر الواجهة، ثم نتحقق من المستند الذي يستطيع المستخدم طباعته.
        _ = window.Dispatcher.BeginInvoke(new Action(() =>
            Field<DevExpress.Xpf.Core.SimpleButton>(window, "PrintButton").RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent))));
        await Task.Delay(1200);
        var preview = Application.Current.Windows.OfType<show_report>().Single();
        var viewer = (DocumentViewer)preview.FindName("PreviewViewer");
        Check(preview.IsVisible && preview.IsActive && preview.Owner == window && viewer.Document is not null &&
            window.FindName("MaintenancePrintFlyout") is null, "Print button did not open the separate show_report window.");
        preview.Close();
        await Task.Delay(200);
        Check(viewer.Document is null, "Closing preview retained its document.");
        foreach (var size in new[] { new Size(1180, 800), new Size(760, 600) })
        {
            window.WindowState = WindowState.Normal;
            window.Width = size.Width;
            window.Height = size.Height;
            await Task.Delay(150);
            window.UpdateLayout();
            var bounds = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
            foreach (string name in new[] { "Combo_Buildings", "date_br_begin", "Combo_Check_Man", "PrintButton" })
            {
                var field = Field<FrameworkElement>(window, name);
                Check(bounds.Contains(field.TransformToAncestor(window).TransformBounds(new Rect(field.RenderSize))), "Control clipped: " + name);
            }
            if (size.Width == 1180)
            {
                // لقطة فعلية لتدقيق القالب الجديد، مع تصحيح انعكاس تصيير العنصر العربي المنفصل.
                var root = (FrameworkElement)window.Content;
                var visual = new System.Windows.Media.DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.PushTransform(new System.Windows.Media.ScaleTransform(-1, 1, root.ActualWidth / 2, 0));
                    drawing.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(root.RenderSize));
                    drawing.Pop();
                }
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth,
                    (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, "daily-editor-template.png"));
                png.Save(file);
            }
        }
        window.Close();
        QueryPeers("daily window closed");
        foreach (string name in new[] { "Combo_Buildings", "Combo_Check_Man", "date_br_begin" })
        {
            // تغيير القالب يفصل حاويات الأزرار، بينما قد تحتفظ أداة الوصول بالمراجع السابقة.
            var field = Field<PopupBaseEdit>(window, name);
            field.Template = null;
            field.ApplyTemplate();
        }
        QueryPeers("editor templates detached");
        // نفتح تقرير الإحصائية الحقيقي من زره ونتحقق من وصول صفحات قالب REPX إلى العارض نفسه.
        Maintenance_app.App.login_user = new T_SystemUser { User_ID = 1, User_Name = "مستخدم الاختبار" };
        var statistics = new MaintenanceStatisticsWindow(Create);
        statistics.Show();
        await Task.Delay(200);
        _ = statistics.Dispatcher.BeginInvoke(new Action(() =>
            Field<DevExpress.Xpf.Core.SimpleButton>(statistics, "PrintStatisticsButton").RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent))));
        for (int attempt = 0; attempt < 60 && !Application.Current.Windows.OfType<show_report>().Any(); attempt++)
            await Task.Delay(100);
        var statisticsPreview = Application.Current.Windows.OfType<show_report>().Single();
        var statisticsViewer = Field<DevExpress.Xpf.Printing.DocumentPreviewControl>(statisticsPreview, "StatisticsViewer");
        Check(statisticsPreview.IsVisible && statisticsPreview.Owner == statistics && statisticsViewer.IsVisible &&
            statisticsViewer.DocumentSource is DevExpress.XtraReports.UI.XtraReport { Pages.Count: > 0 },
            "Statistics did not load its report into show_report.");
        statisticsPreview.Close();
        await Task.Delay(150);
        Check(statisticsViewer.DocumentSource is null, "Closing statistics preview retained its report.");
        statistics.Close();

        // نموذج الخدمات يستخدم قالب RDLC مستقلًا ويمر عبر زر الطباعة نفسه في نافذته.
        var servicesForm = new daliy_services_window(Create);
        await servicesForm.LoadChoicesAsync();
        servicesForm.Show();
        Field<ComboBoxEdit>(servicesForm, "Combo_Buildings").SelectedIndex = 0;
        Field<ComboBoxEdit>(servicesForm, "Combo_Check_Man").SelectedIndex = 0;
        await CheckRdlcPreviewAsync(servicesForm, "نموذج الفحص اليومي للخدمات", () =>
            Field<DevExpress.Xpf.Core.SimpleButton>(servicesForm, "PrintButton").RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)));
        servicesForm.Close();

        // نتحقق من مسارات الكشف والتكليف والإنهاء بعد نقلها إلى نفس نافذة الطباعة.
        var reports = new MaintenanceReportsWindow(Create);
        reports.Show();
        await Task.Delay(200);
        var search = typeof(MaintenanceReportsWindow).GetMethod("Search_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
        search.Invoke(reports, [reports, new RoutedEventArgs()]);
        var reportGrid = Field<DevExpress.Xpf.Grid.GridControl>(reports, "ReportsGrid");
        for (int attempt = 0; attempt < 40 && reportGrid.VisibleRowCount == 0; attempt++) await Task.Delay(100);
        Check(reportGrid.VisibleRowCount == 1, "The report search did not load the seeded order.");
        Field<ComboBoxEdit>(reports, "PrintMode").SelectedIndex = 0;
        var print = typeof(MaintenanceReportsWindow).GetMethod("Print_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await CheckRdlcPreviewAsync(reports, "تقارير الصيانة", () => print.Invoke(reports, [reports, new RoutedEventArgs()]));
        await using (var db = Create())
        {
            var order = await db.T_Emergency_MaintenanceCosts.Include(item => item.T_Build).Include(item => item.T_Service).SingleAsync();
            using var orderReport = MaintenanceOrderReport.Create(order, "مراقب الاختبار");
            await CheckRdlcPreviewAsync(reports, "طباعة تكليف الصيانة", () => MaintenanceOrderReport.ShowPreview(reports, orderReport));
            using var completionReport = MaintenanceCompletionReport.Create(order, DateTime.Today, "مراقب الاختبار", "فحص");
            await CheckRdlcPreviewAsync(reports, "طباعة تقرير إنهاء الصيانة", () => MaintenanceCompletionReport.ShowPreview(reports, completionReport));
        }
        reports.Close();
        // الاختصار والقائمة يفتحان النافذة من نفس المدخل ويعيدان تنشيطها عند التكرار.
        var main = new Maintenance_app.MainWindow(Create, () => { });
        var loaded = typeof(Maintenance_app.MainWindow).GetMethod("MetroWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!;
        main.Loaded -= loaded.CreateDelegate<RoutedEventHandler>(main);
        // يظل تحميل الرئيسية الإنتاجي مفصولًا، فنفعّل خانة السنة في بيانات الاختبار صراحة.
        main.Statistics.ConfigureAccess(true);
        Exception? navigationError = null;
        main.ContentRendered += async (_, _) =>
        {
            try
            {
                // نضغط بطاقة التقارير المرئية من رئيسية حوارية كما تُفتح بعد تسجيل الدخول.
                CaptureVisualPeers(main);
                QueryPeers("main before reports");
                Field<RadioButton>(main, "ReportsTab").IsChecked = true;
                main.UpdateLayout();
                CaptureVisualPeers(main);
                QueryPeers("main reports tab");
                var commands = Field<ItemsControl>(main, "SectionCommands");
                var button = Descendants(commands).OfType<DevExpress.Xpf.Core.SimpleButton>().Single(item =>
                    System.Windows.Automation.AutomationProperties.GetName(item) == "نموذج الفحص اليومي");
                var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(button);
                var invoke = (System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(
                    System.Windows.Automation.Peers.PatternInterface.Invoke);
                invoke.Invoke();
                await Task.Delay(500);
                var daily = Application.Current.Windows.OfType<Daly_Print_Windowxaml>().Single();
                CaptureVisualPeers(daily);
                QueryPeers("daily opened from main");
                // يحجب ShowDialog مقبض النافذة المالكة بينما يحتفظ WPF بقيمة IsEnabled على عناصرها.
                var mainHandle = new System.Windows.Interop.WindowInteropHelper(main).Handle;
                Check(daily.Owner == main && daily.IsVisible && daily.IsActive && !IsWindowEnabled(mainHandle),
                    "The report button did not open the daily dialog in front of the main window.");
                var year = Field<SpinEdit>(main, "StatisticsYear");
                var yearTemplate = year.Template;
                year.Template = null;
                year.ApplyTemplate();
                QueryPeers("main editor template detached while daily is open");
                year.Template = yearTemplate;
                year.ApplyTemplate();
                // يولّد WPF حاويات الأزرار عند قياس القالب المستعاد.
                main.UpdateLayout();
                daily.Close();
                await Task.Delay(100);
                Check(main.IsActive && IsWindowEnabled(mainHandle), "Closing the daily dialog did not restore the main window.");
                Check(year.EditValue is not null, "Main statistics year was lost after restoring its template.");
                var oldYear = Convert.ToInt32(year.EditValue);
                var up = Descendants(year).OfType<System.Windows.Controls.Primitives.ButtonBase>()
                    .Single(item => item.Name == "PART_SpinUpButton");
                // زر السنة ينفذ أمر WPF، ورفع حدث Click وحده لا ينفذ الأمر المرتبط به.
                var upPeer = UIElementAutomationPeer.CreatePeerForElement(up);
                var spinUp = (System.Windows.Automation.Provider.IInvokeProvider)upPeer.GetPattern(PatternInterface.Invoke);
                spinUp.Invoke();
                await Task.Delay(100);
                Check(Convert.ToInt32(year.EditValue) == oldYear + 1, "The statistics spin button stopped updating the year.");
                year.EditValue = oldYear;
                var shortcut = main.InputBindings.OfType<KeyBinding>().Single(binding => binding.Key == Key.Y && binding.Modifiers == ModifierKeys.Alt);
                // نضع الاختصار في طابور الواجهة كي يستمر الاختبار داخل حلقة الحوار.
                _ = main.Dispatcher.BeginInvoke(new Action(() => ((RoutedCommand)shortcut.Command).Execute(null, main)));
                await Task.Delay(500);
                var reopened = Application.Current.Windows.OfType<Daly_Print_Windowxaml>().Single();
                Check(!ReferenceEquals(reopened, daily) && reopened.IsActive && !IsWindowEnabled(mainHandle),
                    "Shortcut did not reopen the daily dialog in front of the main window.");
                reopened.Close();
            }
            catch (Exception ex) { navigationError = ex; }
            finally
            {
                // نغلق نافذة الاختبار دون حوار تأكيد إنهاء جلسة إنتاجية.
                typeof(Maintenance_app.MainWindow).GetField("confirmedClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, true);
                main.Close();
            }
        };
        main.ShowDialog();
        if (navigationError is not null) throw navigationError;
        using var unchanged = Create();
        Check(unchanged.T_Services.Count() == 2 && unchanged.T_Emergency_MaintenanceCosts.Count() == 1, "Printing wrote business data.");
    }

    private static async Task CheckRdlcPreviewAsync(Window owner, string title, Action open)
    {
        _ = owner.Dispatcher.BeginInvoke(open);
        for (int attempt = 0; attempt < 60 && !Application.Current.Windows.OfType<show_report>().Any(); attempt++)
            await Task.Delay(100);
        var preview = Application.Current.Windows.OfType<show_report>().Single();
        var viewer = Field<DocumentViewer>(preview, "PreviewViewer");
        try
        {
            Check(preview.Owner == owner && preview.Title == title && viewer.Document is not null, "Missing RDLC preview for " + title);
        }
        finally { preview.Close(); }
        await Task.Delay(100);
        Check(viewer.Document is null, "Closing the preview retained report pages.");
    }

    // البحث في الشجرة يختار زر المستخدم الفعلي بدل استدعاء الحدث الداخلي مباشرة.
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    #endregion

    #region تشغيل الاختبار على موزع WPF
    // يُفحص حجب إدخال الرئيسية على مستوى نافذة Windows كما يفعله ShowDialog.
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr handle);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Maintenance_app.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            var startup = typeof(Maintenance_app.App).GetMethod("Application_Startup", BindingFlags.Instance | BindingFlags.NonPublic)!;
            app.Startup -= startup.CreateDelegate<StartupEventHandler>(app);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var task = RunAsync(args[0]);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
            task.GetAwaiter().GetResult();
            app.Shutdown();
            Console.WriteLine($"PASS {checks} daily inspection checks, real preview and PDF; SQLite only.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion
}
