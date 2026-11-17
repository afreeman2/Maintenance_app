using DevExpress.Mvvm;
using DevExpress.Mvvm.UI;
using DevExpress.Mvvm.UI.Interactivity;
using DevExpress.Xpf.Core;
using Maintenance_app.dbContext;
using Maintenance_app.Dashboard;
using Maintenance_app.Models;
using Maintenance_app.Notifications;
using Maintenance_app.orders_Windows;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Maintenance_app.Requests;
using Maintenance.Shared;

namespace Maintenance_app;

public partial class MainWindow
{
    #region حالة الإشعارات ودورة الحياة

    public MaintenanceNotificationState Notifications { get; } = new MaintenanceNotificationState();
    private readonly DispatcherTimer notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    private readonly Func<Maintenance_DabaseContext>? notificationContextFactory;
    // مؤقت مستقل يومض العدّاد فقط؛ لا يغير عدد الإشعارات أو معدل قراءة البيانات.
    private readonly DispatcherTimer notificationBadgeBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly CancellationTokenSource notificationCancellation = new CancellationTokenSource();
    private readonly HashSet<INotification> activeToasts = new HashSet<INotification>();
    private readonly HashSet<long> cancellationRemovals = new();
    private INotificationService? notificationService;
    private readonly NotificationSound notificationSound;
    private bool notificationsBusy;
    private bool notificationsRefreshRequested;
    private bool notificationsClosed;

    private void InitializeNotifications()
    {
        NoticePanel.DataContext = Notifications;
        NoticePanel.CloseRequested += CloseNotifications;
        NoticePanel.RefreshRequested += RefreshNotificationsRequested;
        NoticePanel.OpenRequested += OpenNotificationOrder;
        NoticePanel.ArchiveRequested += ArchiveNotificationRequest;
        NoticePanel.CancellationDoubleClicked += CancellationDoubleClicked;
        Notifications.PropertyChanged += NotificationStateChanged;
        MaintenanceNotificationUpdates.Changed += MaintenanceData_Changed;
        notificationService = new NotificationService
        {
            CustomNotificationTemplate = (DataTemplate)FindResource("Maintenance.Toast"),
            CustomNotificationStyle = (Style)FindResource("Maintenance.ToastStyle"),
            CustomNotificationPosition = NotificationPosition.BottomRight,
            CustomNotificationDuration = TimeSpan.FromSeconds(5),
            CreateApplicationShortcut = false,
            UseWin8NotificationsIfAvailable = false
        };
        // التنبيهات المخصصة تعمل دون تسجيل اختصار أو مكونات COM في Windows.
        Interaction.GetBehaviors(this).Add((NotificationService)notificationService);
        notificationTimer.Tick += NotificationTimer_Tick;
        notificationBadgeBlinkTimer.Tick += NotificationBadgeBlink_Tick;
        Loaded += Notifications_Loaded;
        PreviewKeyDown += Notifications_KeyDown;
        SizeChanged += Notifications_SizeChanged;
        Closed += Notifications_Closed;
    }

    private async void Notifications_Loaded(object sender, RoutedEventArgs e)
    {
        notificationTimer.Start();
        await RefreshMaintenanceNotificationsAsync();
    }

