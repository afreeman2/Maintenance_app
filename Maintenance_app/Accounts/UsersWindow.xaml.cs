using DevExpress.Data.Filtering;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors.Native;
using DevExpress.Xpf.Grid;
using MahApps.Metro.Controls;
using Maintenance_app.converts;
using Maintenance_app.Dashboard;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.viewo_Model;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Maintenance_app.Accounts;


public partial class UsersWindow : MetroWindow
{
    #region All Variables
    private bool _isPasting;
    private Users_view_model ViewModel => (Users_view_model)DataContext;
    private bool _isLoadingPermissionUser;
    private bool _isClosed;
    private T_SystemUser? _activePermissionUser;
    private bool _isNewUser = false;
    private bool _isSynchronizingPermissionChecks;

    #endregion

    #region All Functions
    private void View_SearchStringToFilterCriteria(object sender, SearchStringToFilterCriteriaEventArgs e)
    { // لا تبحث أثناء عملية اللصق

        if (_isPasting)
        {
            e.Filter = null;
            e.Highlighting = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(e.SearchString))
        {
            e.Filter = null;
            e.Highlighting = null;
            return;
        }

        string search = e.SearchString.Trim();

        var variants = GetArabicSearchVariants(search);

        var filters = new List<CriteriaOperator>();

        foreach (string variant in variants)
        {
            filters.Add(
                new FunctionOperator(
                    FunctionOperatorType.StartsWith,
                    new OperandProperty("User_Name"),
                    new OperandValue(variant)
                )
            );
        }

        e.Filter = filters.Count == 1
            ? filters[0]
            : new GroupOperator(
                GroupOperatorType.Or,
                filters
            );

        // التضليل بالنص الذي كتبه المستخدم
        e.Highlighting = new List<FieldAndHighlightingString>
    {
        new FieldAndHighlightingString("User_Name", search)
    };
    }
    private List<string> GetArabicSearchVariants(string text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(text))
            return result.ToList();

        text = text.Trim();

        // النص الأصلي
        result.Add(text);

        // إذا كان الحرف الأول من مجموعة أحرف متشابهة
        char first = text[0];
        string rest = text.Substring(1);

        switch (first)
        {
            // الألف
            case 'ا':
            case 'أ':
            case 'إ':
            case 'آ':
                result.Add("ا" + rest);
                result.Add("أ" + rest);
                result.Add("إ" + rest);
                result.Add("آ" + rest);
                break;

            // الياء
            case 'ي':
            case 'ى':
            case 'ئ':
                result.Add("ي" + rest);
                result.Add("ى" + rest);
                result.Add("ئ" + rest);
                break;

            // الواو
            case 'و':
            case 'ؤ':
                result.Add("و" + rest);
                result.Add("ؤ" + rest);
                break;

            // الهاء والتاء المربوطة
            case 'ه':
            case 'ة':
                result.Add("ه" + rest);
                result.Add("ة" + rest);
                break;

            default:
                result.Add(text);
                break;
        }

