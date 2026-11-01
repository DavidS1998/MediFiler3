# Project status

**Snapshot date:** 2026-11-01  
**Overall:** Phase 1 in progress; steps 1.1 through 1.7 are implemented. The user has reviewed the UI; the latest usability refinements build successfully and await a focused interaction check. Step 1.8 integration work remains.

## Handover: resume here

The latest requested usability fixes are implemented. Run the app to check stable carousel scrolling and clicks, tree expander states and bottom clearance, sidebar item alignment, the WinUI Settings slider, and the native CommandBar structure. The latest build succeeded using a temporary output path. No code work is currently blocked.

1. Verify pointer-anchored zoom and the native CommandBar button placement, including the combined fit/percentage button.
2. Verify carousel wheel scrolling is smooth and tiles remain clickable during recentering; check tree row/expander visuals and the 48-pixel bottom reserve.
3. Check consistent Home/Browse sidebar placement and the Settings footer destination. Adjust the tick-marked WinUI slider through odd total counts from 1 to 25 and confirm the carousel updates immediately; the setting is currently session-only.
4. Fix any issues found, complete Step 1.8 integration/refinement, and update this handover. Phase 2 begins with the persistent operation journal before filesystem mutations.

## Current implementation

- .NET 10 / C# WPF application targeting `net10.0-windows10.0.19041.0`; product target is 64-bit Windows 11.
- Solution projects: `MediFiler3.App`, `MediFiler3.Application`, `MediFiler3.Domain`, and `MediFiler3.Infrastructure` under `src/`.
- The WPF app is the composition root and uses `Microsoft.Extensions.Hosting` for dependency injection and logging.
- Main shell switches between WPF Home and Browse views. Home remains a placeholder. Browse supports adding multiple folder roots through a multi-select folder picker or by dropping folders onto the window; each root retains its own hierarchy.
- Browse replaces the current folder roots on each new load and recursively enumerates the selected hierarchies in the background. The tree starts expanded, omits expanders for empty folders, skips junctions and symbolic links, and reports inaccessible paths.
- Selecting a folder asynchronously enumerates its immediate non-hidden, non-system files. The list uses Windows Explorer natural filename ordering; selecting a file updates shared navigation state and the native title bar to `(position/total) filename`.
- The Browse viewer displays JPEG, PNG, BMP, GIF, and TIFF images fit to the available viewport using WPF/WIC. Decode is bounded by viewport pixels at fit-to-view and scales with zoom level up to the source image resolution, preserves aspect ratio, freezes sources for cross-thread use, cancels stale loads, and releases the previous source when selection changes. Unsupported or unreadable files show an explanatory message.
- The carousel shows the current file and up to five neighbors on either side in 150 x 150 square tiles, filling each square with a center-focused crop. The shared asynchronous thumbnail service uses WIC for supported images, Windows Shell thumbnails for other file types, placeholders when no thumbnail exists, a 32 MB LRU memory cache, and a disk cache keyed by normalized path, file size, last-write time, and dimensions.
- Browse navigation supports left/right and up/down arrow keys and mouse-wheel notches over the main image viewport. It stops at the first/last file; after five consecutive attempts past the same edge it advances to the next nonempty folder in loaded tree preorder. Reversing direction or successfully selecting a file resets the overscroll counter.
- For supported images, right-click toggles between file navigation and zoom interaction. In zoom mode, mouse-wheel and Numpad plus/minus zoom by 25% multiplicative steps within 20%–500%; Numpad zoom enters zoom mode automatically. Each zoom level requests a decode at the matching viewport pixel scale, capped at source resolution. Left-drag pans above fit-to-view. Zoom and pan reset when switching back to file navigation or changing files.
- When changing files, the viewer shows the selected carousel thumbnail immediately when available, then replaces it with the full image after decoding.
- Unsupported files use their Shell thumbnail in the main viewer when available, with an explicit thumbnail-preview label.
- Three 48-pixel WinUI CommandBars share a full-window-width XAML Island above the sidebar and media canvas; they align left, center, and right with overflow buttons hidden. The left section shows folder context, the center Add AppBarButton loads folders, and the right section has zoom-out, reset-to-fit with the zoom percentage, and zoom-in controls. The redundant current filename and inline viewer zoom badge are removed.
- The toolbar uses native CommandBar primary commands aligned left/center/right, with 32-pixel AppBarButtons and the reset-to-fit button grouped with its zoom-percent readout, following the layout pattern in the user-referenced MediFiler-V2 `MainWindow.xaml`.
- Zoom-wheel steps preserve the image point under the mouse pointer. The dedicated Settings footer tab hosts a discrete, tick-marked WinUI slider for odd total carousel icon counts from 1 to 25, default 11; this includes the current item, advances by two per slider step, applies immediately, and is not persisted yet.
- The tree and carousel float over the WPF media canvas with translucent backgrounds. The tree starts hidden; the carousel leaves its upper quarter peeking above the bottom edge with a transparent gradient hint. Hovering at the left/bottom edges slides the corresponding panel out; leaving the panel hides the tree or returns the carousel to its peek state.
- The scrollbar is hidden while horizontal scrolling remains available, including mouse-wheel file navigation over the carousel. Leading/trailing spacers represent the omitted folder items, so global scroll offsets animate recentering across the entire folder. Adjacent navigation reuses thumbnail items and keeps async thumbnail work alive to avoid flicker. Thumbnails are center-cropped, spacing is compact, names stay on one line, and item hover/selection visuals are suppressed in favor of a pointer cursor.
- Folder tree rows stretch across the overlay width and use full-row hover/selection backgrounds. The chevron keeps the same shape and rotates when expanded, with a transparent hit area and no default blue open state. A 48-pixel bottom reserve gives the carousel more of the screen while allowing the final tree entries to scroll clear of it.
- Carousel hit testing is disabled only while its panel slides in or out; it remains active during horizontal recentering so clicked tile identity remains tied to the clicked file.
- The sidebar uses consistent NavigationView item spacing without a per-item negative margin, avoiding a clipped Home entry.
- The Home/Browse rail is a WinUI `NavigationView` hosted by a WPF `HwndHost` through Windows App SDK XAML Islands. It is always compact/icon-only; the pane expand/collapse control is disabled. `MainWindow.IsSidebarVisible` can hide the whole rail for future fullscreen mode.
- WPF retains the native Windows title bar. `TitleBarBackground` and `TitleBarForeground` dependency properties apply runtime caption/text colors through DWM; defaults are black and white.
- App project references `Microsoft.WindowsAppSDK` 2.6.4 and is configured unpackaged/framework-dependent with automatic bootstrap initialization. The Windows App Runtime 2.6.4 must be installed on deployment computers.
- WinUI initialization, resources, message pre-translation, island sizing/focus, and shutdown are handled in `src/MediFiler3.App/Interop/`.
- Filesystem mutations have not yet been implemented. No test projects or automated tests currently exist in the solution.

