using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using ListBox = System.Windows.Controls.ListBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

internal static class TechnicianSelectionProbe
{
    #region أدوات فحص النافذة

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    #endregion

    #region فحص المربعات الأصلية ومحاذاة أسماء الفنيين

    private static void CheckLayout(DependencyObject root)
    {
        var checks = Descendants(root).OfType<CheckEdit>().ToList();
        Check(checks.Count > 0, "The native checkbox editors are missing.");
        foreach (CheckEdit check in checks)
        {
            var text = Descendants(check).OfType<TextBlock>().First(t => !string.IsNullOrWhiteSpace(t.Text));
            Check(check.CheckBoxWidth == 22 && check.CheckBoxHeight == 22, "The technician checkbox did not grow to 22 DIP.");
            Check(check.ActualWidth <= text.ActualWidth + 54, "The name is still separated from its checkbox by the popup width.");
            Check(check.HorizontalAlignment == HorizontalAlignment.Right && check.HorizontalContentAlignment == HorizontalAlignment.Right,
                "The checkbox/name group is not aligned to the right.");
            Check(Descendants(check).OfType<FrameworkElement>().Any(x => Math.Abs(x.ActualWidth - 22) < 1 && Math.Abs(x.ActualHeight - 22) < 1),
                "The enlarged checkbox glyph was not rendered.");
        }
    }

    #endregion

    #region اختيار الفنيين والمعاينة والتمرير وإعادة الفتح

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            foreach (string dictionary in new[] { "Styles/Controls.xaml", "Styles/Fonts.xaml", "Styles/Themes/Light.Steel.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MahApps.Metro;component/" + dictionary) });
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            Maintenance_app.App.ConnectionString = string.Empty;
            var window = new maintenance_Order
            {
                WindowState = WindowState.Normal, Width = 1366, Height = 768,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000,
                ShowActivated = false, ShowInTaskbar = false
            };
            var first = new T_Employe { Employe_ID = 11, Emp_Name = "الفني الأول" };
            var second = new T_Employe { Employe_ID = 22, Emp_Name = "الفني الثاني" };
            window.State.Employees.Add(first); window.State.Employees.Add(second);
            // يبقى عنصر غير مختار ظاهرًا عند إعادة فتح قائمة الرموز، التي تخفي المختارين افتراضيًا.
            window.State.Employees.Add(new T_Employe { Employe_ID = 33, Emp_Name = "الفني الثالث" });
            window.Show(); Pump();
            var editor = (ComboBoxEdit)window.FindName("Combo_Technical_Man");
            Check(editor.PopupFooterButtons == PopupFooterButtons.None, "OK/Cancel footer must remain hidden.");
            Check(editor.ClosePopupOnClickMode == PopupCloseMode.Normal, "Outside-click closure would discard the selection.");
            editor.ShowPopup(); Pump();
            Check(editor.IsPopupOpen && editor.PopupRoot is not null, "The actual technician popup did not open.");
            Check(editor.HorizontalContentAlignment == HorizontalAlignment.Right, "The closed technician editor is not right-aligned.");
            CheckLayout(editor.PopupRoot!);
            if (args.Length > 0)
            {
                Directory.CreateDirectory(args[0]);
                var root = (FrameworkElement)editor.PopupRoot!;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(args[0], "technician-popup.png"));
                png.Save(file);
            }
            var selectors = Descendants(editor.PopupRoot!).OfType<ListBox>().ToList();
            Console.WriteLine("POPUP_SELECTORS=" + string.Join(",", selectors.Select(x => x.GetType().FullName)));
            var list = selectors.FirstOrDefault() ?? throw new InvalidOperationException("No actual popup selection list found.");
            list.SelectedItems.Add(first); list.SelectedItems.Add(second); Pump();
            editor.ClosePopup(); Pump();
            var ids = (editor.EditValue as IEnumerable)?.Cast<object>().Select(Convert.ToInt32).ToArray() ?? [];
            Check(ids.Order().SequenceEqual(new[] { 11, 22 }), "Closing did not retain both technician IDs.");
            var visibleText = Descendants(editor).OfType<TextBlock>().Select(x => x.Text).ToList();
            Console.WriteLine("DISPLAY=" + editor.DisplayText);
            Console.WriteLine("VISIBLE_TEXT=" + string.Join("|", visibleText));
            Check(visibleText.Any(x => x.Contains(first.Emp_Name)) && visibleText.Any(x => x.Contains(second.Emp_Name)), "Selected technician names are not visible in the closed editor.");
            editor.ShowPopup(); Pump();
            CheckLayout(editor.PopupRoot!);
            editor.ClosePopup(); Pump();
            Check((editor.EditValue as IEnumerable)?.Cast<object>().Count() == 2, "Reopening lost a technician.");
            // التحقق من الصفوف المنشأة عند التمرير، مع بقاء اختيارات الفنيين الأصلية محفوظة.
            T_Employe? last = null;
            for (int index = 100; index < 220; index++)
            {
                last = new T_Employe { Employe_ID = index, Emp_Name = "فني تمرير " + index };
                window.State.Employees.Add(last);
            }
            editor.ShowPopup(); Pump();
            list = Descendants(editor.PopupRoot!).OfType<ListBox>().First();
            list.ScrollIntoView(last); Pump();
            var lastRow = list.ItemContainerGenerator.ContainerFromItem(last) as DependencyObject;
            Check(lastRow != null, "Scrolling did not generate the last technician row.");
            CheckLayout(lastRow!);
            editor.ClosePopup(); Pump();
            Check((editor.EditValue as IEnumerable)?.Cast<object>().Select(Convert.ToInt32).Order().SequenceEqual(new[] { 11, 22 }) == true,
                "Popup formatting or scrolling changed the selected technician IDs.");
            window.Close(); app.Shutdown();
            Console.WriteLine("PASS: native 22 DIP checkbox glyphs; adjacent technician names; right-aligned editor and rows; actual multiple selection, reopening and scroll-generated rows; names and IDs retained; footer hidden. No database writes.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    #endregion
}
