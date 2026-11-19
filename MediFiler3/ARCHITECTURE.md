# Architecture

Current implementation and invariants. Product behavior is in [SPECIFICATION.md](SPECIFICATION.md), milestones/evidence in [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md), active concerns in [PROJECT_STATUS.md](PROJECT_STATUS.md). Read [AGENTS.md](AGENTS.md) before editing.

## Documentation ownership

Keep exactly six authoritative documentation files under `MediFiler3/`; don't recreate phase reports, history archives, documentary JSON/text or a separate test-runner README. Consolidate durable information by purpose and remove superseded notes rather than append contradictory milestone logs.

| Document | Owns |
|---|---|
| AGENTS | Working instructions; preserve user edits |
| SPECIFICATION | Current product requirements and decisions, feature/format/input tables, explicit deferred scope |
| ARCHITECTURE | Current component relationships, implementation/safety mechanics, build/test/fixture and diagnostic guide |
| DEVELOPMENT_PLAN | Major delivered milestones, acceptance/measurement summaries and long-term remaining scope |
| PROJECT_STATUS | Outstanding issues/decisions, verification gaps and immediate next actions; no completed-work diary |
| HANDOVER | Most recent task, local session state and continuation hints; replace freely, don't store enduring policy/history here |

## 1. Stack and project boundaries

Windows desktop, C#/.NET 10, target `net10.0-windows10.0.19041.0`. WPF owns startup, window composition and navigation. WinUI XAML Islands provide native controls and media. Development builds are unpackaged and framework-dependent (`WindowsPackageType=None`) and use the Windows App SDK bootstrapper. The WindowsX64 publish profile bundles .NET and the Windows App SDK in an unpackaged release folder.

| Project | Responsibility / entry points |
|---|---|
| `src/MediFiler3.Domain` | File/folder/operation records, Windows logical filename comparer |
| `src/MediFiler3.Application` | Navigation/session, Gallery selection/drag policy, Reader projection/geometry, viewer coordination/capabilities, file/folder/operation/persistence contracts, settings/root history/layout services |
| `src/MediFiler3.Infrastructure` | Filesystem discovery/listing/counting, Windows image/thumbnail/text/external services, watchers, SQLite/journal/recovery, identity/Recycle Bin/backups |
| `src/MediFiler3.App` | WPF App/MainWindow/views, DI, WPF image/text/GIF adapters, WinUI hosts/media/dialogs/input/airspace/window placement |
| `tests/MediFiler3.Infrastructure.Tests` | xUnit Application business rules and isolated filesystem/storage/cache/recovery tests, despite the name |
| `tests/MediFiler3.App.Integration` | Explicit Windows executable with real dispatcher/WPF/WinUI/native media, restart workers and isolated fixtures; separate from `dotnet test` |

There is no separate WinUI project. Framework-specific controls/pixels/presentations stay in App; contracts use domain records, encoded bytes or bounded decoded data. Keep filesystem work out of UI handlers and format switches out of central navigation.

The default build uses Windows App SDK 2.7.0 stable, XamlAnimatedGif 2.3.2, Microsoft.Extensions.Hosting 10.0.0 and Microsoft.Data.Sqlite 10.0.14. `global.json` starts at SDK 10.0.0 with latest-major roll-forward/prerelease allowed. Framework-dependent default builds require the matching stable Windows App Runtime. The optional experimental pager build requires the matching 2.6.6 experimental runtime. Codec support comes from Windows.

## 2. Composition and lifecycle

`App.xaml.cs` creates services/host, initializes SQLite/recovery/settings/layout before MainWindow, and applies restored audio preferences before media initialization. Startup is Home without roots/playback. Capture window geometry while dispatcher lives; flush settings/layout/cache/history without waiting on a stopped dispatcher. Keep WinUI available until settings-flush failure notices are handled. Warnings use ContentDialog when WinUI is available, emergency MessageBox only on host failure.

```mermaid
flowchart TD
    App[App: DI, state, recovery, shutdown] --> Shell[MainWindow: page, toolbar, commands]
    Shell --> Views[Home / Settings / BrowseView adapters]
    Views --> Session[Session and Application services]
    Session --> Contracts[Application contracts]
    Contracts --> Infra[Infrastructure: filesystem / Windows / SQLite]
    Views --> Surfaces[WPF viewer and WinUI islands]
```

Browse unmodified Home/End is handled through the shared shortcut route before content input: select first/last from the current query results through NavigationController.SelectFile, resetting boundary attempts without invoking folder-boundary navigation. Empty listings consume the keys without action; inactive Browse and other modes retain their existing routing. MainWindow validates page/mode/root/folder/current/selection before deferred commands. Native callbacks that navigate are deferred out of native input; don't destroy/mutate their source island synchronously. Commands/dialogs defer reconciliation. Generation/cancellation checks reject stale results but cannot forcibly interrupt blocked OS calls or abandon physical compensation halfway.

## 3. Native boundary, input and geometry

Experimental toolbar breadcrumbs live in `WinUiToolbarIsland.Breadcrumbs.cs`; set `FolderBreadcrumbsEnabled` to false to restore the original path rendering. MainWindow supplies active-folder ancestry and defers native link callbacks with page/folder/mode validation. `BrowseView.Breadcrumbs.cs` resolves only the current ancestry, retaining overlapping-root ownership and command/drag guards, then uses normal folder activation. No filesystem discovery or persistent settings are added.

`App/Interop` owns DesktopWindowXamlSource/ContentSiteBridge creation, size/DPI conversion, focus, clipping/stacking and disposal. Home, Settings, sidebar, toolbar, Gallery, Reader, dialogs and media have native hosts. WPF retains the window, tree/carousel, image/GIF/text and fallback surfaces.

