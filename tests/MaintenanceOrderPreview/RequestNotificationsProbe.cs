using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Editors.Flyout;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Maintenance_app.orders_Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Application = System.Windows.Application;
using Size = System.Windows.Size;

internal static class RequestNotificationsProbe
{
    #region أدوات فحص نافذتين وتحديثات مستقلة عن الجلسة
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static T Find<T>(FrameworkElement root, string name) where T : class => (T)root.FindName(name);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static void Wait(Func<bool> condition, string text)
    {
        var clock = Stopwatch.StartNew(); var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) => { if (condition() || clock.ElapsedMilliseconds > 6000) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); Check(condition(), text);
    }
    private static void Await(Task task) { Wait(() => task.IsCompleted, "Task did not complete."); task.GetAwaiter().GetResult(); }
    private static void Close(Maintenance_app.MainWindow window)
    {
        typeof(Maintenance_app.MainWindow).GetField("confirmedClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true); window.Close();
    }
    #endregion
    #region العرض العام والمشاهدة المستقلة وفلترة الطلبات
    [STAThread]
    private static int Main(string[] args)
    {
        var windows = new List<Maintenance_app.MainWindow>();
        try
        {
            Directory.CreateDirectory(args[0]);
            var app = new Maintenance_app.App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
            var startup = typeof(Maintenance_app.App).GetMethod("Application_Startup", BindingFlags.Instance | BindingFlags.NonPublic)!;
            app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light"; Maintenance_app.App.ConnectionString = "";
            Maintenance_app.App.appuser = new T_SystemUser { User_ID = 901, User_Name = "معاينة", formangreq = true };
            using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
            var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
            Maintenance_DabaseContext Create() => new(options);
            using (var db = Create())
            {
                typeof(NotificationRoutingProbe).GetMethod("Seed", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [db]);
                // نفس أسماء الجهات في موقعين، وأكثر من قسم في الإدارة حتى يختبر الفحص المطابقة لا الاختيار الأول.
                foreach (short locationId in new short[] { 1, 2 })
                {
                    string location = "موقع اختبار " + locationId;
                    db.T_Manages.Add(new() { Location = location, Location_ID = locationId, Branch_ID = 1, Manag_ID = 1, Manag = "إدارة الخدمات" });
                    db.T_Sections.AddRange(
                        new T_Section { Location = location, Location_ID = locationId, Branch_ID = 1, Manag_ID = 1, Secti_ID = 1, Secti = "قسم المتابعة", Date_Modify = DateOnly.FromDateTime(DateTime.Today) },
                        new T_Section { Location = location, Location_ID = locationId, Branch_ID = 1, Manag_ID = 1, Secti_ID = 2, Secti = "وحدة المباني", Date_Modify = DateOnly.FromDateTime(DateTime.Today) });
                    db.T_Manages.AddRange(
                        new T_Manage { Location = location, Location_ID = locationId, Branch_ID = 1, Manag_ID = 2, Manag = "إدارة الشبكات والاتصالات" },
                        new T_Manage { Location = location, Location_ID = locationId, Branch_ID = 1, Manag_ID = 3, Manag = "إدارة الشبكات ولاتصالات" });
                    db.T_Sections.AddRange(
                        new T_Section { Location = location, Location_ID = locationId, Branch_ID = 1, Manag_ID = 2, Secti_ID = 3, Secti = "إدارة الشبكات ولاتصالات ", Date_Modify = DateOnly.FromDateTime(DateTime.Today) },
                        new T_Section { Location = location, Location_ID = locationId, Branch_ID = 1, Manag_ID = 3, Secti_ID = 4, Secti = "قسم الدعم", Date_Modify = DateOnly.FromDateTime(DateTime.Today) },
                        new T_Section { Location = location, Location_ID = locationId, Branch_ID = 1, Manag_ID = 3, Secti_ID = 5, Secti = "وحدة الشبكات", Date_Modify = DateOnly.FromDateTime(DateTime.Today) });
                }
                foreach (long id in new long[] { 9001, 9002 }) db.T_Notif_Action_Users.Add(new() { ID = id, User_ID = id + 10, OP_ID = id == 9001 ? 1 : 4,
                    state = id == 9001 ? 0 : 1, Description = "طلب صيانة من تطبيق العميل", Build_ID = 1, Serv_num = 1,
                    Build = "مبنى 2", Serv_type = "خدمة 2", User_Name = "موظف الطلب", Mangment_Request = "إدارة الخدمات – قسم المتابعة – وحدة المباني", ndate = DateOnly.FromDateTime(DateTime.Today) });
                db.T_Notif_Actions.Add(new() { ID = 9003, DOC_ID = 9002, User_ID = 9012, OP_ID = 4, state = 0,
                    DOC_Number = "9002", Build = "مبنى 2", Serv_type = "خدمة 2", User_Name = "موظف الطلب", Mangment_Request = "جهة المعاينة", Description = "طلب ألغاه العميل", ndate = DateTime.Today, rendate = DateTime.Now });
                db.SaveChanges();
            }
            foreach (int i in new[] { 1, 2 })
            {
                var window = new Maintenance_app.MainWindow(Create) { Width = 1366, Height = 900, WindowState = WindowState.Normal, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
                window.Notifications.ToastsEnabled = false;
                window.Notifications.Configure(901, true, Path.Combine(args[0], "session-" + i + Guid.NewGuid().ToString("N")));
                windows.Add(window); window.Show(); Pump();
            }
            Wait(() => windows.All(x => x.Notifications.Items.Count(n => n.IsRequest) == 2), "Both windows did not receive all customer requests.");
            var first = windows[0]; var second = windows[1];
            Check(first.Notifications.Items.Any(x => x.NeedsReport) && first.Notifications.SuspendedReportCount == 1, "Request integration removed maintenance notifications.");
            first.Notifications.Filter = 7; Check(first.Notifications.View.Cast<MaintenanceNotice>().All(x => x.IsRequest), "Request-only filter failed.");
            first.Notifications.Filter = 2; Check(first.Notifications.View.Cast<MaintenanceNotice>().All(x => !x.IsRequest && x.NeedsReport), "Requests leaked into missing-report filter.");
            first.Notifications.Filter = 0;
            var closed = first.Notifications.Items.Single(x => x.RequestId == 9002); first.Notifications.Selected = closed; Pump();
            var panel = Find<NotificationCenter>(first, "NoticePanel");
            Check(Find<FrameworkElement>(panel, "ArchiveNoticeRequest").IsEnabled && closed.CanArchive && closed.RequestOperation == 4, "Manager cancellation-removal action unavailable.");
            var stateA = new MaintenanceNotificationState(); var stateB = new MaintenanceNotificationState();
            stateA.Configure(100, true, Path.Combine(args[0], Guid.NewGuid().ToString("N"))); stateB.Configure(200, true, Path.Combine(args[0], Guid.NewGuid().ToString("N")));
            var store = new MaintenanceNotificationStore(Create);
            var snapshotTask = store.LoadAsync(Maintenance_app.App.appuser); Await(snapshotTask);
            stateA.Apply(snapshotTask.Result); stateB.Apply(snapshotTask.Result.Select(n => new MaintenanceNotice { RequestId = n.RequestId, RequestOperation = n.RequestOperation, Number = n.Number, Location = n.Location, LocationId = n.LocationId, BranchId = n.BranchId, NeedsReport = n.NeedsReport, ReportState = n.ReportState }));
            stateA.MarkRead(stateA.Items.Single(x => x.RequestId == 9001));
            Check(stateB.Items.Single(x => x.RequestId == 9001).IsUnread, "One user's reading marked another user's notification as read.");
            Find<FlyoutControl>(first, "NotificationFlyout").IsOpen = true;
            Await(first.RefreshMaintenanceNotificationsAsync());
            using (var db = Create()) Check(db.T_Notif_Action_Users.Single(x => x.ID == 9001).state == 1, "Visible request was not received.");
            Wait(() => second.Notifications.Items.Any(x => x.RequestId == 9001), "First manager's receipt hid the request from second window.");
            // نقرة زر المركز تفتح شاشة التكليف الفعلية وتجهز بيانات العميل من القاعدة المعزولة.
            first.Notifications.Selected = first.Notifications.Items.Single(x => x.RequestId == 9001); Pump();
            Exception? routeFailure = null; bool routed = false;
            var routeClock = Stopwatch.StartNew();
            var routeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            routeTimer.Tick += (_, _) =>
            {
                var order = app.Windows.OfType<maintenance_Order>().FirstOrDefault();
                if (order is null) return;
                var inspector = Find<ComboBoxEdit>(order, "Combo_Check_Man");
                if (!inspector.IsKeyboardFocusWithin && routeClock.ElapsedMilliseconds < 6000) return;
                routeTimer.Stop();
                try
                {
                    Check(order.RequestId == 9001 && Find<TextEdit>(order, "Text_dtils").Text == "طلب صيانة من تطبيق العميل", "Request number or description lost on opening assignment.");
                    Check(inspector.IsKeyboardFocusWithin && !inspector.IsPopupOpen, "Opening a new request did not focus the inspector field with its list closed.");
                    Check(Find<ComboBoxEdit>(order, "Combo_Buildings").SelectedItem is T_Build { Location_ID: 2 }
                        && Find<ComboBoxEdit>(order, "Combo_Services").SelectedItem is T_Service { Location_ID: 2 }, "Request prefill chose another location.");
                    var management = Find<ComboBoxEdit>(order, "Combo_Management");
                    var section = Find<ComboBoxEdit>(order, "Combo_Department");
                    Check(management.SelectedItem is T_Manage { Location_ID: 2, Manag: "إدارة الخدمات" }
                        && section.SelectedItem is T_Section { Location_ID: 2, Secti: "وحدة المباني" }, "Request did not select its saved management and section.");
                    // إذا لم يذكر الطلب قسمًا وكانت له عدة خيارات، تظل الخانة للمستخدم.
                    var prefillDepartment = typeof(maintenance_Order).GetMethod("PrefillRequestDepartment", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var building = Find<ComboBoxEdit>(order, "Combo_Buildings").SelectedItem;
                    prefillDepartment.Invoke(order, [building, "إدارة الخدمات، قسم المتابعة"]);
                    Check(section.SelectedItem is T_Section { Location_ID: 2, Secti: "قسم المتابعة" }, "Request did not select a saved section without a unit.");
                    prefillDepartment.Invoke(order, [building, "إدارة الخدمات"]);
                    Check(management.SelectedItem is T_Manage { Location_ID: 2 } && section.SelectedItem is null, "Prefill guessed a department with multiple choices.");
                    // اسم الجهة يطابق إدارةً وبندًا في إدارة أخرى يختلف اسمها بألف واحدة.
                    prefillDepartment.Invoke(order, [building, "إدارة الشبكات ولاتصالات"]);
                    Check(management.SelectedItem is T_Manage { Location_ID: 2, Manag_ID: 2 }
                        && section.SelectedItem is T_Section { Location_ID: 2, Secti_ID: 3 }, "Prefill preferred the exact management name over its matching section.");
                    routed = true;
                }
                catch (Exception ex) { routeFailure = ex; }
                finally { order.Close(); }
            };
            routeTimer.Start();
            Find<FrameworkElement>(panel, "OpenNoticeOrder").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            routeTimer.Stop(); if (routeFailure is not null) throw routeFailure;
            Check(routed, "New-request action did not open the assignment window.");
            // كتابة مستقلة تحاكي تطبيق العميل على جهاز آخر، دون نشر إشارة محلية.
            using (var db = Create()) { var request = db.T_Notif_Action_Users.Single(x => x.ID == 9001); request.OP_ID = 2; request.MaintenanceCosts_No = 78; db.SaveChanges(); }
            Wait(() => windows.All(x => x.Notifications.Items.Any(n => n.RequestId == 9001 && n.RequestOperation == 2 && n.RequestHasOrder)), "Remote execution change did not reach both windows.");
            Check(first.Notifications.Items.Single(x => x.RequestId == 9001).Key.Number == 78, "Linked request opened request ID instead of assignment number.");
            var status = new MaintenanceNotificationState(); status.Configure(300, true, Path.Combine(args[0], Guid.NewGuid().ToString("N")));
            status.Apply([new MaintenanceNotice { RequestId = 90, RequestOperation = 3 }]); status.MarkAllRead();
            Check(status.Apply([new MaintenanceNotice { RequestId = 90, RequestOperation = 6 }]).Count == 0 && status.Unread == 0, "Receipt 3->6 replayed a final notification.");
            var transient = new MaintenanceNotice { RequestId = 88, RequestOperation = 4, IsTransient = true };
            Check(status.Apply([transient]).Count == 1 && status.Total == 0, "Cancellation remained in active list.");
            status.MarkTransientDelivered([transient]); Check(status.Apply([transient]).Count == 0, "Cancellation replayed for same account.");
            // لقطة مركز مستقل تمنع انعكاس HWND العربي في الصورة المصدرة.
            first.Notifications.Filter = 7;
            first.Notifications.Selected = first.Notifications.Items.Single(x => x.RequestId == 9002); Pump();
            var imagePanel = new NotificationCenter { DataContext = first.Notifications, Width = 780, Height = 900 };
            imagePanel.Measure(new Size(780, 900)); imagePanel.Arrange(new Rect(0, 0, 780, 900)); imagePanel.UpdateLayout();
            var bitmap = new RenderTargetBitmap(780, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(imagePanel);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(args[0], "request-notification-center.png"))) png.Save(file);
            // نقرة واحدة تختار البطاقة، والثانية تحذف إشعار الإلغاء من القناة المشتركة.
            var remove = typeof(Maintenance_app.MainWindow).GetMethod("CancellationDoubleClicked", BindingFlags.Instance | BindingFlags.NonPublic)!;
            imagePanel.CancellationDoubleClicked += (EventHandler<MaintenanceNotice>)remove.CreateDelegate(typeof(EventHandler<MaintenanceNotice>), first);
            var list = Find<ListBox>(imagePanel, "NoticeList");
            list.ScrollIntoView(first.Notifications.Selected);
            imagePanel.UpdateLayout();
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(first.Notifications.Selected);
            item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            using (var db = Create()) Check(db.T_Notif_Actions.Any(x => x.DOC_ID == 9002), "A single click removed the cancellation.");
            var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 1, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.GetSetMethod(true)!.Invoke(doubleClick, [2]);
            item.RaiseEvent(doubleClick);
            Wait(() => windows.All(x => x.Notifications.Items.All(n => n.RequestId != 9002)), "Double-click did not remove the cancellation from both lists.");
            using (var db = Create())
            {
                Check(db.T_Notif_Action_Users.Any(x => x.ID == 9002), "Double-click removed the client request.");
                db.T_Notif_Action_Users.Add(new() { ID = 9004, User_ID = 9014, OP_ID = 4, state = 1, Description = "إلغاء للتجربة", Build = "مبنى 2", Serv_type = "خدمة 2", User_Name = "عميل", ndate = DateOnly.FromDateTime(DateTime.Today) });
                db.T_Notif_Actions.Add(new() { ID = 9005, DOC_ID = 9004, User_ID = 9014, OP_ID = 4, state = 1, DOC_Number = "9004", Description = "إلغاء للتجربة", Build = "مبنى 2", Serv_type = "خدمة 2", User_Name = "عميل", ndate = DateTime.Today });
                db.SaveChanges();
            }
            Wait(() => first.Notifications.Items.Any(n => n.RequestId == 9004), "Second cancellation did not appear.");
            var toastService = DispatchProxy.Create<DevExpress.Mvvm.INotificationService, AppearanceToastProxy>();
            var toastProxy = (AppearanceToastProxy)(object)toastService;
            toastProxy.Completion = new();
            typeof(Maintenance_app.MainWindow).GetField("notificationService", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(first, toastService);
            var show = typeof(Maintenance_app.MainWindow).GetMethod("ShowMaintenanceToastAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var showing = (Task)show.Invoke(first, [new List<MaintenanceNotice> { first.Notifications.Items.Single(n => n.RequestId == 9004) }])!;
            Check(((Maintenance_app.MainWindow.MaintenanceToast)toastProxy.Models.Single()).ActionHint == "اضغط لحذف إشعار الإلغاء", "Cancellation balloon has an incorrect click hint.");
            toastProxy.Completion.SetResult(DevExpress.Mvvm.NotificationResult.Activated);
            Await(showing);
            using (var db = Create()) Check(!db.T_Notif_Actions.Any(x => x.DOC_ID == 9004) && db.T_Notif_Action_Users.Any(x => x.ID == 9004), "Balloon click did not remove just the cancellation notification.");
            foreach (var window in windows) Close(window); windows.Clear(); app.Shutdown();
            Console.WriteLine($"PASS: {checks} live WPF request notification checks; two windows, all-customer visibility, independent read state, real one-second remote update, manager action, filters, receipt identity and cancellation deduplication. SQLite only."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { foreach (var window in windows) Close(window); Application.Current?.Shutdown(); }
    }
    #endregion
}
