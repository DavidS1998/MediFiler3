# Architecture and decisions

This document records the architecture direction for MediFiler3. Product requirements remain in [SPECIFICATION.md](SPECIFICATION.md), and implementation status and the phase plan are tracked in [PROJECT_STATUS.md](PROJECT_STATUS.md) and [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md).

## Platform

- C# and WPF on `net10.0-windows`.
- Supported platform: 64-bit Windows 11.
- Use MVVM for presentation and keep filesystem and domain logic independent of WPF.
- The WPF application is the composition root and owns UI dispatching.
- Keep WPF as the host framework. Use WinUI controls through Windows App SDK XAML Islands selectively when they provide meaningful Windows-native behavior or consistency; do not migrate the application shell to WinUI.
- Prefer the native Windows caption for the WPF top-level window. Set its caption and text colors at runtime through the Windows 11 DWM attributes, defaulting to black and white. WinUI `ExtendsContentIntoTitleBar` and `SetTitleBar` apply to a WinUI top-level `Window`, not to this WPF host.

## Project boundaries

Start with a small set of projects rather than one assembly per feature:

```text
MediFiler3.sln
src/
  MediFiler3.App/             WPF startup, views, view models, UI resources
  MediFiler3.WinUI/           Optional WinUI class library for custom hosted XAML controls (add when needed)
  MediFiler3.Application/     Use cases, navigation state, workflows, contracts
  MediFiler3.Domain/          File/folder models, sorting policy, operation records
  MediFiler3.Infrastructure/  Filesystem, Windows shell, SQLite, watcher, media adapters
tests/
  MediFiler3.Domain.Tests/
  MediFiler3.Application.Tests/
  MediFiler3.Infrastructure.Tests/
```

The application layer describes operations such as loading a folder, moving files, and undoing an operation. Infrastructure implements those contracts. Keep file handlers behind capability-focused interfaces so the main viewer does not accumulate format-specific logic.

The browsing session state owns loaded roots, the active folder, its sorted file list, and the current file and one-based position. Selecting a folder loads only its direct files asynchronously, excludes Windows Hidden and System attributes, and uses Windows logical filename ordering. The native WPF title displays `(position/total) filename` while a file is active.

The initial image viewer uses WPF/WIC for JPEG, PNG, BMP, GIF, and TIFF. Decode to the viewport's pixel bounds while setting only one decode dimension to preserve aspect ratio; cap decoding at source resolution, freeze the resulting bitmap, and release it when the active file changes. Initial GIF display uses its first frame; animated GIF and WebP support are required future capabilities.

The thumbnail service returns encoded PNG data so WPF image objects remain outside application contracts. It uses bounded workers, WIC for supported images, and `IShellItemImageFactory` with `SIIGBF_THUMBNAILONLY` for other file types; unavailable thumbnails use UI placeholders. Cache by normalized path, file size, last-write time, and requested dimensions, with a bounded LRU memory cache and a local application data disk cache. The carousel defaults to five neighbors per side, adjustable with a discrete, tick-marked WinUI Slider hosted in the WPF Settings view. The slider represents an odd total icon count from 1 to 25 (default 11), changing by two per step, including the current file. It uses 150 × 150 square tiles, filled by uniformly scaling and center-cropping. The neighbor count is currently session-only; persist it with the later settings store. Keep carousel item instances stable during adjacent file navigation so thumbnail work and hit testing do not flicker. When changing files, show an available carousel thumbnail while the full viewer image loads.

The WPF media canvas uses the full space beneath the toolbar and beside the navigation rail. The folder tree and carousel are WPF overlays with translucent backgrounds and edge-triggered slide reveal. Center the selected carousel item in the viewport using leading/trailing spacer items sized for omitted files across the entire folder, hide its scrollbar, animate recentering between files, and keep tile spacing tight. Crop thumbnails to a centered square and constrain filenames to one line. The carousel starts with about one eighth of its height peeking into view at 65% opacity; make the peek and reveal behavior configurable in future settings.

## WPF and WinUI boundary

The WPF app owns the process, top-level window, composition root, and application navigation state. Keep ordinary views and controls in WPF. Use WinUI XAML Islands only for controls where their built-in behavior and Windows consistency justify the interop cost. The Home/Browse navigation rail uses the WinUI `NavigationView` hosted by a WPF `HwndHost`; it stays in compact icon-only mode, with its expand/collapse toggle disabled. Settings is a footer destination; its carousel slider is hosted in its own XAML Island within the WPF Settings view. Selected destinations are bridged to the WPF page host. The whole rail remains hideable through `MainWindow.IsSidebarVisible` for future fullscreen behavior. Three WinUI `CommandBar` controls in a separate XAML Island span the full top edge of the WPF window; they align left, center, and right in a shared grid and are fixed at 48 pixels high with overflow hidden. Place actions in `PrimaryCommands` and use 32-pixel AppBarButtons; group the reset-to-fit AppBarButton with its zoom percentage readout. The toolbar and navigation rail have pitch-black backgrounds. The WPF media canvas fills the remaining area; the folder tree and carousel are translucent WPF overlays that slide in near the left and bottom edges. Reserve 48 pixels below the folder tree so its last entries remain reachable; the carousel returns to a one-eighth bottom-edge peek at reduced opacity with a gradient hint.

