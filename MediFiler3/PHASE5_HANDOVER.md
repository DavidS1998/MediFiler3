# Phase 5 continuation handover

Updated 2026-11-11 after step 5.5 closeout. **The approved Gallery phase 5.1-5.5 and internal drag/drop follow-ups are complete with recorded verification limits.** Read [current status](PROJECT_STATUS.md), [acceptance evidence](PHASE5_ACCEPTANCE.md), AGENTS.md, SPECIFICATION.md, ARCHITECTURE.md and DEVELOPMENT_PLAN.md. Earlier running notes, exact crash evidence and superseded next tasks are retained in [handover history](PHASE5_HANDOVER_HISTORY.md) and [status history](PROJECT_STATUS_PHASE5_HISTORY.md).

## Baseline and next work

Implementation baseline: clean master/`ae272bd`, including grouped operations, Browse history selection fixes, click/drag separation, cropped translucent preview and nested-folder/edge/native-wheel drag navigation. User confirmed wheel scrolling during a drag, nested-folder destinations and edge scrolling. Step 5.5 reran the clean build, 327 unit/filesystem tests, 192 full native checks and a 31-check focused Gallery repeat, all without failures/skips. Current closeout changes are test-only diagnostics/documentation. Do not reset, restore obsolete experiments, stage/commit/push automatically, or restart the user's running app.

The subsequently approved Continuous Reader slices 1-6 are complete with recorded limits; read [READER_HANDOVER.md](READER_HANDOVER.md) and [READER_ACCEPTANCE.md](READER_ACCEPTANCE.md) for current behavior, verification and continuation. No Reader implementation slice remains. The closeout evidence here remains the Gallery baseline. Additional sorts/filtering and external file drag/export remain deferred. Bulk rename is not planned. Reported regressions can be fixed within existing behavior/safety policy. [PHASE5_ACCEPTANCE.md](PHASE5_ACCEPTANCE.md) retains physical/environment/native limits and the measured 0.76-1.03s cost of clearing 10,000 selected items.

## Current code map

| Area | Responsibility |
|---|---|
| Application/Navigation/NavigationSessionState.cs, NavigationController.cs | Shared sorted workspace/current record, root ancestry, lazy mode navigation, incremental roots and membership validation |
| Application/Navigation/GallerySelectionState.cs, GalleryHoverSelection.cs, GalleryDragGesture.cs | Path-based sorted snapshots, focus/anchor, Alt-hover direction/visits, unselected versus selected drag scope and press/movement threshold separation |
| App/Interop/WinUiGalleryIsland.cs, .Selection.cs, .Drag.cs | Retained virtualized grid, bounded native decoded LRU, deferred diffs/reveal/clipping, native selection/accessibility and custom native tile drag preview |
| App/Views/BrowseView.Gallery.cs, .GalleryOperations.cs | Mode lifecycle, explicit selection/drag command snapshots, grouped moves/recycle, single-file rename and history integration |
| App/Views/BrowseView.GalleryDrag.cs, .GalleryDragNavigation.cs, .GalleryDragWheel.cs | Opaque gesture tokens, tree target cues, cleanup/deferral, 600ms expansion, edge scroll and temporary native wheel hook |
| App/Views/BrowseView.OperationReconciliation.cs, .Reconciliation.cs, .Shortcuts.cs, .xaml.cs | Verified move-echo reuse, watcher ownership, scoped shortcut routing, incremental tree/count/history refresh and Browse selection behavior |
| App/MainWindow.xaml.cs, .Fullscreen.cs | View commands/history availability, deferred WPF/native key routing and fallback wheel message routing |
| Application/Files/IThumbnailService.cs, Infrastructure/Files/FileSystemThumbnailService*.cs | Shared Windows-default raw PNG API, explicit-size compatibility, caching/freshness/coalescing/ownership |
| Application/Operations/IFileOrganizationService.cs, Infrastructure/Operations/FileSystemFileOrganizationService*.cs | Root-identity-scoped journal, safe grouped moves/recycle/Undo/Redo, partial results, verified identities/collisions/compensation |
| Native GalleryChecks/Input/Resume/Mutation/DragChecks and ThumbnailRenderingChecks | Real controls/automation, pixels, watchers, retained native sources, scoped operations and drag routing |
| Infrastructure.Tests | Application business rules, filesystems/watchers, cache/journal/backup/restore/recovery regressions |

