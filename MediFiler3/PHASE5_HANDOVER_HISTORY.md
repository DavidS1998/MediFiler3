> Archived at Phase 5.5 closeout on 2026-11-11. Dated evidence and superseded instructions are historical. Start with [current status](PROJECT_STATUS.md), [current handover](PHASE5_HANDOVER.md) and [acceptance report](PHASE5_ACCEPTANCE.md).

# Phase 5 continuation handover

## Native drag-loop wheel routing fix (2026-11-11)

User physical review found that WPF wheel handlers/window message preprocessing did not receive wheel input during the native drag loop. BrowseView.GalleryDragWheel.cs now installs a WH_MOUSE_LL hook only while an authorized Gallery move drag exists. It handles wheel packets only for the active foreground owner and a currently validated pointer over the tree, coalesces bounded deltas, and dispatches scrolling outside the input callback. Consumed packets are not also delivered to the focused Gallery control. Other input continues through the hook chain; existing WPF wheel handlers remain fallback paths.

End/drop/cancel/context cleanup removes the hook and invalidates queued batches. Queued work retains the exact gesture identity, so an old drag cannot scroll a later context. Windows line/page settings, high-resolution deltas, hover expansion and edge scrolling retain their prior behavior. No filesystem work is performed by the hook.

Verification: clean solution build (zero warnings/errors), **327 unit/filesystem tests and 192 full native checks passed, zero failures/skips**; `git diff --check` passes. Regression coverage now checks real hook installation/removal and native MSLLHOOKSTRUCT packet decoding/routing in both directions, outside-tree rejection and queued-wheel cancellation, instead of testing only the scrolling helper. Transparent native runner windows do not consume physical input intended for another foreground app. Actual OS wheel delivery during a physical drag still requires user interactive review; tests dispatch captured-format native packets without injecting global pointer input.

Evidence: `%TEMP%\MediFiler3Integration-ce5e3b2a195344088da38a163a9773c3`; log `%TEMP%\MediFiler3DragWheelFix-final.log`. Earlier uncommitted hover/scroll changes are preserved; no commit/push was performed. Temporary build output preserves the running app.


## Drag-hover folder expansion and tree scrolling (2026-11-11)

During an authorized Gallery drag, hovering a collapsed folder header for 600ms expands it without selecting it or navigating away from the source. This includes the source folder itself even though a same-folder drop remains rejected, allowing access to its descendants. A single 50ms Input-priority DispatcherTimer performs hit-testing against the current tree layout and scrolls in 32px top/bottom viewport bands. Edge scrolling resets hover dwell and target highlighting so a stationary pointer cannot expand a stale row after scrolling.

Mouse-wheel scrolling during the drag honors the Windows line/page setting. WPF preview-wheel handling and the existing window/native message preprocessing route wheel input only for the active validated Gallery drag while its pointer is over the tree. Normal wheel behavior outside a drag is unchanged. DragLeave unconditionally stops navigation because OLE leave events can retain cached in-tree coordinates; reenter/over restarts it. Drop, cancellation, deactivation/context invalidation and shutdown stop timer/candidate work through existing gesture cleanup. Expansion remains UI-only; no filesystem work occurs on hover/scroll ticks.

Implementation: BrowseView.GalleryDragNavigation.cs, existing Gallery drag routing, tree XAML wheel handler and MainWindow.Fullscreen.cs message preprocessing. Existing move snapshot, click/drag separation, translucent cropped preview, collisions, root history, counters and thumbnail safeguards remain intact.

Verification: clean solution build (zero warnings/errors), **327 unit/filesystem tests and 192 full native checks passed, zero failures/skips**; `git diff --check` passes. The new native check uses a real overflowing tree and verifies delayed expansion, unchanged active folder/current file, both edge/wheel directions, stale-coordinate leave, reentry and cancellation cleanup. Evidence: `%TEMP%\MediFiler3Integration-e0339f28f2ae44f68f8b3988c7fcbf04`; log `%TEMP%\MediFiler3DragTreeNavigation-verified.log`. Physical OS drag-loop pointer/wheel feel and DPI/fullscreen review remain interactive acceptance. Baseline was clean user commit `bcdfece`; these changes remain uncommitted. The running app output was preserved.


