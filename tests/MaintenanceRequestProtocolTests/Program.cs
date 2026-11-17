using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.Notifications;
using Maintenance_app.orders_Windows;
using Maintenance_app.Requests;
using Maintenance_request.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ClientContext = Maintenance_request.database_cotext.Cont_maintContext;

internal static class RequestProtocolTests
{
    #region أدوات تحقق الطرفين على الاتصال المعزول نفسه
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static async Task Reject(Func<Task> action)
    {
        bool rejected = false;
        try { await action(); } catch (InvalidOperationException) { rejected = true; } catch (UnauthorizedAccessException) { rejected = true; }
        Check(rejected, "Operation should be rejected.");
    }
    private static async Task State(Func<Maintenance_DabaseContext> create, long id, int operation, int state, long? number, int? eventOperation = null)
    {
        await using var db = create();
        var request = await db.T_Notif_Action_Users.SingleAsync(x => x.ID == id);
        var action = await db.T_Notif_Actions.SingleAsync(x => x.DOC_ID == id);
        Check(request.OP_ID == operation && request.state == state && request.MaintenanceCosts_No == number, "Request header state mismatch.");
        Check(action.OP_ID == (eventOperation ?? operation) && action.state == state && action.MaintenanceCosts_No == number, "Request event state mismatch.");
    }
    #endregion

