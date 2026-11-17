using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using MahApps.Metro.Controls;
using Maintenance_app.Dashboard;
using Maintenance_app.dbContext;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Maintenance_app.Accounts
{
    /// <summary>
    /// Interaction logic for Password_Change.xaml
    /// </summary>
    public partial class Password_Change : MetroWindow
    {
        private readonly int _userId;
        private readonly DbContextOptions<Maintenance_DabaseContext> _options;
        private bool _isSaving;

        public Password_Change()
        {
            _userId = App.login_user?.User_ID ?? 0;
            _options = new DbContextOptionsBuilder<Maintenance_DabaseContext>()
                .UseSqlServer(App.ConnectionString)
                .Options;
            InitializeComponent();
            txtCurrentUser.Text = App.login_user?.User_Name ?? string.Empty;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_userId <= 0 || App.login_user?.User_ID != _userId)
            {
                ApplicationThemeHelper.ApplicationThemeName = "Win11Light";

                UiMessages.Show(this, Title, "يرجى تسجيل الدخول أولًا.", MessageBoxButton.OK, MessageBoxImage.Information);
                ApplicationThemeHelper.ApplicationThemeName = "None";


                Close();
                return;
            }
            txtCurrentPassword.Focus();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_isSaving)
                return;
            if (_userId <= 0 || App.login_user?.User_ID != _userId)
            {
                ShowError("انتهت جلسة المستخدم. يرجى تسجيل الدخول مجددًا.");
                return;
            }

            string currentPassword = txtCurrentPassword.Password ?? string.Empty;
            string newPassword = txtNewPassword.Password ?? string.Empty;
            string confirmation = txtConfirmPassword.Password ?? string.Empty;

            if (string.IsNullOrEmpty(currentPassword))
            {
                ShowError("أدخل كلمة المرور الحالية.", txtCurrentPassword);
                return;
            }
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                ShowError("أدخل كلمة المرور الجديدة.", txtNewPassword);
                return;
            }
            if (newPassword.Length < 3)
            {
                ShowError("كلمة المرور الجديدة يجب أن تكون 3 خانات على الأقل.", txtNewPassword);
                return;
            }
            if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal))
            {
                ShowError("تأكيد كلمة المرور لا يطابق كلمة المرور الجديدة.", txtConfirmPassword);
                return;
            }
            if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            {
                ShowError("اختر كلمة مرور جديدة مختلفة عن الحالية.", txtNewPassword);
                return;
            }

            // Preserve the application's existing password format and column limit.
            string encodedCurrentPassword = App.encrypt(currentPassword);
            string encodedNewPassword = App.encrypt(newPassword);
            if (encodedNewPassword.Length > 12)
            {
                ShowError("كلمة المرور الجديدة تتجاوز الحد المسموح، يرجى تقصيرها.", txtNewPassword);
                return;
            }

            string? error = null;
            PasswordBoxEdit? errorField = null;
            SetSavingState(true);
            try
            {
                await using var context = new Maintenance_DabaseContext(_options);
                await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                // Check the current password and update only this user's Password atomically.
                // Binary comparison is required even if the database uses a case-insensitive collation.
                var currentUser = await context.T_SystemUsers
                    .Where(user => user.User_ID == _userId && user.st == true
                        && EF.Functions.Collate(user.PassWord, "Latin1_General_100_BIN2") == encodedCurrentPassword)
                    .SingleOrDefaultAsync();

                if (currentUser == null)
                {
                    error = "كلمة المرور الحالية غير صحيحة، أو لم يعد الحساب متاحًا.";
                    errorField = txtCurrentPassword;
                }
                else
                {
                    currentUser.PassWord = encodedNewPassword;
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    if (App.login_user?.User_ID == _userId)
                        App.login_user.PassWord = encodedNewPassword;
                }
            }
            catch (Exception)
            {
                error = "تعذر حفظ كلمة المرور. تحقق من الاتصال ثم حاول مرة أخرى.";
            }
            finally
            {
                SetSavingState(false);
            }

            if (error != null)
            {
                ShowError(error, errorField);
                return;
            }

            ClearPasswordFields();
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";

            UiMessages.Show(this, Title, "تم تغيير كلمة المرور.", MessageBoxButton.OK, MessageBoxImage.Information);
            ApplicationThemeHelper.ApplicationThemeName = "None";

            DialogResult = true;
        }

        private void SetSavingState(bool isSaving)
        {
            _isSaving = isSaving;
            formPanel.IsEnabled = !isSaving;
            btnSave.IsEnabled = !isSaving;

            btnClose.IsEnabled = !isSaving;
            txtSaveLabel.Text = isSaving ? "جارٍ الحفظ..." : "حفظ كلمة المرور";
        }

        private void ShowError(string message, PasswordBoxEdit? field = null)
        {
            txtError.Text = message;
            errorPanel.Visibility = Visibility.Visible;
            field?.Focus();
        }

        private void PasswordField_EditValueChanged(object sender, EditValueChangedEventArgs e)
        {
            if (errorPanel != null)
                errorPanel.Visibility = Visibility.Collapsed;
        }

        private void ClearPasswordFields()
        {
            txtCurrentPassword.Clear();
            txtNewPassword.Clear();
            txtConfirmPassword.Clear();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSaving)
                Close();
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            e.Cancel = _isSaving;
        }

        protected override void OnClosed(EventArgs e)
        {
            ClearPasswordFields();
            base.OnClosed(e);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isSaving && e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }
    }

}
