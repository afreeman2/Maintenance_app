using Maintenance_app.dbContext;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;

namespace Maintenance_app.Dashboard;

#region نتائج الإحصاءات السنوية وحالات الصيانة
// مجموعة سنوية مجمعة في القاعدة؛ لا تُحمّل أسماء الموظفين أو بنود التكليف إلى الرئيسية.
// Month يتيح عرض ربع السنة أو نصفها من المصدر نفسه، والقيمة 0 تبقي التوافق مع أي اختبار/مصدر قديم مجمّع سنويًا.
public sealed record AnnualMaintenanceCount(int Year, bool HasReport, byte? ReportState, bool? FinishedFlag, long Count, int Month = 0);
public sealed record AnnualStatisticsResult(IReadOnlyList<AnnualMaintenanceCount> Counts);
// مرشح شاشة الإحصائية؛ null يعني عدم تقييد الإحصائية بمبنى معين.
public sealed record AnnualStatisticsFilter(short? BuildingId = null);
#endregion

public sealed class AnnualStatisticsRepository(Func<Maintenance_DabaseContext> createContext)
{
    #region قراءة آخر تقرير لكل تكليف وتجميع الأعداد السنوية
    public async Task<AnnualStatisticsResult> LoadAsync(AnnualStatisticsFilter? filter = null, CancellationToken cancellationToken = default)
    {
        await using var db = createContext();
        return new(await Query(db, filter).ToListAsync(cancellationToken));
    }

    // استعلام المجاميع نفسه قابل للفحص على SQLite وترجمته إلى SQL Server دون فتح اتصال تشغيل.
    public static IQueryable<AnnualMaintenanceCount> Query(Maintenance_DabaseContext db, AnnualStatisticsFilter? filter = null)
    {
        // نفس أولوية مركز الإشعارات: تاريخ التعديل الأحدث، ثم المعرّف الأعلى عند تساوي التاريخ.
        // استخدام NOT EXISTS مع المفتاح الكامل يمنع تكرار التكليف ولا يحتاج OUTER APPLY غير المدعوم في SQLite.
        var latestReports = db.T_FinishedMaintenances.AsNoTracking().Where(report =>
            !db.T_FinishedMaintenances.Any(newer => newer.Location == report.Location
                && newer.Location_ID == report.Location_ID && newer.Branch_ID == report.Branch_ID
                && newer.MaintenanceCosts_No == report.MaintenanceCosts_No
                && (newer.Date_Modify > report.Date_Modify || newer.Date_Modify == report.Date_Modify && newer.ID > report.ID)));
        var orders = db.T_Emergency_MaintenanceCosts.AsNoTracking();
        // خيار المبنى في نموذج الإحصائية لا يغير مصدر الحالة أو أولوية تقرير الإنهاء.
        if (filter?.BuildingId is short buildingId) orders = orders.Where(order => order.Build_ID == buildingId);
        return (
            from order in orders
            join report in latestReports
                on new { order.Location, order.Location_ID, order.Branch_ID, Number = order.Emergency_MaintenanceCosts_No }
                equals new { report.Location, report.Location_ID, report.Branch_ID, Number = report.MaintenanceCosts_No } into reports
            from report in reports.DefaultIfEmpty()
            group order by new
            {
                Year = order.Date_com.Year,
                Month = order.Date_com.Month,
                // DefaultIfEmpty قد يعيد تقريرًا فارغًا من LEFT JOIN؛ فحص الكيان نفسه أدق من مقارنة مفتاح غير قابل للخلو.
                HasReport = report != null,
                ReportState = (byte?)report.State,
                FinishedFlag = order.Emergency_MaintenanceCosts_ST
            } into grouped
            select new AnnualMaintenanceCount(grouped.Key.Year, grouped.Key.HasReport, grouped.Key.ReportState,
                grouped.Key.FinishedFlag, grouped.LongCount(), grouped.Key.Month)
        );
    }
    #endregion
}

