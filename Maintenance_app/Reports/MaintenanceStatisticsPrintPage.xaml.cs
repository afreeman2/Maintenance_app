using System.Windows.Controls;

namespace Maintenance_app.reports;

public partial class MaintenanceStatisticsPrintPage : UserControl
{
    #region تهيئة صفحة التقرير
    // يُنشئ القالب الورقي وحده، ويستقبل بيانات الإحصائية من نافذة المعاينة.
    public MaintenanceStatisticsPrintPage() => InitializeComponent();
    #endregion
}