## Click/drag separation and cropped preview fix (2026-11-11)

Automatic `UIElement.CanDrag` on tile content intercepted ordinary GridView clicks. It is now intentionally false (as is CanDragItems). Pointer press records drag scope without handling/capturing the press; native GridView retains click/Ctrl/Shift selection. GalleryDragGesture starts only once after movement reaches the Windows horizontal/vertical drag threshold. Grid-level handled-events-too movement/release/cancellation handlers track the gesture even when the item owns pointer routing. Explicit StartDragAsync then raises the tile drag-start event for the existing custom preview/payload. Release without threshold movement never begins a drag. Stale/recycled source tiles are rejected.

Starting a drag exits hover-to-fit; hover cannot reapply fit while dragging. Each preview layer is rendered after restoring the normal cropped tile and completing its layout, avoiding black hover-fit margins. Opacity (80%), top-left hotspot, bounded stack, selection snapshot, opaque token and safe grouped-move/history policies remain unchanged.

Verification: solution build zero warnings/errors, **327 unit/filesystem tests and 191 full native checks passed, zero failures/skips**; `git diff --check` passes. Gesture regressions cover click/release, small movement, horizontal/vertical thresholds, cancellation and single start. Native preview checks use a wide green image in hover-fit state and assert cropped green pixels replace black margins; native click routing remains enabled. Evidence: `%TEMP%\MediFiler3Integration-9416204ceb6843248aafdd521974042c`; log `%TEMP%\MediFiler3DragClickFix-final.log`. Physical click/drag feel and mixed-DPI still require interactive review. Changes remain uncommitted; the running app output was preserved.


## Translucent pointer-anchored drag preview (2026-11-11)

Gallery drags now use each tile's native UIElement DragStarting/DropCompleted events so DragUI can set an explicit top-left hotspot `(0,0)`. This supersedes the CanDragItems source event description below. The same pre-selection origin snapshot, opaque token, safe tree targets and grouped move command remain unchanged.

The preview is a compact stack with the originating tile on top and up to two additional currently realized selected tiles. RenderTargetBitmap captures only existing UI visuals, capped at 192px per tile with 8px stack offsets (208px maximum canvas dimension). All premultiplied BGRA channels, including alpha, are multiplied by 0.8 for 80% opacity. Original source tiles and thumbnail caches are unchanged. Large/offscreen selections retain their full operation snapshot without decoding or realizing extra items just for the preview.

Asynchronous capture uses a drag deferral and gesture-version checks; cancellation/context changes discard late results and release preview resources. Capture failures retain the native default preview and existing move operation. Native tests check rendered alpha, source opacity, bounded dimensions and top-left anchor in addition to the earlier drag/drop scope and safety checks. Final verification: clean build (zero warnings/errors), **326 unit/filesystem tests and 191 full native checks passed, zero failures/skips**; `git diff --check` passes. Evidence: `%TEMP%\MediFiler3Integration-5a3ba52babc746b0b95e29c57b5d7bf8`; log `%TEMP%\MediFiler3DragPreview-final.log`. Physical pointer/hotspot feel and DPI/fullscreen appearance remain interactive acceptance. Changes remain uncommitted; the running app output was preserved.


## Gallery-to-tree drag moves (2026-11-11)

Internal drag-and-drop from Gallery onto loaded tree folders is implemented. A pointer press captures an immutable naturally ordered snapshot: an unselected origin drags only itself; a selected origin drags the whole selected set. Native GridView drag support supplies the drag operation; its package carries a per-gesture opaque text token rather than accepting filesystem paths from external data. Gallery status shows Moving N files, the tree is revealed throughout the gesture, valid folder headers highlight pink, and allowed targets advertise Move. Same-folder, blank, stale-token, stale-context and unknown-tree targets are rejected. Existing Explorer folder loading is unchanged; external file export/import is outside this slice.

