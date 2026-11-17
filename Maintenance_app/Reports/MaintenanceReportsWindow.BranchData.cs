using Microsoft.EntityFrameworkCore;
using Maintenance_app.orders_Windows;
using Maintenance_app.viewo_Model;
using System.Globalization;
using AggregatedContext = Maintenance_app.dbContext_Aggregated.Maintenance_Aggregated_DabaseContext;

namespace Maintenance_app.reports;

public partial class MaintenanceReportsWindow
{
    #region بيانات تقارير الفروع
    private sealed record BranchChoice(short Id, string Name);
    private sealed record BranchCompletion(string Date, string Observer, string Notes);
    private sealed record BranchFilters(List<string> Buildings, List<string> Services, List<string> Technicians);

    // يحتفظ البند بترميزه وموقعه لعرضهما في العمودين المشتركين مع التقرير المحلي.
    private sealed record BranchDetail(long Id, string Statement, string Scope, short ActionCode)
    {
        public string ActionName => maint_reports_viewomodel.GetActionName(ActionCode);
    }

    private sealed record BranchReportLine(MaintenanceReportRow Order, BranchDetail? Detail)
    {
        // رقم البند وحده لا يكفي عند استعادة تحديد الصف عبر الفروع والمواقع.
        public (MaintenanceOrderKey, long?) RowKey => (Order.Key, DetailId);
        public long? DetailId => Detail?.Id;
        public string AssignmentKey => Order.AssignmentKey;
        public short BranchId => Order.BranchId;
        public long Number => Order.Number;
        public decimal Cost => Order.Cost;
        public string Statement => Detail?.Statement ?? "لا توجد بنود صيانة مسجلة.";
        public string Scope => Detail?.Scope ?? "";
        public short? ActionCode => Detail?.ActionCode;
        public string ActionName => Detail?.ActionName ?? "";
    }

    // تُقرأ الفروع من قاعدة التجميع، وتظل الفروع القديمة متاحة لعرض التقارير التاريخية.
    private static Task<List<BranchChoice>> QueryBranchesAsync(AggregatedContext db, CancellationToken token) =>
        db.TBranches.AsNoTracking().OrderBy(branch => branch.Branch).ThenBy(branch => branch.BranchId)
            .Select(branch => new BranchChoice(branch.BranchId, branch.Branch ?? "فرع " + branch.BranchId))
            .ToListAsync(token);

    // قوائم المباني والخدمات والفنيين تُقرأ من الجداول المرتبطة بالفرع المختار فقط.
    private static async Task<BranchFilters> QueryBranchFiltersAsync(AggregatedContext db, short branchId, CancellationToken token)
    {
        var buildings = await QueryNamesAsync(db.TBuilds.AsNoTracking().Where(item => item.BranchId == branchId)
            .Select(item => item.Build), token);
        var services = await QueryNamesAsync(db.TServices.AsNoTracking().Where(item => item.BranchId == branchId)
            .Select(item => item.ServType), token);
        var technicians = await QueryNamesAsync(db.TEmployes.AsNoTracking().Where(item => item.BranchId == branchId)
            .Select(item => item.EmpName), token);
        return new(buildings, services, technicians);
    }

    private static Task<List<string>> QueryNamesAsync(IQueryable<string> names, CancellationToken token) =>
        names.Where(name => name != null && name != "").Distinct().OrderBy(name => name).ToListAsync(token);

    // يطبّق استعلام التجميع الفترة وحدودها الشاملة، ويضيف شرط الفرع إذا اختاره المستخدم.
    private static async Task<List<MaintenanceReportRow>> QueryBranchReportsAsync(
        AggregatedContext db, short? branchId, DateOnly? from, DateOnly? to, CancellationToken token)
    {
        var query = db.TEmergencyMaintenanceCosts.AsNoTracking().AsSplitQuery();
        if (branchId is short selectedBranch) query = query.Where(order => order.BranchId == selectedBranch);
        if (from is DateOnly startDate) query = query.Where(order => order.DateCom >= startDate);
        if (to is DateOnly endDate) query = query.Where(order => order.DateCom <= endDate);

        var orders = await query
            .Include(order => order.Branch)
            .Include(order => order.TMaintenanceCostsParts)
            .Include(order => order.TBuild).Include(order => order.TService).Include(order => order.TEmploye)
            .Include(order => order.TEmpMaints).ThenInclude(item => item.TEmploye)
            .Include(order => order.TEmergencyMaintenanceCostsServices).ThenInclude(item => item.TManage)
            .Include(order => order.TEmergencyMaintenanceCostsServices).ThenInclude(item => item.TSection)
            .Include(order => order.TFinishedMaintenances)
            .OrderByDescending(order => order.DateCom).ThenByDescending(order => order.EmergencyMaintenanceCostsNo)
            .ToListAsync(token);

        return orders.Select(order =>
        {
            var completion = order.TFinishedMaintenances.OrderByDescending(item => item.DateModify)
                .ThenByDescending(item => item.Id).FirstOrDefault();
            // نقرأ تفاصيل كل بند مرة واحدة للجدول والكشف المطبوع.
            var details = order.TEmergencyMaintenanceCostsServices.OrderBy(item => item.Id)
                .Select(item => new BranchDetail(item.Id, item.Statement ?? "",
                    JoinScope(item.TManage?.Manag, item.TSection?.Secti), item.MaintType)).ToArray();
            // نفس حساب تكلفة القطع في تكليف الصيانة، مع اعتبار السعر غير المسجل صفرًا.
            var cost = order.TMaintenanceCostsParts.Sum(part => part.Quantity * (part.Price ?? 0m));
            return new MaintenanceReportRow(
                new(order.Location, order.LocationId, order.BranchId, order.EmergencyMaintenanceCostsNo),
                order.EmergencyMaintenanceCostsNo, order.DateCom, order.DateCom.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                order.TBuild?.Build ?? "", order.TService?.ServType ?? "", MaintenanceType(order.Type),
                string.Join("، ", order.TEmpMaints.OrderBy(item => item.Id).Select(item => item.TEmploye?.EmpName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))),
                Status(completion?.State, order.EmergencyMaintenanceCostsSt),
                string.Join(" • ", details.Select(item => item.Statement).Where(text => !string.IsNullOrWhiteSpace(text))),
                string.Join(" • ", details.Select(item => item.Scope).Where(text => !string.IsNullOrWhiteSpace(text))),
                completion is null ? null : new(completion.DateM.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    completion.Observer ?? "", completion.Notes ?? ""),
                order.Branch?.Branch ?? $"فرع {order.BranchId}", cost, order.ServNum,
                PrintSegments: CreatePrintSegments(details.Select(item => new PrintDetails(item.Statement, item.Scope, item.ActionName))),
                Inspector: order.TEmploye?.EmpName, BranchDetails: details);
        }).ToList();
    }
    #endregion
}
