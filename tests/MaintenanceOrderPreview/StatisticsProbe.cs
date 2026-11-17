using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Size = System.Windows.Size;

internal static class StatisticsProbe
{
    #region أدوات المعاينة والانتظار وتنظيف نوافذ الاختبار
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static T Find<T>(FrameworkElement owner, string name) where T : class => (T)owner.FindName(name);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void WaitUntil(Func<bool> condition, string message, int timeout = 9000)
    {
        var watch = Stopwatch.StartNew(); var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) => { if (condition() || watch.ElapsedMilliseconds > timeout) { frame.Continue = false; timer.Stop(); } };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); Check(condition(), message);
    }
    private static void CloseMain(Maintenance_app.MainWindow window)
    {
        typeof(Maintenance_app.MainWindow).GetField("confirmedClose", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
        window.Close();
    }
    private static Rect Bounds(Maintenance_app.MainWindow window, FrameworkElement element) => element.TransformToAncestor((Visual)window.Content).TransformBounds(new Rect(element.RenderSize));
    private static void Layout(Maintenance_app.MainWindow window, Size size)
    {
        window.Width = size.Width; window.Height = size.Height; Pump(); var root = (FrameworkElement)window.Content;
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); Pump(); root.UpdateLayout();
    }
    private static void Render(Maintenance_app.MainWindow main, Size size, string path)
    {
        // لقطة RTL غير مرتبطة بـHWND لتجنب انعكاس النص أثناء التصدير من خارج الشاشة.
        var snapshot = new Maintenance_app.MainWindow { WindowState = WindowState.Normal };
        Find<Border>(snapshot, "StatisticsCard").DataContext = main.Statistics;
        Layout(snapshot, size); var root = (FrameworkElement)snapshot.Content;
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file); CloseMain(snapshot);
    }
    #endregion

    #region بيانات الحالات الخمس في سنتين مختلفتين
    private static void Seed(Maintenance_DabaseContext db)
    {
        db.Database.EnsureCreated(); var today = DateOnly.FromDateTime(DateTime.Today); int year = today.Year;
        const string location = "موقع المعاينة";
        db.AddRange(new T_Branch { Branch_ID = 1, Branch = "فرع الاختبار" }, new T_Location { Location = location, Location_ID = 1, Branch_ID = 1 },
            new T_Build { Location = location, Location_ID = 1, Branch_ID = 1, Build_ID = 1, Build = "مبنى الاختبار" },
            new T_Service { Location = location, Location_ID = 1, Branch_ID = 1, Serv_num = 1, Serv_type = "خدمة الاختبار" });
        int number = 0;
        void Add(byte state, int orderYear)
        {
            number++;
            var order = new T_Emergency_MaintenanceCost { Location = location, Location_ID = 1, Branch_ID = 1, Build_ID = 1, Serv_num = 1,
                Emergency_MaintenanceCosts_No = number, Date_com = new(orderYear, 1, 1), Date_Modify = today, Emergency_MaintenanceCosts_ST = state < 4 };
            if (state < 4) order.T_FinishedMaintenances.Add(new() { ID = number, Location = location, Location_ID = 1, Branch_ID = 1,
                Build_ID = 1, MaintenanceCosts_No = number, DateM = today, Date_Modify = today, State = state, observer = "موظف اختبار", notes = "اختبار" });
            db.Add(order);
        }
        for (byte state = 0; state < 5; state++) for (int row = 0; row <= state; row++) Add(state, year);
        Add(0, year - 1); db.SaveChanges();
    }
    #endregion

    #region اختبار الرئيسية الحقيقية والبطاقات والسنة والتحديث
    [STAThread]
    private static int Main(string[] args)
    {
        Maintenance_app.MainWindow? main = null; Maintenance_app.App? app = null;
        try
        {
            Directory.CreateDirectory(args[0]);
            using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
            var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
            Maintenance_DabaseContext Create() => new(options);
            using (var db = Create()) Seed(db);
            app = new Maintenance_app.App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
            var startup = typeof(Maintenance_app.App).GetMethod("Application_Startup", BindingFlags.NonPublic | BindingFlags.Instance)!;
            app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            Maintenance_app.App.ConnectionString = string.Empty; Maintenance_app.App.appuser = new T_SystemUser { User_ID = 2147460 };
            main = new Maintenance_app.MainWindow(Create) { ShowInTaskbar = false, ShowActivated = false, WindowState = WindowState.Normal,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, Width = 1366, Height = 768 };
            main.Notifications.ToastsEnabled = false; main.Show(); Pump();
            WaitUntil(() => main.Statistics.HasData || main.Statistics.IsError, "Statistics initial load did not finish.");
            Check(!main.Statistics.IsError, "Statistics query failed: " + main.Statistics.Status);
            var names = new[] { "Completed", "NotCompleted", "Suspended", "InProgress", "NeedsReport" };
            for (int index = 0; index < names.Length; index++)
                Check(Find<TextBlock>(main, "Statistics" + names[index] + "Value").Text == (index + 1).ToString(), "Actual card binding wrong: " + names[index]);
            var yearEditor = Find<SpinEdit>(main, "StatisticsYear"); int year = DateTime.Today.Year;
            yearEditor.EditValue = year - 1; Pump();
            Check(main.Statistics.CompletedCount == 1 && main.Statistics.TotalCount == 1 && main.Statistics.SelectedYearNumber == year - 1, "Year editor did not select prior-year orders.");
            yearEditor.EditValue = null; Pump();
            Check(main.Statistics.TotalCount == 16 && main.Statistics.CompletedCount == 2, "Clearing year did not aggregate all years.");
            yearEditor.EditValue = year - 2; Pump(); Check(main.Statistics.TotalCount == 0 && main.Statistics.CompletedText == "0", "Empty year should show zero.");
            yearEditor.EditValue = year; Pump();
            foreach (var size in new[] { new Size(1080, 640), new Size(1366, 768), new Size(1536, 864), new Size(1920, 1080) })
            {
                Layout(main, size);
                Rect card = Bounds(main, Find<Border>(main, "StatisticsCard"));
                Render(main, size, Path.Combine(args[0], $"maintenance-statistics-{size.Width}x{size.Height}.png"));
                Check(card.Left >= 0 && card.Top >= 0 && card.Right <= size.Width && card.Bottom <= size.Height, "Statistics card cropped at " + size + ": " + card);
                Check(Bounds(main, Find<FrameworkElement>(main, "WorkspaceCanvas")).Contains(card), "Statistics card overlaps the main status bar.");
                var quickBounds = Bounds(main, Find<Border>(main, "QuickAccessPanel"));
                var actions = Descendants(Find<ItemsControl>(main, "QuickAccessCommands")).OfType<SimpleButton>().ToList();
                Check(actions.Count == 6, "Quick access lost an existing action.");
                foreach (var action in actions)
                {
                    Check(quickBounds.Contains(Bounds(main, action)), "A quick-access action spills over the statistics.");
                    foreach (var text in Descendants(action).OfType<TextBlock>().Where(text => !string.IsNullOrEmpty(text.Text)))
                        Check(Bounds(main, action).Contains(Bounds(main, text)), "Quick-access text clipped at " + size);
                }
                var rects = new List<Rect>();
                foreach (string name in names)
                {
                    var value = Find<TextBlock>(main, "Statistics" + name + "Value"); var tile = Find<Border>(main, "Statistics" + name + "Card");
                    var rect = Bounds(main, tile); rects.Add(rect);
                    Check(rect.Width >= 130 && rect.Height >= 75 && card.Contains(rect), "Statistics tile collapsed or cropped: " + name + " at " + size);
                    Check(Bounds(main, value).Width > 8 && !string.IsNullOrEmpty(value.Text), "Statistics count failed to render.");
                }
                for (int i = 1; i < rects.Count; i++) Check(!rects[i].IntersectsWith(rects[i - 1]), "Statistics tiles overlap.");
                Render(main, size, Path.Combine(args[0], $"maintenance-statistics-{size.Width}x{size.Height}.png"));
            }
            // تغيير مستقل يحاكي جهازًا آخر، ثم نشر حدث محلي لاختبار التحديث دون إعادة اختيار السنة.
            using (var db = Create()) { db.T_FinishedMaintenances.Single(row => row.ID == 1).State = 2; db.SaveChanges(); }
            MaintenanceNotificationUpdates.Publish();
            WaitUntil(() => main.Statistics.CompletedCount == 0 && main.Statistics.SuspendedCount == 4, "Committed update did not refresh maintenance cards.");
            using (var db = Create()) { db.T_FinishedMaintenances.Single(row => row.ID == 1).State = 0; db.SaveChanges(); }
            WaitUntil(() => main.Statistics.CompletedCount == 1 && main.Statistics.SuspendedCount == 3, "Periodic statistics refresh missed remote change.", 10000);
            Check(main.Statistics.SelectedYearNumber == year, "Automatic refresh reset the selected year.");
            var refresh = Find<SimpleButton>(main, "RefreshStatisticsButton");
            refresh.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump(); Check(main.Statistics.HasData && !main.Statistics.IsError, "Manual refresh failed.");
            Find<SimpleButton>(main, "CloseStatisticsButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump();
            Check(Find<Border>(main, "StatisticsCard").Visibility == Visibility.Collapsed, "Hide statistics failed.");
            Find<SimpleButton>(main, "ShowStatisticsButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump();
            Check(Find<Border>(main, "StatisticsCard").Visibility == Visibility.Visible, "Restore statistics failed.");
            var timer = (DispatcherTimer)typeof(Maintenance_app.MainWindow).GetField("statisticsTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
            CloseMain(main); main = null; Check(!timer.IsEnabled, "Statistics refresh timer survived window close.");
            app.Shutdown(); Console.WriteLine($"PASS: {checks} native maintenance statistics checks; five rendered cards, year/all-years/empty year, four sizes, local/remote/manual refresh, hide/restore and timer shutdown; isolated SQLite only."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (main is not null) CloseMain(main); app?.Shutdown(); }
    }
    #endregion
}
