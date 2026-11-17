using Maintenance_app.Models;
using Maintenance_app.Reports;
using Microsoft.Reporting.WinForms;
using System.Data;
using System.Globalization;
using System.IO;

namespace Maintenance_app.orders_Windows;

/// <summary>Feeds the original RDLC layouts with the original DataSet1/DataSet2 field names.</summary>
public static class MaintenanceOrderReport
{
    #region تجهيز بيانات قالب تكليف الصيانة الأصلي
    public static LocalReport Create(T_Emergency_MaintenanceCost order, string inspectorName)
    {
        DataTable Table()
        {
            var table = new DataTable("tb1");
            for (int i = 1; i <= 30; i++) table.Columns.Add("elm" + i, typeof(string));
            return table;
        }
        var details = Table();
        var parts = Table();
        var technicians = order.T_Emp_Maints.OrderBy(x => x.id).Select(x => x.T_Employe.Emp_Name).ToList();
        var state = new MaintenanceOrderState();
        var culture = CultureInfo.GetCultureInfo("ar-LY");
        foreach (var line in order.T_Emergency_MaintenanceCosts_Services.OrderBy(x => x.ID))
        {
            var row = details.NewRow();
            row["elm27"] = inspectorName;
            row["elm6"] = order.Date_com.ToString("dddddd", culture);
            row["elm10"] = order.Emergency_MaintenanceCosts_No.ToString(CultureInfo.InvariantCulture);
            row["elm12"] = order.Type is >= 0 and < 3 ? state.DocumentTypes[order.Type.Value] : string.Empty;
            row["elm14"] = order.Branch?.Branch ?? App.Branch_Name ?? string.Empty;
            row["elm7"] = order.Location;
            row["elm8"] = order.T_Build.Build;
            row["elm9"] = string.Join(" , ", technicians);
            row["elm30"] = technicians.Count > 1 ? "السادة:" : "السيد:";
            row["elm29"] = technicians.Count > 1 ? "التابعون " : "التابع ";
            row["elm1"] = order.T_Service.Serv_type;
            row["elm2"] = line.Maint_Type is >= 0 and < 4 ? state.RepairActions[line.Maint_Type] : string.Empty;
            row["elm3"] = line.Statement;
            row["elm4"] = line.T_Section?.Secti ?? string.Empty;
            row["elm5"] = line.T_Manage?.Manag ?? string.Empty;
            row["elm20"] = order.Date_com.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            details.Rows.Add(row);
        }
        int number = 1;
        foreach (var part in order.T_MaintenanceCosts_Parts.OrderBy(x => x.ID))
        {
            var row = parts.NewRow();
            row["elm1"] = (number++).ToString(CultureInfo.InvariantCulture);
            row["elm2"] = part.Part;
            decimal quantity = decimal.Round(part.Quantity, 3);
            row["elm3"] = quantity.ToString(quantity == decimal.Truncate(quantity) ? "#,0" : "#,0.000", CultureInfo.CurrentCulture) + "  "
                + (part.Unit < state.Units.Length ? state.Units[part.Unit] : string.Empty);
            parts.Rows.Add(row);
        }
        string template = technicians.Count > 1 ? "Mainte_Report _milit.rdlc" : "Mainte_Report.rdlc";
        string path = Path.Combine(AppContext.BaseDirectory, "Reports", template);
        if (!File.Exists(path)) throw new FileNotFoundException("ملف تقرير تكليف الصيانة غير موجود.", path);
        var report = new LocalReport { ReportPath = path };
        report.DataSources.Add(new ReportDataSource("DataSet1", details.DefaultView));
        report.DataSources.Add(new ReportDataSource("DataSet2", parts.DefaultView));
        return report;
    }

    #endregion

    #region عرض معاينة الطباعة
    // المعاينة داخل Flyout بالنافذة المالكة؛ تبقى بيانات وقالب التقرير كما هي.
    public static void ShowPreview(MahApps.Metro.Controls.MetroWindow owner, LocalReport report){
        show_report f = new show_report();
        f.ShowPreview(owner, report, "طباعة تقرير إنهاء الصيانة");
        f.ShowDialog();
    }
    #endregion
}
