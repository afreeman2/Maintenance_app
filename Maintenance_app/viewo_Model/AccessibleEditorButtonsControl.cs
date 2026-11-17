using DevExpress.Xpf.Editors;
using System.Windows;
using System.Windows.Automation.Peers;

namespace Maintenance_app.viewo_Model;

public sealed class AccessibleEditorButtonsControl : ButtonsControl
{
    #region حاويات أزرار المحرر المتاحة لأتمتة WPF
    // تستخدم الحاوية الأصلية سلوك الزر وربطه، وتضيف Peer للحاوية المؤقتة التي يعيد WPF تدويرها.
    protected override DependencyObject GetContainerForItemOverride() => new AccessibleButtonContainer();

    private sealed class AccessibleButtonContainer : ButtonContainer
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    }
    #endregion
}
