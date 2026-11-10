# Phase 4 handover: Home and persistence

Updated: 2026-11-10. Steps 4.1 through 4.4 are implemented; 4.5 is next. New Home interactive acceptance remains open. Phase 3 manual interactive review is accepted by the user.

Approved policy updates supersede the original proposals below: usage counts explicit opens of a specific root only, with no persisted subfolder history. Missing/renamed roots stay listed for user-directed manual location or removal. Persist both volume and mute. Settings and Home wiring are implemented; layout/cache wiring remains pending.

Implemented storage: schema 4; ApplicationSettings, WindowLayout, RootFolderHistory and ThumbnailCacheIndex contain independently versioned JSON payloads. Typed contracts live in Application/Persistence; SqliteApplicationStateStore is registered in App. Migration from schemas 1/2/3 is transactional through the final version update. 151 unit/filesystem tests pass, including upgrade/reopen and failure rollback/retry. The original baseline/code-map details below describe the pre-4.1 starting point where stated.

## Start here

Read [AGENTS.md](AGENTS.md), [SPECIFICATION.md](SPECIFICATION.md), [ARCHITECTURE.md](ARCHITECTURE.md), [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md), and [PROJECT_STATUS.md](PROJECT_STATUS.md). Phase 3 evidence and user interactive acceptance are in [PHASE3_INTEGRATION.md](PHASE3_INTEGRATION.md).

The working tree was clean before this documentation pass, on `master` at `c903b68`. GIF support and the integration runner are tracked files. Inspect the current status/diff when resuming and preserve subsequent work. This handover does not create a commit.

**Next implementation task: Step 4.5, validated window/panel layout persistence.** Step 4.4 Home is implemented with native WinUI cards and user visual/input/DPI/picker/drop review pending. Step 4.2 restores/saves volume, mute, carousel density and pin, coalesces writes and flushes on shutdown. The added center-toolbar Refresh/F5 preserves file path or nearest valid index. Verification: 157 unit/filesystem tests and 87 native checks, zero skips; build clean.

## Baseline before step 4.1 (historical)

| Area | Implemented | Phase 4 gap |
|---|---|---|
| Host | .NET 10 WPF; Windows 11 x64; Windows App SDK 2.6.4 islands for toolbar/navigation/native controls | Preserve this boundary and the compact navigation rail |
| Home | Three placeholder sections for recent, frequent and favorite folders; Home is the startup destination | Real data, folder activation, remove-from-recent and favorites actions |
| SQLite | `%LOCALAPPDATA%\MediFiler3\medi-filer.db`; schema version 3; append-only operation journal; WAL/FULL durability; startup recovery | Settings, folder usage/favorites, layout and cache-index records |
| Settings UI | Carousel density, applied immediately; 1-25 odd total icons, default 11 | Save/restore existing values and add the necessary settings wiring |
| Audio settings | Shared mute/volume across media in the current viewer session; new players apply them before opening | Persist mute across sessions as explicitly required; saving volume is required |
| Layout | Fixed initial window/panel dimensions; folder pin is session-only | Restore window bounds/state and supported panel preferences safely |
| Thumbnails | PNG disk cache plus 32 MB/512-entry memory LRU; three workers; cache keys include path/size/time/dimensions | Persist cache information, invalidate/prune safely, define limits |
| Filesystem changes | Application operations refresh affected state; external changes need explicit refresh/reload | Availability reporting and bounded reconciliation/watchers |
| Viewers | Static images/WebP, animated GIF with one-cycle duration, bounded text, audio/video, external opening | Preserve behavior and resource release while adding settings/history |

Pre-4.1 functional verification: **141 unit/filesystem tests and 81 native integration checks passed**, with zero skips using the complete local fixture set. The subsequent tooltip fix changed display separators to ASCII and was build-checked. The documentation pass does not rerun those functional suites.

## Recommended implementation sequence

