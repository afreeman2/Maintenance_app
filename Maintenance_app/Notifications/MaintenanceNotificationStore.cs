using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using Microsoft.EntityFrameworkCore;
using Maintenance.Shared;
using Maintenance_app.Requests;

namespace Maintenance_app.Notifications;

#region بيانات الإشعار وصلاحية الاطلاع

// يحمل الإشعار المفتاح المركب كاملًا؛ الرقم وحده لا يميز التكليف بين المواقع والفروع.
public sealed class MaintenanceNotice : System.ComponentModel.INotifyPropertyChanged
{
    // أسماء ثابتة تربط عنوان المجموعة بما يحفظ في إشعار التقرير، دون تغيير ترميز الحالات.
    public const string SuspendedGroup = "إنهاء صيانة معلّق";
    public const string InProgressGroup = "إنهاء صيانة قيد الإنجاز";
    public string Location { get; set; } = string.Empty;
    public short LocationId { get; set; }
    public short BranchId { get; set; }
    public long Number { get; set; }
    public DateOnly Date { get; set; }
    public string Building { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Employees { get; set; } = string.Empty;
    public byte? ReportState { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool NeedsReport { get; set; }
    // الطلبات عامة لكل حسابات المنظومة، بينما المستخدم المرسل يبقى محفوظًا في بيانات المصدر.
    public long? RequestId { get; set; }
    public int RequestOperation { get; set; }
    public int RequestDeliveryState { get; set; }
    public int RequestEventOperation { get; set; }
    public DateTime RequestRevision { get; set; }
    public int EventOperation => RequestEventOperation == 0 ? RequestOperation : RequestEventOperation;
    public long? AssignedOrderNumber { get; set; }
    public bool RequestHasOrder { get; set; }
    public bool IsRequest => RequestId.HasValue;
    public bool IsTransient { get; set; }
    public bool CanArchive => IsRequest && RequestOperation == RequestProtocol.Cancelled;
    // البطاقة ومسار الفتح يستخدمان شرط إصدار التكليف نفسه قبل استلام العميل وبعده.
    public bool CanAssignRequest => IsRequest && RequestProtocol.CanAssign(RequestOperation, AssignedOrderNumber);
    public bool CanOpen => !IsRequest || CanAssignRequest || RequestHasOrder;
    public string OpenLabel => !IsRequest ? "تقرير الإنهاء" : CanAssignRequest ? "إصدار تكليف" : "عرض التكليف والإنهاء";
    private bool isUnread;
    public bool IsUnread
    {
        get => isUnread;
        set
        {
            isUnread = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsUnread)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ReadLabel)));
        }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public MaintenanceOrderKey Key => new MaintenanceOrderKey(Location, LocationId, BranchId, AssignedOrderNumber ?? Number);
    // تغيير مرحلة الإنهاء يولد إشعارًا جديدًا، مع منع التكرار عند التحديث العادي.
    public string Identity => IsRequest ? System.Text.Json.JsonSerializer.Serialize(new object?[] { "request", RequestId, RequestProtocol.StableStage(RequestOperation), RequestRevision })
        : System.Text.Json.JsonSerializer.Serialize(new object?[] { Location, LocationId, BranchId, Number, NeedsReport ? -1 : ReportState });
    public string Title => IsRequest ? RequestNotificationText.Status(RequestProtocol.StableStage(RequestOperation))
        : NeedsReport ? "تكليف يحتاج تقرير إنهاء" : GroupName;
    // إيصال الإلغاء مستقل عن المرحلة المستقرة؛ وصول العملية7 أو8 يجب ألا يضيع بسبب مشاهدة1 أو2 سابقًا.
    public string DeliveryIdentity => IsRequest ? "request-event:" + RequestId + ":" + (EventOperation == 6 ? 3 : EventOperation) + ":" + RequestRevision.ToString("O") : Identity;
    public string ToastTitle => IsRequest ? RequestNotificationText.Status(EventOperation) : Title;
    public string ToastText => IsRequest ? RequestNotificationText.Manager(EventOperation, Employees, Branch, Service) : Summary;
    public string ToastAccentColor => IsRequest ? RequestNotificationAppearance.For(EventOperation).AccentColor : AccentColor;
    public string ToastTintColor => IsRequest ? RequestNotificationAppearance.For(EventOperation).TintColor : TintColor;
    public string GroupName => IsRequest ? RequestOperation == 4 ? "طلبات ألغيت من العميل" : RequestProtocol.StableStage(RequestOperation) == 1 ? "طلبات صيانة واردة"
        : RequestProtocol.StableStage(RequestOperation) == 2 ? "طلبات جاري تنفيذها" : "طلبات تم تنفيذها"
        : NeedsReport ? "تكليفات تحتاج تقرير إنهاء" : ReportState == 2 ? SuspendedGroup : InProgressGroup;
    public int GroupOrder => IsRequest ? 3 + RequestProtocol.StableStage(RequestOperation) : NeedsReport ? 0 : ReportState == 2 ? 1 : 2;
    // اللون الذهبي للمعلّق والفيروزي لقيد الإنجاز، والأزرق للتكليف الذي لم يسجل له تقرير.
    public string AccentColor => IsRequest ? RequestNotificationAppearance.For(RequestProtocol.StableStage(RequestOperation)).AccentColor : NeedsReport ? "#3559A0" : ReportState == 2 ? "#96620D" : "#087F8C";
    public string TintColor => IsRequest ? RequestNotificationAppearance.For(RequestProtocol.StableStage(RequestOperation)).TintColor : NeedsReport ? "#EDF2FA" : ReportState == 2 ? "#FFF3DC" : "#E2F4F5";
    public int DaysWaiting => Math.Max(0, DateOnly.FromDateTime(DateTime.Today).DayNumber - Date.DayNumber);
    public string WaitingLabel => DaysWaiting == 0 ? "اليوم" : "منذ " + DaysWaiting + " يومًا";
    public string DateLabel => Date.ToString("dd/MM/yyyy");
    public string ReadLabel => IsUnread ? "جديد" : "تمت مشاهدته";
    public string Summary => IsRequest ? RequestNotificationText.Manager(RequestProtocol.StableStage(RequestOperation), Employees, Branch, Service) : "تكليف رقم " + Number + " • " + Building + " • " + Service;
    public OrderSearchRow ToSearchRow() => new OrderSearchRow(Number, Date.ToDateTime(TimeOnly.MinValue), Building, Employees, Service, Location, LocationId, BranchId);
}