    private async void NotificationTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshMaintenanceNotificationsAsync();
    }

    // الحفظ داخل الجلسة يوقظ القراءة فورًا؛ المؤقت يلتقط تغييرات بقية الأجهزة كل ثانية.
    private void MaintenanceData_Changed(object? sender, EventArgs e)
    {
        if (notificationsClosed || !IsLoaded || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() => _ = RefreshMaintenanceNotificationsAsync()));
    }

    private void Notifications_Closed(object? sender, EventArgs e)
    {
        notificationsClosed = true;
        notificationTimer.Stop();
        UpdateNotificationBadgeBlink();
        notificationBadgeBlinkTimer.Tick -= NotificationBadgeBlink_Tick;
        notificationCancellation.Cancel();
        NotificationFlyout.IsOpen = false;
        foreach (INotification toast in activeToasts.ToArray()) toast.Hide();
        Notifications.PropertyChanged -= NotificationStateChanged;
        MaintenanceNotificationUpdates.Changed -= MaintenanceData_Changed;
    }

    #endregion

    #region تحميل القراءة فقط والتحديث والتعامل مع انقطاع الاتصال

    public async Task RefreshMaintenanceNotificationsAsync()
    {
        if (notificationsClosed) return;
        // نجمع طلبات التحديث المتزامنة بدل تشغيل استعلامات متداخلة أو فقد تغيير أثناء القراءة.
        if (notificationsBusy) { notificationsRefreshRequested = true; return; }
        T_SystemUser? user = App.appuser ?? App.login_user;
        bool allowed = MaintenanceNoticeAccess.CanView(user);
        Notifications.Configure(user?.User_ID ?? 0, allowed);
        Notifications.CanManageRequests = allowed;
        NotificationBell.IsEnabled = allowed;
        if (!allowed)
        {
            NotificationFlyout.IsOpen = false;
            foreach (INotification toast in activeToasts.ToArray()) toast.Hide();
            return;
        }
        notificationsBusy = true;
        if (!Notifications.HasSnapshot) Notifications.BeginLoad();
        try
        {
            if (notificationContextFactory is null && string.IsNullOrWhiteSpace(App.ConnectionString)) throw new InvalidOperationException("Connection unavailable.");
            Maintenance_DabaseContext CreateContext() => notificationContextFactory is not null ? notificationContextFactory()
                : new Maintenance_DabaseContext(new DbContextOptionsBuilder<Maintenance_DabaseContext>()
                    .UseSqlServer(App.ConnectionString, sql => sql.CommandTimeout(15)).Options);
            var store = new MaintenanceNotificationStore(CreateContext);
            List<MaintenanceNotice> snapshot = await store.LoadAsync(user, notificationCancellation.Token);
            // لا نعرض نتيجة لمستخدم انتهت جلسته أو تغيرت أثناء انتظار الشبكة.
            T_SystemUser? current = App.appuser ?? App.login_user;
            if (notificationsClosed) return;
            if (current?.User_ID != user!.User_ID || !MaintenanceNoticeAccess.CanView(current))
            {
                Notifications.Configure(current?.User_ID ?? 0, MaintenanceNoticeAccess.CanView(current));
                return;
            }
            List<MaintenanceNotice> added = Notifications.Apply(snapshot);
            // عرض سابق نجح لكن تأكيده تعذر: نعيد التأكيد دون إعادة التنبيه نفسه بعد رجوع الاتصال.
            await ObserveDisplayedRequestsAsync(snapshot.Where(Notifications.WasTransportDelivered));
            // ظهور البطاقات داخل المركز يستلم الطلب المشترك، ولا يقرأه نيابة عن بقية الحسابات.
            if (NotificationFlyout.IsOpen) await ObserveDisplayedRequestsAsync(Notifications.Items);
            if (Notifications.ToastsEnabled && added.Count > 0)
            {
                // كل عملية تعرض بلونها حتى عند وصول ورود وإلغاء وإنجاز في القراءة نفسها.
                foreach (var group in added.GroupBy(x => x.IsRequest ? "request:" + x.DeliveryIdentity : "maintenance:" + x.GroupName))
                    _ = ShowMaintenanceToastAsync(group.ToList());
            }
        }
        catch (OperationCanceledException) when (notificationCancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            // لا رسائل منبثقة متكررة من المؤقت؛ تبقى آخر لقطة مع تحذير واضح بأنها قديمة.
            if (!notificationsClosed) Notifications.Fail();
        }
        finally
        {
            notificationsBusy = false;
            if (notificationsRefreshRequested && !notificationsClosed && !Dispatcher.HasShutdownStarted)
            {
                notificationsRefreshRequested = false;
                _ = Dispatcher.BeginInvoke(new Action(() => _ = RefreshMaintenanceNotificationsAsync()));
            }
        }
    }

    private void NotificationStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        NoticeBadge.Content = Notifications.BadgeText;
        NoticeBadge.Visibility = Notifications.HasUnread ? Visibility.Visible : Visibility.Collapsed;
        UpdateNotificationBadgeBlink();
        NotificationBell.ToolTip = "إشعارات الصيانة: " + Notifications.Unread + " غير مقروءة، " + Notifications.Total + " تحتاج متابعة";
        NoticePanel.IsEnabled = Notifications.CanView;
    }

    private async void RefreshNotificationsRequested(object? sender, EventArgs e)
    {
        await RefreshMaintenanceNotificationsAsync();
    }

    #endregion

    #region وميض عدّاد الإشعارات غير المقروءة

    private void UpdateNotificationBadgeBlink()
    {
        // لا نعيد بدء الوميض عند تغير التصفية أو التحديث ما دام هناك غير مقروء.
        if (!notificationsClosed && Notifications.HasUnread)
        {
            if (!notificationBadgeBlinkTimer.IsEnabled)
            {
                NoticeBadge.Opacity = 1;
                notificationBadgeBlinkTimer.Start();
            }
        }
        else
        {
            // عند قراءة الكل أو سحب الصلاحية أو الإغلاق نوقف المؤقت ونعيد وضوح العدّاد.
            notificationBadgeBlinkTimer.Stop();
            NoticeBadge.Opacity = 1;
        }
    }

    private void NotificationBadgeBlink_Tick(object? sender, EventArgs e)
    {
        if (notificationsClosed || !Notifications.HasUnread)
        {
            UpdateNotificationBadgeBlink();
            return;
        }

        // أعاد المستخدم اعتماد تغيير الشفافية؛ يبقى العدد ثابتًا والشارة متصلة بالجرس أثناء الوميض.
        NoticeBadge.Opacity = NoticeBadge.Opacity >= 1 ? 0.35 : 1;
    }

    #endregion

    #region الجرس ولوحة DevExpress والتنقل إلى تقرير الإنهاء

    private async void NotificationBell_Click(object sender, RoutedEventArgs e)
    {
        NotificationFlyout.IsOpen = !NotificationFlyout.IsOpen;
        if (NotificationFlyout.IsOpen)
        {
            FitNotificationPanel();
            await RefreshMaintenanceNotificationsAsync();
            await ObserveDisplayedRequestsAsync(Notifications.Items);
        }
    }

    private void CloseNotifications(object? sender, EventArgs e)
    {
        NotificationFlyout.IsOpen = false;
        NotificationBell.Focus();
    }

    private void Notifications_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && NotificationFlyout.IsOpen)
        {
            CloseNotifications(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void Notifications_SizeChanged(object sender, SizeChangedEventArgs e) => FitNotificationPanel();

    private void FitNotificationPanel()
    {
        NoticePanel.Width = Math.Min(780, Math.Max(600, ActualWidth - 80));
        NoticePanel.Height = Math.Min(900, Math.Max(490, ActualHeight - 110));
    }

    private void OpenNotificationOrder(object? sender, MaintenanceNotice row)
    {
        if (!MaintenanceNoticeAccess.CanView(App.appuser ?? App.login_user)) return;
        if (!row.CanOpen) return;
        NotificationFlyout.IsOpen = false;
        Notifications.MarkRead(row);
        if (row.CanAssignRequest)
        {
            if (!inout_Menu.IsEnabled) { UiMessages.Notify(this, "إصدار التكليف يحتاج صلاحية تكليف الصيانة."); return; }
            if (maintenanceOrderWindow is { IsVisible: true }) { maintenanceOrderWindow.Activate(); return; }
            try
            {
                maintenanceOrderWindow = new maintenance_Order(notificationContextFactory) { Owner = this, RequestId = row.RequestId };
                maintenanceOrderWindow.ShowDialog();
            }
            catch (Exception ex) { UiMessages.Notify(this, ex.GetBaseException().Message); }
            finally { maintenanceOrderWindow = null; _ = RefreshMaintenanceNotificationsAsync(); }
            return;
        }
        // لا نثق بحالة نسخة الإشعار القديمة؛ الشاشة تحمل التكليف وتقرير الإنهاء الحاليين من القاعدة.
        ShowMaintenanceCompletion(row.Key);
    }

    // الحذف اليدوي لإشعار إلغاء العميل فقط؛ الإنجاز يختفي تلقائيًا بعد انتهاء تنبيهه.
    private async void ArchiveNotificationRequest(object? sender, MaintenanceNotice row)
    {
        if (!row.CanArchive || !Notifications.CanManageRequests) return;
        if (Maintenance_app.Dashboard.UiMessages.Show(this, "طلبات الصيانة", "هل تريد حذف إشعار إلغاء العميل من قائمة إشعارات المنظومة؟", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RemoveCancellationNoticeAsync(row);
    }

    private async void CancellationDoubleClicked(object? sender, MaintenanceNotice row) =>
        await RemoveCancellationNoticeAsync(row);

    private async Task RemoveCancellationNoticeAsync(MaintenanceNotice row)
    {
        // الزر والبطاقة والبالون تستخدم قناة الحذف نفسها، وتُمنع المحاولات المتزامنة للطلب نفسه.
        var user = App.appuser ?? App.login_user;
        if (!row.CanArchive || row.RequestId is not long id || !MaintenanceNoticeAccess.CanView(user)
            || !cancellationRemovals.Add(id)) return;
        try
        {
            await new MaintenanceRequestInboxStore(CreateNotificationContext).ArchiveAsync(id, user);
            await RefreshMaintenanceNotificationsAsync();
        }
        catch (Exception ex) { UiMessages.Notify(this, ex.GetBaseException().Message); }
        finally { cancellationRemovals.Remove(id); }
    }
    private Maintenance_DabaseContext CreateNotificationContext() => notificationContextFactory is not null ? notificationContextFactory()
        : new(new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlServer(App.ConnectionString, sql => sql.CommandTimeout(15)).Options);

    private async Task<bool> ObserveDisplayedRequestsAsync(IEnumerable<MaintenanceNotice> rows)
    {
        var displayed = rows.Where(x => x.RequestDeliveryState == 0 && x.IsRequest && x.RequestOperation is 1 or 4).ToList();
        if (displayed.Count == 0 || notificationsClosed) return !notificationsClosed;
        try
        {
            await new MaintenanceRequestInboxStore(CreateNotificationContext).ObserveAsync(displayed, notificationCancellation.Token);
            foreach (var row in displayed) row.RequestDeliveryState = 1;
            return true;
        }
        catch (OperationCanceledException) when (notificationsClosed) { return false; }
        catch (Exception) { return false; /* لا نسجل التسليم محليًا عند فشل تأكيده في القاعدة. */ }
    }

    #endregion

    #region التنبيهات المنبثقة المخصصة من DevExpress

    public sealed class MaintenanceToast
    {
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string ActionHint { get; set; } = "اضغط لعرض الإشعارات";
        // نفس هوية مجموعة الإشعار داخل المركز، مع لون عام عند تجميع حالات مختلفة.
        public string AccentColor { get; set; } = "#3559A0";
        public string TintColor { get; set; } = "#EDF2FA";
    }

    private async Task ShowMaintenanceToastAsync(List<MaintenanceNotice> added)
    {
        if (notificationService == null || notificationsClosed) return;
        MaintenanceNotice row = added[0];
        bool sameGroup = added.All(item => item.GroupName == row.GroupName);
        INotification? toast = null;
        try
        {
            toast = notificationService.CreateCustomNotification(new MaintenanceToast
            {
                ActionHint = added.Count == 1 && row.CanArchive ? "اضغط لحذف إشعار الإلغاء" : "اضغط لعرض الإشعارات",
                Title = added.Count == 1 ? row.ToastTitle : sameGroup ? added.Count + " إشعارات — " + (row.IsRequest ? row.ToastTitle : row.GroupName) : added.Count + " إشعارات صيانة تحتاج متابعة",
                Text = added.Count == 1 ? row.ToastText : row.IsRequest ? "افتح مركز الإشعارات لمراجعة طلبات الصيانة وحالتها الحالية." : "افتح مركز الإشعارات لمراجعة التكليفات وتقارير الإنهاء المعلّقة وقيد الإنجاز.",
                AccentColor = sameGroup ? row.ToastAccentColor : "#3559A0",
                TintColor = sameGroup ? row.ToastTintColor : "#EDF2FA"
            });
            activeToasts.Add(toast);
            var showing = toast.ShowAsync();
            if (showing.IsFaulted || showing.IsCanceled) return;
            notificationSound.TryPlay(Notifications.SoundEnabled);
            Notifications.MarkTransportDelivered(added);
            Notifications.MarkTransientDelivered(added);
            await ObserveDisplayedRequestsAsync(added);
            NotificationResult result = await showing;
            Notifications.HideCompletedRequests(added);
            if (result == NotificationResult.Activated && !notificationsClosed
                && MaintenanceNoticeAccess.CanView(App.appuser ?? App.login_user))
            {
                if (added.Count == 1 && row.CanArchive)
                {
                    await RemoveCancellationNoticeAsync(row);
                    return;
                }
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Maximized;
                Activate();
                if (added.Count == 1 && !row.IsTransient)
                {
                    OpenNotificationOrder(this, row);
                    return;
                }
                FitNotificationPanel();
                NotificationFlyout.IsOpen = true;
                Notifications.Selected = Notifications.Items.FirstOrDefault(item => item.Identity == row.Identity);
            }
        }
        catch (Exception)
        {
            // تعذر التنبيه العابر لا يعطل قائمة العمل أو دورة التحديث.
        }
        finally
        {
            if (toast != null) activeToasts.Remove(toast);
        }
    }

    #endregion
}
