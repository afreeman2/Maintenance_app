using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Editors.Automation;
using System.Windows.Automation.Peers;

namespace Maintenance_app.viewo_Model;

public sealed class AccessibleDateEdit : DateEdit
{
    #region إتاحة التاريخ مع حماية أزرار القالب
    // يبقى قرين DevExpress الأصلي مسؤولًا عن القيمة والتقويم وأنماط الوصول.
    protected override AutomationPeer OnCreateAutomationPeer() => new AccessibleDatePeer(this);

    private sealed class AccessibleDatePeer(DateEdit owner) : DateEditAutomationPeer(owner)
    {
        private readonly EditorButtonsAutomationPeers buttonPeers = new();
        protected override List<AutomationPeer> GetChildrenCore() => buttonPeers.Replace(base.GetChildrenCore());
    }
    #endregion
}