    #region دورة الطلب والتكليف والإنهاء والرجوع والحذف
    private static async Task<int> Main()
    {
        try
        {
            using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
            var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
            var clientOptions = new DbContextOptionsBuilder<ClientContext>().UseSqlite(connection).Options;
            Maintenance_DabaseContext Create() => new TestContext(options);
            var client = new RequestClientStore(() => new(clientOptions));
            var inbox = new MaintenanceRequestInboxStore(Create);
            var orders = new MaintenanceOrderEfStore(Create);
            var completions = new MaintenanceCompletionEfStore(Create);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var building = new T_Build { Location = "معاينة", Location_ID = 1, Branch_ID = 1, Build_ID = 1, Build = "مبنى الاختبار" };
            var service = new T_Service { Location = "معاينة", Location_ID = 1, Branch_ID = 1, Serv_num = 1, Serv_type = "كهرباء" };
            var manage = new T_Manage { Location = "معاينة", Location_ID = 1, Branch_ID = 1, Manag_ID = 1, Manag = "إدارة" };
            var section = new T_Section { Location = "معاينة", Location_ID = 1, Branch_ID = 1, Manag_ID = 1, Secti_ID = 1, Secti = "قسم", Date_Modify = today };
            var employee = new T_Employe { Location = "معاينة", Location_ID = 1, Branch_ID = 1, Employe_ID = 1, Emp_Name = "فني" };
            await using (var db = Create())
            {
                await db.Database.EnsureCreatedAsync();
                db.AddRange(new T_Branch { Branch_ID = 1, Branch = "فرع" }, new T_Location { Location = "معاينة", Location_ID = 1, Branch_ID = 1 }, building, service, manage, section, employee);
                foreach (var pair in new[] { (77L, 501L), (88L, 502L) })
                {
                    db.T_Notif_Action_Users.Add(new() { ID = pair.Item1, User_ID = pair.Item2, OP_ID = 1, state = 0,
                        Build_ID = 1, Build = building.Build, Serv_num = 1, Serv_type = service.Serv_type, Description = "طلب اختبار", User_Name = "عميل", ndate = today, rendate = DateTime.Now });
                    db.T_Notif_Actions.Add(new() { ID = pair.Item1 + 1, DOC_ID = pair.Item1, DOC_Number = pair.Item1.ToString(), User_ID = pair.Item2,
                        OP_ID = 1, state = 0, Branch_ID = 1, Build_ID = 1, Build = building.Build, Serv_num = 1, Serv_type = service.Serv_type, Description = "طلب اختبار", User_Name = "عميل", ndate = DateTime.Today, rendate = DateTime.Now });
                }
                await db.SaveChangesAsync();
            }
            var managerA = new T_SystemUser { User_ID = 1, formangreq = true };
            var managerB = new T_SystemUser { User_ID = 2 };
            var notices = new MaintenanceNotificationStore(Create);
            var a = await notices.LoadAsync(managerA); var b = await notices.LoadAsync(managerB);
            Check(a.Count(x => x.IsRequest) == 2 && b.Count(x => x.IsRequest) == 2, "Requests were filtered to the current manager.");
            await inbox.ObserveAsync(a);
            await State(Create, 77, 1, 1, null);
            Check((await notices.LoadAsync(managerB)).Count(x => x.IsRequest) == 2, "One manager receiving a request hid it from another.");
            await client.UpdateAsync(77, 501, "نص معدل", 1, "كهرباء"); await State(Create, 77, 1, 1, null);
            await Reject(() => client.CancelAsync(77, 502));
            Check((await client.LoadAsync(501)).All(x => x.User_ID == 501), "Client read another employee's requests.");
            var draft = new MaintenanceOrderDraft(building, today, employee, 2, service, [employee], [new(manage, section, "صيانة", "فحص الكهرباء", "كهرباء")], [], 7, 77);
            var key = await orders.InsertAsync(draft); await State(Create, 77, 2, 0, key.Number);
            Check(await client.ReceiveAsync(77, 501, 2), "Executing response not received."); await State(Create, 77, 2, 1, key.Number);
            Check(!await client.ReceiveAsync(77, 501, 2), "Executing response received twice.");
            await Reject(() => client.CancelAsync(77, 501)); await Reject(() => client.UpdateAsync(77, 501, "غير مسموح", 1, "كهرباء"));
            var pending = new CompletionDraft(today, 2, "موظف", "بانتظار مواد");
            await completions.SaveAsync(key, pending, null);
            long reportId = (await completions.LoadAsync(key)).Report!.ID;
            await State(Create, 77, 2, 1, key.Number);
            await completions.SaveAsync(key, pending with { Status = 3 }, reportId); await State(Create, 77, 2, 1, key.Number);
            await completions.DeleteAsync(key, reportId); await State(Create, 77, 2, 1, key.Number);
            await completions.SaveAsync(key, pending with { Status = 0 }, null);
            reportId = (await completions.LoadAsync(key)).Report!.ID; await State(Create, 77, 3, 0, key.Number);
            Check(!await client.ReceiveAsync(77, 501, 2), "Old receipt overwrote a newer final response.");
            await Reject(() => inbox.ArchiveAsync(77, managerA));
            Check(await client.ReceiveAsync(77, 501, 3), "Final response not received."); await State(Create, 77, 6, 1, key.Number, 3);
            Check((await notices.LoadAsync(managerB)).Single(x => x.RequestId == 77).EventOperation == 3, "Client receipt hid original completed event from another manager.");
            await Reject(() => inbox.ArchiveAsync(77, managerB));
            await completions.SaveAsync(key, pending with { Status = 1 }, reportId); await State(Create, 77, 6, 1, key.Number, 3);
            await completions.SaveAsync(key, pending, reportId); await State(Create, 77, 8, 0, key.Number);
            Check(await client.ReceiveAsync(77, 501, 8), "Completion cancellation not received."); await State(Create, 77, 2, 1, key.Number, 8);
            var reverted = (await notices.LoadAsync(managerB)).Single(x => x.RequestId == 77);
            Check(reverted.RequestOperation == 2 && reverted.EventOperation == 8 && reverted.ToastText.Contains("إلغاء إنهاء"), "Original cancellation was lost after employee receipt.");
            await completions.SaveAsync(key, pending with { Status = 0 }, reportId); await State(Create, 77, 3, 0, key.Number);
            await completions.DeleteAsync(key, reportId); await State(Create, 77, 8, 0, key.Number);
            await client.ReceiveAsync(77, 501, 8);
            await orders.DeleteAsync(key); await State(Create, 77, 7, 0, -1);
            // يمكن إعادة إصدار التكليف قبل فتح العميل للتنبيه السابق، ويبقى حدث الإلغاء قائمًا للبالون.
            key = await orders.InsertAsync(draft); await client.ReceiveAsync(77, 501, 2);
            await using (var db = Create())
            {
                var request = await db.T_Notif_Action_Users.SingleAsync(x => x.ID == 77);
                var actions = await db.T_Notif_Actions.Where(x => x.DOC_ID == 77).OrderBy(x => x.rendate).ThenBy(x => x.ID).ToListAsync();
                Check(request.OP_ID == 2 && request.MaintenanceCosts_No == key.Number, "Request was not available for immediate reassignment.");
                Check(actions.Any(x => x.OP_ID == 7 && x.state == 0) && actions.Last().OP_ID == 2,
                    "Execution cancellation was lost when a new order was issued.");
            }
            var replies = await client.LoadRepliesAsync(501);
            Check(replies.PendingCancellations.Count == 1 && replies.PendingCancellations[0].Request.OP_ID == 7,
                "Client did not retain the cancellation as a toast-only event.");
            await client.ReceivePreviousCancellationAsync(replies.PendingCancellations[0].ActionId, 501);
            await completions.SaveAsync(key, pending with { Status = 1 }, null); await client.ReceiveAsync(77, 501, 3);
            await Reject(() => inbox.ArchiveAsync(77, managerA));
            Check((await client.CompletedNoticeAsync(77, 501)).OP_ID == 6, "Completed request unavailable for client manual dismissal.");
            await Reject(() => client.CompletedNoticeAsync(77, 502));
            await using (var db = Create())
            {
                Check(await db.T_Notif_Action_Users.AnyAsync(x => x.ID == 77) && await db.T_Notif_Actions.AnyAsync(x => x.DOC_ID == 77), "Notification lifecycle deleted the request needed by rollback.");
                Check(await db.T_Emergency_MaintenanceCosts.AnyAsync() && await db.T_FinishedMaintenances.AnyAsync(), "Archiving deleted work documents.");
            }
            await client.CancelAsync(88, 502); await State(Create, 88, 4, 0, null);
            var cancellation = (await notices.LoadAsync(managerA)).Single(x => x.RequestId == 88);
            Check(!cancellation.IsTransient && cancellation.CanArchive, "Cancellation does not remain for manager manual removal.");
            await inbox.ObserveAsync([cancellation]);
            await using (var db = Create()) Check(await db.T_Notif_Action_Users.AnyAsync(x => x.ID == 88 && x.OP_ID == 4), "Showing cancellation deleted its source before client receipt.");
            Check((await notices.LoadAsync(managerB)).Any(x => x.RequestId == 88 && x.CanArchive), "First manager consumed the cancellation card for everyone.");
            await inbox.ArchiveAsync(88, managerB);
            Check(!(await notices.LoadAsync(managerA)).Any(x => x.RequestId == 88), "Manual cancellation removal left manager notification.");
            Check((await client.LoadAsync(502)).Any(x => x.ID == 88 && x.OP_ID == 4), "Manager removal prevented client cancellation toast.");
            // دعم إلغاء قديم حذف الإصدار السابق رأسه وترك قناة النقل.
            await using (var db = Create())
            {
                db.T_Notif_Actions.Add(new() { ID = 1100, DOC_ID = 999, DOC_Number = "999", User_ID = 503, OP_ID = 4, state = 1, Description = "إلغاء قديم", Build = "مبنى الاختبار", User_Name = "عميل", Serv_type = "كهرباء", ndate = DateTime.Today, rendate = DateTime.Now });
                await db.SaveChangesAsync();
            }
            await inbox.ArchiveAsync(999, managerB);
            await using (var db = Create()) Check(!await db.T_Notif_Actions.AnyAsync(x => x.DOC_ID == 999), "Legacy cancellation without header cannot be removed.");
            Console.WriteLine($"PASS: {checks} cross-application SQLite checks; both EF stores, all managers, employee ownership, states 1/2/3/4/6/7/8, 8->2 receipt, report statuses, archive and cancellation delivery. No production database.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion
}