Isolate island creation, lifetime, sizing, focus, keyboard message filtering, and event bridging in the WPF app's interop layer. Built-in controls can be created directly in the island host; put custom hosted WinUI XAML in a small WinUI class library if that becomes necessary. Bridge navigation through application-owned destinations and commands. Do not share framework-specific visual objects, controls, or dependency properties across the boundary. The WPF host remains responsible for coordinating state between the island and WPF pages.

Initialize and dispose the Windows App SDK XAML environment with the WPF window lifecycle. Verify keyboard focus transfer, access keys, tab navigation, resizing, DPI changes, accessibility, and shutdown behavior. The current host uses Windows App SDK 2.6.4 as an unpackaged, framework-dependent app with automatic bootstrap initialization; the Windows App Runtime must be installed on the target computer. Revisit self-contained deployment when packaging is specified. Do not add XAML Islands for controls that WPF already provides adequately.

Keep the native WPF caption and system caption buttons for Windows behavior. Apply the requested runtime caption and text colors through DWM (`DWMWA_CAPTION_COLOR` and `DWMWA_TEXT_COLOR`, supported on Windows 11). Do not use WinUI `SetTitleBar` for the WPF window; it does not convert an island into the top-level WinUI window required by that API.

## Navigation and roots

Use one navigation/session state service for loaded roots, active folder, sorted files, active file and position, viewing mode, and interaction state. Loading multiple folders (including by drag and drop) creates multiple separate roots in one session. Preserve each root's real hierarchy and do not merge files from distinct roots into a synthetic folder listing.

The initial folder tree loads the selected hierarchies asynchronously and shows them expanded by default. Keep expansion behavior and row padding as presentation settings so users can customize them later. Do not follow junctions or symbolic links recursively.

Folder rows use a dedicated right-aligned ellipsis button to open folder-management actions. The row button remains WPF, while its menu is a WinUI `MenuFlyout` shown from the existing toolbar XAML Island and positioned at the clicked row. File/folder rename, new-folder naming, and non-empty folder deletion confirmation each use a single standalone window containing WinUI controls, hosted by its own XAML Island; do not nest a `ContentDialog` inside the host window. Move-folder destination selection uses the basic native folder picker, initially showing the source folder's parent. Keep left-click for navigation and right-click for moving the current file to that folder. A top-right Pin/Unpin control uses the corresponding Segoe MDL2 Assets glyphs, rotated 90 degrees counterclockwise, and keeps the tree panel open while pinned; its state is session-only until the future settings persistence step. The selected state reverses the icon and button surface colors.

The WPF shell's toolbar and sidebar separators use opaque `#292B31` borders. The hosted WinUI surfaces may use transparent backgrounds where they sit inside those shell borders.

Switching viewing modes preserves the loaded roots and applicable active folder/file context, without rescanning. It clears the selection from the previous mode because the user's interaction context has changed. Each mode may keep its own temporary presentation state while active.

Views translate mouse and keyboard input into navigation commands. In Browse, left/right and up/down arrows and mouse-wheel notches over the image viewport or carousel move one file. Navigation stops at a folder's first and last files; five consecutive attempts past the same edge advance to the adjacent nonempty folder in loaded tree preorder. Changing direction or successfully selecting a file resets the overscroll count. A central interaction policy routes wheel behavior based on the active mode and handler capabilities. For supported images, right-click toggles between file navigation and zoom interaction. Numpad plus/minus enter zoom mode and adjust zoom by 25% multiplicative steps from 20% through 500%; the wheel over the image viewport also zooms in that mode and left-drag pans when zoomed above fit. Decode dimensions scale with the zoom level, bounded by the source image dimensions, so zooming requests additional source detail. Zoom is centered on the viewport and resets when returning to file navigation or when the selected file changes. Text scrolling and media playback similarly must not accidentally navigate files while the user interacts with content or controls.

## Sorting

Use Windows Explorer-style filename sorting, including natural numeric ordering (for example, `05`, `57`, `100`). Centralize the comparer so the folder list, carousel, gallery, and continuous reader use the same ordering. Sorting should follow Windows behavior for case and numeric substrings rather than using a plain ordinal string comparison.

