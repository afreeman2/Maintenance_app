using DevExpress.Xpf.Core;
using Maintenance_app.orders_Windows;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

internal static class DeleteRowProbe
{
    #region أدوات فحص الصفوف والرسائل الفعلية دون قاعدة العمل
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    // نختار زر الصف نفسه من الجدول المعروض، لا من قالب منشأ خارج ربط DevExpress.
    private static SimpleButton RowButton(maintenance_Order owner, string gridName, object row)
    {
        Pump();
        return Walk((DependencyObject)owner.FindName(gridName)).OfType<SimpleButton>()
            .Single(x => ReferenceEquals(x.Tag, row));
    }
    // الرد على رسائل التأكيد والنجاح عبر أزرارها الحقيقية، مع إغلاق احتياطي عند تعذر الرد.
    private static void Delete(SimpleButton button, bool confirm)
    {
        int dialogs = 0;
        Exception? failure = null;
        var started = DateTime.UtcNow;
        var seen = new HashSet<Window>();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            foreach (var dialog in Application.Current.Windows.Cast<Window>().Where(x => x.IsVisible && x is ThemedWindow).ToArray())
            {
                if (seen.Contains(dialog)) continue;
                dialog.UpdateLayout();
                var buttons = Walk(dialog).OfType<ThemedWindowDialogButton>().Where(x => x.IsVisible).ToList();
                if (buttons.Count == 0) continue;
                try
                {
                    string[] wanted = dialogs == 0 ? (confirm ? ["Yes", "نعم"] : ["No", "لا"]) : ["OK", "موافق"];
                    var answer = buttons.Single(x => wanted.Contains(x.Content?.ToString()?.Replace("_", "")));
                    seen.Add(dialog); dialogs++;
                    var peer = UIElementAutomationPeer.CreatePeerForElement(answer);
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            }
            if ((DateTime.UtcNow - started).TotalSeconds > 8)
            {
                failure ??= new Exception("Timed out waiting for deletion dialogs.");
                foreach (Window dialog in Application.Current.Windows.Cast<Window>().Where(x => x is ThemedWindow).ToArray()) dialog.Close();
            }
        };
        timer.Start();
        try
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            if (failure is not null) throw failure;
            Check(dialogs == (confirm ? 2 : 1), "Expected actual confirmation and optional success dialogs.");
        }
        finally { timer.Stop(); }
    }
    #endregion

    #region تشغيل الحذف والإلغاء على بيانات مسودة معزولة
    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            foreach (string resource in new[] { "Styles/Controls.xaml", "Styles/Fonts.xaml", "Styles/Themes/Light.Steel.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MahApps.Metro;component/" + resource) });
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            Maintenance_app.App.ConnectionString = string.Empty;
            var window = new maintenance_Order { Width = 1366, Height = 768, WindowState = WindowState.Normal,
                Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
            typeof(maintenance_Order).GetField("loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            var repair1 = new RepairLine(null, null, "صيانة", "الأول", "الكهرباء");
            var repair2 = new RepairLine(null, null, "صيانة", "الثاني", "الكهرباء");
            var part1 = new MaterialLine("الأولى", 2, "قطعة", 5);
            var part2 = new MaterialLine("الثانية", 3, "قطعة", 7);
            window.State.Repairs.Add(repair1); window.State.Repairs.Add(repair2);
            window.State.Parts.Add(part1); window.State.Parts.Add(part2);
            window.Show(); Pump();
            Check(ReferenceEquals(RowButton(window, "Repiers_Grid", repair2).Tag, repair2), "Repair button lost the row.");
            Delete(RowButton(window, "Repiers_Grid", repair2), false);
            Check(window.State.Repairs.Count == 2, "Cancel removed repair.");
            Delete(RowButton(window, "Repiers_Grid", repair2), true);
            Check(window.State.Repairs.SequenceEqual(new[] { repair1 }), "Wrong repair removed.");
            Delete(RowButton(window, "Parts_Grid", part2), false);
            Check(window.State.Parts.Count == 2, "Cancel removed material.");
            Delete(RowButton(window, "Parts_Grid", part2), true);
            Check(window.State.Parts.SequenceEqual(new[] { part1 }), "Wrong material removed or wrong handler.");
            Check(window.State.Repairs.Single() == repair1, "Material deletion changed repairs.");
            Check(window.State.Parts.Sum(x => x.Quantity * x.Price) == 10, "Remaining material total is incorrect.");
            window.Close(); app.Shutdown();
            Console.WriteLine($"PASS: {checks} live row-template, confirmation, cancel and repair/material deletion checks. Draft data only; no database.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion
}