- Size native HWNDs in physical pixels from WPF logical bounds/DPI. MainWindow.TitleBar obtains AppWindow from the WPF HWND, uses AppWindowTitleBar colors and extends WinUiTitleBarIsland title content into the caption. Reserve physical LeftInset/RightInset scaled into WPF units; cache drag geometry and bridge WPF WM_NCHITTEST to HTCAPTION over passive title content only. The title island reports HTCLIENT over search, Filters, and recent chips, and HTTRANSPARENT over passive caption content. Native drag rectangles leave the file controls out of the drag region. AppWindow uses TitleBarHeightOption.Standard. Windows retains system buttons, drag/maximize/system-menu behavior; fullscreen hides/reveals the content with the existing shell. Close detaches callbacks/hooks and explicitly disposes the title island. No WinUI top-level Window.SetTitleBar or direct DWM color writes are used.
- HWND airspace is real: WPF translucency doesn't composite over video/Gallery/Reader. Clip native regions around overlays/reveal edges/fullscreen shell and raise revealed toolbar/sidebar above other islands. Don't revive the rejected popup-tree/transparency workaround.
- TreeScrollClearance supplies scrollable trailing space (CarouselOverlay.Height in enabled Browse, 48px otherwise), not a fixed row outside the viewport. TreeFooterMarginConverter gives only the last visible root container that bottom margin, after its entire subtree. TreeFooterRoot observes root collection and root Hide changes, transferring the footer when needed; observers detach at shutdown. This extends the pixel-scroll extent while keeping full-height viewport, scrollbar, recycling and genuine tree nodes.
- Carousel reveal uses BrowseView.CarouselRevealBounds: the full shown panel rectangle, independent of translation/visibility, shared by WPF movement/leave and normal/fullscreen native cursor probes. Remove the separate BottomRevealTarget; enabled/Browse/active guards prevent inactive reveal. Native video exclusions still follow visible overlay pixels, so enlarging hover bounds does not cut a blank full-height strip out of the video.
- Folder reveal has one `BrowseView.FolderRevealBounds` rectangle: sidebar plus the full fixed tree width in Browse/Reader; in Gallery a hidden unpinned tree uses sidebar width only, expanding to the full retention area while shown/pinned. Normal WPF events, the 40ms native-surface cursor probe and fullscreen coordination share it; sidebar layout supplies the current sidebar width. There is no separate LeftRevealTarget or hidden 26px native edge cutout. Clip only the tree's actual animated visible area so native content stays present beneath the hidden reveal area. Gallery/Reader's existing 26px content inset is layout spacing, not another reveal target.
- Reattachment can reset Gallery's HWND region without changing geometry: reapply exclusions on activation/loading/visibility/arrangement despite cached keys. Test GetWindowRgn/PtInRegion, not only bookkeeping.
- WPF preview and ComponentDispatcher preprocessing feed `MainWindow.TryHandleViewShortcut`. `WinUiViewKeyboard` additionally forwards native PreviewKeyDown from Reader/toolbar/sidebar before controls consume arrows, using native modifier/repeat state. Handled events must not trigger Reader's restoration-cancelling KeyDown observer. Preserve native Gallery modifier navigation, owned-modal/disabled-owner input and native drag ownership of Escape/modifiers.
- Synthetic KeyEventArgs passed to ViewerHost must carry Keyboard.PreviewKeyDownEvent before Handled is set (F4 crash invariant). Deferred shortcuts revalidate context. Reader's dormant-key guard bypasses shared commands without disabling Browse/Gallery mappings.
- `WinUiContentDialogHost` supplies a dedicated full-sized XamlRoot above native content, not the short toolbar root. Escape/close cancel; await ShowAsync before disposal; cancel comparison preview work. `RoundedDialogWindow` clips the outer HWND including children with DPI/size-aware rounded regions and correct GDI ownership. Transparent brushes alone cannot remove black corners.
- `WindowLayoutController`, `WindowLayoutService`, `WindowPlacementPolicy`, `WindowsWindowPlacement` separate dispatcher capture, storage, pure logical geometry and Win32 monitor/workspace/screen conversion. Preserve normal restore bounds and last non-minimized state. Apply normal placement and attach the native caption before maximizing at Loaded. Republish native drag rectangles after window-state changes. Coalesce capture at 100ms, storage at 250ms. Defer work-area changes during gestures; transient fullscreen must never overwrite restart bounds.

## 4. Session, folder loading and Home

`NavigationSessionState` owns roots, active folder, ordered query results/indexed membership, current/position, viewing mode, GallerySelectionState and ReaderSessionState. `FolderTreeEntry.Parent` defines ownership for overlapping roots; don't infer root scope with path prefixes. `NavigationController` owns selection/boundaries. View adapters share context without loading inactive presentation.

ApplicationSettings.BrowseOverscrollEnabled is an optional v1 payload bool defaulting true for old profiles. Settings > Files forwards its native toggle through MainWindow/BrowseView to NavigationController.SetOverscrollEnabled, which cancels pending boundary context and resets attempts. Disabled RegisterBoundaryAttempt is a no-op; ordinary selection/navigation and Reader's no-crossing behavior remain. Carousel Settings uses a left-aligned 320px slider like Reader settings. Gallery toolbar uses explicit 150px slider widths, Auto columns, 8px group margins/spacing and vertical centering; no ticks protrude beyond the row, and step snapping remains.

Folder styling uses Application FolderTextColorPolicy for the depth fallback and FolderNameRule for ordered name/count conditions (the type and settings field retain their legacy names for compatibility). Kind defaults to Name for older records; FileCount uses nullable inclusive MinimumFiles/MaximumFiles and a cached direct count, never recursive totals or filesystem scans. Unknown counts skip count conditions. Rules have stable Guid identities and optional RGB/ExpandedByDefault/HideFolder actions and ApplyToRootFolders (default false); ResolveActions scans matching enabled rules for the first explicit color and first root-eligible structure action independently, returning FolderRuleActions. Unspecified actions fall through; no expansion action is a no-op on current state. FirstMatch remains a condition-only helper. FolderTreeEntry caches RuleTextColor, the expansion default and IsHiddenByRule. Root exclusion applies only to structure actions, using IsRoot; WPF tree container Visibility collapses hidden rows/subtrees without removing nodes or changing navigation/count data. AllFolders conditions match even when counts are unknown. Background rule application honors manual expansion. Explicit Settings updates pass applyStructureImmediately and override earlier manual choices; controller tracks pending nodes for that rule version and a selected-ancestor protection set (updated on selection) to prevent navigation changes. Successful publication consumes each immediate request once, so subsequent count updates preserve manual state. Automatic first-child expansion respects the cached default. Depth follows Parent links and subtree attachments notify descendant depth changes iteratively.

App FolderRuleController observes root/child changes and direct-count property changes incrementally. It snapshots at most 32 node/name/direct-count triples on UI, matches a cloned rule array on one worker batch, then publishes after rule-version/cancellation, current-count equality and live tree-membership checks. Membership uses Parent links and constant-time child dictionaries. Count updates enqueue only affected folders, only when enabled count conditions exist; unchanged counts and total-only updates do not trigger matching. Coalesced queues cover new nodes, removed subtrees and rule changes; shutdown cancels/detaches both collection and property observers. Label bindings read cached results only.

