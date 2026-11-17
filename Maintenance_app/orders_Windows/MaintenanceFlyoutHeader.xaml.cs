using MahApps.Metro.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Maintenance_app.orders_Windows;

public partial class MaintenanceFlyoutHeader : UserControl
{
    #region تحميل التصميم وإغلاق اللوحة المالكة
    // عناصر الترويسة وألوانها وأبعادها في XAML؛ الحدث يغيّر حالة الفتح فقط دون اعتماد المسودة.
    public MaintenanceFlyoutHeader() => InitializeComponent();

    private void CloseFlyout_Click(object sender, RoutedEventArgs e)
    {
        for (DependencyObject? element = this; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is Flyout flyout) { flyout.IsOpen = false; break; }
    }
    #endregion
}
