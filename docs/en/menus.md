# Menus

[简体中文](../zh-CN/menus.md)

Add application actions to the context menu. Retain a registration for the feature lifetime; the Viewer borrows your menu object.

## Register an action

The example assumes a live `viewer`:

```csharp
using var registration = viewer.RegisterMenuItem(
    new Fizzy.ImageViewer.Menus.MenuItem("Mark ready", () => viewer.HudLabelText = "Ready"));
```

Implement `IMenuItem` for custom behavior, or use the action helpers. Helpers accept `Action` or `Func<ValueTask>`; return asynchronous work so its completion and failure are observed.

## Visibility and captured targets

Menu visibility is determined by `IMenuItem.IsVisible`, evaluated on each opening.
Action-based helpers accept an optional visibility predicate. Use `SeparatorMenuItem`
for separators; the renderer removes leading, repeated and trailing separators.
`MenuItem` and `CheckableMenuItem` require a nonempty, non-whitespace header and
non-null action (and checked-state getter where applicable); constructors reject
invalid arguments before registration. Valid headers are preserved as supplied.
Built-in interaction actions capture their target when the menu opens. Cancel Measurement
only cancels that activation; a replacement activation, including the same tool, is unaffected.

## Menu registration lifetime

`RegisterMenuItem(IMenuItem)` returns an `IDisposable` registration handle. Dispose it from
any thread to revoke only that registration; disposal is idempotent and safe after viewer
closure. Registering the same object twice creates independent registrations. Ignoring the
handle keeps the registration alive until the viewer closes. The viewer never disposes the
caller-owned menu object itself.

Revocation prevents new callbacks from an already open menu and disables its current item;
the next opening omits it. An executing action is allowed to finish, including when it
revokes itself or the viewer closes. `IMenuItem.ExecuteAsync()` returns a `ValueTask`
and starts on the viewer STA; no WPF event objects cross this interface. Bindings await
completion and log failures. The same menu object cannot execute again while its action
is in flight, including after reopening; other actions remain available. Action helpers
accept either `Action` or `Func<ValueTask>`. Async work that outlives the viewer must own
its resources and avoid depending on the viewer dispatcher after it closes.

Visibility/check callbacks run on the viewer STA and may change registrations;
registrations added while building the menu appear on the next opening. Do not block these
callbacks on another thread that is waiting for a viewer operation.

Normal menu closure preserves the pending click until input drains. Explicit revocation or
viewer shutdown invalidates it immediately. If menu construction fails after freezing a
frame, the viewer resumes submissions, removes partial bindings and closes that opening.
Header, visibility and check-state failures are logged at the WPF event boundary; they do
not escape into the dispatcher. A later opening evaluates registrations again.
Viewer shutdown releases registrations, callbacks and WPF menu bindings.

## Captured image targets

Built-in freeze/export actions retain the committed frame, mapping and applicable ROI captured for that opening. Independent leases let accepted work finish after closure; export never switches to a new frame. Custom asynchronous actions must explicitly choose and own targets. Borrowing Viewer does not promise availability after closure. See [snapshots](snapshots.md).
