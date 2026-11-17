using MahApps.Metro.Controls;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Grid;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Size = System.Windows.Size;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Microsoft.Win32;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Maintenance_app.dbContext;

internal static class Program
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static T Find<T>(Window window, string name) where T : class => (T)window.FindName(name);
    private static void Click(Window window, string name) =>
        Find<FrameworkElement>(window, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Layout(Window window, Size size)
    {
        window.Width = size.Width;
        window.Height = size.Height;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        var content = (FrameworkElement)window.Content;
        content.Measure(size);
        content.Arrange(new Rect(size));
        content.UpdateLayout();
        content.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        content.UpdateLayout();
    }
    private static void Render(Window window, string output, string name, Size? viewport = null)
    {
        var size = viewport ?? new Size(1920, 1080);
        // Use an unshown snapshot for export. This avoids HWND mirroring and monitor
        // DPI transforms while the separate native-window checks still verify resizing.
        var snapshot = new maintenance_Order { DataContext = window.DataContext };
        foreach (var field in typeof(maintenance_Order).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (field.GetValue(window) is BaseEdit original && field.GetValue(snapshot) is BaseEdit copy)
                copy.EditValue = original.EditValue;
        foreach (string nameToCopy in new[] { "By_Main", "By_Emp", "By_Service" })
            Find<System.Windows.Controls.CheckBox>(snapshot, nameToCopy).IsChecked =
                Find<System.Windows.Controls.CheckBox>(window, nameToCopy).IsChecked;
        foreach (string nameToCopy in new[] { "Config_Service_Windows", "Config_Parts_Windows", "adv_searsh" })
            Find<Flyout>(snapshot, nameToCopy).IsOpen = Find<Flyout>(window, nameToCopy).IsOpen;
        Layout(snapshot, size);
        var target = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render((Visual)snapshot.Content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(file);
        snapshot.Close();
    }
    private static void CheckBounds(Window window, string name, Size size)
    {
        var field = Find<FrameworkElement>(window, name);
        var content = (Visual)window.Content;
        var rect = field.TransformToAncestor(content).TransformBounds(new Rect(field.RenderSize));
        Require(field.ActualWidth >= 24 && field.ActualHeight >= 25, name + " has no usable space.");
        Require(rect.Left >= -1 && rect.Top >= -1 && rect.Right <= size.Width + 1 && rect.Bottom <= size.Height + 1,
            name + " extends outside the window at " + size);
    }
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
            var window = new maintenance_Order();
            Require(window.WindowState == WindowState.Maximized && window.ResizeMode == ResizeMode.CanResize,
                "The window must start maximized and remain resizable.");
            Require(window.Width == 1920 && window.Height == 1080, "Full HD base dimensions are missing.");
            Require(window.FindName("tb1") is null && window.FindName("tp2") is null && window.FindName("tpg1") is null,
                "Legacy tabs have not been removed.");
            // Use a real HWND off-screen so DevExpress also runs its loaded/resize layout.
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.WindowState = WindowState.Normal;
            window.Left = -30000;
            window.Top = -30000;
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Directory.CreateDirectory(args[0]);
            var state = window.State;
            var management = new T_Manage { Manag_ID = 1, Manag = "إدارة الخدمات", Location = "معاينة" };
            var department = new T_Section { Secti_ID = 1, Manag_ID = 1, Secti = "قسم الصيانة", Location = "معاينة" };
            state.Managements.Add(management);
            state.AllDepartments.Add(department);
            state.FilterDepartments(management);
            state.Buildings.Add(new T_Build { Build_ID = 1, Build = "المبنى الإداري" });
            state.Services.Add(new T_Service { Serv_num = 1, Serv_type = "الكهرباء" });
            state.Employees.Add(new T_Employe { Employe_ID = 1, Emp_Name = "فني المعاينة الأول" });
            state.Employees.Add(new T_Employe { Employe_ID = 2, Emp_Name = "فني المعاينة الثاني" });
            Find<ComboBoxEdit>(window, "Combo_Buildings").EditValue = state.Buildings[0];
            Find<ComboBoxEdit>(window, "Combo_Check_Man").EditValue = state.Employees[0];
            Find<ComboBoxEdit>(window, "Combo_Services").EditValue = state.Services[0];
            Find<ComboBoxEdit>(window, "Doc_Type").EditValue = "طارئة";
            Find<ComboBoxEdit>(window, "Combo_Technical_Man").EditValue = new List<object> { 1, 2 };
            Find<ComboBoxEdit>(window, "Combo_Management").EditValue = management;
            Find<ComboBoxEdit>(window, "Combo_Department").EditValue = department;
            Find<ComboBoxEdit>(window, "Combo_Repier_type").EditValue = "صيانة";
            Find<TextEdit>(window, "Text_dtils").EditValue = "فحص اللوحة الكهربائية واستبدال القاطع التالف";
            Layout(window, new Size(1000, 800));
            var orderDate = Find<DateEdit>(window, "date_check");
            Require(orderDate.EditValue is DateTime, "The default order date is missing.");
            Require(Find<ComboBoxEdit>(window, "Combo_Technical_Man").StyleSettings is CheckedTokenComboBoxStyleSettings,
                "Technicians are not a DevExpress token selection.");
            Click(window, "Add_ToList");
            Require(state.Repairs.Count == 1, "The repair button did not add the draft line.");
            Click(window, "Add_ToList");
            Require(state.Repairs.Count == 1 && Find<FrameworkElement>(window, "mybo").IsEnabled,
                "A rejected repair entry changed the draft or left the window disabled.");
            foreach (var size in new[] { new Size(1000,700), new Size(1366,768), new Size(1536,864), new Size(1920,1040), new Size(1920,1080) })
            {
                Layout(window, size);
                foreach (string name in new[] { "date_check", "Combo_Buildings", "Combo_Check_Man", "Combo_Technical_Man",
                    "Combo_Services", "Combo_Management", "Combo_Department", "Text_dtils", "Repiers_Grid", "Insert", "Text_emng_Search",
                    "Text_Parts", "Text_Quantity", "Text_Value", "Combo_Units", "Parts_Grid", "Total_Part" })
                    CheckBounds(window, name, size);
                var repairs = Bounds(window, "RepairPanel");
                var materials = Bounds(window, "MaterialsPanel");
                var actions = Bounds(window, "OrderActionsCard");
                Require(repairs.Bottom <= materials.Top + 1, "Repair and material sections overlap.");
                Require(materials.Bottom <= actions.Top + 1, "Materials overlap the footer.");
                Require(Find<GridControl>(window, "Repiers_Grid").ActualHeight >= 64
                    && Find<GridControl>(window, "Parts_Grid").ActualHeight >= 64,
                    "Both grids must show a header and at least one row at " + size + "; repairs="
                    + Find<GridControl>(window, "Repiers_Grid").ActualHeight + "; materials="
                    + Find<GridControl>(window, "Parts_Grid").ActualHeight);
                Require(!Bounds(window, "OrderTitle").IntersectsWith(Bounds(window, "WindowActions")), "Title and window buttons overlap.");
                foreach (string name in new[] { "Repiers_Grid", "Parts_Grid" })
                {
                    var table = Find<GridControl>(window, name);
                    Require(table.Columns.Sum(c => c.ActualWidth) >= table.ActualWidth - 45,
                        name + " leaves unused width instead of filling the screen: viewport=" + size
                            + ", grid=" + table.ActualWidth + ", columns=" + table.Columns.Sum(c => c.ActualWidth)
                            + ", AutoWidth=" + ((TableView)table.View).AutoWidth);
                }
            }
            foreach (var column in Find<GridControl>(window, "Repiers_Grid").Columns.Where(c => !string.IsNullOrEmpty(c.FieldName)))
                Require(column.ActualWidth >= 70, "Repair column collapsed: " + column.FieldName);
            Find<TextEdit>(window, "Text_Parts").EditValue = "قاطع كهربائي";
            Find<SpinEdit>(window, "Text_Quantity").EditValue = 2.5m;
            Find<ComboBoxEdit>(window, "Combo_Units").EditValue = "قطعة";
            Find<SpinEdit>(window, "Text_Value").EditValue = 12.345m;
            Click(window, "Add_To_Prt_List");
            Require(state.Parts.Count == 1 && state.TotalParts == 30.8625m, "The material button or decimal total is incorrect.");
            Click(window, "Add_To_Prt_List");
            Require(state.Parts.Count == 1 && Find<FrameworkElement>(window, "mybo").IsEnabled,
                "A rejected material entry changed the draft or left the window disabled.");
            foreach (var size in new[] { new Size(1000,700), new Size(1366,768), new Size(1920,1080) })
            {
                Layout(window, size);
                foreach (string name in new[] { "Text_Parts", "Text_Quantity", "Text_Value", "Combo_Units", "Parts_Grid", "Total_Part" })
                    CheckBounds(window, name, size);
            }
            Render(window, args[0], "maintenance-order-preview");
            Render(window, args[0], "maintenance-order-materials");
            Render(window, args[0], "maintenance-order-compact", new Size(1366,768));
            Render(window, args[0], "maintenance-order-workarea", new Size(1920,1040));
            window.EditMaterial(state.Parts[0]);
            Render(window, args[0], "maintenance-order-material-edit");
            Find<SpinEdit>(window, "Text_Quantity_Update").EditValue = 3m;
            Click(window, "Update_Parts_Button");
            Require(state.TotalParts == 37.035m, "Editing materials did not refresh the total.");
            window.EditRepair(state.Repairs[0]);
            Render(window, args[0], "maintenance-order-repair-edit");
            Find<TextEdit>(window, "Text_dtils_Update").EditValue = "تم تحديث تفاصيل المعاينة";
            Click(window, "Update_Button");
            Require(state.Repairs[0].Details == "تم تحديث تفاصيل المعاينة", "Repair edit failed.");
            Click(window, "search_button");
            state.SearchResults.Add(new(101, DateTime.Today, "المبنى الإداري", "فني المعاينة الأول", "الكهرباء", "معاينة", 0, 0));
            state.SearchStatus = "عدد النتائج: 1";
            Render(window, args[0], "maintenance-order-search");
            foreach (var column in Find<GridControl>(window, "Search_Grid").Columns)
                Require(column.ActualWidth >= 70, "Search column collapsed: " + column.FieldName);
            Require(Find<GridControl>(window, "Search_Grid").Columns.Count == 5, "Search columns missing.");
            HeaderFocusRegression.Run(window);
            Require(!Find<SimpleButton>(window, "Insert").IsEnabled, "Unconfigured database write action is enabled.");
            Click(window, "clear_Button");
            Require(state.Parts.Count == 0 && state.Repairs.Count == 0 && state.TotalParts == 0m, "Clear did not reset the draft.");
            HeaderFocusRegression.Run(window);
            if (args.Length > 1) VerifyLiveRead(window, args[1]);
            window.Close();
            app.Shutdown();
            Console.WriteLine("PASS: Full HD/maximized/resizable; no tabs; 5 viewport sizes; both sections visible without overlap; DevExpress grids and token selector; add/edit/reset; decimal totals; search overlay. No database writes.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static Rect Bounds(Window window, string name)
    {
        var element = Find<FrameworkElement>(window, name);
        return element.TransformToAncestor((Visual)window.Content).TransformBounds(new Rect(element.RenderSize));
    }
    private static void VerifyLiveRead(maintenance_Order window, string connectionFile)
    {
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
            Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
        const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SSF\Maintenance_services\";
        using var user = registry.OpenSubKey(path + "NameUserSQL");
        using var password = registry.OpenSubKey(path + "PassUserSQL");
        string Decode(object? value) => value is string text ? Encoding.UTF8.GetString(Convert.FromBase64String(text))
            : throw new InvalidOperationException("Missing connection configuration.");
        var create = typeof(Maintenance_app.App).Assembly.GetType("Maintenance_app.DatabContext.SqlConnectionSettings")!
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        Maintenance_app.App.ConnectionString = (string)create.Invoke(null,
            [File.ReadAllText(connectionFile), Decode(user?.GetValue("NameUserSQL")), Decode(password?.GetValue("PassUserSQL"))])!;
        // Pump only this plain Application's dispatcher so WPF continuations can complete.
        void Wait(Task task)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            task.GetAwaiter().GetResult();
        }
        Wait(window.LoadLookupsAsync());
        Find<TextEdit>(window, "Text_emng_Search").EditValue = long.MaxValue.ToString(CultureInfo.InvariantCulture);
        Wait(window.SearchAsync());
        Require(window.State.SearchStatus == "لا توجد نتائج مطابقة.", "Live search failed: " + window.State.SearchStatus);
        using var db = new Maintenance_DabaseContext(new DbContextOptionsBuilder<Maintenance_DabaseContext>()
            .UseSqlServer(Maintenance_app.App.ConnectionString).Options);
        var sampleTask = db.T_Emergency_MaintenanceCosts.AsNoTracking().Where(x => x.Emergency_MaintenanceCosts_ST == false && !x.T_FinishedMaintenances.Any())
            .OrderBy(x => x.Emergency_MaintenanceCosts_No)
            .Select(x => new OrderSearchRow(x.Emergency_MaintenanceCosts_No, DateTime.MinValue,
                string.Empty, string.Empty, string.Empty, x.Location, x.Location_ID, x.Branch_ID)).FirstOrDefaultAsync();
        Wait(sampleTask);
        if (sampleTask.Result is OrderSearchRow sample)
        {
            Wait(window.LoadOrderAsync(sample));
            Require(Convert.ToInt64(Find<TextEdit>(window, "txt_maint_no").EditValue) == sample.Number, "Record load failed.");
        }
        Console.WriteLine("PASS: live lookup loading, search projection, and order loading by all composite-key fields (read-only; no connection details printed).");
        Maintenance_app.App.ConnectionString = string.Empty;
    }
}
