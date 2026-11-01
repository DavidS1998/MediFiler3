# Project status

**Snapshot date:** 2026-11-01  
**Overall:** Phase 1 in progress; steps 1.1 through 1.6 are implemented. Step 1.7 keyboard/mouse navigation and image zoom/pan are implemented; integration and manual verification remain.

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
- A compact persistent command bar provides the folder picker; the folder tree uses high-contrast text, full-width clickable rows, adjustable-through-style row padding, and a subtle narrow scrollbar.
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

1. Manually check zoom mode, zoom bounds and increments, panning, and mode reset on file changes on Windows 11.
2. Manually check full-resolution zoom sharpness, Numpad mode entry, zoom bounds and increments, panning, and immediate reset when returning to navigation mode.
3. Preserve the WinUI/WPF boundary and keep the rail permanently compact and programmatically hideable when adding future controls.

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
- The shell and tree presentation were manually checked by the user. The image viewer, carousel, Shell fallback, and navigation behavior have also been manually checked; zoom and panning await manual interaction checks.

## Outstanding decisions

- Step 1.7 boundary behavior is settled: stop at the first/last file, then move to the adjacent nonempty folder after five consecutive overscroll attempts in that direction.
- Zoom behavior is settled: centered 25% multiplicative steps, 20%–500% limits, mouse wheel and Numpad plus/minus in zoom mode (Numpad activates it automatically), and left-drag panning above fit-to-view. Switching to navigation resets zoom and pan. Decode resolution follows zoom level and is capped at the source image size.
- Carousel density is currently fixed at five neighboring files on each side and 150 x 150 WPF layout units; user-configurable values remain future settings work.

## Verification record

- `dotnet build MediFiler3.sln` on 2026-11-01 succeeded with 0 warnings and 0 errors.
- `dotnet test MediFiler3.sln --no-build --verbosity normal` exited 0, but no test projects exist in the solution, so no tests were discovered or executed.
- The user has manually verified the image viewer, 150 x 150 center-cropped carousel, thumbnail-to-full-image transition, Shell fallback, and navigation stage as sufficiently functional for this stage. Zoom/panning has been build-verified and still needs manual interaction checking on Windows 11.
- `dotnet build MediFiler3.sln` after navigation implementation on 2026-11-01 succeeded with 0 warnings and 0 errors.
- A standard build could not overwrite the running app's locked output. `dotnet build MediFiler3.sln -p:BaseOutputPath=C:\Users\xofas\AppData\Local\Temp\MediFiler3Build\` succeeded with 0 warnings and 0 errors; zoom remains pending manual verification.
