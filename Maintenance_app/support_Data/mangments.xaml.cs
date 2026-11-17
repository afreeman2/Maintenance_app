using DevExpress.Data.Async.Helpers;
using DevExpress.Data.Browsing;
using DevExpress.Data.Filtering;
using DevExpress.Mvvm.Xpf;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Core.Native;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Editors.Native;
using DevExpress.Xpf.Grid;
using MahApps.Metro.Controls;
using Maintenance_app.converts;
using Maintenance_app.DatabContext;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.viewo_Model;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;

namespace Maintenance_app.support_Data
{
    /// <summary>
    /// Interaction logic for servic_types.xaml
    /// </summary>
    public partial class mangments : MetroWindow
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
                        new OperandProperty("Manag"),
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
        new FieldAndHighlightingString("Manag", search)
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

            var names = context.T_Manages
                .AsNoTracking()
                .Select(x => x.Manag)
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

            var names = context.T_Manages.Where(k => k.Manag_ID != ((T_Manage)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow).Manag_ID)
                .AsNoTracking()
                .Select(x => x.Manag)
                .ToList();

            return names.Any(x =>
                NormalizeArabic(x) == searchName);
        }

        #endregion

        #region " Windows Behavior"
        public mangments()
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
                insert_Click(insert, new RoutedEventArgs());

        }
        #endregion

        #region Class code
        private void ClassCode_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            if (sender == TxtName_update)
                update_button_ClickAsync(update_button, new RoutedEventArgs());
            else
                insert_Click(insert, new RoutedEventArgs());
        }

        private void ClassCode_TextChanged(object sender, TextChangedEventArgs e)
        {
            var input = (TextBox)sender;
            input.Tag = 0;
            input.GetBindingExpression(TagProperty)?.UpdateSource();
        }



        private static void ShowClassCodeError(TextBox input, string message)
        {
            input.Tag = 1;
            ErrorRule.ErrorMessage = message;
            input.GetBindingExpression(TagProperty)?.UpdateSource();
            input.Focus();
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

        #endregion








        #region All Main Button
        #region insert
        private async void insert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtName.Tag = 0;

                #region قيود ادخال
                if (string.IsNullOrWhiteSpace(TxtName.SearchText))
                {
                    TxtName.Tag = 1;
                    ErrorRule.ErrorMessage = "أدخل الادارة او القسم";
                    TxtName.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName.Focus();
                    return;
                }
                string name = TxtName.SearchText.Trim();

                if (InstitutionExists(name))
                {
                    TxtName.Tag = 1;
                    ErrorRule.ErrorMessage = "الادارة او القسم موجود مسبقًا";
                    TxtName.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName.Focus();
                    return;

                }


                #endregion

                DbContextHelper.InitOptions(
              App.ConnectionString
          );

                using var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                // Keep the availability check and save atomic across simultaneous users.
                await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                context.T_Manages.Add(new T_Manage
                {
                    Manag = name,
                    Branch_ID = App.location.Branch_ID,
                    Location = App.location.Location,
                    Location_ID = App.location.Location_ID
                ,
                    Date_Modify = DateTime.Now.Date.ToDateOnly()
                });
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                ((mangment_viewmodel)this.DataContext).DataSourceRefresh(new DataSourceRefreshArgs());
                TxtName.SearchText = null;
                ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
                ThemedMessageBox.Show(
               null,
               title: "منظومة الخدمات والصيانة",
               text: $"تم ادراج الادارة او القسم",
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
                #region قيود ادخال
                if (string.IsNullOrWhiteSpace(TxtName_update.Text))
                {
                    TxtName_update.Tag = 1;
                    ErrorRule.ErrorMessage = "أدخل الادارة او القسم";
                    TxtName_update.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName_update.Focus();
                    return;
                }
                string name = TxtName_update.Text.Trim();

                if (InstitutionExists_update(name))
                {
                    TxtName_update.Tag = 1;
                    ErrorRule.ErrorMessage = "اسم الادارة او القسم موجود مسبقًا";
                    TxtName_update.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName_update.Focus();
                    return;

                }
                #endregion

                DbContextHelper.InitOptions(
              App.ConnectionString
          );

                using var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                int selectedId = ((T_Manage)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow).Manag_ID;
                await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                T_Manage t = await context.T_Manages.SingleAsync(type => type.Manag_ID == selectedId);
                t.Manag = name;


                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                //((T_Manag)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow).Manag = TxtName_update.Text;

                //((T_Manag)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).Content[1]).Manag = TxtName_update.Text; 
                //TxtName_update.Clear();
                ((mangment_viewmodel)this.DataContext).DataSourceRefresh(new DataSourceRefreshArgs());
                ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
                ThemedMessageBox.Show(
               null,
               title: "منظومة الخدمات والصيانة",
               text: $"تم تعديل الادارة او القسم",
               messageBoxButtons: MessageBoxButton.OK,
               defaultButton: MessageBoxResult.OK,
               icon: MessageBoxImage.Information,
               true);
                ApplicationThemeHelper.ApplicationThemeName = "None";
                TxtName.Focus();
                TxtName_update.Clear();

                TxtName_update.Tag = 0;
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

            TxtName_update.Tag = 0;
            config_Nationalty.IsOpen = false;
        }
        #endregion
        #endregion







        #region GridControl
        private void Benef_List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Benef_List.SelectedItem == null)
                return;
            T_Manage t = ((T_Manage )((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow);
            TxtName_update.Text = t.Manag;
            TxtName_update.Focus();
            config_Nationalty.IsOpen = true;

        }


        #endregion

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
            (sender as GridSearchControl).NullText = "أدخل الادارة او القسم";
        }
    }

}
