# Phase 4 continuation handover

Updated 2026-11-10. Read PROJECT_STATUS.md for the consolidated current snapshot and SPECIFICATION.md/ARCHITECTURE.md/DEVELOPMENT_PLAN.md for requirements, boundaries and sequence. This replaces the old pre-4.1 proposals; those are no longer instructions to reimplement completed work.

## Current baseline and scope

Phase 3 is complete and manually accepted. Phase 4 steps 4.1-4.6 are implemented; 4.7 automated acceptance is complete; physical acceptance remains pending. Home/dialog/backup/layout changes have automated coverage but still need the physical review listed in PROJECT_STATUS.md. Last full runs: 276 unit/filesystem tests and 144 native checks passed, no skips. Final build clean. The monitor-enumeration fallback is included in the current regression build; physical hot-plug and the forced failure branch remain unverified.

Implementation is at `master`, `b0b3ec1` (`WinUI dialogs and window state saving`) as the committed baseline before this uncommitted 4.6 implementation. Inspect current Git history/status; preserve subsequent work. The user explicitly approved the proposed 4.6 watcher/cache scope and authorized its implementation. Preserve existing work when continuing.

| Step | Status |
|---|---|
| 4.1 | Typed Application contracts, SQLite state stores and journal-preserving migrations implemented. Current database schema is 7; do not revert it to the original step-4.1 schema 4. |
| 4.2 | Volume/mute restored before media, carousel density and folder pin persistence, coalesced saves/flush. Refresh/F5 implemented. |
| 4.3 | Root-only usage/favorites, normalized explicit-open counting, bounded availability, Locate/Remove service. |
| 4.4 | Native responsive Home cards, shared root stars, root opening, compact relative time/tooltips and unified Remove entry. Ignore is removed. |
| Follow-ups | Root-scoped history, collision previews/automatic numbered Rename, app-managed backups/Settings cleanup and ContentDialog migration implemented. |
| 4.5 | Monitor-aware normal bounds/maximized persistence, minimized-state handling and runtime work-area reconciliation implemented. No extra navigation preferences or adjustable panel widths were added. |
| 4.6 | Implemented: loaded-root reconciliation, thumbnail index/invalidation and agreed retention/Settings maintenance. |
| 4.7 | Automated restart/integration and viewer/operation regressions complete; physical checklist remains open in PHASE4_ACCEPTANCE.md. |

## Step 4.6 implemented policy (2026-11-10)

The user approved and implementation now includes loaded-root filesystem reconciliation and thumbnail maintenance. Schema stays 7; cache payload version stays 1 with additive optional SourcePath/Invalidated fields. No operation journal or backup lifecycle changes.

- FileSystemFolderChangeMonitor uses recursive coverage for all loaded roots while Browse is active, including inactive roots. Duplicate/nested coverage is reduced. Leaving Browse, new root loads and shutdown cancel the old session; returning starts a rescan. Refreshes with unchanged paths retain watcher coverage. Hierarchy reconciliation retains existing reparse/owned-backup exclusions and existing nodes/expansion, applying updates in batches of 32.
- FolderChangeBuffer coalesces paths case-insensitively: 500 ms quiet debounce, 2 s maximum continuous-event delay, at most 1,024 pending distinct paths. Overflow/error requests root rescans. One awaited dispatcher consumer prevents unbounded callback queues. Commands, loading, dialogs and external-opening actions defer application; context revisions/cancellation reject obsolete work. App-generated changes merge through rescanning actual current state.
- Watcher setup/health probes run off UI, with two shared probe permits, five-second healthy-root checks and failed-root exponential retries from 1 to 60 s. Failed coverage reports degraded status and polls the active folder during retries. Reconnection rescans hierarchy. A blocked Windows network call cannot be forcibly interrupted; permits bound simultaneous probes. Saved Home paths/history are never inferred or pruned.
- Reconciliation retains the selected path or nearest bounded position. Unaffected image/text/media presentation remains active. Affected media reloads paused. Automatic work never records root visits. F5 remains available and force-invalidates active-folder thumbnails, including same-length/timestamp edits. Change hints force invalidation too; unsupported/missed notifications without changed metadata still require F5.
- Existing PNG keys, three decoder workers and 32 MiB/512-entry memory LRU remain. ThumbnailCacheIndex now records owned filename/key, encoded byte size, last access and optional source mapping. Access writes batch every 30 s and flush at shutdown; failed writes retain pending state. Invalidated locked files remain marked invalid across restart and are retried rather than served as stale thumbnails. In-flight decoders cannot publish results from before invalidation/clear.
- Idle housekeeping starts after 30 s without thumbnail requests, then at most hourly. Remove entries unused for 90 days; over 1 GiB, prune least recently used toward 900 MiB. Locked/unsafe entries may keep usage above target until retry. Maintenance runs off UI, serializes passes and yields cache access between 32-entry batches. Settings displays indexed usage and offers Clear thumbnail cache. Usage can undercount legacy files until adoption.
- Only verified owned cache leaf names inside the resolved cache directory are eligible. Reject reparse directories/ancestors/leaves. Adopt valid legacy hash-named PNGs conservatively (unknown-age adoption starts at current time); unknown files remain. Legacy entries without source mapping invalidate conservatively. Recognized abandoned temporary writes older than one day can be removed. Source media, UndoBackups, saved settings/history and operation journal records are never cache-pruned. Explicit overwrite-backup cleanup and its confirmation/history-retirement rules remain separate.

