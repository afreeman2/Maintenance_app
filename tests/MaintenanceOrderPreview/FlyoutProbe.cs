using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Grid;
using MahApps.Metro.Controls;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using Maintenance_app.Reports;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Xps.Packaging;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Size = System.Windows.Size;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

internal static class FlyoutProbe
{
    #region أدوات فحص اللوحات الفعلية دون قاعدة بيانات العمل
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static T Find<T>(FrameworkElement root, string name) where T : class => (T)root.FindName(name);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static void Wait(Func<bool> condition, string message)
    {
        var watch = Stopwatch.StartNew(); var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => { if (condition() || watch.ElapsedMilliseconds > 8000) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); Check(condition(), message);
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Click(FrameworkElement element) => element.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Escape(MetroWindow window) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void Show(MetroWindow window)
    {
        window.WindowState = WindowState.Normal; window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000; window.Top = -30000; window.Width = 1366; window.Height = 768;
        window.ShowInTaskbar = false; window.ShowActivated = false; window.Show(); Pump();
    }
    private static void CheckBounds(MetroWindow window, params string[] names)
    {
        foreach (var name in names)
        {
            var element = Find<FrameworkElement>(window, name);
            var rect = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
            Check(rect.Width > 20 && rect.Height > 20 && rect.Left >= -1 && rect.Top >= -1 && rect.Right <= window.ActualWidth + 1 && rect.Bottom <= window.ActualHeight + 1, name + " clipped: " + rect);
        }
    }
    // تصدير اللوحة وحدها يتجنب انعكاس سطح HWND في لقطات RTL خارج الشاشة.
    private static void Snapshot(Flyout panel, string path)
    {
        var target = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth), (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var draw = visual.RenderOpen())
        {
            // جذر HWND يعكس سطح RTL عند التصدير؛ نصحح سطح اللقطة وحده دون تغيير الواجهة.
            draw.PushTransform(new ScaleTransform(-1, 1, panel.ActualWidth / 2, 0));
            draw.DrawRectangle(new VisualBrush(panel) { Stretch = Stretch.Fill }, null, new Rect(0, 0, panel.ActualWidth, panel.ActualHeight));
            draw.Pop();
        }
        target.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static void CloseNative(Flyout panel)
    {
        var close = Walk(panel).OfType<ButtonBase>().First(x => x.IsVisible && x.Name == "FlyoutCloseButton");
        Click(close); Wait(() => !panel.IsOpen, "Native close did not close panel.");
    }
    // فحص التصميم الظاهر: رأس واحد داخل البطاقة، مع ألوان نافذة الصيانة وإغلاق قابل للاستخدام.
    private static void CheckDcmsPanel(MetroWindow owner, Flyout panel)
    {
        Check(panel.Position == Position.Bottom && panel.Background is SolidColorBrush { Color.A: 0 }, "Panel must open from bottom with transparent surface.");
        var header = Walk(panel).OfType<MaintenanceFlyoutHeader>().Single();
        Check(ReferenceEquals(Find<Border>(header, "FlyoutHeaderSurface").Background, owner.FindResource("Order.Hero")), "Header does not use maintenance palette.");
        Check(Find<TextBlock>(header, "HeaderTitle").Text == panel.Header.ToString(), "Card title missing.");
        var close = Find<FrameworkElement>(header, "FlyoutCloseButton");
        Check(close.IsVisible && close.ActualWidth >= 32 && close.ActualHeight >= 32, "Card close is not usable.");
        Check(!Walk(panel).OfType<ButtonBase>().Any(x => x.Name == "PART_BackButton" && x.IsVisible), "Default close duplicates card header.");
    }
    #endregion

    #region البحث والتعديل والطباعة عبر Flyout
    // تاريخ يحمل وقتًا فعليًا؛ نفحص العرض داخل الخلية مع الحفاظ على القيمة الأصلية.
    private static void CheckDateCell(maintenance_Order owner)
    {
        var datedRow = new OrderSearchRow(101, new DateTime(2026, 11, 3, 14, 25, 36), "مبنى المعاينة", "فني المعاينة", "الكهرباء", "معاينة", 0, 0);
        owner.State.SearchResults.Add(datedRow); Pump();
        var grid = Find<FrameworkElement>(owner, "Search_Grid");
        Wait(() => Walk(grid).OfType<TextEdit>().Any(x => x.EditValue is DateTime), "Date cell was not rendered.");
        var cell = Walk(grid).OfType<TextEdit>().Single(x => x.EditValue is DateTime);
        Check(cell.DisplayText == "03/11/2026" && Equals(cell.EditValue, datedRow.Date), "Date cell must hide time without changing its value: " + cell.DisplayText);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string output = args[0]; Directory.CreateDirectory(output);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            foreach (string resource in new[] { "Styles/Controls.xaml", "Styles/Fonts.xaml", "Styles/Themes/Light.Steel.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MahApps.Metro;component/" + resource) });
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            Maintenance_app.App.ConnectionString = "";
            Maintenance_app.App.appuser = new T_SystemUser { User_ID = 7, fordon = true };
            var orderWindow = new maintenance_Order(); Show(orderWindow);
            // مسار معاينة محدود لفحص تنسيق التاريخ دون تشغيل أوامر التعديل والطباعة الأخرى.
            if (args.Length > 1 && args[1] == "--date-only")
            {
                Click(Find<FrameworkElement>(orderWindow, "search_button"));
                var panel = Find<Flyout>(orderWindow, "adv_searsh");
                Wait(() => panel.IsShown, "Search panel failed to open.");
                CheckDateCell(orderWindow);
                Snapshot(panel, Path.Combine(output, "search-date-only.png"));
                orderWindow.Close(); app.Shutdown();
                Console.WriteLine($"PASS: {checks} live WPF search/date checks; date displayed without time, original DateTime unchanged. No database access.");
                return 0;
            }
            var repair = new RepairLine(null, null, "صيانة", "فحص قاطع الكهرباء", "الكهرباء");
            var part = new MaterialLine("قاطع كهرباء", 2, "قطعة", 30);
            orderWindow.State.Repairs.Add(repair); orderWindow.State.Parts.Add(part);
            orderWindow.EditRepair(repair);
            var repairPanel = Find<Flyout>(orderWindow, "Config_Service_Windows");
            Wait(() => repairPanel.IsShown, "Repair Flyout failed to open.");
            Check(repairPanel.IsModal && repairPanel.IsPinned && repairPanel.Position == Position.Bottom, "Panel behavior differs from standard.");
            CheckDcmsPanel(orderWindow, repairPanel);
            Check(ReferenceEquals(repairPanel.DataContext, orderWindow.State), "Flyout lost inherited data context.");
            Check(ThemeManager.GetThemeName(repairPanel) == "Win11Light", "Flyout theme not set.");
            Find<TextEdit>(orderWindow, "Text_dtils_Update").EditValue = "مسودة لا تحفظ";
            CloseNative(repairPanel);
            Check(orderWindow.State.Repairs.Single().Details == repair.Details, "Cancel saved edits.");
            Check(typeof(maintenance_Order).GetField("editingRepair", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(orderWindow) is null, "Native close retained editing reference.");
            orderWindow.EditMaterial(part); var partPanel = Find<Flyout>(orderWindow, "Config_Parts_Windows");
            Wait(() => partPanel.IsShown, "Material panel not open.");
            CheckDcmsPanel(orderWindow, partPanel);
            Snapshot(partPanel, Path.Combine(output, "material-edit.png"));
            Find<TextEdit>(orderWindow, "Text_Parts_Update").EditValue = "مادة معدلة";
            Click(Find<FrameworkElement>(orderWindow, "Update_Parts_Button")); Pump();
            Check(orderWindow.State.Parts.Single().Name == "مادة معدلة" && !partPanel.IsOpen, "Local edit save stopped working.");
            orderWindow.EditRepair(repair); Wait(() => repairPanel.IsShown, "Repair reopen failed.");
            Click(Find<FrameworkElement>(orderWindow, "search_button"));
            var searchPanel = Find<Flyout>(orderWindow, "adv_searsh");
            Wait(() => searchPanel.IsShown, "Order search not open.");
            CheckDcmsPanel(orderWindow, searchPanel);
            CheckDateCell(orderWindow);
            Check(!repairPanel.IsOpen && !partPanel.IsOpen, "Multiple panels left open.");
            Check(ReferenceEquals(Find<ComboBoxEdit>(orderWindow, "Combo_emp_Search").ItemsSource, orderWindow.State.SearchEmployees), "Search lookup binding missing.");
            foreach (var size in new[] { new Size(1000, 700), new Size(1366, 768), new Size(1920, 1080) })
            {
                orderWindow.Width = size.Width; orderWindow.Height = size.Height; Pump();
                CheckBounds(orderWindow, "from_date", "To_date", "Combo_emp_Search", "Search_Grid", "Search_Advance_Button");
                Snapshot(searchPanel, Path.Combine(output, $"order-search-{size.Width}.png"));
            }
            Escape(orderWindow); Wait(() => !searchPanel.IsOpen, "Escape failed.");
            orderWindow.EditRepair(repair); Wait(() => repairPanel.IsShown, "Repair reopen failed.");
            foreach (var size in new[] { new Size(1000, 700), new Size(1366, 768), new Size(1920, 1080) })
            {
                orderWindow.Width = size.Width; orderWindow.Height = size.Height; Pump();
                CheckBounds(orderWindow, "Combo_Management_Update", "Text_dtils_Update", "Update_Button");
                Snapshot(repairPanel, Path.Combine(output, $"repair-edit-{size.Width}.png"));
            }
            Escape(orderWindow);

            var complete = new maintenance_complete(); Show(complete);
            complete.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(complete), 0, Key.F3) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            var completeSearch = Find<Flyout>(complete, "adv_searsh"); Wait(() => completeSearch.IsShown, "F3 completion search failed.");
            CheckDcmsPanel(complete, completeSearch);
            Check(ReferenceEquals(completeSearch.DataContext, complete.State), "Completion search lost data context.");
            foreach (var size in new[] { new Size(1000, 700), new Size(1366, 768), new Size(1920, 1080) })
            {
                complete.Width = size.Width; complete.Height = size.Height; Pump();
                CheckBounds(complete, "from_date", "To_date", "Combo_emp_Search", "Doc_Type_Searsh", "Search_Grid", "OpenSearchResult");
                Snapshot(completeSearch, Path.Combine(output, $"completion-search-{size.Width}.png"));
            }
            Find<System.Windows.Controls.CheckBox>(complete, "By_State").IsChecked = true; Pump();
            var statuses = Find<ComboBoxEdit>(complete, "Doc_Type_Searsh"); statuses.EditValue = "معلقة"; statuses.ShowPopup(); Pump();
            Check(statuses.IsPopupOpen && completeSearch.IsOpen && statuses.EditValue?.ToString() == "معلقة", "Nested choice popup interferes with Flyout.");
            statuses.ClosePopup(); CloseNative(completeSearch);
            var order = new T_Emergency_MaintenanceCost
            {
                Location = "موقع المعاينة", Location_ID = 1, Branch_ID = 1, Emergency_MaintenanceCosts_No = 7202600001,
                Date_com = DateOnly.FromDateTime(DateTime.Today), Type = 2, T_Build = new T_Build { Build = "المبنى الإداري" },
                T_Service = new T_Service { Serv_type = "الكهرباء" }, T_Emp_Maints = [new() { T_Employe = new T_Employe { Emp_Name = "فني المعاينة" } }],
                T_Emergency_MaintenanceCosts_Services = Enumerable.Range(1, 45).Select(i => new T_Emergency_MaintenanceCosts_Service { ID = i, Maint_Type = 0, Statement = "فحص لوحة الكهرباء رقم " + i }).ToList()
            };
            using var completionReport = MaintenanceCompletionReport.Create(order, DateTime.Today, "موظف المعاينة", "تفاصيل التقرير الأصلي");
            using var rendered = MaintenanceReportRenderer.CreateDocument(completionReport);
            var document = rendered.Document;
            var documentPackageUri = PackUriHelper.GetPackageUri(((BitmapImage)((System.Windows.Controls.Image)document.Pages[0].Child.Children[0]).Source).UriSource);
            Check(PackageStore.GetPackage(documentPackageUri) is not null, "Preview package is not registered.");
            Check(document.Pages.Count > 1, "Multi-page RDLC truncated.");
            Check(document.Pages[0].Child.Width > 700 && document.Pages[0].Child.Height > 1000, "Paper dimensions changed.");
            // مسار WPF عند تحريك المؤشر يعيد فتح الصورة من عنوانها؛ العرض وحده لا يفحصه.
            foreach (var page in document.Pages)
            {
                var image = (System.Windows.Controls.Image)page.Child.Children[0];
                var reloaded = new BitmapImage(new Uri(image.Source.ToString()!, UriKind.RelativeOrAbsolute));
                Check(reloaded.PixelWidth == ((BitmapSource)image.Source).PixelWidth, "FixedDocument cannot reopen page image for selection.");
            }
            // إخراج المستند إلى XPS يمر بمسار الطباعة نفسه دون طابعة أو نافذة اختيار جهاز.
            string printPath = Path.Combine(output, "report-print.xps");
            if (File.Exists(printPath)) File.Delete(printPath);
            using (var xps = new XpsDocument(printPath, FileAccess.ReadWrite))
                XpsDocument.CreateXpsDocumentWriter(xps).Write(document.DocumentPaginator);
            using (var xps = new XpsDocument(printPath, FileAccess.Read))
                Check(xps.GetFixedDocumentSequence().DocumentPaginator.PageCount == document.Pages.Count, "Print output lost pages.");
            // يراقب المؤقت نافذة الطباعة خلال الحلقة الحوارية، ثم يعيد التحكم بعد إغلاقها.
            show_report InspectPreview(Action open, MetroWindow owner, Action<show_report> inspect)
            {
                show_report? found = null;
                Exception? error = null;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                timer.Tick += (_, _) =>
                {
                    found = app.Windows.OfType<show_report>().SingleOrDefault(item => item.IsVisible);
                    if (found is null) return;
                    timer.Stop();
                    try
                    {
                        Check(found.Owner == owner && owner.FindName("MaintenancePrintFlyout") is null, "Report preview is not an independent owned window.");
                        inspect(found);
                    }
                    catch (Exception ex) { error = ex; }
                    finally { if (found.IsVisible) found.Close(); }
                };
                timer.Start();
                try { open(); }
                finally { timer.Stop(); }
                if (error is not null) throw error;
                return found ?? throw new Exception("show_report did not open.");
            }

            Uri? viewerPackageUri = null;
            var preview = InspectPreview(() => MaintenanceCompletionReport.ShowPreview(complete, completionReport), complete, opened =>
            {
                var viewer = Find<DocumentViewer>(opened, "PreviewViewer");
                var viewerDocument = (FixedDocument)viewer.Document;
                viewerPackageUri = PackUriHelper.GetPackageUri(((BitmapImage)((System.Windows.Controls.Image)viewerDocument.Pages[0].Child.Children[0]).Source).UriSource);
                Check(viewer.PageCount == document.Pages.Count && ApplicationCommands.Print.CanExecute(null, viewer), "Print/page navigation unavailable.");
            });
            Check(Find<DocumentViewer>(preview, "PreviewViewer").Document is null, "Print resources retained.");
            Check(viewerPackageUri is not null && PackageStore.GetPackage(viewerPackageUri) is null &&
                PackageStore.GetPackage(documentPackageUri) is not null, "Closing one preview removed another package or retained its own.");

            complete.DisplayDocument(new(order, null));
            Find<DateEdit>(complete, "date_check").EditValue = DateTime.Today;
            Find<TextEdit>(complete, "Text_man").EditValue = "موظف المعاينة";
            InspectPreview(() => Click(Find<FrameworkElement>(complete, "Print_Button")), complete,
                opened => Check(!Find<FrameworkElement>(complete, "Print_Button").IsEnabled, "Print released its busy guard early."));
            Wait(() => Find<FrameworkElement>(complete, "Print_Button").IsEnabled, "Print close did not release busy guard.");
            using var orderReport = MaintenanceOrderReport.Create(order, "مراقب المعاينة");
            InspectPreview(() => MaintenanceOrderReport.ShowPreview(orderWindow, orderReport), orderWindow, Escape);
            Check(orderReport.Render("PDF").Length > 1000 && completionReport.Render("EXCELOPENXML").Length > 1000 && completionReport.Render("WORDOPENXML").Length > 1000, "Original export formats broken.");
            complete.Close();

            rendered.Dispose(); rendered.Dispose();
            Check(PackageStore.GetPackage(documentPackageUri) is null, "Disposed document retained registered image package.");
            orderWindow.Close(); app.Shutdown(); Console.WriteLine($"PASS: {checks} Flyout, layout, editing, theme, image reopen, XPS print, multi-page export and package cleanup checks."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion
}