BrowseView.GalleryDrag.cs owns token/root/folder/revision/record validation, target cues and reconciliation deferral cleanup. WinUiGalleryIsland.Drag.cs captures origin selection before normal item selection and prepares/completes the native drag; GallerySelectionState.GetDraggedFiles supplies business scope. Drops reuse RunGalleryFileCommandAsync with the explicit captured files, preserving grouped collisions/partial outcomes/cancellation/root history/counts and thumbnail safeguards. Mode/activity/listing changes invalidate gesture scope. Native drag owns Escape/modifiers; cancel/completion/rejection clean up cues and deferrals. No directory scanning or filesystem mutation occurs in drag-over handlers.

Verification: solution build zero warnings/errors; **326 unit/filesystem tests and 191 full native checks passed, zero failures/skips**; `git diff --check` passes. New tests cover selection scope, native package preparation/caption, WPF target routing/highlight, rejected/cancelled/stale drags and one-group Undo. The first new native test attempts preceded deferred Gallery publication; tests now wait for the same realized listing required by a physical gesture, retaining stale-source guards. Full evidence: `%TEMP%\MediFiler3Integration-fd4f04c38c83486aad6031c651c6219f`; log `%TEMP%\MediFiler3GalleryDrag-final.log`.

Physical pointer threshold/drag preview/cursor, actual native-to-WPF OLE transfer, Escape cancellation, long tree scrolling and mixed-DPI/fullscreen dragging still need interactive acceptance. Automated routing uses a real native DataPackage and routed WPF drag events; it does not synthesize a physical OS pointer drag. Existing native stability limits remain. Baseline was clean user commit `6416ace` (`Bug fixes`); these drag changes remain uncommitted. Temporary build output preserves the running app.


## Browse history current-file selection (2026-11-11)

Browse file-only Undo now prefers the successfully restored/renamed file in the active folder. When Redo removes the current file, reload retains its prior natural-order index, selecting the next surviving file or the previous file at the end instead of jumping to the first file. Gallery keeps its existing current-file/selection policy. Tree identity, counts, watcher echo filtering and paused-media handling are unchanged.

Verification: clean solution build (zero warnings/errors), **325 unit/filesystem tests and 189 full native checks passed, zero failures/skips**; `git diff --check` passes. The new native regression exercises immediate move/delete Undo, Redo and the last-file neighbor fallback. Evidence: `%TEMP%\MediFiler3Integration-af2774dc67374ae88119faf7a325f106`; log `%TEMP%\MediFiler3BrowseHistorySelection.log`. Changes remain uncommitted; temporary build output preserves the running app. Existing physical/native stability limits remain open.


## Incremental operation reconciliation (2026-11-11)

Move Undo/Redo now updates both source and destination direct-file counts before completing in Gallery and Browse. File-only history reconciles the active listing without rebuilding roots. Folder commands/history and explicit refresh merge scanned roots/children into retained collections: unchanged nodes keep identity and expansion, renamed/replaced subtrees inherit mapped expansion, and removed active folders fall back to a surviving ancestor. Refresh still updates history availability even when the active root object is retained. No root visit is counted.

Created/deleted/renamed watcher hints carry structural-path metadata. Changed/content hints dominate structural hints for the same path; overflow/recovery remain strong. A bounded 512-path/30-second record of verified same-volume file moves can retain an existing source preview through delayed move/Undo echoes, after checking file identity/size/timestamp (or verified absence) through Infrastructure. Unknown/new destinations, overwrite destinations, changed identities, changed metadata, strong content hints and expired records retain normal invalidation. Strong invalidation also retires echo records. No cache keys, persistence schema, thumbnail crop or default-source policy changes.