| Step | Deliverable | Acceptance focus |
|---|---|---|
| 4.1 (complete) | Typed Application contracts/models; Infrastructure stores; migration from journal-only schema | New/reopened databases and all supported upgrade paths preserve journal intents, events, compensation links and undo/redo candidates |
| 4.2 | Persist existing settings, restoring volume and mute before any media opens; carousel density and folder pin | Restart round trips, defaults/invalid-value handling, current-session updates and orderly flush |
| 4.3 (complete) | Root-only usage/favorites service and bounded availability checks | Defined visit counting, stable path keys, recent/frequent ordering, independent recent removal/favorites, retained unavailable entries |
| 4.4 (implemented; manual review pending) | Populate Home with synchronized root-favorite stars and missing-root actions | Open through the existing root/folder navigation workflow; new-folder loading, empty states, async unavailable/error handling and refresh |
| 4.5 | Persist supported window/panel layout and navigation preferences | Visible bounds after monitor/DPI changes, valid normal/maximized restoration, no startup media autoplay |
| 4.6 | Reconcile external filesystem changes and add cache metadata/maintenance | Debounced/cancellable refresh, watcher overflow recovery, coherent selection/counts/thumbnails, deletion restricted to owned cache entries |
| 4.7 | Restart and integration acceptance | Settings/history/favorites/layout survive sessions; journal behavior and existing viewers remain correct; regress accepted Phase 3 interactions |

Keep each step reviewable. Do not add gallery/continuous-reader modes, animated WebP, broad codecs, playback speed, adjustable text page sizes, new themes or shortcut editors as incidental work.

## Persistence design guidance

Keep contracts and typed values in Application; keep SQL, path availability checks and watchers in Infrastructure. WPF/WinUI objects and UI dispatch stay in App. The existing `SqliteDatabase` is a suitable store boundary; settings/root-history/layout/cache-index tables are separate from journal tables. Reuse the existing database unless implementation evidence warrants a different store.

The current schema is version 4. Fresh creation and upgrades from versions 1, 2 and 3 run transactionally; only the final migration sets user_version. Rollback/retry, reopening and unknown-newer-version rejection are tested. Keep prepared-operation recovery conservative and journal writes append-only.

Give settings explicit defaults and validation. Persist changes through application-owned services, coalesce rapid slider/window updates, and flush outstanding writes during shutdown. Avoid dispatching settings SQL on every media progress tick. Capture UI state while the dispatcher is alive; hosted-service shutdown should flush captured data without waiting for a stopped UI dispatcher. Local store loading must not trigger filesystem scans or block the initial Home view on missing network roots.

Restore shared volume and mute before constructing/opening a media session. `MediaAudioSettings` currently lives inside `ViewerHostView`; persistence will need deliberate injection/initialization and change propagation through the existing viewer/Browse/shell boundary. Saving only when leaving media would miss changes at shutdown. Preserve paused media opening and the existing per-session resource lifetimes.

Store layout in logical units and use the normal restore bounds for a maximized/minimized window. Validate dimensions, clamp the window into a visible work area, and restore only normal/maximized state. The specification starts at Home; restoring recent history does not itself imply reopening the previous collection or starting playback.

Thumbnail images already persist on disk. Add metadata/retention around that service instead of generating a second cache. Cache maintenance must stay inside the resolved application-owned cache directory and must never remove source files. Missing/corrupt cache data should remain recoverable through regeneration.

## Policies to settle during their step

Rows marked approved record user decisions. Other entries remain proposed defaults or unresolved details for their step.

