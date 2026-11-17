using Maintenance_app.Models;
using System.Windows.Input;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Maintenance_app;

public partial class MainWindow
{
    #region صلاحيات القوائم والوصول السريع
    private static T_SystemUser? WorkspaceUser =>
        (App.appuser ?? App.login_user) is { st: not false } user ? user : null;

    // تعتمد القوائم وبطاقات الوصول السريع والأوامر على الصلاحيات الفعلية للحساب المسجّل.
    private void ApplyWorkspacePermissions()
    {
        var user = WorkspaceUser;
        inout_Menu.IsEnabled = user?.formaint == true;
        CompletionMenu.IsEnabled = user?.fordon == true;
        SupportBuildingsMenu.IsEnabled = user?.forbuild == true;
        SupportServicesMenu.IsEnabled = user?.forservices == true;
        SupportEmployeesMenu.IsEnabled = user?.foremp == true;
        SupportManagementsMenu.IsEnabled = SupportSectionsMenu.IsEnabled = user?.mang_check == true;
        report_mnue.IsEnabled = statistics_mnue.IsEnabled = user?.formaintrepo == true;
        BranchReportsMenu.IsEnabled = user?.branch_report == true;
        opt.IsEnabled = user?.foruser == true;
        // نماذج الفحص اليومية وكلمة مرور الحساب متاحة للمستخدم المسجّل وفق السلوك المعتمد.
        DailyInspectionMenu.IsEnabled = DailyInspectionSERVICEMenu.IsEnabled = cha.IsEnabled = user is not null;

        if (!report_mnue.IsEnabled) maintenanceReportsWindow?.Close();
        if (!statistics_mnue.IsEnabled) maintenanceStatisticsWindow?.Close();
        if (!BranchReportsMenu.IsEnabled) branchReportsWindow?.Close();
        CommandManager.InvalidateRequerySuggested();
    }

    // تنفّذ بطاقة الوصول السريع أمر القائمة نفسه، بما فيه أوامر إعدادات الحساب الموجّهة.
    private void ExecuteWorkspaceTarget(MenuItem target)
    {
        if (!target.IsEnabled) return;
        if (target.Command is RoutedCommand command)
        {
            if (command.CanExecute(target.CommandParameter, this))
                command.Execute(target.CommandParameter, this);
            return;
        }
        target.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent, target));
    }
    #endregion

    #region اختصارات الرئيسية
    // تُسجّل المفاتيح على النافذة وتستخدم حالة تمكين بند القائمة نفسه عند التنفيذ.
    private void InitializeWorkspaceShortcuts()
    {
        InputBindings.Add(new KeyBinding(OpenMaintenanceOrderCommand, Key.F7, ModifierKeys.None));
        BindWorkspaceShortcut(SupportBuildingsMenu, Key.D1, ModifierKeys.Control, Key.NumPad1);
        BindWorkspaceShortcut(SupportServicesMenu, Key.D2, ModifierKeys.Control, Key.NumPad2);
        BindWorkspaceShortcut(SupportEmployeesMenu, Key.D3, ModifierKeys.Control, Key.NumPad3);
        BindWorkspaceShortcut(SupportManagementsMenu, Key.D4, ModifierKeys.Control, Key.NumPad4);
        BindWorkspaceShortcut(SupportSectionsMenu, Key.D5, ModifierKeys.Control, Key.NumPad5);
        BindWorkspaceShortcut(report_mnue, Key.C, ModifierKeys.Control);
        BindWorkspaceShortcut(BranchReportsMenu, Key.T, ModifierKeys.Alt);
        BindWorkspaceShortcut(statistics_mnue, Key.I, ModifierKeys.Control);
        BindWorkspaceShortcut(DailyInspectionMenu, Key.Y, ModifierKeys.Alt);
        BindWorkspaceShortcut(DailyInspectionSERVICEMenu, Key.X, ModifierKeys.Alt);
    }

    private void BindWorkspaceShortcut(MenuItem target, Key key, ModifierKeys modifiers, Key? alternateKey = null)
    {
        var command = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(command,
            (_, args) => { ExecuteWorkspaceTarget(target); args.Handled = true; },
            (_, args) => { args.CanExecute = target.IsEnabled; args.Handled = true; }));
        InputBindings.Add(new KeyBinding(command, key, modifiers));
        if (alternateKey is Key alternate)
            InputBindings.Add(new KeyBinding(command, alternate, modifiers));
    }
    #endregion
}
