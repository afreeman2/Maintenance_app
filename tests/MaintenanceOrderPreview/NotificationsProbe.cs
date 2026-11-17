using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using Maintenance_app.Models;
using Maintenance_app.Notifications;
using Maintenance_app.orders_Windows;
using Maintenance_app.dbContext;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Data;
using Application = System.Windows.Application;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using ListBox = System.Windows.Controls.ListBox;
using Size = System.Windows.Size;

internal static class NotificationsProbe
{
    #region أدوات الفحص والبيانات الاصطناعية

    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static T Find<T>(FrameworkElement root, string name) where T : class => (T)root.FindName(name);
    private static void Click(FrameworkElement root, string name)
    {
        Find<FrameworkElement>(root, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Pump();
    }
    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static MaintenanceNotice Notice(int number, bool needsReport = true, byte state = 2)
    {
        return new MaintenanceNotice
        {
            Number = number, Location = "موقع المعاينة", LocationId = 1, BranchId = 1,
            Date = DateOnly.FromDateTime(DateTime.Today).AddDays(-number), NeedsReport = needsReport, ReportState = needsReport ? null : state,
            Building = "المبنى الإداري", Service = number % 2 == 0 ? "الكهرباء" : "التكييف", Employees = "فني المعاينة الأول، فني المعاينة الثاني",
            Branch = "فرع المعاينة", Notes = needsReport ? string.Empty : "بانتظار استكمال الأعمال وتسجيل النتيجة النهائية."
        };
    }
    private static void Capture(FrameworkElement element, string path)
    {
        Pump();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);
    }

    // تصدير محتوى مستقل عن انعكاس HWND عند تصوير نافذة RTL، مع استمرار فحوص النافذة الحقيقية أعلاه.
    private static void CapturePanel(NotificationCenter panel, MaintenanceNotificationState state, string path)
    {
        var copy = new NotificationCenter { DataContext = state, Width = panel.ActualWidth, Height = panel.ActualHeight };
        Size size = new Size(copy.Width, copy.Height);
        copy.Measure(size); copy.Arrange(new Rect(size)); copy.UpdateLayout();
        Capture(copy, path);
    }

    #endregion

    #region معاينة التكليف بالمفتاح المركب للقراءة فقط

    private static void CheckOrderPreview(Window owner)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
        Maintenance_DabaseContext Create() => new Maintenance_DabaseContext(options);
        var today = DateOnly.FromDateTime(DateTime.Today);
        using (var db = Create())
        {
            db.Database.EnsureCreated();
            db.Add(new T_Branch { Branch_ID = 1, Branch = "اختبار" });
            foreach (short locationId in new short[] { 1, 2 })
            {
                string location = "معاينة " + locationId;
                db.Add(new T_Location { Location = location, Location_ID = locationId, Branch_ID = 1 });
                db.Add(new T_Build { Location = location, Location_ID = locationId, Branch_ID = 1, Build_ID = 1, Build = "مبنى " + locationId });
                db.Add(new T_Service { Location = location, Location_ID = locationId, Branch_ID = 1, Serv_num = 1, Serv_type = "خدمة" });
                var record = new T_Emergency_MaintenanceCost
                {
                    Location = location, Location_ID = locationId, Branch_ID = 1, Emergency_MaintenanceCosts_No = 77,
                    Build_ID = 1, Serv_num = 1, Date_com = today.AddDays(-locationId), Date_Modify = today,
                    Emergency_MaintenanceCosts_ST = true, User_ID = 1
                };
                record.T_FinishedMaintenances.Add(new T_FinishedMaintenance
                {
                    ID = locationId, Location = location, Location_ID = locationId, Branch_ID = 1,
                    MaintenanceCosts_No = 77, Build_ID = 1, State = 2, DateM = today, Date_Modify = today,
                    observer = "اختبار", notes = "معلق"
                });
                db.Add(record);
            }
            db.SaveChanges();
        }
        var order = new maintenance_Order
        {
            Owner = owner, IsNotificationPreview = true, WindowState = WindowState.Normal, Width = 1366, Height = 768,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowInTaskbar = false
        };
        // استبدال مخزن النسخة الاصطناعية فقط بقاعدة SQLite المعزولة لاختبار طريق التحميل الفعلي.
        typeof(maintenance_Order).GetField("Store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(order, new MaintenanceOrderEfStore(Create));
        order.Show(); Pump();
        Check(!Find<FrameworkElement>(order, "OrderOverviewCard").IsEnabled && !Find<SimpleButton>(order, "Insert").IsEnabled,
            "Notification preview allowed writing before loading finished.");
        var notice = new OrderSearchRow(77, DateTime.Today, "", "", "", "معاينة 2", 2, 1);
        Wait(order.LoadOrderAsync(notice, readOnly: true));
        Check(Convert.ToDateTime(Find<DateEdit>(order, "date_check").EditValue).Date == DateTime.Today.AddDays(-2), "Notification opened the other location's record.");
        Check(!Find<FrameworkElement>(order, "OrderActionsCard").IsEnabled && !order.AddRepair() && !order.AddMaterial(), "Read-only preview allowed a draft mutation.");
        Check(order.Title.Contains("للقراءة فقط"), "Preview mode was not identified.");
        order.Close(); Pump();
        using var verify = Create();
        Check(verify.T_Emergency_MaintenanceCosts.Count() == 2 && verify.T_FinishedMaintenances.Count() == 2, "Preview mutated source records.");
    }

