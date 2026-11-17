using Maintenance_app.converts;
using Maintenance_app.DatabContext;
using Maintenance_app.Models;
using Maintenance_app.viewo_Model;
using DevExpress.Data.Async.Helpers;
using DevExpress.Data.Filtering;
using DevExpress.Mvvm.Native;
using DevExpress.Mvvm.Xpf;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors.Native;
using DevExpress.Xpf.Grid;
using DevExpress.Xpf.Reports.UserDesigner.Native;
using MahApps.Metro.Controls;
using Maintenance_app;
 using Maintenance_app.dbContext;
 using Microsoft.EntityFrameworkCore;
 using System.Windows;
using System.Windows.Controls;
 using System.Windows.Input;
 using System.Windows.Threading;
 
namespace Maintenance_app.support_Data
{
    /// <summary>
    /// Interaction logic for InstitutionWindow.xaml
    /// </summary>
    public partial class employeys : MetroWindow
    {
        private bool _isPasting;
        private CancellationTokenSource _searchCancellation;
        #region "All Functions"
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
                        new OperandProperty("Emp_Name"),
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
        new FieldAndHighlightingString("Emp_Name", search)
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

        public bool InstitutionExists(string name)
        {
            string searchName = NormalizeArabic(name);

            if (string.IsNullOrEmpty(searchName))
                return false;

            DbContextHelper.InitOptions(
                App.ConnectionString
            );

            using var context = new Maintenance_DabaseContext(DbContextHelper.Options);

            var names = context.T_Employes
                .AsNoTracking()
                .Select(x => x.Emp_Name)
                .ToList();

            return names.Any(x =>
                NormalizeArabic(x) == searchName);
        }
        public bool InstitutionExists_update(string name)
        {
            string searchName = NormalizeArabic(name);

            if (string.IsNullOrEmpty(searchName))
                return false;

            DbContextHelper.InitOptions(
                App.ConnectionString
            );

            using var context = new Maintenance_DabaseContext(DbContextHelper.Options);

            var names = context.T_Employes.Where(k => k.Employe_ID != ((T_Employe)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow).Employe_ID)
                .AsNoTracking()
                .Select(x => x.Emp_Name)
                .ToList();

            return names.Any(x =>
                NormalizeArabic(x) == searchName);
        }

        private void cb_classt_type_SubstituteDisplayFilter(object sender, DevExpress.Xpf.Editors.Helpers.SubstituteDisplayFilterEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.DisplayText))
                return;

            string search = e.DisplayText.Trim();

            var variants = GetArabicSearchVariants(search);

            if (variants.Count == 0)
                return;

            CriteriaOperator field =
                new OperandProperty("Emp_Name");

            var filters = variants
                .Select(x =>
                    (CriteriaOperator)new FunctionOperator(
                        FunctionOperatorType.StartsWith,
                        field,
                        new OperandValue(x)))
                .ToArray();

            e.DisplayFilter = new GroupOperator(
                GroupOperatorType.Or,
                filters);

