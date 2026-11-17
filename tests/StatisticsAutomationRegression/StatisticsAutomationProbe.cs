using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using DevExpress.Xpf.Core;
using Maintenance_app;
using Maintenance_app.dbContext;
using Microsoft.EntityFrameworkCore;
using Application = System.Windows.Application;

internal static class StatisticsAutomationProbe
{
    #region فحص مراجع الوصول قبل التصفية وبعد فتح الإحصائية
    // نحتفظ بالمراجع القديمة عمدًا لمحاكاة أدوات الوصول التي تستعلم بعد تبديل التبويب.
    private static readonly List<AutomationPeer> peers = [];
    private static readonly HashSet<AutomationPeer> seen = [];
    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
            var startup=typeof(App).GetMethod("Application_Startup",BindingFlags.NonPublic|BindingFlags.Instance)!;
            app.Startup-=(StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler),app,startup);
            using var connection=new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"); connection.Open();
            var options=new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
            Maintenance_DabaseContext Create()=>new(options);
            using(var db=Create())
            {
                db.Database.EnsureCreated();
                db.T_Branches.Add(new Maintenance_app.Models.T_Branch {Branch_ID=1,Branch="فرع الاختبار",st=true});
                db.T_Locations.Add(new Maintenance_app.Models.T_Location {Branch_ID=1,Location_ID=1,Location="الموقع"});
                db.T_Builds.Add(new Maintenance_app.Models.T_Build {Branch_ID=1,Location_ID=1,Location="الموقع",Build_ID=1,Build="مبنى الاختبار"});
                db.SaveChanges();
            }
            App.appuser=App.login_user=new Maintenance_app.Models.T_SystemUser{User_ID=1};
            var main=new MainWindow(Create){WindowState=WindowState.Normal};
            main.Show(); Pump();
            Capture(UIElementAutomationPeer.CreatePeerForElement(main)!);
            Query("main initial");
            ((System.Windows.Controls.RadioButton)main.FindName("ReportsTab")).IsChecked=true; Pump();
            Capture(UIElementAutomationPeer.CreatePeerForElement(main)!);
            Query("main reports tab");
            typeof(MainWindow).GetMethod("statistics_mnue_Click",BindingFlags.NonPublic|BindingFlags.Instance)!
                .Invoke(main,[main,new RoutedEventArgs()]);
            Pump();
            var statistics=app.Windows.OfType<Maintenance_app.reports.MaintenanceStatisticsWindow>().Single();
            Capture(UIElementAutomationPeer.CreatePeerForElement(statistics)!);
            var building=(UIElement)statistics.FindName("StatisticsBuilding");
            var bp=UIElementAutomationPeer.CreatePeerForElement(building)!;
            Query("statistics open");
            ((System.Windows.Controls.CheckBox)statistics.FindName("StatisticsBuildingEnabled")).IsChecked=true;
            Pump();
            var editor=(DevExpress.Xpf.Editors.ComboBoxEdit)building;
            var dropdown=bp.GetPattern(PatternInterface.ExpandCollapse) as System.Windows.Automation.Provider.IExpandCollapseProvider;
            if(dropdown is null) throw new Exception("ComboBox lost expand-collapse automation.");
            dropdown.Expand(); Pump();
            Capture(bp);
            editor.SelectedIndex=0; Pump();
            if(editor.Text!="مبنى الاختبار") throw new Exception("Building selection failed.");
            dropdown.Collapse(); Pump();
            var period=(DevExpress.Xpf.Editors.ComboBoxEdit)statistics.FindName("StatisticsPeriodType");
            period.SelectedIndex=0; Pump();
            if(period.Text!="ربع السنة") throw new Exception("Period selection changed its value.");
            Query("statistics choices");
            var themeBefore=ApplicationThemeHelper.ApplicationThemeName;
            var preview=new DevExpress.Xpf.Printing.DocumentPreviewWindow {Owner=statistics};
            ThemeManager.SetThemeName(preview,"Win11Light");
            preview.PreviewControl.DocumentSource=new DevExpress.XtraReports.UI.XtraReport();
            preview.Show(); Pump();
            Capture(UIElementAutomationPeer.CreatePeerForElement(preview)!);
            Query("preview open");
            preview.Close(); Pump();
            if(ApplicationThemeHelper.ApplicationThemeName!=themeBefore) throw new Exception("Preview changed global theme.");
            Query("preview closed");
            statistics.Close(); Pump();
            Query("statistics closed");
            for(int cycle=0;cycle<3;cycle++)
            {
                foreach(var tab in new[]{"TransactionsTab","SettingsTab","ReportsTab"})
                {
                    ((System.Windows.Controls.RadioButton)main.FindName(tab)).IsChecked=true; Pump();
                    Capture(UIElementAutomationPeer.CreatePeerForElement(main)!);
                    Query("tab "+tab+" cycle "+cycle);
                }
                typeof(MainWindow).GetMethod("statistics_mnue_Click",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[main,new RoutedEventArgs()]);
                Pump();
                var reopened=app.Windows.OfType<Maintenance_app.reports.MaintenanceStatisticsWindow>().Single();
                Capture(UIElementAutomationPeer.CreatePeerForElement(reopened)!);
                Query("reopened statistics "+cycle);
                reopened.Close(); Pump();
                Query("reclosed statistics "+cycle);
            }
            ApplicationThemeHelper.ApplicationThemeName="Win11Light"; Pump();
            Query("global theme Win11Light");
            ApplicationThemeHelper.ApplicationThemeName="None"; Pump();
            Query("global theme None");
            typeof(MainWindow).GetField("confirmedClose",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(main,true);
            main.Close(); app.Shutdown();
            Console.WriteLine("PASS: captured automation peers survived opening and closing.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static void Capture(AutomationPeer peer)
    {
        if(!seen.Add(peer)) return;
        peers.Add(peer);
        foreach(var child in peer.GetChildren()??[]) Capture(child);
    }
    private static void Query(string phase)
    {
        Console.WriteLine("PHASE "+phase+" peers="+peers.Count);
        foreach(var peer in peers)
        {
            try{_=peer.GetAutomationId();_=peer.GetName();}
            catch(System.Windows.Automation.ElementNotAvailableException){}
            catch(Exception ex)
            {
                var item=peer as ItemAutomationPeer;
                for(DependencyObject? node=item?.ItemsControlAutomationPeer?.Owner;node is not null;node=System.Windows.Media.VisualTreeHelper.GetParent(node))
                    Console.WriteLine("ANCESTOR "+node.GetType().FullName+" "+(node as FrameworkElement)?.Name);
                throw new Exception($"Failed {phase}: peer={peer.GetType().FullName}, item={item?.Item}, parent={item?.ItemsControlAutomationPeer?.GetType().FullName??"null"}, owner={item?.ItemsControlAutomationPeer?.Owner?.GetType().FullName}, name={(item?.ItemsControlAutomationPeer?.Owner as FrameworkElement)?.Name}",ex);
            }
        }
    }
    private static void Pump()=>System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},
        System.Windows.Threading.DispatcherPriority.ContextIdle);
    #endregion
}