Automated coverage includes debounce/overflow, duplicate/nested roots, real nested/inactive-root notifications and cancellation, tree identity/count propagation, expiry/LRU, access bookkeeping, invalidation and queued decoders, locked deletion/restart, legacy/missing/corrupt entries, temporary ownership and linked-cache rejection. Native Browse checks cover insertion/deletion, inactive hierarchy, same-metadata edits, command deferral, overflow, tab lifetime, missing nested-folder fallback, visit counts and media continuity/paused reload.

Step 4.7 automated restart/integration acceptance is complete (PHASE4_ACCEPTANCE.md). Physical review remains for network/removable disconnect/reconnect, live notification storms/large trees, new Settings cache controls and all previously recorded Home/dialog/backup/display checks. Existing codec limits and deferred feature scope remain unchanged.

## Code map

| Location | Continuation relevance |
|---|---|
| `src/MediFiler3.App/App.xaml.cs` | DI; database/recovery startup; settings/layout initialization before MainWindow; flush while UI is alive; final host shutdown. |
| `src/MediFiler3.App/MainWindow.xaml.cs` | Home/Browse/Settings composition, shared root-loading/pickers/drops, toolbar history, backup cleanup confirmation, WindowLayoutController attachment. |
| `src/MediFiler3.App/Views/BrowseView.xaml.cs` | LoadRootsAsync is the explicit-open counting boundary. RefreshAsync rescans without visits, preserves selection and rejects obsolete restoration. Operation refreshes and StopBackgroundFolderCounts are important watcher lifetime/coordination seams. |
| `src/MediFiler3.Application/Navigation/` | Session/controller state, active root via node ancestry, file/folder selection boundaries. |
| `src/MediFiler3.Application/Persistence/` | PersistenceContracts, ApplicationSettingsService, RootFolderHistoryService, WindowLayoutService and pure WindowPlacementPolicy. |
| `src/MediFiler3.Infrastructure/Persistence/` | SqliteDatabase schema 7, typed state store, root-scoped append-only journal and recovery inspector. |
| `src/MediFiler3.Infrastructure/Folders/` | Progressive hierarchy/count service and bounded root-availability checks. Retain reparse safeguards and hidden-owned-backup exclusions. |
| `src/MediFiler3.Infrastructure/Files/FileSystemThumbnailService.cs` | Current WIC/Shell generation, three workers, disk cache, memory LRU and cache-key logic. Uses ThumbnailCacheIndex and the Maintenance partial. |
| `src/MediFiler3.Infrastructure/Operations/` | FileSystemFileOrganizationService partials, identity-verified compensations, FileSystemOperationBackupStore and shared OperationMaintenanceGate. |
| `src/MediFiler3.App/Interop/WinUiToolbarIsland.Dialogs.cs` and `WinUiContentDialogHost.cs` | Actual ContentDialog controls, dedicated XamlRoot, native primary/secondary/close buttons and modal WPF ownership. |
| `src/MediFiler3.App/Interop/WinUiToolbarIsland.Collisions.cs` | Independent async comparison previews/sizes, cancellation and late-update guards. |
| `src/MediFiler3.App/Services/WindowLayoutController.cs` | Dispatcher capture, closing order, display/work-area/DPI hooks and deferred reconciliation during interactive move/resize. |
| `src/MediFiler3.App/Interop/WindowsWindowPlacement.cs` | Monitor/DPI enumeration, screen/workspace coordinate conversion and Win32 normal-bound capture/application. |
| `tests/MediFiler3.Infrastructure.Tests/` | Business/filesystem/SQLite tests; WindowLayoutTests, OperationBackupTests, FileCollisionTests, RootOperationHistoryTests and existing cache/navigation tests. |
| `tests/MediFiler3.App.Integration/` | Explicit WPF/WinUI runner; RootHistoryChecks, ContentDialogChecks, WindowLayoutChecks, HomeChecks and SettingsAndRefreshChecks. Not run by dotnet test. |