FolderTextBrushConverter reads Depth, shading and cached RuleTextColor, using shared frozen fallback brushes and a bounded custom-color cache. Native Settings has five Border cards (Folder tree, Files, Carousel, Continuous reader, System). The mixed rule list uses native reordering, Move up/down, inline two-way enabled switches, actual-color condition text, and noninteractive/raw-accessibility carets for optional expansion actions. No fake children are inserted. RadioButtons switches between literal-name and inclusive count-range fields. Equal-star Grid columns pair minimum/maximum and color/expansion inputs; the color-action CheckBox lives in the Expander header. Compact list containers allow up to 480px before scrolling. Carousel uses a zero-origin 0-24 slider with 2-unit native ticks/stops, mapped to displayed totals 1-25; this avoids the native tick origin offset at minimum 1. Reader spacing hides ticks without changing steps. Rule item selection binds an accent border; Every item keeps a committed SavedRule alongside its live Rule preview; IsDirty drives the Unsaved marker. New rows have no SavedRule. Every publish path uses committed snapshots; inline enabled changes update only that field in both snapshots. Saving validates/replaces only the selected row, preserving identity/position and excluding other unsaved fields/placeholders. Loading controls suppresses draft updates; source detachment discards unsaved fields and placeholders. Containers reserve 16px on the right for scrollbar clearance. Expansion actions include Hide and a roots checkbox defaulting off. Expansion options describe actions without a header; collapsed color-header measurement sets the combo height. Save validates drafts and publishes even unchanged snapshots to reapply effective actions; MainWindow requests immediate structure application while startup/discovery remain default application. Empty validation text collapses its row to bring Save closer to the inputs; error text reveals it. Row toggles/drop completion/removal publish outside native input. Source detachment on unload/disposal, saved-order restoration, load reattachment and editor-version checks protect retained native islands. MainWindow persists cloned snapshots with FolderRulesConfigured=true and FolderCollapseDefaultConfigured=true, preserving explicit empty lists. Optional GetFolderRules() appends a stable-ID #606060 exact-zero rule to unconfigured profiles when capacity allows; it never rewrites startup data, duplicates a default, or resurrects a configured deletion. An independent FolderCollapseDefaultConfigured marker migrates existing configured profiles with a disabled stable-ID AllFolders/collapse default, roots excluded; both migrations honor the 256-rule capacity. Legacy Kind=Name/default fields and optional FolderRulesConfigured preserve schema-7/v1 compatibility. Validation caps 256 rules, 256-character name patterns, nonnegative ordered count bounds and unique IDs; every rule requires an action.

FileNameColorRule is a separate Application record with literal ordinal-ignore-case Contains matching, opaque RGB color (reusing FolderRuleColor), enabled state and stable Guid. Optional ApplicationSettings.FileNameRules defaults null/empty for old schema-7/v1 payloads; ValidateAll enforces 256 rules, 256-character nonblank patterns and unique IDs. No file structure/count actions are exposed. Settings FileRules partial reuses FolderRuleEditorItem and native card templates as a name/color-only adapter, retaining committed-vs-preview isolation, native priority controls, source detach/restore/load and independent deferred-action generation guards. Its TextBox/ColorPicker dependency-property observers update previews even before deferred native change events; new native lists select their first saved row on reattachment.

MainWindow.FileColors keeps the committed snapshot and applies a matched current filename only when the Browse page/mode is active. TitleBarBackground feeds AppWindowTitleBar active/inactive/background/button colors and native title content; choose black/white title foreground and restore the pre-rule brushes. ApplyCaptionColors coalesces changes to one render-priority callback using the latest title/palette/activation, guarded after close. FlushCaptionPresentation caches the applied palette, skipping native setters on title-only/same-color changes, and publishes WPF frame/WinUI title/native button colors in that callback. WinUiTitleBarIsland reuses its background/foreground brushes and disables background transitions. This coordinates application publication, not a guarantee that Windows and WinUI compose on the same physical frame. Matching the same color reuses the WPF brush too. Unchanged toolbar folder context/metadata are no-ops so file navigation retains breadcrumb objects. Native checks read AppWindowTitleBar color properties directly and verify extension/dragging, burst final palette and retained brushes/breadcrumbs. Toolbar and Reader surfaces receive no file-color rules. BrowseView forwards snapshots to Gallery and initializes future islands from the latest snapshot.

WinUiGalleryIsland.FileColors queues only realized container/Tile pairs, matching at most 32 names on one worker batch. Apply gradients only after rule version, active-generation, source and exact live container/Tile checks. Recycling resets the backdrop first; settings updates repaint surviving tiles without changing files, selection, images or thumbnail jobs. Pausing/destroying clears pending work and invalidates publication; reactivation repaints the current realized set. Filename backdrop geometry, transparent/80%-opacity endpoints and hover visibility remain. FileColorsCompletion permits deterministic native verification; workers never scan a full folder or touch the filesystem.

`FileSystemFolderTreeService` publishes shallow roots then cancellable breadth-first discovery. Reconcile children in at-most-32 batches with background dispatcher yields. Initial listing is independent. Track discovery completion/count versions so unknown totals stay unknown and stale counts cannot replace operation results. Suppress unchanged DisplayName/count notifications; rescans must not flicker root labels. Exclude reparse/owned-backup paths. Tree virtualization uses recycling/pixel scrolling with two-way model expansion/selection.

`BrowseView.xaml.cs` orchestrates activation, counts, operations and menus. Internal `ActivateFolderAsync` keeps its same-folder no-op. Explicit active-row left-click alone requests forced `ActivateFolderCoreAsync`, cancels older work, clears shared current/selection and commits first-file selection with a validated listing. Reader may defer projection replacement to retain warm pages. Buttons/expanders are excluded.

Operation/watcher reconciliation merges roots/children by path, retains expansion, maps changed subtrees and yields large diffs. Rename/delete/history must not rebuild the whole tree. Shallow collapse changes only direct child expandos. The WPF ellipsis opens a native MenuFlyout positioned through the toolbar island with screen/DPI conversion.

