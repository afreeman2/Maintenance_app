using DevExpress.Xpf.Core;
using Maintenance_app.orders_Windows;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Application = System.Windows.Application;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using MenuItem = System.Windows.Controls.MenuItem;
using Microsoft.Win32;
using System.Reflection;

internal static class LaunchProbe
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Maintenance_app.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        // Load the real resources without running the login/registration startup flow
        // from this test executable's different output directory.
        var startup = typeof(Maintenance_app.App).GetMethod("Application_Startup", BindingFlags.NonPublic | BindingFlags.Instance)!;
        app.Startup -= (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, startup);
        Exception? failure = null;
        app.DispatcherUnhandledException += (_, e) =>
        {
            failure = e.Exception;
            e.Handled = true;
            foreach (var child in app.Windows.OfType<maintenance_Order>().ToArray()) child.Close();
        };
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var source = XDocument.Load(Path.Combine(args[0], "Maintenance_app", "App.xaml"));
            XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var resources = source.Root!.Element(p + "Application.Resources")!.Element(p + "ResourceDictionary")!;
            foreach (var attribute in source.Root.Attributes().Where(a => a.IsNamespaceDeclaration))
            {
                string value = attribute.Value;
                if (value.StartsWith("clr-namespace:Maintenance_app") && !value.Contains(";assembly="))
                    value += ";assembly=Maintenance_app";
                resources.SetAttributeValue(attribute.Name, value);
            }
            foreach (var dictionary in resources.Descendants(p + "ResourceDictionary"))
            {
                var location = dictionary.Attribute("Source");
                if (location is not null && !location.Value.StartsWith("pack:"))
                    location.Value = "pack://application:,,,/Maintenance_app;component/" + location.Value;
            }
            string resourceText = System.Text.RegularExpressions.Regex.Replace(resources.ToString(),
                "clr-namespace:Maintenance_app(?:\\.[^;\"\\s]+)?(?=\")", match => match.Value + ";assembly=Maintenance_app");
            app.Resources = (ResourceDictionary)XamlReader.Parse(resourceText);
            ApplicationThemeHelper.ApplicationThemeName = "None";
            Maintenance_app.App.ConnectionString = string.Empty;
            int connectionArgument = Array.IndexOf(args, "--connection-file");
            if (connectionArgument >= 0)
            {
                using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                    Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
                const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SSF\Maintenance_services\";
                using var user = registry.OpenSubKey(path + "NameUserSQL");
                using var password = registry.OpenSubKey(path + "PassUserSQL");
                string Decode(object? value) => value is string text ? Maintenance_app.App.decrypt(text)
                    : throw new InvalidOperationException("Missing connection configuration.");
                var create = typeof(Maintenance_app.App).Assembly.GetType("Maintenance_app.DatabContext.SqlConnectionSettings")!
                    .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
                Maintenance_app.App.ConnectionString = (string)create.Invoke(null,
                    [File.ReadAllText(args[connectionArgument + 1]), Decode(user?.GetValue("NameUserSQL")), Decode(password?.GetValue("PassUserSQL"))])!;
            }
            var main = new Maintenance_app.MainWindow
            {
                WindowState = args.Contains("--maximized-owner") ? WindowState.Maximized : WindowState.Normal,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -30000, Top = -30000, Width = 1366, Height = 810,
                ShowInTaskbar = false, ShowActivated = args.Contains("--maximized-owner")
            };
            main.Show();
            main.UpdateLayout();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var target = (MenuItem)main.FindName("inout_Menu");
            Console.WriteLine("MENU_ENABLED=" + target.IsEnabled);
            var buttons = Descendants(main).OfType<SimpleButton>().Where(button =>
                button.DataContext is Maintenance_app.MainWindow.WorkspaceAction action && action.Target == target).ToArray();
            Console.WriteLine("MATCHING_MAIN_BUTTONS=" + buttons.Length);
            if (buttons.Length == 0) throw new InvalidOperationException("No maintenance action button was generated.");
            var command = Maintenance_app.MainWindow.OpenMaintenanceOrderCommand;
            if (!command.InputGestures.OfType<System.Windows.Input.KeyGesture>().Any(gesture => gesture.Key == System.Windows.Input.Key.F7))
                throw new InvalidOperationException("F7 is not registered.");
            target.IsEnabled = false;
            if (command.CanExecute(null, main)) throw new InvalidOperationException("The shortcut bypasses disabled access.");
            target.IsEnabled = true;

            foreach (var button in buttons)
            {
                var point = button.TransformToAncestor(main).Transform(new System.Windows.Point(button.ActualWidth / 2, button.ActualHeight / 2));
                var hit = main.InputHitTest(point) as DependencyObject;
                bool reachable = false;
                for (var candidate = hit; candidate is not null; candidate = candidate is Visual ? VisualTreeHelper.GetParent(candidate) : null)
                    if (candidate == button) { reachable = true; break; }
                Console.WriteLine("BUTTON_INPUT_REACHABLE=" + reachable + ";ENABLED=" + button.IsEnabled
                    + ";VISIBLE=" + button.IsVisible + ";HIT=" + hit?.GetType().Name);
                if (!reachable) throw new InvalidOperationException("Another element intercepts clicks on the maintenance button.");
            }
            var launchers = buttons.Select(button => (Action)(() => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)))).ToList();
            launchers.Add(() => command.Execute(null, main));
            if (args.Contains("--hold-render")) launchers = launchers.Take(1).ToList();
            foreach (var launch in launchers)
            {
                bool opened = false;
                bool maximized = false;
                DateTime? firstSeen = null;
                var start = DateTime.UtcNow;
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (_, _) =>
                {
                    var child = app.Windows.OfType<maintenance_Order>().FirstOrDefault(window => window.IsVisible);
                    if (child is not null)
                    {
                        firstSeen ??= DateTime.UtcNow;
                        if (DateTime.UtcNow - firstSeen > TimeSpan.FromMilliseconds(args.Contains("--hold-render") ? 25000 : connectionArgument >= 0 ? 1800 : 600))
                        {
                            child.UpdateLayout();
                            var content = (FrameworkElement)child.Content;
                            var repairs = (FrameworkElement)child.FindName("Repiers_Grid");
                            var materials = (FrameworkElement)child.FindName("Parts_Grid");
                            Console.WriteLine("NATIVE_RENDER_SIZE=" + content.ActualWidth + "x" + content.ActualHeight
                                + ";REPAIRS=" + repairs.ActualWidth + "x" + repairs.ActualHeight
                                + ";MATERIALS=" + materials.ActualWidth + "x" + materials.ActualHeight);
                            if (connectionArgument >= 0)
                                Console.WriteLine("LIVE_LOOKUPS=" + child.State.Buildings.Count + "/" + child.State.Employees.Count
                                    + "/" + child.State.Services.Count + "/" + child.State.Managements.Count + "/" + child.State.AllDepartments.Count);
                            if (content.ActualWidth < 900 || content.ActualHeight < 600
                                || repairs.ActualWidth < 700 || repairs.ActualHeight < 50
                                || materials.ActualWidth < 700 || materials.ActualHeight < 50)
                                failure = new InvalidOperationException("The native order dialog is visible but its content did not render.");
                            try { HeaderFocusRegression.Run(child); }
                            catch (Exception ex) { failure = ex; }
                            opened = true;
                            maximized = child.WindowState == WindowState.Maximized && child.Owner == main;
                            child.Close();
                        }
                    }
                    if (opened || failure is not null || DateTime.UtcNow - start > TimeSpan.FromSeconds(4))
                    {
                        timer.Stop();
                        frame.Continue = false;
                    }
                };
                timer.Start();
                launch();
                if (!opened && failure is null) Dispatcher.PushFrame(frame);
                timer.Stop();
                if (failure is not null) throw failure;
                if (!opened) throw new InvalidOperationException("Clicking the main maintenance button did not open the order window.");
                if (!maximized) throw new InvalidOperationException("The maintenance window did not open maximized with the main window as owner.");
            }
            Console.WriteLine("PASS: both main-window maintenance buttons and F7's command open the owned order dialog maximized with actual application resources and None theme; disabled access is respected.");
            app.Shutdown();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            app.Shutdown(1);
            return 1;
        }
    }
}
