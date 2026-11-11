# Phase 5 continuation handover

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
