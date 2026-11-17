using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Maintenance_app.Notifications;

public partial class NotificationCenter : System.Windows.Controls.UserControl
{
    #region أحداث المركز المرتبطة بالرئيسية

    public event EventHandler? CloseRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler<MaintenanceNotice>? OpenRequested;
    public event EventHandler<MaintenanceNotice>? ArchiveRequested;
    public event EventHandler<MaintenanceNotice>? CancellationDoubleClicked;
    private MaintenanceNotificationState? State => DataContext as MaintenanceNotificationState;

    public NotificationCenter()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
    private void ReadAll_Click(object sender, RoutedEventArgs e) => State?.MarkAllRead();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (State?.Selected is MaintenanceNotice row) OpenRequested?.Invoke(this, row);
    }
    // زر الحذف يطلب التأكيد، والنقر المزدوج على بطاقة الإلغاء ينفذ الحذف مباشرة.
    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        if (State?.Selected is { CanArchive: true } row) ArchiveRequested?.Invoke(this, row);
    }

    private void Notice_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // عدد النقرات متاح عند ضغط الزر؛ النقرة الأولى تختار بطاقة الإلغاء فقط.
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2
            || sender is not ListBoxItem { DataContext: MaintenanceNotice { CanArchive: true } row }) return;
        e.Handled = true;
        CancellationDoubleClicked?.Invoke(this, row);
    }

    private void Notice_Click(object sender, MouseButtonEventArgs e)
    {
        // نقرة واحدة على بطاقة فعلية؛ الفراغ ورؤوس المجموعات لا تفتح السجل السابق.
        if (e.ChangedButton != MouseButton.Left || sender is not ListBoxItem { DataContext: MaintenanceNotice row }) return;
        e.Handled = true;
        if (State is not null) State.Selected = row;
        if (row.CanArchive) return;
        OpenRequested?.Invoke(this, row);
    }

    private void Notice_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true; Open_Click(sender, e);
    }

    #endregion
}
