using RadioButton = System.Windows.Controls.RadioButton;
#nullable disable
using MenuItem = System.Windows.Controls.MenuItem;
using Application = System.Windows.Application;
using Control = System.Windows.Controls.Control;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;

using Maintenance_app.Models;
using Maintenance_app.DatabContext;
using Maintenance_app.converts;
using Maintenance_app.viewo_Model;
using Maintenance_app.main_Windows;
using Microsoft.EntityFrameworkCore;
using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MahApps.Metro;
using MahApps.Metro.Controls.Dialogs;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;
using System.ComponentModel;
using Microsoft.Win32;
using System.Globalization;
using Maintenance_app.Dashboard;
using Maintenance_app.reports;
using Maintenance_app.support_Data;
using System.Threading;
using DevExpress.Xpf.Core;
using Maintenance_app.Reports;

namespace Maintenance_app
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>



    /// <summary>
    /// Interaction logic for Main.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public const string DisplayVersion = "3.0.0.0";
        public static readonly RoutedUICommand AboutSystemCommand = new RoutedUICommand("حول النظام", nameof(AboutSystemCommand),
            typeof(MainWindow), new InputGestureCollection { new KeyGesture(Key.F1) });
        public static readonly RoutedUICommand OpenMaintenanceOrderCommand = new RoutedUICommand(
            "تكليف الصيانة", nameof(OpenMaintenanceOrderCommand), typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.F7) });
       
        private orders_Windows.maintenance_Order maintenanceOrderWindow;
        // يحتفظ بكل شاشة مستقلة حتى لا تُنشأ نوافذ متكررة عند النقر المتتابع على القائمة.
        private MaintenanceReportsWindow maintenanceReportsWindow;
        #region نافذة تقارير الفروع
        // يحتفظ بالنافذة المفتوحة لئلا تتكرر التقارير عند النقر المتتابع.
        private MaintenanceReportsWindow branchReportsWindow;
        #endregion
        #region نافذة نموذج الفحص اليومي واختصارها
        // يمنع تكرار نافذة نموذج الفحص اليومي عند استخدام القائمة أو الاختصار.
        private Daly_Print_Windowxaml dailyInspectionWindow;
        private daliy_services_window dailyInspectionserviceWindow;


        #endregion
        private MaintenanceStatisticsWindow maintenanceStatisticsWindow;
        // كل مصدر من البيانات المساعدة له نافذته المستقلة، ويحفظ القاموس نافذة المصدر المفتوحة فقط.
        private readonly Dictionary<int, MaintenanceSupportWindow> maintenanceSupportWindows = new();
        public AnnualStatisticsViewModel Statistics { get; private set; }
        private readonly CancellationTokenSource statisticsCancellation = new CancellationTokenSource();
        private bool statisticsLoading;

        public sealed class WorkspaceAction
        {
            public string Section { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string Shortcut { get; set; }
            public MahApps.Metro.IconPacks.PackIconMaterialKind Icon { get; set; }
            public MenuItem Target { get; set; }

        }

        private ICollectionView workspaceView;
        private string workspaceSection = "operations";
        private bool confirmedClose;

        /// الخاصة بالنسخ الأحتياطي والأسترجاع Try متغيرات خاصه بحفظ رسالة الخطأمن جملة
        private bool cath = false;
        private string ms;
        // ************************
        private byte clo = 0;

        // *******************
        // متغيير لتفعيل الاختصارات بالشاشة
        public static RoutedCommand SaveCommand_AltS = new RoutedCommand();
        // *********************
        // مؤقت لعرض الوقت في الشاشة الرئيسية
        DispatcherTimer timer = new DispatcherTimer();
        // *************************
        // متغيير لمعرفة مسار مجلد المنظومة
        private string AppPath = System.IO.Directory.GetCurrentDirectory();
        //var color = ThemeManager.Accents;


        #region تهيئة الرئيسية ومصدر بيانات الإشعارات
        public MainWindow() : this(null) { }

        // مصدر مستقل اختياري لفحص الربط اللحظي والنوافذ على قاعدة معزولة.
        public MainWindow(Func<Maintenance_app.dbContext.Maintenance_DabaseContext> contextFactory, Action playNotificationSound = null)
        {
            // المصدر البديل يسمح بفحص استدعاء الصوت دون تشغيل صوت أثناء الاختبارات.
            notificationSound = new Maintenance.Shared.NotificationSound(playNotificationSound);
            notificationContextFactory = contextFactory;
            Statistics = new AnnualStatisticsViewModel();
            Statistics.SelectedYearNumber = DateTime.Today.Year;
            InitializeComponent();
            InitializeMaintenanceCompletion();
            CommandBindings.Add(new CommandBinding(AboutSystemCommand, AboutSystem_Click));
            // تسجيل الاختصار على الرئيسية صراحة يضمن عمله مهما كان عنصر التركيز الحالي.
            InputBindings.Add(new KeyBinding(AboutSystemCommand, Key.F1, ModifierKeys.None));

            InitializeAccountCommands();
            CommandBindings.Add(new CommandBinding(OpenMaintenanceOrderCommand, bt_inout_par_Click,
                (_, args) => { args.CanExecute = inout_Menu.IsEnabled; args.Handled = true; }));
            WindowWorkArea.Attach(this);
            InitializeWorkspace();
            InitializeWorkspaceShortcuts();
            InitializeNotifications();
            InitializeMaintenanceStatistics();
            timer_Tick(this, EventArgs.Empty);
        }

        #endregion

        private void MinimizeWindow_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        private void CloseWindow_Click(object sender, RoutedEventArgs e) { Close(); }
        #region فتح التعريف بالمنظومة
        private AboutWindow aboutWindow;
        private bool aboutOpenPending;

        // يصل الزر وF1 إلى المسار نفسه، ويؤجَّل العرض حتى ينتهي حدث الإدخال الحالي.
        private void AboutSystem_Click(object sender, RoutedEventArgs e)
        {
            if (aboutOpenPending) return;
            aboutOpenPending = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    if (!IsVisible) return;
                    if (aboutWindow is { IsLoaded: true })
                    {
                        aboutWindow.WindowState = WindowState.Maximized;
                        aboutWindow.Activate();
                        return;
                    }

                    // الحوار المملوك يبقى أمام الرئيسية طوال عرضه، وتُحرر مرجعيته عند الإغلاق.
                    aboutWindow = new AboutWindow { Owner = this, ShowActivated = true, ShowInTaskbar = false };
                    aboutWindow.ShowDialog();
                    aboutWindow = null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError("تعذّر فتح حول النظام: {0}", ex);
                    aboutWindow = null;
                    UiMessages.Show(this, "حول النظام", "تعذّر فتح واجهة حول النظام.\n" + ex.GetBaseException().Message,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { aboutOpenPending = false; }
            }));
        }
        #endregion
        private void TitleSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) MaximizeWindow_Click(sender, e);
            else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        #region تهيئة تبويبات الرئيسية والوصول السريع
        private void InitializeWorkspace()
        {
            var actions = new List<WorkspaceAction>();
            AddWorkspaceAction(actions, "operations", "تكليف الصيانة", "اصدار وطباعة تكليف الصيانة.", "F7", "FileDocumentOutline", inout_Menu);
            // وصول سريع لتقرير الإنهاء بنفس صلاحية الأمر والقائمة والاختصار.
            AddWorkspaceAction(actions, "operations", "تقرير إنهاء الصيانة", "عرض التكليف وتسجيل حالة التنفيذ ومراجعة تقارير الإنهاء وطباعتها.", "F5", "ClipboardCheckOutline", CompletionMenu);
             // البيانات المساعدة الخاصة بالصيانة تظهر في الوصول السريع وفي تبويبها، لا في قوائم المعاملات الموروثة.
            AddWorkspaceAction(actions, "reference", "المباني", "إدارة وعرض المباني المستخدمة في تكاليف الصيانة.", "Ctrl+1", "OfficeBuildingOutline", SupportBuildingsMenu);
            AddWorkspaceAction(actions, "reference", "أنواع الخدمات", "عرض أنواع الخدمات المستخدمة في التكليفات.", "Ctrl+2", "Tools", SupportServicesMenu);
            AddWorkspaceAction(actions, "reference", "الفنيون", "عرض قائمة الفنيين المكلفين بأعمال الصيانة.", "Ctrl+3", "AccountHardHatOutline", SupportEmployeesMenu);
            AddWorkspaceAction(actions, "reference", "الإدارات والأقسام", "عرض الإدارات المرتبطة ببيانات الصيانة.", "Ctrl+4", "OfficeBuildingCogOutline", SupportManagementsMenu);
            AddWorkspaceAction(actions, "reference", "الأقسام والوحدات", "عرض الأقسام والوحدات التابعة للإدارات.", "Ctrl+5", "SourceBranch", SupportSectionsMenu);
            AddWorkspaceAction(actions, "reports", "تقارير الصيانة", "البحث في التكليفات وعرض تقارير الإنهاء.", "Ctrl+C", "ClipboardTextClock", report_mnue);
            AddWorkspaceAction(actions, "reports", "تقارير الفروع", "عرض وطباعة تقارير الصيانة حسب الفرع.", BranchReportsMenu.InputGestureText, "OfficeBuildingOutline", BranchReportsMenu);
            AddWorkspaceAction(actions, "reports", "نموذج الفحص اليومي", "اختيار المبنى والفاحص وطباعة نموذج الفحص اليومي.", "Alt+Y", "ClipboardCheckOutline", DailyInspectionMenu);
            AddWorkspaceAction(actions, "reports", "نموذج الفحص اليومي للخدمات", "اختيار المبنى والفاحص وطباعة نموذج الفحص اليومي للخدمات.", "Alt+X", "ClipboardCheckOutline", DailyInspectionSERVICEMenu);

            AddWorkspaceAction(actions, "reports", "إحصائية الصيانة", "عرض المؤشرات السنوية لحالات التكليفات.", "Ctrl+I", "ChartBoxOutline", statistics_mnue);
            AddWorkspaceAction(actions, "settings", "المستخدمون والصلاحيات", "يتطلب صلاحية إدارة المستخدمين.", "F10", "AccountCogOutline", opt);
            AddWorkspaceAction(actions, "settings", "تغيير كلمة المرور", "تحديث كلمة المرور الخاصة بحسابك الحالي.", "F11", "LockReset", cha);
            AddWorkspaceAction(actions, "settings", "إغلاق المنظومة", "إنهاء جلسة العمل وإغلاق المنظومة بعد التأكيد.", "Alt+F4", "Logout", close);
            AddWorkspaceAction(actions, "help", "حول النظام", "التعريف بالمنظومة والإصدار وجهة التصميم. الاختصار: F1", "F1", "InformationOutline", AboutMenuItem);
            workspaceView = CollectionViewSource.GetDefaultView(actions);
            workspaceView.Filter = item =>
            {
                var action = (WorkspaceAction)item;
                return action.Section == workspaceSection;
            };
            SectionCommands.ItemsSource = workspaceView;
            // تُبقي الرئيسية الإجراءات اليومية، وتظهر معها مصادر التكليف التي يحتاجها المستخدم سريعًا.
            QuickAccessCommands.ItemsSource = actions.Where(action => action.Section == "operations" || action.Section == "reports"
                || action.Target == SupportBuildingsMenu || action.Target == SupportServicesMenu || action.Target == SupportEmployeesMenu
                || action.Target == SupportManagementsMenu || action.Target == SupportSectionsMenu).ToList();
            RefreshWorkspace();
        }

        private static void AddWorkspaceAction(List<WorkspaceAction> actions, string section, string title, string description, string shortcut, string iconName, MenuItem target)
        {
            MahApps.Metro.IconPacks.PackIconMaterialKind icon;
            if (!Enum.TryParse(iconName, out icon)) icon = MahApps.Metro.IconPacks.PackIconMaterialKind.FileDocumentOutline;
            actions.Add(new WorkspaceAction { Section = section, Title = title, Description = description, Shortcut = shortcut, Icon = icon, Target = target });
        }

        private void RefreshWorkspace()
        {
            // قد يُستدعى الحدث من تبويب محدد في XAML قبل تهيئة القائمة.
            if (workspaceView is null) return;
            ApplyWorkspacePermissions();
            workspaceView.Refresh();
        }

        private void WorkspaceSection_Checked(object sender, RoutedEventArgs e)
        {
            workspaceSection = (string)((RadioButton)sender).Tag;
            RefreshWorkspace();
        }

        // جميع بطاقات الرئيسية تنفذ بند القائمة المرتبط وتلتزم بحالة تمكينه.
        private void QuickAccess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: WorkspaceAction action })
                ExecuteWorkspaceTarget(action.Target);
        }

        #endregion

        private void CloseQuickAccess_Click(object sender, RoutedEventArgs e)
        {
            QuickAccessPanel.Visibility = Visibility.Collapsed;
            ShowQuickAccessButton.Visibility = Visibility.Visible;
            ShowQuickAccessButton.Focus();
        }

        private void ShowQuickAccess_Click(object sender, RoutedEventArgs e)
        {
            QuickAccessPanel.Visibility = Visibility.Visible;
            ShowQuickAccessButton.Visibility = Visibility.Collapsed;
        }

        private void CloseStatistics_Click(object sender, RoutedEventArgs e)
        {
            StatisticsCard.Visibility = Visibility.Collapsed;
            ShowStatisticsButton.Visibility = Visibility.Visible;
            ShowStatisticsButton.Focus();
        }

        private async void ShowStatistics_Click(object sender, RoutedEventArgs e)
        {
            StatisticsCard.Visibility = Visibility.Visible;
            ShowStatisticsButton.Visibility = Visibility.Collapsed;
            StatisticsYear.Focus();
            await RefreshStatisticsAsync();
        }

        private async void RefreshStatistics_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatisticsAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            statisticsCancellation.Cancel();
            timer.Stop();
            base.OnClosed(e);
        }

        private void SETSHORTCUT()
        {
            //if ((App.appuser?.forinout == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.F7));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, inout_Menu_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}

            //if ((App.appuser?.for_work_his == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.F5));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, work_his_Menu_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}

            //if ((App.appuser?.forbanks == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.F6));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, Bank_mnue_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}

            //if ((App.appuser?.forbranch_bank == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.F8));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, Branch_Bank_mnue_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}
            //if ((App.appuser?.forranks == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.F9));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, rank_mnue_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}
            //if ((App.appuser?.for_ref == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.F12));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, ref_mnue_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}
            //if ((App.appuser?.forres == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.E, ModifierKeys.Control));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, rce_mnue_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}
            //if ((App.appuser?.fornat == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.A, ModifierKeys.Control));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, nat_mnue_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}

            //if ((App.appuser?.for_deb == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.B, ModifierKeys.Control));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, deb_mnue_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}


            //SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.F, ModifierKeys.Control));
            //CommandBindings.Add(new CommandBinding(SaveCommand_AltS, man_mnue_Click));
            //SaveCommand_AltS = new RoutedCommand();


            //if ((App.appuser?.for_report == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.C, ModifierKeys.Control));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, report_mnue_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}
            //if ((App.appuser?.for_virwo == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.D, ModifierKeys.Control));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, viewo_mnue_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}
            //if ((App.appuser?.foruser == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.F10));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, opt_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}
            //if ((App.appuser?.fordebplace == true))
            //{
            //    SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.G, ModifierKeys.Control));
            //    CommandBindings.Add(new CommandBinding(SaveCommand_AltS, dis_place_mnue_Click));
            //    SaveCommand_AltS = new RoutedCommand();
            //}



            //SaveCommand_AltS.InputGestures.Add(new KeyGesture(Key.F11));
            //CommandBindings.Add(new CommandBinding(SaveCommand_AltS, cha_Click));
            //SaveCommand_AltS = new RoutedCommand();


        }


        private void timer_Tick(object sender, EventArgs e)
        {
            ltim.Text = DateTime.Now.ToString("hh:mm:ss");
            LDATE.Text = DateTime.Now.ToString("dd-MM-yyyy");

        }

        private async void close_Click(object sender, RoutedEventArgs e)
        {
            //MetroDialogOptions.ColorScheme = MetroDialogColorScheme.Accented;

            //var mySettings = new MetroDialogSettings();

            //mySettings.AffirmativeButtonText = "نعم";
            //mySettings.NegativeButtonText = "لا";

            //mySettings.DialogMessageFontSize = 18;
            //mySettings.DialogTitleFontSize = 18;
            //mySettings.AnimateShow = true;


            //MessageDialogResult result = await this.ShowMessageAsync(this.Title, "هل أنت متأكد من إغلاق المنظومة ؟ ", MessageDialogStyle.AffirmativeAndNegative, mySettings);
            //if (result != MessageDialogResult.Negative)
            //    App.Current.Shutdown();

            Close();



        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (confirmedClose) return;
            ApplicationThemeHelper.ApplicationThemeName = "Win11Light";

            if (!UiMessages.Confirm(this, "هل تريد إنهاء العمل وإغلاق منظومة الخدمات والصيانة؟", true))
            {
                e.Cancel = true;
                return;
            }
            ApplicationThemeHelper.ApplicationThemeName = "None";

            confirmedClose = true;
            Application.Current.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
        }




        private void credite_op_SubmenuClosed(object sender, RoutedEventArgs e)
        {
            credite_op.Foreground = Brushes.White;

        }

        private void credite_op_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            credite_op.Foreground = Brushes.Black;

        }
        private void xcredite_op_SubmenuClosed(object sender, RoutedEventArgs e)
        {
            Supports_Data.Foreground = Brushes.White;

        }

        private void xcredite_op_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            Supports_Data.Foreground = Brushes.Black;

        }


        private void REPORT_MNUE_SubmenuClosed(object sender, RoutedEventArgs e)
        {
            REPORT_MNUE.Foreground = Brushes.White;

        }

        private void REPORT_MNUE_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            REPORT_MNUE.Foreground = Brushes.Black;

        }

        private void mng_SubmenuClosed(object sender, RoutedEventArgs e)
        {
            mng.Foreground = Brushes.White;

        }

        private void mng_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            mng.Foreground = Brushes.Black;

        }


        //private void AccentSelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    System.Collections.Generic.IEnumerable<MahApps.Metro.Accent> color = ThemeManager.Accents;
        //    var selectedAccent = color.ElementAt((int)AccentSelector.SelectedIndex);

        //    Tuple<AppTheme, Accent> theme = null;
        //    if (selectedAccent != null)
        //        theme = ThemeManager.DetectAppStyle(System.Windows.Application.Current);
        //    ThemeManager.ChangeAppStyle(System.Windows.Application.Current, selectedAccent, theme.Item1);
        //    string sq = "select * from T_Color_Chose where user_id=" + Module1.us_id;
        //    SqlCommand cm = new SqlCommand(sq, Module1.con);
        //    SqlDataReader rd = cm.ExecuteReader();
        //    if (rd.Read())
        //    {
        //        rd.Close();
        //        sq = "update  T_Color_Chose set color_id=" + (int)AccentSelector.SelectedIndex + " where user_id=" + Module1.us_id;
        //        cm = new SqlCommand(sq, Module1.con);
        //        cm.ExecuteNonQuery();
        //    }
        //    else
        //    {
        //        rd.Close();
        //        sq = "insert into  T_Color_Chose (color_id,user_id)values(" + (int)AccentSelector.SelectedIndex + "," + Module1.us_id + ")";
        //        cm = new SqlCommand(sq, Module1.con);
        //        cm.ExecuteNonQuery();
        //    }
        //    rd.Close();
        //    AccentSelector.Focus();
        //}

        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeSessionAsync();
        }

        private async Task InitializeSessionAsync()
        {



            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ar-LY");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ar-LY");
            //'*********************
            //'عرض إصدار المنظومة
            vesion.Text = "الإصدار " + DisplayVersion;

            //'*****************
            //'عرض تاريخ اليوم

            //'********************
            //'تفعيل إختصارات الشاشة
            SETSHORTCUT();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += timer_Tick;
            timer.Start();
            //'*****************
            //'عرض التوقيت في الشاشة

            //timer= new DispatcherTimer();

            //'***************
            //'عرض إسم المستخدم
            lus.Text = App.usname;


            //الصلاحيات
            //============================================

            //متابعة الصكوك
            //inout_Menu.IsEnabled = (App.appuser?.forinout == true);

            //========================================

            //متابعة الصكوك
            //work_his_Menu.IsEnabled = (App.appuser?.for_work_his == true);

            //========================================


            //البيانات المساعدة


            // فروع البنوك
            //Bank_mnue.IsEnabled = (App.appuser?.forbanks == true);
            //Branch_Bank_mnue.IsEnabled = (App.appuser?.forbranch == true);

            //Branch_Bank_mnue.IsEnabled = (App.appuser?.forbranch_bank == true);
            //rank_mnue.IsEnabled = (App.appuser?.forranks == true);
            //rce_mnue.IsEnabled = (App.appuser?.forres == true);
            //ref_mnue.IsEnabled = (App.appuser?.for_ref == true);
            //deb_mnue.IsEnabled = (App.appuser?.for_deb == true);
            //nat_mnue.IsEnabled = (App.appuser?.fornat == true);
            //dis_place_mnue.IsEnabled = (App.appuser?.fordebplace == true);

            //========================================

            //========================================


            //التقارير
            // نقارير الصكوك
            //report_mnue.IsEnabled = (App.appuser?.for_report == true);
            //viewo_mnue.IsEnabled = (App.appuser?.for_virwo == true);
            RefreshWorkspace();
            // تحميل حالات الصيانة مع الحفاظ على السنة التي اختارها المستخدم أثناء الانتظار.
            await RefreshStatisticsAsync();



            //========================================.
        }




        private void instrument_Menu_Click(object sender, RoutedEventArgs e)
        {

            //f.Top = 140;
            //f.Left = 15;


        }

        private void TMNADD_CREDITE_Click(object sender, RoutedEventArgs e)
        {
            //inout f = new inout();
            //f.ShowDialog();
            //f.Top = 160;
            //f.Left = 80;

        }

        private void Ins_Reports_Click(object sender, RoutedEventArgs e)
        {


        }

        private void sw_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
            //System.Windows.Forms.Application.Restart();
        }

        private void Owned_Building_report_Click(object sender, RoutedEventArgs e)
        {
            //Rports f = new Rports();
            //f.ShowDialog();
        }

        private void Bank_mnue_Click(object sender, RoutedEventArgs e)
        {
            //Bank f = new Bank();
            //f.ShowDialog();
        }

        private void Branch_Bank_mnue_Click(object sender, RoutedEventArgs e)
        {
            //App.is_button = false;
            //Bank_Branch f = new Bank_Branch();
            //f.ShowDialog();
        }

        private void sk_types_Click(object sender, RoutedEventArgs e)
        {

        }

        private void out_type_Click(object sender, RoutedEventArgs e)
        {

        }

        private void minimiz_Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void bt_inout_par_Click(object sender, RoutedEventArgs e)
        {
            if (!inout_Menu.IsEnabled) return;
            if (maintenanceOrderWindow != null && maintenanceOrderWindow.IsVisible)
            {
                maintenanceOrderWindow.Activate();
                return;
            }
            try
            {
                maintenanceOrderWindow = new orders_Windows.maintenance_Order
                {
                    Owner = this,
                    WindowState = WindowState.Maximized
                };
                maintenanceOrderWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                try
                {
                    string logDirectory = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Maintenance_app", "Logs");
                    System.IO.Directory.CreateDirectory(logDirectory);
                    System.IO.File.AppendAllText(System.IO.Path.Combine(logDirectory, "order-window.log"),
                        DateTimeOffset.Now.ToString("O") + Environment.NewLine + ex + Environment.NewLine);
                }
                catch (System.IO.IOException) { }
                catch (UnauthorizedAccessException) { }
                Maintenance_app.Dashboard.UiMessages.Show(this, "تكليف الصيانة", "تعذّر فتح واجهة تكليف الصيانة.\n" + ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                maintenanceOrderWindow = null;
                // إعادة القراءة بعد إغلاق التكليف تُظهر الإضافات والحذف المحلي دون انتظار المؤقت.
                _ = RefreshMaintenanceNotificationsAsync();
            }
        }

        private void work_his_Menu_Click(object sender, RoutedEventArgs e)
        {
            work_his_par_Click(sender, e);
        }

        private void work_his_par_Click(object sender, RoutedEventArgs e)
        {
            //inclusion_statement_Window c = new inclusion_statement_Window();
            //c.ShowDialog();
        }

        private void rank_mnue_Click(object sender, RoutedEventArgs e)
        {
            //rank_windows r = new rank_windows();
            //r.ShowDialog();
        }

        private void ref_mnue_Click(object sender, RoutedEventArgs e)
        {
            //ref_Window r = new ref_Window();
            //r.ShowDialog();
        }

        private void rce_mnue_Click(object sender, RoutedEventArgs e)
        {
            //rce_window r = new rce_window();
            //r.ShowDialog();
        }

        private void nat_mnue_Click(object sender, RoutedEventArgs e)
        {
            //nat_windows c = new nat_windows();
            //c.ShowDialog();
        }

        private void deb_mnue_Click(object sender, RoutedEventArgs e)
        {
            //deb_windows f = new deb_windows();
            //f.ShowDialog();
        }

        #region فتح نوافذ التقارير والإحصائية
        private void DailyInspection_Click(object sender, RoutedEventArgs e)
        {
            if (!DailyInspectionMenu.IsEnabled) return;
            if (dailyInspectionWindow is { IsLoaded: true })
            {
                if (dailyInspectionWindow.WindowState == WindowState.Minimized)
                    dailyInspectionWindow.WindowState = WindowState.Maximized;
                dailyInspectionWindow.Activate();
                dailyInspectionWindow.Focus();
                return;
            }
            dailyInspectionWindow = new Daly_Print_Windowxaml(notificationContextFactory) { Owner = this };
            dailyInspectionWindow.Closed += (_, _) => dailyInspectionWindow = null;
            // يطابق فتح النموذج حواريًا سلوك المنظومة القديمة ويبقي الرئيسية معطّلة حتى إغلاقه.
            dailyInspectionWindow.ShowDialog();
        }

        private void report_mnue_Click(object sender, RoutedEventArgs e)
        {
            if (!report_mnue.IsEnabled) return;
            // نافذة مستقلة للتقارير وتُجلب للمقدمة إن كانت مفتوحة بالفعل.
            if (maintenanceReportsWindow is { IsLoaded: true })
            {
                BringReportWindowToFront(maintenanceReportsWindow);
                return;
            }

            maintenanceReportsWindow = new MaintenanceReportsWindow(notificationContextFactory) { Owner = this };
            maintenanceReportsWindow.Closed += (_, _) => maintenanceReportsWindow = null;
            maintenanceReportsWindow.Show();
            BringReportWindowToFront(maintenanceReportsWindow);
        }

        // صلاحية تقارير الفروع مستقلة، وتُفحص أيضًا عند تنفيذ الأمر من بطاقة الوصول السريع.
        private void BranchReportsMenu_Click(object sender, RoutedEventArgs e)
        {
            if (!BranchReportsMenu.IsEnabled) return;
            if (branchReportsWindow is { IsLoaded: true })
            {
                BringReportWindowToFront(branchReportsWindow);
                return;
            }

            branchReportsWindow = new MaintenanceReportsWindow(dbContext_Aggregated.DbContextHelper.CreateContext, true) { Owner = this };
            branchReportsWindow.Closed += (_, _) => branchReportsWindow = null;
            branchReportsWindow.Show();
            BringReportWindowToFront(branchReportsWindow);
        }

        private void statistics_mnue_Click(object sender, RoutedEventArgs e)
        {
            if (!statistics_mnue.IsEnabled) return;
            // نافذة مستقلة للإحصائية وتُجلب للمقدمة إن كانت مفتوحة بالفعل.
            if (maintenanceStatisticsWindow is { IsLoaded: true })
            {
                BringReportWindowToFront(maintenanceStatisticsWindow);
                return;
            }

            maintenanceStatisticsWindow = new MaintenanceStatisticsWindow(notificationContextFactory) { Owner = this };
            maintenanceStatisticsWindow.Closed += (_, _) => maintenanceStatisticsWindow = null;
            maintenanceStatisticsWindow.Show();
            BringReportWindowToFront(maintenanceStatisticsWindow);
        }

        private static void BringReportWindowToFront(Window reportWindow)
        {
            // يُعاد تنشيط النافذة بعد إظهارها حتى لا تفتح خلف الرئيسية أو خلف نافذة أخرى.
            if (reportWindow is Maintenance_app.reports.MaintenanceReportsWindow)
            {
                reportWindow.WindowState = WindowState.Maximized;
            }
            else
            {
                // باقي النوافذ تفتح بالحجم المحدد في XAML
                reportWindow.WindowState = WindowState.Normal;
            }
            reportWindow.Activate();
            reportWindow.Focus();
        }
        #endregion

        #region فتح واجهة البيانات المساعدة للصيانة
        // لكل بند نافذة مستقلة بخانات إدخال خاصة به؛ لا تُجمع المصادر في تبويبات نافذة واحدة.
        private void SupportBuildingsMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SupportBuildingsMenu.IsEnabled) OpenMaintenanceSupportDatabuild();
        }

        private void OpenMaintenanceSupportDatabuild()
        {
            buildings f = new buildings();
            f.ShowDialog();
        }

        private void SupportServicesMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SupportServicesMenu.IsEnabled) OpenMaintenanceSupportDatax();
        }
        private void SupportSectionsMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SupportSectionsMenu.IsEnabled) OpenMaintenanceSupportDatasection();
        }

        private void OpenMaintenanceSupportDatasection()
        {
            sections f = new sections();
            f.ShowDialog();
        }

        private void SupportEmployeesMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SupportEmployeesMenu.IsEnabled) OpenMaintenanceSupportDataemployey();
        }

        private void OpenMaintenanceSupportDataemployey()
        {
            employeys f = new employeys();
            f.ShowDialog();
        }

        private void SupportManagementsMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SupportManagementsMenu.IsEnabled) OpenMaintenanceSupportDatamang();
        }

        private void OpenMaintenanceSupportDatamang()
        {
            mangments f = new mangments();
            f.ShowDialog();
        }

         private void OpenMaintenanceSupportDatax()
        {
            servic_types f = new servic_types();
            f.ShowDialog();
        }
        private void OpenMaintenanceSupportData(int tab)
        {
            if (maintenanceSupportWindows.TryGetValue(tab, out var openedWindow) && openedWindow.IsLoaded)
            {
                if (openedWindow.WindowState == WindowState.Minimized) openedWindow.WindowState = WindowState.Normal;
                openedWindow.Activate();
                return;
            }

            var supportWindow = new MaintenanceSupportWindow(notificationContextFactory, tab) { Owner = this };
            maintenanceSupportWindows[tab] = supportWindow;
            supportWindow.Closed += (_, _) => maintenanceSupportWindows.Remove(tab);
            supportWindow.Show();
            supportWindow.Activate();
        }
        #endregion

        private void viewo_mnue_Click(object sender, RoutedEventArgs e)
        {

        }

        private void inout_Menu_Click(object sender, RoutedEventArgs e)
        {
            bt_inout_par_Click(sender, e);
        }

        private void man_mnue_Click(object sender, RoutedEventArgs e)
        {

        }

        private void dis_place_mnue_Click(object sender, RoutedEventArgs e)
        {
            //dis_place_window f = new dis_place_window();
            //f.ShowDialog();
        }

        private void INFOOK(object sender, RoutedEventArgs e)
        {

        }

        private void CloseSec_OnClick(object sender, RoutedEventArgs e)
        {


        }

        private void OkButtonOnClick(object sender, RoutedEventArgs e)
        {
            confirmedClose = true;
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Application.Current.Shutdown();
        }

        private void DailyInspectionSERVICEMenu_Click(object sender, RoutedEventArgs e)
        {
            if (!DailyInspectionSERVICEMenu.IsEnabled) return;

            if (dailyInspectionserviceWindow is { IsLoaded: true })
            {
                if (dailyInspectionserviceWindow.WindowState == WindowState.Minimized)
                    dailyInspectionserviceWindow.WindowState = WindowState.Maximized;
                dailyInspectionserviceWindow.Activate();
                dailyInspectionserviceWindow.Focus();
                return;
            }
            dailyInspectionserviceWindow = new daliy_services_window(notificationContextFactory) { Owner = this };
            dailyInspectionserviceWindow.Closed += (_, _) => dailyInspectionserviceWindow = null;
            // يطابق فتح النموذج حواريًا سلوك المنظومة القديمة ويبقي الرئيسية معطّلة حتى إغلاقه.
            dailyInspectionserviceWindow.ShowDialog();
        }
    }
}