`RootFolderHistoryService` serializes root-only mutations off UI; accepted explicit root publication counts once. Immutable records/Changed notifications feed HomeView orchestration and WinUiHomeIsland cards. Availability is separate from loading history: two probes maximum, three-second caller timeout, permit retained while a blocked OS call finishes. Locate atomically rekeys without merging; Remove only removes saved state. Home versions/cancels availability/open requests, publishes lists before probes, defers navigating native callbacks, restores star focus where possible, and updates relative labels every second without card rebuilds or storage writes.

## 5. Browse handlers and resources

`ViewerCoordinator<T>` and capability-based handlers coordinate generation/cancellation, stale presentation disposal and metadata. App services BitmapViewerHandlers, TextViewerHandler and MediaViewerHandler produce ViewerPresentation variants for ViewerHostView; unsupported types use thumbnail/external recovery. Central interaction policy selects navigation versus content interaction. Format routing lives in SPECIFICATION.

| Adapter | Loading/lifetime |
|---|---|
| Static image | WicImageDecoder uses content services on workers, one decode dimension to preserve aspect, source bounds, frozen WPF pixels and original metadata. Zoom requests source detail; carousel can supply the immediate preview. |
| GIF | GifPresentation/XamlAnimatedGif owns a unique WPF Image and compressed stream after file close. Logical-canvas composition has full-frame caching off; WIC metadata supplies effective one-cycle duration. Dispose on file/view/shutdown. Large logical GIF canvases can exceed static-image memory expectations. |
| Text | FileSystemTextContentService reads bounded Unicode-safe windows; IWindowedViewerHandler supplies content/count scope. Retain same-file window/scroll on return, never execute content. |
| Media | WinUiMediaPlaybackSession adapts MediaPlayerElement/Windows.Media.Playback behind MediaPlaybackController, with cancellable 30s opening, shared audio settings and stale artwork rejection. Release before operations; return/affected content opens paused. Resize/reveal preserves playback session. |
| External | WindowsExternalFileService/association lookup own Windows calls; DefaultApplicationResolver cancels/version-checks results. Association lookup is bounded STA work. Preserve owner/file capture, duplicate suppression and stale completion checks. |

Carousel instances survive adjacent navigation. Coalesce visuals without dropping input; cancel obsolete loads, keep spacer geometry/bounded square fade ghosts and respect animation settings. Fades last 110ms; input/removal never waits on them. Suppress ListBox RequestBringIntoView so selection auto-scrolling does not race explicit index-based centering; do not rewind to a captured offset after refresh. Centering does not depend on realizing the selected container. Movement runs on a render-priority 16ms timer with an 80ms elapsed-time cubic ease-out deadline; retargeting does not restart that deadline. Distances over 1.5 tile widths snap immediately, as do initial positioning and disabled Windows animations. This bounds trailing rather than exponentially approaching each target on a background timer; delayed frames settle directly when the deadline has passed. Disabled carousel requests no carousel thumbnails. WPF scaling/decoding remains raw/high-quality, not blurred.

## 6. Gallery, drag and thumbnails

### Native Gallery lifetime

WinUiGalleryIsland retains grid/ItemsSource across tabs. Apply the latest coalesced snapshot at Render priority only when host/native grid are visible/loaded. Empty, disjoint, large membership changes and reordered survivors publish one Reset on the same source; surviving-path changes stay incremental. Cancel obsolete tiles/hover/selection/reveal before replacement. Routine updates retain decoded survivors.

GallerySelectionState uses case-insensitive paths and natural-order snapshots. `_updating` prevents feedback. Use real GridView-owned automation peers, not duplicate manually constructed peers. Reject old-grid input during deferred diffs. Focus is separate from the selected set. Reveals wait for measured layout independently of SelectedItem and yield to newer input/requests; don't poll or reload equal lists merely to reveal. OutlinedStatus has one live label; outline copies are accessibility-raw.

### Drag

GalleryDragGesture records press scope without capture/handling. Automatic CanDrag/CanDragItems stay false because they intercepted clicks. Start explicit StartDragAsync only after Windows movement thresholds; validate recycled visual identity. Selected origins snapshot the whole natural-order set before native pointer selection changes.

The package carries an opaque per-gesture internal token, not accepted external paths. Capture at most three already realized cropped tiles (192px, 8px offsets, maximum 208px canvas), restore crop during drag, use 80% opacity/hotspot (0,0), native deferrals/version checks/cleanup and default-preview fallback. No offscreen realization/decode for preview.

BrowseView.GalleryDrag* validates gesture/root/folder/revision/files/destination, defers reconciliation and reuses grouped operations. A 50ms timer re-hit-tests layout, expands after 600ms dwell and scrolls 32px edge bands. Scrolling resets dwell/highlight; compare container AND folder identity because tree rows recycle. Leave/drop/cancel/context cleanup stops work.

Native OLE wheel delivery uses temporary WH_MOUSE_LL only during authorized drag, foreground owner and tree hover. Coalesce signed/high-resolution deltas, dispatch outside callback, revalidate exact gesture, honor Windows line/page settings and fallback WPF routing. Every termination unhooks/invalidates packets. Never leave a global hook installed idle or do filesystem work inside it.

### Thumbnail pipeline

FileSystemThumbnailService* returns encoded PNGs. Gallery/carousel use GetDefaultThumbnailPngAsync: normal WinRT StorageFile.GetThumbnailAsync SingleItem without explicit size/options, aspect/orientation/transparency normalization retaining returned dimensions. Display sliders aren't source-size requests. Explicit-size fallback/artwork/collision/tests retain their resize/key contract; don't emulate defaults by passing zero to that API. Miscellaneous files retain Windows ThumbnailType.Icon output as well as image thumbnails; ThumbnailViewerHandler displays these alongside the external/no-integrated-viewer message without integrated-viewer capabilities. Supported images still prefer source decoding over generic icons. Existing real-thumbnail disk entries remain valid; unavailable results are memory-only, so no disk migration is needed.

| Resource | Bound/rule |
|---|---|
| Encoded memory | 512 entries / 32 MiB LRU, including unavailable results |
| Native Gallery decoded | 512 entries / 128 MiB, actual-dimension byte accounting |
| Work | Three-worker gates at service/native Gallery boundaries, cancellation after permits, per-key coalescing |
| Disk | `%LOCALAPPDATA%\MediFiler3\ThumbnailCache`, existing PNG format |
| Keys | Uppercase normalized full path, length, modified ticks, requested size; private default 0x0 bucket distinct from old explicit hashes |
| Fresh fallback | Known stale images bypass Shell pixels via WinRT source decode, default max 256px without upscaling; Fant downsampling where needed |
| Maintenance | Access writes batch at 30s/shutdown; after 30s idle and at most hourly, expire 90-day-unused entries and prune above 1 GiB toward 900 MiB in yielded 32-entry batches |

