using DevExpress.Xpf.Grid;
using DevExpress.Xpf.Grid.Automation;
using System.Windows.Automation.Peers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Maintenance_app.orders_Windows
{
    /// <summary>
    /// Keeps the DevExpress automation tree and patterns, but avoids 26.1.6's
    /// ColumnHeaderAutomationPeer.HasKeyboardFocusCore null dereference while
    /// a header is being created or disconnected.
    /// </summary>
    public class AccessibleOrderGrid : GridControl
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new OrderGridAutomationPeer(this);
    }

    internal sealed class OrderGridAutomationPeer(GridControl owner) : GridControlAutomationPeer(owner)
    {
        protected override HeaderPanelAutomationPeerBase CreateHeaderPeer() => new OrderHeaderPanelAutomationPeer(owner);
    }

    internal sealed class OrderHeaderPanelAutomationPeer(GridControl owner) : HeaderPanelAutomationPeer(owner)
    {
        protected override List<AutomationPeer> GetChildrenCore()
        {
            var children = base.GetChildrenCore();
            // DevExpress creates logical peers from columns even when no visual
            // header exists (for example in the closed search overlay).
            foreach (var entry in HeaderPeers.ToArray())
                if (entry.Value is ColumnHeaderAutomationPeer peer && peer is not OrderColumnHeaderAutomationPeer)
                    HeaderPeers[entry.Key] = new OrderColumnHeaderAutomationPeer(owner, peer.Column);
            for (int i = 0; i < children.Count; i++)
                if (children[i] is ColumnHeaderAutomationPeer peer && peer is not OrderColumnHeaderAutomationPeer)
                    children[i] = HeaderPeers.Values.OfType<OrderColumnHeaderAutomationPeer>()
                        .FirstOrDefault(cached => ReferenceEquals(cached.Column, peer.Column))
                        ?? new OrderColumnHeaderAutomationPeer(owner, peer.Column);
            return children;
        }

        protected override ColumnHeaderAutomationPeerBase CreateColumnHeaderAutomationPeer(GridColumnHeader header) =>
            new OrderColumnHeaderAutomationPeer(owner, header);
    }

    internal sealed class OrderColumnHeaderAutomationPeer : ColumnHeaderAutomationPeer
    {
        private readonly GridControl grid;
        private readonly GridColumnHeader? header;

        public OrderColumnHeaderAutomationPeer(GridControl grid, GridColumnHeader header) : base(grid, header)
        {
            this.grid = grid;
            this.header = header;
        }
        public OrderColumnHeaderAutomationPeer(GridControl grid, GridColumn column) : base(grid, column)
        {
            this.grid = grid;
        }
        // Query the element itself instead of dereferencing the view's navigation
        // state. Detached and unloaded headers cannot own keyboard focus.
        protected override bool HasKeyboardFocusCore()
        {
            if (header is not null) return header.IsLoaded && header.IsKeyboardFocusWithin;
            if (!grid.IsLoaded || !grid.IsKeyboardFocusWithin) return false;
            for (DependencyObject? element = Keyboard.FocusedElement as DependencyObject;
                element is Visual; element = VisualTreeHelper.GetParent(element))
                if (element is GridColumnHeader focusedHeader)
                    return focusedHeader.IsLoaded && ReferenceEquals(BaseGridHeader.GetGridColumn(focusedHeader), Column);
            return false;
        }
    }
}
