using MahApps.Metro.Controls;
using Maintenance_app.Models;
using Maintenance_app.Reports;
using Microsoft.Reporting.WinForms;
using System.Data;
using System.Globalization;
using System.IO;

namespace Maintenance_app.orders_Windows;

// تعبئة حقول Done_Mainte_Report الأصلية، دون استبدال تصميم المستند المعتمد.
public static class MaintenanceCompletionReport
{
    #region تجهيز مصدر بيانات تقرير الإنهاء
    public static LocalReport Create(T_Emergency_MaintenanceCost order, DateTime? date, string? observer, string? notes)
    {
        var table = new DataTable("tb1");
        for (int i = 1; i <= 30; i++) table.Columns.Add("elm" + i, typeof(string));
        var choices = new MaintenanceOrderState();
        foreach (var line in order.T_Emergency_MaintenanceCosts_Services.OrderBy(x => x.ID))
        {
            var row = table.NewRow();
            row["elm27"] = order.T_Employe?.Emp_Name ?? "";
            row["elm6"] = date?.ToString("dddd", CultureInfo.GetCultureInfo("ar-LY")) ?? "";
            row["elm10"] = order.Emergency_MaintenanceCosts_No.ToString();
            row["elm12"] = order.Type is >= 0 and < 3 ? choices.DocumentTypes[order.Type.Value] : "";
            row["elm14"] = order.Branch?.Branch ?? App.Branch_Name ?? "";
            row["elm7"] = order.Location; row["elm8"] = order.T_Build?.Build ?? "";
            row["elm9"] = string.Join(" - ", order.T_Emp_Maints.OrderBy(x => x.id).Select(x => x.T_Employe?.Emp_Name));
            row["elm1"] = order.T_Service?.Serv_type ?? "";
            row["elm2"] = line.Maint_Type >= 0 && line.Maint_Type < choices.RepairActions.Length ? choices.RepairActions[line.Maint_Type] : "";
            row["elm3"] = line.Statement; row["elm4"] = line.T_Section?.Secti ?? ""; row["elm5"] = line.T_Manage?.Manag ?? "";
            row["elm20"] = date?.ToString("dd/MM/yyyy") ?? ""; row["elm25"] = observer ?? ""; row["elm26"] = notes ?? "";
            table.Rows.Add(row);
        }
        string path = Path.Combine(AppContext.BaseDirectory, "Reports", "Done_Mainte_Report.rdlc");
        if (!File.Exists(path)) throw new FileNotFoundException("قالب تقرير إنهاء الصيانة غير موجود.", path);
        var report = new LocalReport { ReportPath = path };
        report.DataSources.Add(new ReportDataSource("DataSet1", table.DefaultView)); return report;
    }
    #endregion

    #region عرض معاينة الطباعة
    // المعاينة داخل Flyout بالنافذة المالكة؛ تبقى بيانات وقالب التقرير كما هي.
    public static void ShowPreview(MahApps.Metro.Controls.MetroWindow owner, LocalReport report)
    {
        show_report f = new show_report();
        f.ShowPreview(owner, report, "طباعة تقرير إنهاء الصيانة");
        f.ShowDialog();
     
    }
    #endregion
}
