using Maintenance_app.Dashboard;
using Maintenance_app.dbContext;
using Maintenance_app.Notifications;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Threading;

namespace Maintenance_app;

public partial class MainWindow
{
    #region دورة تحديث إحصاءات الصيانة وإيقافها
    private readonly DispatcherTimer statisticsTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool statisticsRefreshRequested;
    private void InitializeMaintenanceStatistics()
    {
        Resources["Main.QuickTitleFont"] = 14d;
        Resources["Main.QuickButtonHeight"] = 64d;
        statisticsTimer.Tick += StatisticsTimer_Tick;
        WorkspaceCanvas.SizeChanged += StatisticsLayout_SizeChanged;
        MaintenanceNotificationUpdates.Changed += StatisticsData_Changed;
        Loaded += (_, _) => statisticsTimer.Start();
        Closed += (_, _) =>
        {
            statisticsTimer.Stop();
            statisticsTimer.Tick -= StatisticsTimer_Tick;
            WorkspaceCanvas.SizeChanged -= StatisticsLayout_SizeChanged;
            MaintenanceNotificationUpdates.Changed -= StatisticsData_Changed;
        };
    }
    private async void StatisticsTimer_Tick(object? sender, EventArgs e) => await RefreshStatisticsAsync();
    // يتكيّف حجم الزر مع مساحة العمل، وتحتسب البطاقة ارتفاعها من جميع صفوف الأزرار.
    private void StatisticsLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Height < 470;
        // لا نفرض ارتفاعًا على الحاوية كي تبقى الصفوف الإضافية داخل حدودها.
        Resources["Main.QuickButtonHeight"] = compact ? 48d : 64d;
        Resources["Main.QuickTitleFont"] = compact ? 12d : 14d;
        // نحافظ على قراءة عنوان الخلفية عند قصر النافذة بدل ضغط الرسم وجميع نصوصه.
        WorkspaceHeroContent.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceHeroCompact.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        // عند الارتفاع المتوسط نستخدم عنوانًا بحجمه الطبيعي مع الشعار، وتبقى الرسمة في العمود المقابل.
        bool medium = e.NewSize.Height < 620;
        WorkspaceHeroFull.Visibility = medium ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceHeroMedium.Visibility = medium ? Visibility.Visible : Visibility.Collapsed;
    }
    // كل حفظ تكليف أو إنهاء معتمد يحدث البطاقات فورًا، وتلتقط الدورة تغييرات الأجهزة الأخرى.
    private void StatisticsData_Changed(object? sender, EventArgs e)
    {
        if (!IsLoaded || statisticsCancellation.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() => _ = RefreshStatisticsAsync()));
    }
    #endregion

    #region تحميل المجاميع السنوية من القاعدة دون تغيير بيانات العمل
    private async Task RefreshStatisticsAsync()
    {
        if (statisticsCancellation.IsCancellationRequested) return;
        if (statisticsLoading) { statisticsRefreshRequested = true; return; }
        bool allowed = MaintenanceNoticeAccess.CanView(App.appuser ?? App.login_user);
        Statistics.ConfigureAccess(allowed);
        if (!allowed) return;
        statisticsLoading = true; Statistics.BeginLoad();
        int userId = (App.appuser ?? App.login_user)!.User_ID;
        try
        {
            if (notificationContextFactory is null && string.IsNullOrWhiteSpace(App.ConnectionString))
                throw new InvalidOperationException("إعداد الاتصال غير متاح.");
            Maintenance_DabaseContext Create() => notificationContextFactory is not null ? notificationContextFactory()
                : new Maintenance_DabaseContext(new DbContextOptionsBuilder<Maintenance_DabaseContext>()
                    .UseSqlServer(App.ConnectionString, sql => sql.CommandTimeout(15)).Options);
            // تمرير رمز الإلغاء بالاسم لأن LoadAsync يدعم الآن مرشح شاشة الإحصائية كمعامل أول اختياري.
            var result = await new AnnualStatisticsRepository(Create).LoadAsync(cancellationToken: statisticsCancellation.Token);
            if (statisticsCancellation.IsCancellationRequested) return;
            var current = App.appuser ?? App.login_user;
            if (current?.User_ID != userId) { Statistics.ConfigureAccess(false); return; }
            Statistics.Apply(result);
        }
        catch (OperationCanceledException) when (statisticsCancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!statisticsCancellation.IsCancellationRequested) Statistics.Fail("تعذّر تحديث إحصاءات الصيانة. تحقق من الاتصال ثم أعد التحديث.");
        }
        finally
        {
            statisticsLoading = false;
            if (statisticsRefreshRequested && !statisticsCancellation.IsCancellationRequested && !Dispatcher.HasShutdownStarted)
            {
                statisticsRefreshRequested = false;
                _ = Dispatcher.BeginInvoke(new Action(() => _ = RefreshStatisticsAsync()));
            }
        }
    }
    #endregion
}
