using Maintenance_request.data;
using Maintenance_request.database_cotext;
using Maintenance_request.Requests;
using Maintenance_request.Registration;
using Maintenance_request.user_requst_windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class DesignProbe
{
    #region أدوات الفحص واللقطات دون تشغيل بيانات المستخدم
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static T Find<T>(FrameworkElement window, string name) where T : class => (T)window.FindName(name);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    { yield return root; for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Walk(VisualTreeHelper.GetChild(root,i))) yield return child; }
    private static Rect Bounds(FrameworkElement root, FrameworkElement field) => field.TransformToAncestor(root).TransformBounds(new Rect(field.RenderSize));
    private static void Capture(Window window, Size size, string path)
    {
        window.WindowState = WindowState.Normal; window.Width = size.Width; window.Height = size.Height;
        var root = (FrameworkElement)window.Content; root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); Pump(); root.UpdateLayout();
        SaveVisual(root, size, path);
    }
    // نحفظ لقطة من العناصر المعروضة بعد انتهاء التخطيط من دون تغيير أبعادها.
    private static void SaveVisual(Visual root, Size size, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96,96,PixelFormats.Pbgra32); bitmap.Render(root);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); png.Save(file);
    }
    // يعزل الفرشاة محتوى البطاقة عن موضعها داخل النافذة، ويصحح انعكاس لقطة العنصر العربي.
    private static void SaveFlyoutCard(Border card, string path)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushTransform(new ScaleTransform(-1, 1, card.ActualWidth / 2, 0));
            drawing.DrawRectangle(new VisualBrush(card), null, new Rect(card.RenderSize));
            drawing.Pop();
        }
        SaveVisual(visual, card.RenderSize, path);
    }
    private static async Task Rejected(Func<Task> action)
    {
        bool rejected = false; try { await action(); } catch (InvalidOperationException) { rejected = true; } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Expected backend rejection.");
    }
    private sealed class Gate : DbCommandInterceptor
    {
        public bool Pause; public TaskCompletionSource Entered = new(), Release = new();
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token=default)
        { if(Pause) { Pause=false; Entered.SetResult(); await Release.Task; } return result; }
    }
    private sealed class FailingContext(DbContextOptions<Cont_maintContext> options) : Cont_maintContext(options)
    {
        private int writes;
        public override async Task<int> SaveChangesAsync(CancellationToken token=default)
        { int result=await base.SaveChangesAsync(token); if(++writes==2) throw new InvalidOperationException("Synthetic failure after both writes."); return result; }
    }
    #endregion

    #region فحص المعاملات والملكية وكلمات المرور والحفظ المزدوج والتحجيم
    private static async Task Run(string output, bool editOnly)
    {
        Directory.CreateDirectory(output);
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var gate = new Gate();
        var options = new DbContextOptionsBuilder<Cont_maintContext>().UseSqlite(connection).AddInterceptors(gate).Options;
        Cont_maintContext Create() => new(options);
        using(var db=Create())
        {
            db.Database.EnsureCreated();
            db.T_Mangment_Requests.Add(new(){Mangment_Request_ID=7,Mangment_Request="إدارة اختبار"});
            db.T_Builds.Add(new(){Build_ID=2,Location="موقع اختبار",Location_ID=1,Branch_ID=1,Build="مبنى اختبار"});
            db.T_Services.Add(new(){Serv_num=3,Location="موقع اختبار",Location_ID=1,Branch_ID=1,Serv_type="خدمة تجريبية"});
            db.SaveChanges();
        }
        var registration=new RegistrationStore(Create);
        var owner=await registration.RegisterAsync(new("موظف تجريبي","abc","abc","PC-TEST",7,2));
        var other=await registration.RegisterAsync(new("حساب آخر","abc","abc","OTHER-PC",7,2));
        Maintenance_request.App.curr_user=owner;
        var composer=new RequestComposerStore(Create);
        long id=await composer.SendAsync(owner.User_ID,"  بيان صيانة  ",3);
        using(var db=Create())
        {
            var header=await db.T_Notif_Action_Users.SingleAsync(x=>x.ID==id);
            var action=await db.T_Notif_Actions.SingleAsync(x=>x.DOC_ID==id);
            Check(header.Description=="بيان صيانة" && header.OP_ID==1 && header.state==0 && header.MaintenanceCosts_State==false,"New request protocol changed.");
            Check(action.DOC_ID==id && action.User_ID==owner.User_ID && action.OP_ID==1 && action.Branch_ID==1 && action.rendate==header.rendate,"Request and delivery are not one linked event.");
            Check(header.Build=="مبنى اختبار" && header.Mangment_Request=="إدارة اختبار" && header.Serv_type=="خدمة تجريبية","Request metadata lost.");
        }
        await Rejected(()=>composer.SendAsync(owner.User_ID," ",3));
        await Rejected(()=>composer.SendAsync(owner.User_ID,"طلب",99));
        await Rejected(()=>composer.SendAsync(999,"طلب",3));
        int before; using(var db=Create()) before=await db.T_Notif_Action_Users.CountAsync();
        await Rejected(()=>new RequestComposerStore(()=>new FailingContext(options)).SendAsync(owner.User_ID,"فشل اصطناعي",3));
        using(var db=Create()) Check(await db.T_Notif_Action_Users.CountAsync()==before && await db.T_Notif_Actions.CountAsync()==before,"Failed send left a partial header/action.");
        await composer.SendAsync(other.User_ID,"طلب حساب آخر",3);
        var found=await composer.SearchAsync(owner.User_ID,DateOnly.FromDateTime(DateTime.Today),DateOnly.FromDateTime(DateTime.Today));
        Check(found.Count==1 && found[0].User_ID==owner.User_ID,"Search exposed another owner's request.");
        using(var db=Create()){var row=await db.T_Notif_Action_Users.SingleAsync(x=>x.ID==id); row.OP_ID=2;await db.SaveChangesAsync();}
        Check((await composer.SearchAsync(owner.User_ID,DateOnly.FromDateTime(DateTime.Today),DateOnly.FromDateTime(DateTime.Today))).Count==0,"Search allowed editing executing request.");
        await Rejected(()=>composer.SearchAsync(owner.User_ID,DateOnly.FromDateTime(DateTime.Today),DateOnly.FromDateTime(DateTime.Today.AddDays(-1))));
        var accounts=new AccountStore(Create);
        Check(await accounts.AuthenticateAsync(owner.User_ID,"OTHER-PC","abc") is null,"Sign-in accepted another device.");
        Check(await accounts.AuthenticateAsync(owner.User_ID,"PC-TEST","bad") is null,"Sign-in accepted wrong password.");
        Check((await accounts.AuthenticateAsync(owner.User_ID,"PC-TEST","abc"))?.User_ID==owner.User_ID,"Valid sign-in failed.");
        await Rejected(()=>accounts.ChangePasswordAsync(owner.User_ID,"wrong","xyz","xyz"));
        await Rejected(()=>accounts.ChangePasswordAsync(owner.User_ID,"abc","abcdefghij","abcdefghij"));
        await accounts.ChangePasswordAsync(owner.User_ID,"abc","xyz","xyz");
        Check(await accounts.AuthenticateAsync(owner.User_ID,"PC-TEST","abc") is null && await accounts.AuthenticateAsync(owner.User_ID,"PC-TEST","xyz") is not null,"Changed password not used by fresh sign-in.");
        var messages=new List<string>();
        var config=new Config_Request_Window(Create,(text,_)=>messages.Add(text)); await config.LoadDataAsync();
        var begin=Find<DatePicker>(config,"Date_Begin"); var end=Find<DatePicker>(config,"Date_End");
        begin.SelectedDate=DateTime.Today;end.SelectedDate=DateTime.Today;
        Find<MahApps.Metro.Controls.Flyout>(config,"adv_searsh").IsOpen=true;
        await config.SearchAsync();
        var grid=Find<DevExpress.Xpf.Grid.GridControl>(config,"Search_Grid");
        Check(grid.ItemsSource is IEnumerable<T_Notif_Action_User> rows && rows.All(x=>x.User_ID==owner.User_ID && x.OP_ID==1),"UI search owner/state filter failed.");
        typeof(Config_Request_Window).GetMethod("cancel_search_Click",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(config,[config,new RoutedEventArgs()]);
        Check(begin.SelectedDate is null && end.SelectedDate is null && grid.ItemsSource is null,"Search close retained dates/results.");
        Check(Find<MahApps.Metro.Controls.Flyout>(config,"EditorPanel").IsOpen,"Editing is not hosted by its XAML flyout.");
        config.Close();
        // نفتح فوق نافذة مالكة مزاحة عن وسط الشاشة لنرصد أي تمركز على سطح المكتب أو خروج عن الإطار.
        foreach (var size in new[] { new Size(1280, 800), new Size(1000, 660) })
        {
            var host = new Window
            {
                Left = SystemParameters.WorkArea.Left + 37, Top = SystemParameters.WorkArea.Top + 29,
                Width = size.Width, Height = size.Height, WindowStyle = WindowStyle.None, ShowInTaskbar = false
            };
            host.Show();
            config = new Config_Request_Window(Create, (text, _) => messages.Add(text))
            {
                Owner = host, Width = host.ActualWidth, Height = host.ActualHeight
            };
            config.Show();
            await Task.Delay(1000);
            config.UpdateLayout();
            Check(Math.Abs(config.Left - host.Left) <= 2 && Math.Abs(config.Top - host.Top) <= 2
                && Math.Abs(config.ActualWidth - host.ActualWidth) <= 2 && Math.Abs(config.ActualHeight - host.ActualHeight) <= 2,
                $"Edit overlay does not match its owner at {size}: overlay {config.Left},{config.Top}; owner {host.Left},{host.Top}.");
            var editorCard = Find<Border>(config, "EditorCard");
            var searchCard = Find<Border>(config, "SearchCard");
            var searchFlyout = Find<MahApps.Metro.Controls.Flyout>(config, "adv_searsh");
            Rect ownerBounds = new(0, 0, host.ActualWidth, host.ActualHeight);
            void CheckCard(Border card, double maxWidth)
            {
                var bounds = Bounds(config, card);
                Check(ownerBounds.Contains(bounds) && Math.Abs(bounds.Left + bounds.Width / 2 - ownerBounds.Width / 2) <= 2
                    && Math.Abs(bounds.Top + bounds.Height / 2 - ownerBounds.Height / 2) <= 2,
                    $"{card.Name} is off-center or clipped at {size}. Card: {bounds}, owner: {ownerBounds}.");
                Check(Math.Abs(bounds.Width - Math.Min(maxWidth, ownerBounds.Width - card.Margin.Left - card.Margin.Right)) <= 2,
                    $"{card.Name} has the wrong width at {size}: {bounds.Width}.");
            }
            CheckCard(editorCard, 1080);
            if (size.Width == 1280)
                SaveFlyoutCard(editorCard, Path.Combine(output, "client-edit-request.png"));
            searchFlyout.IsOpen = true;
            await Task.Delay(1000);
            config.UpdateLayout();
            CheckCard(searchCard, 1040);
            if (size.Width == 1280)
                SaveFlyoutCard(searchCard, Path.Combine(output, "client-search-request.png"));
            Check(Math.Abs(Find<DevExpress.Xpf.Grid.GridControl>(config, "Search_Grid").ActualHeight - 280) <= 1,
                "The search result grid lost its reference height.");
            Check(Panel.GetZIndex(searchFlyout) > Panel.GetZIndex(Find<MahApps.Metro.Controls.Flyout>(config, "EditorPanel")),
                "The search flyout did not open above the editor.");
            Console.WriteLine($"Edit layout: {ownerBounds.Size}; editor {editorCard.RenderSize}; search {searchCard.RenderSize}.");
            config.Close();
            host.Close();
        }
        if (editOnly) return;
        var editor=new Request_Window(Create,(text,_)=>messages.Add(text)); Check(editor.WindowState==WindowState.Maximized,"New request does not start maximized."); await editor.LoadDataAsync();
        Check(!await editor.SaveRequestAsync() && Validation.GetHasError(Find<TextBox>(editor,"TxtName")),"Empty request has no inline error.");
        Find<TextBox>(editor,"TxtName").Text="طلب واجهة"; Check(!Validation.GetHasError(Find<TextBox>(editor,"TxtName")),"Typing did not immediately clear request error.");
        Check(!await editor.SaveRequestAsync() && Validation.GetHasError(Find<ComboBox>(editor,"_Combo_Types")),"Missing service has no inline error.");
        Find<ComboBox>(editor,"_Combo_Types").SelectedIndex=0; Check(!Validation.GetHasError(Find<ComboBox>(editor,"_Combo_Types")),"Selecting service did not immediately clear error.");
        gate.Pause=true; var saving=editor.SaveRequestAsync(); await gate.Entered.Task;
        Check(!await editor.SaveRequestAsync(),"Overlapping send was accepted."); gate.Release.SetResult(); Check(await saving,"First UI send failed.");
        Check(Find<TextBox>(editor,"TxtName").Text=="" && Find<ComboBox>(editor,"_Combo_Types").SelectedIndex==-1 && messages.Count==1,"Sent form was not cleared or success repeated.");
        Find<TextBox>(editor,"TxtName").Text="تفاصيل طلب الصيانة"; Find<ComboBox>(editor,"_Combo_Types").SelectedIndex=0;
        Capture(editor,new Size(1180,800),Path.Combine(output,"request-editor.png")); editor.Close();
        var login=new Maintenance_request.Login(Create,()=>"PC-TEST",_=>{}); Check(login.WindowState==WindowState.Normal,"Login did not return to its previous normal state."); await login.LoadDataAsync();
        Check(!await login.SignInAsync() && Validation.GetHasError(Find<PasswordBox>(login,"txbPassword")),"Missing sign-in password has no inline error.");
        Find<PasswordBox>(login,"txbPassword").Password="bad"; Check(!Validation.GetHasError(Find<PasswordBox>(login,"txbPassword")),"Password editing retained error.");
        Check(!await login.SignInAsync(),"Wrong-password UI sign-in accepted.");
        Find<PasswordBox>(login,"txbPassword").Password="xyz"; Check(await login.SignInAsync(),"Valid UI sign-in failed.");
        Capture(login,new Size(1080,720),Path.Combine(output,"client-login.png"));login.Close();
        var unchangedName=Maintenance_request.App.curr_user.User_Name;
        var profile=await accounts.SaveProfileAsync(owner.User_ID,"موظف بعد التعديل",7,2,false,3);
        Check(profile.User_Name=="موظف بعد التعديل" && profile.ISsound==false && profile.Sound_CHoise==3,"Old profile fields or sound choices were not saved.");
        Check(Maintenance_request.App.curr_user.User_Name==unchangedName,"Store changed the active session before the UI accepted the save.");
        await registration.RegisterAsync(new("اسم مستخدم آخر","abc","abc","PC-TEST",7,2));
        await Rejected(()=>accounts.SaveProfileAsync(owner.User_ID,"اسم مستخدم آخر",7,2,true,1));
        await Rejected(()=>accounts.SaveProfileAsync(owner.User_ID,"اسم",999,2,true,1));
        await Rejected(()=>accounts.SaveProfileAsync(owner.User_ID,"اسم",7,99,true,1));
        await Rejected(()=>accounts.SaveProfileAsync(owner.User_ID,"اسم",7,2,true,9));
        Maintenance_request.App.curr_user=profile;
        long afterProfile=await composer.SendAsync(owner.User_ID,"طلب بعد تعديل الحساب",3);
        using(var db=Create()) Check((await db.T_Notif_Action_Users.SingleAsync(x=>x.ID==afterProfile)).User_Name==profile.User_Name,"New request used outdated account metadata.");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel(); bool stopped=false;
            try { await composer.SendAsync(owner.User_ID,"طلب ملغى قبل الإرسال",3,cancelled.Token); } catch(OperationCanceledException) { stopped=true; }
            Check(stopped,"Canceled submission ignored the cancellation token.");
        }
        foreach(var resource in new[]{"Maintenance.ClientNotification1.wav","Maintenance.RequestNotification.wav","Maintenance.ClientNotification3.wav"})
        { using var sound=typeof(Maintenance_request.Notifications.ClientSoundPlayer).Assembly.GetManifestResourceStream(resource); Check(sound is {Length:>44},"Sound choice is not embedded: "+resource); }
        var main=new Maintenance_request.MainWindow(Create,()=>{},Path.Combine(output,"receipts"));
        Check(main.WindowState==WindowState.Normal && main.ResizeMode==ResizeMode.CanResizeWithGrip,"Main did not return to its previous normal/resizable state.");
        var snapshot=await new Maintenance_request.Notifications.RequestClientStore(Create).LoadAsync(owner.User_ID);
        typeof(Maintenance_request.MainWindow).GetMethod("SetDashboardRequests",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[snapshot]);
        foreach(var size in new[]{new Size(1000,660),new Size(1280,800),new Size(1920,1080)})
        {
            Capture(main,size,Path.Combine(output,$"client-home-{size.Width}x{size.Height}.png"));
            var root=(FrameworkElement)main.Content;var button=Find<Button>(main,"NewRequestButton");
            Check(new Rect(size).Contains(Bounds(root,button)),"New request action clipped at "+size);
            foreach(string name in new[]{"TotalCountText","NewCountText","InProgressCountText","CompletedCountText"})
                Check(new Rect(size).Contains(Bounds(root,Find<TextBlock>(main,name))),"Statistics clipped at "+size);
            foreach(var iconButton in Walk(root).OfType<Button>().Where(button=>button.Width<=54 && button.Content is FrameworkElement))
            {
                var presenter=Walk(iconButton).OfType<ContentPresenter>().First();
                var icon=(FrameworkElement)iconButton.Content;
                Check(presenter.ActualWidth>=icon.DesiredSize.Width && presenter.ActualHeight>=icon.DesiredSize.Height,"Header button padding clips its icon at "+size);
            }
        }
        Find<DevExpress.Xpf.Editors.SpinEdit>(main,"StatsYear").EditValue=DateTime.Today.Year-1;
        Check(Find<TextBlock>(main,"TotalCountText").Text=="0","Dashboard year filter failed.");
        // نفتح النافذة المعزولة حتى يتحقق الفحص من موضع لوحة الحساب داخل قالب النافذة الفعلي.
        var loaded = typeof(Maintenance_request.MainWindow).GetMethod("MetroWindow_Loaded", BindingFlags.NonPublic | BindingFlags.Instance)!;
        main.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), main, loaded);
        main.WindowStartupLocation = WindowStartupLocation.Manual;
        main.Left = -30000;
        main.Top = -30000;
        main.Width = 1280;
        main.Height = 800;
        main.ShowInTaskbar = false;
        main.ShowActivated = false;
        main.Show();
        await main.LoadAccountAsync();
        Check(Find<TextBox>(main,"AccountName").Text==profile.User_Name && Equals(Find<ComboBox>(main,"AccountManagement").SelectedValue,7)
            && Equals(Find<ComboBox>(main,"AccountBuilding").SelectedValue,(short)2),"Account panel lost original name/management/building options.");
        Check(Find<ComboBox>(main,"SoundChoice").SelectedValue?.ToString()=="3" && Find<CheckBox>(main,"SoundEnabled").IsChecked==false,"Account sound preferences did not load.");
        var card=Find<Border>(main,"AccountCard");
        await Task.Delay(500);
        foreach (var size in new[] { new Size(1280, 800), new Size(1000, 660) })
        {
            main.Width = size.Width;
            main.Height = size.Height;
            Pump();
            main.UpdateLayout();
            var bounds = Bounds(main, card);
            var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            Check(bounds.Width > 0 && bounds.Height > 0
                && Math.Abs(center.X - main.ActualWidth / 2) <= 2
                && Math.Abs(center.Y - main.ActualHeight / 2) <= 2,
                $"Account card is not centered at {size}. Card: {bounds}; owner: {main.ActualWidth}x{main.ActualHeight}.");
        }
        var accountImage=new RenderTargetBitmap(800,620,96,96,PixelFormats.Pbgra32);accountImage.Render(card);
        var accountPng=new PngBitmapEncoder();accountPng.Frames.Add(BitmapFrame.Create(accountImage));using(var file=File.Create(Path.Combine(output,"client-account.png"))) accountPng.Save(file);
        var clock=(DispatcherTimer)typeof(Maintenance_request.MainWindow).GetField("clockTimer",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(main)!;
        clock.Start(); Check(clock.IsEnabled,"Clock did not start."); main.Close(); Check(!clock.IsEnabled,"Clock survived window close.");
        var register=new Register_Windows(Create,()=>"PC-TEST",_=>{}); Check(register.WindowState==WindowState.Normal && register.ResizeMode==ResizeMode.CanResizeWithGrip,"Registration did not return to its previous normal/resizable state."); register.Close();
        var password=new Maintenance_request.Settings.Password_Chanche();Check(password.WindowState==WindowState.Normal && password.ResizeMode==ResizeMode.CanResize,"Password window did not return to its previous state.");Capture(password,new Size(620,650),Path.Combine(output,"client-password.png"));password.Close();
    }
    #endregion

    #region تشغيل الفحص داخل موزع WPF وإيقافه بعد انتهاء المهمة
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            var app=new Maintenance_request.App{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.InitializeComponent();
            var start=typeof(Maintenance_request.App).GetMethod("Application_Startup",BindingFlags.NonPublic|BindingFlags.Instance)!;
            app.Startup-=(StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler),app,start);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var task=Run(args[0], args.Contains("--edit-only"));var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
            timer.Tick+=(_,_)=>{if(task.IsCompleted)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();task.GetAwaiter().GetResult();app.Shutdown();
            Console.WriteLine($"PASS: {checks} client design/store checks. SQLite only.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    #endregion
}