Default entries retain creation-time dimensions across DPI changes; no blanket purge/migration. No manual blur on disk or display. Invalidation rejects pre-invalidation/clear in-flight publication. Optional Invalidated/RequiresFreshSource state and zero-byte freshness markers survive restart/housekeeping/explicit image-cache clear. F5 thus measures fresh-source fallback, not normal provider quality; compare normal quality with new/uninvalidated files.

Maintenance verifies owned hash leaf names within resolved non-reparse ancestry. Unknown/link/changed/unsafe files remain. Adopt legacy PNGs conservatively (unknown age starts now); missing source mapping invalidates conservatively. Recognized abandoned temporary writes older than one day may be removed. Locked entries/invalidation remain retryable; indexed usage can undercount unadopted legacy files. Never cache-prune sources, backups, settings or journals.

## 7. Reader pipeline

WinUiToolbarIsland.Reader hosts a stable WinUI NumberBox, total count and four navigation buttons in an AppBarElementContainer. A publication guard prevents updates from requesting navigation. The container hides for empty or inactive Reader contexts. MainWindow lets the focused page editor handle editing keys, defers page requests out of native input and revalidates folder/page/modal context before JumpReaderPage. Ctrl+G retains the bounded numeric dialog. Build with `-p:UseExperimentalReaderPager=true` to restore the original native PagerControl and its matching experimental SDK. Browse image reset-to-fit combines icon/percentage label on one AppBarButton. Folder MenuFlyoutItems use native FontIcons. WPF label text occupies its own column before the reserved menu button.

ReaderSessionState owns projection, path/fraction/gap anchor, dimension ratios/prefix geometry and visible/preload window, never pixels. It projects the shared listing without replacing it. ReaderNavigation provides bounded navigation/query policy; ReaderOptions persists in the existing settings payload.

WinUiReaderIsland uses ScrollViewer/Canvas with explicit row realization, not another native observable source. Realize visible pages plus one previous and configured upcoming pages. FileSystemReaderImageService opens a disposable stream, uses WinRT BitmapDecoder with EXIF/sRGB/premultiplied BGRA and bounded output, then closes inputs. Copy bytes into XAML-owned WriteableBitmap, never a disposable bitmap still needed by deferred rendering.

| Resource/state | Invariant |
|---|---|
| Source jobs | Two concurrent service jobs; native permits also bound scheduling |
| Output/page | Maximum 4,194,304 pixels / 16 MiB BGRA; not a codec-internal/process ceiling |
| Width | DPI-aware 128px buckets, capped at 8192, no source upscaling |
| Native decoded LRU | Maximum 128 MiB; valid width-bucket survivors reusable |
| Geometry | Original aspect; minimum 24px row/placeholder; centered column, no trailing page gap |
| Inactive | Cancel/remove realized rows, reject late results; keep geometry and bounded warm pixels |

ChangeView can finish asynchronously even without animation. Keep `_restore` and intended path/fraction/offset until the actual offset arrives; realize the intended window instead of interpreting stale zero/old offsets. Explicit small-page targets survive non-scrolling input; actual scrolling supersedes them. End follows the tail as dimensions arrive. Delayed actions check interaction version, membership and activity. Retry invalidates only its source; update automation page names after reorder while retaining unaffected rows/images.

For native/UIA scrolling accepted while settled, ViewChanging captures the requested next offset as a path/fraction anchor against the current geometry. A source dimension can arrive before ViewChanged, and native UpdateLayout may deliver that offset reentrantly while `_updating` prevents publication. Early capture prevents restoration from requesting the old anchor in that ordering. Ignore ViewChanging during `_restore`/`_updating` and for unchanged offsets so Reader's own restoration requests and explicit jumps cannot recapture stale geometry.

BrowseView.Reader publishes current pages without starting Browse playback/carousel. Reader file moves/rename/recycle/Undo/Redo are guarded no-ops; file-action controls are disabled. ReaderOperations retains only a no-op compatibility entry point. Folder-tree folder commands remain available. Folder refresh defers reconciliation until final listings; MapPaths stages folder mappings and ratios atomically with replacement projection. Normal listings remain synchronous for immediate jump/End/retry.

ReaderExternal owns lightweight association/launch state independently from inactive ViewerHost. APIs remain while toolbar/shortcut entry points are deliberately hidden. SetReaderControls, SetGalleryControls and UpdateExternalCommandVisibility jointly derive visibility: neither setter may restore external buttons in Gallery/Reader accidentally. Reader-only dormant keys bypass shared command mappings; failed-page recovery remains.

## 8. Operations, journal and backups

IFileOrganizationService.ForRootAsync resolves root filesystem identity off UI and binds IFileOperationJournal.ForRoot(identity). Prepare durable intent before mutation, verify before completed events. Recovery inspects unfinished intents across roots and flags ambiguity instead of automatically retrying uncertain work. Root-scoped candidates/batches/redo invalidation/events/compensations cannot cross scopes. Legacy NULL RootScope records remain inspectable but aren't guessed into active stacks.

Commands snapshot source/clicked-root ownership and context before dialogs. BrowseCollisionResolver dispatches UI and revalidates answers. FileRenameName owns extension-preserving names/suffixes. Collision previews load metadata and bounded 240x160 thumbnails off UI; closing cancels work. Folder previews iteratively scan descendants on workers, count regular files (including Hidden/System) and sum logical file lengths. Check cancellation between entries/directories, never follow reparse points, and retain partial totals with IsComplete=false on skipped links/read failures. FileCollisionPreview carries optional FileCount and completeness; both native cards display counts/size, calculating/unavailable states and explicit incomplete labels. A not-yet-created incoming folder is empty. Paths/exact bytes remain hover information. These are live observations, not an atomic filesystem snapshot or a mutation safety check.

