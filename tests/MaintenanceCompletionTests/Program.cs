using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using Maintenance_app.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Data;
using System.IO;

// تهيئة هوية SQLite للمفاتيح المركبة مع نقطة فشل بعد الكتابة لاختبار التراجع الحقيقي.
internal sealed class CompletionTestContext(DbContextOptions<Maintenance_DabaseContext> options, Func<bool> fail) : Maintenance_DabaseContext(options)
{
    #region تجهيز مفاتيح الاختبار وفشل المعاملة المتعمد
    private static long identity = 100;
    public override async Task<int> SaveChangesAsync(CancellationToken token = default)
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State == EntityState.Added))
            foreach (var property in entry.Properties.Where(p => p.Metadata.IsPrimaryKey() && p.Metadata.ValueGenerated == ValueGenerated.OnAdd))
                if (property.IsTemporary || Convert.ToInt64(property.CurrentValue) == 0)
                { property.CurrentValue = Convert.ChangeType(Interlocked.Increment(ref identity), property.Metadata.ClrType); property.IsTemporary = false; }
        int count = await base.SaveChangesAsync(token);
        if (fail()) throw new InvalidOperationException("فشل اختبار بعد الكتابة وقبل اعتماد المعاملة");
        return count;
    }
    #endregion
}

internal static class Program
{
    #region أدوات التحقق
    private static int checks;
    private static void Check(bool condition, string text) { checks++; if (!condition) throw new Exception(text); }
    private static async Task Fails(Func<Task> action)
    {
        bool rejected = false;
        try { await action(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Expected rejection did not occur.");
    }
    #endregion

    #region دورة حياة التقرير على قاعدة معزولة
    private static async Task<int> Main()
    {
        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
            bool failSave = false;
            Maintenance_DabaseContext Create() => new CompletionTestContext(options, () => failSave);
            var orderStore = new MaintenanceOrderEfStore(Create); var store = new MaintenanceCompletionEfStore(Create);
            int changeSignals = 0;
            // الإشارة الفورية تُنشر بعد الاعتماد فقط؛ الفشل أو القراءة لا يصدران تنبيه حفظ.
            MaintenanceNotificationUpdates.Changed += (_, _) => changeSignals++;
            var today = DateOnly.FromDateTime(DateTime.Today);
            var building = new T_Build { Location = "معاينة", Location_ID = 1, Branch_ID = 1, Build_ID = 1, Build = "مبنى الاختبار" };
            var service = new T_Service { Location = building.Location, Location_ID = 1, Branch_ID = 1, Serv_num = 1, Serv_type = "كهرباء" };
            var manager = new T_Manage { Location = building.Location, Location_ID = 1, Branch_ID = 1, Manag_ID = 1, Manag = "إدارة الخدمات" };
            var section = new T_Section { Location = building.Location, Location_ID = 1, Branch_ID = 1, Secti_ID = 1, Manag_ID = 1, Secti = "قسم الصيانة", Date_Modify = today };
            var employee = new T_Employe { Location = building.Location, Location_ID = 1, Branch_ID = 1, Employe_ID = 1, Emp_Name = "فني اختبار", Adje = 0 };
            await using (var db = Create())
            {
                await db.Database.EnsureCreatedAsync();
                db.AddRange(new T_Branch { Branch_ID = 1, Branch = "فرع الاختبار" }, new T_Location { Location = building.Location, Location_ID = 1, Branch_ID = 1 }, building, service, manager, section, employee);
                db.Add(new T_Notif_Action_User { ID = 77, OP_ID = 1, Description = "طلب اختبار", ndate = today, Build = building.Build, Serv_type = service.Serv_type, Mangment_Request = "جهة اختبار", User_Name = "اختبار" });
                db.Add(new T_Notif_Action { ID = 78, DOC_ID = 77, DOC_Number = "77", OP_ID = 1, Branch_ID = 5, Description = "طلب اختبار", Build = building.Build, Serv_type = service.Serv_type, Mangment_Request = "جهة اختبار", User_Name = "اختبار" });
                await db.SaveChangesAsync();
            }
            var draft = new MaintenanceOrderDraft(building, today, employee, 2, service, [employee], [new(manager, section, "صيانة", "فحص لوحة الكهرباء", service.Serv_type)], [], 7, 77);
            var key = await orderStore.InsertAsync(draft);
            Check(changeSignals == 1, "Committed assignment did not publish an immediate notification refresh.");
            var reportDraft = new CompletionDraft(today, 2, "موظف الاختبار", "بانتظار قطع الغيار");
            Check((await store.LoadAsync(key)).Order.T_Employe?.Emp_Name == employee.Emp_Name, "Inspector not loaded for print.");
            Check((await store.SearchAsync(new(Status: 4))).Single().Key == key, "Orders without reports missing.");
            Check((await store.SearchAsync(new(From: today, To: today, Employee: employee, Service: service))).Count == 1, "Combined filters failed.");
            Check((await store.SearchAsync(new(From: today.AddDays(1)))).Count == 0, "Date filter ignored.");
            foreach (var invalid in new[] { reportDraft with { Date = today.AddDays(1) }, reportDraft with { Status = 4 }, reportDraft with { Observer = "   " }, reportDraft with { Observer = new string('س', 51) } })
                await Fails(() => store.SaveAsync(key, invalid, null));

            // فشل متأخر يجب أن يعيد التقرير والرأس والبنود والإشعارات معًا إلى قيمها السابقة.
            failSave = true; await Fails(() => store.SaveAsync(key, reportDraft, null)); failSave = false;
            Check(changeSignals == 1, "Failed report save published a notification change before commit.");
            await using (var db = Create())
            {
                Check(!await db.T_FinishedMaintenances.AnyAsync(), "Failed transaction leaked report.");
                Check(!(await db.T_Emergency_MaintenanceCosts.SingleAsync()).Emergency_MaintenanceCosts_ST.GetValueOrDefault(), "Failed transaction changed header.");
                Check((await db.T_ManNofiusers.SingleAsync()).estate == 1, "Failed transaction changed notification.");
                Check((await db.T_Notif_Action_Users.SingleAsync()).OP_ID == 2, "Failed transaction changed request.");
            }
            await store.SaveAsync(key, reportDraft, null);
            Check(changeSignals == 2, "Committed completion report did not publish an immediate refresh.");
            var doc = await store.LoadAsync(key); long id = doc.Report!.ID;
            Check(doc.Report.State == 2 && doc.Report.observer == reportDraft.Observer && doc.Report.notes == reportDraft.Notes, "Report content changed.");
            Check(doc.Order.Emergency_MaintenanceCosts_ST == true && doc.Order.T_Emergency_MaintenanceCosts_Services.All(x => x.Serv_status == true), "Finished flags missing.");
            await using (var db = Create())
            {
                Check((await db.T_ManNofiusers.SingleAsync()).estate == 2 && (await db.T_ManNotifs.SingleAsync()).mtype == 2, "Pending notification missing.");
                Check((await db.T_ManNofiusers.SingleAsync()).group_type == MaintenanceNotice.SuspendedGroup
                    && (await db.T_ManNotifs.SingleAsync()).dicrb.StartsWith(MaintenanceNotice.SuspendedGroup), "Suspended notification title/group missing.");
                Check((await db.T_Notif_Action_Users.SingleAsync()).OP_ID == 2, "Pending insert changed original request rule.");
            }
            await Fails(() => store.SaveAsync(key, reportDraft, null));
            await Fails(() => store.SaveAsync(key, reportDraft, id + 1));
            Check((await store.SearchAsync(new(Status: 2))).Count == 1 && (await store.SearchAsync(new(Status: 4))).Count == 0, "State filter failed.");

            // اختبار انتقال الحالات الأربع وتحديث إشعارات الطلب عند الإغلاق وإعادة التعليق.
            foreach (byte state in new byte[] { 0, 3, 1, 2 })
            {
                await store.SaveAsync(key, reportDraft with { Status = state }, id);
                await using var db = Create();
                Check((await db.T_FinishedMaintenances.SingleAsync()).State == state, "Status code changed.");
                Check(await db.T_ManNotifs.CountAsync() == (state < 2 ? 0 : 1), "Wrong notification count.");
                if (state >= 2)
                {
                    string expected = state == 2 ? MaintenanceNotice.SuspendedGroup : MaintenanceNotice.InProgressGroup;
                    Check((await db.T_ManNofiusers.SingleAsync()).group_type == expected
                        && (await db.T_ManNotifs.SingleAsync()).dicrb.StartsWith(expected), "Report transition retained the previous notification group.");
                }
                Check((await db.T_Notif_Action_Users.SingleAsync()).OP_ID == (state < 2 ? 3 : 8), "Wrong request transition.");
                Check((await db.T_Notif_Actions.SingleAsync()).Branch_ID == (state < 2 ? 3 : 5), "Wrong request routing.");
            }
            doc = await store.LoadAsync(key);
            using (var report = MaintenanceCompletionReport.Create(doc.Order, DateTime.Today, "موظف الاختبار", "ملاحظات اختبار"))
            {
                var rows = (DataView)report.DataSources[0].Value;
                Check((string)rows[0]["elm25"] == "موظف الاختبار" && (string)rows[0]["elm27"] == employee.Emp_Name, "Original report fields missing.");
                byte[] pdf = report.Render("PDF"); Check(pdf.Length > 1000, "Original completion RDLC cannot render.");
                Directory.CreateDirectory("artifacts/completion-preview"); await File.WriteAllBytesAsync("artifacts/completion-preview/completion-report-test.pdf", pdf);
            }
            // الحذف من الحالة النهائية يعيد الطلب إلى 8 ويعيد إنشاء إشعار التكليف.
            await store.SaveAsync(key, reportDraft with { Status = 0 }, id);
            await using (var db = Create())
            {
                (await db.T_Notif_Action_Users.SingleAsync()).state = 1;
                (await db.T_Notif_Actions.SingleAsync()).state = 1;
                await db.SaveChangesAsync();
            }
            await store.SaveAsync(key, reportDraft with { Status = 0, Notes = "تعديل الملاحظات فقط" }, id);
            await using (var db = Create()) Check((await db.T_Notif_Action_Users.SingleAsync()).state == 1
                && (await db.T_Notif_Actions.SingleAsync()).state == 1, "Unchanged final status reset the request read state.");
            await store.DeleteAsync(key, id);
            Check((await store.LoadAsync(key)).Report is null, "Delete left a report.");
            await using (var db = Create())
            {
                Check((await db.T_Emergency_MaintenanceCosts.SingleAsync()).Emergency_MaintenanceCosts_ST == false, "Delete did not reopen header.");
                Check((await db.T_Emergency_MaintenanceCosts_Services.SingleAsync()).Serv_status == false, "Delete did not reopen lines.");
                Check((await db.T_ManNofiusers.SingleAsync()).estate == 1 && (await db.T_ManNotifs.SingleAsync()).estate == 1, "Delete did not restore notification.");
                Check((await db.T_Notif_Action_Users.SingleAsync()).OP_ID == 8, "Delete did not restore request operation 8.");
            }
            await Fails(() => store.DeleteAsync(key, id));

            // نطاق آخر بنفس الرقم داخل الفرع يُعرض كخيار مستقل ويمنع تعديل الإشعارات الملتبسة.
            await using (var db = Create())
            {
                db.AddRange(new T_Location { Location = "موقع آخر", Location_ID = 2, Branch_ID = 1 },
                    new T_Build { Location = "موقع آخر", Location_ID = 2, Branch_ID = 1, Build_ID = 1, Build = "مبنى آخر" },
                    new T_Service { Location = "موقع آخر", Location_ID = 2, Branch_ID = 1, Serv_num = 1, Serv_type = "خدمة أخرى" });
                db.Add(new T_Emergency_MaintenanceCost { Location = "موقع آخر", Location_ID = 2, Branch_ID = 1, Emergency_MaintenanceCosts_No = key.Number,
                    Build_ID = 1, Serv_num = 1, Date_com = today, Date_Modify = today, Emergency_MaintenanceCosts_ST = false });
                await db.SaveChangesAsync();
            }
            Check((await store.SearchAsync(new(Number: key.Number))).Count == 2, "Duplicate number was silently collapsed.");
            Check((await store.SearchAsync(new(Service: service))).Count == 1, "Service filter crossed location scope.");
            await Fails(() => store.SaveAsync(key, reportDraft, null));
            Check((await store.LoadAsync(key)).Report is null, "Ambiguous notification rollback failed.");
            Console.WriteLine($"PASS: {checks} completion database/report checks; SQLite only; no production database."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    #endregion
}
