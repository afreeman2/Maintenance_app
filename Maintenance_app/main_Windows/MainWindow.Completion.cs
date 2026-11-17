using Maintenance_app.Dashboard;
using Maintenance_app.orders_Windows;
using System.Windows;
using System.Windows.Input;

namespace Maintenance_app;

public partial class MainWindow
{
    #region فتح تقرير إنهاء الصيانة والصلاحيات والاختصار
    public static readonly RoutedUICommand OpenMaintenanceCompletionCommand = new("تقرير إنهاء الصيانة",
        nameof(OpenMaintenanceCompletionCommand), typeof(MainWindow), new InputGestureCollection { new KeyGesture(Key.F5) });
    private maintenance_complete? completionWindow;

    // توحيد صلاحية القائمة والوصول السريع والاختصار، ومنع فتح نسخة ثانية من الشاشة.
    private void InitializeMaintenanceCompletion()
    {
        InputBindings.Add(new KeyBinding(OpenMaintenanceCompletionCommand, Key.F5, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(OpenMaintenanceCompletionCommand, CompletionMenu_Click,
            (_, args) => { args.CanExecute = CompletionMenu.IsEnabled && WorkspaceUser?.fordon == true; args.Handled = true; }));
    }
    private void CompletionMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!CompletionMenu.IsEnabled || WorkspaceUser?.fordon != true) return;
        ShowMaintenanceCompletion();
    }

    // مسار موحد للقائمة والإشعار؛ المفتاح الكامل يحدد التكليف والتقرير الأحدث فعليًا.
    private void ShowMaintenanceCompletion(MaintenanceOrderKey? key = null)
    {
        if (completionWindow is { IsLoaded: true }) { completionWindow.Activate(); return; }
        try { completionWindow = new maintenance_complete(notificationContextFactory) { Owner = this, InitialOrderKey = key }; completionWindow.ShowDialog(); }
        catch (Exception ex) { UiMessages.Notify(this, "تعذّر فتح تقرير إنهاء الصيانة.\n" + ex.GetBaseException().Message, System.Windows.Media.Brushes.Red); }
        finally { completionWindow = null; _ = RefreshMaintenanceNotificationsAsync(); }
    }
    #endregion
}
