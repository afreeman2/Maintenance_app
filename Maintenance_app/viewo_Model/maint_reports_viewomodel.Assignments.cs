using DevExpress.Data.Linq;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Microsoft.EntityFrameworkCore;

namespace Maintenance_app.viewo_Model;

public partial class maint_reports_viewomodel
{
    #region مصدر بنود الصيانة وتجميع التكليفات
    private delegate IQueryable<T_Emergency_MaintenanceCosts_Service> ReportSearch(Maintenance_DabaseContext context);
    private ReportSearch? currentReportSearch;
    internal Func<Maintenance_DabaseContext>? ReportContextFactory { get; set; }

    // يحفظ شروط البحث كما كانت عند الضغط على «عرض» ليستعملها الجدول والإجماليات والتحديث.
    private ReportSearch CaptureReportSearch()
    {
        var selectedBranch = isbranch ? branch : null;
        var selectedType = istype ? typeid : null;
        var selectedService = isservice ? serv_num : null;
        var selectedState = isstate ? stateid : null;
        var selectedBuilding = isbuilding ? build_num : null;
        var selectedEmployee = ismen ? man_id : null;
        var filterDate = isdate;
        var startDate = datefrom is { } from ? DateOnly.FromDateTime(from) : (DateOnly?)null;
        var endExclusive = dateto is { } to ? DateOnly.FromDateTime(to.Date.AddDays(1)) : (DateOnly?)null;

        return context =>
        {
            var query = context.T_Emergency_MaintenanceCosts_Services.AsNoTracking();
            if (selectedBranch.HasValue)
                query = query.Where(x => x.T_Emergency_MaintenanceCost.Branch_ID == selectedBranch.Value);
            if (selectedType.HasValue)
                query = query.Where(x => x.T_Emergency_MaintenanceCost.Type == selectedType.Value);
            if (selectedService.HasValue)
                query = query.Where(x => x.T_Emergency_MaintenanceCost.Serv_num == selectedService.Value);
            if (selectedState.HasValue)
                query = query.Where(x => x.T_Emergency_MaintenanceCost.T_FinishedMaintenances.Any(k => k.State == selectedState.Value));
            if (filterDate)
                query = query.Where(x => x.T_Emergency_MaintenanceCost.Date_com >= startDate && x.T_Emergency_MaintenanceCost.Date_com < endExclusive);
            if (selectedBuilding.HasValue)
                query = query.Where(x => x.T_Emergency_MaintenanceCost.Build_ID == selectedBuilding.Value);
            if (selectedEmployee.HasValue)
                query = query.Where(x => x.T_Emergency_MaintenanceCost.T_Emp_Maints.Any(k => k.Employe_ID == selectedEmployee.Value));
            return query;
        };
    }

    // يعرض الجدول كيان البند الأصلي ويضيف فقط حقولًا قابلة للفرز والتجميع على الخادم.
    public sealed class ReportGridLine
    {
        public required T_Emergency_MaintenanceCosts_Service Line { get; init; }
        public long ID { get; init; }
        public short BranchId { get; init; }
        public short LocationId { get; init; }
        public required string Location { get; init; }
        public required string AssignmentKey { get; init; }
        public required string Statement { get; init; }
        public string? Management { get; init; }
        public short ActionCode { get; init; }

        public string ActionName => GetActionName(ActionCode);
        // يقرأ قالب الموقع المشترك الوصف المحسوب من الإدارة والقسم في البند المحلي.
        public string Scope => Line.place;
    }

    // تُستخدم مسميات نوع الإجراء نفسها في الجدول وكشف الطباعة.
    internal static string GetActionName(short actionCode) => actionCode switch
    {
        0 => "صيانة",
        1 => "تغيير",
        2 => "تركيب",
        3 => "مناولة",
        _ => actionCode.ToString()
    };

    private EntityInstantFeedbackSource CreateItemsSource(ReportSearch search)
    {
        var showAssignments = ReportContextFactory is not null;
        var createContext = ReportContextFactory ?? (() => new Maintenance_DabaseContext(
            new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlServer(App.ConnectionString).Options));
        var source = new EntityInstantFeedbackSource
        {
            // رقم التكليف مشترك بين البنود. المفتاح المركب للبند يحافظ عليها جميعًا.
            KeyExpression = showAssignments ? "Location;LocationId;BranchId;ID" : nameof(T_Emergency_MaintenanceCosts_Service.Emergency_MaintenanceCosts_No)
        };
        source.GetQueryable += (_, e) =>
        {
            var context = createContext();
            e.Tag = context;
            // الكيانات للقراءة فقط وعلاقات الرأس محمّلة مع البنود، لذلك يمكن قراءة الصف في قالب المجموعة.
            e.AreSourceRowsThreadSafe = showAssignments;
            var lines = search(context)
                .Include(x => x.T_Manage)
                .Include(x => x.T_Section)
                .Include(x => x.T_Emergency_MaintenanceCost).ThenInclude(x => x.T_Employe)
                .Include(x => x.T_Emergency_MaintenanceCost).ThenInclude(x => x.T_Emp_Maints).ThenInclude(x => x.T_Employe)
                .Include(x => x.T_Emergency_MaintenanceCost).ThenInclude(x => x.T_FinishedMaintenances)
                .Include(x => x.T_Emergency_MaintenanceCost).ThenInclude(x => x.T_MaintenanceCosts_Parts)
                .Include(x => x.T_Emergency_MaintenanceCost).ThenInclude(x => x.T_Location)
                .Include(x => x.T_Emergency_MaintenanceCost).ThenInclude(x => x.Branch)
                .Include(x => x.T_Emergency_MaintenanceCost).ThenInclude(x => x.T_Build)
                .Include(x => x.T_Emergency_MaintenanceCost).ThenInclude(x => x.T_Service)
                .OrderByDescending(x => x.Emergency_MaintenanceCosts_No);
            // تعرض النسخة المشتركة للنافذة البنود الأصلية عندما لا يطلب المستدعي شكل التجميع المحلي.
            e.QueryableSource = showAssignments
                ? lines.Select(x => new ReportGridLine
                {
                    Line = x,
                    ID = x.ID,
                    Location = x.Location,
                    LocationId = x.Location_ID,
                    BranchId = x.Branch_ID,
                    // توحيد عرض الجزء الرقمي يحافظ على ترتيب أرقام التكليف تنازليًا داخل المفتاح الكامل.
                    AssignmentKey = ("0000000000000000000" + x.Emergency_MaintenanceCosts_No.ToString())
                        .Substring(x.Emergency_MaintenanceCosts_No.ToString().Length)
                        + "|" + x.Branch_ID.ToString() + "|" + x.Location_ID.ToString() + "|" + x.Location,
                    Statement = x.Statement,
                    Management = x.T_Manage.Manag,
                    ActionCode = x.Maint_Type
                })
                : lines;
        };
        source.DismissQueryable += (_, e) => (e.Tag as Maintenance_DabaseContext)?.Dispose();
        return source;
    }
    #endregion

