using MahApps.Metro.Controls;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Grid;
using MahApps.Metro.IconPacks;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using Maintenance_app.Notifications;
using System.Xml.Linq;
using System.Windows.Markup;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Size = System.Windows.Size;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

internal static class CompletionProbe
{
    #region أدوات فحص نافذة WPF الفعلية
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static T Find<T>(Window window, string name) where T : class => (T)window.FindName(name);
    private static void Click(Window window, string name) => Find<FrameworkElement>(window, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static Border Field(BaseEdit editor) => (Border)((FrameworkElement)editor.Parent).Parent;
    private static Rect Bounds(Window window, string name) => Find<FrameworkElement>(window, name).TransformToAncestor(window).TransformBounds(new Rect(Find<FrameworkElement>(window, name).RenderSize));
    private static void Layout(Window window, Size size)
    {
        window.Width = size.Width; window.Height = size.Height;
        Pump(); var root = (FrameworkElement)window.Content;
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); Pump();
    }
    private static void Render(FrameworkElement root, string path)
    {
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    // لقطة غير مرتبطة بمقبض HWND تمنع انعكاس نص RTL عند التصدير من نافذة خارج الشاشة.
    private static void RenderWindow(maintenance_complete window, Size size, string path)
    {
        var snapshot = new maintenance_complete { WindowState = WindowState.Normal, DataContext = window.DataContext };
        foreach (var field in typeof(maintenance_complete).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            if (field.GetValue(window) is BaseEdit original && field.GetValue(snapshot) is BaseEdit copy) copy.EditValue = original.EditValue;
        foreach (string name in new[] { "By_Main", "By_Emp", "By_Service", "By_State" })
            Find<System.Windows.Controls.CheckBox>(snapshot, name).IsChecked = Find<System.Windows.Controls.CheckBox>(window, name).IsChecked;
        Find<Flyout>(snapshot, "adv_searsh").IsOpen = Find<Flyout>(window, "adv_searsh").IsOpen;
        foreach (string name in new[] { "Insert", "Edite_button", "delete_button", "Print_Button" }) Find<FrameworkElement>(snapshot, name).IsEnabled = Find<FrameworkElement>(window, name).IsEnabled;
        Layout(snapshot, size); Render((FrameworkElement)snapshot.Content, path); snapshot.Close();
    }
    private static void CheckPopup(Window window, string name, string[] values, string output)
        => CheckPopup(Find<ComboBoxEdit>(window, name), name, values, output);
    private static void CheckPopup(ComboBoxEdit editor, string name, string[] values, string output)
    {
        editor.ShowPopup(); Pump(); Check(editor.IsPopupOpen && editor.PopupRoot is not null, name + ": popup missing.");
        var icons = Descendants(editor.PopupRoot!).OfType<PackIconMaterial>().Where(x => x.ActualWidth >= 16).ToList();
        Check(icons.Count >= values.Length, name + ": option icons missing; got " + icons.Count);
        var text = Descendants(editor.PopupRoot!).OfType<TextBlock>().Select(x => x.Text).ToList();
        foreach (var value in values) Check(text.Contains(value), name + ": option text missing: " + value);
        Render((FrameworkElement)editor.PopupRoot!, Path.Combine(output, name + "-popup.png"));
        editor.ClosePopup(); Pump();
        for (int i = 0; i < values.Length; i++)
        {
            editor.EditValue = values[i]; Pump();
            Check(editor.SelectedIndex == i && Equals(editor.SelectedItem, values[i]), name + ": value/index mapping changed.");
            editor.ShowPopup(); Pump(); editor.ClosePopup(); Pump();
            Check(Equals(editor.EditValue, values[i]), name + ": selection was lost on reopen.");
        }
    }
    #endregion

    #region سيناريوهات التخطيط والتحقق والخيارات الثابتة
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string output = args.Length > 0 ? args[0] : "artifacts/completion-preview"; Directory.CreateDirectory(output);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-LY");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            foreach (string dictionary in new[] { "Styles/Controls.xaml", "Styles/Fonts.xaml", "Styles/Themes/Light.Steel.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MahApps.Metro;component/" + dictionary) });
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            Maintenance_app.App.ConnectionString = string.Empty;
            Maintenance_app.App.appuser = new T_SystemUser { User_ID = 7, fordon = true };
            var window = new maintenance_complete();
            Check(window.WindowState == WindowState.Maximized && window.ResizeMode == ResizeMode.CanResize && window.AllowsTransparency, "Window chrome/resize differs from standards.");
            Check(window.Width == 1920 && window.Height == 1080, "Full HD defaults missing.");
            window.WindowState = WindowState.Normal; window.ShowActivated = false; window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -30000; window.Top = -30000; window.Show(); Pump();
            var date = Find<DateEdit>(window, "date_check"); var status = Find<ComboBoxEdit>(window, "Doc_Type"); var observer = Find<TextEdit>(window, "Text_man");
            Check(!Validation.GetHasError(Field(date)) && !Validation.GetHasError(Field(status)), "Errors shown before command.");
            Check(window.ReadDraft() is null && Validation.GetHasError(Field(Find<TextEdit>(window, "Text_emng_Search"))), "Missing order must block save.");
            var order = new T_Emergency_MaintenanceCost
            {
                Location = "موقع المعاينة", Location_ID = 1, Branch_ID = 1, Emergency_MaintenanceCosts_No = 7202600001,
                Date_com = DateOnly.FromDateTime(DateTime.Today), Type = 2, T_Build = new T_Build { Build = "المبنى الإداري" },
                T_Service = new T_Service { Serv_type = "الكهرباء" },
                T_Emp_Maints = [new() { T_Employe = new T_Employe { Emp_Name = "الفني الأول" } }, new() { T_Employe = new T_Employe { Emp_Name = "الفني الثاني" } }],
                T_Emergency_MaintenanceCosts_Services = [new() { ID = 1, T_Manage = new T_Manage { Manag = "إدارة الخدمات" },
                    T_Section = new T_Section { Secti = "قسم الصيانة" }, Maint_Type = 0, Statement = "فحص لوحة الكهرباء واستبدال القاطع التالف بالمبنى الإداري" }]
            };
            window.DisplayDocument(new(order, null)); Pump();
            Check(window.State.Repairs.Count == 1 && window.State.Technicians.Contains("الفني الثاني"), "Original detail fields missing.");
            Check(!Validation.GetHasError(Field(Find<TextEdit>(window, "Text_emng_Search"))), "Loading must clear validation.");
            Check(window.ReadDraft() is null && Validation.GetHasError(Field(date)), "Required date not validated.");
            date.EditValue = DateTime.Today.AddDays(1); Pump(); Check(!Validation.GetHasError(Field(date)), "Date change did not clear error.");
            Check(window.ReadDraft() is null && Validation.GetHasError(Field(date)), "Future date accepted.");
            date.EditValue = DateTime.Today; Pump();
            Check(window.ReadDraft() is null && Validation.GetHasError(Field(status)), "Required status not validated.");
            status.ShowPopup(); Pump(); Check(Validation.GetHasError(Field(status)), "Opening alone cleared required error.");
            var list = Descendants(status.PopupRoot!).OfType<System.Windows.Controls.ListBox>().First();
            list.SelectedIndex = 2; Pump(); Check(!Validation.GetHasError(Field(status)), "Selection inside open popup did not clear error.");
            Check(status.IsPopupOpen, "Popup closed before immediate-clear check."); status.ClosePopup(); Pump();
            Check(window.ReadDraft() is null && Validation.GetHasError(Field(observer)), "Required observer not validated.");
            observer.Focus();
            var composition = new TextComposition(InputManager.Current, observer, "س");
            observer.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
            Check(!Validation.GetHasError(Field(observer)), "Typing did not immediately clear observer error.");
            observer.EditValue = "  "; Check(window.ReadDraft() is null, "Whitespace observer accepted.");
            observer.EditValue = "موظف المعاينة"; Find<TextEdit>(window, "Text_notes").EditValue = "تم فحص الموقع، والعمل معلق لحين توفير قطع الغيار.";
            Check(window.ReadDraft() is { Status: 2, Observer: "موظف المعاينة" }, "Draft code/text mismatch.");
            CheckPopup(window, "Doc_Type", window.State.CompletionStatuses, output);
            status.EditValue = "معلقة";
            foreach (var size in new[] { new Size(1000, 700), new Size(1366, 768), new Size(1536, 864), new Size(1920, 1040), new Size(1920, 1080) })
            {
                Layout(window, size);
                foreach (string name in new[] { "txt_maint_no", "Combo_Buildings", "emp_text", "date_check", "Doc_Type", "Text_man", "Text_notes", "Repiers_Grid", "Text_emng_Search", "Insert", "But_Cancl" })
                {
                    var rect = Bounds(window, name);
                    Check(rect.Width >= 35 && rect.Height >= 25, name + " unusable at " + size);
                    Check(rect.Left >= -1 && rect.Top >= -1 && rect.Right <= size.Width + 1 && rect.Bottom <= size.Height + 1, name + " out of bounds at " + size + ": " + rect);
                }
                Check(!Bounds(window, "ReportCard").IntersectsWith(Bounds(window, "RepairsCard")), "Report overlaps detail grid.");
                Check(Find<GridControl>(window, "Repiers_Grid").ActualHeight >= 70, "Grid collapsed at " + size);
                RenderWindow(window, size, Path.Combine(output, $"completion-{size.Width}x{size.Height}.png"));
            }
            // لوحة البحث والحالة الخامسة: تكليف لا يملك تقريرًا.
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.F3) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); Pump();
            Check(Find<Flyout>(window, "adv_searsh").IsOpen, "Advanced search shortcut not wired.");
            // تفعيل كل فلتر ملاصق لخانته، مع ربط طرفي فترة التاريخ دون تغيير شروط البحث.
            foreach (var pair in new[] { ("By_Main", "from_date"), ("By_Main_To", "To_date"), ("By_Emp", "Combo_emp_Search"), ("By_Service", "Combo_Services_Search"), ("By_State", "Doc_Type_Searsh") })
            {
                var toggle = Find<System.Windows.Controls.CheckBox>(window, pair.Item1);
                var editor = Find<BaseEdit>(window, pair.Item2);
                Check(ReferenceEquals(toggle.Parent, Field(editor).Parent), "Filter toggle is detached from its field: " + pair.Item1);
                var toggleBounds = Bounds(window, pair.Item1); var editorBounds = Bounds(window, pair.Item2);
                Check(Math.Abs(toggleBounds.Top + toggleBounds.Height / 2 - editorBounds.Top - editorBounds.Height / 2) < 2,
                    "Filter activation must sit beside its editor: " + pair.Item1);
                toggle.IsChecked = true; Pump(); Check(editor.IsEnabled, "Filter did not enable its own field: " + pair.Item1);
                toggle.IsChecked = false; Pump(); Check(!editor.IsEnabled, "Filter did not disable its own field: " + pair.Item1);
            }
            Find<System.Windows.Controls.CheckBox>(window, "By_Main_To").IsChecked = true; Pump();
            Check(Find<System.Windows.Controls.CheckBox>(window, "By_Main").IsChecked == true && Find<DateEdit>(window, "from_date").IsEnabled,
                "Date range toggles lost shared activation.");
            Find<System.Windows.Controls.CheckBox>(window, "By_Main").IsChecked = false;
            Find<System.Windows.Controls.CheckBox>(window, "By_State").IsChecked = true; Pump();
            CheckPopup(window, "Doc_Type_Searsh", window.State.SearchStatuses, output);
            foreach (var size in new[] { new Size(1000, 700), new Size(1920, 1080) })
            {
                Layout(window, size);
                foreach (string name in new[] { "from_date", "Combo_emp_Search", "Doc_Type_Searsh", "Search_Grid", "OpenSearchResult" })
                    Check(Bounds(window, name).Bottom <= size.Height && Bounds(window, name).Right <= size.Width, "Search control cropped: " + name);
                RenderWindow(window, size, Path.Combine(output, $"completion-search-{size.Width}.png"));
            }
            Click(window, "CancelSearch");
            window.DisplayDocument(new(order, new T_FinishedMaintenance { ID = 12, DateM = order.Date_com, State = 1, observer = "موظف محفوظ", notes = "تقرير سابق" })); Pump();
            Check(!Find<FrameworkElement>(window, "Insert").IsEnabled && Find<FrameworkElement>(window, "Edite_button").IsEnabled && observer.Text == "موظف محفوظ", "Saved report mode incorrect.");
            Click(window, "But_Cancl"); Pump();
            Check(window.State.Repairs.Count == 0 && date.EditValue is null && status.EditValue is null && string.IsNullOrEmpty(observer.Text), "Clear retained old report values.");
            Check(!Validation.GetHasError(Field(observer)), "Clear retained validation."); window.Close();