Paths are relative to the corresponding src/MediFiler3.* or tests/MediFiler3.* project. See the [runner README](../tests/MediFiler3.App.Integration/README.md) for entry points and fixture requirements.

## Presentation and selection contracts

- WPF hosts WinUI islands. Preserve focus/keyboard, cancellation, DPI handling, HWND airspace clipping and clean island lifetime. Watchers span Browse/Gallery and stop in Home/Settings; healthy startup is metadata-only. Automatic work never records root visits.
- Width/Height persist independently (100-400, step 50, default 150) with 150-logical-pixel sliders. Center rows; retain two-pixel gaps, gradient captions, square-first crop and full-aspect hover-fit. Solid square five-pixel #FF1493 outlines and outlined selected counter with 24px right clearance remain.
- Native Extended selection owns ordinary Ctrl/Shift/range/focus input. Alt-hover uses the first valid tile to choose add/remove, visits paths once and resets at gesture/context boundaries. Ctrl-hover does not paint. Selection is case-insensitive/path-based and command snapshots follow current natural sort order. Focus is separate from selection.
- Mode/root/folder changes clear mode-local selection. Home/Settings retain it; reconciliation prunes removed paths without reselecting an explicit clear. Enter/double-click hands the focused file to Browse without a root rescan.
- Shortcut commands work across tree/toolbar/viewer focus, validate context again when deferred, and preserve owned modal input. During dragging, native drag handling owns Escape/modifiers.

## Drag lifecycle and preview

- Automatic tile CanDrag and GridView.CanDragItems stay false: they previously intercepted normal clicks. A press records scope without handling/capturing input. GalleryDragGesture requires Windows horizontal/vertical movement thresholds before explicit native StartDragAsync. Release/small travel stays a normal native click. Validate the source visual against recycled containers before starting.
- An unselected origin drags only itself; a selected origin captures the naturally ordered whole selection before normal pointer selection changes. Native tile DragStarting/DropCompleted provide preview customization/completion. The package exposes only an opaque per-gesture text token for internal tree moves; never accept external paths as a substitute for that token.
- A drag reveals the tree and shows Moving N files. Valid destinations highlight pink and advertise Move. Same-folder/blank/stale/foreign targets are rejected. The normal cropped preview is 80% opaque, has hotspot (0,0), and contains at most three realized tiles (192px each, 8px offsets, maximum 208px canvas). No extra offscreen realization or thumbnail decoding is performed for the preview. Hover-fit is suspended during capture/drag so letterbox margins do not appear.
- Preview rendering uses native capture deferrals, version validation and resource cleanup. Failed capture retains the native default visual. Drag completion/cancel/context invalidation clears visual/target state and releases reconciliation deferrals; drops reuse the existing grouped move service with the captured files.
- Hover expands existing collapsed tree nodes after 600ms without selection/navigation. A 50ms timer re-hit-tests layout and scrolls viewport edges; scrolling resets dwell and old highlight. DragLeave stops timer work even with cached in-tree coordinates, and reenter restarts it.
- Native drag-wheel delivery uses a temporary WH_MOUSE_LL hook, limited to the active authorized gesture, foreground owner and pointer over the tree. It coalesces signed wheel deltas and dispatches layout/scroll work outside the input callback, checking gesture identity again. Drop/cancel/context cleanup unhooks and invalidates queued packets. Normal input outside that scope passes through. Preserve Windows line/page settings, high-resolution deltas and fallback WPF routing. Do not leave a global input hook installed while idle.

## Thumbnail and native lifetime contracts

