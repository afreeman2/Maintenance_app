using System.Windows;
using System.Windows.Input;
using MahApps.Metro.Controls;

namespace Maintenance_app.main_Windows;

public partial class AboutWindow : MetroWindow
{
    #region معلومات الإصدار وتهيئة التعريف بالمنظومة
    // يقرأ الإصدار من المصدر نفسه الذي تعرضه الرئيسية حتى تبقى معلومات النسخة متطابقة.
    public string VersionLabel => "الإصدار " + MainWindow.DisplayVersion;

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
    }
    #endregion

    #region أوامر إطار النافذة
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    // يتيح زر الإطار تغيير المساحة، بينما تعيد الاستعادة من شريط المهام عرض النافذة مكبرة.
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized) restoreMaximized = true;
        else if (restoreMaximized)
        {
            restoreMaximized = false;
            WindowState = WindowState.Maximized;
        }
        base.OnStateChanged(e);
    }

    private bool restoreMaximized;

    // السحب من الترويسة لتحريك النافذة، والنقر المزدوج يستخدم أمر التكبير نفسه.
    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not (System.Windows.Controls.Border or System.Windows.Controls.TextBlock)) return;
        if (e.ClickCount == 2) Maximize_Click(sender, e);
        else DragMove();
    }
    #endregion
}