    #endregion

    #region اختبار الحالة والمركز والجرس وخدمة DevExpress الحقيقية

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(args[0]);
            var app = new Maintenance_app.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            // إلغاء حدث دخول التطبيق الأصلي في برنامج الفحص فقط؛ لا وصول إلى تسجيل المستخدم أو قاعدة العمل.
            MethodInfo startup = typeof(Maintenance_app.App).GetMethod("Application_Startup", BindingFlags.Instance | BindingFlags.NonPublic)!;
            app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            Maintenance_app.App.ConnectionString = string.Empty;
            Maintenance_app.App.appuser = new T_SystemUser { User_ID = 2147480, formaint = true };
            string storage = Path.Combine(args[0], "read-state-" + Guid.NewGuid().ToString("N"));
            var main = new Maintenance_app.MainWindow
            {
                WindowState = WindowState.Normal, Width = 1366, Height = 900,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000,
                ShowActivated = true, ShowInTaskbar = false
            };
            main.Notifications.Configure(2147480, true, storage);
            main.Show(); Pump();
            // هذه الفحوص تستخدم قائمة مصطنعة؛ التحديث الحقيقي كل ثانية يفحصه NotificationRoutingProbe.
            var refreshTimer = (DispatcherTimer)typeof(Maintenance_app.MainWindow).GetField("notificationTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
            Check(refreshTimer.Interval == TimeSpan.FromSeconds(1), "Live notification interval is not one second.");
            refreshTimer.Stop();
            var state = main.Notifications;
            Check(state.HasError, "Unconfigured connection did not show a nonblocking failure state.");
            var badge = Find<Badge>(main, "NoticeBadge");
            Check(badge.Opacity == 1, "An empty badge retained a dimmed state.");
            var rows = new[] { Notice(12), Notice(9, false), Notice(4, false, 3), Notice(2) };
            Check(state.Apply(rows).Count == 4 && state.Total == 4 && state.Unread == 4, "Initial snapshot or unread count failed.");
            // نفحص مرور الزمن الفعلي والشفافية مع بقاء الشارة نفسها والعدد دون تغيير.
            var bell = Find<SimpleButton>(main, "NotificationBell");
            for (int sample = 0; sample < 40 && badge.Opacity == 1; sample++)
            {
                Wait(Task.Delay(40)); Pump();
            }
            Check(badge.Opacity == 0.35 && ReferenceEquals(badge, Badge.GetBadge(bell)) && Equals(badge.Content, "4"),
                "Unread badge did not dim or was replaced or changed its count.");
            for (int sample = 0; sample < 40 && badge.Opacity < 1; sample++)
            {
                Wait(Task.Delay(40)); Pump();
            }
            Check(badge.Opacity == 1, "Unread badge did not return to full visibility.");
            Check(state.Apply(rows.Select(row => Notice((int)row.Number, row.NeedsReport, row.ReportState ?? 2))).Count == 0, "Unchanged polling repeated notifications.");
            Check(state.NeedsReportCount == 2 && state.PendingReportCount == 2, "Counts do not match pending phases.");
            Check(state.SuspendedReportCount == 1 && state.InProgressReportCount == 1, "Report-state counters were combined.");
            var groups = state.View.Groups!.Cast<CollectionViewGroup>().ToList();
            Check(groups.Count == 3 && groups[1].Name.ToString() == MaintenanceNotice.SuspendedGroup
                && groups[2].Name.ToString() == MaintenanceNotice.InProgressGroup, "Distinct notification groups missing or out of order.");
            var suspended = state.Items.Single(row => row.ReportState == 2);
            var progressing = state.Items.Single(row => row.ReportState == 3);
            Check(suspended.Title == MaintenanceNotice.SuspendedGroup && progressing.Title == MaintenanceNotice.InProgressGroup,
                "Report notification title did not replace assignment title.");
            Check(suspended.AccentColor != progressing.AccentColor && suspended.TintColor != progressing.TintColor,
                "Suspended and in-progress notifications share colors.");
            state.MarkRead(state.Items[0]);
            Check(state.Total == 4 && state.Unread == 3, "Reading incorrectly removed a pending task.");
            var reloaded = new MaintenanceNotificationState();
            reloaded.Configure(2147480, true, storage);
            reloaded.Apply(rows);
            Check(reloaded.Unread == 3, "Read status did not persist for the user.");
            reloaded.Configure(2147481, true, storage);
            reloaded.Apply(rows);
            Check(reloaded.Unread == 4, "Read status leaked between users.");
            state.Search = "الكهرباء";
            Check(state.View.Cast<object>().Count() == 3, "Service search failed.");
            state.Search = string.Empty; state.Filter = 3;
            Check(state.View.Cast<object>().Count() == 2, "Pending-report filter failed.");
            state.Filter = 4;
            Check(state.View.Cast<object>().Count() == 2, "Age filter failed.");
            state.Filter = 5;
            Check(state.View.Cast<MaintenanceNotice>().Single().ReportState == 2, "Suspended-only filter failed.");
            state.Filter = 6;
            Check(state.View.Cast<MaintenanceNotice>().Single().ReportState == 3, "In-progress-only filter failed.");
            state.Filter = 0;
            state.Fail();
            Check(state.HasError && state.Total == 4 && state.Status.Contains("قديمة"), "Connection failure lost the last successful snapshot.");
            state.Apply(rows);
            Check(!state.HasError, "Successful retry did not clear failure state.");
            var panel = Find<NotificationCenter>(main, "NoticePanel");
            var flyout = Find<DevExpress.Xpf.Editors.Flyout.FlyoutControl>(main, "NotificationFlyout");
            flyout.IsOpen = true; Pump(); Wait(Task.Delay(350)); Pump();
            Check(flyout.IsOpen && panel.ActualWidth >= 600, "Real DevExpress flyout failed to open.");
            Check(Find<Badge>(main, "NoticeBadge").Content?.ToString() == "3", "Bell badge did not update.");
            var list = Find<ListBox>(panel, "NoticeList");
            list.SelectedItem = state.Items.First(row => !row.NeedsReport);
            Pump();
            Check(state.Unread == 3, "Selecting a row removed it from unread results before the click completed.");
            state.MarkRead(state.Selected!);
            Check(state.Unread == 2, "Opening/reading a notification did not update its read state.");
            CapturePanel(panel, state, Path.Combine(args[0], "notification-center.png"));
            // تظهر مجموعتا التقرير منفصلتين في معاينة الفلتر المشترك أيضًا.
            state.Filter = 3; Pump();
            Check(state.View.Groups!.Count == 2, "Combined report filter did not retain the two state groups.");
            CapturePanel(panel, state, Path.Combine(args[0], "notification-report-groups.png"));
            state.Filter = 0;
            foreach (Size size in new[] { new Size(600, 530), new Size(760, 790), new Size(780, 900) })
            {
                panel.Width = size.Width; panel.Height = size.Height;
                panel.Measure(size); panel.Arrange(new Rect(size)); panel.UpdateLayout(); Pump();
                foreach (string name in new[] { "NoticeSearch", "NoticeFilter", "RefreshNotices", "NoticeList", "OpenNoticeOrder", "CloseCenter" })
                {
                    var field = Find<FrameworkElement>(panel, name);
                    Rect bounds = field.TransformToAncestor(panel).TransformBounds(new Rect(field.RenderSize));
                    Check(field.ActualWidth > 20 && field.ActualHeight > 20 && bounds.Left >= -1 && bounds.Right <= size.Width + 1 && bounds.Bottom <= size.Height + 1,
                        name + " clipped at " + size);
                }
            }
            Click(panel, "ReadAllNotices");
            Check(state.Total == 4 && state.Unread == 0 && Find<Badge>(main, "NoticeBadge").Visibility == Visibility.Collapsed,
                "Mark-all-read removed work or left the badge visible.");
            Wait(Task.Delay(1100)); Pump();
            Check(badge.Opacity == 1 && badge.Visibility == Visibility.Collapsed,
                "Badge kept blinking after all notifications were read.");
            state.Apply(new[] { Notice(9, false), Notice(2) });
            Check(state.Total == 2, "Completed/deleted work was retained in the list.");
            state.Configure(2147480, false, storage);
            Check(state.Total == 0 && state.Selected == null, "Revoked access retained confidential notice data.");
            Check(badge.Opacity == 1 && badge.Visibility == Visibility.Collapsed, "Revoking access left a blinking badge.");
            state.Configure(2147480, true, storage);
            var newlyAdded = state.Apply(new[] { Notice(21) });
            Check(newlyAdded.Count == 1, "New work did not produce a notification.");
            Wait(Task.Delay(1100)); Pump();
            Check(Equals(Badge.GetBadge(bell).Content, "1") && Badge.GetBadge(bell).Visibility == Visibility.Visible,
                "A new unread notification did not restore the badge after reading all.");
            flyout.IsOpen = false;
            // تشغيل خدمة DevExpress الفعلية ببيانات اصطناعية وفحص نافذة التنبيه ثم إغلاقها برمجيًا.
            Task toastTask = (Task)typeof(Maintenance_app.MainWindow).GetMethod("ShowMaintenanceToastAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, new object[] { newlyAdded })!;
            Pump(); Wait(Task.Delay(600)); Pump();
            Window? toastWindow = app.Windows.Cast<Window>().FirstOrDefault(window => window != main && window.IsVisible);
            Check(toastWindow != null, "DevExpress custom notification window did not appear.");
            Console.WriteLine("TOAST=" + toastWindow!.ActualWidth + "x" + toastWindow.ActualHeight);
            Check(Descendants(toastWindow!).OfType<TextBlock>().Any(text => text.Text.Contains("تكليف يحتاج")), "Custom notification has no Arabic title.");
            foreach (TextBlock text in Descendants(toastWindow!).OfType<TextBlock>().Where(text => text.Text.Contains("تكليف يحتاج")))
            {
                Rect bounds = text.TransformToAncestor(toastWindow!).TransformBounds(new Rect(text.RenderSize));
                Check(bounds.Left >= -1 && bounds.Right <= toastWindow!.ActualWidth + 1, "The actual toast title is clipped.");
            }
            var toastTemplate = (DataTemplate)main.FindResource("Maintenance.Toast");
            var toastContent = (FrameworkElement)toastTemplate.LoadContent();
            toastContent.DataContext = new Maintenance_app.MainWindow.MaintenanceToast { Title = "تكليف يحتاج تقرير إنهاء", Text = newlyAdded[0].Summary };
            toastContent.Measure(new Size(370, 170)); toastContent.Arrange(new Rect(0, 0, 370, 170)); toastContent.UpdateLayout();
            Capture(toastContent, Path.Combine(args[0], "notification-toast.png"));
            var active = (HashSet<INotification>)typeof(Maintenance_app.MainWindow).GetField("activeToasts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
            foreach (INotification toast in active.ToArray()) toast.Hide();
            Wait(toastTask);
            // تغيير حالة التكليف يستبدل مجموعته ويولد هوية متابعة واحدة للحالة الجديدة.
            state.Apply(new[] { Notice(21, false, 2) });
            Check(state.Total == 1 && state.NeedsReportCount == 0 && state.SuspendedReportCount == 1
                && state.View.Groups!.Cast<CollectionViewGroup>().Single().Name.ToString() == MaintenanceNotice.SuspendedGroup,
                "Assignment did not move to the suspended report group.");
            var changed = state.Apply(new[] { Notice(21, false, 3) });
            Check(changed.Count == 1 && state.Total == 1 && state.SuspendedReportCount == 0 && state.InProgressReportCount == 1,
                "Report status transition duplicated the assignment or retained old counts.");
            foreach (var row in new[] { Notice(22, false, 2), Notice(23, false, 3) })
            {
                Task coloredToast = (Task)typeof(Maintenance_app.MainWindow).GetMethod("ShowMaintenanceToastAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, new object[] { new List<MaintenanceNotice> { row } })!;
                Pump(); Wait(Task.Delay(400)); Pump();
                var coloredWindow = app.Windows.Cast<Window>().First(window => window != main && window.IsVisible);
                Check(Descendants(coloredWindow).OfType<TextBlock>().Any(text => text.Text == row.Title
                    && text.Foreground is SolidColorBrush brush && brush.Color == (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(row.AccentColor)),
                    "Actual toast did not use report-state title and color.");
                foreach (INotification toast in active.ToArray()) toast.Hide(); Wait(coloredToast);
            }
            CheckOrderPreview(main);
            typeof(Maintenance_app.MainWindow).GetField("confirmedClose", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, true);
            main.Close(); app.Shutdown();
            Console.WriteLine("PASS: " + checks + " notification UI/state checks; real DevExpress flyout, badge and custom toast; user-specific read persistence, filtering, duplicates, refresh removal, stale data, permissions, three panel sizes and read-only composite-key order preview on isolated SQLite. No production database access.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    #endregion
}
