using Maintenance_app.Dashboard;
using Maintenance_app.dbContext;
using Maintenance_app.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

internal static class AnnualStatisticsSpecs
{
    #region تجهيز حالات إحصائية مستقلة عن قاعدة العمل
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    public static async Task RunAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<Maintenance_DabaseContext>().UseSqlite(connection).Options;
        Maintenance_DabaseContext Create() => new TestContext(options);
        var today = DateOnly.FromDateTime(DateTime.Today);
        await using (var db = Create())
        {
            await db.Database.EnsureCreatedAsync(); db.Add(new T_Branch { Branch_ID = 1, Branch = "اختبار" });
            foreach (short scope in new short[] { 1, 2 })
            {
                string location = "موقع " + scope;
                db.AddRange(new T_Location { Location = location, Location_ID = scope, Branch_ID = 1 },
                    new T_Build { Location = location, Location_ID = scope, Branch_ID = 1, Build_ID = 1, Build = "مبنى" },
                    new T_Service { Location = location, Location_ID = scope, Branch_ID = 1, Serv_num = 1, Serv_type = "خدمة" });
            }
            T_Emergency_MaintenanceCost Order(long number, int year = 2025, short scope = 1, bool? flag = true)
            {
                var value = new T_Emergency_MaintenanceCost { Location = "موقع " + scope, Location_ID = scope, Branch_ID = 1,
                    Emergency_MaintenanceCosts_No = number, Build_ID = 1, Serv_num = 1, Date_com = new(year, 6, 1), Date_Modify = today,
                    Emergency_MaintenanceCosts_ST = flag };
                db.Add(value); return value;
            }
            void Report(T_Emergency_MaintenanceCost order, long id, byte? state, int days = 0) =>
                order.T_FinishedMaintenances.Add(new() { ID = id, Location = order.Location, Location_ID = order.Location_ID,
                    Branch_ID = 1, MaintenanceCosts_No = order.Emergency_MaintenanceCosts_No, Build_ID = 1,
                    DateM = new(2027, 1, 3), Date_Modify = today.AddDays(days), State = state, observer = "موظف اختبار", notes = "اختبار إحصاء" });
            Order(1, flag: false); Order(2, flag: null);
            Report(Order(3, flag: false), 30, 0); Report(Order(4), 40, 1); Report(Order(5), 50, 2); Report(Order(6), 60, 3);
            Order(7); Report(Order(8), 80, null); Report(Order(9), 90, 9);
            var repeated = Order(10); Report(repeated, 100, 2, -1); Report(repeated, 101, 0);
            var tie = Order(11); Report(tie, 110, 3); Report(tie, 111, 1);
            var datePriority = Order(12); Report(datePriority, 999, 0, -1); Report(datePriority, 120, 2);
            Report(Order(1, scope: 2), 200, 3);
            Order(101, 2026, flag: false); Report(Order(102, 2026), 1020, 0);
            await db.SaveChangesAsync();
        }
        #endregion

        #region صحة الحالات والسنة والمفتاح المركب وحالة العرض
        var repository = new AnnualStatisticsRepository(Create); var result = await repository.LoadAsync();
        var model = new AnnualStatisticsViewModel(); model.ConfigureAccess(true); model.SelectedYearNumber = 2025; model.Apply(result);
        Check(model.CompletedCount == 2, "Completed orders counted wrong or a stale report won.");
        Check(model.NotCompletedCount == 2, "Not-completed report/tie-break count wrong.");
        Check(model.SuspendedCount == 2, "Suspended count or report date priority wrong.");
        Check(model.InProgressCount == 2, "Composite-key isolation failed in annual count.");
        Check(model.NeedsReportCount == 2, "False/null order flags without reports must need completion.");
        Check(model.TotalCount == 13 && model.UnclassifiedCount == 3, "Multiple reports duplicated orders or inconsistent records were misclassified.");
        Check(model.SelectionStatus.Contains("3"), "Unclassified records must be disclosed.");
        model.SelectedYearNumber = 2026;
        Check(model.CompletedCount == 1 && model.NeedsReportCount == 1 && model.TotalCount == 2, "Year selection did not use order date.");
        model.SelectedYearNumber = 2024; Check(model.TotalCount == 0 && model.CompletedText == "0", "Empty selected year did not display zero.");
        model.SelectedYearNumber = null;
        Check(model.CompletedCount == 3 && model.NeedsReportCount == 3 && model.TotalCount == 15, "All-years aggregation failed.");
        model.SelectedYearNumber = 2025; model.BeginLoad(); model.Apply(result);
        Check(model.SelectedYearNumber == 2025 && !model.IsBusy && model.HasData, "Refresh reset selected year or loading state.");
        model.Fail("تعذّر التحديث.");
        Check(model.CompletedCount == 2 && model.IsError && model.Status.Contains("آخر قراءة"), "Failure erased valid cached counts.");
        model.ConfigureAccess(false);
        Check(!model.HasData && model.CompletedText == "—" && !model.CanRefresh, "Logged-out session retained visible statistics.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool rejected = false;
        try { await repository.LoadAsync(cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
        Check(rejected, "Cancellation ignored.");
        // فحص SQL Server دون اتصال أو أسرار: يجب ترجمة التجميع والمفتاح وآخر تقرير بالكامل.
        await using (var db = new Maintenance_DabaseContext(new DbContextOptionsBuilder<Maintenance_DabaseContext>()
            .UseSqlServer("Server=localhost;Database=StatisticsTranslationOnly;Integrated Security=True;TrustServerCertificate=True").Options))
        {
            string sql = AnnualStatisticsRepository.Query(db).ToQueryString();
            Check(sql.Contains("COUNT_BIG") && sql.Contains("GROUP BY") && sql.Contains("NOT EXISTS") && sql.Contains("LEFT JOIN"),
                "Annual counts did not translate to a server-side aggregate.");
        }
        await using (var db = Create()) Check(await db.T_Emergency_MaintenanceCosts.CountAsync() == 15
            && await db.T_FinishedMaintenances.CountAsync() == 14, "Statistics read mutated source data.");
        Console.WriteLine($"PASS: {checks} annual maintenance statistics checks; five states, latest-report precedence, composite isolation, order year/all years, failure/cancellation, SQL Server aggregate translation; isolated SQLite only.");
        #endregion
    }
}
