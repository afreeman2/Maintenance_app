
using Maintenance_app.dbContext;
using Maintenance_app.Reports.charts_work;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Maintenance_app.Reports
{
    public class MaintenanceChartService
    {
        private readonly Maintenance_DabaseContext _db;

        public MaintenanceChartService(
            Maintenance_DabaseContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<MaintenanceChartResult>> GetChartAsync(
            int fromYear,
            int toYear,
            bool groupByMonth,
            short? buildId = null,
            int? endMonth = null,
            CancellationToken cancellationToken = default)
        {
            // ==========================================
            // 1. التحقق من السنوات
            // ==========================================

            if (fromYear < 1 ||
                toYear > 9998 ||
                fromYear > toYear)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fromYear),
                    "مدى السنوات غير صحيح.");
            }

            if (groupByMonth && fromYear != toYear)
            {
                throw new ArgumentException(
                    "التجميع الشهري يتطلب سنة واحدة.");
            }

            // ==========================================
            // 2. تحديد آخر شهر
            // ==========================================

            int lastMonth = groupByMonth
                ? endMonth ?? 12
                : 12;

            if (lastMonth < 1 || lastMonth > 12)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(endMonth),
                    "الشهر المحدد غير صحيح.");
            }

            // ==========================================
            // 3. تحديد الفترة الزمنية
            // ==========================================

            DateOnly startDate =
                new DateOnly(fromYear, 1, 1);

            DateOnly endDate =
                new DateOnly(
                    groupByMonth ? fromYear : toYear,
                    lastMonth,
                    1).AddMonths(1);

            // ==========================================
            // 4. تجميع تكلفة قطع الغيار في SQL Server
            // ==========================================

            var partsSummary =
                from p in _db.T_MaintenanceCosts_Parts
                    .AsNoTracking()

                group p by new
                {
                    p.Location,
                    p.Location_ID,
                    p.Branch_ID,
                    p.MaintenanceCosts_No
                }
                into g

                select new
                {
                    g.Key.Location,
                    g.Key.Location_ID,
                    g.Key.Branch_ID,
                    g.Key.MaintenanceCosts_No,

                    TotalPrice = g.Sum(x =>
                        (x.price ?? 0m) * x.Quantity)
                };

            // ==========================================
            // 5. قراءة أوامر الصيانة وحالتها وتكلفتها
            // ==========================================

            var query =
                from e in _db.T_Emergency_MaintenanceCosts
                    .AsNoTracking()

                join f in _db.T_FinishedMaintenances
                    .AsNoTracking()
                    on new
                    {
                        e.Location,
                        e.Location_ID,
                        e.Branch_ID,
                        MaintenanceCosts_No =
                            e.Emergency_MaintenanceCosts_No
                    }
                    equals new
                    {
                        f.Location,
                        f.Location_ID,
                        f.Branch_ID,
                        f.MaintenanceCosts_No
                    }

                join p in partsSummary
                    on new
                    {
                        e.Location,
                        e.Location_ID,
                        e.Branch_ID,
                        MaintenanceCosts_No =
                            e.Emergency_MaintenanceCosts_No
                    }
                    equals new
                    {
                        p.Location,
                        p.Location_ID,
                        p.Branch_ID,
                        p.MaintenanceCosts_No
                    }
                    into partsJoin

                from p in partsJoin.DefaultIfEmpty()

                where e.Date_com >= startDate
                   && e.Date_com < endDate
                   && (!buildId.HasValue ||
                       e.Build_ID == buildId.Value)

                select new
                {
                    e.Date_com,
                    f.State,

                    TotalPrice = (decimal?)p.TotalPrice
                };

            // ==========================================
            // 6. تنفيذ الاستعلام قبل GroupBy النهائي
            // ==========================================

            var rows = await query
                .ToListAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // ==========================================
            // 7. تجميع النتائج في الذاكرة
            // ==========================================

            var results = rows
                .GroupBy(x => new
                {
                    Year = x.Date_com.Year,

                    Month = groupByMonth
                        ? x.Date_com.Month
                        : 0
                })
                .Select(g => new MaintenanceChartResult
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,

                    // منجزة
                    Don = g.Count(x => x.State == 0),

                    // معلقة
                    Temp = g.Count(x => x.State == 1),

                    // غير منجزة
                    Undon = g.Count(x => x.State == 2),

                    // قيد التنفيذ
                    Moveing = g.Count(x => x.State == 3),

                    // إجمالي الأوامر
                    MCount = g.Count(),

                    // إجمالي تكلفة قطع الغيار
                  
                    DonPrice = g.Where(x => x.State == 0)
            .Sum(x => x.TotalPrice) ?? 0m,

                    TempPrice = g.Where(x => x.State == 1)
             .Sum(x => x.TotalPrice) ?? 0m,

                    UndonPrice = g.Where(x => x.State == 2)
              .Sum(x => x.TotalPrice) ?? 0m,

                    MoveingPrice = g.Where(x => x.State == 3)
                .Sum(x => x.TotalPrice) ?? 0m,

                    MPrice = g.Sum(x => x.TotalPrice) ?? 0m
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToList();

            return results;
        }
    }
}
