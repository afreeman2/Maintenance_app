using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using Maintenance_app.Dashboard;
using Maintenance_app.dbContext;
using Maintenance_app.Models;

namespace Maintenance_app.Accounts;

public partial class ChangePasswordWindow : MetroWindow
{
    #region تهيئة حساب الجلسة
    private readonly T_SystemUser account;
    private readonly AccountStore store;
    private bool saving;

    internal ChangePasswordWindow(T_SystemUser account, Func<Maintenance_DabaseContext>? contextFactory)
    {
        this.account = account;
        store = new AccountStore(contextFactory);
        InitializeComponent();
        Loaded += (_, _) => CurrentPassword.Focus();
    }
    #endregion

    #region التحقق من كلمة المرور وحفظها
    // يقرأ الحفظ كلمة المرور الحالية من القاعدة ويحدّث الحساب المفتوح فقط.
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        var current = CurrentPassword.Password;
        var replacement = NewPassword.Password;
        if (current.Length == 0) { AccountFieldValidation.Show(CurrentPassword, "أدخل كلمة المرور الحالية."); return; }
        if (string.IsNullOrWhiteSpace(replacement)) { AccountFieldValidation.Show(NewPassword, "أدخل كلمة المرور الجديدة."); return; }
        if (replacement != ConfirmPassword.Password) { AccountFieldValidation.Show(ConfirmPassword, "تأكيد كلمة المرور غير مطابق."); return; }

        saving = true;
        IsEnabled = false;
        try
        {
            if (!await store.ChangePasswordAsync(account.User_ID, current, replacement))
            {
                IsEnabled = true;
                AccountFieldValidation.Show(CurrentPassword, "كلمة المرور الحالية غير صحيحة.");
                return;
            }
            account.PassWord = AccountStore.Encode(replacement);
            if (App.login_user?.User_ID == account.User_ID) App.login_user.PassWord = account.PassWord;
            UiMessages.Show(this, Title, "تم تغيير كلمة المرور.", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception)
        {
            UiMessages.Show(this, Title, "تعذّر تغيير كلمة المرور. تحقق من الاتصال وأعد المحاولة.", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { saving = false; IsEnabled = true; }
    }

    // إزالة التحقق السابق فور الكتابة وإظهار خلفية أغمق عند وجود كلمة مرور.
    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        var field = (PasswordBox)sender;
        AccountFieldValidation.Clear(field);
        field.Background = string.IsNullOrWhiteSpace(field.Password) ? System.Windows.Media.Brushes.White
            : (System.Windows.Media.Brush)FindResource("Account.InputFilled");
    }

    private void Close_Click(object sender, RoutedEventArgs e) { if (!saving) Close(); }
    #endregion
}
