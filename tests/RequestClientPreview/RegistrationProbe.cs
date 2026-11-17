using DevExpress.Xpf.Core;
using Maintenance_request.data;
using Maintenance_request.database_cotext;
using Maintenance_request.Registration;
using Maintenance_request.user_requst_windows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

internal static class RegistrationProbe
{
    #region أدوات فحص التسجيل والقاعدة المعزولة
    private static int checks, dialogs;
    private static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    private static T Find<T>(FrameworkElement owner, string name) where T : class => (T)owner.FindName(name);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    // نفحص النص المرئي نفسه؛ وجود حد أحمر وحده لا يثبت أن المستخدم يرى سبب الخطأ.
    private static void CheckErrorText(Register_Windows window, string name, string expected)
    {
        Pump(); var message = Find<TextBlock>(window, name);
        Check(message.IsVisible && message.ActualHeight > 0 && message.Text == expected, "Missing visible Arabic error: " + name);
    }
    private static void CheckErrorCleared(Register_Windows window, string name)
    {
        Pump(); var message = Find<TextBlock>(window, name);
        Check(message.Visibility == Visibility.Collapsed && string.IsNullOrEmpty(message.Text), "Error message retained: " + name);
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task Reject(Func<Task> action)
    {
        bool rejected = false;
        try { await action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Expected registration rejection.");
    }
    private sealed class Gate : DbCommandInterceptor
    {
        public bool Pause;
        public TaskCompletionSource Entered = new(), Release = new();
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Pause) { Pause = false; Entered.SetResult(); await Release.Task; }
            return result;
        }
    }
    private sealed class Context(DbContextOptions<Cont_maintContext> options, Func<bool> fail) : Cont_maintContext(options)
    {
        public override async Task<int> SaveChangesAsync(CancellationToken token = default)
        {
            int result = await base.SaveChangesAsync(token);
            if (fail()) throw new InvalidOperationException("فشل اصطناعي بعد الكتابة وقبل اعتماد التسجيل");
            return result;
        }
    }
    private static void Capture(Register_Windows window, string path)
    {
        var input = Find<TextBox>(window, "txtepm_name");
        FrameworkElement root = (FrameworkElement)VisualTreeHelper.GetParent(AdornerLayer.GetAdornerLayer(input)); root.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(-1, 1, root.ActualWidth / 2, 0));
            context.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight)); context.Pop();
        }
        image.Render(drawing); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); png.Save(file);
    }
    #endregion

    #region سيناريوهات القاعدة والواجهة والرسائل
    private static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var gate = new Gate(); bool failSave = false;
        var options = new DbContextOptionsBuilder<Cont_maintContext>().UseSqlite(connection).AddInterceptors(gate).Options;
        Cont_maintContext Create() => new Context(options, () => failSave);
        using (var db = Create())
        {
            await db.Database.EnsureCreatedAsync();
            db.T_Mangment_Requests.Add(new() { Mangment_Request_ID = 7, Mangment_Request = "إدارة اختبار" });
            db.T_Builds.Add(new() { Build_ID = 2, Location = "موقع اختبار", Location_ID = 1, Branch_ID = 1, Build = "مبنى اختبار" });
            await db.SaveChangesAsync();
        }
        var store = new RegistrationStore(Create);
        var draft = new RegistrationDraft("اسم محفوظ", "abc", "abc", "PC-TEST", 7, 2);
        var created = await store.RegisterAsync(draft);
        Check(created.User_ID > 0 && Maintenance_request.App.decrypt(created.PassWord) == draft.Password, "Registration or login encoding changed.");
        Check(created.State && created.ISsound == true && created.Sound_CHoise == 2 && created.Build_ID == 2 && created.Mangment_Request_ID == 7, "User defaults or lookup mapping changed.");
        await Reject(() => store.RegisterAsync(draft));
        await store.RegisterAsync(draft with { ComputerId = "OTHER-PC" });
        await store.RegisterAsync(draft with { Name = "اسم_%" });
        await store.RegisterAsync(draft with { Name = "اسم_حرفي" });
        Check(RegistrationStore.PasswordError("كلمة", "كلمة") is null && RegistrationStore.PasswordError("كلمات", "كلمات") is not null, "Encoded UTF-8 column length ignored.");
        foreach (var invalid in new[] { draft with { Name = " " }, draft with { Password = "ab", Confirmation = "ab" }, draft with { Confirmation = "wrong" }, draft with { Password = "abcdefghij", Confirmation = "abcdefghij" }, draft with { ComputerId = "" }, draft with { ManagementId = 90 }, draft with { BuildingId = 90 } })
            await Reject(() => store.RegisterAsync(invalid with { Name = invalid.Name == " " ? " " : "اسم جديد" }));
        int before;
        using (var db = Create()) before = await db.T_Maintenance_request_Users.CountAsync();
        failSave = true; await Reject(() => store.RegisterAsync(draft with { Name = "اختبار التراجع" })); failSave = false;
        using (var db = Create()) Check(await db.T_Maintenance_request_Users.CountAsync() == before, "Failed registration transaction was committed.");

        int navigations = 0;
        var previous = new T_Maintenance_request_User { User_ID = 900, User_Name = "حساب سابق" }; Maintenance_request.App.curr_user = previous;
        var window = new Register_Windows(Create, () => "PC-TEST", _ => navigations++) { WindowState = WindowState.Normal, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false };
        await window.LoadDataAsync(); window.Show(); Pump();
        var name = Find<TextBox>(window, "txtepm_name"); var password = Find<PasswordBox>(window, "txb_new_Password");
        var confirmation = Find<PasswordBox>(window, "txb_sh_Password"); var management = Find<ComboBox>(window, "cbomangment"); var building = Find<ComboBox>(window, "_Combo_Bulding");
        Check(window.mang_list.Count == 1 && window.building_list.Count == 1 && ReferenceEquals(management.ItemsSource, window.mang_list), "Lookup loading or ItemsSource binding failed.");
        Check(management.DisplayMemberPath == "Mangment_Request" && building.SelectedValuePath == "Build_ID", "Lookup display/value mapping failed.");
        Check(!new Control[] { name, password, confirmation, management, building }.Any(Validation.GetHasError), "Eager validation on open.");
        Find<FrameworkElement>(window, "save_User").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Check(Validation.GetHasError(name), "Save click did not validate empty name.");
        CheckErrorText(window, "NameError", "أدخل اسم المستخدم بما لا يتجاوز100 حرف.");
        Check(AdornerLayer.GetAdornerLayer(name)?.GetAdorners(name)?.Any(x => x.IsVisible) == true, "Validation has no visible error adorner.");
        Capture(window, Path.Combine(output, "registration-validation.png"));
        // الخلفية الشفافة تتحقق من البكسل الناتج، وليس من قيمة الخاصية وحدها.
        Check(window.AllowsTransparency && ((SolidColorBrush)window.Background).Color.A == 0 && ((SolidColorBrush)Find<Border>(window, "RegistrationCard").Background).Color.A == 255, "Transparent window or readable registration card changed.");
        var captured = new BitmapImage(); captured.BeginInit(); captured.CacheOption = BitmapCacheOption.OnLoad; captured.UriSource = new Uri(Path.GetFullPath(Path.Combine(output, "registration-validation.png"))); captured.EndInit();
        var pixels = new FormatConvertedBitmap(captured, PixelFormats.Bgra32, null, 0); byte[] background = new byte[4]; pixels.CopyPixels(new Int32Rect(0, 0, 1, 1), background, 4, 0);
        Check(background[3] == 0, "Rounded exterior is not transparent.");
        name.Text = "مستخدم واجهة"; Check(!Validation.GetHasError(name), "Typing did not clear name error.");
        CheckErrorCleared(window, "NameError");
        Check(!await window.SaveUserAsync() && Validation.GetHasError(password), "Empty password not focused.");
        CheckErrorText(window, "PasswordError", "أدخل كلمة المرور.");
        password.Password = "ab"; Check(!Validation.GetHasError(password), "Password change retained old error.");
        CheckErrorCleared(window, "PasswordError");
        Check(!await window.SaveUserAsync() && Validation.GetHasError(password), "Short password accepted.");
        password.Password = "abc"; Check(!await window.SaveUserAsync() && Validation.GetHasError(confirmation), "Missing confirmation not marked.");
        CheckErrorText(window, "ConfirmationError", "أدخل تأكيد كلمة المرور.");
        confirmation.Password = "other"; Check(!Validation.GetHasError(confirmation), "Confirmation edit retained error.");
        CheckErrorCleared(window, "ConfirmationError");
        Check(!await window.SaveUserAsync() && Validation.GetHasError(confirmation), "Mismatch accepted.");
        confirmation.Password = "abc"; Check(!await window.SaveUserAsync() && Validation.GetHasError(management), "Missing management accepted.");
        CheckErrorText(window, "ManagementError", "اختر الإدارة التابع لها.");
        management.IsDropDownOpen = true; management.SelectedIndex = 0; Pump();
        Check(!Validation.GetHasError(management), "Selection did not clear error while popup open."); management.IsDropDownOpen = false;
        CheckErrorCleared(window, "ManagementError");
        Check(!await window.SaveUserAsync() && Validation.GetHasError(building), "Missing building accepted.");
        CheckErrorText(window, "BuildingError", "اختر المبنى.");
        building.SelectedIndex = 0; Check(!Validation.GetHasError(building), "Building selection retained error.");
        CheckErrorCleared(window, "BuildingError");
        var surface = (Border)((FrameworkElement)name.Parent).Parent;
        Check(((SolidColorBrush)surface.Background).Color == (Color)ColorConverter.ConvertFromString("#DEE8F0"), "Filled field did not darken.");
        name.Text = " "; Check(((SolidColorBrush)surface.Background).Color == (Color)ColorConverter.ConvertFromString("#F6F8FA"), "Whitespace darkened input.");
        name.Text = "اسم محفوظ";
        Check(!await window.SaveUserAsync() && Validation.GetHasError(name), "Duplicate name did not become inline error.");
        Check(ReferenceEquals(Maintenance_request.App.curr_user, previous), "Rejected registration changed current user.");
        name.Text = "مستخدم واجهة"; Capture(window, Path.Combine(output, "registration-filled.png"));
        // استخدام الرسائل الحقيقية مع إغلاقها آليًا؛ لا يغيّر الاختبار ثيم النافذة أو التطبيق.
        Exception? messageFailure = null;
        var dismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        dismiss.Tick += (_, _) =>
        {
            foreach (var dialog in Application.Current.Windows.Cast<Window>().Where(x => x is ThemedWindow).ToArray())
            {
                var ok = Walk(dialog).OfType<ThemedWindowDialogButton>().FirstOrDefault(x => x.IsVisible);
                if (ok is null) continue;
                try { dialogs++; ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(ok).GetPattern(PatternInterface.Invoke)).Invoke(); }
                catch (Exception ex) { messageFailure = ex; dialog.Close(); }
            }
        };
        dismiss.Start();
        try
        {
            gate.Pause = true;
            var saving = window.SaveUserAsync(); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!await window.SaveUserAsync(), "Double save was not blocked.");
            gate.Release.SetResult(); Check(await saving, "Valid user did not save.");
            if (messageFailure is not null) throw messageFailure;
            Check(navigations == 1 && Maintenance_request.App.curr_user.User_Name == "مستخدم واجهة", "Successful registration did not set current user and navigate.");
            Check(password.Password == "" && confirmation.Password == "", "Passwords retained after registration.");
            Check(ThemeManager.GetThemeName(window) == "Win11Light" && ApplicationThemeHelper.ApplicationThemeName == "None", "Message switched theme.");
            using (var db = Create()) Check(await db.T_Maintenance_request_Users.CountAsync(x => x.User_Name == "مستخدم واجهة") == 1, "Concurrent save created duplicate user.");
            Check(dialogs == 1, "Expected success message missing or duplicated.");
        }
        finally { dismiss.Stop(); window.Close(); }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            foreach (string resource in new[] { "Styles/Controls.xaml", "Styles/Fonts.xaml", "Styles/Themes/Light.Steel.xaml" }) app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MahApps.Metro;component/" + resource) });
            ApplicationThemeHelper.ApplicationThemeName = "None";
            var task = RunAsync(args[0]); var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); task.GetAwaiter().GetResult();
            app.Shutdown(); Console.WriteLine($"PASS: {checks} registration store/live WPF checks; lookups, inline validation, popup selection, duplicates, literal names, encoded length, rollback, double-save, success navigation and stable themes. SQLite only."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion
}
