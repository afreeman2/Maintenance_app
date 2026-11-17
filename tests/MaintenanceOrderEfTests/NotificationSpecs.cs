using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Maintenance_app.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

internal static class NotificationSpecs
{
    #region اختبار حالات الإنهاء وعزل المفاتيح وغياب الكتابة

    public static async Task RunAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
        Maintenance_DabaseContext Create() => new TestContext(options);
        var user = new T_SystemUser { User_ID = 1, formaint = true };
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        await using (var db = Create())
        {
            await db.Database.EnsureCreatedAsync();
            db.T_Branches.Add(new T_Branch { Branch_ID = 1, Branch = "فرع اختبار" });
            foreach (short locationId in new short[] { 1, 2 })
            {
                string location = "موقع " + locationId;
                db.T_Locations.Add(new T_Location { Branch_ID = 1, Location_ID = locationId, Location = location });
                db.T_Builds.Add(new T_Build { Branch_ID = 1, Location_ID = locationId, Location = location, Build_ID = 1, Build = "مبنى " + locationId });
                db.T_Services.Add(new T_Service { Branch_ID = 1, Location_ID = locationId, Location = location, Serv_num = 1, Serv_type = "كهرباء" });
                foreach (long number in locationId == 1 ? new long[] { 10, 11, 12, 13, 14, 15, 16 } : new long[] { 10 })
                {
                    var order = new T_Emergency_MaintenanceCost
                    {
                        Location = location, Location_ID = locationId, Branch_ID = 1, Build_ID = 1, Serv_num = 1,
                        Emergency_MaintenanceCosts_No = number, Emergency_MaintenanceCosts_ST = number == 16 ? null : number >= 12,
                        Date_com = today.AddDays(-(int)number), Date_Modify = today, User_ID = 1
                    };
                    // 10 بلا تقرير في الموقعين، 11 تقرير نهائي رغم علم رأس قديم، 12 نهائي، 13 معلق، 14 جار، 15 علم إنهاء دون تقرير.
                    if (locationId == 1 && number >= 11 && number <= 14)
                    {
                        order.T_FinishedMaintenances.Add(new T_FinishedMaintenance
                        {
                            Location = location, Location_ID = locationId, Branch_ID = 1, Build_ID = 1,
                            MaintenanceCosts_No = number, State = (byte)(number - 11), DateM = today,
                            Date_Modify = today, observer = "فاحص", notes = "ملاحظة اختبار"
                        });
                    }
                    db.T_Emergency_MaintenanceCosts.Add(order);
                }
            }
            // إشعار قديم ومكرر منطقيًا لا ينبغي أن يضاعف الصفوف أو يعيد تكليفًا منتهيًا إلى القائمة.
            db.T_ManNofiusers.Add(new T_ManNofiuser { id = 1, eno = 12, Branch_ID = 1, estate = 1, edate = DateTime.Today, dicrb = "قديم", group_type = "اختبار" });
            await db.SaveChangesAsync();
        }
        var store = new MaintenanceNotificationStore(Create);
        List<MaintenanceNotice> rows = await store.LoadAsync(user);
        Require(rows.Count == 5, "Pending count must include two scoped #10 orders, pending reports 13/14 and null-state #16.");
        Require(rows.Count(row => row.Number == 10) == 2 && rows.Where(row => row.Number == 10).Select(row => row.Identity).Distinct().Count() == 2,
            "Repeated numbers across locations were merged.");
        Require(rows.All(row => row.Number != 11 && row.Number != 12 && row.Number != 15), "Completed/terminal order leaked into notifications.");
        Require(rows.Single(row => row.Number == 13).ReportState == 2 && !rows.Single(row => row.Number == 13).NeedsReport, "Suspended report classification failed.");
        Require(rows.Single(row => row.Number == 14).ReportState == 3 && rows.Single(row => row.Number == 14).Notes == "ملاحظة اختبار", "In-progress report details missing.");
        Require(rows.All(row => row.Branch == "فرع اختبار" && row.Service == "كهرباء" && row.DaysWaiting >= 10), "Notification display fields or age are incorrect.");
        await using (var db = Create())
        {
            // تقرير نهائي أحدث يحسم الحالة حتى مع بقاء تقرير معلق قديم في السجل.
            db.T_FinishedMaintenances.Add(new T_FinishedMaintenance
            {
                ID = 10000, Location = "موقع 1", Location_ID = 1, Branch_ID = 1, Build_ID = 1,
                MaintenanceCosts_No = 13, State = 0, DateM = today, Date_Modify = today,
                observer = "فاحص", notes = "نتيجة نهائية أحدث"
            });
            var order = await db.T_Emergency_MaintenanceCosts.SingleAsync(item => item.Location_ID == 1 && item.Emergency_MaintenanceCosts_No == 10);
            order.Emergency_MaintenanceCosts_ST = true;
            await db.SaveChangesAsync();
        }
        rows = await store.LoadAsync(user);
        Require(rows.Count == 3 && rows.All(row => row.Number != 13) && rows.Single(row => row.Number == 10).LocationId == 2,
            "Refresh retained a completed row or removed another scope.");
        await using (var db = Create())
        {
            Require(await db.T_ManNofiusers.CountAsync() == 1 && await db.T_Emergency_MaintenanceCosts.CountAsync() == 8,
                "Reading notifications mutated legacy notices or orders.");
        }
        bool denied = false;
        var ordinaryUserRows = await store.LoadAsync(new T_SystemUser { User_ID = 2 });
        Require(ordinaryUserRows.Select(row => row.Identity).OrderBy(value => value).SequenceEqual(rows.Select(row => row.Identity).OrderBy(value => value)),
            "An authenticated ordinary user did not receive the same system notifications.");
        try { await store.LoadAsync(new T_SystemUser()); }
        catch (UnauthorizedAccessException) { denied = true; }
        Require(denied && MaintenanceNoticeAccess.CanView(new T_SystemUser { User_ID = 3, fordon = true })
            && MaintenanceNoticeAccess.CanView(new T_SystemUser { User_ID = 4 })
            && !MaintenanceNoticeAccess.CanView(null), "Authenticated notification access is not enforced.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool cancelled = false;
        try { await store.LoadAsync(user, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled, "Cancellation was ignored.");
        Console.WriteLine("PASS: relational notification projection; report states 0/1/2/3, null/true flags, composite-key isolation, stale legacy notices, refresh removal, permissions and cancellation; no production database.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    #endregion
}
