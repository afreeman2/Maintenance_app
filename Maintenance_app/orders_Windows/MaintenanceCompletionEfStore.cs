using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.Notifications;
using Microsoft.EntityFrameworkCore;
using Maintenance_app.Requests;
using System.Data;

namespace Maintenance_app.orders_Windows;

// نقل عمليات Done_Mainte إلى EF Core، مع اعتماد الرأس والبنود والإشعارات ضمن معاملة واحدة.
public sealed class MaintenanceCompletionEfStore(Func<Maintenance_DabaseContext> createContext)
{
    #region قراءة التكليف والبحث بالمفتاح المركب
    public async Task<CompletionDocument> LoadAsync(MaintenanceOrderKey key)
    {
        var order = await new MaintenanceOrderEfStore(createContext).LoadAsync(key, true);
        await using var db = createContext();
        // الفاحص محفوظ في رأس التكليف ويظهر اسمه في نموذج الإنهاء المطبوع.
        order.T_Employe = await db.T_Employes.AsNoTracking().SingleOrDefaultAsync(x => x.Location == key.Location
            && x.Location_ID == key.LocationId && x.Branch_ID == key.BranchId && x.Employe_ID == order.Employe_ID);
        var reports = await Reports(db, key).AsNoTracking().ToListAsync();
        if (reports.Count > 1) throw new InvalidOperationException("يوجد أكثر من تقرير للتكليف؛ راجع البيانات قبل المتابعة.");
        return new(order, reports.SingleOrDefault());
    }

    // جميع شروط الفني والخدمة تستخدم نطاق الموقع والفرع، حتى لا تختلط الأرقام المتكررة.
    public async Task<List<CompletionSearchRow>> SearchAsync(CompletionSearchFilter filter)
    {
        await using var db = createContext();
        var query = db.T_Emergency_MaintenanceCosts.AsNoTracking();
        if (filter.Number is long number) query = query.Where(x => x.Emergency_MaintenanceCosts_No == number);
        if (filter.From is DateOnly from) query = query.Where(x => x.Date_com >= from);
        if (filter.To is DateOnly to) query = query.Where(x => x.Date_com <= to);
        if (filter.Employee is T_Employe employee)
            query = query.Where(x => x.Location == employee.Location && x.Location_ID == employee.Location_ID
                && x.Branch_ID == employee.Branch_ID && x.T_Emp_Maints.Any(t => t.Employe_ID == employee.Employe_ID));
        if (filter.Service is T_Service service)
            query = query.Where(x => x.Location == service.Location && x.Location_ID == service.Location_ID
                && x.Branch_ID == service.Branch_ID && x.Serv_num == service.Serv_num);
        if (filter.Status == 4) query = query.Where(x => !x.T_FinishedMaintenances.Any());
        else if (filter.Status is int status) query = query.Where(x => x.T_FinishedMaintenances.Any(r => r.State == status));
        var rows = await query.Include(x => x.T_Build).Include(x => x.T_Service).Include(x => x.Branch)
            .Include(x => x.T_Emp_Maints).ThenInclude(x => x.T_Employe).Include(x => x.T_FinishedMaintenances)
            .AsSplitQuery().OrderByDescending(x => x.Date_com).ThenByDescending(x => x.Emergency_MaintenanceCosts_No).ToListAsync();
        var statuses = new MaintenanceCompletionState().CompletionStatuses;
        return rows.Select(x => new CompletionSearchRow(
            new(x.Location, x.Location_ID, x.Branch_ID, x.Emergency_MaintenanceCosts_No), x.Date_com.ToString("dd/MM/yyyy"),
            x.T_Build?.Build ?? "", string.Join(" - ", x.T_Emp_Maints.OrderBy(t => t.id).Select(t => t.T_Employe?.Emp_Name)),
            x.T_Service?.Serv_type ?? "", x.T_FinishedMaintenances.FirstOrDefault()?.State is byte s && s < statuses.Length
                ? statuses[s] : "بدون تقرير إنهاء", x.Location + " / " + (x.Branch?.Branch ?? x.Branch_ID.ToString()))).ToList();
    }