Window shortcut commands are dispatched after native message processing with page/mode/root/folder/file/selection checks; modal input remains separate. An initial full native run stopped after the native-island F11 check: Windows logged Microsoft.UI.Xaml.dll `0xc000027b` (WER report `a6f5529d-57b1-4dc6-ad96-77d08cc3b42a`). Details are preserved in `%TEMP%\MediFiler3TreeFeedback-WER.txt`; log `%TEMP%\MediFiler3TreeFeedback.log`. Deferred dispatch addresses an identified lifetime risk, but the crash cause is not proven and native stability remains a verification limit. A subsequent history-availability timeout exposed a missing retained-root refresh notification and was fixed. Final working-tree verification: solution build zero warnings/errors; **325 unit/filesystem tests and 188 full native checks passed, zero failures/skips**; `git diff --check` passes. Final native evidence: `%TEMP%\MediFiler3Integration-c675e34e21194533b39e6cda12a7aa7e`, log `%TEMP%\MediFiler3TreeFeedback-complete.log`. Temporary output preserves the running app.

Regressions cover immediate counters in both views, retained roots/children/collapse state through file history and folder rename/delete/restore, delayed restored/unaffected thumbnail object identity, structural/content hint precedence, and rejection of a replacement file with identical metadata. Existing uncommitted work and all operation/backup/freshness safeguards remain; no commit/push performed.


## Phase 5.4 feedback update (2026-11-11)

View shortcuts now share window-level WPF/native message routing, independent of tree/toolbar/viewer focus, while owned modal dialogs retain their own input. Gallery Ctrl+A/Escape/Enter and command shortcuts route to Gallery; native grid modifier navigation remains native, with navigation from other focused controls directed to Gallery. Browse navigation/media/text/image/external-open shortcuts use its active viewer policy regardless of control focus.

Gallery single-file rename is available via F2, toolbar and context menu only when exactly one file is selected (the selected file is the target even if focus/current differs). Extension preservation, post-dialog context checks and collision safety remain. Bulk rename is not planned. Single-file recycle bypasses confirmation; multiple selected files require confirmation. This supersedes earlier rename-disabled/always-confirm-recycle statements below.

Ordinary directory Changed hints now request metadata reconciliation instead of invalidating all descendant thumbnails. Exact file hints, F5, overflow/recovery and degraded-coverage safety retain freshness invalidation. Actual native tests preserve the same decoded preview through cancelled/submitted Browse rename dialogs and Gallery moves plus real watcher reconciliation; the test resolves native containers using retained source records, not equal replacement session records.

Verification: clean solution build (zero warnings/errors), **322 unit/filesystem tests and 187 full native checks passed, zero failures/skips**; focused Gallery resume 10/10 passed. Full native evidence: `%TEMP%\MediFiler3Integration-b97188613bdd4df5b8b55c9f3dcd4373`; log `%TEMP%\MediFiler3Feedback-final.log`. Initial new-test failures were corrected in test container lookup and child-directory watcher setup. Physical input/Narrator/DPI/appearance/performance acceptance remains open. Work remains uncommitted; running app output was preserved.


## Phase 5.4 implementation update (2026-11-11)

This update supersedes the earlier 5.4-pending and uncommitted-baseline descriptions below. Work began from clean commit `f32617e` (`Better thumbnail generation`); Phase 5.4 changes are uncommitted. No commit/push was requested or performed.

Gallery now moves the naturally ordered selected-file snapshot through tree-right-click using `MoveFilesAsync`, and recycles selected files through one confirmed, journal-backed `SendFilesToRecycleBinAsync` batch. Toolbar/context-menu/Delete recycling and native Ctrl+Z/Ctrl+Y/root-scoped toolbar history are wired. Gallery rename remains disabled; bulk rename is not planned. Existing Browse and folder-menu behavior remains.