        return result.ToList();
    }
    private string NormalizeArabic(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text
            .Replace("أ", "ا")
            .Replace("إ", "ا")
            .Replace("آ", "ا")
            .Replace("ٱ", "ا")

            .Replace("ى", "ي")
            .Replace("ئ", "ي")

            .Replace("ؤ", "و")

            .Replace("ة", "ه")

            .Replace("ـ", "")

            .Replace("َ", "")
            .Replace("ُ", "")
            .Replace("ِ", "")
            .Replace("ً", "")
            .Replace("ٌ", "")
            .Replace("ٍ", "")
            .Replace("ْ", "")
            .Replace("ّ", "");
    }

    private async Task RefreshUsersAsync()
    {
        try
        {
            await ViewModel.RefreshAsync();
        }
        catch (OperationCanceledException) when (_isClosed)
        {
            // Closing the window cancels its outstanding count query.
        }
        catch (Exception ex)
        {
            if (!_isClosed)
                MessageBox.Show("تعذر تحديث المستخدمين:\n" + ex.Message,
                    "إدارة المستخدمين", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateSelectAllPermissionsState(T_SystemUser user)
    {
        var permissions = GetPermissionValues(user);
        var enabledCount = permissions.Count(enabled => enabled);
        var previousSyncState = _isSynchronizingPermissionChecks;
        _isSynchronizingPermissionChecks = true;
        try
        {
            chkAllPermissions.IsChecked = enabledCount == permissions.Length
                ? true : enabledCount == 0? false: null;
        }
        catch(Exception ex)
        { MessageBox.Show(ex.Message); }
        finally
        {
            _isSynchronizingPermissionChecks = previousSyncState;
        }
    }

    private static bool[] GetPermissionValues(T_SystemUser user)
    {
        return new[]
        {
                user.forbuild,
                user.foremp,
                user.forlocation,
                user.formaint,
                user.fordon,
                user.formaintrepo,
                user.formangreq,
                user.foruser,
                user.forservices,
                user.mang_check,
                user.branch_report
            };
    }

    private static void SetAllPermissions(T_SystemUser user, bool enabled)
    {
        user.forbuild = enabled;
        user.foremp = enabled;
        user.forlocation = enabled;
        user.formaint = enabled;
        user.fordon = enabled;
        user.formaintrepo = enabled;
        user.formangreq = enabled;
        user.foruser = enabled;
        user.forservices = enabled;
        user.mang_check = enabled;
        user.branch_report = enabled;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is System.Windows.Controls.Button)
                return true;

            element = element is Visual || element is Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }


    #endregion


    #region Windows Behavior

    public UsersWindow()
    {
        InitializeComponent();
    }

    private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshUsersAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        gridUsers.ClearValue(DevExpress.Xpf.Grid.DataControlBase.ItemsSourceProperty);
        ViewModel.Dispose();
        base.OnClosed(e);
    }

    #endregion

    #region مزامنة صلاحيات المستخدم الحالي
    // بعد نجاح الحفظ تعتمد الرئيسية السجل المحدث عند إغلاق شاشة إدارة المستخدمين.
    private static void UpdateSignedInUser(T_SystemUser savedUser)
    {
        if ((App.appuser ?? App.login_user)?.User_ID != savedUser.User_ID) return;
        App.appuser = App.login_user = savedUser;
        App.usname = savedUser.User_Name;
    }
    #endregion

    #region Main Buttons

    #region Buttons Delete User
    private async void btnDeleteUser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.CommandParameter is not int userId)
        {
            return;
        }




        #region رسالة معلومة الادخال
        ApplicationThemeHelper.ApplicationThemeName = "Win11Light";

        var result = ThemedMessageBox.Show(
            null,
            title: "منظومة الخدمات والصيانة",
            text: "هل أنت متأكد من حذف المستخدم؟",
            messageBoxButtons: MessageBoxButton.YesNo,
            defaultButton: MessageBoxResult.No,
            icon: MessageBoxImage.Question,
            true);

        ApplicationThemeHelper.ApplicationThemeName = "None";
        if (result == MessageBoxResult.No)
            return;
        #endregion

        try
        {
            await using var db = ViewModel.CreateContext();

            var existingUser = await db.T_SystemUsers
                .FirstOrDefaultAsync(u => u.User_ID == userId);

            if (existingUser == null)
            {
                await RefreshUsersAsync();
                return;
            }

            //db.T_SystemUsers.Remove(existingUser);
            existingUser.st = false;
            await db.SaveChangesAsync();
            UpdateSignedInUser(existingUser);

            if (_activePermissionUser?.User_ID == userId)
                ClosePermissionFlyout_Click(sender, e);
            await RefreshUsersAsync();

            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";

            ThemedMessageBox.Show(
              null,
              title: "منظومة الخدمات والصيانة",
              text: "تم حذف المستخدم",
              messageBoxButtons: MessageBoxButton.OK,
              defaultButton: MessageBoxResult.OK,
              icon: MessageBoxImage.Information,
              true);

            ApplicationThemeHelper.ApplicationThemeName = "None";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "تعذر حذف المستخدم:\n" + ex.Message,
                "إدارة المستخدمين",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    #endregion

    #region Close Button
    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    #endregion

    #region CheckBoxex Check All
    private void PermissionCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizingPermissionChecks || _activePermissionUser == null)
            return;

        UpdateSelectAllPermissionsState(_activePermissionUser);
    }
    #endregion
    #endregion

    #region Controls Behavior

    #region TextSearch
    private void TxtName_Pasting(object sender, DataObjectPastingEventArgs e)
    {

        _isPasting = true;
        Dispatcher.BeginInvoke(
    new Action(() =>
    {
        _isPasting = false;
    }),
    DispatcherPriority.Background);


    }
    private void TxtName_Loaded(object sender, RoutedEventArgs e)
    {
        (sender as GridSearchControl).NullText = "ابحث باسم المستخدم...";
    }
    #endregion


    #region TxtUserName
    private void txtUserName_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            txtPassword.Focus();
        }
    }

    private void txtUserName_EditValueChanging(object sender, DevExpress.Xpf.Editors.EditValueChangingEventArgs e)
    {
        txtUserName.SetCurrentValue(TagProperty, 0);
        txtUserName.GetBindingExpression(TagProperty)?.UpdateSource();
    }

    #endregion

    #region TxtPassord
    private void txtPassword_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            txtConfirmPassword.Focus();
        }
    }
    private void txtPassword_EditValueChanged(object sender, DevExpress.Xpf.Editors.EditValueChangedEventArgs e)
    {
        txtPassword.SetCurrentValue(TagProperty, 0);
        txtPassword.GetBindingExpression(TagProperty)?.UpdateSource();
    }


    #endregion

    #region TxtConfirmPassword
    private void txtConfirmPassword_EditValueChanged(object sender, DevExpress.Xpf.Editors.EditValueChangedEventArgs e)
    {
        txtConfirmPassword.SetCurrentValue(TagProperty, 0);
        txtConfirmPassword.GetBindingExpression(TagProperty)?.UpdateSource();
    }



    private void txtConfirmPassword_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            chkAllPermissions.Focus();
        }
    }

    #endregion

    #region chkAllPermissions
    private void chkAllPermissions_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            chkIssued.Focus();
        }
    }

    private void ToggleAllPermissions_Click(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizingPermissionChecks || _activePermissionUser == null)
            return;

        // A partial selection becomes fully enabled; a full selection is cleared.
        var enableAll = !GetPermissionValues(_activePermissionUser).All(enabled => enabled);
        _isSynchronizingPermissionChecks = true;
        try
        {
            SetAllPermissions(_activePermissionUser, enableAll);

            foreach (var checkBox in FindVisualChildren<System.Windows.Controls.CheckBox>(permissionFlyoutPanel))
            {
                if (!ReferenceEquals(checkBox, chkAllPermissions))
                    checkBox.SetCurrentValue(
                        System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                        enableAll);
            }

            chkAllPermissions.IsChecked = enableAll;
        }
        finally
        {
            _isSynchronizingPermissionChecks = false;
        }

    }


    #endregion

    #region chkIssued

    private void chkIssued_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            chkIncoming.Focus();
        }

    }

    #endregion

    #region chkIncoming

    private void chkIncoming_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            chkIssuedArchive.Focus();
        }
    }
    #endregion

    #region chkIssuedArchive
    private void chkIssuedArchive_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            chkIncomingArchive.Focus();
        }

    }

    #endregion

    #region chkIncomingArchive
    private void chkIncomingArchive_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            chkExplorer.Focus();
        }
    }
    #endregion

    #region chkExplorer

    private void chkExplorer_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            chkClass.Focus();
        }
    }
    #endregion

    #region chkClass
    private void chkClass_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            chkEciver.Focus();
        }
    }

    #endregion

    #region chkEciver
    private void chkEciver_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            chkEmail.Focus();
        }
    }
    #endregion

    #region chkEmail
    private void chkEmail_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            chkWhatsapp.Focus();
        }
    }
    #endregion

    #region chkWhatsapp
    private void chkWhatsapp_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CompletionPermission.Focus();
        }
    }

    #endregion

    #region chkUsers
    //private void chkUsers_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    //{
    //    if (e.Key == Key.Enter)
    //    {
    //        chkNewYear.Focus();
    //    }
    //}
    #endregion

    #region التنقل من صلاحية إنهاء الصيانة
    // يتابع مفتاح Enter التنقل إلى صلاحية تقارير الفروع.
    private void CompletionPermission_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) chkNewYear.Focus();
    }
    #endregion

    #region chkNewYear
    private void chkNewYear_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            btnApplyPermissionChanges.Focus();
        }
    }
    #endregion
    #endregion

    #region UserCard

    private async void UserCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement card ||
            IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        // A single click focuses/selects this card, even before its data has loaded.
        if (card.DataContext is DevExpress.Xpf.Grid.CardData cardData &&
            cardData.RowHandle.Value >= 0)
            gridUsers.View.FocusedRowHandle = cardData.RowHandle.Value;

        if (e.ClickCount != 2 ||
            card.Tag is not int userId ||
            _isLoadingPermissionUser ||
            !btnApplyPermissionChanges.IsEnabled)
        {
            return;
        }

        e.Handled = true;
        _isLoadingPermissionUser = true;
        try
        {
            // Feedback rows are read-only proxies; edit a detached database entity.
            var user = await ViewModel.GetUserAsync(userId);
            if (_isClosed)
                return;
            if (user == null)
            {
                MessageBox.Show("لم يتم العثور على المستخدم في قاعدة البيانات.");
                await RefreshUsersAsync();
                return;
            }

            OpenUserEditor(user, isNewUser: false);
        }
        catch (OperationCanceledException) when (_isClosed)
        {
        }
        catch (Exception ex)
        {
            if (!_isClosed)
                MessageBox.Show("تعذر تحميل بيانات المستخدم:\n" + ex.Message,
                    "إدارة المستخدمين", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoadingPermissionUser = false;
        }
    }

    #region MouseLeftButtonDown

    #endregion

    #endregion







    #region Edit Panel

    private void NewUser_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoadingPermissionUser || !btnApplyPermissionChanges.IsEnabled)
            return;

        OpenUserEditor(new T_SystemUser
        {
            User_Name = string.Empty,
            PassWord = string.Empty,
            st = true
        }, isNewUser: true);
    }

    private void OpenUserEditor(T_SystemUser user, bool isNewUser)
    {
        _isNewUser = isNewUser;
        _activePermissionUser = user;
        _isSynchronizingPermissionChecks = true;
        try
        {
            permissionFlyoutPanel.DataContext = user;
            txtPanelTitle.Text = isNewUser ? "إضافة مستخدم جديد" : "صلاحيات المستخدم";
            btnApplyPermissionChanges.Content = isNewUser ? "إضافة المستخدم" : "حفظ التعديلات";
            txtUserName.IsReadOnly = false;
            txtUserName.Text = user.User_Name ?? string.Empty;
            txtPassword.Clear();
            txtConfirmPassword.Clear();
            foreach (FrameworkElement editor in new FrameworkElement[]
                     { txtUserName, txtPassword, txtConfirmPassword })
            {
                editor.SetCurrentValue(TagProperty, 0);
                editor.GetBindingExpression(TagProperty)?.UpdateSource();
            }
            UpdateSelectAllPermissionsState(user);
            edit_panel.IsOpen = true;
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
        finally
        {
            _isSynchronizingPermissionChecks = false;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (edit_panel.IsOpen && ReferenceEquals(_activePermissionUser, user))
                txtUserName.Focus();
        }), DispatcherPriority.Input);
    }

    private static void ShowUserEditorError(FrameworkElement editor, string message)
    {
        ErrorRule.ErrorMessage = message;
        editor.SetCurrentValue(TagProperty, 1);
        editor.GetBindingExpression(TagProperty)?.UpdateSource();
        editor.Focus();
    }

    #region Close Panel
    private void ClosePermissionFlyout_Click(object sender, RoutedEventArgs e)
    {
        edit_panel.IsOpen = false;
        permissionFlyoutPanel.DataContext = null;
        _activePermissionUser = null;
        _isNewUser = false;
        txtPassword.Clear();
        txtConfirmPassword.Clear();
    }

    #endregion

    #region Insert Button
    private async void ApplyPermissionChanges_Click(object sender, RoutedEventArgs e)
    {
        #region ضوابط للادخال
        if (!btnApplyPermissionChanges.IsEnabled ||
            permissionFlyoutPanel.DataContext is not T_SystemUser selectedUser)
            return;

        bool wasNew = _isNewUser;

        string name = txtUserName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            txtUserName.SetCurrentValue(TagProperty, 1);
            ErrorRule.ErrorMessage = "يجب ادخال اسم المستخدم";
            txtUserName.GetBindingExpression(TagProperty)?.UpdateSource();
            txtUserName.Focus();
            return;

        }

        if (name.Length > 50)
        {
            ShowUserEditorError(txtUserName, "اسم المستخدم يجب ألا يتجاوز 50 حرفًا");
            return;
        }

        string password = txtPassword.Password ?? string.Empty;
        if (wasNew && string.IsNullOrWhiteSpace(password))
        {
            ShowUserEditorError(txtPassword, "يجب إدخال كلمة مرور للمستخدم الجديد");
            return;
        }

        if (!string.IsNullOrEmpty(password) && password.Length < 3)
        {
            ShowUserEditorError(txtPassword, "كلمة المرور يجب أن تكون 3 خانات على الأقل");
            return;
        }
        if (!string.IsNullOrEmpty(txtPassword.Password) && string.IsNullOrEmpty(txtConfirmPassword.Password))
        {
            ShowUserEditorError(txtConfirmPassword, "يجب تأكيد كلمة المرور");
            return;
        }

        if (txtPassword.Password != txtConfirmPassword.Password)
        {
            ShowUserEditorError(txtConfirmPassword, "كلمة المرور غير متطابقة مع تأكيد كلمة المرور");
            return;
        }

        string encodedPassword = string.IsNullOrEmpty(password) ? string.Empty : App.encrypt(password);
        if (encodedPassword.Length > 12)
        {
            ShowUserEditorError(txtPassword, "كلمة المرور تتجاوز الحد المسموح، يرجى تقصيرها");
            return;
        }
        if (!GetPermissionValues(selectedUser).Any(enabled => enabled))
        {
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            ThemedMessageBox.Show(
           null,
           title: "منظومة الخدمات والصيانة",
           text: $"يرجى اختيار صلاحية واحدة على الأقل",
           messageBoxButtons: MessageBoxButton.OK,
           defaultButton: MessageBoxResult.OK,
           icon: MessageBoxImage.Error,
           true);
            ApplicationThemeHelper.ApplicationThemeName = "None";

            return;
        }
        #endregion

        try
        {
            #region الاتصال بقاعدة البياانت
            btnApplyPermissionChanges.IsEnabled = false;
            await using var db = ViewModel.CreateContext();

            if (await db.T_SystemUsers.AnyAsync(u =>
                u.User_Name == name && (wasNew || u.User_ID != selectedUser.User_ID)))
            {
                ErrorRule.ErrorMessage = "اسم المستخدم موجود مسبقاً";
                txtUserName.SetCurrentValue(TagProperty, 1);
                txtUserName.GetBindingExpression(TagProperty)?.UpdateSource();
                txtUserName.Focus();
                return;
            }

            #endregion


            // نحتفظ بالسجل المحفوظ لتطبيق صلاحيات الحساب الحالي عند الرجوع إلى الرئيسية.
            var savedUser = selectedUser;
            #region كود الادراج
            if (wasNew)
            {
                selectedUser.User_Name = name;
                selectedUser.st = true;
                selectedUser.PassWord = encodedPassword;
                // يفترض أن User_ID عمود IDENTITY في SQL Server.
                db.T_SystemUsers.Add(selectedUser);
            }

            #endregion


            #region كود التعديل
            else
            {
                var existingUser = await db.T_SystemUsers
                    .FirstOrDefaultAsync(u => u.User_ID == selectedUser.User_ID);
                if (existingUser == null)
                {
                    MessageBox.Show("لم يتم العثور على المستخدم في قاعدة البيانات.");
                    return;
                }

                existingUser.User_Name = name;
                existingUser.forbuild = selectedUser.forbuild;
                existingUser.foremp = selectedUser.foremp;
                existingUser.forlocation = selectedUser.forlocation;
                existingUser.formaint = selectedUser.formaint;
                existingUser.fordon = selectedUser.fordon;
                existingUser.formaintrepo = selectedUser.formaintrepo;
                existingUser.formangreq = selectedUser.formangreq;
                existingUser.foruser = selectedUser.foruser;
                existingUser.branch_report = selectedUser.branch_report;
                existingUser.forservices = selectedUser.forservices;
                existingUser.mang_check = selectedUser.mang_check;
                savedUser = existingUser;

                
                if (!string.IsNullOrEmpty(password))
                    existingUser.PassWord = encodedPassword;
            }

            #endregion


            await db.SaveChangesAsync();
            UpdateSignedInUser(savedUser);

            selectedUser.User_Name = name;
            _isNewUser = false;
            if (wasNew)
                ClosePermissionFlyout_Click(sender, e);

            await RefreshUsersAsync();

            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            ThemedMessageBox.Show(
           null,
           title: "منظومة الخدمات والصيانة",
           text: wasNew ? "تمت إضافة المستخدم بنجاح" : "تم حفظ بيانات المستخدم بنجاح",
           messageBoxButtons: MessageBoxButton.OK,
           defaultButton: MessageBoxResult.OK,
           icon: MessageBoxImage.Information,
           true);
            ApplicationThemeHelper.ApplicationThemeName = "None";
            txtPassword.Clear();
            txtConfirmPassword.Clear();
        }
        catch (Exception ex)
        {
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            ThemedMessageBox.Show(
           null,
           title: "منظومة الخدمات والصيانة",
           text: $"حدث خطأ اثناء عملية الحفظ {ex.Message}",
           messageBoxButtons: MessageBoxButton.OK,
           defaultButton: MessageBoxResult.OK,
           icon: MessageBoxImage.Error,
           true);
            ApplicationThemeHelper.ApplicationThemeName = "None";
        }
        finally
        {
            btnApplyPermissionChanges.IsEnabled = true;
        }
    }

    #endregion


    #endregion



















}


