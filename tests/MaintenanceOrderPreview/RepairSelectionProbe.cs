using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using MahApps.Metro.Controls;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

internal static class RepairSelectionProbe
{
    #region فحص بيانات EF المنفصلة داخل لوحة التعديل الحقيقية
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static T Find<T>(FrameworkElement owner, string name) where T : class => (T)owner.FindName(name);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static async Task RunAsync(string output)
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
        Maintenance_DabaseContext Create() => new(options);
        // الأرقام نفسها تتكرر في موقع آخر ورقم موقع آخر وفرع آخر لاكتشاف أي مطابقة ناقصة.
        await using (var db = Create())
        {
            await db.Database.EnsureCreatedAsync();
            foreach (var scope in new[] { ("أ", (short)1, (short)1), ("الهدف", (short)2, (short)1), ("الهدف", (short)1, (short)2), ("الهدف", (short)1, (short)1) })
            {
                string label = scope.Item1 + " " + scope.Item2 + " " + scope.Item3;
                db.T_Manages.Add(new T_Manage { Location = scope.Item1, Location_ID = scope.Item2, Branch_ID = scope.Item3, Manag_ID = 1, Manag = "إدارة " + label });
                db.T_Sections.Add(new T_Section { Location = scope.Item1, Location_ID = scope.Item2, Branch_ID = scope.Item3, Manag_ID = 1, Secti_ID = 1, Secti = "قسم " + label });
            }
            await db.SaveChangesAsync();
        }
        var window = new maintenance_Order { WindowState = WindowState.Normal, Width = 1366, Height = 768,
            Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        await using (var db = Create())
        {
            foreach (var item in await db.T_Manages.AsNoTracking().OrderBy(x => x.Location).ThenByDescending(x => x.Location_ID).ThenByDescending(x => x.Branch_ID).ToListAsync()) window.State.Managements.Add(item);
            foreach (var item in await db.T_Sections.AsNoTracking().OrderBy(x => x.Location).ThenByDescending(x => x.Location_ID).ThenByDescending(x => x.Branch_ID).ToListAsync()) window.State.AllDepartments.Add(item);
        }
        T_Section saved;
        // السياق المستقل ينتج نفس سجلات القوائم بكائنات مختلفة، كما يحدث عند تحميل التكليف.
        await using (var db = Create()) saved = await db.T_Sections.AsNoTracking().Include(x => x.T_Manage)
            .SingleAsync(x => x.Location == "الهدف" && x.Location_ID == 1 && x.Branch_ID == 1 && x.Secti_ID == 1);
        var expectedManage = window.State.Managements.Single(x => x.Location == "الهدف" && x.Location_ID == 1 && x.Branch_ID == 1);
        var expectedSection = window.State.AllDepartments.Single(x => x.Location == "الهدف" && x.Location_ID == 1 && x.Branch_ID == 1);
        Check(!ReferenceEquals(saved, expectedSection) && !ReferenceEquals(saved.T_Manage, expectedManage), "Fixture must use detached EF instances.");
        var row = new RepairLine(saved.T_Manage, saved, "صيانة", "بيان صيانة محفوظ", "الكهرباء", 91);
        window.State.Repairs.Add(row); window.Show(); Pump(); window.EditRepair(row); await Task.Delay(300); Pump();
        var management = Find<ComboBoxEdit>(window, "Combo_Management_Update");
        var department = Find<ComboBoxEdit>(window, "Combo_Department_update");
        Check(ReferenceEquals(management.SelectedItem, expectedManage), "Saved management was not selected from lookup; text=" + management.Text);
        Check(ReferenceEquals(department.SelectedItem, expectedSection), "Saved department was not selected from lookup; text=" + department.Text);
        Check(management.Text == expectedManage.Manag && department.Text == expectedSection.Secti, "Saved Arabic names are not displayed.");
        Check(Find<TextEdit>(window, "Text_dtils_Update").Text == row.Details && Equals(Find<ComboBoxEdit>(window, "Combo_Repier_type_Update").EditValue, row.Action), "Other row data changed.");
        management.ShowPopup(); Pump(); Check(management.IsPopupOpen && ReferenceEquals(management.SelectedItem, expectedManage), "Opening lookup lost saved selection."); management.ClosePopup();
        department.ShowPopup(); Pump(); Check(department.IsPopupOpen && ReferenceEquals(department.SelectedItem, expectedSection), "Department popup lost saved selection."); department.ClosePopup();
        Directory.CreateDirectory(output);
        var panel = Find<Flyout>(window, "Config_Service_Windows");
        var image = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var draw = visual.RenderOpen())
        { draw.PushTransform(new ScaleTransform(-1, 1, panel.ActualWidth / 2, 0)); draw.DrawRectangle(new VisualBrush(panel), null, new Rect(0, 0, panel.ActualWidth, panel.ActualHeight)); }
        image.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(Path.Combine(output, "saved-repair-data.png"))) png.Save(stream);
        Find<FrameworkElement>(window, "But_Cancl_Update").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Check(ReferenceEquals(window.State.Repairs.Single(), row), "Cancel replaced saved row.");
        window.EditRepair(row); Pump(); Check(ReferenceEquals(management.SelectedItem, expectedManage) && ReferenceEquals(department.SelectedItem, expectedSection), "Reopen lost saved names.");
        Find<TextEdit>(window, "Text_dtils_Update").EditValue = "تفاصيل معدلة محليًا";
        Find<FrameworkElement>(window, "Update_Button").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Check(window.State.Repairs.Single() is { Id: 91, Details: "تفاصيل معدلة محليًا" }, "Valid edit did not preserve row identity.");
        Check(ReferenceEquals(window.State.Repairs[0].Manage, expectedManage) && ReferenceEquals(window.State.Repairs[0].Section, expectedSection), "Saved local row does not use full-scope selection.");
        window.EditRepair(new(null, null, "صيانة", "بلا بيانات اختيار", "الكهرباء")); Pump();
        Check(management.SelectedItem is null && department.SelectedItem is null, "Null row retained previous selection.");
        await using (var db = Create()) Check(await db.T_Manages.CountAsync() == 4 && await db.T_Sections.CountAsync() == 4, "UI test modified persisted lookup records.");
        window.Close();
    }
    #endregion

    #region تشغيل الفحص دون قاعدة العمل
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            foreach (string resource in new[] { "Styles/Controls.xaml", "Styles/Fonts.xaml", "Styles/Themes/Light.Steel.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MahApps.Metro;component/" + resource) });
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light"; Maintenance_app.App.ConnectionString = "";
            Maintenance_app.App.appuser = new T_SystemUser { User_ID = 7 };
            var task = RunAsync(args[0]);
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); task.GetAwaiter().GetResult();
            app.Shutdown(); Console.WriteLine($"PASS: {checks} persisted lookup, detached EF, full-scope display, popup, cancel, reopen and edit checks; isolated SQLite only."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion
}
