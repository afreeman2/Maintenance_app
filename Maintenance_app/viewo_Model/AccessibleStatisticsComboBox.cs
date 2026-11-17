using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Editors.Automation;
using System.Windows.Automation.Peers;

namespace Maintenance_app.viewo_Model;

public sealed class AccessibleStatisticsComboBox : ComboBoxEdit
{
    #region إتاحة قائمة الإحصائية وأزرارها
    // نبقي أنماط الاختيار والقيمة الأصلية، ونخصص فقط حاويات أزرار القالب الداخلية.
    protected override AutomationPeer OnCreateAutomationPeer() => new StatisticsComboBoxPeer(this);

    private sealed class StatisticsComboBoxPeer(ComboBoxEdit owner) : ComboBoxEditAutomationPeer(owner)
    {
        private readonly EditorButtonsAutomationPeers buttonPeers = new();

        protected override List<AutomationPeer> GetChildrenCore() => buttonPeers.Replace(base.GetChildrenCore());
    }
    #endregion
}
