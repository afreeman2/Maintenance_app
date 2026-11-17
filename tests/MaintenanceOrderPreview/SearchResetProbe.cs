using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Grid;
using MahApps.Metro.Controls;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using CheckBox = System.Windows.Controls.CheckBox;
using Control = System.Windows.Controls.Control;

internal static class SearchResetProbe
{
    #region أدوات فحص البحث الفعلي وقاعدة الاختبار المعزولة
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static T Find<T>(FrameworkElement owner, string name) where T : class => (T)owner.FindName(name);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static void Click(FrameworkElement button) { button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(); }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Show(MetroWindow window)
    {
        typeof(maintenance_Order).Assembly.GetType(window.GetType().FullName!)!.GetField("loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        window.WindowState = WindowState.Normal; window.Width = 1366; window.Height = 768;
        window.Left = window.Top = -30000; window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowInTaskbar = window.ShowActivated = false; window.Show(); Pump();
    }
    private static string[] Fields(bool complete) => complete
        ? ["from_date", "To_date", "Combo_emp_Search", "Combo_Services_Search", "Doc_Type_Searsh"]
        : ["from_date", "To_date", "Combo_emp_Search", "Combo_Services_Search"];
    // تعبئة فلاتر اللوحة دون المساس بحقوق السجل المعروض أو قوائم الخيارات.
    private static void Fill(MetroWindow owner, bool complete)
    {
        Find<Flyout>(owner, "adv_searsh").IsOpen = true; Pump();
        foreach (string name in new[] { "By_Main", "By_Emp", "By_Service" }) Find<CheckBox>(owner, name).IsChecked = true;
        Find<DateEdit>(owner, "from_date").EditValue = DateTime.Today.AddDays(-2);
        Find<DateEdit>(owner, "To_date").EditValue = DateTime.Today;
        Find<ComboBoxEdit>(owner, "Combo_emp_Search").SelectedIndex = 0;
        Find<ComboBoxEdit>(owner, "Combo_Services_Search").SelectedIndex = 0;
        if (complete) { Find<CheckBox>(owner, "By_State").IsChecked = true; Find<ComboBoxEdit>(owner, "Doc_Type_Searsh").SelectedIndex = 2; }
        if (owner is maintenance_Order order)
        {
            order.State.SearchResults.Add(new(77, DateTime.Today, "مبنى", "فني", "خدمة", "موقع اختبار 2", 2, 1));
            order.State.SearchStatus = "نتائج سابقة";
        }
        else
        {
            var state = ((maintenance_complete)owner).State;
            state.SearchResults.Add(new(new("موقع اختبار 2", 2, 1, 77), "03/11/2026", "مبنى", "فني", "خدمة", "معلقة", "موقع اختبار 2"));
            state.SearchStatus = "نتائج سابقة"; state.Refresh();
        }
        var grid = Find<GridControl>(owner, "Search_Grid"); grid.CurrentItem = ((System.Collections.IEnumerable)grid.ItemsSource).Cast<object>().First(); Pump();
    }
    private static void AssertClean(MetroWindow owner, bool complete)
    {
        foreach (string name in Fields(complete)) Check(Find<BaseEdit>(owner, name).EditValue is null, "Field retained: " + name);
        foreach (string name in complete ? new[] { "By_Main", "By_Main_To", "By_Emp", "By_Service", "By_State" } : new[] { "By_Main", "By_Emp", "By_Service" })
            Check(Find<CheckBox>(owner, name).IsChecked == false, "Filter retained: " + name);
        var grid = Find<GridControl>(owner, "Search_Grid");
        Check(!((System.Collections.IEnumerable)grid.ItemsSource).Cast<object>().Any() && grid.CurrentItem is null, "Results or current row retained.");
        Check((owner is maintenance_Order order ? order.State.SearchStatus : ((maintenance_complete)owner).State.SearchStatus) == "", "Search status retained.");
        Check(!Walk(Find<Flyout>(owner, "adv_searsh")).OfType<Border>().Any(Validation.GetHasError), "Validation retained.");
    }
    // تعليق استعلام SQLite الحقيقي يسمح بفحص وصول النتيجة بعد إغلاق اللوحة.
    private sealed class QueryGate : DbCommandInterceptor
    {
        public bool Pause;
        public TaskCompletionSource Entered = new();
        public TaskCompletionSource Release = new();
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Pause) { Pause = false; Entered.SetResult(); await Release.Task; }
            return result;
        }
    }
    #endregion

    #region سيناريوهات الإغلاق والاختيار والنتائج المتأخرة
    private static async Task RunAsync()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
        Maintenance_DabaseContext Create() => new(options);
        // بيانات اصطناعية مشتركة مع فحص التوجيه؛ لا اتصال بقاعدة العمل.
        using (var db = Create()) typeof(NotificationRoutingProbe).GetMethod("Seed", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [db]);
        var order = new maintenance_Order();
        typeof(maintenance_Order).GetField("Store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(order, new MaintenanceOrderEfStore(Create));
        var complete = new maintenance_complete(Create);
        using (var db = Create())
        {
            foreach (var item in db.T_Employes.AsNoTracking()) { order.State.SearchEmployees.Add(item); complete.State.Employees.Add(item); }
            foreach (var item in db.T_Services.AsNoTracking()) { order.State.Services.Add(item); complete.State.Services.Add(item); }
        }
        foreach (var window in new MetroWindow[] { order, complete })
        {
            bool isComplete = window is maintenance_complete; Show(window);
            var preserved = isComplete ? Find<BaseEdit>(window, "Text_notes") : Find<BaseEdit>(window, "Text_dtils");
            preserved.EditValue = "مسودة محفوظة في الواجهة";
            for (int mode = 0; mode < 3; mode++)
            {
                Fill(window, isComplete);
                if (mode == 0) Click(Find<FrameworkElement>(window, isComplete ? "CancelSearch" : "Cancel_Search_Button"));
                else if (mode == 1) Click(Walk(Find<Flyout>(window, "adv_searsh")).OfType<ButtonBase>().Single(x => x.Name == "FlyoutCloseButton"));
                else window.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Pump(); AssertClean(window, isComplete);
                Check(Equals(preserved.EditValue, "مسودة محفوظة في الواجهة"), "Closing search changed document draft.");
                Find<Flyout>(window, "adv_searsh").IsOpen = true; Pump(); AssertClean(window, isComplete);
                Find<Flyout>(window, "adv_searsh").IsOpen = false;
            }
            // تنبيه تاريخ مفقود يظهر داخل اللوحة ثم يزول مع الإغلاق والتنظيف.
            Find<Flyout>(window, "adv_searsh").IsOpen = true;
            Find<CheckBox>(window, "By_Main").IsChecked = true;
            if (isComplete) await complete.SearchAsync(); else await order.SearchAsync();
            Check(Walk(Find<Flyout>(window, "adv_searsh")).OfType<Border>().Any(Validation.GetHasError), "Expected search validation error.");
            Click(Find<FrameworkElement>(window, isComplete ? "CancelSearch" : "Cancel_Search_Button")); AssertClean(window, isComplete);
            // نجاح اختيار نتيجة يحمل التكليف أولًا ثم ينظف البحث، دون فقد بيانات التكليف.
            Fill(window, isComplete);
            if (isComplete) Click(Find<FrameworkElement>(window, "OpenSearchResult"));
            else Find<GridControl>(window, "Search_Grid").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
            for (int i = 0; Find<Flyout>(window, "adv_searsh").IsOpen && i < 100; i++) await Task.Delay(20);
            Check(!Find<Flyout>(window, "adv_searsh").IsOpen, "Selection did not close panel.");
            AssertClean(window, isComplete);
            Check(isComplete ? complete.State.OrderNumber == "77" : Convert.ToInt64(Find<BaseEdit>(order, "txt_maint_no").EditValue) == 77, "Selected order was lost.");
        }
        var gate = new QueryGate();
        var delayedOptions = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).AddInterceptors(gate).Options;
        var delayed = new maintenance_complete(() => new(delayedOptions)); Show(delayed);
        Find<Flyout>(delayed, "adv_searsh").IsOpen = true; gate.Pause = true;
        var search = delayed.SearchAsync(); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Find<Flyout>(delayed, "adv_searsh").IsOpen = false; Pump(); AssertClean(delayed, true);
        Find<Flyout>(delayed, "adv_searsh").IsOpen = true; Pump(); AssertClean(delayed, true);
        gate.Release.SetResult(); await search; Pump(); AssertClean(delayed, true);
        Check(delayed.State.SearchResults.Count == 0, "Late query repopulated results.");
        order.Close(); complete.Close(); delayed.Close();
    }
    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            foreach (string resource in new[] { "Styles/Controls.xaml", "Styles/Fonts.xaml", "Styles/Themes/Light.Steel.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MahApps.Metro;component/" + resource) });
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light"; Maintenance_app.App.ConnectionString = "";
            var task = RunAsync(); var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); task.GetAwaiter().GetResult();
            app.Shutdown(); Console.WriteLine($"PASS: {checks} live advanced-search cleanup, cancel/header/Escape/reopen/selection and delayed-query checks; isolated SQLite only."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion
}
