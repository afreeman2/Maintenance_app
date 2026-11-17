using DevExpress.Mvvm;
using Maintenance.Shared;
using Maintenance_app.Notifications;
using Maintenance_app.Models;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Size = System.Windows.Size;
using Brushes = System.Windows.Media.Brushes;
using FlowDirection = System.Windows.FlowDirection;

// بديل العرض يسجل بيانات القالب دون إرسال تنبيه تجريبي إلى سطح المكتب.
public class AppearanceToastProxy : DispatchProxy
{
    public List<object> Models { get; } = [];
    public int Shows;
    public TaskCompletionSource<NotificationResult>? Completion;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "CreateCustomNotification") { Models.Add(args![0]!); return new Toast(this); }
        return null;
    }
    private sealed class Toast(AppearanceToastProxy owner) : INotification
    {
        public void Hide() { }
        public Task<NotificationResult> ShowAsync() { owner.Shows++; return owner.Completion?.Task ?? Task.FromResult(NotificationResult.TimedOut); }
    }
}
internal static class RequestAppearanceProbe
{
    #region فحص ألوان القالب والصوت وتسوية الإلغاء
    private static int checks;
    private static void Check(bool ok, string text) { checks++; if (!ok) throw new Exception(text); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task Run(string output)
    {
        Directory.CreateDirectory(output); Maintenance_app.App.appuser = new T_SystemUser { User_ID = 9901 };
        int sounds = 0;
        var window = new Maintenance_app.MainWindow(null!, () => sounds++);
        string receipts = Path.Combine(output, Guid.NewGuid().ToString("N")); window.Notifications.Configure(9901, true, receipts);
        var service = DispatchProxy.Create<INotificationService, AppearanceToastProxy>(); var proxy = (AppearanceToastProxy)(object)service;
        typeof(Maintenance_app.MainWindow).GetField("notificationService", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, service);
        var show = typeof(Maintenance_app.MainWindow).GetMethod("ShowMaintenanceToastAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Task Show(MaintenanceNotice row) => (Task)show.Invoke(window, [new List<MaintenanceNotice> { row }])!;
        var template = (DataTemplate)window.FindResource("Maintenance.Toast");
        var sheet = new UniformGrid { Columns = 2, Rows = 3, Width = 800, Height = 810, Background = Brushes.White, FlowDirection = FlowDirection.RightToLeft };
        var colors = new HashSet<string>();
        foreach (int operation in new[] { 1, 4, 2, 7, 3, 8 })
        {
            var row = new MaintenanceNotice { RequestId = 70 + operation, Number = 70 + operation, RequestOperation = operation, RequestDeliveryState = 1, Building = "مبنى اختبار", Service = "كهرباء", Employees = "أحمد", Branch = "إدارة الخدمات - وحدة المباني" };
            await Show(row); var model = (Maintenance_app.MainWindow.MaintenanceToast)proxy.Models.Last();
            Check(model.Title == row.ToastTitle && model.AccentColor == row.ToastAccentColor && model.TintColor == row.ToastTintColor, "Manager toast lost operation title/color."); colors.Add(model.AccentColor);
            Check(model.Text.Contains("أحمد") && model.Text.Contains("إدارة الخدمات - وحدة المباني") && model.Text.Contains("كهرباء") && !model.Text.Contains("طلب رقم"), "Manager message missing customer, department/service or contains request number.");
            var card = (Border)template.LoadContent(); card.DataContext = model;
            sheet.Children.Add(new Border { Padding = new Thickness(12), Child = card });
            card.Measure(new Size(350, double.PositiveInfinity));
            Check(card.DesiredSize.Height < 150, "Typical balloon has excessive height.");
            card.VerticalAlignment = VerticalAlignment.Top;
            card.Arrange(new Rect(0, 0, 350, card.DesiredSize.Height)); Pump();
            Check(((SolidColorBrush)card.BorderBrush).Color.ToString() == "#FF" + row.ToastAccentColor[1..], "Manager XAML color binding failed.");
            Check(Walk(card).OfType<TextBlock>().Any(x => x.Text == row.ToastTitle), "Manager XAML title binding failed.");
        }
        Check(colors.Count == 6 && proxy.Shows == 6, "Missing operation color or duplicate Show.");
        var longest = new MaintenanceNotice { RequestId = 999, RequestOperation = 1, Employees = new string('م', 100), Branch = new string('ق', 150), Service = new string('خ', 50) };
        var longCard = (Border)template.LoadContent(); longCard.DataContext = new Maintenance_app.MainWindow.MaintenanceToast { Title = longest.ToastTitle, Text = longest.ToastText, AccentColor = longest.ToastAccentColor, TintColor = longest.ToastTintColor };
        longCard.Measure(new Size(350, double.PositiveInfinity));
        Check(longCard.DesiredSize.Height <= 320, "Maximum-length customer/department/service text exceeds toast height: " + longCard.DesiredSize.Height);
        Check(sounds == 1, "Simultaneous sounds overlapped or were absent.");
        await Task.Delay(1100); window.Notifications.SoundEnabled = false;
        await Show(new() { Number = 88, NeedsReport = true }); Check(sounds == 1, "Muted notification played sound.");
        window.Notifications.SoundEnabled = true; await Show(new() { Number = 89, NeedsReport = true }); Check(sounds == 2, "Sound did not resume.");
        // عند تجميع إلغائين يبقى عنوان الإلغاء ظاهرًا، ولا يستبدل باسم مجموعة الطلبات الواردة.
        await (Task)show.Invoke(window, [new List<MaintenanceNotice> {
            new() { RequestId = 601, Number = 601, RequestOperation = 7, RequestDeliveryState = 1 },
            new() { RequestId = 602, Number = 602, RequestOperation = 7, RequestDeliveryState = 1 } }])!;
        var grouped = (Maintenance_app.MainWindow.MaintenanceToast)proxy.Models.Last();
        Check(grouped.Title.Contains("إلغاء تنفيذ الطلب") && grouped.Title.StartsWith("2"), "Grouped cancellation lost its title.");
        Check(grouped.AccentColor == "#B65C09" && grouped.Text.Contains("طلبات الصيانة"), "Grouped cancellation lost its color or request text.");
        // إيصال إلغاء مستقل دون إعادة تنبيه المرحلة المستقرة بعد الاستلام.
        var state = new MaintenanceNotificationState(); state.Configure(51, true, Path.Combine(output, Guid.NewGuid().ToString("N")));
        MaintenanceNotice Notice(int op) => new() { RequestId = 100, Number = 100, RequestOperation = op };
        state.Apply([Notice(1)]); state.MarkAllRead(); state.MarkTransportDelivered([Notice(1)]);
        Check(state.Apply([Notice(7)]).Single().RequestOperation == 7, "Execution cancellation hidden by earlier incoming request.");
        state.MarkTransportDelivered([Notice(7)]); Check(state.Apply([Notice(7)]).Count == 0, "Cancellation repeated on poll.");
        Check(state.Apply([new MaintenanceNotice { RequestId = 100, Number = 100, RequestOperation = 1, RequestEventOperation = 7 }]).Count == 0, "7->1 replayed incoming notification.");
        state.Apply([Notice(2)]); state.MarkAllRead(); state.MarkTransportDelivered([Notice(2)]);
        Check(state.Apply([Notice(8)]).Single().RequestOperation == 8, "Completion cancellation hidden by earlier executing state.");
        state.MarkTransportDelivered([Notice(8)]); Check(state.Apply([new MaintenanceNotice { RequestId = 100, Number = 100, RequestOperation = 2, RequestEventOperation = 8 }]).Count == 0, "8->2 replayed execution notification.");
        var now = DateTimeOffset.UtcNow; int calls = 0;
        var sound = new NotificationSound(() => calls++, () => now);
        Check(!sound.TryPlay(false) && calls == 0, "Mute ignored.");
        Check(sound.TryPlay(true) && !sound.TryPlay(true) && calls == 1, "Audio burst not coalesced.");
        now += TimeSpan.FromSeconds(2); Check(sound.TryPlay(true) && calls == 2, "Next audio notification lost.");
        Check(!new NotificationSound(() => throw new InvalidOperationException("اختبار فشل الصوت")).TryPlay(true), "Audio failure escaped.");
        // نفحص تحميل المورد من تجميعة المنظومة وتشغيله عبر المسار الفعلي.
        Check(new NotificationSound().TryPlay(true), "Manager could not start the embedded notification sound.");
        // لا يختفي الإنجاز قبل نهاية التنبيه، ولا يؤثر إخفاؤه على حساب مدير آخر أو دورة جديدة.
        var revision = DateTime.Now;
        var final = new MaintenanceNotice { RequestId = 700, Number = 700, RequestOperation = 3, RequestRevision = revision, RequestDeliveryState = 1 };
        window.Notifications.Apply([final]); proxy.Completion = new(); var displaying = Show(final);
        Check(!displaying.IsCompleted && window.Notifications.Total == 1, "Manager completion disappeared before toast ended.");
        proxy.Completion.SetResult(NotificationResult.TimedOut); await displaying; proxy.Completion = null;
        Check(window.Notifications.Total == 0 && window.Notifications.Apply([final]).Count == 0, "Manager completion returned after automatic removal.");
        var restarted = new MaintenanceNotificationState(); restarted.Configure(9901, true, receipts); restarted.Apply([final]);
        Check(restarted.Total == 0, "Completed manager notification returned after restart.");
        var incomplete = new MaintenanceNotificationState(); incomplete.Configure(8800, true, receipts); incomplete.MarkTransportDelivered([final]);
        var resume = new MaintenanceNotificationState(); resume.Configure(8800, true, receipts);
        Check(resume.Apply([final]).Count == 1, "Interrupted final toast left a permanently stuck completion card.");
        var another = new MaintenanceNotificationState(); another.Configure(9902, true, receipts);
        Check(another.Apply([final]).Count == 1, "One manager's auto-removal hid completion from another account.");
        var undo = new MaintenanceNotice { RequestId = 700, Number = 700, RequestOperation = 2, RequestEventOperation = 8, RequestRevision = revision.AddSeconds(1) };
        Check(restarted.Apply([undo]).Single().ToastTitle == "تم إلغاء إنهاء الصيانة" && restarted.Items.Single().GroupName == "طلبات جاري تنفيذها", "Completion rollback did not restore executing card.");
        var cancelled = new MaintenanceNotice { RequestId = 701, Number = 701, RequestOperation = 4 };
        restarted.Apply([cancelled]); restarted.MarkTransportDelivered([cancelled]); restarted.Apply([cancelled]);
        Check(restarted.Total == 1 && restarted.Items.Single().CanArchive, "Client cancellation disappeared without manager manual action.");
        sheet.Measure(new Size(800, 810)); sheet.Arrange(new Rect(0, 0, 800, 810)); sheet.UpdateLayout(); Pump();
        var bitmap = new RenderTargetBitmap(800, 810, 96, 96, PixelFormats.Pbgra32); bitmap.Render(sheet);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, "six-request-notifications.png"))) png.Save(file);
        typeof(Maintenance_app.MainWindow).GetField("confirmedClose", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true); window.Close();
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Maintenance_app.App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
            var startup = typeof(Maintenance_app.App).GetMethod("Application_Startup", BindingFlags.Instance | BindingFlags.NonPublic)!;
            app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var task = Run(args[0]); var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); task.GetAwaiter().GetResult();
            app.Shutdown(); Console.WriteLine($"PASS: {checks} manager appearance/audio checks; six rendered operations, one Show, audio invocation/mute/coalescing/failure, cancellation receipts and no stable-state replay. No work database or real audio output."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion
}
