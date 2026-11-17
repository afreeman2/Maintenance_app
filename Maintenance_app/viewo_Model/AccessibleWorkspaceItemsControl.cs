using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Maintenance_app.viewo_Model;

public sealed class AccessibleWorkspaceItemsControl : ItemsControl
{
    #region حاويات أوامر الرئيسية وإتاحتها لأدوات الوصول
    // يستخدم WPF الحاوية نفسها عند إعادة تدوير عنصر خرج من التبويب؛ يلزم أن تملك AutomationPeer صالحًا.
    protected override DependencyObject GetContainerForItemOverride() => new WorkspaceItemPresenter();

    private sealed class WorkspaceItemPresenter : ContentPresenter
    {
        // ContentPresenter الافتراضي لا ينشئ Peer، مما يترك RecyclableWrapper.Peer فارغًا بعد تصفية التبويب.
        protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    }
    #endregion
}