## Implementation caveats to preserve

- Root usage is path-keyed; Undo scopes use root filesystem identity. Overlapping roots use node ancestry, not path-prefix matching. Cross-root file moves belong to the source root. NULL-scoped legacy history is retained but excluded from active-root Undo/Redo.
- Source/destination identity checks happen again after dialog choices. Overwrite preserves data, not a destructive overwrite API. Whole folders are replaced, not merged. During Undo/Redo, numbered Rename moves the blocking existing item so journal paths remain valid; the opposite action restores it.
- Backups use same-volume dedicated storage: LocalAppData/MediFiler3/UndoBackups or a hidden volume-root user-specific directory. Unwritable/linked storage refuses replacement. Legacy sibling backups remain where recorded and are explicitly clearable, not automatically migrated.
- Cleanup serializes with all mutations, validates journal ownership/identity/type/path/reparse/recovery state and durably appends HistoryDiscarded to the whole owning root/batch before deletion. A locked delete can retire history while leaving data for a cleanup retry. Never prune backup directories as cache.
- The thumbnail disk cache is `%LOCALAPPDATA%\MediFiler3\ThumbnailCache`. Keys hash normalized uppercase path, length, LastModifiedUtc ticks and requested dimensions. The memory LRU holds up to 32 MB/512 entries, including missing-thumbnail results. Change hints and F5 force invalidation, including same-length/timestamp edits. Writes use unique temporary PNGs and a final move. Maintenance must coordinate with in-flight reads/writes, not just remove files arbitrarily.
- Volume/mute are shared independently, applied before opening, and media opens paused. Release native media before mutations/history/reload. New watcher work must preserve that lifecycle and avoid autoplay.
- Do not revive the rejected folder-tree popup/transparency implementation. WPF overlays are kept reachable via native video clipping; translucency does not composite over video. The Settings slider opaque-background fix remains.
- ContentDialog uses its own full-sized island, not the toolbar's short root. Await/hide it before destroying the host, preserve Cancel on Escape/host close and cancel preview tasks. System pickers/Open With/Properties stay native; application errors use emergency MessageBox only if WinUI is unavailable.
- Window bounds are logical dimensions plus monitor-relative offsets; native monitor/workspace rectangles are pixels. Preserve taskbar workspace/screen conversion, last non-minimized state, normal restore bounds for maximized windows and temporary minimum-size relaxation. WPF's ShowActivated=false/maximized restriction is handled by deferred Loaded maximization for background windows.
- Window capture on Closing must run before App flushes. Settings/layout write tasks cannot wait on a stopped dispatcher. Schema stays 7 for optional version-1 layout metadata; layout restoration never opens old roots or changes startup Home.

## Acceptance and local workflow

Physical checks still needed: new Home layout/focus/drop/pickers; ContentDialog physical keyboard/DPI/long paths; cleanup on other-volume permission failures; monitor hot-plug/rearrangement, mixed DPI and taskbar relocation. Phase 3 manual acceptance must not be reported as acceptance of these later changes.

Use the commands in PROJECT_STATUS.md and the runner README. Keep a running user app open; use a temporary BaseOutputPath if normal output is locked. Build once before --no-build test/native runs; concurrent builds share obj directories and can fail with file locks. Runner data/fixtures are isolated, but copied media codec coverage is still limited (MKV is the WebM/Matroska subset). Step 4.6 adds source and automated checks; fixture codec limits remain unchanged.

## Suggested opening prompt

