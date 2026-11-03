# Project status

**Snapshot date:** 2026-11-03
**Overall:** Phase 1 complete. Phase 2.1-2.4 are complete. Step 2.5 is in progress: verified move/rename and folder-creation undo/redo, grouped file moves, and history UI are implemented; Recycle Bin restore remains.

## Handover: resume here

Phase 1 is closed. The append-only SQLite journal and startup recovery assessment (2.1-2.2), journal tests (2.3), and safe filesystem operations plus UI (2.4) are implemented. Step 2.5 now has verified compensation for moves, renames, and empty folder creation, plus grouped file moves, toolbar history controls, and filesystem tests. Continue by implementing best-effort restoration for items sent to the Recycle Bin and adding coverage for its success/failure reporting.

1. Read `AGENTS.md`, `SPECIFICATION.md`, `ARCHITECTURE.md`, `DEVELOPMENT_PLAN.md`, and this status file before coding.
2. Inspect the solution and current working tree. Preserve the WPF host and existing WPF/WinUI XAML Island boundary.
3. Keep journal changes append-only and recovery conservative; ambiguous operations must not be retried automatically.
4. Implement verified undo/redo and batch grouping in Step 2.5, including safe compensation checks, UI refresh/results, and tests. Never silently overwrite or permanently delete; never fall back to permanent deletion if sending to the Recycle Bin fails.
5. Build after changes and update the development plan/status as Phase 2 progresses.

## Current implementation

