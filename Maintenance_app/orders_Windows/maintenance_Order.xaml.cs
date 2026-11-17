using DevExpress.CodeParser;
using DevExpress.Xpf.Bars;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Printing;
using DevExpress.Xpf.Printing.PreviewControl.Bars;
using DevExpress.XtraReports.UI;
using MahApps.Metro.Controls;
using Maintenance.Shared;
using Maintenance_app.Accounts;
using Maintenance_app.dataSet;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.viewo_Model;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
 using static System.Runtime.InteropServices.JavaScript.JSType;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Maintenance_app.orders_Windows
{
    public partial class maintenance_Order : MetroWindow
    {
        #region المتغيرات والخصائص وأحداث الواجهة
        // مفتاح التكليف المفتوح: الموقع ورقمه والفرع ورقم التكليف؛ null تعني مسودة جديدة.
        private MaintenanceOrderKey? currentKey;
        // الإطارات التي تحمل أخطاء حاليًا، لمسحها عند التفريغ أو تحميل تكليف آخر.
        private readonly HashSet<Border> invalidFields = new HashSet<Border>();
        // القائمة الأصلية للفنيين أثناء فتحها، لتنسيق الصفوف الحالية والصفوف التي تظهر عند التمرير.
        private System.Windows.Controls.ListBox? technicianPopupList;
        // وضع العمل: 1 للإدراج، و2 لتعديل تكليف سبق حفظه.
        private int _op = 1;
        // يبقى true أثناء عملية الحفظ أو التعديل أو الحذف لمنع تداخل العمليات.
        private bool persistenceBusy;
        // فتح التكليف من الإشعارات يراجع البيانات فقط، ولا يسمح بتجاوز قيود إنهاء الصيانة.
        private bool notificationReadOnly;
        public bool IsNotificationPreview { get; set; }
        // رقم طلب الصيانة الذي سيربط بالتكليف الجديد عند الإدراج؛ اختياري.
        public long? RequestId { get; set; }

        // عمليات قاعدة البيانات ومعاملاتها موجودة في MaintenanceOrderEfStore.cs.
        private readonly MaintenanceOrderEfStore Store;
        // المصدر الافتراضي SQL Server؛ تمرير مصدر مستقل يتيح فحص نفس مسار الطلب دون قاعدة العمل.
        private readonly Func<Maintenance_DabaseContext> contextFactory;
        private readonly bool customContext;
        private bool HasConnection => customContext || !string.IsNullOrWhiteSpace(App.ConnectionString);
        private int CurrentUserId
        {
            get
            {
                return (App.appuser ?? App.login_user)?.User_ID ?? 0;
            }
        }

        // قوائم العرض والبيانات المرتبطة بالجداول؛ لا تحتوي أوامر حفظ.
        public MaintenanceOrderState State { get; } = new MaintenanceOrderState();

        // نسخة السطر الأصلي الجاري تعديله، لتحديد موضع استبداله عند الاعتماد.
        private RepairLine? editingRepair;
        // المادة الأصلية الجاري تعديلها؛ إلغاء اللوحة يمسح المرجع فقط.
        private MaterialLine? editingPart;
        // يحمي من تكرار تحميل القوائم إذا تكرر حدث Loaded.
        private bool loaded;
        // يتغير عند تنظيف البحث حتى لا يعيد استعلام سابق نتائجه بعد إغلاق اللوحة.
        private long searchRevision;
        // يتذكر استخدام التخطيط المضغوط أو الكبير؛ null قبل أول تطبيق.
        private bool? compactLayout;
        private EventHandler<OrderActionEventArgs>? lookupRequested;
        public event EventHandler<OrderSearchRow>? OrderSelected;
        public event EventHandler<OrderActionEventArgs> LookupRequested
        {
            add
            {
                lookupRequested += value;
                UpdateActionButtons();
            }

            remove
            {
                lookupRequested -= value;
                UpdateActionButtons();
            }
        }

        #endregion

        #region تهيئة النافذة والتخطيط وحالات الأزرار
        // تهيئة عناصر XAML وربط قوائم العرض وضبط التاريخ وحدود التكبير.
        // تحميل القوائم من القاعدة يبدأ لاحقًا في Order_Loaded بعد ظهور النافذة.
        public maintenance_Order() : this(null) { }
        public maintenance_Order(Func<Maintenance_DabaseContext>? source)
        {
            customContext = source is not null;
            contextFactory = source ?? CreateDefaultContext;
            Store = new MaintenanceOrderEfStore(contextFactory);
            InitializeComponent();
            ApplyResponsiveLayout(1080);
            DataContext = State;
            date_check.EditValue = DateTime.Today;
            WindowWorkArea.Attach(this);
            UpdateActionButtons();
        }

        // اختيار مقاسات التخطيط المناسبة عند تغيير ارتفاع مساحة محتوى النافذة.
        private void OrderLayout_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayout(e.NewSize.Height);
        }

        // تحديث موارد المقاسات المستخدمة في XAML؛ أقل من 900 يستخدم التخطيط المضغوط.
        // حفظ آخر وضع يمنع إعادة تحديث الموارد عند كل تغير صغير في الحجم.
        private void ApplyResponsiveLayout(double height)
        {
            bool compact = height < 900;
            if (compactLayout == compact)
            {
                return;
            }

            compactLayout = compact;
            // المقاسات تتكيف مع ارتفاع النافذة، ويتولى Windows مقياس العرض.
            Resources["Order.FieldHeight"] = compact ? 38d : 44d;
            Resources["Order.FieldFont"] = compact ? 13d : 15d;
            Resources["Order.LabelFont"] = compact ? 12d : 13d;
            Resources["Order.LabelGap"] = new Thickness(0, 0, 0, compact ? 5 : 6);
            Resources["Order.GridHeaderHeight"] = compact ? 32d : 40d;
            Resources["Order.GridRowHeight"] = compact ? 32d : 40d;
            Resources["Order.HeroHeight"] = new GridLength(compact ? 64 : 80);
            Resources["Order.OverviewHeight"] = new GridLength(compact ? 152 : 184);
            Resources["Order.FooterHeight"] = new GridLength(compact ? 60 : 72);
            Resources["Order.SectionHeaderHeight"] = new GridLength(compact ? 34 : 42);
            Resources["Order.InputRowHeight"] = new GridLength(compact ? 68 : 84);
            Resources["Order.Gutter"] = new Thickness(compact ? 6 : 8, compact ? 12 : 20, compact ? 6 : 8, compact ? 12 : 20);
            Resources["Order.CardPadding"] = new Thickness(compact ? 10 : 16);
            // تقليل الشريط الفاصل بين إطارات الأقسام، مع فاصل أصغر عند ضيق ارتفاع النافذة.
            Resources["Order.CardGap"] = new Thickness(0, 0, 0, compact ? 4 : 6);
        }

        // الحفظ يحتاج مستخدمًا واتصالًا جاهزين؛ الإدراج للمسودة، والتعديل والحذف والطباعة لتكليف مفتوح.
        private void UpdateActionButtons()
        {
            if (Insert is null)
            {
                return;
            }

            bool ready = !notificationReadOnly && !persistenceBusy && CurrentUserId > 0 && HasConnection;
            Insert.IsEnabled = ready && _op == 1;
            Edite_button.IsEnabled = ready && currentKey is not null;
            delete_button.IsEnabled = ready && currentKey is not null;
            Print_Button.IsEnabled = ready && currentKey is not null;
            Add_Buildings.IsEnabled = ready || lookupRequested is not null;
            Add_Mang.IsEnabled = ready || lookupRequested is not null;
            Add_Section.IsEnabled = ready || lookupRequested is not null;
        }

        // تحديث محتويات القائمة نفسها بدل استبدالها، لتبقى ارتباطات XAML وإشعارات الجداول سليمة.
        private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
        {
            target.Clear();
            foreach (T item in source)
            {
                target.Add(item);
            }
        }

        #endregion

        #region تحميل القوائم والاتصال وتصفية الأقسام
        // تحميل البيانات مرة واحدة عند فتح النافذة، ثم عرض رقم مبدئي للتكليف الجديد.
        private async void Order_Loaded(object sender, RoutedEventArgs e)
        {
            if (IsNotificationPreview)
            {
                // منع الكتابة من أول لحظة ظهور، قبل انتظار تحميل بيانات الإشعار من الشبكة.
                notificationReadOnly = true;
                OrderOverviewCard.IsEnabled = RepairPanel.IsEnabled = MaterialsPanel.IsEnabled = OrderActionsCard.IsEnabled = false;
                UpdateActionButtons();
                return;
            }
            if (loaded)
            {
                return;
            }

            loaded = true;
            // انتظار ظهور النافذة قبل تحميل القوائم، مع الحفاظ على حدود التكبير.
            await Dispatcher.InvokeAsync(() =>
            {
            }, DispatcherPriority.ContextIdle);
            try
            {
                await LoadLookupsAsync();
                await PrefillRequestAsync();
                await get_id();
                UpdateActionButtons();

                // بعد تعبئة طلب العميل، يكون الفاحص أول خانة ينتقل إليها المستخدم بلوحة المفاتيح.
                if (RequestId.HasValue && IsLoaded)
                    Combo_Check_Man.Focus();
            }

            catch (Exception ex)
            {
                ShowError("تعذّر تحميل القوائم. " + ex.Message);
            }
        }

        // قراءة قوائم الاختيار دون تتبع تعديلات بواسطة AsNoTracking.
        // قائمة الفنيين للإدخال مقيدة بالقيم Adje=0 أو 2، بينما البحث يحتفظ بجميع الموظفين.
        public async Task LoadLookupsAsync()
        {
            if (!HasConnection)
            {
                return;
            }

            await using Maintenance_DabaseContext db = CreateContext();
            Replace(State.Buildings, await db.T_Builds.AsNoTracking().OrderBy(x => x.Build).ToListAsync());
            var employees = await db.T_Employes.AsNoTracking().OrderBy(x => x.Emp_Name).ToListAsync();
            Replace(State.Employees, employees.Where(x => x.Adje == 0 || x.Adje == 2));
            Replace(State.SearchEmployees, employees);
            Replace(State.Services, await db.T_Services.AsNoTracking().OrderBy(x => x.Serv_type).ToListAsync());
            Replace(State.Managements, await db.T_Manages.AsNoTracking().OrderBy(x => x.Manag).ToListAsync());
            Replace(State.AllDepartments, await db.T_Sections.AsNoTracking().OrderBy(x => x.Secti).ToListAsync());
            State.FilterDepartments(null);
        }

        // إنشاء سياق EF Core مستقل بإعداد الاتصال الحالي؛ using يغلقه بعد انتهاء العملية.
        private Maintenance_DabaseContext CreateContext() => contextFactory();
        private static Maintenance_DabaseContext CreateDefaultContext()
        {
            DbContextOptionsBuilder<Maintenance_DabaseContext> options = new DbContextOptionsBuilder<Maintenance_DabaseContext>();
            options.UseSqlServer(App.ConnectionString);
            return new Maintenance_DabaseContext(options.Options);
        }

        // إلغاء القسم القديم وتصفية الأقسام للإدارة المختارة، حتى لا يبقى اختيار لا يتبعها.
        private void Management_Changed(object sender, EditValueChangedEventArgs e)
        {
            if (DataContext is not MaintenanceOrderState)
            {
                return;
            }

            Combo_Department.EditValue = null;
            State.FilterDepartments(Combo_Management.SelectedItem as T_Manage);
        }

        #endregion

        #region تجهيز بنود الصيانة والمواد ولوحات تعديلها
        // نجهز وصف الطلب وخياراته من بياناته المحفوظة، ثم يبقى اختيار الفاحص والفنيين بيد المستخدم.
        private async Task PrefillRequestAsync()
        {
            if (RequestId is not long requestId) return;
            await using var db = CreateContext();
            var request = await db.T_Notif_Action_Users.AsNoTracking().SingleOrDefaultAsync(x => x.ID == requestId);
            // نتحقق من الحالة المحفوظة لأن البطاقة قد تكون مفتوحة لدى أكثر من مستخدم.
            if (request is null || !RequestProtocol.CanAssign(request.OP_ID, request.MaintenanceCosts_No))
                throw new InvalidOperationException("الطلب أُلغي أو رُبط بتكليف؛ حدّث الإشعارات.");
            var buildings = State.Buildings.Where(x => x.Build_ID == request.Build_ID && x.Build == request.Build).ToList();
            if (buildings.Count == 1)
            {
                Combo_Buildings.EditValue = buildings[0];
                PrefillRequestDepartment(buildings[0], request.Mangment_Request);
            }
            var services = State.Services.Where(x => x.Serv_num == request.Serv_num && (buildings.Count != 1
                || x.Location == buildings[0].Location && x.Location_ID == buildings[0].Location_ID && x.Branch_ID == buildings[0].Branch_ID)).ToList();
            if (services.Count == 1) Combo_Services.EditValue = services[0];
            Text_dtils.EditValue = request.Description; Doc_Type.SelectedIndex = 2;
            Title = "تكليف صيانة لطلب رقم " + requestId;
        }

        private void PrefillRequestDepartment(T_Build building, string? savedName)
        {
            if (string.IsNullOrWhiteSpace(savedName)) return;

            // القسم والوحدة يستخدمان القائمة نفسها، وقد تُحفظ الجهة باسم واحد أو بمسار كامل.
            static string Normalize(string name) => string.Join("|", name.Split(['-', '–', '—', '/', '|', '،'],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

            string name = Normalize(savedName);
            if (name.Length == 0) return;
            var managements = State.Managements.Where(x => x.Location == building.Location
                && x.Location_ID == building.Location_ID && x.Branch_ID == building.Branch_ID).ToList();
            var sections = State.AllDepartments.Where(x => x.Location == building.Location
                && x.Location_ID == building.Location_ID && x.Branch_ID == building.Branch_ID).ToList();
            var pairs = (from management in managements
                         join section in sections on management.Manag_ID equals section.Manag_ID
                         select (Management: management, Section: section)).ToList();
            var exactPairs = pairs.Where(x => Normalize(x.Management.Manag) + "|" + Normalize(x.Section.Secti) == name).ToList();
            if (exactPairs.Count > 1) return;
            if (exactPairs.Count == 1)
            {
                SelectRequestDepartment(exactPairs[0].Management, exactPairs[0].Section);
                return;
            }

            // إذا سُجل «الإدارة - القسم - الوحدة»، نختار الوحدة عند مطابقة الاسمين تحت الإدارة نفسها.
            // تبقى المطابقة الكاملة أعلاه أولًا لأن القائمة قد تخزن القسم والوحدة في اسم واحد.
            var path = name.Split('|');
            if (path.Length == 3)
            {
                var matchingUnits = pairs.Where(x => Normalize(x.Management.Manag) == path[0]
                    && Normalize(x.Section.Secti) == path[2]
                    && sections.Any(section => section.Manag_ID == x.Management.Manag_ID
                        && Normalize(section.Secti) == path[1])).ToList();
                if (matchingUnits.Count == 1)
                {
                    SelectRequestDepartment(matchingUnits[0].Management, matchingUnits[0].Section);
                    return;
                }
                if (matchingUnits.Count > 1) return;
            }

            var exactSections = pairs.Where(x => Normalize(x.Section.Secti) == name).ToList();
            // قد يطابق اسم الجهة إدارةً وبندًا في إدارة أخرى يختلف اسمها فقط بتهجئة «والـ».
            // نربط البند المطابق بإدارته الفعلية عندما يكون هذا الربط وحيدًا داخل الموقع.
            static string NormalizeManagementSpelling(string value) =>
                (" " + Normalize(value)).Replace(" وال", " ول", StringComparison.Ordinal);

            string managementSpelling = NormalizeManagementSpelling(savedName);
            var sectionsUnderMatchingName = exactSections.Where(x =>
                NormalizeManagementSpelling(x.Management.Manag) == managementSpelling).ToList();
            if (sectionsUnderMatchingName.Count == 1)
            {
                SelectRequestDepartment(sectionsUnderMatchingName[0].Management, sectionsUnderMatchingName[0].Section);
                return;
            }

            var exactManagements = managements.Where(x => Normalize(x.Manag) == name).ToList();
            if (exactManagements.Count > 1) return;
            if (exactManagements.Count == 1)
            {
                var management = exactManagements[0];
                var children = sections.Where(x => x.Manag_ID == management.Manag_ID).ToList();
                // إذا حُفظ اسم الإدارة وحده، نملأ القسم فقط عندما يكون لها قسم واحد.
                SelectRequestDepartment(management, children.Count == 1 ? children[0] : null);
                return;
            }

            if (exactSections.Count == 1)
                SelectRequestDepartment(exactSections[0].Management, exactSections[0].Section);
        }

        private void SelectRequestDepartment(T_Manage management, T_Section? section)
        {
            // اختيار الإدارة أولًا يشغّل تصفية الأقسام قبل تعيين القسم من القائمة.
            Combo_Management.EditValue = management;
            Combo_Department.EditValue = section;
        }
        // فحص مدخلات البيان وإضافة سطر إلى قائمة العرض؛ تعاد false مع تركيز الخانة الناقصة.
        // الحفظ الفوري للبند الموجود في القاعدة مسؤولية حدث Add_ToList_Click، لا هذه الدالة.
        public bool AddRepair()
        {
            if (notificationReadOnly) return false;
            ClearInputErrors();
            if (Combo_Services.SelectedItem is not T_Service)
            {
                return RejectInput(Combo_Services, "اختر نوع الخدمة.");
            }

            if (Combo_Management.SelectedItem is not T_Manage manage)
            {
                return RejectInput(Combo_Management, "اختر الإدارة.");
            }

            if (Combo_Department.SelectedItem is not T_Section section)
            {
                return RejectInput(Combo_Department, "اختر القسم أو الوحدة.");
            }

            if (section.Manag_ID != manage.Manag_ID || section.Location != manage.Location || section.Location_ID != manage.Location_ID || section.Branch_ID != manage.Branch_ID)
            {
                return RejectInput(Combo_Department, "اختر قسمًا تابعًا للإدارة والموقع المختارين.");
            }

            if (string.IsNullOrWhiteSpace(Text_dtils.Text))
            {
                return RejectInput(Text_dtils, "أدخل تفاصيل الصيانة.");
            }

            if (Text_dtils.Text.Trim().Length > 200)
            {
                return RejectInput(Text_dtils, "تفاصيل الصيانة يجب ألا تتجاوز 200 حرف.");
            }

            if (Combo_Repier_type.SelectedItem is not string action || !State.RepairActions.Contains(action))
            {
                return RejectInput(Combo_Repier_type, "اختر الإجراء.");
            }

            State.Repairs.Add(new RepairLine(manage, section, action, Text_dtils.Text.Trim(), ((T_Service)Combo_Services.SelectedItem).Serv_type));
            Text_dtils.EditValue = null;
            Text_dtils.Focus();
            return true;
        }

        // إضافة مادة إلى قائمة العرض بعد فحص الاسم والكمية والوحدة والسعر.
        // تستخدم decimal للكمية والسعر، ويحدّث تغير القائمة الإجمالي من خلال State.
        public bool AddMaterial()
        {
            if (notificationReadOnly) return false;
            ClearInputErrors();
            if (string.IsNullOrWhiteSpace(Text_Parts.Text) || Text_Parts.Text.Trim().Length > 150)
            {
                return RejectInput(Text_Parts, "أدخل اسم المادة بما لا يتجاوز 150 حرفًا.");
            }

            if (!ReadDecimal(Text_Quantity, out decimal quantity) || quantity <= 0)
            {
                return RejectInput(Text_Quantity, "أدخل كمية صحيحة أكبر من صفر.");
            }

            if (Combo_Units.SelectedItem is not string unit || !State.Units.Contains(unit))
            {
                return RejectInput(Combo_Units, "اختر الوحدة.");
            }

            if (!ReadDecimal(Text_Value, out decimal price) || price < 0)
            {
                return RejectInput(Text_Value, "أدخل سعرًا صحيحًا لا يقل عن صفر.");
            }

            State.Parts.Add(new MaterialLine(Text_Parts.Text.Trim(), quantity, unit, price));
            Text_Parts.EditValue = null;
            Text_Quantity.EditValue = null;
            Text_Value.EditValue = null;
            Text_Parts.Focus();
            return true;
        }

        // تذكر السطر الجاري تعديله وتعبئة لوحة التعديل؛ تبقى القائمة كما هي حتى زر الاعتماد.
        public void EditRepair(RepairLine row)
        {
            DismissOverlays();
            editingRepair = row;
            // تحميل التكليف وقوائم الاختيار بسياقات EF مستقلة ينتج كائنات مختلفة للسجل نفسه.
            // نختار عنصر القائمة بالمفتاح المركب كي يظهر اسمه، دون خلط أرقام المواقع أو الفروع.
            Combo_Management_Update.EditValue = row.Manage is { } manage
                ? State.Managements.FirstOrDefault(x => x.Location == manage.Location && x.Location_ID == manage.Location_ID
                    && x.Branch_ID == manage.Branch_ID && x.Manag_ID == manage.Manag_ID) : null;
            Combo_Department_update.EditValue = row.Section is { } section
                ? State.AllDepartments.FirstOrDefault(x => x.Location == section.Location && x.Location_ID == section.Location_ID
                    && x.Branch_ID == section.Branch_ID && x.Secti_ID == section.Secti_ID) : null;
            Combo_Repier_type_Update.EditValue = row.Action;
            Text_dtils_Update.EditValue = row.Details;
            Config_Service_Windows.IsOpen = true;
            Text_dtils_Update.Focus();
        }

        // تعبئة لوحة تعديل المادة بقيم السطر المختار دون حفظ التغييرات في هذه المرحلة.
        public void EditMaterial(MaterialLine row)
        {
            DismissOverlays();
            editingPart = row;
            Text_Parts_Update.EditValue = row.Name;
            Text_Quantity_Update.EditValue = row.Quantity;
            Combo_Units_Update.EditValue = row.Unit;
            Text_Value_Update.EditValue = row.Price;
            Config_Parts_Windows.IsOpen = true;
            Text_Parts_Update.Focus();
        }

        // فتح لوحة تعديل بيان الصيانة الذي يقف عليه المؤشر في الجدول.
        private void Repair_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Repiers_Grid.CurrentItem is RepairLine row)
            {
                EditRepair(row);
            }
        }

        // فتح لوحة تعديل المادة المختارة من جدول المواد.
        private void Part_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Parts_Grid.CurrentItem is MaterialLine row)
            {
                EditMaterial(row);
            }
        }

        #endregion

        #region إظهار وإغلاق اللوحات الداخلية
        // تنظيف البحث وإغلاق اللوحات وإلغاء مسودة التعديل، دون تغيير بيانات التكليف.
        private void DismissOverlays()
        {
            ClearInputErrors();
            ResetAdvancedSearch();
            Config_Service_Windows.IsOpen = Config_Parts_Windows.IsOpen = adv_searsh.IsOpen = false;
            editingRepair = null;
            editingPart = null;
        }

        // زر الإغلاق الأصلي للـFlyout ينظف حالة التعديل والتحقق مثل زر الإلغاء.
        private void InternalFlyout_IsOpenChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not Flyout { IsOpen: false } flyout) return;
            ClearInputErrors();
            if (ReferenceEquals(flyout, Config_Service_Windows)) editingRepair = null;
            if (ReferenceEquals(flyout, Config_Parts_Windows)) editingPart = null;
            if (ReferenceEquals(flyout, adv_searsh)) ResetAdvancedSearch();
        }

        // تنظيف لوحة البحث فقط؛ تبقى بيانات التكليف وقوائم الفنيين والخدمات الأصلية كما هي.
        private void ResetAdvancedSearch()
        {
            searchRevision++;
            foreach (BaseEdit field in new BaseEdit[] { from_date, To_date, Combo_emp_Search, Combo_Services_Search })
                field.EditValue = null;
            By_Main.IsChecked = By_Emp.IsChecked = By_Service.IsChecked = false;
            State.SearchResults.Clear();
            Search_Grid.CurrentItem = null;
            State.SearchStatus = string.Empty;
            ClearInputErrors();
        }

        // تنفيذ إغلاق لوحات البحث أو التعديل من أزرار الإلغاء.
        private void DismissOverlay_Click(object sender, RoutedEventArgs e)
        {
            DismissOverlays();
        }

        // إظهار لوحة شروط البحث؛ تنفيذ الاستعلام يتم بزر البحث داخلها.
        private void Search_Click(object sender, RoutedEventArgs e)
        {
            DismissOverlays();
            adv_searsh.IsOpen = true;
        }

        #endregion

        #region البحث المتقدم وتحميل التكليف المختار
        // انتظار اكتمال البحث حتى ينتهي تحديث النتائج ومؤشر الانشغال.
        private async void ExecuteSearch_Click(object sender, RoutedEventArgs e)
        {
            await SearchAsync();
        }

        // بناء بحث للتكليفات غير المنتهية فقط، ثم إضافة الشروط التي اختارها المستخدم.
        // await ينتظر الاستعلام دون حجب واجهة WPF، وfinally يعيد تفعيل زر البحث حتى عند الخطأ.
        public async Task SearchAsync()
        {
            if (sfBusyIndicator.DeferedVisibility)
            {
                return;
            }

            ClearInputErrors();
            long? number = null;
            if (!string.IsNullOrWhiteSpace(Text_emng_Search.Text))
            {
                if (!long.TryParse(Text_emng_Search.Text, out long parsed))
                {
                    RejectInput(Text_emng_Search, "أدخل رقم تكليف صحيحًا.");
                    State.SearchStatus = "أدخل رقم تكليف صحيحًا.";
                    return;
                }

                number = parsed;
            }

            DateOnly? from = null, to = null;
            if (By_Main.IsChecked == true)
            {
                if (from_date.EditValue is not DateTime start || To_date.EditValue is not DateTime end || start.Date > end.Date)
                {
                    RejectInput(from_date.EditValue is not DateTime ? from_date : To_date, "اختر فترة تاريخ صحيحة.");
                    State.SearchStatus = "اختر فترة تاريخ صحيحة.";
                    return;
                }

                from = DateOnly.FromDateTime(start);
                to = DateOnly.FromDateTime(end);
            }

            if (By_Emp.IsChecked == true && Combo_emp_Search.SelectedItem is not T_Employe)
            {
                RejectInput(Combo_emp_Search, "اختر الفني المطلوب.");
                State.SearchStatus = "اختر الفني المطلوب.";
                return;
            }

            if (By_Service.IsChecked == true && Combo_Services_Search.SelectedItem is not T_Service)
            {
                RejectInput(Combo_Services_Search, "اختر نوع الخدمة.");
                State.SearchStatus = "اختر نوع الخدمة.";
                return;
            }

            // فحص المدخلات يسبق فحص الاتصال؛ لا يبدأ أي استعلام عند وجود خطأ في شروط البحث.
            if (!HasConnection)
            {
                State.SearchStatus = "لم يتم إعداد الاتصال بقاعدة البيانات.";
                return;
            }

            // منع إرسال بحث ثان أثناء الانتظار وإفراغ النتائج القديمة قبل عرض النتائج الجديدة.
            Search_Advance_Button.IsEnabled = false;
            sfBusyIndicator.DeferedVisibility = true;
            State.SearchResults.Clear();
            long revision = searchRevision;
            try
            {
                await using Maintenance_DabaseContext db = CreateContext();
                // استبعاد التكليف المنتهي سواء سجلت حالته في الرأس أو وُجد له تقرير إنهاء.
                var query = db.T_Emergency_MaintenanceCosts.AsNoTracking().Where(x => x.Emergency_MaintenanceCosts_ST == false && !x.T_FinishedMaintenances.Any());
                if (number is not null)
                {
                    query = query.Where(x => x.Emergency_MaintenanceCosts_No == number);
                }

                if (from is not null)
                {
                    query = query.Where(x => x.Date_com >= from && x.Date_com <= to);
                }

                if (By_Service.IsChecked == true && Combo_Services_Search.SelectedItem is T_Service service)
                {
                    query = query.Where(x => x.Serv_num == service.Serv_num && x.Location == service.Location && x.Location_ID == service.Location_ID && x.Branch_ID == service.Branch_ID);
                }

                if (By_Emp.IsChecked == true && Combo_emp_Search.SelectedItem is T_Employe employee)
                {
                    query = query.Where(x => x.Location == employee.Location && x.Location_ID == employee.Location_ID && x.Branch_ID == employee.Branch_ID && x.T_Emp_Maints.Any(m => m.Employe_ID == employee.Employe_ID));
                }

                // اختيار حقول جدول النتائج وأسماء الفنيين فقط، مع الاحتفاظ بكل أجزاء المفتاح لفتح السجل الصحيح.
                var rows = await query.OrderByDescending(x => x.Emergency_MaintenanceCosts_No).Select(x => new { Number = x.Emergency_MaintenanceCosts_No, x.Date_com, Building = x.T_Build.Build, Service = x.T_Service.Serv_type, Employees = x.T_Emp_Maints.Select(m => m.T_Employe.Emp_Name).ToList(), x.Location, x.Location_ID, x.Branch_ID }).ToListAsync();
                // أُغلقت اللوحة أو نُظفت أثناء الانتظار؛ لا نعرض نتائج الطلب السابق.
                if (revision != searchRevision) return;
                foreach (var row in rows)
                {
                    State.SearchResults.Add(new OrderSearchRow(row.Number, row.Date_com.ToDateTime(TimeOnly.MinValue), row.Building, string.Join("، ", row.Employees), row.Service, row.Location, row.Location_ID, row.Branch_ID));
                }

                State.SearchStatus = rows.Count == 0 ? "لا توجد نتائج مطابقة." : $"عدد النتائج: {rows.Count}";
            }

            catch (Exception ex)
            {
                if (revision == searchRevision) State.SearchStatus = "تعذّر البحث. " + ex.Message;
            }
            finally
            {
                sfBusyIndicator.DeferedVisibility = false;
                Search_Advance_Button.IsEnabled = true;
            }
        }

        // تحميل التكليف المختار من النتائج ثم إغلاق لوحة البحث وإبلاغ أي مشترك بحدث OrderSelected.
        private async void SearchResult_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sfBusyIndicator.DeferedVisibility || Search_Grid.CurrentItem is not OrderSearchRow row)
            {
                return;
            }

            sfBusyIndicator.DeferedVisibility = true;
            try
            {
                await LoadOrderAsync(row);
                DismissOverlays();
                OrderSelected?.Invoke(this, row);
            }

            catch (Exception ex)
            {
                State.SearchStatus = "تعذّر فتح التكليف. " + ex.Message;
            }
            finally
            {
                sfBusyIndicator.DeferedVisibility = false;
            }
        }

        // تحميل الرأس والفنيين والبنود والمواد، ثم تحويل النافذة من وضع الإدراج إلى التعديل.
        public async Task LoadOrderAsync(OrderSearchRow row, bool readOnly = false)
        {
            ClearInputErrors();
            MaintenanceOrderKey key = new MaintenanceOrderKey(row.Location, row.LocationId, row.BranchId, row.Number);
            T_Emergency_MaintenanceCost order = await Store.LoadAsync(key, allowFinished: readOnly);
            notificationReadOnly = readOnly;
            currentKey = key;
            _op = 2;
            Add_ToList.IsEnabled = true;
            Add_To_Prt_List.IsEnabled = true;
            Repiers_Grid.IsEnabled = true;
            Parts_Grid.IsEnabled = true;
            Combo_Services.IsReadOnly = true;
            // تحديد التكليف بالرقم والموقع ورقم الموقع والفرع لتجنب تكرار الأرقام.
            txt_maint_no.EditValue = order.Emergency_MaintenanceCosts_No;
            date_check.EditValue = order.Date_com.ToDateTime(TimeOnly.MinValue);
            Combo_Buildings.EditValue = State.Buildings.FirstOrDefault(x => x.Build_ID == order.Build_ID && x.Location == order.Location && x.Location_ID == order.Location_ID && x.Branch_ID == order.Branch_ID);
            // إعادة الموظفين المرتبطين بالتكليف إلى قائمة الاختيار حتى لو لم يعودوا ضمن قائمة الإدخال الافتراضية.
            foreach (var employee in State.SearchEmployees.Where(x => x.Location == order.Location && x.Location_ID == order.Location_ID && x.Branch_ID == order.Branch_ID && (x.Employe_ID == order.Employe_ID || order.T_Emp_Maints.Any(m => m.Employe_ID == x.Employe_ID))))
            {
                if (!State.Employees.Contains(employee))
                {
                    State.Employees.Add(employee);
                }
            }

            Combo_Check_Man.EditValue = State.SearchEmployees.FirstOrDefault(x => x.Employe_ID == order.Employe_ID && x.Location == order.Location && x.Location_ID == order.Location_ID && x.Branch_ID == order.Branch_ID);
            Combo_Technical_Man.EditValue = order.T_Emp_Maints.Select(x => (object)x.Employe_ID).ToList();
            Doc_Type.EditValue = order.Type is >= 0 and < 3 ? State.DocumentTypes[order.Type.Value] : null;
            Combo_Services.EditValue = State.Services.FirstOrDefault(x => x.Serv_num == order.Serv_num && x.Location == order.Location && x.Location_ID == order.Location_ID && x.Branch_ID == order.Branch_ID);
            string service = (Combo_Services.SelectedItem as T_Service)?.Serv_type ?? row.Service;
            // تحويل سجلات EF إلى صفوف عرض، وترجمة رموز الإجراء والوحدة إلى النصوص العربية مع حفظ معرّف السطر.
            Replace(State.Repairs, order.T_Emergency_MaintenanceCosts_Services.Select(x => new RepairLine(x.T_Manage, x.T_Section, x.Maint_Type is >= 0 and < 4 ? State.RepairActions[x.Maint_Type] : x.Maint_Type.ToString(), x.Statement ?? string.Empty, service, x.ID)));
            Replace(State.Parts, order.T_MaintenanceCosts_Parts.Select(x => new MaterialLine(x.Part, x.Quantity, x.Unit < State.Units.Length ? State.Units[x.Unit] : x.Unit.ToString(), x.price ?? 0m, x.ID)));
            // لوحة العنوان وأزرار النافذة تبقى متاحة، بينما بيانات التكليف وأوامر الكتابة للقراءة فقط.
            OrderOverviewCard.IsEnabled = RepairPanel.IsEnabled = MaterialsPanel.IsEnabled = OrderActionsCard.IsEnabled = !readOnly;
            if (readOnly) Title = "عرض تكليف الصيانة من الإشعارات — للقراءة فقط";
            UpdateActionButtons();
        }

        #endregion

        #region إضافة المباني والإدارات والأقسام
        // تحديد نوع الإضافة من Tag: مبنى أو إدارة أو قسم.
        // عند وجود مشترك في LookupRequested يفوض الإضافة إليه؛ وإلا يستخدم نافذة الإضافة الافتراضية.
        private async void LookupAdd_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string action)
            {
                return;
            }

            if (lookupRequested is not null)
            {
                lookupRequested.Invoke(this, new OrderActionEventArgs(action));
                return;
            }

            if (!BeginOperation())
            {
                return;
            }

            try
            {
                mybo.IsEnabled = false;
                await using Maintenance_DabaseContext db = CreateContext();
                var locations = await db.T_Locations.AsNoTracking().Include(x => x.Branch).OrderBy(x => x.Location).ToListAsync();
                bool editing = Config_Service_Windows.IsOpen;
                var selected = (editing ? Combo_Management_Update : Combo_Management).SelectedItem as T_Manage;
                var dialog = new OrderLookupWindow(action, locations, State.Managements.ToList(), Combo_Buildings.SelectedItem as T_Build, selected, CreateContext)
                {
                    Owner = this
                };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                switch (dialog.Added)
                {
                    case T_Build building:
                        State.Buildings.Add(building);
                        Combo_Buildings.EditValue = building;
                        break;
                    case T_Manage manage:
                        State.Managements.Add(manage);
                        (editing ? Combo_Management_Update : Combo_Management).EditValue = manage;
                        break;
                    case T_Section section:
                        State.AllDepartments.Add(section);
                        State.FilterDepartments(Combo_Management.SelectedItem as T_Manage);
                        (editing ? Combo_Department_update : Combo_Department).EditValue = section;
                        break;
                }
            }

            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }

        #endregion

        #region تفريغ المسودة وإعادة وضع الإدراج
        // إفراغ المسودة محليًا وإعادة وضع الإدراج؛ لا يحذف هذا الإجراء أي سجل من قاعدة البيانات.
        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ClearInputErrors();
            currentKey = null;
            _op = 1;
            RequestId = null;
            Combo_Services.IsReadOnly = false;
            Add_ToList.IsEnabled = true;
            Add_To_Prt_List.IsEnabled = true;
            Repiers_Grid.IsEnabled = true;
            Parts_Grid.IsEnabled = true;
            State.Repairs.Clear();
            State.Parts.Clear();
            txt_maint_no.EditValue = null;
            Combo_Buildings.EditValue = null;
            Combo_Check_Man.EditValue = null;
            Doc_Type.EditValue = null;
            Combo_Technical_Man.EditValue = null;
            Combo_Services.EditValue = null;
            Combo_Management.EditValue = null;
            Combo_Department.EditValue = null;
            Combo_Repier_type.EditValue = null;
            Text_dtils.EditValue = null;
            Text_Parts.EditValue = null;
            Text_Quantity.EditValue = null;
            Text_Value.EditValue = null;
            Combo_Units.EditValue = null;
            Text_emng_Search.EditValue = null;
            date_check.EditValue = DateTime.Today;
            DismissOverlays();
            UpdateActionButtons();
        }

        #endregion

        #region التحكم بالنافذة وعرض الأخطاء
        // المفتاح Escape يغلق اللوحات الداخلية دون إغلاق نافذة التكليف.
        private void Order_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DismissOverlays();
                e.Handled = true;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        // النقر المزدوج على العنوان يبدّل التكبير، والسحب بالزر الأيسر يحرك النافذة.
        private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        // عرض الخطأ بواسطة ThemedMessageBox ومالكها هذه النافذة، مع إبقاء الثيم الحالي.
        private void ShowError(string message)
        {
            Maintenance_app.Dashboard.UiMessages.Show(this, "تكليف الصيانة", message, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion

        #region إدارة انشغال العمليات ورسائل التأكيد والنجاح
        // حجز التنفيذ لعملية واحدة؛ النقر المتكرر أثناء عملية قائمة يعيد false دون بدء حفظ آخر.
        private bool BeginOperation()
        {
            if (notificationReadOnly) return false;
            if (persistenceBusy)
            {
                return false;
            }

            persistenceBusy = true;
            UpdateActionButtons();
            return true;
        }

        // تحرير حجز التنفيذ وإعادة الواجهة والأزرار لحالتها المناسبة؛ تستدعى داخل finally.
        private void EndOperation()
        {
            persistenceBusy = false;
            mybo.IsEnabled = true;
            UpdateActionButtons();
        }

        // لا يسمح بالحذف إلا بنتيجة Yes؛ الاختيار الافتراضي No لتجنب الحذف غير المقصود.
        private bool ConfirmDelete(string text)
        {
            return Maintenance_app.Dashboard.UiMessages.Show(this, "تكليف الصيانة", text, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        // إظهار نتيجة النجاح بعد اكتمال العملية المطلوبة.
        private void ShowSaved(string text)
        {
            Maintenance_app.Dashboard.UiMessages.Show(this, "تكليف الصيانة", text, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region تنسيق قائمة اختيار الفنيين

        // نبقي قالب DevExpress الأصلي وربط الاختيار كما هما؛ نغير المقاسات والمحاذاة فقط.
        private void TechnicianPopup_Opened(object sender, RoutedEventArgs e)
        {
            technicianPopupList = PopupElements(Combo_Technical_Man.PopupRoot).OfType<System.Windows.Controls.ListBox>().FirstOrDefault();
            if (technicianPopupList == null)
            {
                return;
            }

            technicianPopupList.ItemContainerGenerator.StatusChanged -= TechnicianItems_Generated;
            technicianPopupList.ItemContainerGenerator.StatusChanged += TechnicianItems_Generated;
            FormatTechnicianItems();
        }

        private void TechnicianPopup_Closed(object sender, RoutedEventArgs e)
        {
            if (technicianPopupList != null)
            {
                technicianPopupList.ItemContainerGenerator.StatusChanged -= TechnicianItems_Generated;
                technicianPopupList = null;
            }
        }

        // يشمل ذلك الصفوف المنشأة لاحقًا عند التمرير داخل القوائم الطويلة.
        private void TechnicianItems_Generated(object? sender, EventArgs e)
        {
            if (technicianPopupList?.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                // الانتظار إلى ما بعد التخطيط يضمن إنشاء مربع الاختيار داخل الصف الجديد عند التمرير.
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FormatTechnicianItems));
            }
        }

        private void FormatTechnicianItems()
        {
            if (technicianPopupList == null)
            {
                return;
            }

            for (int index = 0; index < technicianPopupList.Items.Count; index++)
            {
                if (technicianPopupList.ItemContainerGenerator.ContainerFromIndex(index) is not ComboBoxEditItem item)
                {
                    continue;
                }

                item.MinHeight = 36;
                item.FontSize = 15;
                item.Padding = new Thickness(8, 4, 8, 4);
                item.HorizontalContentAlignment = HorizontalAlignment.Right;
                foreach (CheckEdit check in PopupElements(item).OfType<CheckEdit>())
                {
                    check.CheckBoxWidth = 22;
                    check.CheckBoxHeight = 22;
                    check.HorizontalAlignment = HorizontalAlignment.Right;
                    check.HorizontalContentAlignment = HorizontalAlignment.Right;
                    check.FlowDirection = FlowDirection.RightToLeft;
                }
            }
        }

        private static IEnumerable<DependencyObject> PopupElements(DependencyObject? parent)
        {
            if (parent == null)
            {
                yield break;
            }

            yield return parent;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                foreach (DependencyObject child in PopupElements(VisualTreeHelper.GetChild(parent, index)))
                {
                    yield return child;
                }
            }
        }

        #endregion

        #region التحقق المرئي من خانات الإدخال بأسلوب DCMS
        // يوقف محاولة الإدخال فقط؛ لا يعرض كخطأ تشغيل ولا يغير أي معاملة قاعدة بيانات.
        private sealed class InputValidationException : Exception
        {
        }

        // إطار الخانة يحمل Tag المرتبط بقيمة محررها، ويستقبل خطأ WPF دون تعديل EditValue.
        private static Border? InputBorder(BaseEdit editor)
        {
            return (editor.Parent as FrameworkElement)?.Parent as Border;
        }

        private bool RejectInput(BaseEdit editor, string message)
        {
            Border? border = InputBorder(editor);
            var binding = border?.GetBindingExpression(TagProperty);
            if (border != null && binding != null)
            {
                Validation.MarkInvalid(binding, new ValidationError(new ExceptionValidationRule(), binding, message, null));
                invalidFields.Add(border);
            }

            // قد يكون البحث قد عطّل المحتوى أثناء الانتظار؛ نعيده قبل تركيز الخانة ذات الخطأ.
            mybo.IsEnabled = true;
            editor.Focus();
            return false;
        }

        // يسمح لدوال قراءة الرأس والخانات بإيقاف الأمر دون رسالة منبثقة أو تغيير توقيعها.
        private InputValidationException InputError(BaseEdit editor, string message)
        {
            RejectInput(editor, message);
            return new InputValidationException();
        }

        private void Input_EditValueChanged(object sender, EditValueChangedEventArgs e)
        {
            if (sender is BaseEdit editor)
            {
                ClearInputError(editor);
            }
        }

        // الاختيار داخل القائمة قد يسبق اعتماد EditValue؛ نمسح التنبيه دون انتظار إغلاق القائمة.
        private void Input_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (sender is BaseEdit editor)
            {
                ClearInputError(editor);
            }
        }

        // يشمل التغيير من الأزرار أو التقويم قبل اكتمال اعتماد القيمة؛ لا نلغي التغيير نفسه.
        private void Input_EditValueChanging(object sender, EditValueChangingEventArgs e)
        {
            if (sender is BaseEdit editor)
            {
                ClearInputError(editor);
            }
        }

        // مثل DCMS: بدء الكتابة يمسح الرسالة السابقة حتى لو لم يعتمد المحرر القيمة الجديدة بعد.
        private void Input_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is BaseEdit editor)
            {
                ClearInputError(editor);
            }
        }

        private void ClearInputError(BaseEdit editor)
        {
            if (InputBorder(editor) is Border border)
            {
                var binding = border.GetBindingExpression(TagProperty);
                if (binding != null)
                {
                    Validation.ClearInvalid(binding);
                    invalidFields.Remove(border);
                }
            }
        }

        // لا نعيد التحقق عند المسح؛ تظهر أخطاء المطلوب مجددًا عند المحاولة التالية فقط.
        private void ClearInputErrors()
        {
            foreach (Border border in invalidFields)
            {
                var binding = border.GetBindingExpression(TagProperty);
                if (binding != null)
                {
                    Validation.ClearInvalid(binding);
                }
            }

            invalidFields.Clear();
        }

        // توحيد قراءة الأرقام مع الثقافة الحالية، دون تغيير القيمة أو إكراهها على الصفر عند الخطأ.
        private static bool ReadDecimal(BaseEdit editor, out decimal value)
        {
            return decimal.TryParse(Convert.ToString(editor.EditValue, CultureInfo.CurrentCulture), NumberStyles.Number, CultureInfo.CurrentCulture, out value);
        }

        #endregion

        #region قراءة بيانات التكليف والتحقق منها والترقيم
        // تجميع نسخة من مدخلات الرأس والفنيين والبنود والمواد لإرسالها إلى طبقة الحفظ.
        // تتحقق الاختيارات أولًا مع تركيز الخانة الناقصة، ثم تطبق Validate قواعد التكليف كاملة.
        private MaintenanceOrderDraft ReadDraft()
        {
            ClearInputErrors();
            // قراءة خانات الرأس والتحقق منها قبل الاتصال بقاعدة البيانات.
            T_Build? building = Combo_Buildings.SelectedItem as T_Build;
            if (building == null)
            {
                throw InputError(Combo_Buildings, "اختر المبنى.");
            }

            if (date_check.EditValue is not DateTime date)
            {
                throw InputError(date_check, "أدخل تاريخ التكليف.");
            }

            if (date.Date > DateTime.Today)
            {
                throw InputError(date_check, "تاريخ التكليف يجب ألا يكون في المستقبل.");
            }

            T_Employe? inspector = Combo_Check_Man.SelectedItem as T_Employe;
            if (inspector == null)
            {
                throw InputError(Combo_Check_Man, "اختر الفاحص.");
            }

            if (Doc_Type.SelectedIndex < 0)
            {
                throw InputError(Doc_Type, "اختر نوع التكليف.");
            }

            T_Service? service = Combo_Services.SelectedItem as T_Service;
            if (service == null)
            {
                throw InputError(Combo_Services, "اختر نوع الخدمة.");
            }

            // قائمة الفنيين المتعددين تحتوي معرّفات الموظفين المختارين.
            HashSet<int> technicianIds = new HashSet<int>();
            IEnumerable? selected = Combo_Technical_Man.EditValue as IEnumerable;
            if (selected != null)
            {
                foreach (object item in selected)
                {
                    T_Employe? employee = item as T_Employe;
                    if (employee != null)
                    {
                        technicianIds.Add(employee.Employe_ID);
                    }
                    else
                    {
                        technicianIds.Add(Convert.ToInt32(item));
                    }
                }
            }

            List<T_Employe> technicians = new List<T_Employe>();
            foreach (T_Employe employee in State.Employees)
            {
                if (technicianIds.Contains(employee.Employe_ID) && employee.Location == building.Location && employee.Location_ID == building.Location_ID && employee.Branch_ID == building.Branch_ID)
                {
                    technicians.Add(employee);
                }
            }

            if (technicians.Count == 0)
            {
                throw InputError(Combo_Technical_Man, "اختر فنيًا واحدًا على الأقل من موقع المبنى وفرعه.");
            }

            if (service.Location != building.Location || service.Location_ID != building.Location_ID || service.Branch_ID != building.Branch_ID)
            {
                throw InputError(Combo_Services, "الخدمة يجب أن تتبع موقع المبنى وفرعه.");
            }

            if (inspector.Location != building.Location || inspector.Location_ID != building.Location_ID || inspector.Branch_ID != building.Branch_ID)
            {
                throw InputError(Combo_Check_Man, "الفاحص يجب أن يتبع موقع المبنى وفرعه.");
            }

            if (State.Repairs.Count == 0)
            {
                throw InputError(Text_dtils, "أضف بيان صيانة واحدًا على الأقل إلى التكليف.");
            }

            foreach (RepairLine line in State.Repairs)
            {
                if (line.Manage == null || line.Section == null || line.Manage.Location != building.Location || line.Manage.Location_ID != building.Location_ID || line.Manage.Branch_ID != building.Branch_ID)
                {
                    throw InputError(Combo_Buildings, "المبنى وبنود الصيانة يجب أن تتبع الموقع والفرع نفسيهما.");
                }
            }

            MaintenanceOrderDraft draft = new MaintenanceOrderDraft(building, DateOnly.FromDateTime(date), inspector, (short)Doc_Type.SelectedIndex, service, technicians, State.Repairs.ToList(), State.Parts.ToList(), CurrentUserId, RequestId);
            // التحقق الأخير يشمل التاريخ والفنيين والبنود وتوافق الموقع والفرع وصحة الكميات والأسعار.
            MaintenanceOrderEfStore.Validate(draft);
            return draft;
        }

        // عرض رقم مقترح بحسب المستخدم والسنة؛ يعاد حساب الرقم داخل معاملة الإدراج لتجنب التعارض.
        private async Task get_id()
        {
            if (CurrentUserId > 0 && HasConnection)
            {
                txt_maint_no.EditValue = await Store.NextNumberAsync(CurrentUserId, DateTime.Today.Year);
            }
        }

        #endregion

        #region إدراج التكليف وتعديله وحذفه
        // إدراج الرأس والفنيين والبنود والمواد والتنبيهات في معاملة واحدة.
        private async void Insert_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginOperation())
            {
                return;
            }

            try
            {
                MaintenanceOrderDraft draft = ReadDraft();
                mybo.IsEnabled = false;
                // لا يعود مفتاح التكليف إلا بعد اعتماد معاملة الحفظ التي تشمل الرأس والملحقات والتنبيهات.
                currentKey = await Store.InsertAsync(draft);
                RequestId = null;
                await LoadOrderAsync(ToSearchRow(currentKey));
                Add_ToList.IsEnabled = Add_To_Prt_List.IsEnabled = false;
                Repiers_Grid.IsEnabled = Parts_Grid.IsEnabled = false;
                ShowSaved("تم الحفظ");
            }
            catch (InputValidationException)
            {
                // الخطأ ظاهر على الخانة؛ نكتفي بإيقاف الأمر وإعادة حالة الواجهة في finally.
            }
            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }

        // تعديل بيانات التكليف المفتوح ثم إعادة عرض البيانات المحفوظة.
        private async void Edite_button_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginOperation())
            {
                return;
            }

            try
            {
                if (currentKey is null)
                {
                    throw new InvalidOperationException("اعرض التكليف قبل التعديل.");
                }

                MaintenanceOrderDraft draft = ReadDraft();
                mybo.IsEnabled = false;
                // تحديث القاعدة أولًا ثم إعادة التحميل، حتى تعرض الواجهة البيانات التي حُفظت فعلًا.
                await Store.UpdateAsync(currentKey, draft);
                await LoadOrderAsync(ToSearchRow(currentKey));
                ShowSaved("تم التعديل");
            }
            catch (InputValidationException)
            {
                // الخطأ ظاهر على الخانة؛ نكتفي بإيقاف الأمر وإعادة حالة الواجهة في finally.
            }
            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }

        // حذف التكليف وملحقاته بعد موافقة المستخدم.
        private async void delete_button_Click(object sender, RoutedEventArgs e)
        {
            if (currentKey is null || !ConfirmDelete("هل أنت متأكد من حذف تكليف الصيانة وملحقاته؟"))
            {
                return;
            }

            if (!BeginOperation())
            {
                return;
            }

            try
            {
                mybo.IsEnabled = false;
                await Store.DeleteAsync(currentKey);
                Clear_Click(sender, e);
                await get_id();
                ShowSaved("تم الحذف");
            }

            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }

        #endregion

        #region البحث المباشر برقم التكليف
        // تحويل المفتاح إلى صف يحمل بيانات التعريف المطلوبة لإعادة التحميل؛ بقية النصوص مؤقتة وفارغة.
        private static OrderSearchRow ToSearchRow(MaintenanceOrderKey key)
        {
            return new OrderSearchRow(key.Number, DateTime.MinValue, string.Empty, string.Empty, string.Empty, key.Location, key.LocationId, key.BranchId);
        }

        // البحث المباشر بالرقم مع مراعاة الموقع والفرع.
        public async Task show_emn()
        {
            ClearInputErrors();
            if (!long.TryParse(Text_emng_Search.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out long number))
            {
                throw InputError(Text_emng_Search, "أدخل رقم تكليف صحيحًا.");
            }

            await using Maintenance_DabaseContext db = CreateContext();
            var query = db.T_Emergency_MaintenanceCosts.AsNoTracking().Where(x => x.Emergency_MaintenanceCosts_No == number);
            if (Combo_Buildings.SelectedItem is T_Build building)
            {
                query = query.Where(x => x.Location == building.Location && x.Location_ID == building.Location_ID && x.Branch_ID == building.Branch_ID);
            }

            var candidates = await query.Select(x => new MaintenanceOrderKey(x.Location, x.Location_ID, x.Branch_ID, x.Emergency_MaintenanceCosts_No)).Take(2).ToListAsync();
            if (candidates.Count == 0)
            {
                throw InputError(Text_emng_Search, "رقم التكليف غير موجود.");
            }

            if (candidates.Count > 1)
            {
                throw InputError(Text_emng_Search, "الرقم موجود بأكثر من موقع؛ اختر التكليف من البحث المتقدم.");
            }

            await LoadOrderAsync(ToSearchRow(candidates[0]));
        }

        #endregion

        #region إضافة بيان الصيانة واعتماد حفظه
        // البند في المسودة محلي؛ وفي التكليف المفتوح يحفظ فورًا كما في الأصل.
        private async void Add_ToList_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginOperation())
            {
                return;
            }

            try
            {
                if (!AddRepair())
                {
                    return;
                }

                RepairLine row = State.Repairs[State.Repairs.Count - 1];
                if (_op == 2 && currentKey is not null)
                {
                    try
                    {
                        mybo.IsEnabled = false;
                        long id = await Store.AddRepairAsync(currentKey, row);
                        State.Repairs[State.Repairs.Count - 1] = new RepairLine(row.Manage, row.Section, row.Action, row.Details, row.Service, id);

                        ShowSaved("تم الادراج");
                    }

                    catch
                    {
                        // فشل الحفظ الفوري: إزالة السطر المضاف محليًا حتى لا يظهر كبند محفوظ.
                        State.Repairs.Remove(row);
                        throw;
                    }
                }

                Combo_Services.IsReadOnly = true;
                Combo_Management.EditValue = null;
                Combo_Department.EditValue = null;
                Combo_Repier_type.EditValue = null;
                Combo_Management.Focus();
            }

            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }

        #endregion

        #region إضافة المواد وحساب إجماليها
        // إضافة المادة وتحديث الإجمالي، والتراجع عنها عند فشل الحفظ.
        private async void Add_To_Prt_List_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginOperation())
            {
                return;
            }

            try
            {
                if (!AddMaterial())
                {
                    return;
                }

                MaterialLine row = State.Parts[State.Parts.Count - 1];
                if (_op == 2 && currentKey is not null)
                {
                    try
                    {
                        mybo.IsEnabled = false;
                        long id = await Store.AddPartAsync(currentKey, row);
                        State.Parts[State.Parts.Count - 1] = new MaterialLine(row.Name, row.Quantity, row.Unit, row.Price, id);
                        ShowSaved("تم الادراج");

                    }
                    catch
                    {
                        // إلغاء الإضافة المحلية عند فشل حفظ المادة، فيعود الإجمالي إلى قيمته السابقة.
                        State.Parts.Remove(row);
                        throw;
                    }
                }

                get_all_sum();
            }

            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }

        // تحديث القيمة المعروضة في خانة الإجمالي من الخاصية المرتبطة TotalParts في State.
        private void get_all_sum()
        {
            Total_Part.GetBindingExpression(BaseEdit.EditValueProperty)?.UpdateTarget();
        }

        #endregion

        #region حذف بنود الصيانة والمواد
        // قراءة سطر البيان من Tag وطلب التأكيد؛ يحذف من العرض بعد نجاح حذف القاعدة إن كان محفوظًا.
        private async void Service_Delete_click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not RepairLine row || !ConfirmDelete("هل تريد حذف بيان الصيانة؟"))
            {
                return;
            }

            if (!BeginOperation())
            {
                return;
            }

            try
            {
                mybo.IsEnabled = false;
                if (_op == 2 && currentKey is not null)
                {
                    await Store.DeleteRepairAsync(currentKey, row.Id);
                }

                State.Repairs.Remove(row);
                ShowSaved("تم الحذف");

            }

            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }

        // حذف المادة المحددة بعد التأكيد ثم تحديث الإجمالي؛ مواد المسودة تحذف من القائمة فقط.
        private async void Parts_Delete_click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not MaterialLine row || !ConfirmDelete("هل تريد حذف المادة؟"))
            {
                return;
            }

            if (!BeginOperation())
            {
                return;
            }

            try
            {
                mybo.IsEnabled = false;
                if (_op == 2 && currentKey is not null)
                {
                    await Store.DeletePartAsync(currentKey, row.Id);
                }

                State.Parts.Remove(row);
                ShowSaved("تم الحذف");

                get_all_sum();
            }

            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }

        #endregion

        #region اعتماد تعديلات بنود الصيانة والمواد
        // اعتماد تعديل بيان الصيانة المختار.
        private async void Update_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginOperation())
            {
                return;
            }

            try
            {
                ClearInputErrors();
                if (editingRepair is null)
                {
                    return;
                }

                T_Manage? manage = Combo_Management_Update.SelectedItem as T_Manage;
                T_Section? section = Combo_Department_update.SelectedItem as T_Section;
                if (manage is null || section is null || section.Manag_ID != manage.Manag_ID)
                {
                    throw InputError(manage == null ? Combo_Management_Update : Combo_Department_update, "اختر الإدارة والقسم التابع لها.");
                }

                if (Combo_Repier_type_Update.SelectedItem is not string action || string.IsNullOrWhiteSpace(Text_dtils_Update.Text))
                {
                    throw InputError(string.IsNullOrWhiteSpace(Text_dtils_Update.Text) ? Text_dtils_Update : Combo_Repier_type_Update, "أدخل تفاصيل الصيانة واختر الإجراء.");
                }

                if (Text_dtils_Update.Text.Trim().Length > 200)
                {
                    throw InputError(Text_dtils_Update, "تفاصيل الصيانة يجب ألا تتجاوز 200 حرف.");
                }

                if (section.Location != manage.Location || section.Location_ID != manage.Location_ID || section.Branch_ID != manage.Branch_ID)
                {
                    throw InputError(Combo_Department_update, "اختر قسمًا تابعًا لموقع الإدارة وفرعها.");
                }

                RepairLine row = new RepairLine(manage, section, action, Text_dtils_Update.Text.Trim(), editingRepair.Service, editingRepair.Id);
                mybo.IsEnabled = false;
                if (_op == 2 && currentKey is not null)
                {
                    await Store.UpdateRepairAsync(currentKey, row.Id, row);
                }

                int index = State.Repairs.IndexOf(editingRepair);
                if (index >= 0)
                {
                    State.Repairs[index] = row;
                }

                DismissOverlays();
                ShowSaved("تم التعديل");

            }
            catch (InputValidationException)
            {
                // الخطأ ظاهر على الخانة؛ نكتفي بإيقاف الأمر وإعادة حالة الواجهة في finally.
            }
            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }

        // اعتماد تعديل المادة وإعادة حساب إجماليها.
        private async void Update_Parts_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginOperation())
            {
                return;
            }

            try
            {
                ClearInputErrors();
                if (editingPart is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(Text_Parts_Update.Text) || Text_Parts_Update.Text.Trim().Length > 150)
                {
                    throw InputError(Text_Parts_Update, "أدخل اسم المادة بما لا يتجاوز 150 حرفًا.");
                }

                if (!ReadDecimal(Text_Quantity_Update, out decimal quantity) || quantity <= 0)
                {
                    throw InputError(Text_Quantity_Update, "أدخل كمية صحيحة أكبر من صفر.");
                }

                if (Combo_Units_Update.SelectedItem is not string unit || !State.Units.Contains(unit))
                {
                    throw InputError(Combo_Units_Update, "اختر الوحدة.");
                }

                if (!ReadDecimal(Text_Value_Update, out decimal price) || price < 0)
                {
                    throw InputError(Text_Value_Update, "أدخل سعرًا صحيحًا لا يقل عن صفر.");
                }

                MaterialLine row = new MaterialLine(Text_Parts_Update.Text.Trim(), quantity, unit, price, editingPart.Id);
                mybo.IsEnabled = false;
                if (_op == 2 && currentKey is not null)
                {
                    await Store.UpdatePartAsync(currentKey, row.Id, row);
                }

                int index = State.Parts.IndexOf(editingPart);
                if (index >= 0)
                {
                    State.Parts[index] = row;
                }

                DismissOverlays();
                ShowSaved("تم التعديل");

                get_all_sum();

            }
            catch (InputValidationException)
            {
                // الخطأ ظاهر على الخانة؛ نكتفي بإيقاف الأمر وإعادة حالة الواجهة في finally.
            }
            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }

        #endregion

        #region بدء تكليف جديد
        // بدء تكليف جديد وتوليد رقم مبدئي؛ الرقم النهائي يحسب وقت الحفظ.
        private async void clear_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginOperation())
            {
                return;
            }

            try
            {
                Clear_Click(sender, e);
                mybo.IsEnabled = false;
                await get_id();
            }

            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }

        #endregion

        #region طباعة تكليف الصيانة
        // تحميل البيانات للطباعة بالقوالب الأصلية RDLC.
        private async void Print_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginOperation())
            {
                return;
            }

            try
            {
                if (currentKey is null)
                {
                    throw new InvalidOperationException("اعرض التكليف قبل الطباعة.");
                }

                mybo.IsEnabled = false;
                T_Emergency_MaintenanceCost order = await Store.LoadAsync(currentKey, allowFinished: true);
                await using Maintenance_DabaseContext db = CreateContext();
                string inspector = await db.T_Employes.AsNoTracking().Where(x => x.Location == currentKey.Location && x.Location_ID == currentKey.LocationId && x.Branch_ID == currentKey.BranchId && x.Employe_ID == order.Employe_ID).Select(x => x.Emp_Name).SingleOrDefaultAsync() ?? string.Empty;
                if (Combo_Buildings.SelectedItem is not T_Build building)
                {
                    AccountFieldValidation.Show(Combo_Buildings, "اختر المبنى.");
                    return;
                }
                var state = new MaintenanceOrderState();

                var technicians = order.T_Emp_Maints
                    .OrderBy(x => x.id)
                    .Select(x => x.T_Employe.Emp_Name)
                    .ToList();

                DataSet1 dataSet = new DataSet1();

                dataSet.DataTable1.Rows.Clear();
                sub_data parts = new sub_data();
                parts.DataTable1.Rows.Clear();
                foreach (var line in order.T_Emergency_MaintenanceCosts_Services
                                          .OrderBy(x => x.ID))
                {
                    DataRow row = dataSet.DataTable1.NewRow();
                    
                    row["DataColumn29"] = order.Branch_ID==21 ?"ادارة الشؤون الادارية والخدمات" : "قسم الشؤون الادارية والخدمات";
                    row["DataColumn27"] = order.Branch_ID == 21 ? "قسم الصيانة والخدمات / وحدة شؤون الخدمات" : "وحدة الخدمات والعلاقات";
                    row["DataColumn15"] = order.Branch_ID == 21 ? "رئيس وحدة شؤون الخدمات" : "رئيس وحدة الخدمات والعلاقات";
                    row["DataColumn16"] = order.Branch_ID == 21 ? "رئيس قسم الخدمات والصيانة" : "رئيس قسم الشؤون الإدارية والخدمات";
                    row["DataColumn26"] = order.Branch_ID == 21 ? "الادارة او القسم" : "القسم او الوحدة";
                    row["DataColumn25"] = order.Branch_ID == 21 ? "القسم او الوحدة" : "القسم او الوحدة";

                    row["DataColumn1"] = order.T_Service.Serv_type;

                    row["DataColumn32"] =
                        order.Emergency_MaintenanceCosts_No;

                    row["DataColumn12"] =
                        order.Type is >= 0 and < 3
                            ? state.DocumentTypes[order.Type.Value]
                            : string.Empty;

                    row["DataColumn14"] =
                        order.Branch?.Branch ??
                        App.Branch_Name ??
                        string.Empty;

                    row["DataColumn7"] = order.Location;

                    row["DataColumn8"] = order.T_Build.Build;

                    row["DataColumn9"] =
                        technicians.Count == 0
                            ? ""
                            : $"{(technicians.Count > 1 ? "السادة:" : "السيد:")} " +
                              $"{string.Join(" و ", technicians)} " +
                              $"{(technicians.Count > 1 ? "التابعون" : "التابع")} {(order.Branch_ID == 21 ? "لوحدة شؤون الخدمات بقسم الصيانة و الخدمات / بادارة الشؤون الادارية والخدمات" : "لوحدة الخدمات والعلاقات / بقسم الشؤون الادارية والخدمات")}";

                    row["DataColumn2"] =
                        line.Maint_Type is >= 0 and < 4
                            ? state.RepairActions[line.Maint_Type]
                            : string.Empty;

                    row["DataColumn3"] = line.Statement;

                    row["DataColumn4"] =
                        line.T_Section?.Secti ?? string.Empty;

                    row["DataColumn5"] =
                        line.T_Manage?.Manag ?? string.Empty;

                    row["DataColumn20"] =
                        order.Date_com.ToString(
                            "dd/MM/yyyy",
                            CultureInfo.InvariantCulture);

                    // إضافة الصف إلى الجدول
                    dataSet.DataTable1.Rows.Add(row);
                }
               
                foreach (var part in order.T_MaintenanceCosts_Parts.OrderBy(x => x.ID))
                {
                    var row = parts.DataTable1.NewRow();
                    row["DataColumn4"] = part.Part;
                    decimal quantity = decimal.Round(part.Quantity, 3);
                    row["DataColumn5"] = quantity.ToString(quantity == decimal.Truncate(quantity) ? "#,0" : "#,0.000", CultureInfo.CurrentCulture) + "  "
                        + (part.Unit < state.Units.Length ? state.Units[part.Unit] : string.Empty);
                    parts.DataTable1.Rows.Add(row);
                }

                // مسار التقرير الرئيسي
                string mainReportPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "Reports",
                    "Mainte_Report.repx"
                );

                // مسار تقرير المواد
                string partsReportPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "reports",
                    "parts.repx"
                );

                // التحقق من وجود التقرير الرئيسي
                if (!File.Exists(mainReportPath))
                {
                    throw new FileNotFoundException(
                        "ملف تقرير تكليف الصيانة غير موجود",
                        mainReportPath
                    );
                }

                // تحميل التقرير الرئيسي
                XtraReport rb = XtraReport.FromFile(
                    mainReportPath,
                    true
                );

                // تعبئة التقرير الرئيسي
                rb.DataSource = dataSet;
                rb.DataMember = "DataTable1";
                rb.RequestParameters = false;

                // البحث عن XRSubreport داخل التقرير الرئيسي
                XRSubreport subReport = rb.FindControl(
                    "xrSubreport1",
                    true
                ) as XRSubreport;

                if (subReport == null)
                {
                    throw new InvalidOperationException(
                        "لم يتم العثور على اداة التقرير الفرعي في التقرير الرئيسي."
                    );
                }

                // هل التكليف يحتوي على مواد؟
                if (parts.DataTable1.Rows.Count > 0)
                {
                    // التحقق من وجود ملف تقرير المواد
                    if (!File.Exists(partsReportPath))
                    {
                        throw new FileNotFoundException(
                            "ملف تقرير المواد غير موجود",
                            partsReportPath
                        );
                    }

                    // تحميل تقرير المواد
                    XtraReport partsReport = XtraReport.FromFile(
                        partsReportPath,
                        true
                    );

                    // تعبئة تقرير المواد
                    partsReport.DataSource = parts;
                    partsReport.DataMember = "DataTable1";
                    partsReport.RequestParameters = false;

                    // ربط التقرير الفرعي بالرئيسي
                    subReport.ReportSourceUrl = null;
                    subReport.ReportSource = partsReport;

                    subReport.Visible = true;
                    subReport.CanShrink = true;
                }
                else
                {
                    // لا توجد مواد: إخفاء التقرير الفرعي
                    subReport.ReportSourceUrl = null;
                    subReport.ReportSource = null;
                    subReport.Visible = false;
                    subReport.CanShrink = true;
                }


                var preview = new DocumentPreviewWindow();






                preview.WindowState = WindowState.Maximized;
                preview.Title = "نموذج تكليف صيانة";
                var provider = new DocumentCommandProvider();

                #region إزالة
                provider.RibbonActions.Add(
                    new RemoveAction()
                    {
                        ElementName = DefaultPreviewBarItemNames.Find
                    }
                );
                provider.RibbonActions.Add(
                  new RemoveAction()
                  {
                      ElementName = DefaultPreviewBarItemNames.DocumentMap
                  }
                );
                provider.RibbonActions.Add(
                 new RemoveAction()
                 {
                     ElementName = DefaultPreviewBarItemNames.Watermark
                 }
               );
                provider.RibbonActions.Add(
                 new RemoveAction()
                 {
                     ElementName = DefaultPreviewBarItemNames.NavigationPane
                 }
               );
                provider.RibbonActions.Add(
                 new RemoveAction()
                 {
                     ElementName = DefaultPreviewBarItemNames.Open
                 }
               );
                provider.RibbonActions.Add(
                 new RemoveAction()
                 {
                     ElementName = DefaultPreviewBarItemNames.PrintDirect
                 }
               );
                provider.RibbonActions.Add(
                 new RemoveAction()
                 {
                     ElementName = DefaultPreviewBarItemNames.PageLayoutSplit
                 }
               );
                provider.RibbonActions.Add(
                 new RemoveAction()
                 {
                     ElementName = DefaultPreviewBarItemNames.Parameters
                 }
               );
                provider.RibbonActions.Add(
                new RemoveAction()
                {
                    ElementName = DefaultPreviewBarItemNames.Scale
                }
              );
                provider.RibbonActions.Add(
               new RemoveAction()
               {
                   ElementName = DefaultPreviewBarItemNames.ShowCoverPage
               }
             );


                provider.RibbonActions.Add(
              new RemoveAction()
              {
                  ElementName = DefaultPreviewBarItemNames.PageSetup
              }
            );
                provider.RibbonActions.Add(
            new RemoveAction()
            {
                ElementName = DefaultPreviewBarItemNames.EditingFields
            }
          );
                provider.RibbonActions.Add(
         new RemoveAction()
         {
             ElementName = DefaultPreviewBarItemNames.ContinuousScrolling
         }
        );
                // تعيينه للـ PreviewControl
                preview.PreviewControl.CommandProvider = provider;
                #endregion
                ApplicationThemeHelper.ApplicationThemeName = "Win11Light";

                preview.PreviewControl.DocumentSource = rb;
                preview.Show();

                await Task.Run(() =>
                {
                    rb.CreateDocument(false); // أسرع
                });
                preview.Closed += pr_close;


                // إنشاء التقرير بالقالب الأصلي؛ using يحرر موارده بعد إغلاق عارض التقرير.

                //using var report = MaintenanceOrderReport.Create(order, inspector);
                //MaintenanceOrderReport.ShowPreview(this, report);
            }

            catch (Exception ex)
            {
                mybo.IsEnabled = true;
                ShowError(ex.GetBaseException().Message);
            }
            finally
            {
                EndOperation();
            }
        }
        private void pr_close(object? sender, EventArgs e)
        {
            ApplicationThemeHelper.ApplicationThemeName = "None";

        }

        #endregion

        #region اختصارات Enter والتنقل بين الخانات
        // توزيع Enter حسب الخانة: بحث بالرقم أو إضافة بند أو اعتماد تعديل.
        // بقية الخانات تنتقل إلى العنصر التالي، وHandled يمنع تكرار معالجة المفتاح.
        private async void OriginalNavigation_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter || sender is not FrameworkElement field)
            {
                return;
            }

            e.Handled = true;
            switch (field.Name)
            {
                case "Text_emng_Search":
                    if (!BeginOperation())
                    {
                        return;
                    }

                    try
                    {
                        mybo.IsEnabled = false;
                        await show_emn();
                    }
                    catch (InputValidationException)
                    {
                        // الخطأ ظاهر على الخانة؛ نكتفي بإيقاف الأمر وإعادة حالة الواجهة في finally.
                    }
                    catch (Exception ex)
                    {
                        mybo.IsEnabled = true;
                        ShowError(ex.GetBaseException().Message);
                    }
                    finally
                    {
                        EndOperation();
                    }

                    break;
                case "Text_dtils":
                    Add_ToList_Click(sender, e);
                    break;
                case "Text_Value":
                    Add_To_Prt_List_Click(sender, e);
                    break;
                case "Text_Value_Update":
                    Update_Parts_Button_Click(sender, e);
                    break;
                case "Text_dtils_Update":
                    Update_Button_Click(sender, e);
                    break;
                default:
                    field.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    break;
            }
        }
        #endregion
    }
}