- Gallery/carousel use GetDefaultThumbnailPngAsync. Normal Windows retrieval uses SingleItem without explicit size/options, preserving returned dimensions/aspect/orientation. Display sliders are not source-resolution requests. Gallery loads raw native PNGs; carousel uses raw frozen WPF PNG decoding/HighQuality display scaling. No manual blur/SoftThumbnailDecoder remains.
- The private default-size 0x0 cache bucket stays separate from old explicit-size hashes. Do not call the explicit API with zero sizes to emulate defaults; it still clamps inputs. Default entries retain creation-time dimensions across later DPI changes. No blanket purge/migration occurs.
- Known invalidated images bypass stale Windows system pixels with fresh-source WinRT decoding, bounded to 256px for defaults without upscaling. RequiresFreshSource metadata/zero-byte markers persist across restart, housekeeping and explicit cache clearing. F5 tests freshness fallback, so compare normal provider quality using new/uninvalidated disposable sources.
- Retain encoded 512-entry/32 MiB and decoded 512-entry/128 MiB bounds, actual-dimension byte accounting, three-worker limits, per-key coalescing and cancellation after permits. Cache, backup, source and journal ownership remain separate.
- Native lists update from the latest coalesced snapshot only after visible/loaded host/grid layout at Render priority. Disjoint/empty replacements publish one Reset on the retained collection; shared-path changes remain incremental. Do not mutate native collections synchronously from sidebar/native callbacks. Decoded previews are not globally flushed for routine reconciliation.
- Selection synchronization suppresses feedback/reentrancy. Deferred focus/reveal rejects stale records/input, waits for actual layout and yields to newer interaction. Preserve HWND exclusions for the pinned tree and fullscreen shell on reattachment/loading/arrangement even when cached region keys are unchanged.

## Operations, history and reconciliation

- Never silently overwrite or permanently delete media. Retain root-filesystem-identity-scoped journals, ancestry ownership for overlapping roots, source-root history for cross-root file moves, explicit preserving collisions, same-volume verified backup storage, compensation/recovery and retirement-before-backup-delete. [Phase 4 handover](PHASE4_HANDOVER.md) retains the detailed storage/layout policies.
- Gallery mutations capture root/folder/mode/revision/current records, validate after dialogs and reject stale collision choices. Expensive filesystem work stays in services. Reconciliation is deferred across commands/dialogs; release media before mutations/history and reopen affected/cancelled media paused.
- Reuse MoveFilesAsync and SendFilesToRecycleBinAsync. Per-item outcomes, UnattemptedCount and WasCancelled are explicit. Ordinary failures continue; cancellation/recovery uncertainty stop remaining items. Started mutations finish verification/durable events before cancellation applies between items. Completed members remain a grouped history entry; restore is best effort with strict identity/content checks.
- Rename applies only to exactly one selected Gallery file, preserving extension. Single-file recycle skips confirmation; multiple selected files require it. Existing folder-menu commands retain their own behavior.
- Update source/destination counts immediately. Merge roots/children instead of replacing the whole tree; keep unchanged node identity/expansion and map expansion for changed subtrees. Keep history availability fresh even when the active root object is retained. Browse Undo chooses restored files; Redo removing current chooses its next/last-neighbor position. Gallery retains its own selection/current policy.
- Structural created/deleted/renamed hints are distinct from content changes. Verified same-volume move source previews may survive bounded 512-path/30-second echo tracking after identity/size/timestamp or absence validation. Unknown/new/overwrite destinations, changed versions, content hints, F5, overflow/recovery and expired tracking retain normal invalidation. Strong hints retire echo tracking; they must dominate structural hints for the same path.

## Verification and remaining limits

The [acceptance report](PHASE5_ACCEPTANCE.md) is the current source of test totals, performance measurements, user physical confirmations and open limits. All 126 production source/project files match their pre-closeout hashes. Native instability history remains: earlier ObservableCollection `0xC0000005` faults and a later Microsoft.UI.Xaml.dll `0xc000027b` transition fault have preserved evidence in [history](PHASE5_HANDOVER_HISTORY.md); passing runs do not establish universal elimination. Preserve WER/trace and shared-path/disjoint/transition context if a crash recurs. Keep the historically timing-sensitive 110ms carousel fade assertion intact.

Mixed-DPI/fullscreen/physical focus/Narrator, real-library image throughput/long-session memory, network/removable/permission/installer/external-launch cases and prior Phase 3/4 limits remain explicit. The latest user acceptance covers nested-folder drag/hover access, edge scrolling and native drag-wheel scrolling. Do not mark other physical cases accepted by inference.

Build once using temporary output before no-build tests; never build concurrently against shared obj directories. Temporary builds do not update the user's running app. Keep all earlier implementation and safety behavior while discussing the next feature slice.