## Completed work

- **1.1 Solution structure and startup:** project boundaries, host-based DI/logging, WPF startup and host shutdown.
- **1.2 Main window and shell:** dark high-contrast shell, Home/Browse switching, WinUI `NavigationView` in an island, native Windows caption with runtime colors, permanently compact rail, and a full-hide property for future fullscreen mode.
- **1.3 Folder roots and hierarchy:** multi-select folder picker, folder drag and drop, separate loaded roots, asynchronous recursive folder enumeration, reset-on-load context, cancellation of superseded work, and inaccessible-folder reporting.
- **1.4 File enumeration and navigation state:** immediate-folder file listings, hidden/system filtering, Explorer-style natural sorting, shared active-folder/current-file state, first-file selection, and title-bar position display.
- **1.5 Image viewer:** viewport-fitted WPF/WIC image display, bounded decode dimensions, aspect-ratio preservation, cancellation and stale-result checks, source freezing and release, and unsupported/unreadable-file messaging.
- **1.6 Thumbnails and carousel:** bounded asynchronous thumbnail generation, Shell thumbnail fallback for unsupported formats, 150-square carousel tiles, loading placeholders, memory/disk caching, and centered current-file context.
- **1.7:** arrow-key and image-viewport wheel file navigation; five same-direction boundary overscroll attempts advance to the adjacent nonempty folder in loaded tree preorder. Supported-image right-click interaction toggle, Numpad/mouse-wheel zoom from 20%–500%, and drag panning are implemented.
- Specification and architecture decisions are recorded in `MediFiler3/SPECIFICATION.md` and `MediFiler3/ARCHITECTURE.md`.