| Topic | Recommended starting point / decision needed |
|---|---|
| What counts as a visit? | Approved: count successful explicit opens of the root only. Subfolder browsing/overscroll has no persisted history; exclude refresh, reconciliation and probing. |
| Entry removal | One Remove entry action removes the root from all Home lists, including favorites and saved usage; source folders remain untouched. |
| Folder identity | Reuse current Windows path-normalization conventions and keep the original display path. Approved: renamed/missing roots remain until the user manually locates or removes them; never guess identity from names. Locate rejects a destination that already has a saved entry; no automatic merging. |
| Frequent ordering | Persist integer counts; define deterministic tie-breaking, such as last-opened time then path. No silent retention pruning is implemented. Home display limits can be chosen separately. |
| Unavailable folders | Distinguish missing, access-denied and temporarily unavailable; retain saved entries and check asynchronously, especially network/removable roots. |
| Settings scope | Volume and mute persistence are both required. Start with existing controls/defaults; extra preferences require separate discussion. A decimal-separator setting and additional customization remain optional follow-ups. |
| Layout/navigation | Save existing window bounds/state and panel preferences first. Define which expansion/selection preferences are worth persisting; startup Home remains the current requirement. |
| Watcher scope | Start with active/loaded context, debounce bursts, handle overflow with refresh, release watchers on root/tab/shutdown changes, and coordinate with application-initiated mutations. Avoid a watcher for every discovered folder. |
| Cache limits | Choose age/size limits, last-access bookkeeping and cleanup timing before implementing pruning; there is currently no disk quota. |

Step 4.3 implementation: RootFolderHistoryService provides lists, RecordOpenedRootsAsync, SetFavoriteAsync, CheckAvailabilityAsync, LocateAsync and RemoveAsync. Changed may run on a worker; Home must dispatch and reject obsolete results. Explicit root loads record accepted paths centrally in BrowseView.LoadRootsAsync, so Home must call the existing shell root-load workflow rather than count separately. Favorites are root-only, alphabetical and Home-managed; all Home list entries derive their star state from the same record. Locate preserves stats/favorite state without adding a visit, requires accessibility and atomically rejects destination collisions. Availability uses two worker slots and three-second caller timeouts. Tests: 167 unit/filesystem and 89 native checks passed; build clean. Home controls are now implemented: WinUiHomeIsland owns native cards/control intents; HomeView owns async orchestration and dispatcher/generation checks; MainWindow bridges pickers and the existing root-loading workflow. Lists publish before progressive availability. Native checks cover service-backed actions, responsive spacing, star/unstar, root opening, empty states, reopen and stale probes. Current verification: 167 unit/filesystem tests, 99 native checks, zero skips; solution build clean. Home physical keyboard/focus, DPI, picker/drop and appearance await review.

## Code map

| Entry point | Why it matters |
|---|---|
| `src/MediFiler3.Infrastructure/Persistence/SqliteDatabase.cs` | Database location, schema/version, durability and hosted startup |
| `src/MediFiler3.Infrastructure/Persistence/SqliteFileOperationJournal.cs` | Existing journal persistence to preserve |
| `src/MediFiler3.App/App.xaml.cs` | DI, database/recovery startup, host shutdown |
| `src/MediFiler3.App/MainWindow.xaml.cs` | Home/Settings construction, settings/viewer bridging, folder picker/drop flow and window lifecycle |
| `src/MediFiler3.App/Views/HomeView.xaml` | Placeholder collections to populate |
| `src/MediFiler3.App/Views/SettingsView.xaml.cs` | Existing carousel preference event |
| `src/MediFiler3.App/Views/BrowseView.xaml.cs` | Root/folder activation, operations, panel pin and refresh/cancellation boundaries |
| `src/MediFiler3.App/Views/ViewerHostView.Media.cs` | Current viewer-owned shared audio settings |
| `src/MediFiler3.Application/Viewing/MediaPlayback.cs` | `MediaAudioSettings` and pre-open application to each player |
| `src/MediFiler3.Application/Navigation/NavigationSessionState.cs` | In-memory roots/folder/file selection; no persistence yet |
| `src/MediFiler3.Application/Navigation/NavigationController.cs` | Overscroll, selection and cancellation; history counting must distinguish automatic probes |
| `src/MediFiler3.Infrastructure/Files/FileSystemThumbnailService.cs` | Existing PNG cache, workers, LRU and file keys |
| `tests/MediFiler3.Infrastructure.Tests` | Isolated database/filesystem tests and application business logic coverage |
| `tests/MediFiler3.App.Integration` | Explicit real WPF/WinUI smoke runner; launch/association adapters are stubbed |

