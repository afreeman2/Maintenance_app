# Message dialog automation regression

## Reported error

`NullReferenceException` in `System.Windows.Automation.Peers.ItemAutomationPeer.GetAutomationIdCore()` while opening `UiMessages.Display`'s `ThemedMessageBox`.

## Reproduction and cause

The regression probe loads the real application resources and main window with the application's existing `None` theme. It creates and retains automation peers before showing a message, then queries their IDs after the modal message closes.

With the original `Display` implementation this reproduced the exact exception on a cached `System.Windows.Automation.Peers.ItemsControlItemAutomationPeer`. `Display` changed `ApplicationThemeHelper.ApplicationThemeName` and the owner's attached theme immediately before opening the modal window. Those changes rebuilt existing item containers and detached their automation peers.

## Change

`UiMessages.Display` no longer changes the application or live owner theme. The message body retains its local `Win11Light` theme. The existing `ThemedMessageBox`, Arabic captions, icons, RTL alignment, ownership, placement and result mapping remain in place. No global exception suppression or accessibility disabling was introduced.

## Validation

`tests/MaintenanceOrderPreview/MessageAutomationProbe.cs` checks:

- Real main-window resources with the `None` theme.
- Informational messages, operation confirmations and a confirmation from the owner's `Closing` event.
- Native message-button invocation through UI Automation.
- Automation IDs and names while dialogs are live, and IDs of cached peers after dialogs close.
- Application and owner themes remain unchanged.
- Canceling the close confirmation keeps the main window visible.

The regression failed on the previous implementation with the reported stack and passes after the change. The production project builds successfully. No database writes are involved.
