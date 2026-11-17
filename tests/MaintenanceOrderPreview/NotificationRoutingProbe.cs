using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.Notifications;
using Maintenance_app.orders_Windows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using Application = System.Windows.Application;
using ListBox = System.Windows.Controls.ListBox;

internal static class NotificationRoutingProbe
{
    #region أدوات الاختبار على نافذتين وقاعدة SQLite معزولة
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    // القوائم المجمعة تملك مولد عناصر لكل مجموعة؛ نفحص الحاوية المرئية الفعلية داخلها.
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static T Find<T>(FrameworkElement owner, string name) where T : class => (T)owner.FindName(name);
    private static void WaitUntil(Func<bool> condition, string failure, int timeout = 4000)
    {
        var frame = new DispatcherFrame(); var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) => { if (condition() || watch.ElapsedMilliseconds > timeout) { timer.Stop(); frame.Continue = false; } };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); Check(condition(), failure);
    }
    private static void Delay(int milliseconds)
    {
        var watch = Stopwatch.StartNew(); WaitUntil(() => watch.ElapsedMilliseconds >= milliseconds, "Dispatcher wait failed.", milliseconds + 1500);
    }
    private static void CloseMain(Maintenance_app.MainWindow main)
    {
        typeof(Maintenance_app.MainWindow).GetField("confirmedClose", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, true);
        main.Close();
    }
    private static void Seed(Maintenance_DabaseContext db)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        db.Database.EnsureCreated(); db.Add(new T_Branch { Branch_ID = 1, Branch = "فرع اختبار" });
        foreach (short locationId in new short[] { 1, 2 })
        {
            string location = "موقع اختبار " + locationId;
            db.AddRange(new T_Location { Location = location, Location_ID = locationId, Branch_ID = 1 },
                new T_Build { Location = location, Location_ID = locationId, Branch_ID = 1, Build_ID = 1, Build = "مبنى " + locationId },
                new T_Service { Location = location, Location_ID = locationId, Branch_ID = 1, Serv_num = 1, Serv_type = "خدمة " + locationId },
                new T_Employe { Location = location, Location_ID = locationId, Branch_ID = 1, Employe_ID = locationId, Emp_Name = "فني " + locationId, Adje = 0 });
            foreach (long number in locationId == 1 ? new long[] { 77, 78 } : new long[] { 77 })
            {
                var order = new T_Emergency_MaintenanceCost { Location = location, Location_ID = locationId, Branch_ID = 1,
                    Emergency_MaintenanceCosts_No = number, Build_ID = 1, Serv_num = 1, Employe_ID = locationId,
                    Date_com = today, Date_Modify = today, Emergency_MaintenanceCosts_ST = locationId == 1 };
                order.T_Emp_Maints.Add(new T_Emp_Maint { id = number * 10 + locationId, Location = location, Location_ID = locationId,
                    Branch_ID = 1, MaintenanceCosts_No = number, Employe_ID = locationId });
                order.T_Emergency_MaintenanceCosts_Services.Add(new T_Emergency_MaintenanceCosts_Service { ID = number * 10 + locationId,
                    Location = location, Location_ID = locationId, Branch_ID = 1, Emergency_MaintenanceCosts_No = number,
                    Statement = "عمل الاختبار " + locationId, Date_Modify = today, Maint_Type = 0 });
                if (locationId == 1) order.T_FinishedMaintenances.Add(new T_FinishedMaintenance { ID = number, Location = location,
                    Location_ID = locationId, Branch_ID = 1, Build_ID = 1, MaintenanceCosts_No = number,
                    DateM = today.AddDays(-1), Date_Modify = today, State = number == 77 ? (byte)2 : (byte)3,
                    observer = "موظف التقرير " + number, notes = "ملاحظات محفوظة " + number });
                db.Add(order);
            }
        }
        db.SaveChanges();
    }
    #endregion

    #region النقر الحقيقي على بطاقة الإشعار والتحقق من بيانات تقرير الإنهاء
    private static void OpenNotice(Application app, Maintenance_app.MainWindow main, MaintenanceNotice notice, int route)
    {
        Console.WriteLine($"OPEN route={route} key={notice.Key} state={notice.ReportState} current={main.Notifications.Items.Any(row => ReferenceEquals(row, notice))}");
        var panel = Find<NotificationCenter>(main, "NoticePanel"); var list = Find<ListBox>(panel, "NoticeList");
        main.Activate(); Pump();
        var flyout = Find<DevExpress.Xpf.Editors.Flyout.FlyoutControl>(main, "NotificationFlyout");
        // نافذتا الاختبار خارج الشاشة؛ تثبيت اللوحة يمنع إغلاقها بفقد التنشيط أثناء تبديل المالك.
        flyout.StaysOpen = true; flyout.IsOpen = true; Pump();
        WaitUntil(() => panel.IsVisible && panel.ActualWidth > 0, "Notification flyout did not become visible.");
        Console.WriteLine($"PANEL {panel.ActualWidth}x{panel.ActualHeight} visible={panel.IsVisible} rows={list.Items.Count} realized={Descendants(list).OfType<ListBoxItem>().Count()}");
        list.ScrollIntoView(notice); Pump();
        WaitUntil(() => Descendants(list).OfType<ListBoxItem>().Any(item => ReferenceEquals(item.DataContext, notice)),
            "Actual grouped notification row was not realized.");
        bool opened = false; Exception? error = null; var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) =>
        {
            var child = app.Windows.OfType<maintenance_complete>().FirstOrDefault(window => window.IsVisible);
            if (child is not null && child.State.OrderNumber == notice.Number.ToString()
                && Find<Border>(child, "BusyCover").Visibility == Visibility.Collapsed)
            {
                try
                {
                    Check(child.Owner == main && child.WindowState == WindowState.Maximized, "Notification did not open owned completion window.");
                    Check(child.InitialOrderKey == notice.Key && child.State.Building == "مبنى " + notice.LocationId, "Notification opened another scope with the same order number.");
                    Check(child.State.Repairs.Count == 1 && child.State.Technicians == "فني " + notice.LocationId, "Order details/technicians were not loaded.");
                    var state = Find<ComboBoxEdit>(child, "Doc_Type");
                    if (notice.NeedsReport)
                        Check(state.EditValue is null && string.IsNullOrEmpty(Find<TextEdit>(child, "Text_man").Text)
                            && child.State.ReportMode == "تقرير جديد", "Assignment inherited a report from another scope.");
                    else
                        Check(state.SelectedIndex == notice.ReportState && Find<TextEdit>(child, "Text_man").Text == "موظف التقرير " + notice.Number
                            && Find<TextEdit>(child, "Text_notes").Text == "ملاحظات محفوظة " + notice.Number
                            && (DateTime)Find<DateEdit>(child, "date_check").EditValue == DateTime.Today.AddDays(-1), "Saved completion report fields were not loaded.");
                    bool canWrite = Maintenance_app.App.appuser.fordon;
                    Check(Find<FrameworkElement>(child, "ReportCard").IsEnabled == canWrite, "Notification bypassed completion edit permission.");
                    Check(Find<SimpleButton>(child, notice.NeedsReport ? "Insert" : "Edite_button").IsEnabled == canWrite, "Wrong insert/edit action state.");
                    Check(!app.Windows.OfType<maintenance_Order>().Any(window => window.IsVisible), "Notification still opened assignment preview.");
                    opened = true;
                }
                catch (Exception ex) { error = ex; }
                child.Close(); timer.Stop();
            }
            if (watch.ElapsedMilliseconds > 6000)
            {
                error = new Exception("Timed out while loading completion report."); timer.Stop();
                foreach (var window in app.Windows.Cast<Window>().Where(window => window != main && window is not Maintenance_app.MainWindow).ToArray()) window.Close();
            }
        };
        timer.Start();
        if (route == 0)
        {
            int unreadBeforeSelection = main.Notifications.Unread;
            list.SelectedItem = notice; Pump();
            Check(main.Notifications.Unread == unreadBeforeSelection, "Unread notification disappeared on selection before its click.");
            var item = Descendants(list).OfType<ListBoxItem>().FirstOrDefault(item => ReferenceEquals(item.DataContext, notice));
            Check(item is not null, "Actual notification row was not realized.");
            item!.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        }
        else
        {
            list.SelectedItem = notice; Pump();
            if (route == 1) Find<FrameworkElement>(panel, "OpenNoticeOrder").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            else list.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(main), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }
        Pump(); timer.Stop(); if (error is not null) throw error; Check(opened, "Notification route did not open completion dialog.");
    }
    #endregion

    #region التحديث المشترك لجميع الحسابات وثبات القائمة وتوقف المؤقت
    [STAThread]
    private static int Main(string[] args)
    {
        Maintenance_app.MainWindow? first = null, second = null; Maintenance_app.App? app = null;
        try
        {
            using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
            var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
            Maintenance_DabaseContext Create() => new(options);
            using (var db = Create()) Seed(db);
            app = new Maintenance_app.App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
            MethodInfo startup = typeof(Maintenance_app.App).GetMethod("Application_Startup", BindingFlags.Instance | BindingFlags.NonPublic)!;
            app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light"; Maintenance_app.App.ConnectionString = string.Empty;
            var user = new T_SystemUser { User_ID = 2147470 };
            Maintenance_app.App.appuser = user; Maintenance_app.App.login_user = user;
            foreach (int client in new[] { 1, 2 })
            {
                var main = new Maintenance_app.MainWindow(Create) { WindowState = WindowState.Normal, Width = 1366, Height = 900,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
                main.Notifications.ToastsEnabled = false;
                main.Notifications.Configure(user.User_ID, true, Path.Combine(args[0], "read-client-" + client + "-" + Guid.NewGuid().ToString("N")));
                if (client == 1) first = main; else second = main;
                main.Show(); Pump();
            }
            WaitUntil(() => first!.Notifications.Total == 3 && second!.Notifications.Total == 3, "Ordinary users did not receive all notifications.");
            Check(!Find<System.Windows.Controls.MenuItem>(first!, "CompletionMenu").IsEnabled, "Ordinary user unexpectedly gained write permission.");
            var held = first!.Notifications.Items[0];
            first.Notifications.Selected = held; Pump();
            var group = first.Notifications.View.Groups![0]; string before = first.Notifications.Status;
            Delay(2200);
            Check(first.Notifications.Status != before && ReferenceEquals(held, first.Notifications.Items[0])
                && ReferenceEquals(group, first.Notifications.View.Groups![0]) && ReferenceEquals(held, first.Notifications.Selected),
                "Periodic refresh rebuilt an unchanged list or did not run.");

            // تغيير مستقل دون نشر حدث محلي يحاكي كتابة جهاز آخر؛ يلتقطه مؤقت النافذتين.
            using (var db = Create()) { db.T_FinishedMaintenances.Single(report => report.ID == 77).State = 3; db.SaveChanges(); }
            WaitUntil(() => first.Notifications.SuspendedReportCount == 0 && second!.Notifications.SuspendedReportCount == 0
                && first.Notifications.InProgressReportCount == 2 && second.Notifications.InProgressReportCount == 2,
                "External report change did not reach both clients.");
            using (var db = Create()) { db.T_FinishedMaintenances.Single(report => report.ID == 77).State = 2; db.SaveChanges(); }
            MaintenanceNotificationUpdates.Publish();
            WaitUntil(() => first.Notifications.SuspendedReportCount == 1 && second!.Notifications.SuspendedReportCount == 1,
                "Committed local change did not refresh all open clients.");

            var assignment = first.Notifications.Items.Single(row => row.NeedsReport);
            var pending = first.Notifications.Items.Single(row => row.ReportState == 2);
            var progressing = first.Notifications.Items.Single(row => row.ReportState == 3);
            first.Notifications.Filter = 1;
            OpenNotice(app, first, assignment, 0);
            first.Notifications.Filter = 0;
            OpenNotice(app, first, pending, 0);
            user.fordon = true;
            OpenNotice(app, first, progressing, 0); OpenNotice(app, first, pending, 1); OpenNotice(app, first, assignment, 2);
            first.Notifications.MarkAllRead();
            Check(first.Notifications.Unread == 0 && second!.Notifications.Unread == 3,
                "Reading on one client hid unread work on another client.");

            // إنهاء عمل على القاعدة يزيله من القائمتين تلقائيًا؛ لا يحتاج المستخدم ضغط تحديث.
            using (var db = Create()) { db.T_FinishedMaintenances.Single(report => report.ID == 78).State = 0; db.SaveChanges(); }
            WaitUntil(() => first.Notifications.Total == 2 && second!.Notifications.Total == 2, "Final completion was retained on another client.");
            Maintenance_app.App.appuser = null; Maintenance_app.App.login_user = null;
            WaitUntil(() => first.Notifications.Total == 0 && second!.Notifications.Total == 0, "Logging out retained notification data.");
            var timer = (DispatcherTimer)typeof(Maintenance_app.MainWindow).GetField("notificationTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(first)!;
            CloseMain(first); CloseMain(second!); Check(!timer.IsEnabled, "Live update timer survived window close."); first = second = null;
            app.Shutdown(); Console.WriteLine($"PASS: {checks} live notification/navigation checks; two native windows, ordinary-user access, one-second remote polling, immediate local refresh, stable groups, all three completion states, single click/button/Enter, composite keys and write permissions; isolated SQLite only."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (first is not null) CloseMain(first); if (second is not null) CloseMain(second); app?.Shutdown(); }
    }
    #endregion
}