| Phase | Invariant |
|---|---|
| Collision/overwrite | Reject types/links/unsafe ancestry/volumes. Explicit overwrite revalidates destination identity and journals preservation into same-volume GUID storage before installing incoming data; no destructive overwrite API. |
| Failed install | Journal preservation rollback; retain versions/recovery paths if races make restoration ambiguous. |
| History/compensation | Undo reverse batch order, Redo original order. Compensation stays in root and doesn't branch redo. Once displacement/physical compensation starts, finish verification/rollback despite cancellation. |
| History collision | Preserve blockers; ResolvedPath/DisplacedItem event metadata restores them on opposite replay. IsReplacementBackup/OriginalAttributes identify/hide/restore backup state. Ambiguity marks RecoveryRequired, excluding unsafe retries. |
| Recycle restore | STA Shell namespace adapter requires unique original-path/type/identity match. Verify restored identity; file-only streamed length/SHA-256 fallback may compare selected recycled content when Windows changes identity. Never permanently delete as fallback. |
| Partial batch | Retain per-item outcomes, UnattemptedCount and WasCancelled. Ordinary errors may continue; cancellation/recovery uncertainty stops. Completed items form one root-scoped group. |
| Cleanup | OperationMaintenanceGate serializes inventory/cleanup/mutations. Durably HistoryDiscarded the whole dependent root/batch before deleting verified backups. Failed deletion leaves retired data for retry. Never delete restored/current/untracked/changed/link/recovery data. Expiry checks run after startup recovery and hourly; only backups at least 30 days past their last Completed/Redone preservation are eligible. File timestamps and later HistoryDiscarded events do not reset/define age. |

FileSystemOperationBackupStore uses local app UndoBackups when same-volume, otherwise hidden `.MediFiler3-UndoBackups-<user identity hash>` at that volume root. Verify ownership/reparse state; refuse unavailable safe storage. New replacements don't use sibling backups; tracked legacy paths remain usable and supported by verified review/cleanup without relocation.

IOperationBackupMaintenance exposes cancellable inventory, reviewed-ID cleanup, expiry, Explorer reveal and Changed notifications. FileSystemOperationBackupStore.Inventory verifies payload/identity/type/state, owned path ancestry, the non-reparse tree and root-scoped history-group readiness on a worker under the maintenance gate. One indexed journal snapshot caches group eligibility; returned rows include retained reasons and saved/expiry dates. Inventory is advisory; cleanup revalidates and journal group retirement remains authoritative. Reveal validates the journal ID/path/identity and launches Explorer selection, never the backup's default application. OperationBackupRetentionService is registered after OperationJournalRecoveryService, runs immediately and hourly, logs removals/failures and retries retained backups. Cancellation cannot abandon an already-retired item's physical deletion midway. Changed fires even on partial/cancelled cleanup so active history availability refreshes.

MainWindow.Backups cancels inventory work on leaving Settings/closing, rejects stale page context and defers native requests through existing Settings handlers. WinUiToolbarIsland.Backups hosts a finite-height virtualized ListView with location HyperlinkButtons, full-path tooltips and per-item eligibility/expiry. Both review and clear use the list; only clear shows a destructive primary action, with Cancel as default. Manual clear sends only reviewed eligible operation IDs. Link completion does not access dialog controls after Closed. Preview tests record reveal callbacks rather than launch Explorer.

Release native media before file/folder mutations/history and reopen affected/cancelled current media paused. Merge tree/count updates, not full resets. Undo chooses restored files; Redo removing current chooses next/prior-at-end, using Reader's image projection when applicable. Refresh history availability even when root node identity is retained.

## 9. Watcher reconciliation

FileSystemFolderChangeMonitor covers all loaded roots, including inactive ones, only in Browse/Gallery/Reader, reducing duplicate/nested coverage. Stop on Home/Settings, replacement/shutdown; retain healthy coverage when paths stay equal.

FolderChangeBuffer: 500ms quiet debounce, 2s maximum delay, 1,024 distinct paths, overflow/error rescan. One awaited dispatcher consumer prevents unbounded queues. Commands/loading/dialogs/external opens defer application; revisions reject stale work. Health probes have two permits, five-second healthy checks and failure backoff 1-60s. Degraded coverage polls active folder, then rescans on reconnect. Blocked OS calls retain permits until completion.

Healthy startup/return reconciles metadata without flushing unchanged pixels. Content hints/F5/overflow/failed-coverage recovery stay authoritative, including equal-size/timestamp edits. Edits without delivered hints while coverage stopped can need F5. Retain selected path/nearest position, safe missing-folder fallback, tree expansion and unrelated playback/zoom/text/anchors. Affected media reopens paused; automatic work never increments root visits.

BrowseView.OperationReconciliation tracks verified same-volume structural move echoes for at most 512 paths/30 seconds after identity/size/timestamp or absence checks. Warm source previews may survive these echoes. Unknown/new/overwrite destinations, changed versions, expiry/F5/overflow/recovery/content hints use normal invalidation. Strong hints retire echo tracking and dominate structural hints for the same path.

## 10. Persistence

Database: `%LOCALAPPDATA%\MediFiler3\medi-filer.db`. SqliteDatabase uses schema **7**, WAL/FULL durability, transactional supported 1-6 upgrades and rejects newer schemas. Don't downgrade/replace a user's database to run older code.

| Data | Storage/ownership |
|---|---|
| Intent/events | FileOperations/FileOperationEvents: preparation/completion/recovery/compensation |
| Root scope | Schema 5 nullable RootScope/index; legacy unscoped records preserved, never inferred |
| Replacement compatibility | Schema 6 guards against older builds misinterpreting replacement/history semantics |
| Typed state | Schema 7 retains ApplicationSettings, WindowLayout, RootFolderHistory, ThumbnailCacheIndex; independent JSON payload versions currently v1 |
| Keys | Settings/layout singleton; normalized case-insensitive absolute roots; existing thumbnail keys |

Optional Reader/options, BrowseOverscrollEnabled, monitor metadata and cache freshness/source fields extend payloads without schema bumps. Validate writes; corrupt/unknown data reports/logs errors and falls back without automatic destructive overwrite. Real changes may replace fallback state. Loading history doesn't probe/reopen roots.

Settings/layout debounce at 250ms and serialize saves; root-history mutations serialize independently; cache access batches at 30s. Playback progress never schedules settings writes. Capture latest state while UI lives; flush started writes at shutdown without dispatcher dependency.

## 11. Build and verification

Run from repository root on Windows/PowerShell with .NET SDK/Windows App Runtime. Use the same temporary BaseOutputPath for build and no-build runners to avoid replacing a running user binary. Never build concurrently against shared obj directories; don't automatically restart the app, commit, push or deploy.