public sealed class AnnualStatisticsViewModel : INotifyPropertyChanged
{
    #region السنة وحالة القراءة والأعداد المعروضة
    private IReadOnlyList<AnnualMaintenanceCount> counts = [];
    private int? selectedYearNumber = DateTime.Today.Year;
    private int? rangeFromYear;
    private int? rangeToYear;
    private int selectedLastMonth = 12;
    private bool useYearRange;
    private bool allowed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool HasData { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsError { get; private set; }
    public bool CanRefresh => allowed && !IsBusy;
    public bool CanSelectYear => allowed;
    public string Status { get; private set; } = "تظهر إحصاءات الصيانة بعد تسجيل الدخول والاتصال بالقاعدة.";
    public string UpdatedAt { get; private set; } = string.Empty;
    public int? SelectedYearNumber
    {
        get => selectedYearNumber;
        set { if (selectedYearNumber == value) return; selectedYearNumber = value; NotifyAll(); }
    }
    // سنة الإحصائية هي سنة التكليف؛ تغير الحالة أو تاريخ التقرير لا ينقل التكليف إلى سنة أخرى.
    private IEnumerable<AnnualMaintenanceCount> Selection => useYearRange
        ? counts.Where(row => (!rangeFromYear.HasValue || row.Year >= rangeFromYear) && (!rangeToYear.HasValue || row.Year <= rangeToYear))
        : counts.Where(row => (selectedYearNumber is null || row.Year == selectedYearNumber)
            && (row.Month == 0 || row.Month <= selectedLastMonth));
    public long CompletedCount => Selection.Where(row => row.HasReport && row.ReportState == 0).Sum(row => row.Count);
    public long NotCompletedCount => Selection.Where(row => row.HasReport && row.ReportState == 1).Sum(row => row.Count);
    public long SuspendedCount => Selection.Where(row => row.HasReport && row.ReportState == 2).Sum(row => row.Count);
    public long InProgressCount => Selection.Where(row => row.HasReport && row.ReportState == 3).Sum(row => row.Count);
    // يطابق «يحتاج تقرير إنهاء» في المركز؛ علم منتهٍ بلا تقرير حالة تحتاج مراجعة وليست إنهاءً مؤكدًا.
    public long NeedsReportCount => Selection.Where(row => !row.HasReport && row.FinishedFlag != true).Sum(row => row.Count);
    public long TotalCount => Selection.Sum(row => row.Count);
    public long UnclassifiedCount => TotalCount - CompletedCount - NotCompletedCount - SuspendedCount - InProgressCount - NeedsReportCount;
    private string Display(long value) => HasData ? value.ToString("N0") : "—";
    public string CompletedText => Display(CompletedCount);
    public string NotCompletedText => Display(NotCompletedCount);
    public string SuspendedText => Display(SuspendedCount);
    public string InProgressText => Display(InProgressCount);
    public string NeedsReportText => Display(NeedsReportCount);
    public string TotalText => HasData ? "إجمالي التكليفات: " + TotalCount.ToString("N0") : string.Empty;
    public string Period => selectedYearNumber is int year
        ? (selectedLastMonth == 12 ? "تكليفات سنة " + year : $"تكليفات من 1 إلى {selectedLastMonth} لسنة {year}")
        : "تكليفات كل السنوات";
    public string SelectionStatus => IsError ? Status : HasData && UnclassifiedCount > 0
        ? $"هناك {UnclassifiedCount:N0} تكليفات تحتاج مراجعة حالة الإنهاء أو تقريرها، ولا تُضاف إلى الحالات الخمس." 
        : HasData && TotalCount == 0 ? "لا توجد تكليفات صيانة للفترة المختارة." : Status;
    public string AvailableYearsHint => "اختر سنة التكليف، أو امسح السنة لعرض كل السنوات. السنوات المسجلة: "
        + string.Join("، ", counts.Select(row => row.Year).Distinct().OrderDescending());
    #endregion

    #region تحميل الإحصاءات والحفاظ على السنة واللقطة السابقة
    public void ConfigureAccess(bool canView)
    {
        if (allowed == canView) return;
        allowed = canView;
        if (!canView) { counts = []; HasData = false; UpdatedAt = string.Empty; IsBusy = false; IsError = false; }
        Status = canView ? "جارٍ تجهيز إحصاءات الصيانة." : "سجّل الدخول لعرض إحصاءات الصيانة.";
        NotifyAll();
    }
    public void BeginLoad()
    {
        IsBusy = true;
        // إبقاء لقطة الأعداد السابقة ظاهرة عند التحديث الدوري أو تعذر اتصال سابق.
        if (!HasData) Status = "جارٍ تحميل إحصاءات الصيانة…";
        NotifyAll();
    }
    public void Apply(AnnualStatisticsResult result)
    {
        counts = result.Counts;
        HasData = true; IsBusy = false; IsError = false;
        UpdatedAt = "آخر تحديث " + DateTime.Now.ToString("HH:mm:ss");
        Status = "حسب آخر تقرير لكل تكليف. امسح السنة لعرض جميع السنوات.";
        NotifyAll();
    }
    // يدعم نموذج الإحصائية القديم عند اختيار فترة من سنة إلى سنة، دون إعادة تحميل البيانات مرتين.
    public void SetYearRange(int? from, int? to)
    {
        rangeFromYear = from; rangeToYear = to; useYearRange = from.HasValue || to.HasValue;
        NotifyAll();
    }
    public void UseSingleYear(int? year, int lastMonth = 12)
    {
        // اختيار فترة السنة يحد النتائج بحسب الأشهر، بينما الفترة بين السنوات تبقى سنوية كاملة.
        useYearRange = false; selectedYearNumber = year; selectedLastMonth = Math.Clamp(lastMonth, 1, 12); NotifyAll();
    }
    public void Fail(string message)
    {
        IsBusy = false; IsError = true;
        Status = HasData ? message + " الأعداد المعروضة من آخر قراءة ناجحة." : message;
        NotifyAll();
    }
    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    #endregion
}
