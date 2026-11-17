using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;

internal static class OrderValidationProbe
{
    #region أدوات فحص النافذة الفعلية

    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Pump()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static T Find<T>(Window window, string name) where T : class
    {
        return (T)window.FindName(name);
    }

    private static Border Frame(BaseEdit field)
    {
        return (Border)((FrameworkElement)field.Parent).Parent;
    }

    private static void Click(Window window, string name)
    {
        FrameworkElement button = Find<FrameworkElement>(window, name);
        button.Focus();
        Pump();
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Pump();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, i)))
                yield return child;
        }
    }

    // الكتابة في TextBox الحقيقي تختبر وصول النص إلى EditValue ومسح الخطأ، لا تعيين القيمة فقط.
    private static void Type(TextEdit field, string text)
    {
        field.Focus();
        Pump();
        TextBox editor = Descendants(field).OfType<TextBox>().First();
        editor.Focus();
        editor.SelectAll();
        if (text.Length == 0)
            field.Clear();
        else
            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, editor, text));
        Pump();
    }

    // اختيار فعلي من قائمة DevExpress، مع فحص اختفاء الخطأ قبل إغلاقها وبعد الإغلاق.
    private static void Choose(ComboBoxEdit field, object item, bool multiple = false)
    {
        bool hadError = Validation.GetHasError(Frame(field));
        field.ShowPopup();
        Pump();
        Check(field.IsPopupOpen && field.PopupRoot != null, "The actual choice popup did not open.");
        Check(!hadError || Validation.GetHasError(Frame(field)), "Opening the popup without choosing cleared the error.");
        ListBox list = Descendants(field.PopupRoot!).OfType<ListBox>().First();
        if (multiple)
            list.SelectedItems.Add(item);
        else
            list.SelectedItem = item;
        Pump();
        Check(!Validation.GetHasError(Frame(field)), field.Name + " retained its error while choosing in the open popup.");
        field.ClosePopup();
        Pump();
        Check(!Validation.GetHasError(Frame(field)), field.Name + " restored its error after closing the popup.");
    }

    private static void Error(BaseEdit field, string text)
    {
        Pump();
        Border frame = Frame(field);
        Check(Validation.GetHasError(frame), field.Name + " has no field validation error.");
        Check(Validation.GetErrors(frame)[0].ErrorContent.ToString()!.Contains(text), field.Name + " has the wrong Arabic message.");
        Check(((SolidColorBrush)frame.BorderBrush).Color == Color.FromRgb(220, 0, 12), "Filled-field color overrode the red validation border.");
        Adorner[]? adorners = AdornerLayer.GetAdornerLayer(frame)?.GetAdorners(frame);
        Check(adorners?.Length > 0, "The actual validation adorner is missing.");
        Check(adorners!.SelectMany(Descendants).OfType<TextBlock>().Any(t => t.Text.Contains(text)), "The visible error template has no Arabic message.");
        Check(adorners!.SelectMany(Descendants).OfType<Border>().Any(b => b.Name == "errorMessage" && b.Opacity == 1), "The focused field did not reveal its error message.");
    }

    // فحص رأس التكليف دون حفظ أو اتصال: الدالة تعيد المسودة الصحيحة أو توقف المحاولة بخطأ خانة.
    private static object? ReadDraft(maintenance_Order window)
    {
        try
        {
            return typeof(maintenance_Order).GetMethod("ReadDraft", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException?.GetType().Name == "InputValidationException")
        {
            return null;
        }
    }

    private static void Capture(maintenance_Order window, string path)
    {
        Pump();
        var target = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    #endregion

    #region سيناريوهات المدخلات والتنقل والتعديل والبحث

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-LY");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            foreach (string dictionary in new[] { "Styles/Controls.xaml", "Styles/Fonts.xaml", "Styles/Themes/Light.Steel.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MahApps.Metro;component/" + dictionary) });
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            Maintenance_app.App.ConnectionString = string.Empty;
            Maintenance_app.App.appuser = new T_SystemUser { User_ID = 1 };
            var window = new maintenance_Order
            {
                WindowState = WindowState.Normal, Width = 1366, Height = 768,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000,
                ShowInTaskbar = false
            };
            window.Show();
            window.Activate();
            Pump();
            Directory.CreateDirectory(args[0]);

            Check(!Descendants(window).OfType<Border>().Any(Validation.GetHasError), "Required errors appeared on initial load.");
            Click(window, "Add_To_Prt_List");
            var part = Find<TextEdit>(window, "Text_Parts");
            Error(part, "اسم المادة");
            Check(part.IsKeyboardFocusWithin, "The first invalid material field was not focused.");
            Check(window.State.Parts.Count == 0 && app.Windows.Count == 1, "Invalid entry mutated the draft or opened a message box.");
            Capture(window, Path.Combine(args[0], "material-required.png"));
            Type(part, "   ");
            Check(!Validation.GetHasError(Frame(part)), "Editing did not clear the old error.");
            Click(window, "Add_To_Prt_List");
            Error(part, "اسم المادة");
            Type(part, "مادة اختبار");
            var quantity = Find<SpinEdit>(window, "Text_Quantity");
            quantity.EditValue = 0m;
            Click(window, "Add_To_Prt_List");
            Error(quantity, "أكبر من صفر");
            quantity.EditValue = 2.5m;
            Check(!Validation.GetHasError(Frame(quantity)), "Corrected quantity retained its error.");
            Click(window, "Add_To_Prt_List");
            Error(Find<ComboBoxEdit>(window, "Combo_Units"), "الوحدة");
            Choose(Find<ComboBoxEdit>(window, "Combo_Units"), "قطعة");
            Click(window, "Add_To_Prt_List");
            Error(Find<SpinEdit>(window, "Text_Value"), "سعر");
            Find<SpinEdit>(window, "Text_Value").EditValue = 0m;
            Click(window, "Add_To_Prt_List");
            Check(window.State.Parts.Count == 1, "A valid zero-priced material was rejected.");
            window.EditMaterial(window.State.Parts[0]);
            Find<SpinEdit>(window, "Text_Quantity_Update").EditValue = 0m;
            Click(window, "Update_Parts_Button");
            Error(Find<SpinEdit>(window, "Text_Quantity_Update"), "أكبر من صفر");
            Check(window.State.Parts[0].Quantity == 2.5m, "Invalid edit changed the original row.");
            Find<SpinEdit>(window, "Text_Quantity_Update").EditValue = 3m;
            Click(window, "Update_Parts_Button");
            Check(window.State.Parts[0].Quantity == 3m, "Corrected material edit failed.");
            Click(window, "clear_Button");
            Check(!Descendants(window).OfType<Border>().Any(Validation.GetHasError), "Clear retained validation errors.");

            Check(ReadDraft(window) == null, "A missing building was accepted.");
            Error(Find<ComboBoxEdit>(window, "Combo_Buildings"), "المبنى");
            var state = window.State;
            var building = new T_Build { Build_ID = 1, Build = "مبنى اختبار", Location = "اختبار" };
            var employee = new T_Employe { Employe_ID = 1, Emp_Name = "فني اختبار", Location = "اختبار" };
            var service = new T_Service { Serv_num = 1, Serv_type = "خدمة اختبار", Location = "اختبار" };
            var manage = new T_Manage { Manag_ID = 1, Manag = "إدارة اختبار", Location = "اختبار" };
            var section = new T_Section { Secti_ID = 1, Manag_ID = 1, Secti = "قسم اختبار", Location = "اختبار" };
            state.Buildings.Add(building); state.Employees.Add(employee); state.Services.Add(service);
            state.Managements.Add(manage); state.AllDepartments.Add(section); state.FilterDepartments(manage);
            Choose(Find<ComboBoxEdit>(window, "Combo_Buildings"), building);
            var date = Find<DateEdit>(window, "date_check");
            date.EditValue = null;
            Check(ReadDraft(window) == null, "Missing date was accepted."); Error(date, "تاريخ");
            date.EditValue = DateTime.Today.AddDays(1);
            Check(ReadDraft(window) == null, "Future date was accepted."); Error(date, "المستقبل");
            date.EditValue = DateTime.Today;
            Check(ReadDraft(window) == null, "Missing inspector was accepted."); Error(Find<ComboBoxEdit>(window, "Combo_Check_Man"), "الفاحص");
            Find<ComboBoxEdit>(window, "Combo_Check_Man").EditValue = employee;
            Check(ReadDraft(window) == null, "Missing document type was accepted."); Error(Find<ComboBoxEdit>(window, "Doc_Type"), "نوع التكليف");
            Find<ComboBoxEdit>(window, "Doc_Type").EditValue = "طارئة";
            Check(ReadDraft(window) == null, "Missing service was accepted."); Error(Find<ComboBoxEdit>(window, "Combo_Services"), "الخدمة");
            Find<ComboBoxEdit>(window, "Combo_Services").EditValue = service;
            Check(ReadDraft(window) == null, "Missing technicians were accepted."); Error(Find<ComboBoxEdit>(window, "Combo_Technical_Man"), "فنيًا");
            Choose(Find<ComboBoxEdit>(window, "Combo_Technical_Man"), employee, multiple: true);
            Check(ReadDraft(window) == null, "An order with no repair lines was accepted."); Error(Find<TextEdit>(window, "Text_dtils"), "بيان صيانة");
            Find<ComboBoxEdit>(window, "Combo_Management").EditValue = manage;
            Find<ComboBoxEdit>(window, "Combo_Department").EditValue = section;
            Find<ComboBoxEdit>(window, "Combo_Repier_type").EditValue = "صيانة";
            Find<TextEdit>(window, "Text_dtils").EditValue = "بيان اختبار";
            Click(window, "Add_ToList");
            Check(ReadDraft(window) is MaintenanceOrderDraft, "Valid header and repair were rejected.");
            Check(!Descendants(window).OfType<Border>().Any(Validation.GetHasError), "Valid draft retained errors.");

            var search = Find<TextEdit>(window, "Text_emng_Search");
            Type(search, "رقم غير صحيح");
            search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
            Pump();
            Error(search, "رقم تكليف");
            Check(Find<FrameworkElement>(window, "mybo").IsEnabled && app.Windows.Count == 1, "Invalid Enter search disabled the UI or opened a dialog.");
            Capture(window, Path.Combine(args[0], "number-invalid.png"));
            Type(search, string.Empty);
            Click(window, "search_button");
            Find<CheckBox>(window, "By_Main").IsChecked = true;
            Click(window, "Search_Advance_Button");
            Error(Find<DateEdit>(window, "from_date"), "فترة تاريخ");
            Check(app.Windows.Count == 1 && window.State.Parts.Count == 0, "Search validation opened a dialog or changed data.");
            Maintenance_app.App.ConnectionString = string.Empty;
            Click(window, "clear_Button");
            Check(!Descendants(window).OfType<Border>().Any(Validation.GetHasError), "Reset left a search error.");
            window.Close(); app.Shutdown();
            Console.WriteLine("PASS: " + checks + " live WPF inline-validation checks; required inputs, typing, focus, red adorners, decimal rules, header/date/technicians, edit, Enter search, date search and reset. No database access or writes.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Maintenance_app.App.ConnectionString = string.Empty;
        }
    }

    #endregion
}
