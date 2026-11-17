using DevExpress.Xpf.Editors;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Maintenance_app.viewo_Model;

// تشترك قوائم DevExpress ومحررات التاريخ في حاويات أزرار قد تنفصل أثناء تبديل القالب.
internal sealed class EditorButtonsAutomationPeers
{
    #region تثبيت أقران الأزرار مع حفظ بقية شجرة الوصول
    private readonly Dictionary<ItemsControl, EditorButtonsPeer> peers = [];

    internal List<AutomationPeer> Replace(List<AutomationPeer>? children)
    {
        if (children is null) return [];
        for (int index = 0; index < children.Count; index++)
        {
            if (children[index] is not ItemsControlAutomationPeer { Owner: ButtonsControl buttons }) continue;
            if (!peers.TryGetValue(buttons, out var peer))
            {
                peer = new EditorButtonsPeer(buttons);
                peers.Add(buttons, peer);
            }
            children[index] = peer;
        }
        return children;
    }

    private sealed class EditorButtonsPeer(ButtonsControl owner) : ItemsControlAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => "ItemsControl";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new EditorButtonPeer(item, this);
    }

    private sealed class EditorButtonPeer(object item, EditorButtonsPeer parent) : ItemAutomationPeer(item, parent)
    {
        // عند زوال الحاوية نعيد معرّفًا فارغًا بدل طلب Peer من حاوية WPF المعاد تدويرها.
        protected override string GetAutomationIdCore() => ContainerPeer()?.GetAutomationId() ?? string.Empty;
        protected override string GetNameCore() => ContainerPeer()?.GetName() ?? Item?.ToString() ?? string.Empty;
        protected override string GetClassNameCore() => "ItemsControlItem";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;

        private AutomationPeer? ContainerPeer()
        {
            if (ItemsControlAutomationPeer?.Owner is not ItemsControl owner || Item is not { } value ||
                owner.ItemContainerGenerator.ContainerFromItem(value) is not FrameworkElement container) return null;
            return UIElementAutomationPeer.CreatePeerForElement(container) ?? new FrameworkElementAutomationPeer(container);
        }
    }
    #endregion
}
