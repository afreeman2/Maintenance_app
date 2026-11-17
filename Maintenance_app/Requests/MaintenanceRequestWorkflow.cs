using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using Maintenance.Shared;
using Microsoft.EntityFrameworkCore;

namespace Maintenance_app.Requests;

// تحديث طرفي بروتوكول الطلب داخل معاملة التكليف نفسها، دون تغيير مخطط قاعدة البيانات.
public static class MaintenanceRequestWorkflow
{
    #region إصدار التكليف وإلغاء التنفيذ وتغيير الإنهاء
    public static async Task LinkAsync(Maintenance_DabaseContext db, long requestId, MaintenanceOrderKey key)
    {
        var request = await db.T_Notif_Action_Users.SingleAsync(x => x.ID == requestId);
        if (!RequestProtocol.CanAssign(request.OP_ID, request.MaintenanceCosts_No))
            throw new InvalidOperationException("الطلب لم يعد متاحًا لإصدار تكليف؛ أعد تحديث الإشعارات.");
        await EnsureUniqueOrderAsync(db, key);
        var order = await MaintenanceOrderEfStore.ByKey(db.T_Emergency_MaintenanceCosts, key).SingleAsync();
        if (request.Build_ID is short building && order.Build_ID != building
            || request.Serv_num is short service && order.Serv_num != service)
            throw new InvalidOperationException("المبنى والخدمة في التكليف يجب أن يطابقا طلب العميل.");
        request.MaintenanceCosts_No = key.Number;
        await SetOperationAsync(db, request, RequestProtocol.Executing, false);
    }

    public static async Task CancelExecutionAsync(Maintenance_DabaseContext db, MaintenanceOrderKey key)
    {
        var requests = await LinkedAsync(db, key);
        foreach (var request in requests)
        {
            request.MaintenanceCosts_No = -1;
            await SetOperationAsync(db, request, RequestProtocol.ExecutionCancelled, false);
        }
    }

    // التقرير المعلق أو الجاري لا يلغي التنفيذ؛ الرجوع من نهاية فعلية يولد العملية 8 فقط.
    public static async Task ChangeCompletionAsync(Maintenance_DabaseContext db, MaintenanceOrderKey key, byte? oldStatus, byte? newStatus)
    {
        foreach (var request in await LinkedAsync(db, key))
        {
            bool wasFinal = RequestProtocol.IsFinalReport(oldStatus)
                || request.OP_ID is RequestProtocol.Completed or RequestProtocol.Closed;
            if (RequestProtocol.IsFinalReport(newStatus))
            {
                if (request.OP_ID is not (RequestProtocol.Completed or RequestProtocol.Closed))
                    await SetOperationAsync(db, request, RequestProtocol.Completed, true);
            }
            else if (wasFinal)
                await SetOperationAsync(db, request, RequestProtocol.CompletionCancelled, false);
            // بقاء الطلب جاريًا لا يعيد إرسال العملية 2 عند كل تعديل للتقرير المعلق.
        }
    }

    private static async Task<List<T_Notif_Action_User>> LinkedAsync(Maintenance_DabaseContext db, MaintenanceOrderKey key)
    {
        var rows = await db.T_Notif_Action_Users.Where(x => x.MaintenanceCosts_No == key.Number).ToListAsync();
        if (rows.Count > 0) await EnsureUniqueOrderAsync(db, key);
        return rows;
    }
    private static async Task EnsureUniqueOrderAsync(Maintenance_DabaseContext db, MaintenanceOrderKey key)
    {
        // جداول الطلبات القديمة لا تحمل الموقع؛ نرفض الربط الملتبس بدل تعديل طلب فرع آخر.
        if (await db.T_Emergency_MaintenanceCosts.CountAsync(x => x.Emergency_MaintenanceCosts_No == key.Number) > 1)
            throw new InvalidOperationException("رقم التكليف مكرر بين المواقع؛ لا يمكن تحديد الطلب المرتبط بأمان.");
    }
    private static async Task SetOperationAsync(Maintenance_DabaseContext db, T_Notif_Action_User request, int operation, bool final)
    {
        var action = await db.T_Notif_Actions.Where(x => x.DOC_ID == request.ID && x.User_ID == request.User_ID)
            .OrderByDescending(x => x.rendate).ThenByDescending(x => x.ID).FirstOrDefaultAsync();
        // إذا أُعيد التكليف قبل استلام الإلغاء، نبقي حدث الإلغاء ونكتب الرد الجديد في سجل مستقل.
        if (operation == RequestProtocol.Executing && request.OP_ID == RequestProtocol.ExecutionCancelled && request.state == 0)
        {
            action = new T_Notif_Action
            {
                DOC_ID = request.ID, DOC_Number = action?.DOC_Number ?? request.ID.ToString(),
                DOC_Type = action?.DOC_Type ?? 0, User_ID = request.User_ID, ndate = request.ndate.ToDateTime(TimeOnly.MinValue),
                Description = request.Description, Build_ID = request.Build_ID, Build = request.Build, Serv_num = request.Serv_num,
                Serv_type = request.Serv_type, Mangment_Request = request.Mangment_Request, User_Name = request.User_Name
            };
            db.T_Notif_Actions.Add(action);
        }
        request.OP_ID = operation; request.state = 0; request.MaintenanceCosts_State = final;
        // وقت العملية يميز دورة إنجاز جديدة عن إنجاز سابق أخفاه العميل من قائمته.
        // هامش يحفظ ترتيب الردود حتى مع تقريب الوقت في عمود datetime.
        var now = DateTime.Now;
        var nextRevision = request.rendate.AddMilliseconds(10);
        request.rendate = now > nextRevision ? now : nextRevision;
        if (action is not null)
        {
            action.OP_ID = operation; action.state = 0;
            action.rendate = request.rendate;
            action.MaintenanceCosts_No = request.MaintenanceCosts_No; action.MaintenanceCosts_State = final;
            action.Branch_ID = operation == RequestProtocol.Executing ? (short)2 : operation == RequestProtocol.Completed ? (short)3 : (short)5;
        }
    }
    #endregion
}