## Handlers and thumbnails

Handlers declare supported file types and capabilities. Keep detection, asynchronous loading/resource lifetime, presentation, thumbnail generation, and metadata retrieval separable so gallery thumbnail work does not require opening a full viewer. Start with WPF imaging/WIC for common images, a bounded text reader, and an unsupported-file view with an option to open the associated Windows application.

Use a shared thumbnail service with a bounded in-memory cache and a disk cache under local application data. Key cached results by normalized path, file size, last-write time, and requested dimensions; allow refresh/invalidation. Generate thumbnails on bounded workers, return placeholders immediately, cancel work when folders or visible items change, and freeze WPF bitmap sources before cross-thread use.

Video needs are intentionally modest. Select the simplest maintainable Windows-compatible playback backend that meets basic playback requirements on the supported Windows 11 64-bit platform. Unsupported formats can be opened in their associated dedicated applications. Avoid taking on a broad codec runtime unless implementation evidence shows it is needed.

## Filesystem and persistence

Keep filesystem operations behind asynchronous services with cancellation and progress. Enumeration should not read file contents. Treat `FileSystemWatcher` notifications as hints and reconcile against actual directory contents; do not follow junctions or symbolic links recursively by default. Use bounded background work, partial results, and batched UI updates for large folders.

Use SQLite for settings/history and a persistent operation journal, preferably through `Microsoft.Data.Sqlite` with explicit SQL while the schema remains small. Before a mutation, persist a prepared operation; perform and verify the filesystem action; then persist its final state. Recover or flag interrupted operations at startup. Undo/redo are compensating operations that verify recorded file identities and destination availability before acting; conflicts never overwrite. Related file moves carry a shared batch ID and undo in reverse order, redo in original order, and retain individual outcomes. Compensation records link to the original operation without invalidating redo history. Recycle Bin restore runs through a Windows Shell namespace adapter on an STA thread; it requires a unique original-path, item-type, and file-identity match. After restore it verifies the file identity, with a streamed length/SHA-256 comparison against the selected Recycle Bin item as a file-only fallback if Windows assigns a new identity. The current UI exposes Undo/Redo toolbar commands and Ctrl+Z/Ctrl+Y, then refreshes affected folder and file state.

Recycle Bin restoration is best-effort. Keep operation history even when automatic restoration is unavailable, and show a clear visual result for each restore attempt indicating whether it succeeded. A failed Recycle Bin request must never fall back to permanent deletion. Deleting a folder prompts the user for a Yes/No decision before sending it to the Recycle Bin.

Cross-volume moves may be copy-then-delete and need verified copy state to recover partial failures. Batch moves share one logical journal group while retaining per-file results. Refresh affected folders and navigation state after successful operations.

## Dependencies

- `CommunityToolkit.Mvvm` for observable view models and commands.
- `Microsoft.Extensions.Hosting` / dependency injection / logging where useful for composition and lifetimes.
- `Microsoft.Data.Sqlite` for the local journal and settings.
- WPF imaging/WIC initially for images.
- A basic Windows-compatible media backend selected during the media phase.
- Windows Shell APIs isolated behind an adapter for Recycle Bin and shell integration.
- Windows App SDK XAML hosting; add a WinUI class library only if custom hosted WinUI XAML is introduced.
- `Microsoft.WindowsAppSDK` 2.6.4 for the WinUI `NavigationView` and current XAML Islands API.

Keep third-party types behind application-owned interfaces. Do not add a dependency solely to avoid a small, well-contained adapter.

## Delivery order

1. **Foundation:** .NET 10 WPF shell, project boundaries, composition, logging, persistence contracts, interaction and filesystem contracts. Establish the XAML Islands hosting boundary when integrating the WinUI navigation control.
2. **Browsing:** load separate roots, folder tree, per-folder file list, shared navigation state, image viewer, carousel, Explorer-style sorting, keyboard/wheel behavior.
3. **Safe operations:** SQLite operation journal before broad file editing; move/rename/create/delete-to-Recycle-Bin, undo/redo, conflict checks, restart recovery, Yes/No folder-delete prompt, and restore status cues.
4. **Media and text:** video/audio/text/unsupported handlers and context-specific interactions.
5. **Home and persistence:** recent/frequent/favorite folders, settings/layout, external-change reconciliation.
6. **Gallery and batch operations:** virtualization, mode-local selection, grouped operations, direct open in viewer.
7. **Continuous reader:** lazy loading, bounded preload and resource release, synchronized navigation context.
8. **Refinement:** search, drag/drop, metadata, duplicate detection, extra handlers, and usability/performance work.

The specification's full acceptance criteria remain the completion target; an early browsing phase is not a claim that media, organization, or persistence requirements are complete.
