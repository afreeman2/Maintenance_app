using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Maintenance_app.Accounts;
using Maintenance_app.Dashboard;

namespace Maintenance_app;

public partial class MainWindow
{
    #region أوامر الإعدادات واختصاراتها
    public static readonly RoutedUICommand OpenUsersCommand = new("المستخدمون والصلاحيات", nameof(OpenUsersCommand), typeof(MainWindow));
    public static readonly RoutedUICommand ChangePasswordCommand = new("تغيير كلمة المرور", nameof(ChangePasswordCommand), typeof(MainWindow));
    private bool openingAccountWindow;

    // تصل أزرار الإعدادات وF10 وF11 إلى أوامر الرئيسية نفسها عبر WPF.
    private void InitializeAccountCommands()
    {
        CommandBindings.Add(new CommandBinding(OpenUsersCommand, OpenUsers_Executed,
            (_, args) => { args.CanExecute = WorkspaceUser?.foruser == true; args.Handled = true; }));
        CommandBindings.Add(new CommandBinding(ChangePasswordCommand, ChangePassword_Executed,
            (_, args) => { args.CanExecute = WorkspaceUser is not null; args.Handled = true; }));
        InputBindings.Add(new KeyBinding(OpenUsersCommand, Key.F10, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(ChangePasswordCommand, Key.F11, ModifierKeys.None));
    }
    #endregion

    #region فتح واجهات الحساب بعد انتهاء حدث الإدخال
    private void OpenUsers_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (WorkspaceUser?.foruser != true)
        {
            UiMessages.Show(this, OpenUsersCommand.Text, "يتطلب فتح إدارة المستخدمين صلاحية إدارة المستخدمين.", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        OpenAccountWindow(OpenUsersCommand.Text, () => new UsersWindow(), () =>
        {
            // قد يغيّر مدير الحسابات اسمه أو صلاحية إنهاء الصيانة أثناء وجوده في النافذة.
            lus.Text = App.usname;

            RefreshWorkspace();
        });
    }

    private void ChangePassword_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (WorkspaceUser is null)
        {
            UiMessages.Show(this, ChangePasswordCommand.Text, "سجّل الدخول أولًا لتغيير كلمة المرور.", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        OpenAccountWindow(ChangePasswordCommand.Text, () => new Password_Change());
    }

    // ننهي مسار النقرة أو الاختصار قبل بدء الحوار، ونمنع تكرار الفتح مع النقر السريع.
    private void OpenAccountWindow(string title, Func<Window> createWindow, Action? onClosed = null)
    {
        if (openingAccountWindow) return;
        openingAccountWindow = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try
            {
                if (!IsVisible) return;
                var window = createWindow();
                window.Owner = this;
                window.ShowDialog();
                onClosed?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("تعذّر فتح واجهة الحساب: {0} ({1})", title, ex.GetType().Name);
                UiMessages.Show(this, title, "تعذّر فتح النافذة. أعد المحاولة من الإعدادات.", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { openingAccountWindow = false; }
        }));
    }
    #endregion
}
