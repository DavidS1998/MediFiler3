# Phase 4 and UX continuation handover

> Historical Phase 4 baseline. Current Phase 5 state/Git/verification are in [PROJECT_STATUS.md](PROJECT_STATUS.md) and [PHASE5_HANDOVER.md](PHASE5_HANDOVER.md). Keep the safety and ownership policies below, but do not use the older baseline/counts or Gallery-not-started continuation instructions as current status.

Updated 2026-11-10. Start with PROJECT_STATUS.md for the consolidated snapshot, ARCHITECTURE.md for policy and PHASE4_ACCEPTANCE.md for evidence/physical limits. Earlier pre-4.6 proposals and uncommitted-state notes are superseded by this handover.

## Baseline and scope

Implementation is committed at `master`, **`aa68870` (`Fullscreen support`)**, 19 commits ahead of `origin/master` at handover start. Git was clean; this handover adds documentation-only edits and makes no commit/push. Inspect status/history next time and preserve subsequent user work.

Phase 3 is manually accepted. Phase 4 steps 4.1-4.6 and automated 4.7 acceptance are complete, including watcher/cache integration and later UX changes. The user tentatively accepted Phase 4 but could not test every physical use case. Dialog appearance and the root-label flicker fix were explicitly accepted. Newest changes: native Settings, persisted carousel off switch, compact centered Home, white filled favorite stars, optimized/fading carousel, session-only fullscreen with coordinated edge reveals, and native `.jfif` image/thumbnail routing.

Fresh handover verification: **276 unit/filesystem tests and 144 native checks passed, zero failures/skips; build zero warnings/errors**. Physical hover/DPI/taskbar/network/removable/permissions/input/animation checks and forced monitor-enumeration failure remain unverified; do not report them as passed.

## What to do next

No implementation was started by this handover. The recommended next feature is Phase 5 Gallery, then continuous reader. Review the specification and propose Gallery scope/slices before implementation: virtualization/tile sizing, keyboard/Ctrl/Shift selection, Browse handoff, grouped operations and watcher/cache/root-context integration. Neither viewing mode exists yet. A new checkpoint of source changes is not needed to obtain the old recommendation: source work is already in aa68870; the new handover Markdown may still be uncommitted.

Do not make exhaustive physical acceptance a new prerequisite after the user's tentative acceptance. Retain the known limits and address concrete user-reported defects if they appear. Do not add extra codecs, themes, settings editors or unrelated features without discussion.

## Code map

| Location | Continuation relevance |
|---|---|
| `src/MediFiler3.App/App.xaml.cs` | DI/startup/recovery; settings/layout before window; flush before host shutdown. |
| `src/MediFiler3.App/MainWindow.xaml.cs` | Home/Browse/Settings composition, root loading, shared commands, persistence wiring and backup cleanup confirmation. |
| `src/MediFiler3.App/MainWindow.Fullscreen.cs` | Monitor-sized transient fullscreen, F11/native message filter, 40 ms edge cursor polling, native caption reveal and combined sidebar/tree reveal. |
| `src/MediFiler3.App/Views/BrowseView.xaml.cs` | Explicit root-visit boundary, progressive hierarchy/count lifecycle, F5, operations/media coordination and stable carousel updates. |
| `src/MediFiler3.App/Views/BrowseView.Reconciliation.cs` | Awaited bounded dispatcher consumer, command deferral/context revisions, identity-preserving hierarchy merge and selection/media reconciliation. |
| `src/MediFiler3.App/Views/BrowseView.Carousel.cs` | Carousel enabled/fullscreen state, forced tree reveal/offset, work cancellation/resumption, render-priority coalescing and bounded fade visuals. |
| `src/MediFiler3.Application/Navigation/` | Session/root ownership, file-index map, selection and boundary navigation. Reuse for new viewing modes. |
| `src/MediFiler3.Application/Folders/FolderTreeEntry.cs` | Stable nodes/count propagation; unchanged rescans/labels must not reintroduce root flicker. |
| `src/MediFiler3.Application/Persistence/` | Typed settings/layout/history/cache contracts and services. Schema remains 7, JSON versions remain 1. |
| `src/MediFiler3.Infrastructure/Folders/` | Progressive discovery/counting, bounded root probes and FileSystemFolderChangeMonitor. FolderChangeBuffer contract is in Application. |
| `src/MediFiler3.Infrastructure/Files/FileSystemThumbnailService*.cs` | WIC/Shell PNG generation, worker/memory bounds, index/retention/invalidation and synchronized maintenance. |
| `src/MediFiler3.App/Services/BitmapViewerHandlers.cs` | Image routing, including `.jfif`; keep thumbnail WIC extension routing consistent. |
| `src/MediFiler3.App/Interop/WinUiCarouselSettingsIsland.cs` | Despite its old name, hosts the entire native Settings page. Callbacks are bridged to WPF; tests wait for native controls. |
| `src/MediFiler3.App/Interop/WinUiHomeIsland.cs` | Centered bounded Home cards, compact header, native stars (white filled when on), shared actions and live captions. |
| `src/MediFiler3.App/Interop/NativeSurfaceOrder.cs`, `WinUiMediaIsland.cs` | HWND stacking and native video exclusion regions; preserve native-overlay reachability. |
| `src/MediFiler3.App/Services/WindowLayoutController.cs`, `Interop/WindowsWindowPlacement.cs` | Capture/flush order, transient fullscreen suppression, monitor/DPI and workspace/screen conversions. |
| `src/MediFiler3.Infrastructure/Operations/` | Root-scoped safe mutations, identities, compensations, backup ownership and OperationMaintenanceGate. |
| `src/MediFiler3.App/Interop/WinUiContentDialogHost.cs`, `WinUiToolbarIsland.Dialogs.cs`, `WinUiToolbarIsland.Collisions.cs` | Dedicated dialog root/lifetime/modal behavior and cancellable previews. |
| `tests/MediFiler3.Infrastructure.Tests/` | SQLite/migrations/history/filesystem/cache/watcher/count/index regressions. |
| `tests/MediFiler3.App.Integration/` | Explicit native runner: viewer/media/GIF/Home/dialog/layout/watch/UX checks and three fresh-process restart workers. Not executed by dotnet test. |

