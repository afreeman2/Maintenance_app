using DevExpress.Mvvm;
using Maintenance_request.converts;
using Maintenance_request.data;
using Maintenance_request.database_cotext;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevExpress.Xpf.Grid;
using MahApps.Metro.Controls;
using System.IO;
using Maintenance_request.Notifications;

// بديل خدمة التنبيه يثبت أن واجهة الموظف طلبت العرض قبل استلام الحالة، دون إرسال تنبيه خارجي.
public class ToastProxy : DispatchProxy
{
    public int Count;
    public TaskCompletionSource<NotificationResult>? Completion;
    public List<object> Models { get; } = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name.StartsWith("Create")) { Count++; if (method.Name == "CreateCustomNotification") Models.Add(args![0]!); return new TestToast(Completion?.Task); }
        return null;
    }
    private sealed class TestToast(Task<NotificationResult>? completion) : INotification
    {
        public void Hide() { }
        public Task<NotificationResult> ShowAsync() => completion ?? Task.FromResult(NotificationResult.TimedOut);
    }
}
public class TestNotificationViewModel(INotificationService service) : NotificationMainViewModel
{
    public override INotificationService DefaultNotificationService => service;
}
internal static class Program
{
    #region فحص النافذة الحقيقية مع ردود SQLite معزولة
    private static int checks;
    private static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    private static async Task RunAsync(string output)
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var options = new DbContextOptionsBuilder<Cont_maintContext>().UseSqlite(connection).Options;
        Cont_maintContext Create() => new(options);
        using (var db = Create())
        {
            db.Database.EnsureCreated();
            db.T_Notif_Action_Users.Add(new() { ID = 77, User_ID = 501, OP_ID = 2, state = 0,
                Build = "مبنى اختبار", User_Name = "عميل", Description = "طلب اختبار", Serv_type = "كهرباء", ndate = DateOnly.FromDateTime(DateTime.Today), rendate = DateTime.Today, MaintenanceCosts_No = 123 });
            db.T_Notif_Actions.Add(new() { ID = 78, DOC_ID = 77, DOC_Number = "77", User_ID = 501, OP_ID = 2, state = 0,
                Build = "مبنى اختبار", User_Name = "عميل", Description = "طلب اختبار", Serv_type = "كهرباء", ndate = DateTime.Today, MaintenanceCosts_No = 123 });
            db.SaveChanges();
        }
        Maintenance_request.App.curr_user = new T_Maintenance_request_User { User_ID = 501, User_Name = "عميل" };
        int sounds = 0;
        string receipts = Path.Combine(output, Guid.NewGuid().ToString("N"));
        var window = new Maintenance_request.MainWindow(Create, () => sounds++, receipts) { WindowState = WindowState.Normal, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
        var loaded = typeof(Maintenance_request.MainWindow).GetMethod("MetroWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!;
        window.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window, loaded);
        var service = DispatchProxy.Create<INotificationService, ToastProxy>(); var proxy = (ToastProxy)(object)service;
        window.DataContext = new TestNotificationViewModel(service); window.Show();
        var receive = typeof(Maintenance_request.MainWindow).GetMethod("ProcessRequestRepliesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task Refresh() => (Task)receive.Invoke(window, [CancellationToken.None])!;
        await Refresh(); Check(proxy.Count == 1 && window.list_request.Single().OP_ID == 2, "Executing response not displayed in client UI.");
        Check(sounds == 1 && proxy.Models.Single() is RequestToast { Operation: 2, AccentColor: "#087F8C" }, "Client did not request colored toast and one sound.");
        // نفحص القالب نفسه للحالات الست، لا قيم الكائن فقط.
        var template = (DataTemplate)window.FindResource("Request.Toast"); var colors = new HashSet<string>();
        foreach (int operation in new[] { 1, 4, 2, 7, 3, 8 })
        {
            var model = new RequestToast { Operation = operation, RequestId = 77, Details = "مبنى اختبار • كهرباء" };
            var card = (System.Windows.Controls.Border)template.LoadContent(); card.DataContext = model;
            card.Measure(new Size(370, 190)); card.Arrange(new Rect(0, 0, 370, 190)); card.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            colors.Add(model.AccentColor);
            Check(((SolidColorBrush)card.BorderBrush).Color.ToString() == "#FF" + model.AccentColor[1..], "Client template lost operation color.");
        }
        Check(colors.Count == 6, "Six request operations share a color.");
        var cards = (System.Windows.Controls.ListBox)window.FindName("NoticeList");
        // نختبر موضع المنبثق الفعلي قرب حافة الشاشة، لأن قياس محتواه وحده لا يكشف انزياحه خارج الرئيسية.
        var workArea = SystemParameters.WorkArea;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = 1000; window.Height = 660;
        window.Left = workArea.Left + 12; window.Top = workArea.Top + 12;
        window.UpdateLayout();
        window.ShowNotificationFlyout();
        await Task.Delay(750); window.UpdateLayout();
        var panel = (FrameworkElement)window.FindName("RequestsPanel");
        var frame = (FrameworkElement)window.Content;
        var anchor = (FrameworkElement)window.FindName("RequestsAnchor");
        Rect ScreenBounds(FrameworkElement element) => new(element.PointToScreen(new Point(0,0)),
            element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));
        Check(PresentationSource.FromVisual(panel) is not null && panel.ActualWidth > 0, "The popup was not rendered.");
        var panelBounds = ScreenBounds(panel);
        var frameBounds = ScreenBounds(frame);
        var anchorBounds = ScreenBounds(anchor);
        var centerDifference = Math.Abs((panelBounds.Left + panelBounds.Right - frameBounds.Left - frameBounds.Right) / 2);
        Check(frameBounds.Contains(panelBounds) && panelBounds.Top >= anchorBounds.Bottom && centerDifference <= 6,
            $"The request popup is not centered below the header. Panel: {panelBounds}, owner: {frameBounds}, anchor: {anchorBounds}.");
        Check(cards.Items.Count == 1 && ((System.Windows.Data.ListCollectionView)cards.ItemsSource).Groups?.Count == 1,
            "The request card list lost its row or grouping.");
        var search = (System.Windows.Controls.TextBox)window.FindName("NoticeSearch");
        search.Text = "لا نتيجة";
        Check(cards.Items.Count == 0, "Search did not exclude the nonmatching request.");
        search.Text = "كهرباء";
        Check(cards.Items.Count == 1, "Service name search did not find the request.");
        var filter = (System.Windows.Controls.ComboBox)window.FindName("NoticeFilter");
        filter.SelectedIndex = 1;
        Check(cards.Items.Count == 0, "Stage filter included an executing request under waiting.");
        filter.SelectedIndex = 2;
        Check(cards.Items.Count == 1, "Stage filter lost the executing request.");
        cards.SelectedIndex = 0;
        Check(((FrameworkElement)window.FindName("NoticeDetails")).Visibility == Visibility.Visible
            && ((FrameworkElement)window.FindName("EditNoticeRequest")).Visibility == Visibility.Collapsed,
            "The selected request details or editing permission is incorrect.");
        var readAll = typeof(Maintenance_request.MainWindow).GetMethod("ReadAllNotices_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
        readAll.Invoke(window, [window, new RoutedEventArgs()]);
        var badge = (DevExpress.Xpf.Core.Badge)window.FindName("bg1");
        Check(badge.Visibility == Visibility.Collapsed && badge.Opacity == 1 && window.list_request.Single().OP_ID == 2,
            "Reading notices did not reset the badge independently of the request state.");
        var reading = new ClientNotificationState();
        reading.Configure(501, receipts);
        Check(reading.IsRead(window.list_request.Single()), "The read mark was not persisted locally.");
        search.Clear();
        filter.SelectedIndex = 0;
        cards.SelectedIndex = 0;
        await Dispatcher.CurrentDispatcher.InvokeAsync(panel.UpdateLayout, DispatcherPriority.ContextIdle);
        Check(cards.ItemContainerGenerator.ContainerFromIndex(0) is System.Windows.Controls.ListBoxItem,
            "The notification card was not realized by WPF.");
        Directory.CreateDirectory(output);
        var render = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth), (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        // نعكس مسار التصيير فقط، إذ يرسم RenderTargetBitmap عنصر RTL المفصول عن نافذته معكوسًا.
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushTransform(new ScaleTransform(-1, 1, panel.ActualWidth / 2, 0));
            drawing.DrawRectangle(new VisualBrush(panel), null, new Rect(0, 0, panel.ActualWidth, panel.ActualHeight));
            drawing.Pop();
        }
        render.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(render));
        using (var file = File.Create(Path.Combine(output, "client-notification-center.png"))) png.Save(file);
        await Refresh(); Check(proxy.Count == 1 && sounds == 1, "Client repeated executing notification or sound.");
        foreach (var pair in new[] { (8, 2), (3, 6), (7, 1) })
        {
            using (var db = Create())
            {
                var row = db.T_Notif_Action_Users.Single(); row.OP_ID = pair.Item1; row.state = 0; row.rendate = DateTime.Now;
                var action = db.T_Notif_Actions.Single(); action.OP_ID = pair.Item1; action.state = 0; action.rendate = row.rendate; db.SaveChanges();
            }
            int before = proxy.Count; await Refresh();
            Check(proxy.Models.Last() is RequestToast toast && toast.Operation == pair.Item1 && toast.Title.Contains(pair.Item1 == 8 ? "تم إلغاء إنهاء الصيانة" : pair.Item1 == 7 ? "إلغاء تنفيذ الطلب" : "تم التنفيذ"), "Reply template lost original cancellation operation.");
            using (var db = Create()) Check(db.T_Notif_Action_Users.Single().OP_ID == pair.Item2 && db.T_Notif_Actions.Single().OP_ID == pair.Item1, "Client UI did not settle both rows.");
            Check(proxy.Count == before + 1, "Reply not displayed once.");
            Check(window.list_request.Single().OP_ID == pair.Item2, "Client list does not reflect stable state.");
            await Refresh(); Check(proxy.Count == before + 1, "Client replayed stable state.");
            if (pair.Item2 == 6) {
                await window.DismissCompletedNotificationAsync(77); await Refresh();
                Check(window.list_request.Count == 0, "Client manual completion dismissal did not persist.");
                using var retained = Create(); Check(retained.T_Notif_Action_Users.Any(x => x.ID == 77), "Manual dismissal deleted request needed by undo.");
            }
        }
        // الاستلام يستمر عند كتم الصوت، ولا تعاد النغمة بسبب التحديث بعده.
        await Task.Delay(1100); Maintenance_request.App.curr_user.ISsound = false; int beforeMute = sounds;
        using (var db = Create()) { foreach (var row in db.T_Notif_Action_Users) { row.OP_ID = 2; row.state = 0; row.rendate = DateTime.Now; } foreach (var action in db.T_Notif_Actions) { action.OP_ID = 2; action.state = 0; action.rendate = DateTime.Now; } db.SaveChanges(); }
        await Refresh(); Check(sounds == beforeMute && window.list_request.Single().OP_ID == 2, "Mute blocked receipt or played audio.");
        Maintenance_request.App.curr_user.ISsound = true;
        using (var db = Create()) { foreach (var row in db.T_Notif_Action_Users) { row.OP_ID = 3; row.state = 0; row.rendate = DateTime.Now; } foreach (var action in db.T_Notif_Actions) { action.OP_ID = 3; action.state = 0; action.rendate = DateTime.Now; } db.SaveChanges(); }
        await Refresh(); Check(sounds == beforeMute + 1 && window.list_request.Single().OP_ID == 6, "New completed response did not play one sound.");
        await Refresh(); Check(sounds == beforeMute + 1, "Stable receipt replayed audio.");
        // الطلب الجديد يعرض تأكيد الإرسال حتى لو استلمه المدير أولًا (state=1).
        using (var db = Create())
        {
            var revision = DateTime.Now;
            db.T_Notif_Action_Users.Add(new() { ID = 99, User_ID = 501, OP_ID = 1, state = 1, ndate = DateOnly.FromDateTime(revision), rendate = revision, User_Name = "عميل", Build = "مبنى اختبار", Description = "طلب جديد", Serv_type = "كهرباء" });
            db.T_Notif_Actions.Add(new() { ID = 100, DOC_ID = 99, User_ID = 501, DOC_Number = "99", OP_ID = 1, state = 1, ndate = revision, rendate = revision, User_Name = "عميل", Build = "مبنى اختبار", Description = "طلب جديد", Serv_type = "كهرباء" });
            db.SaveChanges();
        }
        int beforeNew = proxy.Count; await Refresh();
        Check(proxy.Count == beforeNew + 1 && proxy.Models.Last() is RequestToast { Title: "طلب جديد", Text: "تم إرسال طلبك إلى القسم المختص" }, "Client sent confirmation is missing or exposes request number.");
        await Refresh(); Check(proxy.Count == beforeNew + 1, "Sent confirmation repeated.");
        proxy.Completion = new(); await new RequestClientStore(Create).CancelAsync(99, 501); await Refresh();
        Check(window.list_request.Any(x => x.ID == 99 && x.OP_ID == 4), "Cancellation removed before toast finished.");
        Check(proxy.Models.Last() is RequestToast { Text: "تم إلغاء طلب الصيانة الخاص بكم" }, "Client cancellation text differs from specification.");
        // قد يحذف مستخدم المنظومة قناة الإلغاء قبل نهاية تنبيه العميل؛ رأسه يكفي لإتمام الإخفاء.
        using (var db = Create()) { db.T_Notif_Actions.RemoveRange(db.T_Notif_Actions.Where(x => x.DOC_ID == 99)); db.SaveChanges(); }
        proxy.Completion.SetResult(NotificationResult.TimedOut); proxy.Completion = null;
        await Task.Delay(30); await Refresh();
        Check(window.list_request.All(x => x.ID != 99), "Cancellation remained after toast ended.");
        cards.SelectedIndex = 0;
        Check(window.list_request.Single().OP_ID == 6 && ((FrameworkElement)window.FindName("DismissNotice")).Visibility == Visibility.Visible,
            "Completed request lacks client manual-removal action.");
        await window.DismissCompletedNotificationAsync(77); await Refresh(); Check(window.list_request.Count == 0, "Completed request not removed by client action.");
        var restarted = new Maintenance_request.MainWindow(Create, () => sounds++, receipts);
        restarted.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), restarted, loaded);
        restarted.DataContext = new TestNotificationViewModel(service); int beforeRestart = proxy.Count;
        await (Task)receive.Invoke(restarted, [CancellationToken.None])!;
        Check(restarted.list_request.Count == 0 && proxy.Count == beforeRestart, "Dismissed/cancelled notification returned after client restart."); restarted.Close();
        using (var db = Create())
        {
            var row = db.T_Notif_Action_Users.Single(x => x.ID == 77); Check(row.OP_ID == 6, "Client dismissal destroyed server request.");
            row.OP_ID = 8; row.state = 0; row.rendate = DateTime.Now;
            var action = db.T_Notif_Actions.Single(x => x.DOC_ID == 77); action.OP_ID = 8; action.state = 0; action.rendate = row.rendate; db.SaveChanges();
        }
        await Refresh();
        Check(window.list_request.Single().OP_ID == 2 && proxy.Models.Last() is RequestToast { Operation: 8, Text: "تم إلغاء تنفيذ طلب الصيانة الخاص بكم" }, "Undo after client manual dismissal failed to restore executing request.");
        using (var db = Create()) { db.T_Notif_Actions.RemoveRange(db.T_Notif_Actions); db.T_Notif_Action_Users.RemoveRange(db.T_Notif_Action_Users); db.SaveChanges(); }
        await Refresh(); Check(window.list_request.Count == 0, "Remote deletion left stale client row.");
        Check(DevExpress.Xpf.Core.ThemeManager.GetThemeName(window) == "Win11Light", "Client theme differs from user standard.");
        // التطبيق الآخر يبني المورد المشترك داخل تجميعته أيضًا.
        Check(new Maintenance.Shared.NotificationSound().TryPlay(true), "Client could not start the embedded notification sound.");
        // إبقاء نافذة الاختبار خارج الشاشة ثم إغلاقها دون تشغيل خروج التطبيق الأصلي.
        window.Close();
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Maintenance_request.App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
            var startup = typeof(Maintenance_request.App).GetMethod("Application_Startup", BindingFlags.Instance | BindingFlags.NonPublic)!;
            app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var task = RunAsync(args[0]); var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); task.GetAwaiter().GetResult();
            app.Shutdown(); Console.WriteLine($"PASS: {checks} actual customer-window checks; notification invocation, 8->2/3->6/7->1, no repeat, remote removal and Win11Light. SQLite only."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion
}
