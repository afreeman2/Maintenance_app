using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.Notifications;
using Maintenance_app.orders_Windows;
using Maintenance.Shared;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Maintenance_app.Requests;

public sealed class MaintenanceRequestInboxStore(Func<Maintenance_DabaseContext> createContext)
{
    #region قراءة جميع الطلبات دون تقييدها بحساب مدير واحد
    public static async Task<List<MaintenanceNotice>> LoadAsync(Maintenance_DabaseContext db, CancellationToken token)
    {
        var requests = await db.T_Notif_Action_Users.AsNoTracking()
            .Where(x => x.OP_ID == 1 || x.OP_ID == 2 || x.OP_ID == 3 || x.OP_ID == 6 || x.OP_ID == 7 || x.OP_ID == 8)
            .OrderBy(x => x.ID).ToListAsync(token);
        var numbers = requests.Where(x => x.MaintenanceCosts_No > 0).Select(x => x.MaintenanceCosts_No!.Value).Distinct().ToList();
        var orders = await db.T_Emergency_MaintenanceCosts.AsNoTracking().Where(x => numbers.Contains(x.Emergency_MaintenanceCosts_No))
            .Select(x => new { x.Location, x.Location_ID, x.Branch_ID, x.Emergency_MaintenanceCosts_No }).ToListAsync(token);
        var result = new List<MaintenanceNotice>();
        var actions = await db.T_Notif_Actions.AsNoTracking().OrderBy(x => x.rendate).ThenBy(x => x.ID).ToListAsync(token);
        foreach (var request in requests)
        {
            var matches = orders.Where(x => x.Emergency_MaintenanceCosts_No == request.MaintenanceCosts_No).ToList();
            var order = matches.Count == 1 ? matches[0] : null;
            var latest = actions.LastOrDefault(x => x.DOC_ID == request.ID && x.User_ID == request.User_ID);
            result.Add(new MaintenanceNotice
            {
                RequestId = request.ID, RequestOperation = request.OP_ID, RequestDeliveryState = request.state, Number = request.ID, Date = request.ndate,
                RequestEventOperation = latest?.OP_ID ?? request.OP_ID, RequestRevision = latest?.rendate ?? request.rendate,
                AssignedOrderNumber = request.MaintenanceCosts_No, RequestHasOrder = order is not null,
                Location = order?.Location ?? "طلبات تطبيق الصيانة", LocationId = order?.Location_ID ?? 0,
                BranchId = order?.Branch_ID ?? 0, Branch = request.Mangment_Request ?? "",
                Building = request.Build ?? "", Service = request.Serv_type ?? "", Employees = request.User_Name ?? "",
                Notes = request.Description ?? ""
            });
        }
        // إلغاء العميل يبقى بطاقة مستقلة لكل مستخدمي المنظومة حتى حذفه يدويًا من قناة الإشعارات.
        var cancellations = actions.Where(x => x.OP_ID == RequestProtocol.Cancelled);
        foreach (var action in cancellations.DistinctBy(x => x.DOC_ID))
            result.Add(new MaintenanceNotice { RequestId = action.DOC_ID, RequestOperation = RequestProtocol.Cancelled,
                RequestEventOperation = 4, RequestRevision = action.rendate, RequestDeliveryState = action.state, Number = action.DOC_ID, Date = DateOnly.FromDateTime(action.ndate),
                Building = action.Build ?? "", Service = action.Serv_type ?? "", Employees = action.User_Name ?? "",
                Branch = action.Mangment_Request ?? "", Notes = action.Description ?? "" });
        return result;
    }
    #endregion

    #region استلام المدير للطلب أو إلغائه دون إخفاء إشعارات بقية المستخدمين
    public async Task ObserveAsync(IEnumerable<MaintenanceNotice> displayed, CancellationToken token = default)
    {
        await using var db = createContext();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        foreach (var notice in displayed.Where(x => x.IsRequest).DistinctBy(x => x.RequestId))
        {
            var request = await db.T_Notif_Action_Users.SingleOrDefaultAsync(x => x.ID == notice.RequestId, token);
            if (notice.RequestOperation == RequestProtocol.New && request?.OP_ID == RequestProtocol.New)
            {
                request.state = 1;
                foreach (var action in await db.T_Notif_Actions.Where(x => x.DOC_ID == request.ID && x.User_ID == request.User_ID && x.OP_ID == 1).ToListAsync(token)) action.state = 1;
            }
            else if (notice.RequestOperation == RequestProtocol.Cancelled)
            {
                if (request is not null)
                {
                    if (request.OP_ID != RequestProtocol.Cancelled || request.MaintenanceCosts_No is > 0) continue;
                    request.state = 1;
                }
                foreach (var action in await db.T_Notif_Actions.Where(x => x.DOC_ID == notice.RequestId && x.OP_ID == RequestProtocol.Cancelled).ToListAsync(token)) action.state = 1;
                // العرض لا يحذف الإلغاء؛ تبقى البطاقة حتى أمر الحذف اليدوي، والرأس لتأكيد العميل.
            }
        }
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
    }
    public async Task ArchiveAsync(long requestId, T_SystemUser? user)
    {
        if (!MaintenanceNoticeAccess.CanView(user)) throw new UnauthorizedAccessException("سجّل الدخول لحذف إشعار الإلغاء.");
        await using var db = createContext();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = await db.T_Notif_Action_Users.SingleOrDefaultAsync(x => x.ID == requestId);
        var cancelledActions = await db.T_Notif_Actions.Where(x => x.DOC_ID == requestId && x.OP_ID == 4).ToListAsync();
        // بعض الإصدارات السابقة حذفت الرأس بعد الإلغاء؛ يبقى حذف إشعارها القديم ممكنًا.
        if (request is not null && request.OP_ID != RequestProtocol.Cancelled)
            throw new InvalidOperationException("الحذف اليدوي من المنظومة مخصص لإشعار إلغاء العميل فقط.");
        if (cancelledActions.Count == 0) return;
        // نحذف قناة إشعار المنظومة فقط، ونبقي الرأس حتى يتلقى العميل تأكيد إلغائه.
        db.T_Notif_Actions.RemoveRange(cancelledActions);
        await db.SaveChangesAsync(); await tx.CommitAsync(); MaintenanceNotificationUpdates.Publish();
    }
    #endregion
}
