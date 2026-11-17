using DevExpress.Data;
using DevExpress.Drawing;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Grid;
using DevExpress.XtraPrinting;
using DevExpress.XtraReports.UI;
using MahApps.Metro.Controls;
using Maintenance_app.Accounts;
using Maintenance_app.Dashboard;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using Maintenance_app.viewo_Model;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Maintenance_app.reports;

// نافذة التقرير تعيد آلية Report_Maint القديمة، لكن بالقراءة الحالية من EF Core ومكونات DevExpress.
public partial class MaintenanceReportsWindow : MetroWindow
{
    #region حالة التقرير والتهيئة
    private readonly Func<Maintenance_DabaseContext>? createContext;
    private List<MaintenanceReportRow> allRows = [];
    private List<MaintenanceReportRow> displayedRows = [];
    // يمنع إعادة تطبيق الفلاتر أثناء ملء القوائم أو مسحها برمجيًا.
    private bool fillingFilters;
    private WindowState previousWindowState = WindowState.Maximized;

    private sealed record MaintenanceReportRow(
        MaintenanceOrderKey Key, long Number, DateOnly OrderDate, string Date, string Building,
        string Service, string MaintenanceType, string Technicians, string Status, string Details, string Scope,
        BranchCompletion? Completion = null, string BranchName = "", decimal Cost = 0m, short? Serv_no = null,
        IReadOnlyList<PrintDetails>? PrintSegments = null, string? Inspector = null,
        IReadOnlyList<BranchDetail>? BranchDetails = null)
    {
        // يبقى مفتاح الفرع مستقلاً عن اسمه الظاهر لتجميع التكليفات دون خلط الفروع المتشابهة.
        public short BranchId => Key.BranchId;
        // يحافظ ترتيب المفتاح النصي على أحدث التواريخ ثم أرقام التكليفات داخل كل فرع.
        public string AssignmentKey { get; } = string.Create(CultureInfo.InvariantCulture,
            $"{OrderDate.DayNumber:D7}|{Number:D19}|{Key.BranchId}|{Key.LocationId}|{Key.Location}");
    }

    // ترتيب القيم يتبع قائمة أشكال الطباعة في الواجهة.
    private enum SummaryPrintLayout { Current, ByDate, ByBuilding, ByTechnician, ByService, HeaderOnly, ByBranch, HeadersByBranch }

    public MaintenanceReportsWindow(Func<Maintenance_DabaseContext>? contextFactory)
    {
        createContext = contextFactory ?? (() => new Maintenance_DabaseContext(new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlServer(App.ConnectionString).Options));
        InitializeReportWindow();
    }

    // يثبت التكبير داخل مساحة العمل، ويحرر الاستعلامات والمعاينة عند إغلاق النافذة.
    private void InitializeReportWindow()
    {
        InitializeComponent();
        ConfigureAssignmentGrid();
        WindowWorkArea.Attach(this);
        Closed += (_, _) =>
        {
            windowLifetime.Cancel();
            branchScope?.Dispose();
            windowLifetime.Dispose();
            if (DataContext is maint_reports_viewomodel report) report.Dispose();
            ReleasePrintPreview();
        };
    }
    #endregion

    #region تجميع بنود التكليف وعرض الرأس
    // لكل بطاقة ظاهرة طلب مستقل يمكن إلغاؤه عند إعادة استخدام صفها أو خروجه من العرض.
    private readonly Dictionary<ContentControl, CancellationTokenSource> assignmentHeaderRequests = [];

    // يربط القالب المشترك حقول الرأس من المصدر المحلي أو المجمّع، ولو كان الرقم وحده متاحًا بعد.
    private sealed record AssignmentHeader(long Number, DateOnly? Date = null, string? Building = null,
        string? Inspector = null, string? Type = null, string? Service = null, string? Status = null,
        decimal? Cost = null, string? Technicians = null, string? BranchName = null);

    // يُستخدم قالب التكليف والأعمدة الثلاثة في الكشف المحلي وكشف الفروع.
    private void ConfigureAssignmentGrid()
    {
        if (!IsBranchReport && DataContext is maint_reports_viewomodel report)
            report.ReportContextFactory = createContext!;

        foreach (var column in new[] { ReportNumberColumn, ReportDateColumn, ReportBuildingColumn,
                     ReportServiceColumn, ReportTypeColumn, ReportStateColumn, ReportPlaceColumn })
        {
            column.Visible = false;
            column.ShowInColumnChooser = false;
        }

        ReportStatementColumn.Header = "تفاصيل الصيانة";
        ReportStatementColumn.AllowGrouping = DevExpress.Utils.DefaultBoolean.False;
        AssignmentActionColumn.Visible = AssignmentScopeColumn.Visible = true;
        ReportTotalCostPanel.Visibility = Visibility.Visible;
        ReportsGrid.RestoreStateKeyFieldName = IsBranchReport
            ? nameof(BranchReportLine.RowKey) : nameof(maint_reports_viewomodel.ReportGridLine.ID);
        // تظهر عناوين الأعمدة تحت بطاقة كل تكليف مع خطوط تفصل خلايا البيان.
        ReportStatementColumn.CellTemplate = (DataTemplate)FindResource("Report.AssignmentStatementCell");
        ReportsTableView.ShowColumnHeaders = false;
        ReportsTableView.ShowHorizontalLines = true;
        ReportsTableView.ShowVerticalLines = true;
        var assignmentTemplate = (DataTemplate)FindResource("Report.AssignmentGroup");
        if (IsBranchReport)
        {
            // مجموعة الفرع تحتفظ بعنوانها وإجمالياتها، وتظهر بطاقة التكليف في المستوى التالي.
            BranchGroupColumn.GroupIndex = 0;
            ReportsTableView.GroupRowTemplateSelector = new BranchGroupTemplateSelector(
                (DataTemplate)FindResource("Report.BranchGroup"), assignmentTemplate);
        }
        else ReportsTableView.GroupRowTemplate = assignmentTemplate;
        AssignmentGroupColumn.SortOrder = ColumnSortOrder.Descending;
        AssignmentGroupColumn.GroupIndex = IsBranchReport ? 1 : 0;
        ReportsGrid.AutoExpandAllGroups = true;
    }