            // نفس القالب في قوائم التكليف الأساسية ولوحتي التعديل، مع ثبات ترتيب القيم.
            var orderWindow = new maintenance_Order { WindowState = WindowState.Normal, Width = 1366, Height = 768, ShowActivated = false,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
            orderWindow.Show(); Pump(); Layout(orderWindow, new Size(1366, 768));
            CheckPopup(orderWindow, "Doc_Type", orderWindow.State.DocumentTypes, output);
            CheckPopup(orderWindow, "Combo_Repier_type", orderWindow.State.RepairActions, output);
            CheckPopup(orderWindow, "Combo_Units", orderWindow.State.Units, output);
            Find<Flyout>(orderWindow, "Config_Service_Windows").IsOpen = true; Pump();
            CheckPopup(orderWindow, "Combo_Repier_type_Update", orderWindow.State.RepairActions, output);
            Find<Flyout>(orderWindow, "Config_Service_Windows").IsOpen = false;
            Find<Flyout>(orderWindow, "Config_Parts_Windows").IsOpen = true; Pump();
            CheckPopup(orderWindow, "Combo_Units_Update", orderWindow.State.Units, output);
            orderWindow.Close();
            // قائمة التصفية الثابتة في مركز الإشعارات تستخدم القالب نفسه وفهارسها السابقة.
            var notices = new MaintenanceNotificationState();
            var panel = new NotificationCenter { DataContext = notices };
            var noticeHost = new Window { Content = panel, Width = 850, Height = 900, ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
            noticeHost.Show(); Pump();
            CheckPopup((ComboBoxEdit)panel.FindName("NoticeFilter"), "NoticeFilter", notices.Filters, output); noticeHost.Close();

            CheckMainLaunch(app);
            Console.WriteLine($"PASS: {checks} native WPF completion/layout/validation/static-choice checks; no database connection."); app.Shutdown(); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion

    #region فتح الشاشة من الرئيسية بموارد التطبيق الحقيقية
    private static void CheckMainLaunch(Application app)
    {
        // تحميل القواميس نفسها المستخدمة في التطبيق، دون تشغيل حدث الدخول أو قراءة ملف الاتصال.
        var source = XDocument.Load(Path.Combine(Environment.CurrentDirectory, "Maintenance_app", "App.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = source.Root!.Element(p + "Application.Resources")!.Element(p + "ResourceDictionary")!;
        foreach (var attribute in source.Root.Attributes().Where(a => a.IsNamespaceDeclaration))
        {
            string value = attribute.Value;
            if (value.StartsWith("clr-namespace:Maintenance_app") && !value.Contains(";assembly=")) value += ";assembly=Maintenance_app";
            resources.SetAttributeValue(attribute.Name, value);
        }
        foreach (var dictionary in resources.Descendants(p + "ResourceDictionary"))
            if (dictionary.Attribute("Source") is { } location && !location.Value.StartsWith("pack:"))
                location.Value = "pack://application:,,,/Maintenance_app;component/" + location.Value;
        string resourceText = System.Text.RegularExpressions.Regex.Replace(resources.ToString(),
            "clr-namespace:Maintenance_app(?:\\.[^;\"\\s]+)?(?=\")", match => match.Value + ";assembly=Maintenance_app");
        app.Resources = (ResourceDictionary)XamlReader.Parse(resourceText);
        var main = new Maintenance_app.MainWindow { ShowInTaskbar = false, ShowActivated = false, WindowState = WindowState.Normal,
            Width = 1366, Height = 768, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
        main.Show(); Pump();
        var menu = Find<System.Windows.Controls.MenuItem>(main, "CompletionMenu");
        var buttons = Descendants(main).OfType<SimpleButton>().Where(x => x.DataContext is Maintenance_app.MainWindow.WorkspaceAction a && a.Target == menu).ToArray();
        var command = Maintenance_app.MainWindow.OpenMaintenanceCompletionCommand;
        Check(buttons.Length == 2, "Completion must appear in main actions and quick access.");
        foreach (var button in buttons)
        {
            var point = button.TransformToAncestor(main).Transform(new System.Windows.Point(button.ActualWidth / 2, button.ActualHeight / 2));
            bool reachable = false;
            for (DependencyObject? hit = main.InputHitTest(point) as DependencyObject; hit is not null; hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : null)
                if (ReferenceEquals(hit, button)) { reachable = true; break; }
            Check(reachable && button.IsEnabled, "Completion action is covered or disabled in the main window.");
        }
        Check(command.InputGestures.OfType<KeyGesture>().Any(x => x.Key == Key.F5), "Completion F5 shortcut missing.");
        menu.IsEnabled = false; Check(!command.CanExecute(null, main), "Disabled menu bypassed by shortcut."); menu.IsEnabled = true;
        Maintenance_app.App.appuser.fordon = false; Check(!command.CanExecute(null, main), "Revoked permission bypassed by shortcut."); Maintenance_app.App.appuser.fordon = true;
        var launchers = buttons.Select(button => (Action)(() => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)))).ToList();
        launchers.Add(() => command.Execute(null, main));
        foreach (var launch in launchers)
        {
            bool opened = false; int ticks = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) =>
            {
                if (app.Windows.OfType<maintenance_complete>().FirstOrDefault(x => x.IsVisible) is { } child)
                {
                    Check(child.Owner == main && child.WindowState == WindowState.Maximized, "Completion dialog owner/maximize incorrect.");
                    Check(Find<GridControl>(child, "Repiers_Grid").ActualWidth > 700, "Completion content failed under actual application resources.");
                    opened = true; child.Close(); timer.Stop();
                }
                if (++ticks > 40) { timer.Stop(); throw new Exception("Completion route did not open dialog."); }
            };
            timer.Start(); launch(); Pump(); timer.Stop(); Check(opened, "Completion launcher not wired.");
        }
        // إغلاق نافذة الاختبار فقط، دون مطالبة تأكيد تخص جلسة عمل المستخدم.
        typeof(Maintenance_app.MainWindow).GetField("confirmedClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(main, true);
        main.Close();
    }
    #endregion
}