    private static IQueryable<T_FinishedMaintenance> Reports(Maintenance_DabaseContext db, MaintenanceOrderKey key) =>
        db.T_FinishedMaintenances.Where(x => x.Location == key.Location && x.Location_ID == key.LocationId
            && x.Branch_ID == key.BranchId && x.MaintenanceCosts_No == key.Number);
    #endregion

    #region فحص بيانات التقرير وحفظه وحذفه
    // نفس ضوابط الأصل: تاريخ غير مستقبلي وحالة معتمدة واسم الموظف، مع حد طول العمود الفعلي.
    public static void Validate(CompletionDraft draft)
    {
        if (draft.Date == default || draft.Date > DateOnly.FromDateTime(DateTime.Today))
            throw new InvalidOperationException("أدخل تاريخًا صحيحًا لا يتجاوز تاريخ اليوم.");
        if (draft.Status > 3) throw new InvalidOperationException("اختر حالة الصيانة.");
        if (string.IsNullOrWhiteSpace(draft.Observer) || draft.Observer.Trim().Length > 50)
            throw new InvalidOperationException("أدخل اسم الموظف المعني بالصيانة بما لا يتجاوز 50 حرفًا.");
    }

    // expectedReportId يميز الإدراج من التعديل ويرفض تقريرًا حُذف أو أُنشئ منذ تحميل الشاشة.
    public async Task SaveAsync(MaintenanceOrderKey key, CompletionDraft draft, long? expectedReportId)
    {
        Validate(draft);
        await using var db = createContext();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var order = await MaintenanceOrderEfStore.ByKey(db.T_Emergency_MaintenanceCosts, key)
            .Include(x => x.T_Emergency_MaintenanceCosts_Services).Include(x => x.T_FinishedMaintenances)
            .Include(x => x.T_Build).Include(x => x.T_Service).SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("التكليف غير موجود؛ أعد تحميل البيانات.");
        var report = order.T_FinishedMaintenances.SingleOrDefault();
        if (report?.ID != expectedReportId)
            throw new InvalidOperationException("تغير تقرير الإنهاء منذ تحميله؛ أعد عرض التكليف قبل الحفظ.");
        byte? oldStatus = report?.State;
        if (report is null)
        {
            report = new T_FinishedMaintenance { Location = key.Location, Location_ID = key.LocationId,
                Branch_ID = key.BranchId, Build_ID = order.Build_ID, MaintenanceCosts_No = key.Number };
            order.T_FinishedMaintenances.Add(report);
        }
        report.DateM = draft.Date; report.Date_Modify = DateOnly.FromDateTime(DateTime.Today);
        report.State = draft.Status; report.observer = draft.Observer.Trim(); report.notes = draft.Notes.Trim();
        // وجود تقرير يمنع تعديل التكليف حتى لو كانت حالة التقرير معلقة أو قيد الإنجاز، كما في الأصل.
        order.Emergency_MaintenanceCosts_ST = true;
        foreach (var line in order.T_Emergency_MaintenanceCosts_Services) line.Serv_status = true;
        await UpdateNotificationsAsync(db, key, order, draft);
        await MaintenanceRequestWorkflow.ChangeCompletionAsync(db, key, oldStatus, draft.Status);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        MaintenanceNotificationUpdates.Publish();
    }

    // حذف التقرير يعيد حالة التكليف وبنوده إلى غير منتهية ويعيد إشعار الحاجة إلى التقرير.
    public async Task DeleteAsync(MaintenanceOrderKey key, long reportId)
    {
        await using var db = createContext();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var report = await Reports(db, key).SingleOrDefaultAsync(x => x.ID == reportId)
            ?? throw new InvalidOperationException("التقرير غير موجود؛ أعد تحميل التكليف.");
        var order = await MaintenanceOrderEfStore.ByKey(db.T_Emergency_MaintenanceCosts, key)
            .Include(x => x.T_Emergency_MaintenanceCosts_Services).Include(x => x.T_Build).Include(x => x.T_Service).SingleAsync();
        db.T_FinishedMaintenances.Remove(report);
        order.Emergency_MaintenanceCosts_ST = false;
        foreach (var line in order.T_Emergency_MaintenanceCosts_Services) line.Serv_status = false;
        await UpdateNotificationsAsync(db, key, order, null);
        await MaintenanceRequestWorkflow.ChangeCompletionAsync(db, key, report.State, null);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        MaintenanceNotificationUpdates.Publish();
    }
    #endregion

