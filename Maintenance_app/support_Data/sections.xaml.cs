using Maintenance_app.converts;
using Maintenance_app.DatabContext;
using Maintenance_app.Models;
using Maintenance_app.viewo_Model;
using DevExpress.CodeParser;
using DevExpress.Data.Async.Helpers;
using DevExpress.Data.Filtering;
using DevExpress.Mvvm.Native;
using DevExpress.Mvvm.Xpf;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Editors.Native;
using DevExpress.Xpf.Grid;
using DevExpress.Xpf.Reports.UserDesigner.Native;
using MahApps.Metro.Controls;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;
using Maintenance_app.dbContext;

namespace Maintenance_app.support_Data
{
    /// <summary>
    /// Interaction logic for InstitutionWindow.xaml
    /// </summary>
    public partial class sections : MetroWindow
    {
        ObservableCollection<T_Manage> users_list = new ObservableCollection<T_Manage>();

        private bool _isPasting;
        private CancellationTokenSource _searchCancellation;
        private bool mov_handel = false;
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
                        new OperandProperty("Secti"),
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
        new FieldAndHighlightingString("Secti", search)
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
            if (string.IsNullOrEmpty(text)) return text;
            return text.Replace('أ', 'ا')
                .Replace('إ', 'ا')
                .Replace('آ', 'ا')
                .Replace('ٱ', 'ا')
                .Replace('ى', 'ي')
                .Replace('ؤ', 'و')
                .Replace('ئ', 'ي');
        }
        public bool InstitutionExists(string name)
        {
            if (cb_classt_type.SelectedItem != null)
            {
                string searchName = NormalizeArabic(name);

                if (string.IsNullOrEmpty(searchName))
                    return false;

                DbContextHelper.InitOptions(
                    App.ConnectionString
                );

                using var context = new Maintenance_DabaseContext(DbContextHelper.Options);

                var names = context.T_Sections.Where(k => k.Manag_ID == ((T_Manage)cb_classt_type.SelectedItem).Manag_ID)
                    .AsNoTracking()
                    .Select(x => x.Secti)
                    .ToList();

                return names.Any(x =>
                    NormalizeArabic(x) == searchName);
            }
            else
                return false;
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

            var names = context.T_Sections.Where(k => k.Manag_ID == ((T_Section)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow).Manag_ID &&
                  k.Secti_ID != ((T_Section)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow).Secti_ID)
                .AsNoTracking()
                .Select(x => x.Secti)
                .ToList();

            return names.Any(x =>
                NormalizeArabic(x) == searchName);
        }
        private async Task LoadclasstypeAsync()
        {
            try
            {
                DbContextHelper.InitOptions(
                    App.ConnectionString
                );

                await using var db = new Maintenance_DabaseContext(DbContextHelper.Options);

                var class_types = await db.T_Manages.OrderBy(k => k.Manag)
                    .AsNoTracking()
                    .ToListAsync();

                users_list = new ObservableCollection<T_Manage>(class_types);

                cb_classt_type.ItemsSource = users_list;
                cb_classt_type.Focus();

            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

    

        #endregion

        #region " Windows Behavior"
        public sections()
        {
            InitializeComponent();
        }
        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Yield();

            await LoadclasstypeAsync();

            cb_classt_type.Focus();


        }
        #endregion



        #region " Controls Behavior"

        #region insert controls

        #region cb_classt_type
        private void cb_classt_type_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(cb_classt_type.DisplayText))
                btnCancelClassType.Visibility = Visibility.Visible;
            else
                btnCancelClassType.Visibility = Visibility.Hidden;
            if (e.Key == Key.Enter)
                TxtName.Focus();
        }
        private void cb_classt_type_SelectedIndexChanged(object sender, RoutedEventArgs e)
        {
            cb_classt_type.Tag = 0;
        }

        private void cb_classt_type_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            cb_classt_type.Tag = 0;
            if (!string.IsNullOrWhiteSpace(e.Text))
                btnCancelClassType.Visibility = Visibility.Visible;
            else
                btnCancelClassType.Visibility = Visibility.Hidden;

        }
        #endregion

        #region TxtName

        private void TxtName_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            TxtName.Tag = 0;
        }
        private void TxtName_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (mov_handel == true)
            {
                mov_handel = false;
                return;
            }
            if (e.Key == Key.Enter)
                insert_Click(insert, new RoutedEventArgs());
        }
        private void TxtName_Pasting(object sender, DataObjectPastingEventArgs e)
        {


            _isPasting = true;
            // السماح بعملية اللصق الطبيعية
            Dispatcher.BeginInvoke(new Action(() => {
                _isPasting = false;
                // إعادة تقييم البحث بعد اكتمال اللصق
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (TxtName != null)
                    {
                        TxtName.SearchText = TxtName.SearchText;
                    }
                }), DispatcherPriority.Input);
            }), DispatcherPriority.Background);
        }

        private void TxtName_Loaded(object sender, RoutedEventArgs e)
        {
            (sender as GridSearchControl).NullText = "أدخل القسم او الوحدة";
        }

        #endregion

        #region txtchar
        private void Txtchar_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                insert_Click(insert, new RoutedEventArgs());
        }

        private void Txtchar_TextChanged(object sender, TextChangedEventArgs e)
        {
            //Txtchar.Tag = 0;
        }
        #endregion

        #endregion


        #region controls update


        #region Txtchar_update
        private void Txtchar_update_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                update_button_ClickAsync(update_button, new RoutedEventArgs());
        }

        private void Txtchar_update_TextChanged(object sender, TextChangedEventArgs e)
        {
            //Txtchar_update.Tag = 0;
        }
        #endregion

        #region TxtName_update
        private void TxtName_update_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtName.Tag = 0;
        }
        private void TxtName_update_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                update_button_ClickAsync(update_button, new RoutedEventArgs());
        }
        #endregion

        #endregion

        #endregion








        #region All Main Button


        #region insert
        private void insert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtName.Tag = 0;

                #region قيود ادخال
           if(cb_classt_type.SelectedItem == null)
                {
                    cb_classt_type.Tag = 1;
                    ErrorRule.ErrorMessage = "اختر الادارة او  القسم ";
                    cb_classt_type.GetBindingExpression(TagProperty).UpdateSource();
                    cb_classt_type.Focus();
                    return;
                }
                if (string.IsNullOrWhiteSpace(TxtName.SearchText))
                {
                    TxtName.Tag = 1;
                    ErrorRule.ErrorMessage = "أدخل القسم او الوحدة";
                    TxtName.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName.Focus();
                    return;
                }

                string name = TxtName.SearchText.Trim();



                if (InstitutionExists(name))
                {
                    TxtName.Tag = 1;
                    ErrorRule.ErrorMessage = "اسم القسم او الوحدة موجود مسبقًا";
                    TxtName.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName.Focus();
                    return;

                }
                //if (string.IsNullOrWhiteSpace(Txtchar.Text))
                //{
                //    Txtchar.Tag = 1;
                //    ErrorRule.ErrorMessage = "أدخل الترميز";
                //    Txtchar.GetBindingExpression(TagProperty).UpdateSource();
                //    Txtchar.Focus();
                //    return;
                //}
                #endregion

                DbContextHelper.InitOptions(
              App.ConnectionString
          );

                using var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                T_Section t = new T_Section() { Secti = name,  Manag_ID = ((T_Manage)cb_classt_type.SelectedItem).Manag_ID,
                    Location = App.location.Location,
                    Location_ID = App.location.Location_ID,
                    Branch_ID = App.location.Branch_ID,
                     
                };
                context.T_Sections.Add(t);
                context.SaveChanges();

                //if(App._listclass != null && App._listclass.Count > 0)
                //App._listclass.Add(new T_Section
                //{
                //    Secti_ID = t.Secti_ID,
                //    Secti = name,
                //    Manag_ID = t.Manag_ID,
                //    Location = App.location.Location,
                //    Location_ID = App.location.Location_ID,
                //    Branch_ID = App.location.Branch_ID,
                //    T_Manage = (T_Manage)cb_classt_type.SelectedItem
                //});


                ((section_viewmodel)this.DataContext).DataSourceRefresh(new DataSourceRefreshArgs());
                TxtName.SearchText = null;
                ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
                ThemedMessageBox.Show(
               null,
               title: "منظومة الخدمات والصيانة",
               text: $"تم ادراج القسم او الوحدة",
               messageBoxButtons: MessageBoxButton.OK,
               defaultButton: MessageBoxResult.OK,
               icon: MessageBoxImage.Information,
               true);
                ApplicationThemeHelper.ApplicationThemeName = "None";
                mov_handel = true;
                TxtName.SearchText = null;
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
                    ErrorRule.ErrorMessage = "أدخل القسم او الوحدة";
                    TxtName_update.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName_update.Focus();
                    return;
                }

                string name = TxtName_update.Text.Trim();



                if (InstitutionExists_update(name))
                {
                    TxtName_update.Tag = 1;
                    ErrorRule.ErrorMessage = "اسم القسم او الوحدة موجود مسبقًا";
                    TxtName_update.GetBindingExpression(TagProperty).UpdateSource();
                    TxtName_update.Focus();
                    return;

                }

                //if (string.IsNullOrWhiteSpace(Txtchar_update.Text))
                //{
                //    Txtchar_update.Tag = 1;
                //    ErrorRule.ErrorMessage = "أدخل الترميز";
                //    Txtchar_update.GetBindingExpression(TagProperty).UpdateSource();
                //    Txtchar_update.Focus();
                //    return;
                //}
                #endregion

                DbContextHelper.InitOptions(
              App.ConnectionString
          );

                using var context = new Maintenance_DabaseContext(DbContextHelper.Options);
                T_Section t = context.T_Sections.SingleOrDefault(k => k.Secti_ID == ((T_Section)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow).Secti_ID)
                    ;
                t.Secti = TxtName_update.Text;
                //t.char_class = Txtchar_update.Text;
                await context.SaveChangesAsync();
                //if (App._listclass is { } classes)
                //{
                //    var cached = classes.FirstOrDefault(item => item.Secti_ID == t.Secti_ID);
                //    if (cached != null)
                //    {
                //        // Replace the item so lookup editors refresh their cached display text.
                //        classes[classes.IndexOf(cached)] = new T_Section
                //        {
                //            Secti_ID = t.Secti_ID,
                //            Secti = t.Secti,
                //            Manag_ID = t.Manag_ID,
                //            Location = App.location.Location,
                //            Location_ID = App.location.Location_ID,
                //            Branch_ID = App.location.Branch_ID,
                //            T_Manage =  cached.T_Manage
                //        };
                //    }
                //}
                ////((T_Section)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow).Secti = TxtName_update.Text;

                //((T_Section)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).Content[1]).Secti = TxtName_update.Text; 
                //TxtName_update.Clear();
                ((section_viewmodel)this.DataContext).DataSourceRefresh(new DataSourceRefreshArgs());
                ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
                ThemedMessageBox.Show(
               null,
               title: "منظومة الخدمات والصيانة",
               text: $"تم تعديل القسم او الوحدة",
               messageBoxButtons: MessageBoxButton.OK,
               defaultButton: MessageBoxResult.OK,
               icon: MessageBoxImage.Information,
               true);
                ApplicationThemeHelper.ApplicationThemeName = "None";
                TxtName.Focus();
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
            //Txtchar_update.Clear();
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
            T_Section t = ((T_Section)((ReadonlyThreadSafeProxyForObjectFromAnotherThread)Benef_List.SelectedItem).OriginalRow);
            TxtName_update.Text = t.Secti;
            //Txtchar_update.Text = t.char_class;
            TxtName_update.Focus();
            config_Nationalty.IsOpen = true;

        }





        #endregion

        private string _classTypeSearchText = string.Empty;
        private void cb_classt_type_EditValueChanged(object sender, DevExpress.Xpf.Editors.EditValueChangedEventArgs e)


        {
            var edit = (ComboBoxEdit)sender;
            if (!string.IsNullOrWhiteSpace(edit.Text))
                btnCancelClassType.Visibility = Visibility.Visible;
            else
                btnCancelClassType.Visibility = Visibility.Hidden;
            cb_classt_type.Tag = 0;
            if (DataContext is section_viewmodel vm)
            {
                if (e.NewValue == null ||
                    e.NewValue == DependencyProperty.UnsetValue)
                {
                    vm.Manag_ID = 0;
                    vm.DataSourceRefresh(new DataSourceRefreshArgs());
                    return;
                }

                vm.Manag_ID = Convert.ToInt16(e.NewValue);

                vm.DataSourceRefresh(new DataSourceRefreshArgs());
            }
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
                new OperandProperty("Manag");

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

        private void ButtonInfo_Click(object sender, RoutedEventArgs e)
        {
            cb_classt_type.Clear();
            btnCancelClassType.Visibility = Visibility.Hidden;
        }

        private void cb_classt_type_PreviewKeyUp_1(object sender, System.Windows.Input.KeyEventArgs e)
        {

        }
    }
    public static class HighlightHelper
    {
        public static readonly DependencyProperty HighlightTextProperty =
            DependencyProperty.RegisterAttached(
                "HighlightText",
                typeof(string),
                typeof(HighlightHelper),
                new PropertyMetadata(string.Empty, OnHighlightTextChanged));

        public static string GetHighlightText(DependencyObject obj)
        {
            return (string)obj.GetValue(HighlightTextProperty);
        }

        public static void SetHighlightText(
            DependencyObject obj,
            string value)
        {
            obj.SetValue(HighlightTextProperty, value);
        }

        private static readonly DependencyProperty IsInitializedProperty =
            DependencyProperty.RegisterAttached(
                "IsInitialized",
                typeof(bool),
                typeof(HighlightHelper),
                new PropertyMetadata(false));

        private static void OnHighlightTextChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock block)
                return;

            Initialize(block);

            UpdateHighlight(block);
        }

        private static void Initialize(TextBlock block)
        {
            if (GetIsInitialized(block))
                return;

            SetIsInitialized(block, true);

            block.Loaded += Block_Loaded;
            block.Unloaded += Block_Unloaded;
            block.TargetUpdated += Block_TargetUpdated;
        }

        private static void Block_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is TextBlock block)
            {
                UpdateHighlight(block);
            }
        }

        private static void Block_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            // لا نحذف الأحداث هنا لأن TextBlock
            // قد يعاد استخدامه بواسطة الـ Popup.
        }

        private static void Block_TargetUpdated(
            object sender,
            DataTransferEventArgs e)
        {
            if (sender is TextBlock block)
            {
                UpdateHighlight(block);
            }
        }

        private static bool GetIsInitialized(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsInitializedProperty);
        }

        private static void SetIsInitialized(
            DependencyObject obj,
            bool value)
        {
            obj.SetValue(IsInitializedProperty, value);
        }

        private static void UpdateHighlight(TextBlock block)
        {
            if (block == null)
                return;

            string text = block.Text ?? string.Empty;
            string search = GetHighlightText(block) ?? string.Empty;

            // مهم جدًا:
            // لا نعيد بناء الـ Inlines إذا كان Text غير موجود
            if (string.IsNullOrEmpty(text))
            {
                block.Inlines.Clear();
                return;
            }

            if (string.IsNullOrWhiteSpace(search))
            {
                block.Inlines.Clear();
                block.Inlines.Add(new Run(text));
                return;
            }

            string normalizedText = NormalizeArabic(text);
            string normalizedSearch = NormalizeArabic(search);

            if (string.IsNullOrEmpty(normalizedSearch))
            {
                block.Inlines.Clear();
                block.Inlines.Add(new Run(text));
                return;
            }

            int normalizedIndex = normalizedText.IndexOf(
                normalizedSearch,
                StringComparison.OrdinalIgnoreCase);

            // لا يوجد تطابق
            if (normalizedIndex < 0)
            {
                block.Inlines.Clear();
                block.Inlines.Add(new Run(text));
                return;
            }

            int originalStart =
                GetOriginalIndex(text, normalizedIndex);

            int originalEnd =
                GetOriginalIndex(
                    text,
                    normalizedIndex + normalizedSearch.Length);

            if (originalStart < 0 ||
                originalEnd <= originalStart ||
                originalStart >= text.Length)
            {
                block.Inlines.Clear();
                block.Inlines.Add(new Run(text));
                return;
            }

            block.Inlines.Clear();

            // النص قبل التطابق
            if (originalStart > 0)
            {
                block.Inlines.Add(
                    new Run(
                        text.Substring(
                            0,
                            originalStart)));
            }

            // النص المطابق
            int length = originalEnd - originalStart;

            var highlightedRun = new Run(
                text.Substring(
                    originalStart,
                    length));
            var color = (Color)ColorConverter.ConvertFromString("#FF0152A4");
            var brush = new SolidColorBrush(color);
            highlightedRun.Background = brush;
            highlightedRun.Foreground = Brushes.White;
            highlightedRun.FontWeight = FontWeights.Bold;

            block.Inlines.Add(highlightedRun);

            // النص بعد التطابق
            if (originalEnd < text.Length)
            {
                block.Inlines.Add(
                    new Run(
                        text.Substring(
                            originalEnd)));
            }
        }

        private static int GetOriginalIndex(
            string original,
            int normalizedIndex)
        {
            if (normalizedIndex <= 0)
                return 0;

            int count = 0;

            for (int i = 0; i < original.Length; i++)
            {
                string normalizedChar =
                    NormalizeArabic(
                        original[i].ToString());

                if (string.IsNullOrEmpty(normalizedChar))
                    continue;

                if (count >= normalizedIndex)
                    return i;

                count += normalizedChar.Length;

                if (count >= normalizedIndex)
                    return i + 1;
            }

            return original.Length;
        }

        private static string NormalizeArabic(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text
                .Trim()

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
    }
}