Commands capture root/folder/mode/revision and current file records, reject stale confirmations/collision choices, defer watcher reconciliation, release media and validate before publishing refreshed listings. Surviving selection is retained; removed paths are pruned without automatically selecting restored files. File history uses listing reconciliation where possible to retain the Gallery and previews.

Batch results include accumulated per-item outcomes, `UnattemptedCount` and `WasCancelled`. Ordinary item failures continue; collision cancellation, token cancellation and recovery-required uncertainty stop remaining work. Completed members remain one Undo/Redo group. Started mutations finish verification/durable journal events before cancellation takes effect between items. Windows recycle cancellation is classified without permanent-delete fallback. Existing identity checks, root scopes, preserving collisions/backups, compensation and cleanup ownership remain.

New regressions cover partial moves/cancellation, pre-cancelled batches, restart grouping, actual Windows grouped recycle/restore/redo, partial missing-item restore/retry, Gallery partial move/history, stale/cancelled confirmations and grouped recycle/history. Initial new native test setup failures were corrected (test placement and waiting for asynchronous folder discovery); production assertions were retained. Final verification on the completed working tree: solution build zero warnings/errors; **321 unit/filesystem tests and 184 full native checks passed, zero failures/skips**. Native evidence: `%TEMP%\MediFiler3Integration-4eaf6de6d2f54a4aab7c46f28ab3c252`; full log: `%TEMP%\MediFiler3Phase54-verified.log`. Build used temporary `MediFiler3ContinuationBuild` output to preserve the running app. `git diff --check` passes. Physical thumbnail/input/DPI/environment limits and Phase 5.5 acceptance remain open; thumbnail policy/crop/cache/native lifetime behavior is unchanged.


Updated 2026-11-11. Start with [PROJECT_STATUS.md](PROJECT_STATUS.md), which records fresh verification and the active product state. This handover is documentation-only. Earlier experiments/evidence remain in [PROJECT_STATUS_HISTORY.md](PROJECT_STATUS_HISTORY.md); dated architecture/plan entries are historical when they conflict with the current snapshot.

## Baseline and read order

Repository root is `C:\Users\xofas\RiderProjects\MediFiler3`, PowerShell, Windows 11 x64, .NET 10. Branch `master`, HEAD `0880e94` (`Gallery view selection initial`); post-HEAD source/test refinements are uncommitted. Preserve all of them. This handover does not stage, commit, push, reset, deploy or start 5.4.

Read AGENTS.md, PROJECT_STATUS.md, SPECIFICATION.md, ARCHITECTURE.md and DEVELOPMENT_PLAN.md. Use PHASE4_HANDOVER.md for operation/storage/fullscreen ownership caveats and PHASE4_ACCEPTANCE.md for still-open physical cases. Their aa68870 baseline and older counts describe Phase 4 history, not the current repository.

## Active decisions and completed work

