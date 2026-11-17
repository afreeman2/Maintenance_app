using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.orders_Windows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Data.Common;

// The database lives only in this connection. Identity assignment below emulates
// SQL Server's identity columns on composite keys, which SQLite cannot generate.
internal sealed class TestContext(DbContextOptions<Maintenance_DabaseContext> options) : Maintenance_DabaseContext(options)
{
    private static long identity = 100;
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State == EntityState.Added))
            foreach (var property in entry.Properties.Where(p => p.Metadata.IsPrimaryKey() && p.Metadata.ValueGenerated == ValueGenerated.OnAdd))
                if (property.IsTemporary || Convert.ToInt64(property.CurrentValue) == 0)
                {
                    object value = Convert.ChangeType(Interlocked.Increment(ref identity), property.Metadata.ClrType);
                    property.CurrentValue = value; property.IsTemporary = false;
                }
        return base.SaveChangesAsync(cancellationToken);
    }
}

internal static class Program
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Fails(Func<Task> action, string message)
    {
        try { await action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }
    private static async Task<int> Main()
    {
        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
            Maintenance_DabaseContext Create() => new TestContext(options);
            var store = new MaintenanceOrderEfStore(Create);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var building = new T_Build { Location = "موقع الاختبار", Location_ID = 1, Branch_ID = 1, Build_ID = 1, Build = "مبنى الاختبار" };
            var service = new T_Service { Location = building.Location, Location_ID = 1, Branch_ID = 1, Serv_num = 1, Serv_type = "كهرباء" };
            var manager = new T_Manage { Location = building.Location, Location_ID = 1, Branch_ID = 1, Manag_ID = 1, Manag = "الإدارة" };
            var section = new T_Section { Location = building.Location, Location_ID = 1, Branch_ID = 1, Secti_ID = 1, Manag_ID = 1, Secti = "القسم", Date_Modify = today };
            var employee = new T_Employe { Location = building.Location, Location_ID = 1, Branch_ID = 1, Employe_ID = 1, Emp_Name = "الفني الأول", Adje = 0 };
            var employee2 = new T_Employe { Location = building.Location, Location_ID = 1, Branch_ID = 1, Employe_ID = 2, Emp_Name = "الفني الثاني", Adje = 2 };
            var repair = new RepairLine(manager, section, "صيانة", "فحص لوحة الكهرباء", service.Serv_type);
            var part = new MaterialLine("قاطع", 2.5m, "قطعة", 12.345m);
            var draft = new MaintenanceOrderDraft(building, today, employee, 2, service, [employee, employee2], [repair], [part], 7, 77);
            await using (var seed = Create())
            {
                await seed.Database.EnsureCreatedAsync();
                seed.AddRange(new T_Branch { Branch_ID = 1, Branch = "فرع الاختبار" },
                    new T_Location { Location = building.Location, Location_ID = 1, Branch_ID = 1 },
                    building, service, manager, section, employee, employee2);
                seed.T_Notif_Action_Users.Add(new T_Notif_Action_User { ID = 77, Description = "طلب", OP_ID = 1, ndate = today,
                    Build = building.Build, Serv_type = service.Serv_type, Mangment_Request = "طلب", User_Name = "اختبار" });
                seed.T_Notif_Actions.Add(new T_Notif_Action { ID = 78, DOC_ID = 77, DOC_Number = "77", OP_ID = 1, Description = "طلب", Branch_ID = 5,
                    Build = building.Build, Serv_type = service.Serv_type, Mangment_Request = "طلب", User_Name = "اختبار" });
                await seed.SaveChangesAsync();
            }
            long first = long.Parse("7" + today.Year + "00001");
            Check(await store.NextNumberAsync(7, today.Year) == first, "First number format changed.");
            var key = await store.InsertAsync(draft);
            Check(key.Number == first, "Saved number differs from original numbering.");
            var saved = await store.LoadAsync(key);
            Check(saved.T_Emergency_MaintenanceCosts_Services.Single().Statement == repair.Details && saved.T_Emp_Maints.Count == 2, "Children were not saved.");
            Check(saved.T_MaintenanceCosts_Parts.Single().Quantity * saved.T_MaintenanceCosts_Parts.Single().price == 30.8625m, "Decimal values were lost.");
            await using (var verify = Create())
            {
                Check((await verify.T_ManNofiusers.SingleAsync()).eno == first && await verify.T_ManNotifs.CountAsync() == 1, "Save notifications missing.");
                var request = await verify.T_Notif_Action_Users.SingleAsync();
                Check(request.MaintenanceCosts_No == first && request.OP_ID == 2 && (await verify.T_Notif_Actions.SingleAsync()).Branch_ID == 2, "Request link changed.");
            }
            // A late request-link failure must roll back parent, children and notifications.
            await Fails(() => store.InsertAsync(draft with { RequestId = 999 }), "Missing request unexpectedly saved.");
            await using (var verify = Create()) Check(await verify.T_Emergency_MaintenanceCosts.CountAsync() == 1 && await verify.T_ManNotifs.CountAsync() == 1, "Failed save leaked rows.");
            Check(await store.NextNumberAsync(7, today.Year) == first + 1, "Failed save consumed a number.");
            var materialId = await store.AddPartAsync(key, part with { Name = "كابل", Quantity = 4 });
            await store.UpdatePartAsync(key, materialId, part with { Name = "كابل معدل", Quantity = 5 });
            Check((await store.LoadAsync(key)).T_MaintenanceCosts_Parts.Single(x => x.ID == materialId).Quantity == 5, "Immediate material edit failed.");
            await store.DeletePartAsync(key, materialId);
            var repairId = await store.AddRepairAsync(key, repair with { Details = "إضافة بيان" });
            await store.UpdateRepairAsync(key, repairId, repair with { Details = "تعديل بيان" });
            Check((await store.LoadAsync(key)).T_Emergency_MaintenanceCosts_Services.Single(x => x.ID == repairId).Statement == "تعديل بيان", "Immediate repair edit failed.");
            await store.DeleteRepairAsync(key, repairId);
            await Fails(() => store.AddRepairAsync(key, repair with { Section = new T_Section { Manag_ID = 50 } }), "Cross-scope repair accepted.");
            await Fails(() => store.AddPartAsync(key, part with { Quantity = -1 }), "Negative quantity accepted.");
            await store.UpdateAsync(key, draft with { UserId = 9, Type = 1, Technicians = [employee], Repairs = [repair with { Details = "التفاصيل الجديدة" }], Parts = [part with { Quantity = 3 }], RequestId = null });
            saved = await store.LoadAsync(key);
            Check(saved.Type == 1 && saved.T_Emp_Maints.Count == 1 && saved.T_Emergency_MaintenanceCosts_Services.Count == 1
                && saved.T_Emergency_MaintenanceCosts_Services.Single().Statement == "التفاصيل الجديدة"
                && saved.T_MaintenanceCosts_Parts.Single().Quantity == 3, "Whole-order update did not replace children.");
            Check(saved.User_ID == 7 && saved.Date_Modify == today, "Update changed original numbering ownership.");
            await using (var verify = Create()) Check((await verify.T_ManNofiusers.SingleAsync()).etype == 1 && (await verify.T_ManNotifs.SingleAsync()).etype == 1, "Update notifications diverged.");
            // Same number at another location: no edit/load/delete may touch that order.
            var foreign = new MaintenanceOrderKey("الموقع الثاني", 2, 1, first);
            await using (var seed = Create())
            {
                seed.AddRange(new T_Location { Location = foreign.Location, Location_ID = 2, Branch_ID = 1 },
                    new T_Build { Location = foreign.Location, Location_ID = 2, Branch_ID = 1, Build_ID = 1, Build = "آخر" },
                    new T_Service { Location = foreign.Location, Location_ID = 2, Branch_ID = 1, Serv_num = 1, Serv_type = "آخر" });
                seed.T_Emergency_MaintenanceCosts.Add(new T_Emergency_MaintenanceCost { Location = foreign.Location, Location_ID = 2, Branch_ID = 1,
                    Emergency_MaintenanceCosts_No = first, Build_ID = 1, Serv_num = 1, Date_com = today, Date_Modify = today, User_ID = 8 });
                seed.T_FinishedMaintenances.Add(new T_FinishedMaintenance { Location = key.Location, Location_ID = key.LocationId, Branch_ID = key.BranchId,
                    MaintenanceCosts_No = key.Number, Build_ID = 1, DateM = today, Date_Modify = today, observer = "الفاحص", notes = "تم" });
                await seed.SaveChangesAsync();
            }
            await Fails(() => store.LoadAsync(key), "Completed order can be loaded for editing.");
            await Fails(() => store.UpdateAsync(key, draft), "Completed order update accepted.");
            await Fails(() => store.DeleteAsync(key), "Completed order deletion accepted.");
            await Fails(() => store.AddPartAsync(key, part), "Completed order child mutation accepted.");
            await using (var seed = Create()) { seed.T_FinishedMaintenances.RemoveRange(await seed.T_FinishedMaintenances.ToListAsync()); await seed.SaveChangesAsync(); }
            // Render the unchanged original templates with EF-loaded data.
            saved = await store.LoadAsync(key);
            using (var report = MaintenanceOrderReport.Create(saved, employee.Emp_Name))
            { Check(report.Render("PDF").Length > 1000, "Original single-technician report did not render."); }
            await store.UpdateAsync(key, draft with { RequestId = null });
            saved = await store.LoadAsync(key);
            using (var report = MaintenanceOrderReport.Create(saved, employee.Emp_Name))
            { Check(report.Render("PDF").Length > 1000, "Original multiple-technician report did not render."); }
            // لا يحمل جدول الطلب موقعًا؛ يرفض حذف التكليف المرتبط عندما يتكرر رقمه بين موقعين.
            await Fails(() => store.DeleteAsync(key), "Ambiguous request link was updated during deletion.");
            await using (var verify = Create())
            {
                Check(await verify.T_Emergency_MaintenanceCosts.CountAsync() == 2 && (await store.LoadAsync(foreign, true)).Location == foreign.Location, "Rejected deletion affected another scope.");
                Check(await verify.T_Emp_Maints.AnyAsync() && await verify.T_Emergency_MaintenanceCosts_Services.AnyAsync(), "Rejected deletion lost child rows.");
                var request = await verify.T_Notif_Action_Users.SingleAsync();
                var notice = await verify.T_Notif_Actions.SingleAsync();
                Check(request.MaintenanceCosts_No == first && request.OP_ID == 2 && notice.OP_ID == 2, "Rejected deletion changed the linked request.");
            }
            await NotificationSpecs.RunAsync();
            await AnnualStatisticsSpecs.RunAsync();
            Console.WriteLine("PASS: isolated relational EF Core insert/update/delete; child CRUD; numbering; notifications; request transitions; rollback; completed-order guards; composite-key isolation; decimal precision; both original RDLC templates rendered to PDF. No live database writes.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