Continue MediFiler3. Read the six project documents and inspect Git status. Steps 4.1-4.6 are implemented with uncommitted watcher/cache changes on top of the existing documentation edits. Review the completed step-4.7 automated restart/integration evidence and the remaining physical checklist in PHASE4_ACCEPTANCE.md. Preserve recorded root-history, collision/backup, ContentDialog, media, window-placement and 4.6 watcher/cache policy behavior.

Step 4.7 evidence: three fresh-process restart checks are included in the native runner. See [PHASE4_ACCEPTANCE.md](PHASE4_ACCEPTANCE.md) for process boundaries, results, limits and the remaining physical checklist. No production behavior or schema changed during step 4.7.

## UX refinement follow-up

The user tentatively accepted Phase 4 while acknowledging that not every physical use case could be tested. Retain the unverified cases as known acceptance limits; do not represent them as passed. This follow-up is explicitly authorized UI refinement, not Phase 5 gallery/reader work.

- Prefer suitable WinUI controls over WPF equivalents while keeping the WPF host. Settings now uses native WinUI buttons, ToggleSwitch, slider, text and ScrollViewer in one XAML Island. Existing cleanup confirmations, ownership and error reporting remain unchanged.
- Settings > Show carousel persists CarouselEnabled (default true, additive optional ApplicationSettings version-1 JSON; schema stays 7). Disabled means no carousel, peek, bottom reveal strip or carousel thumbnail requests; viewer navigation/previews/fallbacks remain available. The disabled slider retains the previous density. Re-enable rebuilds the current neighborhood.
- Home removes the redundant subtitle, uses 16 px header top spacing and a 12 px gap before the cards. Header/cards share a centered viewport-bounded width (maximum 1600 px with 32 px side margins), including wide windows. This supersedes the earlier left-aligned Home decision at the user's request.
- F11 or the rightmost toolbar button toggles borderless fullscreen on the current monitor. Top edge hover reveals the native title bar and toolbar; left edge hover reveals sidebar and folder tree together, with the tree beside the sidebar; bottom hover reveals the carousel when enabled, with no fullscreen peek. Tree pin/reveal behavior remains unchanged. Native focus also supports F11; Escape exits from WPF content. Fullscreen is session-only. WindowLayoutController captures normal geometry before entering and suspends persistence of transient fullscreen bounds; leaving restores/clamps the prior normal/maximized placement, and closing fullscreen retains the prior restart layout. Display/DPI reconciliation refits fullscreen instead of saving it.
- A fullscreen-only cursor timer handles edge hover over WPF and native HWNDs. Revealed native toolbar/sidebar surfaces are raised above Home/Settings islands; video clipping excludes those overlays without moving/reopening video. Open toolbar popups retain their surface. Folder menus reveal their toolbar anchor before opening. No always-on-top mode is introduced.
- Carousel refresh no longer copies the full folder per navigation step. NavigationSessionState maintains an equality-compatible file-index map rebuilt only on list changes. Rapid visual updates coalesce at render priority, prioritize the nearest thumbnails, cancel departing-item work and retarget the running scroll animation rather than restarting it. Surviving tile objects remain stable. Disabled/inactive Browse cancels carousel work; returning resumes missing previews. Actual navigation steps and folder-boundary rules remain unchanged.
- Entering edge tiles fade in over 110 ms; departing tiles use noninteractive fade-out visuals for 110 ms, capped at 24 and released on completion/context reset. Animation honors the Windows client-area animation preference and does not delay collection updates/hit testing.

Verification includes native Settings controls/cleanup, carousel-off persistence through fresh processes, Home centering through 3000 px, 100,000-file navigation indexing, rapid carousel navigation/cancellation, tile identity/fades, F11 from WPF/native islands, native video continuity, edge chrome exclusion and restored/closing-fullscreen layout. Physical edge-hover/DPI/taskbar behavior and animation feel remain review items.

Fullscreen reveal follow-up: top hover now shows the actual native caption with the toolbar and remains active over nonclient title-bar controls. Left hover reveals the sidebar and folder tree together, offsetting the tree beside the sidebar and retaining both while the pointer is within their combined area. Pin state and normal-mode reveals remain unchanged; video exclusions follow the offset tree. `.jfif` routes through the WIC image viewer and WIC thumbnail generation, with a JPEG-encoded fixture covering decoding, image capabilities, metadata, thumbnails and source-file release.