    // يفتح محرر التصفية المدمج من زر العمود الموجود في شريط عناوين المجموعة.
    private void AssignmentColumnFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GridColumn column })
            ReportsTableView.ShowFilterEditor(column);
    }

    private async void AssignmentHeader_Loaded(object sender, RoutedEventArgs e) =>
        await LoadAssignmentHeaderAsync((ContentControl)sender);

    private void AssignmentHeader_Unloaded(object sender, RoutedEventArgs e) =>
        CancelAssignmentHeaderLoad((ContentControl)sender);

    // عند إعادة تدوير صف مرئي يتبدل مفتاحه، فيقرأ الرأس بيانات المجموعة الجديدة.
    private async void AssignmentHeader_TargetUpdated(object sender, DataTransferEventArgs e)
    {
        if (sender is ContentControl { IsLoaded: true } header && e.Property == FrameworkElement.TagProperty)
            await LoadAssignmentHeaderAsync(header);
    }

    private async Task LoadAssignmentHeaderAsync(ContentControl header)
    {
        CancelAssignmentHeaderLoad(header);
        if (windowLifetime.IsCancellationRequested || header.Tag is not string groupKey)
        {
            header.Content = null;
            return;
        }

        if (IsBranchReport)
        {
            // نتيجة البحث المجمّع تتضمن الرأس، لذلك لا نعيد الاستعلام عند التمرير أو الطي.
            header.Content = branchAssignmentsByKey.TryGetValue(groupKey, out var row)
                ? new AssignmentHeader(row.Number, row.OrderDate, row.Building, row.Inspector,
                    row.MaintenanceType, row.Service, row.Status, row.Cost, row.Technicians, row.BranchName)
                : null;
            return;
        }

        if (ParseAssignmentGroupKey(groupKey) is not { } key)
        {
            header.Content = null;
            return;
        }

        header.Content = new AssignmentHeader(key.Number);
        // قراءة الرأس بمفتاحه المركب تعمل حتى إذا كان الصف مطويًا أو لم تُحمّل بنوده بعد.
        var source = ReportsGrid.ItemsSource;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        assignmentHeaderRequests[header] = request;

        bool IsCurrentHeader() => !request.IsCancellationRequested && header.IsLoaded
            && Equals(header.Tag, groupKey) && ReferenceEquals(source, ReportsGrid.ItemsSource);

        try
        {
            // نمهل الجدول حتى يكمل ربط القالب كي لا نطلب بيانات مفتاح تبدّل أثناء تجهيز الصف.
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            if (!IsCurrentHeader()) return;

            await using var context = createContext!();
            var details = await MaintenanceOrderEfStore.ByKey(context.T_Emergency_MaintenanceCosts.AsNoTracking(), key)
                .Include(order => order.T_Build)
                .Include(order => order.T_Service)
                .Include(order => order.T_Employe)
                .Include(order => order.T_Emp_Maints).ThenInclude(technician => technician.T_Employe)
                .Include(order => order.T_MaintenanceCosts_Parts)
                .Include(order => order.T_FinishedMaintenances)
                .AsSplitQuery()
                .SingleOrDefaultAsync(request.Token);

            // يمنع التحقق تطبيق نتيجة قديمة على بطاقة أُعيد استخدامها أو على نتائج بحث جديد.
            if (IsCurrentHeader() && details is not null)
                header.Content = new AssignmentHeader(key.Number, details.Date_com, details.T_Build?.Build,
                    details.T_Employe?.Emp_Name, details.MaintenanceTypeName, details.T_Service?.Serv_type,
                    details.MaintenanceStatusName, details.Cost, details.Technicians);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // يظل رقم التكليف مستقلًا عن إلغاء طلب التفاصيل أثناء التمرير أو الإغلاق.
        }
        catch (System.Data.Common.DbException)
        {
            if (IsCurrentHeader()) ReportsStatus.Text = "تعذّر تحميل تفاصيل بعض التكليفات.";
        }
        finally
        {
            if (assignmentHeaderRequests.TryGetValue(header, out var current) && ReferenceEquals(current, request))
                assignmentHeaderRequests.Remove(header);
        }
    }

    // نزيل الطلب أولًا حتى لا يتداخل إلغاؤه مع طلب أحدث لنفس عنصر العرض.
    private void CancelAssignmentHeaderLoad(ContentControl header)
    {
        if (assignmentHeaderRequests.Remove(header, out var request)) request.Cancel();
    }

    // يضم المفتاح رقم التكليف ثم الفرع ومعرّف الموقع واسمه، وقد يحتوي اسم الموقع على فاصل.
    private static MaintenanceOrderKey? ParseAssignmentGroupKey(string groupKey)
    {
        var parts = groupKey.Split('|', 4);
        if (parts.Length != 4) return null;

        var numberText = parts[0].TrimStart('0');
        if (!long.TryParse(numberText.Length == 0 ? "0" : numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            || !short.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var branchId)
            || !short.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var locationId))
            return null;

        return new MaintenanceOrderKey(parts[3], locationId, branchId, number);
    }
    #endregion

    #region تحميل بيانات التقرير وقوائم الفلاتر
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // القوائم لا تعتمد على وجود تقرير سابق، لذلك تقرأ من جداولها مباشرة عند فتح الشاشة.
        // أما الصفوف فلا تُجلب إلا بأمر المستخدم «عرض» حتى لا تظهر بيانات تلقائيًا.
        ShowReportButton.IsEnabled = false;
        StatusFilter.ItemsSource = new[] { "منجزة", "غير منجزة", "معلّقة", "قيد الإنجاز"  };
        var token = windowLifetime.Token;
        try
        {
            if (IsBranchReport)
            {
                SetBranchFiltersAvailable(false);
                await LoadBranchesAsync();
            }
            else
            {
                await LoadFilterChoicesAsync();
                if (!token.IsCancellationRequested)
                    ReportsStatus.Text = "حدد الفلاتر المطلوبة ثم اضغط «عرض» لإظهار تقارير الصيانة.";
            }
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            ReportsStatus.Text = "تعذّر تحميل قوائم التقرير.";
            UiMessages.Show(this, Title, ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!token.IsCancellationRequested) ShowReportButton.IsEnabled = true;
        }
    }

    // تحميل مصادر القوائم المستقلة يمنع ظهور قائمة فارغة عندما لا تتضمن النتائج الحالية مبنى أو فنيًا بعينه.
    private async Task LoadFilterChoicesAsync()
    {
        try
        {
            fillingFilters = true;
            await using var db = createContext!();
            BuildingFilter.ItemsSource = await db.T_Builds.AsNoTracking().OrderBy(item => item.Build).
                ToListAsync();
            ServiceFilter.ItemsSource = await db.T_Services
    .AsNoTracking()
    .OrderBy(item => item.Serv_type)
    .ToListAsync();

            BuildingFilter.ItemsSource = await db.T_Builds
    .AsNoTracking()
    .OrderBy(item => item.Build)
    .ToListAsync();
            TechnicianFilter.ItemsSource = await db.T_Employes
    .AsNoTracking()
    .OrderBy(item => item.Emp_Name)
    .ToListAsync();

        

        }
        finally
        {
            fillingFilters = false;
        }
    }

    private async Task LoadReportsAsync()
    {
        try
        {
            ReportsStatus.Text = "جارِ تحميل بيانات تقارير الصيانة…";
            //allRows = await Task.Run(LoadReportRows);
            ApplyFilters();
        }
        catch (Exception ex)
        {
            fillingFilters = false;
            ReportsStatus.Text = "تعذّر تحميل تقارير الصيانة. " + ex.GetBaseException().Message;
        }
    }


    private List<T_Emergency_MaintenanceCost> LoadReportRows(
        DateOnly? from,
        DateOnly? to,
        string? building,
        short? service,
        string? type,
        string? technician,
        string? status)
    {
        using var db = createContext!();

        IQueryable<T_Emergency_MaintenanceCost> orders =
            db.T_Emergency_MaintenanceCosts
                .AsNoTracking()
                .AsSplitQuery();

        // فلترة التاريخ
        if (from.HasValue)
            orders = orders.Where(x => x.Date_com >= from.Value);

        if (to.HasValue)
            orders = orders.Where(x => x.Date_com <= to.Value);

        // المبنى
        if (!string.IsNullOrWhiteSpace(building))
            orders = orders.Where(x =>
                x.T_Build.Build == building);

        // نوع الخدمة
        if (service.HasValue)
            orders = orders.Where(x =>
                x.Serv_num == service.Value);

        // نوع الصيانة
        if (!string.IsNullOrWhiteSpace(type))
        {
            short? typeId = type switch
            {
                "دورية" => 0,
                "وقائية" => 1,
                "طارئة" => 2,
                _ => null
            };

            orders = typeId.HasValue
                ? orders.Where(x => x.Type == typeId.Value)
                : orders.Where(x => false);
        }

        // الفني
        if (!string.IsNullOrWhiteSpace(technician))
            orders = orders.Where(x =>
                x.T_Emp_Maints.Any(e =>
                    e.T_Employe.Emp_Name == technician));

        // حالة الصيانة
        if (!string.IsNullOrWhiteSpace(status))
        {
            byte? stateId = status switch
            {
                "منجزة" => 0,
                "غير منجزة" => 1,
                "معلّقة" => 2,
                "قيد الإنجاز" => 3,
                _ => null
            };

            if (stateId.HasValue)
            {
                orders = orders.Where(x =>
                    x.T_FinishedMaintenances
                        .OrderByDescending(f => f.Date_Modify)
                        .ThenByDescending(f => f.ID)
                        .Select(f => (byte?)f.State)
                        .FirstOrDefault() == stateId.Value);
            }
            else if (status == "لم يتم الإجراء")
            {
                orders = orders.Where(x =>
                    !x.T_FinishedMaintenances.Any() &&
                    x.Emergency_MaintenanceCosts_ST != true);
            }
            else if (status == "قيد المراجعة")
            {
                orders = orders.Where(x =>
                    !x.T_FinishedMaintenances.Any() &&
                    x.Emergency_MaintenanceCosts_ST == true);
            }
            else
            {
                orders = orders.Where(x => false);
            }
        }

        // تحميل العلاقات المطلوبة للتقرير
        return orders
            .Include(x => x.T_Build)
            .Include(x => x.T_Service)
            .Include(x => x.T_Emp_Maints)
                .ThenInclude(x => x.T_Employe)
            .Include(x => x.T_Emergency_MaintenanceCosts_Services)
                .ThenInclude(x => x.T_Manage)
            .Include(x => x.T_Emergency_MaintenanceCosts_Services)
                .ThenInclude(x => x.T_Section)
            .Include(x => x.T_FinishedMaintenances)
            .Include(x => x.T_MaintenanceCosts_Parts)
            .OrderByDescending(x => x.Date_com)
            .ToList();
    }

    private static string JoinScope(string? management, string? section) => string.Join(" - ", new[] { management, section }.Where(x => !string.IsNullOrWhiteSpace(x)));
    private static string MaintenanceType(short? type) => type switch { 0 => "دورية", 1 => "وقائية", 2 => "طارئة", _ => "غير محدد" };
    private static string Status(T_Emergency_MaintenanceCost order)
    {
        // نجعل الحالة قابلة للقيمة الخالية؛ FirstOrDefault للقيمة الرقمية كان يحوّل التكليف بلا تقرير إلى «منجزة» (0).
        var state = order.T_FinishedMaintenances.OrderByDescending(x => x.Date_Modify).ThenByDescending(x => x.ID).Select(x => (byte?)x.State).FirstOrDefault();
        return Status(state, order.Emergency_MaintenanceCosts_ST);
    }

    // توحيد مسميات الحالات بين قاعدة الصيانة المحلية والقاعدة المجمّعة.
    // التكليف المعلّم كمكتمل يبقى قيد المراجعة إذا غاب تقرير الإنهاء أو كانت حالته غير معروفة.
    private static string Status(byte? state, bool? completed) => state switch
    {
        0 => "منجزة",
        1 => "غير منجزة",
        2 => "معلّقة",
        3 => "قيد الإنجاز",
        _ when completed != true => "لم يتم الإجراء",
        _ => "قيد المراجعة"
    };
    #endregion

    #region تفعيل الفلاتر والتحقق منها
    // عند إلغاء معيار، نمسح القيمة وخطأ التحقق المرتبط بالخانة.
    private void ToggleFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (sender == DateFilter && DateFilter.IsChecked != true)
        {
            ClearFilterValue(FromDate);
            ClearFilterValue(ToDate);
        }
        if (sender == BuildingFilterEnabled && BuildingFilterEnabled.IsChecked != true) ClearFilterValue(BuildingFilter);
        if (sender == ServiceFilterEnabled && ServiceFilterEnabled.IsChecked != true) ClearFilterValue(ServiceFilter);
        if (sender == MaintenanceTypeFilterEnabled && MaintenanceTypeFilterEnabled.IsChecked != true) ClearFilterValue(MaintenanceTypeFilter);
        if (sender == TechnicianFilterEnabled && TechnicianFilterEnabled.IsChecked != true) ClearFilterValue(TechnicianFilter);
        if (sender == StatusFilterEnabled && StatusFilterEnabled.IsChecked != true) ClearFilterValue(StatusFilter);
        if (!fillingFilters && allRows.Count > 0) ApplyFilters();
    }

    private static void ClearFilterValue(BaseEdit editor)
    {
        editor.EditValue = null;
        AccountFieldValidation.Clear(editor);
    }

    // تعديل قيمة خانة مفعلة ينعكس فورًا على النتائج دون إعادة الاستعلام من قاعدة البيانات.
    private void Filter_EditValueChanged(object sender, EditValueChangedEventArgs e)
    {
        if (sender is BaseEdit editor) AccountFieldValidation.Clear(editor);
        if (!fillingFilters && allRows.Count > 0) ApplyFilters();
    }

    private bool ValidateEnabledFilters()
    {
        if (DateFilter.IsChecked == true && FromDate.EditValue is not DateTime)
            return InvalidFilter(FromDate, "أدخل تاريخ البداية.");
        if (DateFilter.IsChecked == true && ToDate.EditValue is not DateTime)
            return InvalidFilter(ToDate, "أدخل تاريخ النهاية.");
        if (DateFilter.IsChecked == true && (DateTime)FromDate.EditValue! > (DateTime)ToDate.EditValue!)
            return InvalidFilter(ToDate, "تاريخ النهاية يجب أن يساوي أو يأتي بعد تاريخ البداية.");
        if (BuildingFilterEnabled.IsChecked == true && string.IsNullOrWhiteSpace(BuildingFilter.Text)) return InvalidFilter(BuildingFilter, "اختر المبنى.");
        if (ServiceFilterEnabled.IsChecked == true && string.IsNullOrWhiteSpace(ServiceFilter.Text)) return InvalidFilter(ServiceFilter, "اختر نوع الخدمات.");
        if (MaintenanceTypeFilterEnabled.IsChecked == true && string.IsNullOrWhiteSpace(MaintenanceTypeFilter.Text)) return InvalidFilter(MaintenanceTypeFilter, "اختر نوع الصيانة.");
        if (TechnicianFilterEnabled.IsChecked == true && string.IsNullOrWhiteSpace(TechnicianFilter.Text)) return InvalidFilter(TechnicianFilter, "اختر الفني.");
        if (StatusFilterEnabled.IsChecked == true && string.IsNullOrWhiteSpace(StatusFilter.Text)) return InvalidFilter(StatusFilter, "اختر حالة الإنهاء.");
        return true;
    }

    private bool InvalidFilter(BaseEdit editor, string message)
    {
        AccountFieldValidation.Show(editor, message);
        return false;
    }
    #endregion

    #region العرض والمسح وتلخيص النتائج
    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        try
        { 
        if (!ShowReportButton.IsEnabled || !ValidateEnabledFilters()) return;
            // بيانات الفروع تُقرأ من سياق التجميع مع نطاق الفرع المحدد وصلاحية النافذة.
            if (IsBranchReport)
            {
                // مثل التقرير المحلي، يعيد العرض الشامل إظهار البنود المخفية بفلتر أحد الأعمدة.
                if (SelectedBranchId is null && DateFilter.IsChecked != true && BuildingFilterEnabled.IsChecked != true
                    && ServiceFilterEnabled.IsChecked != true && MaintenanceTypeFilterEnabled.IsChecked != true
                    && TechnicianFilterEnabled.IsChecked != true && StatusFilterEnabled.IsChecked != true)
                    ReportsGrid.FilterCriteria = null;
                await LoadBranchReportsAsync(SelectedBranchId);
                return;
            }

            #region ارسال متغييرات البحث
            if (DataContext is maint_reports_viewomodel vm)
            {

                #region متغييرات تفعيل خانات البحث
                vm.isdate = (bool)DateFilter.IsChecked!;
                vm.isbuilding = (bool)BuildingFilterEnabled.IsChecked!;
                vm.isservice = (bool)ServiceFilterEnabled.IsChecked!;
                vm.istype = (bool)MaintenanceTypeFilterEnabled.IsChecked!;
                vm.ismen = (bool)TechnicianFilterEnabled.IsChecked!;
                vm.isstate = (bool)StatusFilterEnabled.IsChecked!;

                #endregion
                #region متغييرات البحث
                vm.build_num = ((T_Build)BuildingFilter.SelectedItem)?.Branch_ID;
                vm.serv_num = ((T_Service)ServiceFilter.SelectedItem)?.Serv_num;
                vm.typeid = MaintenanceTypeFilter.SelectedIndex;
                vm.stateid = (byte?)StatusFilter.SelectedIndex;
                vm.man_id = ((T_Employe)TechnicianFilter.SelectedItem)?.Employe_ID;


                vm.datefrom = FromDate.DateTime;
                vm.dateto = ToDate.DateTime;


                #endregion




                #region البحث وعرض البيانات
                // عند عرض الكل، نمسح فلتر الأعمدة المتبقي من العرض السابق حتى يطابق الجدول العدد الكلي.
                if (!IsBranchReport && !(vm.isdate || vm.isbuilding || vm.isservice || vm.istype || vm.ismen || vm.isstate))
                    ReportsGrid.FilterCriteria = null;

                vm.ExecuteSearch();
                ReportsGrid.RefreshData();
                if (!IsBranchReport) ReportsStatus.Text = "";
                #endregion
            }
            
        }
        catch(Exception ex)
        {
            UiMessages.Show(this, Title, ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }   
        #endregion

    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        fillingFilters = true;
        if (IsBranchReport)
        {
            BranchFilterEnabled.IsChecked = false;
            ClearFilterValue(BranchFilter);
        }
        DateFilter.IsChecked = BuildingFilterEnabled.IsChecked = ServiceFilterEnabled.IsChecked = MaintenanceTypeFilterEnabled.IsChecked = TechnicianFilterEnabled.IsChecked = StatusFilterEnabled.IsChecked = false;
        FromDate.EditValue = ToDate.EditValue = BuildingFilter.EditValue = ServiceFilter.EditValue = MaintenanceTypeFilter.EditValue = TechnicianFilter.EditValue = StatusFilter.EditValue = null;
        fillingFilters = false;
        foreach (var editor in new BaseEdit[] { BranchFilter, FromDate, ToDate, BuildingFilter, ServiceFilter, MaintenanceTypeFilter, TechnicianFilter, StatusFilter })
            AccountFieldValidation.Clear(editor);
        if (IsBranchReport)
        {
            ReportsGrid.FilterCriteria = null;
            await RefreshBranchScopeAsync();
        }
        else if (DataContext is maint_reports_viewomodel report)
        {
            // إبقاء ربط ItemsSource حتى يستقبل الجدول مصدر البحث التالي بعد مسح النتائج.
            report.ClearSearch();
            ReportsStatus.Text = "حدد الفلاتر المطلوبة ثم اضغط «عرض» لإظهار تقارير الصيانة.";
        }
    }

    private void ApplyFilters()
    {
        // في تقارير الفروع تكون الفترة قد طُبقت في استعلام التجميع عند الضغط على «عرض».
        DateOnly? from = !IsBranchReport && DateFilter.IsChecked == true &&
                         FromDate.EditValue is DateTime fromDate
            ? DateOnly.FromDateTime(fromDate)
            : null;

        DateOnly? to = !IsBranchReport && DateFilter.IsChecked == true &&
                       ToDate.EditValue is DateTime toDate
            ? DateOnly.FromDateTime(toDate)
            : null;

        string? building = BuildingFilterEnabled.IsChecked == true
            ? BuildingFilter.Text?.Trim()
            : null;

        short? service = ServiceFilterEnabled.IsChecked == true &&
                         ServiceFilter.EditValue is short id
            ? id
            : null;
        // قوائم قاعدة التجميع أسماء نصية، بينما يحتفظ التقرير المحلي بترميز الخدمة.
        var branchService = IsBranchReport && ServiceFilterEnabled.IsChecked == true ? ServiceFilter.Text?.Trim() : null;
        string? type = MaintenanceTypeFilterEnabled.IsChecked == true
            ? MaintenanceTypeFilter.Text?.Trim()
            : null;

        string? technician = TechnicianFilterEnabled.IsChecked == true
            ? TechnicianFilter.Text?.Trim()
            : null;

        string? status = StatusFilterEnabled.IsChecked == true
            ? StatusFilter.Text?.Trim()
            : null;

        displayedRows = allRows.Where(row =>
            (!from.HasValue || row.OrderDate >= from.Value) &&
            (!to.HasValue || row.OrderDate <= to.Value) &&
            (string.IsNullOrEmpty(building) || row.Building == building) &&
            (!service.HasValue || row.Serv_no == service.Value) &&
            (string.IsNullOrEmpty(branchService) || row.Service == branchService) &&
            (string.IsNullOrEmpty(type) || row.MaintenanceType == type) &&
            (string.IsNullOrEmpty(technician) ||
                row.Technicians.Split('،', StringSplitOptions.TrimEntries)
                    .Contains(technician)) &&
            (string.IsNullOrEmpty(status) || row.Status == status)
        ).ToList();

        if (IsBranchReport) ShowBranchAssignments();
        else ReportsGrid.ItemsSource = displayedRows;

        TotalCount.Text = displayedRows.Count.ToString("N0");
        if (IsBranchReport) TotalCostAmount.Text = displayedRows.Sum(row => row.Cost).ToString("N3") + " د.ل";

        CompletedCount.Text = displayedRows
            .Count(x => x.Status == "منجزة").ToString("N0");

        NotCompletedCount.Text = displayedRows
            .Count(x => x.Status == "غير منجزة").ToString("N0");

        SuspendedCount.Text = displayedRows
            .Count(x => x.Status == "معلّقة").ToString("N0");

        InProgressCount.Text = displayedRows
            .Count(x => x.Status == "قيد الإنجاز").ToString("N0");

        ReportsStatus.Text = allRows.Count == 0
            ? (IsBranchReport
                ? BranchSearchPrompt
                : "فعّل معايير الفلترة المطلوبة ثم اضغط عرض.")
            : "عدد التكليفات المعروضة: " +
              displayedRows.Count.ToString("N0") +
              " — انقر مرتين لعرض تقرير الإنهاء.";
    } 
    #endregion

    #region طباعة كشف الصيانة بقالب DevExpress
    private XtraReport? previewReport;

    // يقرأ زر الطباعة نتائج آخر بحث من الـViewModel ثم يفتح معاينة القالب داخل النافذة.
    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (!PrintReportButton.IsEnabled || IsBranchReport && !ShowReportButton.IsEnabled) return;
        if (PrintMode.SelectedIndex < 0)
        {
            InvalidFilter(PrintMode, "اختر شكل الطباعة.");
            return;
        }

        var layout = (SummaryPrintLayout)PrintMode.SelectedIndex;
        var token = windowLifetime.Token;
        PrintReportButton.IsEnabled = false;
        try
        {
            IReadOnlyList<MaintenanceReportRow> rows = displayedRows;
            if (!IsBranchReport)
            {
                if (DataContext is not maint_reports_viewomodel vm) return;

                var source = vm.ItemsSource;
                var orders = await vm.LoadPrintOrdersAsync(token);
                // إذا تغيّر مصدر العرض أثناء القراءة، تُترك النتيجة القديمة دون فتح المعاينة.
                if (token.IsCancellationRequested || !ReferenceEquals(source, vm.ItemsSource)) return;

                rows = orders.Select(CreateLocalPrintRow).ToArray();
            }

            if (rows.Count == 0)
            {
                UiMessages.Show(this, Title, "لا توجد نتائج للطباعة. اعرض نتائج التقرير أولًا.",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ReleasePrintPreview();
            previewReport = CreateSummaryReport(rows, layout);
            previewReport.CreateDocument();
            PreviewViewer.DocumentSource = previewReport;
            MaintenancePrintFlyout.IsOpen = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // إغلاق النافذة يلغي قراءة التكليفات قبل إنشاء المستند.
        }
        catch (Exception ex)
        {
            ReleasePrintPreview();
            if (!token.IsCancellationRequested)
                UiMessages.Show(this, Title, ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!token.IsCancellationRequested) PrintReportButton.IsEnabled = true;
        }
    }

    // نضع بنود التكليف في صف واحد ونحافظ على ترتيب التفاصيل والمواقع والإجراءات المتناظرة.
    private static MaintenanceReportRow CreateLocalPrintRow(T_Emergency_MaintenanceCost order)
    {
        var lines = order.T_Emergency_MaintenanceCosts_Services.OrderBy(line => line.ID).ToArray();
        return new MaintenanceReportRow(
            new MaintenanceOrderKey(order.Location, order.Location_ID, order.Branch_ID, order.Emergency_MaintenanceCosts_No),
            order.Emergency_MaintenanceCosts_No,
            order.Date_com,
            order.Date_com.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            order.T_Build?.Build ?? "",
            order.T_Service?.Serv_type ?? "",
            order.MaintenanceTypeName,
            order.Technicians,
            order.MaintenanceStatusName,
            string.Join(Environment.NewLine, lines.Select(line => line.Statement)),
            string.Join(Environment.NewLine, lines.Select(line => line.place)),
            BranchName: order.Branch?.Branch ?? App.Branch_Name ?? $"فرع {order.Branch_ID}",
            Cost: order.Cost,
            PrintSegments: CreatePrintSegments(lines.Select(line =>
                new PrintDetails(line.Statement, line.place, maint_reports_viewomodel.GetActionName(line.Maint_Type)))));
    }

    // تُصدّر الأوامر مستند DevExpress نفسه المعروض في لوحة المعاينة.
    private void ExportPreview_Click(object sender, RoutedEventArgs e)
    {
        if (previewReport is not { } report) return;
        var extension = PreviewExportFormat.SelectedIndex switch { 1 => "xlsx", 2 => "docx", _ => "pdf" };
        var dialog = new SaveFileDialog
        {
            Title = "تصدير التقرير",
            FileName = Title,
            DefaultExt = "." + extension,
            Filter = $"{PreviewExportFormat.SelectedItem} (*.{extension})|*.{extension}"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            switch (extension)
            {
                case "xlsx": report.ExportToXlsx(dialog.FileName); break;
                case "docx": report.ExportToDocx(dialog.FileName); break;
                default: report.ExportToPdf(dialog.FileName); break;
            }
        }
        catch (Exception ex)
        {
            UiMessages.Show(this, Title, ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PreviewFlyout_IsOpenChanged(object sender, RoutedEventArgs e)
    {
        if (!MaintenancePrintFlyout.IsOpen) ReleasePrintPreview();
    }

    // نفصل مصدر المعاينة قبل تحرير المستند الذي يستعمله العارض.
    private void ReleasePrintPreview()
    {
        if (PreviewViewer is not null) PreviewViewer.DocumentSource = null;
        previewReport?.Dispose();
        previewReport = null;
    }

    // تحافظ خصائص الربط على أسماء حقول قالب REPX، وتُجزّأ البيانات الطويلة للطباعة.
    private sealed record PrintBuilding(string Build);
    private sealed record PrintBranch(string Branch);
    private sealed record PrintDetails(string Statement, string place, string Maint_Type);
    private sealed record SummaryPrintRow(
        MaintenanceReportRow Row, string Building, string BranchHeading, string UserName,
        string GroupKey, string GroupCaption, PrintDetails Details, bool FirstSegment, bool LastSegment)
    {
        // مفتاح المجموعة مستقل عن رقم التكليف المطبوع حتى لا تُدمج تكليفات الفروع والمواقع المختلفة.
        public string AssignmentKey => $"{Row.BranchId}|{Row.Key.LocationId}|{Row.Number}|{Row.Key.Location}";
        public short BranchId => Row.BranchId;
        public string BranchCaption => "الفرع: " + Row.BranchName;
        public long Emergency_MaintenanceCosts_No => Row.Number;
        public DateTime Date_com => Row.OrderDate.ToDateTime(TimeOnly.MinValue);
        public PrintBuilding T_Build { get; } = new(Building);
        public PrintBranch Branch { get; } = new(BranchHeading);
        public PrintDetails T_Emergency_MaintenanceCosts_Services => Details;
        private (string Department, string Unit) Organization { get; } = T_Emergency_MaintenanceCost.GetOrganizationNames(Row.BranchId);
        public string mangordep => Organization.Department;
        public string deporunit => Organization.Unit;
        public string this_user => UserName;
        public string Technicians => Row.Technicians;
        public string MaintenanceTypeName => Row.MaintenanceType;
        public string MaintenanceStatusName => Row.Status;
        public decimal Cost => Row.Cost;
        // يظل مبلغ التكليف ظاهرًا في رأسه المكرر، وتساهم حزمة واحدة فقط في الإجمالي.
        public decimal SummaryCost => FirstSegment ? Cost : 0m;
    }

    // يبقى كل بيان مع موقعه وإجرائه، وتسمح الحزم القصيرة بنقل البنود إلى الصفحة التالية.
    private static PrintDetails[] CreatePrintSegments(IEnumerable<PrintDetails> details) => details.Chunk(8)
        .Select(segment => new PrintDetails(
            string.Join(Environment.NewLine, segment.Select(detail => detail.Statement)),
            string.Join(Environment.NewLine, segment.Select(detail => detail.place)),
            string.Join(Environment.NewLine, segment.Select(detail => detail.Maint_Type))))
        .ToArray();

    // نملأ القالب بنتائج EF Core ونربط كل حزم البيان بمفتاح التكليف نفسه.
    private XtraReport CreateSummaryReport(IReadOnlyList<MaintenanceReportRow> rows, SummaryPrintLayout layout)
    {
        var allBranches = IsBranchReport && SelectedBranchId is null;
        // قوالب التصنيف تشترك مع التقرير المحلي، ويضاف مستوى الفرع فقط في معاينة تقارير الفروع.
        var addBranchGroup = IsBranchReport && layout is (SummaryPrintLayout.ByDate or SummaryPrintLayout.ByBuilding
            or SummaryPrintLayout.ByTechnician or SummaryPrintLayout.ByService);
        var groupedByBranch = addBranchGroup || layout is SummaryPrintLayout.ByBranch or SummaryPrintLayout.HeadersByBranch;
        var user = App.login_user?.User_Name ?? string.Empty;

        var culture = CultureInfo.GetCultureInfo("ar-LY");
        var data = rows.SelectMany(item =>
        {
            var building = allBranches && !groupedByBranch ? $"{item.Building} — {item.BranchName}" : item.Building;
            var technicians = string.IsNullOrWhiteSpace(item.Technicians) ? "غير محدد" : item.Technicians;
            var service = string.IsNullOrWhiteSpace(item.Service) ? "غير محدد" : item.Service;
            // أسماء الفريق تبقى في مجموعة واحدة كي لا تُسجّل تكلفة التكليف لكل فني على حدة.
            var (groupKey, groupCaption) = layout switch
            {
                SummaryPrintLayout.ByDate => (item.OrderDate.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    "لشهر: " + item.OrderDate.ToString("MMMM yyyy", culture)),
                SummaryPrintLayout.ByBuilding => (item.Building, "خاص بمبنى: " + building),
                SummaryPrintLayout.ByTechnician => (technicians, "حسب الفني: " + technicians),
                SummaryPrintLayout.ByService => (service, "حسب نوع الخدمات: " + service),
                SummaryPrintLayout.ByBranch or SummaryPrintLayout.HeadersByBranch =>
                    (item.BranchId.ToString(CultureInfo.InvariantCulture), "الفرع: " + item.BranchName),
                _ => (string.Empty, string.Empty)
            };
            // الكشف المختصر يطبع صفًا واحدًا لكل رأس، بينما تبقى حزم البيان للقوالب التفصيلية.
            IReadOnlyList<PrintDetails> segments = layout is SummaryPrintLayout.HeaderOnly or SummaryPrintLayout.HeadersByBranch
                ? [new PrintDetails(string.Empty, string.Empty, string.Empty)]
                : item.PrintSegments is { Count: > 0 } printSegments
                    ? printSegments : [new PrintDetails(item.Details, item.Scope, string.Empty)];
            return segments.Select((details, index) =>
                new SummaryPrintRow(item, building, allBranches ? "جميع الفروع" : item.BranchName,
                    user, groupKey, groupCaption, details, index == 0, index == segments.Count - 1));
        }).ToList();

        // لكل شكل مطلوب ملف REPX مستقل يمكن تعديله من مصمم التقارير.
        var template = layout switch
        {
            SummaryPrintLayout.ByDate => "Maint_grop_date.repx",
            SummaryPrintLayout.ByBuilding => "Maint_grop_Build.repx",
            SummaryPrintLayout.ByTechnician => "Maint_grop_Technician.repx",
            SummaryPrintLayout.HeaderOnly => "Maint_grop_HeaderOnly.repx",
            SummaryPrintLayout.ByBranch => "Maint_grop_Branch.repx",
            SummaryPrintLayout.HeadersByBranch => "Maint_grop_HeadersByBranch.repx",
            _ => "Maint_grop.repx"
        };
        var path = Path.Combine(AppContext.BaseDirectory, "Reports", template);
        var report = new XtraReport();
        try
        {
            report.LoadLayoutFromXml(path);
            report.DataSource = data;
            report.DataMember = string.Empty;
            report.RequestParameters = false;
            if (report.FindControl("AssignmentDetailsFrame", true) is XRPanel detailsFrame)
            {
                // توصل الجوانب الحزم المتتابعة ويُغلق الإطار عند آخر حزمة من البيان فقط.
                detailsFrame.BeforePrint += (_, _) => detailsFrame.Borders = report.GetCurrentRow() is SummaryPrintRow { LastSegment: false }
                    ? BorderSide.Left | BorderSide.Right : BorderSide.Left | BorderSide.Right | BorderSide.Bottom;
            }
            if (layout == SummaryPrintLayout.ByService) ApplyPrintGrouping(report);
            if (addBranchGroup) ApplyBranchPrintGrouping(report);
            return report;
        }
        catch
        {
            report.Dispose();
            throw;
        }
    }

    // يُضاف تصنيف الخدمة فوق مجموعة التكليف المستقلة في قالب الكشف الأساسي.
    private static void ApplyPrintGrouping(XtraReport report)
    {
        const float captionHeight = 26;
        var header = new GroupHeaderBand
        {
            Level = 1,
            HeightF = captionHeight + 4,
            GroupUnion = GroupUnion.WithFirstDetail,
            RepeatEveryPage = true
        };
        header.GroupFields.Add(new GroupField(nameof(SummaryPrintRow.GroupKey)));

        var caption = new XRLabel
        {
            LeftF = 10,
            TopF = 4,
            WidthF = report.FindControl("AssignmentHeaderFrame", true)?.WidthF
                ?? report.PageWidthF - report.Margins.Left - report.Margins.Right - 20,
            HeightF = captionHeight,
            ForeColor = System.Drawing.Color.FromArgb(36, 74, 124),
            TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleLeft,
            Padding = new PaddingInfo(6, 6, 0, 0)
        };
        caption.ExpressionBindings.Add(new ExpressionBinding("BeforePrint", "Text", "[GroupCaption]"));
        header.Controls.Add(caption);
        report.Bands.Add(header);
    }

    // يعلو مستوى الفرع تصنيف الشهر أو المبنى أو الفني أو الخدمة، ويتكرر عنوانه عند متابعة المجموعة في صفحة جديدة.
    private static void ApplyBranchPrintGrouping(XtraReport report)
    {
        const int branchLevel = 2;
        const float inset = 10;
        var width = report.FindControl("AssignmentHeaderFrame", true)?.WidthF
            ?? report.PageWidthF - report.Margins.Left - report.Margins.Right - 2 * inset;
        var header = new GroupHeaderBand
        {
            Name = "BranchGroupHeader",
            Level = branchLevel,
            HeightF = 39,
            GroupUnion = GroupUnion.WithFirstDetail,
            RepeatEveryPage = true
        };
        // المعرّف هو مفتاح التجميع، ويظهر اسم الفرع في الشريط حتى عند تشابه اسم فرعين.
        header.GroupFields.Add(new GroupField(nameof(SummaryPrintRow.BranchId)));
        var caption = new XRLabel
        {
            Name = "BranchGroupCaption",
            LeftF = inset,
            TopF = 2,
            WidthF = width,
            HeightF = 32,
            BackColor = System.Drawing.Color.FromArgb(33, 63, 106),
            ForeColor = System.Drawing.Color.White,
            Font = new DXFont("Arial", 12, DXFontStyle.Bold),
            TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleLeft,
            Padding = new PaddingInfo(12, 12, 0, 0)
        };
        caption.ExpressionBindings.Add(new ExpressionBinding("BeforePrint", "Text", $"[{nameof(SummaryPrintRow.BranchCaption)}]"));
        header.Controls.Add(caption);

        var footer = new GroupFooterBand
        {
            Name = "BranchGroupFooter",
            Level = branchLevel,
            HeightF = 48,
            GroupUnion = GroupFooterUnion.WithLastDetail,
            KeepTogether = true
        };
        var totals = new XRTable
        {
            LeftF = inset,
            TopF = 3,
            WidthF = width,
            HeightF = 35,
            BackColor = System.Drawing.Color.FromArgb(221, 233, 247),
            ForeColor = System.Drawing.Color.FromArgb(33, 63, 106),
            BorderColor = System.Drawing.Color.FromArgb(156, 178, 206),
            Borders = BorderSide.All,
            BorderWidth = 0.8f,
            Font = new DXFont("Arial", 11, DXFontStyle.Bold),
            TextAlignment = DevExpress.XtraPrinting.TextAlignment.MiddleCenter
        };
        // يجمع التكاليف مرة واحدة لكل تكليف ويعد المفاتيح المميزة مهما تعددت حزم البيان أو التصنيفات داخل الفرع.
        var cost = new XRTableCell { Name = "BranchSubtotalValue", Summary = new XRSummary { Running = SummaryRunning.Group } };
        cost.ExpressionBindings.Add(new ExpressionBinding("BeforePrint", "Text", "FormatString('{0:N3}', sumSum([SummaryCost]))"));
        var count = new XRTableCell { Name = "BranchAssignmentCount", Summary = new XRSummary { Running = SummaryRunning.Group } };
        count.ExpressionBindings.Add(new ExpressionBinding("BeforePrint", "Text", "sumDCount([AssignmentKey])"));
        var totalRow = new XRTableRow();
        totalRow.Cells.AddRange([new XRTableCell { Text = "إجمالي الفرع (د.ل)" }, cost,
            new XRTableCell { Text = "عدد تكليفات الفرع" }, count]);
        totals.Rows.Add(totalRow);
        footer.Controls.Add(totals);
        report.Bands.AddRange([header, footer]);
    }
    #endregion

    #region فتح المستند وإغلاق النافذة
    private void ReportsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsBranchReport)
        {
            // يفتح النقر على البند تكليفه من الفرع الصحيح، وتظل عناوين المجموعات للطي والتوسيع.
            if (e.OriginalSource is not DependencyObject source
                || ReportsGrid.GetRow(ReportsTableView.CalcHitInfo(source).RowHandle) is not BranchReportLine line)
                return;

            // تُعرض تفاصيل الفرع من الصف المقروء من القاعدة المجمّعة بصلاحية عرض التقارير.
            var row = line.Order;
            BranchDetailsFlyout.Header = $"تفاصيل تكليف {row.Number} — {row.BranchName}";
            BranchDetailsFlyout.DataContext = row;
            BranchDetailsFlyout.IsOpen = true;
        }
        else if (ReportsGrid.SelectedItem is MaintenanceReportRow row)
            new maintenance_complete(createContext) { Owner = this, InitialOrderKey = row.Key }.ShowDialog();
    }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || PreviewExportFormat.IsPopupOpen) return;
        if (MaintenancePrintFlyout.IsOpen) MaintenancePrintFlyout.IsOpen = false;
        else if (BranchDetailsFlyout.IsOpen) BranchDetailsFlyout.IsOpen = false;
        else return;
        e.Handled = true;
    }

    // عند الاستعادة من شريط النظام تعود النافذة مكبرة، ويظل تغيير الحجم اليدوي متاحًا.
    private void Window_StateChanged(object sender, EventArgs e)
    {
        var restoreToMaximized = previousWindowState == WindowState.Minimized && WindowState == WindowState.Normal;
        previousWindowState = WindowState;
        if (restoreToMaximized) WindowState = WindowState.Maximized;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Resize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;



    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    #endregion
}

#region محاذاة عناوين أعمدة بنود التكليف
public sealed class AssignmentHeaderLayoutConverter : IMultiValueConverter
{
    // نخصم من عرض العمود الأول إزاحة المجموعات الأب المطبقة على صف الرأس، ثم نحجز إزاحة مستوى التكليف فقط داخله.
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [double totalIndent, int level, ..] || level < 0)
            return DependencyProperty.UnsetValue;

        var assignmentIndent = totalIndent / (level + 1);
        return values switch
        {
            [_, _] => assignmentIndent,
            [_, _, double width, 0] => Math.Max(0, width - totalIndent + assignmentIndent),
            [_, _, double width, int] => width,
            _ => DependencyProperty.UnsetValue
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
#endregion