```powershell
dotnet build MediFiler3.sln --verbosity minimal "-p:BaseOutputPath=$env:TEMP\MediFiler3ContinuationBuild\"
dotnet test tests/MediFiler3.Infrastructure.Tests --no-build --no-restore --verbosity minimal "-p:BaseOutputPath=$env:TEMP\MediFiler3ContinuationBuild\"
dotnet run --project tests/MediFiler3.App.Integration --no-build --no-restore "-p:BaseOutputPath=$env:TEMP\MediFiler3ContinuationBuild\" -- "$env:TEMP\MediFiler3Phase3Fixtures"
git diff --check
```

After restore, build may use `--no-restore`. Unit/native runners can run independently after build; native timing samples should run sequentially. Focused alternatives replace the fixture directory with `--file-query-only`, `--reader-only`, `--gallery-only` (UI refinement/Gallery) or `--gallery-resume-only`; generated focused runs don't replace full codec acceptance. Internal `--phase4-restart-worker` isn't a production option; full runs launch seed/reopen/verify workers automatically.

Publish the self-contained x64 release to a fresh directory:

```powershell
$publishDirectory = Join-Path $env:TEMP ('MediFiler3Publish-' + [guid]::NewGuid())
dotnet publish src/MediFiler3.App -c Release -r win-x64 --self-contained true -p:PublishProfile=WindowsX64 -o $publishDirectory
```

Keep the executable and its runtime files together in the ZIP. Include launch instructions and third-party notices. Retain debug symbols separately for diagnostics, and provide a SHA-256 checksum alongside the ZIP.

`--reader-restoration-only` runs the deterministic native scroll/dimension regression alone; it is also included in Reader/full runs. ReaderRestorationChecks uses real PNG decoding, holds page 7's source result, accepts a fractional ChangeView, completes the decode on its captured dispatcher context, then invokes the normal UpdatePages layout synchronously before yielding. It asserts that the native offset actually arrives during layout, retains the requested path/fraction and pixel offset, and survives a subsequent width reflow. This controls the observed event ordering without production test hooks or timing sleeps.

For Reader width-reflow investigation, set `$env:MEDIFILER3_READER_REFLOW_DIAGNOSTICS = "1"` before the native run (normally `--reader-only`), then unset it afterward. Test-only `ReaderReflowDiagnostics` observes the existing toolbar check and deterministic regression without adding scrolls, focus changes or settling waits. It writes separate `reader-width-reflow.json` and `reader-scroll-dimension-race.json` files in that run's temporary artifact directory on success/failure: timestamped request/acceptance markers, native ViewChanging stacks and ViewChanged events, layout/focus events, dispatcher state changes, actual/anchor offsets, restoration/queue flags, geometry and realized source assignments. The buffer retains at most 8,192 events and reports drops. Observation adds overhead and can affect timing; compare passing and failing traces without inferring failure rates. It is disabled by default and doesn't instrument the production application.

The native runner uses real dispatcher/WPF/WinUI in transparent non-taskbar windows, muted media and stubbed/recorded external launches. Supplied media is copied before mutations; real file/Recycle Bin/backup operations use isolated fixtures. Artifacts remain in `%TEMP%\MediFiler3Integration-<guid>`. This executable is built with the solution but run separately from `dotnet test`. Failures yield exit 1; missing fixtures are skips. Complete format acceptance requires zero skips, not only exit zero.

Generated fixtures cover WPF images/GIF89a, text/extensions, six encodings, large text, silent PCM WAV and corrupt/unknown files. Supply valid `.webp`, `.mp4`, `.m4v`, `.wmv`, `.webm`, `.mkv`, `.mp3`, `.wma`, `.m4a`, `.aac`, `.opus`; optional `.wav` replaces generated WAV. Historical local directory: `%TEMP%\MediFiler3Phase3Fixtures`, not a guaranteed repository resource. Representative origins: Google WebP sample (`https://www.gstatic.com/webp/gallery/1.webp`), W3C Sintel MP4/WebM and official Opus sample; Windows MediaTranscoder produced short MP3/WMA/M4A/ADTS-AAC/WMV. M4V is MP4 alias; MKV is WebM/Matroska subset. Don't rename unrelated content to claim codec coverage or silently install codecs.

| Area | Files in `tests/MediFiler3.App.Integration` |
|---|---|
| Formats/interactions/playback/resources | Program.cs, GifChecks.cs, thumbnail/handler checks |
| State/restart/operations | Phase4RestartChecks.cs, RootHistoryChecks.cs, ContentDialogChecks.cs, WatcherChecks.cs |
| Gallery/selection/drag | GalleryChecks.cs, GalleryInputChecks.cs, GalleryResumeChecks.cs, GalleryMutationChecks.cs, GalleryDragChecks.cs, ThumbnailRenderingChecks.cs |
| Reader/current folder/visibility | ReaderChecks.cs, ReaderRestorationChecks.cs, ReaderMutationChecks.cs, FolderClickChecks.cs |
| Actual-file characterization | ReaderAcceptanceChecks.cs writes reader-acceptance.json for 1,000 generated PNGs |
| Tree/fullscreen/layout | UiRefinementChecks.cs, WindowLayoutChecks.cs |

Restart workers use real SQLite/services/window state across three processes: seed settings/history/backups/cache/layout; reopen and verify scoped replay/watcher/cache isolation; verify fresh Home startup and retained cleanup/history. Not an installer/App-host failure-path test. Native probes must wait for loaded/realized/settled controls and actual offsets: a warm bitmap alone doesn't mean new layout settled. Retain pixel/fraction assertions and diagnostics rather than weaken tests for timing failures. Direct router/automation-peer calls don't establish physical input/Narrator/DPI acceptance; HWND key-message coverage is stronger but still not a whole matrix.

## 12. Failure knowledge to preserve

