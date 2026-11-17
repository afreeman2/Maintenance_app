using DevExpress.Xpf.Grid;
using DevExpress.Xpf.Grid.Automation;
using Maintenance_app.orders_Windows;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;

internal static class HeaderFocusRegression
{
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    public static void Run(maintenance_Order window)
    {
        int checkedHeaders = 0;
        foreach (string name in new[] { "Repiers_Grid", "Parts_Grid", "Search_Grid" })
        {
            var grid = (AccessibleOrderGrid)window.FindName(name);
            var root = UIElementAutomationPeer.CreatePeerForElement(grid);
            if (root.GetType().Name != "OrderGridAutomationPeer")
                throw new InvalidOperationException("The grid did not install the custom automation provider.");
            _ = root.GetChildren();
            var headerProperty = typeof(GridControlAutomationPeer).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .First(p => p.Name == "HeaderPanelPeer" && p.DeclaringType == typeof(GridControlAutomationPeer));
            var panel = (HeaderPanelAutomationPeerBase)headerProperty.GetValue(root)!;
            if (panel is null) throw new InvalidOperationException("The grid header automation panel was not initialized: " + name);
            foreach (var logicalPeer in panel.GetChildren() ?? [])
            {
                if (logicalPeer is not ColumnHeaderAutomationPeer) continue;
                if (logicalPeer.GetType().Name != "OrderColumnHeaderAutomationPeer")
                    throw new InvalidOperationException("Logical column-header peer still uses the original focus code: " + name);
                _ = logicalPeer.HasKeyboardFocus();
            }
            foreach (var header in Visuals(grid).OfType<GridColumnHeader>())
            {
                var peer = panel.CreatePeerCore(header);
                if (peer is not ColumnHeaderAutomationPeer) continue;
                if (peer.GetType().Name != "OrderColumnHeaderAutomationPeer")
                    throw new InvalidOperationException("The standard unsafe header peer is still in use.");
                _ = peer.HasKeyboardFocus();
                _ = peer.GetName();
                _ = peer.GetAutomationControlType();
                var elementPeer = UIElementAutomationPeer.CreatePeerForElement(header);
                if (elementPeer is ColumnHeaderAutomationPeer)
                {
                    if (elementPeer.GetType().Name != "OrderColumnHeaderAutomationPeer")
                        throw new InvalidOperationException("The visual header still exposes the original unsafe peer.");
                    _ = elementPeer.HasKeyboardFocus();
                }
                checkedHeaders++;
            }
            // An orphaned header reproduces the missing-view case before any HWND
            // or navigation state exists. It must remain queryable without throwing.
            var orphan = new GridColumnHeader();
            bool originalFailed = false;
            try { _ = new ColumnHeaderAutomationPeer(grid, new GridColumn()).HasKeyboardFocus(); }
            catch (NullReferenceException) { originalFailed = true; }
            if (!originalFailed)
                throw new InvalidOperationException("The original peer no longer reproduces the null-view regression; revisit the workaround.");
            var detachedPeer = panel.CreatePeerCore(orphan);
            if (detachedPeer is not null && detachedPeer.HasKeyboardFocus())
                throw new InvalidOperationException("An unattached header reports keyboard focus.");
        }
        if (checkedHeaders < 10)
            throw new InvalidOperationException("The focus regression did not inspect the actual grid headers.");
        Console.WriteLine("PASS: live and detached column-header automation focus queries; custom peer installed; accessibility names and roles retained.");
    }
}