- 5.1-5.3 are implemented. 5.4 grouped mutations and 5.5 acceptance/performance remain. Continuous reader/additional sorts/drag-drop are separate later work.
- Browse/Gallery share a collection workspace and current-file context, with lazy expensive presentation. Watcher lifetime spans these two modes and stops in Home/Settings. Return reconciliation is metadata-only for healthy startup and does not count root visits.
- Retain the Gallery grid, ItemsSource and decoded thumbnails across visits. Changed sources are invalidated selectively; full F5/overflow/recovery safety remains. Native list diffs are deferred until visible/loaded, and reveals wait for actual layout while yielding to newer input.
- Display sizes: independent Width/Height sliders, fixed control width 150, values 100-400/50-step/default 150, persisted at schema 7/application payload 1. Tile spacing is two pixels; rows center beside the pinned tree; filenames overlay a gradient and disappear during uncropped fit-hover.
- Current default crop is center-square framing followed by clipping into a rectangular tile. V2's direct rectangular fill has not been adopted.
- Selection is mode-local, path-based, case-insensitive and snapshotted in shared natural sort order. Native Extended selection owns Ctrl/Shift/range/arrow behavior. Alt-hover chooses add/remove on the first valid tile and visits each path once. Ctrl-hover does nothing special.
- Selected/current outlines are solid square five-pixel DeepPink (#FF1493), with no pattern/accent/secondary stroke. Carousel fade ghosts are square too. Counter has one white live-region label, decorative black outline copies and 24px right clearance.
- Manual blur is fully removed. `SoftThumbnailDecoder`/`SoftThumbnailChecks` are gone. Existing raw PNG decoding and native BitmapImage loading are used; WPF HighQuality scaling and source-fallback Fant resizing remain.
- Gallery file mutation/history commands remain guarded. Existing tree-folder ellipsis operations continue to work; do not accidentally disable those while preserving file-mutation guards.

## Code map

| File/area | Responsibility and continuation caution |
|---|---|
| Application/Navigation/NavigationSessionState.cs, NavigationController.cs | Shared roots/folder/files/current file, mode switch and stale navigation/boundary invalidation; retain current-record validation and no implicit folder rescan |
| Application/Navigation/GallerySelectionState.cs | Replace/toggle/range/focus/all/clear, native snapshots, removed-path reconciliation and current sorted command snapshots; focus is distinct from selected set/anchor |
| Application/Navigation/GalleryHoverSelection.cs | Alt-held add/remove direction and visited paths; owner-thread state only, reset at gesture/context boundaries |
| App/Views/BrowseView.Gallery.cs | Retained island, lazy modes, current-file handoff; new Gallery mode entry selects current once, Home/Settings/reveal preserve the existing set including explicit clear |
| App/Interop/WinUiGalleryIsland.cs | Native rendering, virtualization/recycling, bounded thumbnail requests/cache, pending file/reveal queues, clipping and batched/incremental source updates |
| App/Interop/WinUiGalleryIsland.Selection.cs | Selection synchronization/reentrancy guard, native focus, accessible status, Alt input, context menu and stale-action checks |
| App/Views/BrowseView.xaml(.cs), BrowseView.Carousel.cs | WPF carousel crop/raw decode, current pink overlay, square ghost frames and coalesced/cancelled bounded neighboring work |
| Application/Files/IThumbnailService.cs | Explicit-size contract plus GetDefaultThumbnailPngAsync; generic compatibility default is 256px, Windows overrides it |
| Infrastructure/Files/FileSystemThumbnailService.cs and .Maintenance.cs | Windows provider, shared PNG cache/gates/worker limits, default versus explicit buckets, verified ownership and persistent freshness markers |
| App/Views/BrowseView.Reconciliation.cs, Infrastructure/Folders/FileSystemFolderChangeMonitor.cs | Reconciliation deferral/context generations, watcher lifetime, startup versus strong invalidation and thumbnail refresh |
| App/MainWindow.xaml.cs, BrowseView.xaml.cs mutation/history methods | Gallery guard/availability boundaries; root/context revisions and playback release remain essential for later command integration |
| Infrastructure/Operations/FileSystemFileOrganizationService*.cs | Existing grouped moves, collision preservation, root-scoped journal/Undo/Redo and identity checks; inspect before adding grouped recycle |
| Tests/GallerySelectionTests.cs, GalleryHoverSelectionTests.cs | Business/session/gesture invariants |
| Native GalleryChecks.cs, GalleryInputChecks.cs, GalleryResumeChecks.cs, ThumbnailRenderingChecks.cs | Real retained islands, scoped automation/input seams, large lists, actual clipping regions, raw pixels and real-watcher resume |
| Tests/ThumbnailMaintenanceTests.cs | Both cache policies, normal/default sizing, source freshness, persistence/clear/housekeeping, cancellation and ownership |

Paths in the table are relative to their corresponding `src/MediFiler3.*` / `tests/MediFiler3.*` project. The three untracked implementation files listed in PROJECT_STATUS.md are required parts of the implementation, not optional test artifacts.

## Thumbnail contract details

Call **GetDefaultThumbnailPngAsync**, not GetThumbnailPngAsync with zero sizes. The real explicit-size method still clamps zero/negative inputs to at least one pixel. Only the Windows implementation's private cache core uses 0x0 to identify a default-size bucket; the diagnostic 0/0 recorded by CountingThumbnails is not a returned image dimension.

Normal defaults use `StorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem)` without an explicit size/options argument. Normalize to shared PNG while preserving Windows' actual returned size/aspect/orientation. Do not cap that result back to 400 or assume a square/guaranteed 256px result. Display sliders do not select backend source sizes. Default cache keys currently do not include monitor DPI; warm results retain their creation-time dimensions, so live DPI changes do not force regeneration.

Explicit-size clients (viewer fallback, artwork/collision requests, tests) keep their original bounded resize contract and old key hashes. Default cache keys are separate from old 400px entries; no blanket purge/migration occurs. The first default request can be cold even if a fixed-size entry exists, then Gallery/carousel reuse one default PNG via existing per-key gates. Retention remains encoded 512 entries/32 MiB, native decoded 512 entries/128 MiB, with three decoder workers at the shared service/native Gallery boundaries and cancellation after permit acquisition. Native byte accounting uses actual image dimensions.

Known-invalidated images must not trust stale Windows system thumbnail pixels. The fresh-source WinRT decode path remains, bounded at 256px for default requests without upscaling (explicit requests use their requested bounds). Image fallback uses that source path when Windows supplies an icon/no preview; non-image icons remain placeholders.

RequiresFreshSource is optional schema-7/payload-1 metadata and may persist in cached entries/zero-byte freshness markers across restart, housekeeping and explicit image-cache clearing. F5 therefore exercises fresh-source rendering, not normal Windows-default thumbnail retrieval. Use new/uninvalidated disposable sources to compare normal provider quality; do not disable freshness just to mimic V2. Old cached PNGs were never blurred on disk, so removing the former display-only filter needs no blur-cache purge.

## Native lifetime, selection and reconciliation caveats

1. **Collection updates:** use the latest coalesced pending snapshot at Render priority after both WPF host and native grid are loaded/visible. Complete disjoint/empty listings use one Reset on the same ObservableCollection after cancelling obsolete realized tiles/selection/hover. Lists with surviving paths stay incremental. The decoded LRU is not globally flushed. Repeated 80/10,000-file returns assert four resets/no individual index notifications and stable ItemsSource identity.
2. **Crash history:** `0xC0000005` has intermittently occurred inside WinRT collection notifications. Earlier attribution to manually constructed test peers was inconclusive; tests now use real GridView-owned automation peers, but the crash later recurred even with layout deferral. Atomic disjoint replacement is the latest targeted fix. Final continuation and handover runs passed; no blanket native-stability guarantee is justified. Preserve traces and identify whether a recurrence concerns shared-path versus full replacements.
3. **Selection synchronization:** `_updating` suppresses native/session feedback during programmatic changes. Focus-only movement can update shared current file without changing selection or starting Browse rendering. Deferred callbacks check activity/current membership/focus to reject obsolete work. Reveal targets are independent of GridView.SelectedItem, which is not the whole selected set.
4. **Selection lifetime:** mode switch/folder/root changes clear mode-local selection; Home/Settings visits preserve it. Native source reconciliation prunes any old-grid input received between session publication and deferred presentation. A reveal/refresh must not reselect a user-cleared selection.
5. **Alt painting:** first valid tile chooses direction, path membership is validated, revisits are inert. Release, grid exit, changed listing, deactivation and destruction reset it. No Ctrl-click/tap interception remains; native Ctrl+Shift routing must stay intact. Physical Alt/access-key interaction remains unverified.
6. **HWND airspace:** pinned tree flags/WPF transforms can be correct while native Gallery covers them. Reattachment may reset the HWND region without changing cached dimensions/exclusions. Reapply exclusions on activation/loading/visibility/arrangement and at coalesced Loaded priority. Existing rendering still caches unchanged region keys. Tests query GetWindowRgn/PtInRegion, not only bookkeeping.
7. **Pending reveal:** wait for measured/loaded viewport and retry through layout until actually visible; cancel on deactivation, newer requests or user pointer/wheel/key/selection input. Do not add a polling timer or reload an equal listing to position a selection.
8. **Watcher hints:** healthy startup uses metadata-only ReconcileRoots. Delivered paths, F5, overflow and failed-root reconnection keep strong invalidation. Unchanged metadata edits made while watcher coverage is stopped still need F5. Watchers stop in Home/Settings; automatic work never records root visits.
9. **Counter/menus:** outline copies are decorative accessibility-raw elements, not extra live-region labels. Menus close/reject old listing/activity revisions; blank-space Open cannot use an unrelated Browse current file.

## Safe-operation and persistence carryover

The full Phase 4 ownership/identity/compensation rules remain active; see PHASE4_HANDOVER.md. Never silently overwrite or permanently delete media. Retain explicit collision choices, preserving backups, same-volume verified storage, root-identity-scoped history, grouped compensation, recovery safety, cleanup retirement-before-delete, reconciliation deferral around dialogs/commands, paused media reopening and no filesystem work in UI handlers.

Gallery rename/delete/tree-right-click move/history execution currently return early and toolbar availability is disabled in Gallery. GetRootOperationsAsync's active-context checks also reflect Browse-only file execution. Future 5.4 needs an explicit validated Gallery command scope; do not bypass guards by temporarily changing viewing mode or loop single-current-file handlers over a selection. Snapshot current selected records/root/folder and validate after dialogs/context changes.

MoveFilesAsync already exists. Grouped journal-backed Recycle Bin support and Gallery wiring remain unimplemented. Determine collision cancellation/partial results, user confirmation, source selection/reconciliation and one-group Undo/Redo behavior before implementation. Existing folder-menu operations are a separate already-supported path.

## Investigation retained for follow-up

The reviewed V2 file is pinned at [e82c600e80c559be80875865873e92e5a35b669a](https://github.com/DavidS1998/MediFiler-V2/blob/e82c600e80c559be80875865873e92e5a35b669a/Code/FileThumbnail.cs). It feeds Windows' default SingleItem thumbnail into a native BitmapImage without a custom blur. Its Gallery uses ordinary rectangular UniformToFill and switches to Uniform on hover. Only default retrieval/no-blur were adopted here; V3's square-first crop and WPF carousel remain. Cropping/resampling and DPI are plausible contributors to perceived jaggedness, not a proven single cause. A controlled same-file/same-size/same-DPI comparison remains useful if the user reports a problem. Do not copy V2's index-based/unbounded caching or unawaited load/lifetime patterns.

## Verification and next conversation

Fresh handover build is clean; 316 unit/filesystem and 181 full native checks passed without failures/skips. Native artifacts: `%TEMP%\MediFiler3Integration-6e42b77c512642eabed5c157de2a51cb`. The full run includes the 31-check Gallery and 9-check resume subsets, so they were not redundantly rerun. Commands and temporary-output/fixture caveats are in PROJECT_STATUS.md and the runner README.

Physical focus/input/Narrator, latest thumbnail appearance/loading cost, live DPI/monitor transitions, network/removable/permission cases and the Phase 4 checklist remain open. An isolated carousel fade assertion has been timing-sensitive around its 110ms lifetime; record a failure rather than weakening assertions or claiming a rerun proves the original failure absent. No installer or real external-launch/picker acceptance was performed. Normal builds do not hot-update an already-running app; launch a current build deliberately when reviewing appearance.

First review the user's current thumbnail feedback, then agree a concrete next 5.4 slice if proceeding. No feature work is authorized by this documentation-only request. Preserve the uncommitted implementation and do not auto-commit/push.
