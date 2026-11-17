using DevExpress.Xpf.Core;
using Maintenance_app.Dashboard;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

internal static class MessageAutomationProbe
{
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static readonly List<AutomationPeer> captured = [];
    private static readonly HashSet<AutomationPeer> visited = [];
    private static void Inspect(AutomationPeer peer)
    {
        if (!visited.Add(peer)) return;
        captured.Add(peer);
        try { _ = peer.GetAutomationId(); _ = peer.GetName(); }
        catch (ElementNotAvailableException) { return; }
        catch (Exception ex)
        { throw new InvalidOperationException("Automation failed on " + peer.GetType().FullName, ex); }
        foreach (var child in peer.GetChildren() ?? []) Inspect(child);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Maintenance_app.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            var startup = typeof(Maintenance_app.App).GetMethod("Application_Startup", BindingFlags.NonPublic | BindingFlags.Instance)!;
            app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Maintenance_app;component/Resources/ModernTheme.xaml") });
            ApplicationThemeHelper.ApplicationThemeName = "None";
            var orphan = new ThemedWindowDialogButton { Content = "اختبار" };
            var orphanPeer = UIElementAutomationPeer.CreatePeerForElement(orphan);
            Console.WriteLine("DETACHED_BUTTON_PEER=" + orphanPeer.GetType().FullName);
            Inspect(orphanPeer);
            Maintenance_app.App.ConnectionString = string.Empty;
            var owner = new Maintenance_app.MainWindow { Title = "اختبار الرسائل", Width = 1366, Height = 768, WindowState = WindowState.Normal,
                Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false, ShowActivated = false };
            typeof(Maintenance_app.MainWindow).GetField("confirmedClose", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, true);
            var loaded = typeof(Maintenance_app.MainWindow).GetMethod("MetroWindow_Loaded", BindingFlags.NonPublic | BindingFlags.Instance)!;
            owner.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), owner, loaded);
            owner.Show();
            string ownerTheme = ThemeManager.GetThemeName(owner);
            Inspect(UIElementAutomationPeer.CreatePeerForElement(owner));
            var incomingShow = args.Length == 0 ? null : Assembly.LoadFrom(args[0]).GetType("IncomingTransactions.UiMessages", throwOnError: true)!
                .GetMethod("Show", [typeof(Window), typeof(string), typeof(string), typeof(MessageBoxButton), typeof(MessageBoxImage)])!;
            for (int iteration = 0; iteration < (incomingShow is null ? 5 : 9); iteration++)
            {
                bool accept = iteration >= 7;
                Exception? failure = null;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                timer.Tick += (_, _) =>
                {
                    var dialog = app.Windows.Cast<Window>().FirstOrDefault(x => x != owner && x.IsVisible);
                    if (dialog is null) return;
                    timer.Stop();
                    try
                    {
                        dialog.UpdateLayout();
                        foreach (var visual in Visuals(dialog).OfType<UIElement>())
                        {
                            var peer = UIElementAutomationPeer.CreatePeerForElement(visual);
                            if (peer is not null) Inspect(peer);
                        }
                        var buttons = Visuals(dialog).OfType<ThemedWindowDialogButton>().ToList();
                        Console.WriteLine("DIALOG_BUTTONS=" + string.Join(",", buttons.Select(x => x.Content)));
                        // نتأكد من العناوين الظاهرة والقيمة الافتراضية عند تحويل الاستدعاءات المباشرة.
                        if (iteration >= 3)
                        {
                            bool confirm = iteration % 2 == 0 || accept;
                            string[] expected = confirm ? ["نعم", "لا"] : ["موافق"];
                            if (!buttons.Select(x => x.Content?.ToString()).Order().SequenceEqual(expected.Order()))
                                throw new InvalidOperationException("Arabic dialog captions did not match.");
                            if (confirm && !buttons.Any(x => x.IsDefault && Equals(x.Content, "لا")))
                                throw new InvalidOperationException("Confirmation no longer defaults to No.");
                        }
                        var selectedButton = accept ? buttons.Single(x => Equals(x.Content, "نعم"))
                            : buttons.FirstOrDefault(x => x.IsCancel) ?? buttons.First();
                        var buttonPeer = UIElementAutomationPeer.CreatePeerForElement(selectedButton);
                        ((IInvokeProvider)buttonPeer.GetPattern(PatternInterface.Invoke)).Invoke();
                    }
                    catch (Exception ex) { failure = ex; dialog.Close(); }
                };
                timer.Start();
                if (iteration == 0) _ = UiMessages.Show("رسالة اختبار إمكانية الوصول", "اختبار");
                else if (iteration == 1) _ = UiMessages.Confirm(owner, "هل تريد المتابعة؟");
                else if (iteration == 2)
                {
                    void ConfirmClosing(object? sender, System.ComponentModel.CancelEventArgs args) =>
                        args.Cancel = !UiMessages.Confirm(owner, "هل تريد إغلاق المنظومة؟", closing: true);
                    owner.Closing += ConfirmClosing;
                    owner.Close();
                    owner.Closing -= ConfirmClosing;
                    if (!owner.IsVisible) throw new InvalidOperationException("Canceling close must keep the owner open.");
                }
                else
                {
                    bool confirm = iteration % 2 == 0 || accept;
                    var buttons = confirm ? MessageBoxButton.YesNo : MessageBoxButton.OK;
                    var icon = confirm ? MessageBoxImage.Question : MessageBoxImage.Error;
                    var result = iteration < 5 || iteration == 7
                        ? UiMessages.Show(confirm ? owner : null, "اختبار", "رسالة اختبار", buttons, icon)
                        : (MessageBoxResult)incomingShow!.Invoke(null, [confirm ? owner : null, "اختبار", "رسالة اختبار", buttons, icon])!;
                    var expected = accept ? MessageBoxResult.Yes : confirm ? MessageBoxResult.No : MessageBoxResult.OK;
                    if (result != expected)
                        throw new InvalidOperationException("Arabic button returned a different action.");
                }
                timer.Stop();
                if (failure is not null) throw failure;
                if (ApplicationThemeHelper.ApplicationThemeName != "None")
                    throw new InvalidOperationException("Displaying a message changed the application's theme.");
                if (ThemeManager.GetThemeName(owner) != ownerTheme)
                    throw new InvalidOperationException("Displaying a message changed the live owner's local theme.");
                foreach (var peer in captured)
                {
                    try { _ = peer.GetAutomationId(); }
                    catch (ElementNotAvailableException) { }
                    catch (Exception ex) { throw new InvalidOperationException("Detached automation failed on " + peer.GetType().FullName, ex); }
                }
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
            owner.Close(); app.Shutdown();
            Console.WriteLine("PASS: message and confirmation dialogs; live/detached automation IDs; repeated show and close. No database writes.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
