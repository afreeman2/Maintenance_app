using DevExpress.Xpf.Printing;
using DevExpress.XtraReports.UI;
using MahApps.Metro.Controls;
using Maintenance_app.Dashboard;
using Maintenance_app.orders_Windows;
using Microsoft.Reporting.WinForms;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Maintenance_app.Reports;

public partial class show_report : MetroWindow
{
    #region عرض التقرير في نافذة مستقلة وإدارة عمر المستند
    private LocalReport? rdlcReport;

    public show_report()
    {
        InitializeComponent();
        //ContentRendered += BringPreviewToFront;
    }

    // نُظهر الحوار فوق مالكه بعد اكتمال الرسم ثم نستعيد ترتيب النوافذ المعتاد.
    private void BringPreviewToFront(object? sender, EventArgs e)
    {
        ContentRendered -= BringPreviewToFront;
        if (Owner is not { IsVisible: true } owner) return;
        Topmost = true;
        try { Activate(); }
        finally { Topmost = owner.Topmost; }
    }

    // يحتفظ المستدعي بالتقرير حتى إغلاق الحوار، بينما تملك النافذة صور صفحات المعاينة.
    public  void ShowPreview(Window owner, LocalReport report, string title)
    {
        using var document = MaintenanceReportRenderer.CreateDocument(report);
        var window = new show_report { Owner = owner, Title = title, rdlcReport = report };
        window.PreviewViewer.Document = document.Document;
        try { /*window.ShowDialog();*/ }
        finally
        {
            //window.PreviewViewer.Document = null;
            //window.rdlcReport = null;
        }
    }

    // يعرض عارض DevExpress تقرير الإحصائية الأصلي بأوامر الشريط المخصصة له.
    internal static void ShowPreview(Window owner, XtraReport report, string title, DocumentCommandProvider provider)
    {
        var window = new show_report { Owner = owner, Title = title, Language = System.Windows.Markup.XmlLanguage.GetLanguage("ar-LY") };
        window.RdlcPreview.Visibility = Visibility.Collapsed;
        window.StatisticsViewer.Visibility = Visibility.Visible;
        window.StatisticsViewer.CommandProvider = provider;
        report.CreateDocument();
        window.StatisticsViewer.DocumentSource = report;
        try { window.ShowDialog(); }
        finally { window.StatisticsViewer.DocumentSource = null; }
    }
    #endregion

    #region التصدير وإغلاق المعاينة
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (rdlcReport is null) return;
        // التصدير من تقرير RDLC يحافظ على النص والبيانات الأصلية لكل صيغة.
        var (format, extension) = ExportFormats.SelectedIndex switch
        {
            1 => ("EXCELOPENXML", "xlsx"),
            2 => ("WORDOPENXML", "docx"),
            _ => ("PDF", "pdf")
        };
        var dialog = new SaveFileDialog
        {
            Title = "تصدير التقرير", FileName = Title, DefaultExt = "." + extension,
            Filter = $"{ExportFormats.SelectedItem} (*.{extension})|*.{extension}"
        };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(dialog.FileName, rdlcReport.Render(format)); }
        catch (Exception ex)
        {
            UiMessages.Show(this, "تعذّر تصدير التقرير", ex.GetBaseException().Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || ExportFormats.IsPopupOpen) return;
        e.Handled = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    #endregion
}