            e.Handled = true;
        }
        private bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var mail = new System.Net.Mail.MailAddress(email);

                return mail.Address.Equals(
                    email.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region " Windows Behavior"
        public employeys()
        {
            InitializeComponent();
        }
        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TxtName.Focus();
        }
        #endregion



        #region " Controls Behavior"
        #region TxtName

        private void TxtName_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            TxtName.Tag = 0;
        }
        private void TxtName_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                cb_classt_type.Focus();
        }

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
            (sender as GridSearchControl).NullText = "أدخل اسم الفني";
        }

        #endregion

        #region cb_classt_type
        private void cb_classt_type_EditValueChanged(object sender, DevExpress.Xpf.Editors.EditValueChangedEventArgs e)
        {
            cb_classt_type.Tag = 0;
        }

        private void cb_classt_type_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                insert_Click(insert, new RoutedEventArgs());
        }

        private void cb_classt_type_SelectedIndexChanged(object sender, RoutedEventArgs e)
        {
            cb_classt_type.Tag = 0;

        }
        #endregion


        #region TxtName_update
        private void TxtName_update_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtName_update.Tag = 0;
        }
        private void TxtName_update_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                update_button_ClickAsync(update_button, new RoutedEventArgs());
        }
        #endregion


        #region  cb_classt_type_update
        private void cb_classt_type_Copy_EditValueChanged(object sender, DevExpress.Xpf.Editors.EditValueChangedEventArgs e)
        {
            cb_classt_type.Tag = 0;
        }

        private void cb_classt_type_Copy_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                update_button_ClickAsync(update_button, new RoutedEventArgs());
        }

        private void cb_classt_type_Copy_SelectedIndexChanged(object sender, RoutedEventArgs e)
        {
            cb_classt_type.Tag = 0;

        }
        #endregion

        #endregion








        #region All Main Button
        #region insert
        private async void insert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtName.Tag = 0;
                cb_classt_type.Tag = 0;
                #region قيود ادخال
                if (string.IsNullOrWhiteSpace(TxtName.SearchText))
                {
                    TxtName.Tag = 1;
                    ErrorRule.ErrorMessage = "أدخل اسم الفني";
                    TxtName.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName.Focus();
                    return;
                }

                string name = TxtName.SearchText.Trim();



                if (InstitutionExists(name))
                {
                    TxtName.Tag = 1;
                    ErrorRule.ErrorMessage = "اسم الفني موجود مسبقًا";
                    TxtName.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName.Focus();
                    return;

                }

                if (cb_classt_type.SelectedIndex<0)
                {
                    cb_classt_type.Tag = 1;
                    ErrorRule.ErrorMessage = "اختار الوظيفة";
                    cb_classt_type.GetBindingExpression(TagProperty).UpdateSource();
                    cb_classt_type.Focus();
                    return;
                }
             
                #endregion

                DbContextHelper.InitOptions(
              App.ConnectionString
          );

                using var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                context.T_Employes.AddAsync(new T_Employe { Emp_Name = name, Adje = (byte?)cb_classt_type.SelectedIndex,
                    Location = App.location.Location,
                    Location_ID = App.location.Location_ID,
                    Branch_ID = App.location.Branch_ID,
                });
                await context.SaveChangesAsync();
                ((employey_viewmodel)this.DataContext).DataSourceRefresh(new DataSourceRefreshArgs());
                TxtName.SearchText = null;
                cb_classt_type.SelectedIndex = -1;
                ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
                ThemedMessageBox.Show(
               null,
               title: "منظومة الخدمات والصيانة",
               text: $"تم ادراج بيانات الفني",
               messageBoxButtons: MessageBoxButton.OK,
               defaultButton: MessageBoxResult.OK,
               icon: MessageBoxImage.Information,
               true);
                ApplicationThemeHelper.ApplicationThemeName = "None";
                TxtName.Focus();


            }
            catch (Exception ex)
            {
                ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
                ThemedMessageBox.Show(
               null,
               title: "منظومة الخدمات والصيانة",
               text: $"خطأ {ex.Message}",
               messageBoxButtons: MessageBoxButton.OK,
               defaultButton: MessageBoxResult.OK,
               icon: MessageBoxImage.Error,
               true);
                ApplicationThemeHelper.ApplicationThemeName = "None";
            }
        }
        #endregion

        #region update
        private async void update_button_ClickAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtName_update.Tag = 0;
                cb_classt_type_Copy.Tag = 0;
                #region قيود ادخال
                if (string.IsNullOrWhiteSpace(TxtName_update.Text))
                {
                    TxtName_update.Tag = 1;
                    ErrorRule.ErrorMessage = "أدخل اسم الفني";
                    TxtName_update.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName_update.Focus();
                    return;
                }

                string name = TxtName_update.Text.Trim();



                if (InstitutionExists_update(name))
                {
                    TxtName_update.Tag = 1;
                    ErrorRule.ErrorMessage = "اسم الفني موجود مسبقًا";
                    TxtName_update.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName_update.Focus();
                    return;

                }
                if (cb_classt_type_Copy.SelectedIndex < 0)
                {
                    cb_classt_type_Copy.Tag = 1;
                    ErrorRule.ErrorMessage = "اختار الوظيفة";
                    cb_classt_type_Copy.GetBindingExpression(TagProperty).UpdateSource();
                    cb_classt_type_Copy.Focus();
                    return;
                }

                #endregion

                DbContextHelper.InitOptions(
              App.ConnectionString
          );

                using var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                T_Employe t = context.T_Employes.SingleOrDefault(k => k.Employe_ID == ((T_Employe)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow).Employe_ID)
                    ;
                t.Emp_Name = TxtName_update.Text;
                t.Adje = (byte?)cb_classt_type_Copy.SelectedIndex;

                await context.SaveChangesAsync();
                //((T_Employe)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow).title_email_reciver = TxtName_update.Text;

                //((T_Employe)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).Content[1]).title_email_reciver = TxtName_update.Text; 
                //TxtName_update.Clear();
                ((employey_viewmodel)this.DataContext).DataSourceRefresh(new DataSourceRefreshArgs());
                ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
                ThemedMessageBox.Show(
               null,
               title: "منظومة الخدمات والصيانة",
               text: $"تم تعديل بيانات الفني",
               messageBoxButtons: MessageBoxButton.OK,
               defaultButton: MessageBoxResult.OK,
               icon: MessageBoxImage.Information,
               true);
                ApplicationThemeHelper.ApplicationThemeName = "None";
                TxtName.Focus();
                TxtName_update.Clear();
                cb_classt_type_Copy.SelectedIndex = -1  ;

                TxtName_update.Tag = 0;
                cb_classt_type_Copy.Tag = 0;
                config_Nationalty.IsOpen = false;


            }
            catch (Exception ex)
            {
                ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
                ThemedMessageBox.Show(
               null,
               title: "منظومة الخدمات والصيانة",
               text: $"خطأ {ex.Message}",
               messageBoxButtons: MessageBoxButton.OK,
               defaultButton: MessageBoxResult.OK,
               icon: MessageBoxImage.Error,
               true);

                ApplicationThemeHelper.ApplicationThemeName = "None";
                config_Nationalty.IsOpen = false;

            }

        }
        #endregion
        #region close Window
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        #endregion

        #region close update
        private void closeconfig_Click(object sender, RoutedEventArgs e)
        {
            TxtName_update.Clear();
            cb_classt_type_Copy.SelectedIndex = -1  ;

            TxtName_update.Tag = 0;
            cb_classt_type_Copy.Tag = 0;
            config_Nationalty.IsOpen = false;
        }
        #endregion
        #endregion







        #region GridControl
        private void Benef_List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Benef_List.SelectedItem == null)
                return;
            T_Employe t = ((T_Employe)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow);
            TxtName_update.Text = t.Emp_Name;
            cb_classt_type_Copy.SelectedIndex = t.Adje ?? -1;
            TxtName_update.Focus();
            config_Nationalty.IsOpen = true;

        }








        #endregion

       
    }
}