| Failure | Evidence/fix/invariant |
|---|---|
| Reader fractional scroll overwritten by late dimensions | Native trace on 2026-11-14: ChangeView accepted 1776 (page 4, fraction 0.2), then page 7 supplied dimensions. UpdatePages computed old offset 1665 before UpdateLayout delivered 1776; reentrant ViewChanged was ignored during `_updating`, and restoration requested 1665. Capture external/native scroll anchors in ViewChanging while settled; exclude own restoration and unchanged requests. Deterministic gated-decode regression failed before the fix with/without tracing (1428 vs target 1523.2) and passed after it, with exact event-order/pixel/fraction checks. Investigation artifacts: `%TEMP%\MediFiler3Integration-bdb141fe428e426a99e53ae5e390b43b\reader-width-reflow.json`; deterministic red/green logs `%TEMP%\MediFiler3Reflow-regression-{red-2,red-3,green-1}.log`. Temporary paths are hints, not dependencies. |
| Reader native image-worker crash | 2026-11-11 Event 1000 Microsoft.UI.Xaml.dll 3.3.0.0 / 0xc000027b, offset 0x3d1699; WER RO_E_CLOSED (0x80000013). Three stacks resolved AsyncCopyToSurfaceTask::CopyOperation/Execute. SoftwareBitmapSource.SetBitmapAsync returned before deferred render finished using disposed input. Keep XAML-owned WriteableBitmap copies; no global exception suppression. |
| Browse F4/zoom/media crash | Event 1026 (2026-11-12): `Every RoutedEventArgs must have a non-null RoutedEvent associated with it.` ViewerHost.HandleKey -> BrowseView.HandleViewShortcut -> MainWindow.TryHandleViewShortcut/OnFullscreenMessage; synthetic args lacked RoutedEvent before setting Handled. Event 1000 KERNELBASE.dll / 0xe0434352, WER d4b957cd-5365-4649-b29e-e357d504b73c. Assign event before forwarding. |
| Gallery collection faults | Intermittent 0xC0000005 in WinRT collection notifications; peer attribution inconclusive, layout deferral alone didn't eliminate recurrence. Retained sources/atomic disjoint Reset/deferred loaded diffs/real peers are safeguards, not universal elimination. Record shared-path versus disjoint versus transition context. |
| Later native transition fault | Microsoft.UI.Xaml.dll / 0xc000027b after native F11/history transition, WER a6f5529d-57b1-4dc6-ad96-77d08cc3b42a; old `%TEMP%\MediFiler3TreeFeedback-WER.txt` / `MediFiler3TreeFeedback.log`. Deferred native dispatch addresses a lifetime risk; root cause unproven. |
| Physical Reader arrows missing | Router-only tests missed native delivery. PreviewKeyDown bridge and ignoring handled KeyDown avoid control consumption/restoration cancellation. Keep HWND SendMessage/PostMessage/repeat/cross-focus tests. User confirms arrows work. |

Reader dumps were retained outside repo under `%LOCALAPPDATA%\CrashDumps`: MediFiler3.App.exe.61292.dmp, .57884.dmp and .56752.dmp. Matching public PDB RSDS C1689B46-4BD2-CADF-9955-9893CD94970C, age 2: FailFastWithStowedExceptions+0x61 at +0x3d1699, CopyOperation cold +0x36ab02, Execute+0x92 at +0x1e4462. Analysis scanned captured stack addresses with dbghelp/matching symbols, not a complete unwind; nested stowed-exception heap records were absent. Temp symbols/scripts/logs aren't guaranteed to survive. Capture new WER/dumps/context on recurrence. Current outstanding concerns belong in PROJECT_STATUS, not repeated historical pass counts here.

Browse fallback follow-up: ThumbnailViewerHandler uses the Windows-default thumbnail bucket instead of viewport-sized requests. Miscellaneous files retry Shell ListView icons if SingleItem returns no usable preview or cannot decode. A null fallback result retains a known bitmap only for the matching file (including an immediate carousel preview); messages must not erase it. BrowseFallbackChecks exercises a real ViewerHostView with native thumbnails, PSD association-icon fallback, valid ICO source pixels, WPF rendered pixels, resize, retained previews and rejection of previous-file pixels. Run --browse-fallback-only for this focused native check; it also runs in the full suite. The PSD fixture is not a valid decoded PSD or evidence of a Photoshop installation/codec; association icons depend on the machine.

## 13. Shared file queries

BrowseView owns the active scope inventory, which can include descendant files. NavigationSessionState.Files is the ordered query snapshot consumed by Browse, Gallery, the carousel and the Reader image projection. The listing service supplies natural order, size, creation and modification timestamps. FileQuery uses listing metadata and shared format routing. Equal metadata keys retain natural order. Query edits reuse inventory, run matching and sorting off the dispatcher, and publish only after cancellation and context checks. F5 and operation/watcher reconciliation replace the physical inventory and apply the current query. Folder counts use the inventory. An incomplete read is identified separately from an empty result.

Query edits retain the current result or Reader page index, clamped to the available results, and preserve the Reader fraction. Watcher, refresh and operation reconciliation still prefer the current path. Publication prunes Gallery selection. Direct-folder Gallery updates retain the native source and decoded cache. Recursive changes populate a replacement source before attaching it to the retained GridView, keeping the decoded cache and avoiding mutable WinRT collection callbacks. Filtered Browse overscroll probes candidate folders sequentially with cancellation after five attempts and activates the next matching folder.

MainWindow owns the active definition and its optional saved ID. The native filter flyout creates a fresh draft on opening. It can extend beyond the title-bar island while remaining constrained to the desktop work area. The editor applies changes live to the effective query. Save explicitly updates the named definition. Clear filter resets the definition. Recent access uses toolbar chips. The persisted Settings > Files slider offers 0-5 chips with a default of 3. Displayed chips keep their positions while membership is stable. The centered title-bar search uses a 220ms debounce, with explicit Enter submit. Search owns editing keystrokes and Escape returns focus to the active viewer. Fullscreen keeps chrome revealed while search has focus. The settings payload has optional named filters and up to 10 recent IDs. Names are at most 64 characters and each query text field is at most 256 characters. The active definition is session state. Legacy schema-7/v1 profiles omit the new fields and get empty saved/recent lists.

Recursive discovery enumerates physical folders in natural preorder and skips linked paths and managed backup storage. BrowseView publishes a first snapshot then coalesces progress updates, preserves the current anchor while results arrive, and cancels old work on scope or folder changes. Watcher, F5 and operation refresh reconcile the complete scope and use physical direct counts. A result uses its absolute path for physical operations and the inventory selects the journal via the active tree occurrence of the loaded root. Overlapping root occurrences retain independent history scope. Same-folder items in mixed Gallery moves are no-ops. Result captions use relative paths. Recursive scope suppresses Browse overscroll while retaining its preference. File matching and sorting remain separate from rendering and filesystem I/O.