    #region قراءة بيانات كشف الطباعة
    // يعيد كل تكليف مطابق لآخر بحث مرة واحدة، مع العلاقات التي يحتاجها كشف الطباعة.
    internal async Task<List<T_Emergency_MaintenanceCost>> LoadPrintOrdersAsync(CancellationToken token)
    {
        if (currentReportSearch is not { } search || ReportContextFactory is not { } createContext)
            return [];

        await using var db = createContext();
        var matchingLines = search(db);
        return await db.T_Emergency_MaintenanceCosts
            .AsNoTracking()
            .Where(order => matchingLines.Any(line =>
                line.Emergency_MaintenanceCosts_No == order.Emergency_MaintenanceCosts_No
                && line.Branch_ID == order.Branch_ID
                && line.Location_ID == order.Location_ID
                && line.Location == order.Location))
            .Include(order => order.Branch)
            .Include(order => order.T_Build)
            .Include(order => order.T_Service)
            .Include(order => order.T_Emp_Maints).ThenInclude(technician => technician.T_Employe)
            .Include(order => order.T_FinishedMaintenances)
            .Include(order => order.T_MaintenanceCosts_Parts)
            .Include(order => order.T_Emergency_MaintenanceCosts_Services).ThenInclude(line => line.T_Manage)
            .Include(order => order.T_Emergency_MaintenanceCosts_Services).ThenInclude(line => line.T_Section)
            .OrderByDescending(order => order.Emergency_MaintenanceCosts_No)
            .ThenBy(order => order.Branch_ID).ThenBy(order => order.Location_ID).ThenBy(order => order.Location)
            .AsSplitQuery()
            .ToListAsync(token);
    }
    #endregion

    #region إجماليات نتائج التقرير المحلي
    public sealed record ReportTotals(decimal Cost, int Count, int Completed, int Incomplete, int Suspended, int InProgress)
    {
        internal static readonly ReportTotals Empty = new(0m, 0, 0, 0, 0, 0);
    }

    private CancellationTokenSource? totalsRequest;
    private ReportTotals? totals = ReportTotals.Empty;
    public ReportTotals? Totals
    {
        get => totals;
        private set
        {
            totals = value;
            RaisePropertyChanged(nameof(Totals));
        }
    }

    private string totalsStatus = "";
    public string TotalsStatus
    {
        get => totalsStatus;
        private set
        {
            totalsStatus = value;
            RaisePropertyChanged(nameof(TotalsStatus));
        }
    }

    private void CancelReportTotals()
    {
        totalsRequest?.Cancel();
        totalsRequest = null;
    }

    // يقرأ تكلفة وحالة كل تكليف مرة واحدة، مهما كان عدد بنوده، ويتجاهل نتيجة البحث الملغى.
    private async Task UpdateReportTotalsAsync(ReportSearch search)
    {
        if (ReportContextFactory is not { } createContext) return;
        CancelReportTotals();
        using var request = new CancellationTokenSource();
        totalsRequest = request;
        Totals = null;
        TotalsStatus = "جارٍ حساب الإجماليات…";
        try
        {
            await using var context = createContext();
            var orders = await search(context)
                .Select(line => line.T_Emergency_MaintenanceCost)
                .Distinct()
                .Select(order => new
                {
                    Cost = order.T_MaintenanceCosts_Parts.Sum(part => part.Quantity * (part.price ?? 0m)),
                    // تعتمد العدادات آخر تقرير إنهاء حسب ترتيب الحالة المستخدم في التكليف.
                    State = order.T_FinishedMaintenances.OrderByDescending(item => item.Date_Modify)
                        .ThenByDescending(item => item.ID).Select(item => (byte?)item.State).FirstOrDefault()
                })
                .ToListAsync(request.Token);
            if (request.IsCancellationRequested) return;

            Totals = new ReportTotals(orders.Sum(order => order.Cost), orders.Count,
                orders.Count(order => order.State == 0), orders.Count(order => order.State == 1),
                orders.Count(order => order.State == 2), orders.Count(order => order.State == 3));
            TotalsStatus = orders.Count == 0 ? "لا توجد نتائج تطابق الفلاتر." : "";
        }
        catch (Exception) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            TotalsStatus = "تعذّر حساب إجماليات التقرير.";
        }
        finally
        {
            if (ReferenceEquals(totalsRequest, request)) totalsRequest = null;
        }
    }
    #endregion
}
