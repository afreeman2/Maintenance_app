using Maintenance_app.Models;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Size = System.Windows.Size;

internal static class PalettePreview
{
    private static void Render(Window window, Size size, string path)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(size);
        content.Arrange(new Rect(size));
        content.UpdateLayout();
        content.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        window.Close();
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
            DevExpress.Xpf.Core.ApplicationThemeHelper.ApplicationThemeName = "Win11Light";
            Directory.CreateDirectory(args[0]);
            Maintenance_app.App.ConnectionString = string.Empty;
            Maintenance_app.App.usname = "مستخدم المعاينة";
            var main = new Maintenance_app.MainWindow();
            typeof(Maintenance_app.MainWindow).GetField("confirmedClose", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, true);
            var mainLoaded = typeof(Maintenance_app.MainWindow).GetMethod("MetroWindow_Loaded", BindingFlags.NonPublic | BindingFlags.Instance)!;
            main.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), main, mainLoaded);
            Render(main, new Size(1920, 1080), Path.Combine(args[0], "theme-main-preview.png"));
            var login = new Maintenance_app.main_Windows.Login_Windows();
            var loginLoaded = typeof(Maintenance_app.main_Windows.Login_Windows).GetMethod("MetroWindow_Loaded", BindingFlags.NonPublic | BindingFlags.Instance)!;
            login.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), login, loginLoaded);
            Render(login, new Size(1080, 720), Path.Combine(args[0], "theme-login-preview.png"));
            app.Shutdown();
            Console.WriteLine("PASS: main and login palette previews rendered with actual application resources.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
