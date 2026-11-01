# Project status

**Snapshot date:** 2026-11-01  
**Overall:** Phase 1 in progress; steps 1.1 through 1.4 are implemented. Image viewing has not started.

## Current implementation

- .NET 10 / C# WPF application targeting `net10.0-windows10.0.19041.0`; product target is 64-bit Windows 11.
- Solution projects: `MediFiler3.App`, `MediFiler3.Application`, `MediFiler3.Domain`, and `MediFiler3.Infrastructure` under `src/`.
- The WPF app is the composition root and uses `Microsoft.Extensions.Hosting` for dependency injection and logging.
- Main shell switches between WPF Home and Browse views. Home remains a placeholder. Browse supports adding multiple folder roots through a multi-select folder picker or by dropping folders onto the window; each root retains its own hierarchy.
- Browse replaces the current folder roots on each new load and recursively enumerates the selected hierarchies in the background. The tree starts expanded, omits expanders for empty folders, skips junctions and symbolic links, and reports inaccessible paths.
- Selecting a folder asynchronously enumerates its immediate non-hidden, non-system files. The list uses Windows Explorer natural filename ordering; selecting a file updates shared navigation state and the native title bar to `(position/total) filename`.
- A compact persistent command bar provides the folder picker; the folder tree uses high-contrast text, full-width clickable rows, adjustable-through-style row padding, and a subtle narrow scrollbar.
- The Home/Browse rail is a WinUI `NavigationView` hosted by a WPF `HwndHost` through Windows App SDK XAML Islands. It is always compact/icon-only; the pane expand/collapse control is disabled. `MainWindow.IsSidebarVisible` can hide the whole rail for future fullscreen mode.
- WPF retains the native Windows title bar. `TitleBarBackground` and `TitleBarForeground` dependency properties apply runtime caption/text colors through DWM; defaults are black and white.
- App project references `Microsoft.WindowsAppSDK` 2.6.4 and is configured unpackaged/framework-dependent with automatic bootstrap initialization. The Windows App Runtime 2.6.4 must be installed on deployment computers.
- WinUI initialization, resources, message pre-translation, island sizing/focus, and shutdown are handled in `src/MediFiler3.App/Interop/`.
- Image viewing and filesystem mutations have not yet been implemented. No test projects or automated tests currently exist in the solution.

## Completed work

- **1.1 Solution structure and startup:** project boundaries, host-based DI/logging, WPF startup and host shutdown.
- **1.2 Main window and shell:** dark high-contrast shell, Home/Browse switching, WinUI `NavigationView` in an island, native Windows caption with runtime colors, permanently compact rail, and a full-hide property for future fullscreen mode.
- **1.3 Folder roots and hierarchy:** multi-select folder picker, folder drag and drop, separate loaded roots, asynchronous recursive folder enumeration, reset-on-load context, cancellation of superseded work, and inaccessible-folder reporting.
- **1.4 File enumeration and navigation state:** immediate-folder file listings, hidden/system filtering, Explorer-style natural sorting, shared active-folder/current-file state, first-file selection, and title-bar position display.
- Specification and architecture decisions are recorded in `MediFiler3/SPECIFICATION.md` and `MediFiler3/ARCHITECTURE.md`.

## Immediate next tasks

1. Manually check folder selection, direct-child file enumeration, hidden/system filtering, natural sorting, and title-bar position updates on Windows 11.
2. Start Phase 1.5: display common images at fit-to-view, preserve aspect ratio, and release viewer resources.
3. Before adding additional island controls, preserve the WinUI/WPF boundary and ensure the rail remains permanently compact and fully hideable programmatically.

## Known caveats and risks

- XAML Islands use a native child HWND. The app must keep the Windows App SDK dispatcher/XAML environment alive until all islands are disposed and must pre-translate input messages before WPF dispatches them.
- Current WinUI control creation is programmatic in the WPF interop layer; no separate WinUI class library is present because no custom hosted WinUI XAML has been added.
- Current deployment is framework-dependent and unpackaged, so a target machine without the Windows App Runtime 2.6.4 will fail at startup. Self-contained deployment has not been selected.
- DWM caption color attributes are Windows 11-specific, matching the supported OS decision. Failures are logged; there is no separate fallback title bar.
- The navigation rail currently has no user-facing full-hide command. `IsSidebarVisible` is the intended integration point for future fullscreen behavior.
- The folder hierarchy is scanned recursively before it is displayed, on a background worker with cancellation. Very large trees may take time to appear; dropped files are ignored with a status message.
- Tree item virtualization is disabled so the expanded hierarchy has a stable scrollbar extent; extremely large trees will use more UI memory until a stable virtualized scrolling strategy is added.
- The initial shell appearance was manually checked by the user. The new folder loading and tree expansion flows still need a manual interaction check.

## Verification record

- `dotnet build MediFiler3.sln` after step 1.4 succeeded on 2026-11-01 with 0 warnings and 0 errors.
- `dotnet build MediFiler3.sln` — succeeded on 2026-11-01 with 0 warnings and 0 errors.
- `dotnet test MediFiler3.sln --no-build --verbosity normal` — exited successfully, but the solution has no test projects, so no tests were executed.
- Step 1.4 file selection, sorting, filtering, and title-bar updates have not been manually verified in this handover.