public static class MaintenanceNoticeAccess
{
    public static bool CanView(T_SystemUser? user)
    {
        // الإشعارات عامة لجميع الحسابات المسجلة؛ صلاحيات تنفيذ الإنهاء تبقى داخل شاشته.
        return user != null && user.User_ID > 0;
    }
}

#endregion

// قراءة حالة العمل الحقيقية؛ لا نعتمد نصوص جداول التنبيه القديمة التي قد تتأخر أو تتكرر.
public sealed class MaintenanceNotificationStore
{
    #region إنشاء سياق القراءة وتحميل الإشعارات

    private readonly Func<Maintenance_DabaseContext> createContext;

    public MaintenanceNotificationStore(Func<Maintenance_DabaseContext> createContext)
    {
        this.createContext = createContext;
    }

    public async Task<List<MaintenanceNotice>> LoadAsync(T_SystemUser? user, CancellationToken cancellationToken = default)
    {
        if (!MaintenanceNoticeAccess.CanView(user))
        {
            throw new UnauthorizedAccessException("لا توجد صلاحية للاطلاع على إشعارات الصيانة.");
        }

        await using Maintenance_DabaseContext db = createContext();
        // في المصدر الأصلي: 0 منجزة، 1 غير منجزة نهائيًا، 2 معلقة، 3 قيد الإنجاز.
        // تقرير الإنهاء النهائي يستبعد التكليف حتى لو تأخر تحديث علم الرأس، والعكس صحيح.
        var query = db.T_Emergency_MaintenanceCosts.AsNoTracking().Where(order =>
            (!order.T_FinishedMaintenances.Any() && order.Emergency_MaintenanceCosts_ST != true)
            || order.T_FinishedMaintenances.OrderByDescending(report => report.Date_Modify).ThenByDescending(report => report.ID)
                .Select(report => report.State).FirstOrDefault() == 2
            || order.T_FinishedMaintenances.OrderByDescending(report => report.Date_Modify).ThenByDescending(report => report.ID)
                .Select(report => report.State).FirstOrDefault() == 3);
        var rows = await query.OrderBy(order => order.Date_com).ThenBy(order => order.Emergency_MaintenanceCosts_No)
            .Select(order => new
            {
                order.Location, order.Location_ID, order.Branch_ID, order.Emergency_MaintenanceCosts_No, order.Date_com,
                Building = order.T_Build.Build, Service = order.T_Service.Serv_type, Branch = order.Branch.Branch,
                Employees = order.T_Emp_Maints.Select(link => link.T_Employe.Emp_Name).ToList(),
                HasReport = order.T_FinishedMaintenances.Any(),
                ReportState = order.T_FinishedMaintenances
                    .OrderByDescending(report => report.Date_Modify).ThenByDescending(report => report.ID).Select(report => report.State).FirstOrDefault(),
                Notes = order.T_FinishedMaintenances
                    .OrderByDescending(report => report.Date_Modify).ThenByDescending(report => report.ID).Select(report => report.notes).FirstOrDefault()
            }).ToListAsync(cancellationToken);
        List<MaintenanceNotice> notices = new List<MaintenanceNotice>();
        foreach (var row in rows)
        {
            notices.Add(new MaintenanceNotice
            {
                Location = row.Location, LocationId = row.Location_ID, BranchId = row.Branch_ID,
                Number = row.Emergency_MaintenanceCosts_No, Date = row.Date_com,
                Building = row.Building ?? string.Empty, Service = row.Service ?? string.Empty, Branch = row.Branch ?? string.Empty,
                Employees = string.Join("، ", row.Employees), NeedsReport = !row.HasReport,
                ReportState = row.ReportState, Notes = row.Notes ?? string.Empty
            });
        }
        notices.AddRange(await MaintenanceRequestInboxStore.LoadAsync(db, cancellationToken));
        return notices;
    }

    #endregion
}
