using Microsoft.Reporting.WinForms;
using System.IO;

namespace Maintenance_app.orders_Windows;

public static class MaintenanceReportRenderer
{
    #region تحويل صفحات RDLC الأصلية إلى مستند WPF قابل للطباعة
    // 300 نقطة في البوصة تحفظ تفاصيل النص عند الطباعة، مع نفس أبعاد وتنسيق قالب RDLC.
    public static MaintenanceReportDocument CreateDocument(LocalReport report)
    {
        const double dpi = 300;
        var streams = new List<MemoryStream>();
        var document = new MaintenanceReportDocument();
        try
        {
            report.Render("IMAGE", "<DeviceInfo><OutputFormat>PNG</OutputFormat><DpiX>300</DpiX><DpiY>300</DpiY></DeviceInfo>",
                (name, extension, encoding, mime, seek) => { var stream = new MemoryStream(); streams.Add(stream); return stream; }, out _);
            foreach (var stream in streams)
            {
                document.AddPage(stream, dpi);
            }
            if (document.Document.Pages.Count == 0) throw new InvalidOperationException("لم ينتج قالب التقرير صفحات للمعاينة.");
            return document;
        }
        catch { document.Dispose(); throw; }
        finally { foreach (var stream in streams) stream.Dispose(); }
    }
    #endregion
}