## Verification and carryover

```powershell
dotnet restore MediFiler3.sln
dotnet test MediFiler3.sln --no-restore --verbosity minimal
dotnet build MediFiler3.sln --no-restore --verbosity minimal
git diff --check
```

Native checks run explicitly:

```powershell
dotnet run --project tests/MediFiler3.App.Integration -- "$env:TEMP\MediFiler3Phase3Fixtures"
```

That fixture directory is local temporary data, not a repository dependency. On another machine follow the [runner README](../tests/MediFiler3.App.Integration/README.md) and supply valid fixtures. The default run generates local image/text/WAV/GIF fixtures but reports missing external-format fixtures as skips. Do not equate a run with skips to complete codec validation. MKV coverage is a WebM/Matroska subset, not arbitrary MKV support.

Phase 3's user-accepted interactive checks cover: popup/DPI and physical focus/input, video overlay reachability, audible output/real album artwork, native default-app/Open With interaction and full Browse mutation/undo/redo workflows with active media. The user confirmed these checks pass on 2026-11-10. Tree virtualization and very large GIF CPU/memory costs remain known limitations.

Phase 4 is complete when its persisted values survive restart, Home opens saved folders correctly and retains unavailable entries, settings apply before relevant work starts, reconciliation/cache maintenance remain bounded, and migration/operation-journal/viewer regressions are covered. Record evidence and remaining limits in PROJECT_STATUS rather than replacing them with a blanket completion claim.

Latest Home policy supersedes historical checks above: no Ignore and no recent-only removal; Remove entry clears the saved root from all lists. Compact rows use live relative time, with exact time/path on hover. RootFolderStatus no longer has Ignored; IgnoreUnavailableAsync and RemoveFromRecentAsync were removed. Explicit bounded widths keep cards visible beyond the 1600 px content cap. Latest tests: 188 unit/filesystem, 99 native checks, zero skips; clean build.

Current SQLite schema is 5: FileOperations.RootScope (nullable/indexed) partitions Undo/Redo by Windows root-folder identity. Resolve services via ForRootAsync(rootPath); never use global history in UI. NavigationSessionState.ActiveRoot uses node ancestry. File commands use source/active root, folder-menu commands use target-node root, new redo branches stay local, and scoped compensation cannot link another root. Prior unscoped rows are retained for recovery/inspection but not assigned to any active-root stack. Migration from 4 preserves state and journal data with rollback/retry coverage. Current verification: 195 unit/filesystem tests, 101 native checks, zero skips; temporary-output build clean while user app runs.

Collision behavior now uses FileCollision/IFileCollisionResolver and WithCollisionResolver on the root-scoped service. App provides an owned WinUI Rename/Overwrite/Cancel prompt. Explicit replacements preserve hidden app-managed backups in one batch; whole-folder replacement is explained and does not merge. History blockers use linked compensation/event metadata and are restored with the opposite action. New payload fields are additive; schema 6 guards their history semantics against older builds without changing existing records or tables. Do not prune .MediFiler3-Undo-{guid} backups as cache. RecoveryRequired prevents unsafe retries after failed restoration. Current verification: 241 unit/filesystem tests, 109 native checks, zero skips; clean temporary-output build. Physical collision-dialog review is pending.

Backup management follow-up: new replacements use dedicated app-managed same-volume storage; legacy sibling paths remain readable/clearable. Settings Clear overwrite backups explicitly confirms permanent deletion and affected history loss. Schema 7 adds HistoryDiscarded terminal events without dropping records; all owning originals in a group are retired before deletion. Cleanup shares the mutation gate, checks ownership/identity/reparse/recovery state and reports retained items. Never clear the backup directory blindly or prune it as thumbnail cache.