- Phase 2.1 adds operation records and the application-owned journal contract. Infrastructure owns an idempotently initialized, versioned SQLite database in LocalAppData, initialized by the host before the main window.
- Phase 2.2 replaces mutable status rows with immutable operation intents and append-only events, migrates the earlier schema, stores versioned typed payload JSON, tracks compensating-operation links, and exposes undo/redo candidates. User operations invalidate outstanding redo candidates transactionally. SQLite uses WAL plus FULL synchronous durability.
- Startup recovery inspects incomplete operations. Same-volume moves/renames use recorded Windows file identities; cross-volume moves use a verified destination identity plus source absence. Folder creation is only auto-completed when a recorded created-folder identity matches. Recycle Bin and insufficient-evidence cases become `RecoveryRequired`; the service logs each outcome and never retries an ambiguous operation.
- Step 2.3 adds `MediFiler3.Infrastructure.Tests` with isolated temporary SQLite databases. Coverage includes create/reopen, schema migrations from versions 1 and 2, append-only transitions, redo invalidation, undo/redo candidate selection, same- and cross-volume recovery evidence, folder/Recycle Bin ambiguity, Windows file identity lookup, and recovery-service outcome persistence/retry avoidance. `SqliteDatabase` accepts an explicit database path for isolation; the production default remains in LocalAppData.

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
- When changing files, the viewer shows the selected carousel thumbnail's uncropped bitmap immediately when available, then replaces it with the full image after decoding; the carousel continues to show its center-cropped bitmap.
- Unsupported files use their Shell thumbnail in the main viewer when available, with an explicit thumbnail-preview label.
- Three 48-pixel WinUI CommandBars share a full-window-width XAML Island above the sidebar and media canvas; they align left, center, and right with overflow buttons hidden. Toolbar and sidebar backgrounds are pitch black (#000000). The left section shows folder context, the center Add AppBarButton loads folders, and the right section has zoom-out, reset-to-fit with the zoom percentage, and zoom-in controls. The redundant current filename and inline viewer zoom badge are removed.
- The toolbar uses native CommandBar primary commands aligned left/center/right, with 32-pixel AppBarButtons and the reset-to-fit button grouped with its zoom-percent readout, following the layout pattern in the user-referenced MediFiler-V2 `MainWindow.xaml`.
- Zoom-wheel steps preserve the image point under the mouse pointer. The dedicated Settings footer tab hosts a discrete, tick-marked WinUI slider for odd total carousel icon counts from 1 to 25, default 11; this includes the current item, advances by two per slider step, applies immediately, and is not persisted yet.
- The tree and carousel float over the WPF media canvas with translucent backgrounds. The tree starts hidden; the carousel initially and subsequently leaves about 22 pixels (one eighth of its height) peeking above the bottom edge at 65% opacity, with a transparent gradient hint. Hovering at the left/bottom edges slides the corresponding panel out; leaving the panel hides the tree or returns the carousel to its peek state.
- The scrollbar is hidden while horizontal scrolling remains available, including mouse-wheel file navigation over the carousel. Leading/trailing spacers represent the omitted folder items, so global scroll offsets animate recentering across the entire folder. Adjacent navigation reuses thumbnail items and keeps async thumbnail work alive to avoid flicker. Thumbnails are center-cropped, spacing is compact, names stay on one line, and item hover/selection visuals are suppressed in favor of a pointer cursor.
- Folder tree rows stretch across the overlay width and use full-row hover/selection backgrounds. The chevron keeps the same shape and rotates when expanded, with a transparent hit area and no default blue open state. A 48-pixel bottom reserve gives the carousel more of the screen while allowing the final tree entries to scroll clear of it.
- Carousel hit testing is disabled only while its panel slides in or out; it remains active during horizontal recentering so clicked tile identity remains tied to the clicked file.
- The sidebar uses consistent NavigationView item spacing without a per-item negative margin, avoiding a clipped Home entry.
- The Home/Browse rail is a WinUI `NavigationView` hosted by a WPF `HwndHost` through Windows App SDK XAML Islands. It is always compact/icon-only; the pane expand/collapse control is disabled. `MainWindow.IsSidebarVisible` can hide the whole rail for future fullscreen mode.
- WPF retains the native Windows title bar. `TitleBarBackground` and `TitleBarForeground` dependency properties apply runtime caption/text colors through DWM; defaults are black and white.
- App project references `Microsoft.WindowsAppSDK` 2.6.4 and is configured unpackaged/framework-dependent with automatic bootstrap initialization. The Windows App Runtime 2.6.4 must be installed on deployment computers.
- WinUI initialization, resources, message pre-translation, island sizing/focus, and shutdown are handled in `src/MediFiler3.App/Interop/`.
- Step 2.5 is in progress. Move/rename undo/redo verify recorded identities and never overwrite occupied destinations. Empty created folders can be undone only when identity matches and the folder remains empty; undo sends them to the Recycle Bin, and redo recreates them only under the same verified parent. Batch file moves share one journal BatchId; grouped undo runs in reverse order and redo in original order, retaining individual results. Undo/Redo toolbar buttons and Ctrl+Z/Ctrl+Y refresh the tree, active folder, and affected file selection. Recycle Bin restore is still unavailable; the UI reports such history entries as not undoable. The folder menu and toolbar have been build-verified but have not received a separate interactive UI review.

## Completed work

- **1.1 Solution structure and startup:** project boundaries, host-based DI/logging, WPF startup and host shutdown.
- **1.2 Main window and shell:** dark high-contrast shell, Home/Browse switching, WinUI `NavigationView` in an island, native Windows caption with runtime colors, permanently compact rail, and a full-hide property for future fullscreen mode.
- **1.3 Folder roots and hierarchy:** multi-select folder picker, folder drag and drop, separate loaded roots, asynchronous recursive folder enumeration, reset-on-load context, cancellation of superseded work, and inaccessible-folder reporting.
- **1.4 File enumeration and navigation state:** immediate-folder file listings, hidden/system filtering, Explorer-style natural sorting, shared active-folder/current-file state, first-file selection, and title-bar position display.
- **1.5 Image viewer:** viewport-fitted WPF/WIC image display, bounded decode dimensions, aspect-ratio preservation, cancellation and stale-result checks, source freezing and release, and unsupported/unreadable-file messaging.
- **1.6 Thumbnails and carousel:** bounded asynchronous thumbnail generation, Shell thumbnail fallback for unsupported formats, 150-square carousel tiles, loading placeholders, memory/disk caching, and centered current-file context.
- **1.7:** arrow-key and image-viewport wheel file navigation; five same-direction boundary overscroll attempts advance to the adjacent nonempty folder in loaded tree preorder. Supported-image right-click interaction toggle, Numpad/mouse-wheel zoom from 20%–500%, and drag panning are implemented.
- Specification and architecture decisions are recorded in `MediFiler3/SPECIFICATION.md` and `MediFiler3/ARCHITECTURE.md`.

## Phase 2 handover: safe file organization

The append-only SQLite journal and startup assessment, journal tests, and Step 2.4 filesystem operations and UI are in place. Use explicit SQL while the schema remains small. Keep persistence and filesystem inspection behind application contracts and Infrastructure; do not put database or filesystem work in WPF views. Journal reopen/migration, event transitions, redo invalidation, and recovery evidence are covered by automated tests.

Step 2.4 implements moving/renaming files and folders, creating folders, and sending files/folders to the Windows Recycle Bin. Step 2.5 adds verified undo/redo and batching; file/folder moves, renames, and empty-folder creation are reversible, while restoring a Recycle Bin item remains pending. Resolve conflicts explicitly; never overwrite silently. A failed Recycle Bin request must not become permanent deletion. Non-empty folder deletion requires Yes/No confirmation.

### Suggested prompt for a new context

```text
Continue MediFiler3 from the handover in MediFiler3/PROJECT_STATUS.md. Read AGENTS.md, SPECIFICATION.md, ARCHITECTURE.md, DEVELOPMENT_PLAN.md, and PROJECT_STATUS.md first. Phase 1 is complete and the user reports no UI issues. Preserve the existing WPF host and WPF/WinUI XAML Island boundary.

Phase 2.1-2.4 established and tested an append-only SQLite journal, conservative recovery assessment, safe file/folder operations, and folder menu UI. Step 2.5 is in progress: verified undo/redo and batch grouping are implemented for moves, renames, and empty folder creation, with toolbar controls and tests. Finish best-effort restoration for items sent to the Recycle Bin. Ambiguous recovery states must remain flagged and must not be retried automatically. Never silently overwrite or permanently delete files; a failed Recycle Bin operation must not fall back to permanent deletion. Build after changes and update the development plan and project status.
```

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
- The user has reviewed the current interface and reports no UI issues. Step 1.8 is complete.

## Settled interaction behavior and deferred settings

- Step 1.7 boundary behavior is settled: stop at the first/last file, then move to the adjacent nonempty folder after five consecutive overscroll attempts in that direction.
- Zoom behavior is settled: centered 25% multiplicative steps, 20%–500% limits, mouse wheel and Numpad plus/minus in zoom mode (Numpad activates it automatically), and left-drag panning above fit-to-view. Switching to navigation resets zoom and pan. Decode resolution follows zoom level and is capped at the source image size.
- Carousel density defaults to five neighboring files per side, configurable by total icon counts 1, 3, ..., 25 in Settings. The value is session-only until persistence is implemented.

## Verification record

- Step 2.3 verification: `dotnet test MediFiler3.sln --verbosity minimal -m:1` passed all 18 tests. `dotnet build MediFiler3.sln --verbosity minimal -m:1` succeeded with 0 warnings and 0 errors. `git diff --check` is clean.
- Before the latest interface revisions, the user verified the image viewer, original carousel thumbnails and transitions, Shell fallback, and file navigation as sufficient for the current stage.
- Step 1.8 verification: user reports no UI issues. Source review found no integration break in folder loading, state propagation, toolbar context/title updates, or selection refresh. `dotnet build MediFiler3.sln -p:BaseOutputPath=C:\Users\xofas\AppData\Local\Temp\MediFiler3Step18Final\` succeeded with 0 warnings and 0 errors. `git diff --check` is clean. No automated tests exist yet.

- Step 2.4 adds journal-backed file moves/renames, folder creation/moves/renames, and Windows Recycle Bin actions. Existing destinations are conflicts and are never overwritten. Cross-volume file moves use a temporary copy with content verification and source revalidation before source removal; cross-volume folder moves are safely rejected. Folder operations reject reparse points. Non-empty folder deletion requires explicit confirmation, and the WinUI confirmation is now implemented. Failed Recycle Bin requests never fall back to permanent deletion.
- The Browse tree has a right-aligned folder menu button for create, rename, move, Recycle Bin, refresh, Explorer, and properties actions. The ellipsis button opens a WinUI `MenuFlyout` from the existing toolbar XAML Island. File/folder rename, New Folder, and non-empty folder deletion confirmation use standalone WinUI windows with no nested ContentDialog. Move Folder To uses the basic native folder picker, initially showing the source folder's parent. The existing Add Folders workflow retains its system multi-folder picker; read warnings appear in the Browse status notice. The tree panel has a session-only Pin/Unpin control with inverted selected colors and glyphs rotated 90 degrees counterclockwise; persist its state with the future settings store. Left-click remains navigation; right-click moves the current file to the clicked folder. The WinUI toolbar Island exposes current-file rename and Recycle Bin actions; F2, Delete, and Ctrl+Shift+N provide keyboard shortcuts. Operation outcomes appear in Browse.
- Step 2.4 filesystem tests cover same-volume move/rename, content preservation, conflicts, folder creation/move, nested-move rejection, confirmation requirements, and cross-volume folder-move refusal where another volume is available.
- Step 2.4 verification: dotnet test MediFiler3.sln --verbosity minimal -m:1 passed all 25 tests. dotnet build MediFiler3.sln --verbosity minimal -m:1 succeeded with 0 warnings and 0 errors. git diff --check is clean. Recycle Bin shell behavior and the new menu's interactive appearance have not been manually exercised in this verification pass.
- Step 2.5 verification: `dotnet test tests/MediFiler3.Infrastructure.Tests/MediFiler3.Infrastructure.Tests.csproj --no-restore --verbosity minimal` passed all 31 tests. `dotnet build MediFiler3.sln --no-restore` succeeded with 0 warnings and 0 errors. `git diff --check` is clean. Best-effort restoration of items sent to the Recycle Bin remains unfinished.