    #region مزامنة إشعارات التكليف والطلب المرتبط
    private static async Task UpdateNotificationsAsync(Maintenance_DabaseContext db, MaintenanceOrderKey key,
        T_Emergency_MaintenanceCost order, CompletionDraft? draft)
    {
        // مخطط الإشعارات القديم يحمل رقم التكليف والفرع فقط؛ نرفض الغموض بدل تعديل موقع آخر.
        if (await db.T_Emergency_MaintenanceCosts.CountAsync(x => x.Emergency_MaintenanceCosts_No == key.Number
            && x.Branch_ID == key.BranchId) > 1)
            throw new InvalidOperationException("رقم التكليف مكرر داخل الفرع؛ لا يمكن تحديد إشعاراته بأمان.");
        var users = await db.T_ManNofiusers.Where(x => x.eno == key.Number && x.Branch_ID == key.BranchId
            && (x.estate == 1 || x.estate == 2)).ToListAsync();
        var notifications = await db.T_ManNotifs.Where(x => x.eno == key.Number && x.Branch_ID == key.BranchId
            && (x.estate == 1 || x.estate == 2)).ToListAsync();
        if (draft is { Status: < 2 })
        {
            db.T_ManNotifs.RemoveRange(notifications); db.T_ManNofiusers.RemoveRange(users);
            return;
        }
        var user = users.FirstOrDefault();
        if (user is null)
        {
            user = new T_ManNofiuser { id = (await db.T_ManNofiusers.MaxAsync(x => (long?)x.id) ?? 0) + 1,
                eno = key.Number, Branch_ID = key.BranchId };
            db.T_ManNofiusers.Add(user);
        }
        int stage = draft is null ? 1 : 2;
        DateTime date = (draft?.Date ?? order.Date_com).ToDateTime(TimeOnly.MinValue);
        var state = new MaintenanceCompletionState();
        string type = order.Type is >= 0 and < 3 ? new MaintenanceOrderState().DocumentTypes[order.Type.Value] : "";
        string description = draft is null
            ? $"تكليف صيانة يحتاج إلى تقرير إنهاء رقم (( {key.Number} )) بتاريخ (( {date:dd-MM-yyyy} )) لمبنى (( {order.T_Build?.Build} )) نوع الخدمات (( {order.T_Service?.Serv_type} )) نوع الصيانة (( {type} ))"
            : $"{(draft.Status == 2 ? MaintenanceNotice.SuspendedGroup : MaintenanceNotice.InProgressGroup)} رقم (( {key.Number} )) بتاريخ (( {date:dd-MM-yyyy} )) لمبنى (( {order.T_Build?.Build} )) نوع الخدمات (( {order.T_Service?.Serv_type} )) حالة الصيانة (( {state.CompletionStatuses[draft.Status]} ))";
        user.estate = stage; user.edate = date; user.mtype = draft?.Status ?? 0;
        // اسم المجموعة يتبدل مع حالة التقرير، مع بقاء estate=2 وmtype=2/3 كما في قاعدة المصدر.
        user.etype = order.Type ?? 0; user.dicrb = description;
        user.group_type = draft is null ? "تكليف الصيانة" : draft.Status == 2 ? MaintenanceNotice.SuspendedGroup : MaintenanceNotice.InProgressGroup;
        var notification = notifications.FirstOrDefault();
        if (notification is null)
        {
            notification = new T_ManNotif { eno = key.Number, Branch_ID = key.BranchId, not_id = user.id };
            db.T_ManNotifs.Add(notification);
        }
        notification.not_id = user.id; notification.estate = stage; notification.edate = date;
        notification.mtype = user.mtype; notification.etype = user.etype; notification.dicrb = description;
        // إصلاح تكرار الإشعار لنفس التكليف دون لمس إشعارات بقية الفروع.
        db.T_ManNotifs.RemoveRange(notifications.Skip(1)); db.T_ManNofiusers.RemoveRange(users.Skip(1));
    }

    #endregion
}
