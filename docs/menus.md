# Menus

[简体中文](zh-CN/menus.md)

Implement `IMenuItem` and retain the disposable from `RegisterMenuItem`. Viewer borrows the menu object. Disposing registration revokes it without disposing the object; handles support repeated disposal from any thread, even after Viewer closure.

## Opening and execution

`Header`, `IsVisible` and checked state are read on the viewer STA for each opening. State-read failure cancels that opening and is logged. `ExecuteAsync` starts on the STA; return its work rather than launching an unobserved task. Execution exceptions are logged and isolated. The same menu object cannot execute concurrently across openings.

WPF Closed may precede Click, so ordinary closure retains bindings until input drains. Reopening or revocation invalidates old bindings. Started work may outlive the menu or Viewer and must own its dependencies.

## Captured targets

Built-in freeze/export actions retain the committed frame, mapping and applicable ROI captured for that opening. Independent leases let accepted work finish after closure; export never switches to a new frame. An old cancel action cannot cancel a replacement measurement activation.

Custom asynchronous actions must explicitly choose and own targets. Borrowing Viewer does not promise arbitrary availability after closure. See [snapshots](snapshots.md) and the [registration example](public-api.md#menu-registration-lifetime).