## Policies and less-obvious safeguards

The full agreed watcher/cache/UX policy is in ARCHITECTURE.md and summarized in PROJECT_STATUS.md. Keep these additional details in mind:

- Settings CarouselEnabled defaults true for old payloads; disabling does not remove stored density or disable viewer artwork/fallbacks. Do not confuse a one-icon carousel with off.
- NavigationSessionState's file-index map preserves record-equality membership and is rebuilt on collection changes, not each navigation step. Stable neighboring carousel objects and cancelled-item ThumbnailTask reset are necessary for smooth resumption.
- Carousel edge ghosts are noninteractive and capped at 24; release them on completion/context reset. Use client-area animation preference. Do not delay real collection mutation to wait for a fade.
- Fullscreen has no persisted flag. Capture normal layout before entering; keep persistence suppressed through native-caption reveal and shutdown. Caption uses actual WindowStyle changes; negative client-relative Y is valid over nonclient title-bar controls. The monitor's outer pixel rectangle stays fitted when caption visibility changes.
- Fullscreen tree/sidebar reveals share a lifetime and tree offset. WPF mouse-leave crossing into native sidebar must not hide the forced tree. Native video clipping includes the offset; pin state stays independent.
- NativeSurfaceOrder raises toolbar/sidebar above Home/Settings HWNDs. WPF ZIndex alone cannot order native surfaces. Folder menus reveal their toolbar anchor; open native popups hold the toolbar surface.
- Hierarchy reconciliation preserves existing nodes/expansion. Unchanged children must not transition known totals through unknown; DisplayName notifies only when its actual text changes. Initial discovery and genuinely changed/incomplete counts still use unknown placeholders.
- Watcher notifications are hints; bounded rescans converge state. Never count automatic work as root opens or guess new saved paths. Reuse the cancellation/context-generation guards in new modes. Native media must stay paused on affected reloads, and unrelated changes must retain its session.
- Cache quota is an eventual idle target; legacy/locked/untracked items may affect reported usage. Cache, source media, overwrite backups and append-only operation history have distinct ownership/lifetimes. Never combine their cleanup policies.

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

## Verification and limits

Use the build/test/native commands in PROJECT_STATUS.md and the runner README. Build first, then --no-build runners; never launch concurrent builds sharing obj files. Keep a user's running app open and use a consistent temporary BaseOutputPath if needed. The supplied temporary media fixture directory existed and the fresh native run had zero skips. Actual launching/choosers, subjective input/appearance, audible output, every codec variant, deployment and physical network/monitor transitions are not established by automation.

Existing limits: expanded tree virtualization is off; very large trees/GIF canvases remain costly; codec support is Windows-dependent; MKV fixture is the WebM/Matroska subset; same-size/timestamp edits without notifications need F5; blocked OS calls cannot be forcibly cancelled. PHASE4_ACCEPTANCE.md retains the unchecked physical scenarios. No unresolved reproducible issue is currently reported after the accepted flicker fix, but latest UX changes still benefit from user review.

## Suggested opening prompt

Continue MediFiler3. Read MediFiler3/AGENTS.md, SPECIFICATION.md, ARCHITECTURE.md, DEVELOPMENT_PLAN.md, PROJECT_STATUS.md, PHASE4_HANDOVER.md and PHASE4_ACCEPTANCE.md. Inspect Git status/history; the implementation handover baseline is aa68870 (Fullscreen support), with documentation-only updates possibly uncommitted. Phase 4 and its UX refinements are implemented and tentatively accepted; 276 unit/filesystem tests and 144 native checks passed. Summarize the current architecture, safeguards and known verification limits, then propose the Phase 5 Gallery MVP and implementation slices before changing code. Preserve root history, safe collisions/backups/Undo, native dialogs/media, cache/watchers, normal/fullscreen layout, carousel behavior, centered Home/white stars and .jfif routing. Gallery and continuous reader are not implemented.