## Immediate next tasks

1. Check pointer-centered wheel zoom and native CommandBar placement and hit targets.
2. Check that carousel scrolling no longer flickers and tile clicks navigate during recentering; check tree chevrons, 48-pixel bottom clearance, sidebar item alignment, and WinUI Settings slider count/update behavior.
3. Recheck long-folder recentering and existing full-resolution zoom, Numpad mode entry, bounds, panning, and reset behavior.

## Known caveats and risks

- XAML Islands use a native child HWND. The app must keep the Windows App SDK dispatcher/XAML environment alive until all islands are disposed and must pre-translate input messages before WPF dispatches them.
- Current WinUI control creation is programmatic in the WPF interop layer; no separate WinUI class library is present because no custom hosted WinUI XAML has been added.
- Current deployment is framework-dependent and unpackaged, so a target machine without the Windows App Runtime 2.6.4 will fail at startup. Self-contained deployment has not been selected.
- DWM caption color attributes are Windows 11-specific, matching the supported OS decision. Failures are logged; there is no separate fallback title bar.
- The navigation rail currently has no user-facing full-hide command. `IsSidebarVisible` is the intended integration point for future fullscreen behavior.
- The folder hierarchy is scanned recursively before it is displayed, on a background worker with cancellation. Very large trees may take time to appear; dropped files are ignored with a status message.
- Tree item virtualization is disabled so the expanded hierarchy has a stable scrollbar extent; extremely large trees will use more UI memory until a stable virtualized scrolling strategy is added.
- The disk thumbnail cache currently has no size or age-based pruning policy; the in-memory cache is limited to 32 MB and 512 entries.
- Shell thumbnails depend on the file type's installed Windows Shell thumbnail provider. The service requests thumbnail-only results and uses a placeholder when no thumbnail is available.
- Animated GIF playback is not implemented; current GIF viewing uses the first frame. WebP is not currently supported. Both are required for a future version.
- The user has manually reviewed the interface before the latest refinements. Carousel item reuse/click routing, the tree chevron and smaller reserve, consistent sidebar spacing, the WinUI slider host, and native CommandBar structure need focused manual verification.

## Settled interaction behavior and deferred settings

- Step 1.7 boundary behavior is settled: stop at the first/last file, then move to the adjacent nonempty folder after five consecutive overscroll attempts in that direction.
- Zoom behavior is settled: centered 25% multiplicative steps, 20%–500% limits, mouse wheel and Numpad plus/minus in zoom mode (Numpad activates it automatically), and left-drag panning above fit-to-view. Switching to navigation resets zoom and pan. Decode resolution follows zoom level and is capped at the source image size.
- Carousel density defaults to five neighboring files per side, configurable by total icon counts 1, 3, ..., 25 in Settings. The value is session-only until persistence is implemented.

## Verification record

- `dotnet test MediFiler3.sln --no-build --verbosity normal` exited 0, but no test projects exist in the solution, so no tests were discovered or executed.
- Before the latest interface revisions, the user verified the image viewer, original carousel thumbnails and transitions, Shell fallback, and file navigation as sufficient for the current stage.
- Latest verification: `dotnet build MediFiler3.sln -p:BaseOutputPath=C:\Users\xofas\AppData\Local\Temp\MediFiler3UiTune\` succeeded with 0 warnings and 0 errors. `git diff --check` found no whitespace errors. The latest refinements have not yet been manually verified in the running app.
