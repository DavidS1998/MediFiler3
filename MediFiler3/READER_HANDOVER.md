# Continuous Reader continuation handover

Updated 2026-11-12 after slice 6 closeout. **The approved Reader MVP, slices 1-6, is complete with recorded verification limits.** Read [current status](PROJECT_STATUS.md), [Reader acceptance](READER_ACCEPTANCE.md), AGENTS.md, SPECIFICATION.md and ARCHITECTURE.md. The complete preceding handover, dated experiments, superseded picker designs and earlier pending-slice instructions are preserved verbatim in [handover history](READER_HANDOVER_HISTORY.md).

## Current behavior

- Browse, Gallery and Reader share root/folder/full natural file ordering/current file. Reader projects only supported images without filtering the shared listing. Enter at the current image, next supported image, then previous if needed; never cross folders implicitly. One source file is one page; GIF/TIFF use their first frame. Missing/corrupt pages have named placeholders and guarded Retry/Open in Browse actions.
- Native virtualized ScrollViewer/Canvas; fit width at 100%, spacing 0, upcoming preload 2, counter on by default. Top-right Switch/Sort glyph buttons choose width/height; a 10-100% slider precedes them in width mode. Settings persists spacing 0-64 step 4, preload 0-6 and counter visibility; height mode remembers but ignores width percentage. Counter matches Gallery's bottom-right outlined white semibold style. Sidebar uses ReadingMode and separates Home from file views.
- Preserve an in-memory image path/fraction/gap anchor through geometry changes and Home/Settings return. Explicit jumps retain their target when small pages share a viewport; real scrolling returns to the 25%-viewport reading-line rule. End follows the tail as dimensions arrive. Known operation mappings follow file/ancestor renames atomically with the new projection; removing an anchor chooses the next surviving image, then previous. External renames without verified mappings remain removal/insertion.
- Single-current-image rename/recycle/tree-destination move and root-scoped history reuse existing services. Single-page recycle bypasses confirmation. Undo selects the restored image; Redo/removal advances to the next image or previous at the end, skipping unsupported files. Empty Reader cannot mutate an unsupported shared current file. Partial/cancelled/unattempted outcomes and stale-page/root/folder/view checks remain explicit.
- Reader toolbar temporarily hides Rename, Delete, Undo, Redo, Open in default application, Choose an application and Open current Reader image in Browse. Their Reader-only shortcuts (F2, Delete, Ctrl+Z, Ctrl+Y, Ctrl+Enter, Ctrl+Shift+Enter and Enter) are dormant/commented out. Underlying operation/history/external/Browse-handoff APIs and failed-page recovery buttons remain available. Browse controls and shared shortcut behavior remain; Gallery file-operation controls stay visible while its external-application buttons are hidden too. Reader association/launch state is lightweight and never resumes inactive Browse decoding/playback/carousel work. Duplicate/modal/stale-completion guards and owner HWND remain. Native PreviewKeyDown from Reader/toolbar/sidebar routes through the shared guarded window shortcut handler before native controls consume arrows; handled events do not cancel Reader restoration.
- Explicit left-click on the already active folder reloads only its listing and starts at its first file/Reader image. Clear shared current/selection during loading, reject superseded listings and retain tree nodes/counts. Internal same-folder selection/refresh/history keeps its established position rules. Folder menus offer Collapse child folders: only direct child expandos change; the clicked folder and deeper expansion states remain. Tree recycling/pixel scrolling and rounded HWND dialog clipping remain.
- Watchers/F5/overflow and inactive-return reconciliation retain unaffected rows/pixels while explicitly invalidating changed content, even with unchanged metadata. Leaving Reader cancels realized work and removes rows, retaining lightweight geometry and a bounded warm cache. Shutdown releases native surfaces.

## Keys

| Input | Reader action |
|---|---|
| Left / Right | Previous / next image within this folder |
| Up / Down | Scroll 48 logical pixels |
| Page Up / Page Down; Shift+Space / Space | Scroll 90% of the viewport |
| Home / End | Sequence start / end |
| Ctrl+G; Ctrl+R | Bounded numeric Go dialog; retry current page |
| Enter | Temporarily disabled in Reader; Browse-handoff API/recovery action retained |
| Ctrl+Enter / Ctrl+Shift+Enter | Temporarily disabled in Reader; external-open APIs retained |
| F2 / Delete; Ctrl+Z / Ctrl+Y | Temporarily disabled in Reader; operation/history APIs retained |
| F3 / F5 / F11 | Tree pin; fresh refresh; fullscreen |

Keep view/focus-independent routing, native repeat behavior, modal/disabled-owner guards and undefined-modifier behavior. Go is one focused/select-all numeric field restricted to 1 through page count; Go/Enter confirms and Cancel/Escape retains location. Do not restore the earlier filename search/list/grid picker. The user explicitly defers Browse/Gallery search to a future phase; it is not the next active slice.

## Resource and safety invariants

- Source first-frame decoding is independent of shared Windows-default thumbnail cache ownership. Respect EXIF orientation; no source upscaling. Width requests use 128px buckets, capped at 8192; output is capped at 4,194,304 pixels/16 MiB BGRA per page. Two jobs run concurrently; native decoded LRU is at most 128 MiB. These are output/cache bounds, not total process/codec allocation ceilings.
- Preserve XAML-owned WriteableBitmap pixels. Do not reintroduce the prematurely disposed SoftwareBitmap handoff that caused `RO_E_CLOSED` native failures; retain [crash evidence](verification/reader-crash-2026-11-11.txt). Image tooltips remain absent; accessible names/recovery remain.
- Validate native row/current-page membership, interaction versions, cancellation and activity on delayed publication. Stage known mappings with replacement projections; defer operation reconciliation but keep normal listing updates synchronous. Unchanged rows/cache entries survive; source/rescan/F5 invalidation must remain authoritative.
- Never silently overwrite or permanently delete media. Keep explicit collision choices, verified overwrite backups, root/identity journals, same-volume storage and compensation/recovery rules. Keep source, thumbnail cache, backup and history ownership separate. Bulk rename is not planned. Existing Gallery drag threshold/preview/hover/edge/wheel/native-hook lifecycle and warm-thumbnail safeguards remain intact.

## Code and verification

| Area | Entry points |
|---|---|
| Projection, geometry, settings and anchor | Application/Navigation/ReaderSessionState.cs, ReaderNavigation.cs, ReaderOptions.cs |
| Bounded source decoder | Application/Files/IReaderImageService.cs; Infrastructure/Files/FileSystemReaderImageService.cs |
| Native pages, pixels/cache, navigation and key preview | App/Interop/WinUiReaderIsland.cs, .Navigation.cs; WinUiViewKeyboard.cs |
| View, operations, external launch and reconciliation | App/Views/BrowseView.Reader.cs, .ReaderOperations.cs, .ReaderExternal.cs, .Reconciliation.cs; BrowseView.xaml.cs |
| Toolbar/Go and window routing | App/Interop/WinUiToolbarIsland.Reader.cs, .ReaderDialog.cs; MainWindow.Reader.cs, MainWindow.xaml.cs |
| Tests | Infrastructure.Tests/ReaderTests.cs; App.Integration/ReaderChecks.cs, ReaderMutationChecks.cs, FolderClickChecks.cs, ReaderAcceptanceChecks.cs |

Fresh build: zero warnings/errors. Unit/filesystem: **362 passed**. Full native with supplied codec/media fixtures: **221 passed**, including **27 Reader-runner checks**. Earlier slice 6 focused Reader repeat: **27 passed**. Zero failures/skips throughout. Latest full evidence `%TEMP%\MediFiler3Integration-516e430d8ead4687b2b1da1540fd26bb`, log `%TEMP%\MediFiler3Reader-hidden-commands-full.log`; the earlier closeout repeat `%TEMP%\MediFiler3Integration-caf7ba762b904f259f8f8e627c8f6cb8`, log `%TEMP%\MediFiler3Reader-slice6-focused.log`. [Acceptance](READER_ACCEPTANCE.md) retains sample measurements, methodology, user confirmations and limits.

The user confirms Reader arrow keys work and requests closeout after reporting the rest of Reader in order. Dedicated Narrator, mixed-DPI/fullscreen, touchpad, diverse real-library/frame-time/long-session memory and remote/locked/removable/codec/deployment matrices remain carried verification limits. Do not reopen slice 6 merely because those exhaustive matrices were not run, and do not relabel them as checked.

## Git and next work

Closeout started on `master` at `b1cd13b` with the uncommitted arrow fix, its tests and docs present. Preserve that work. Slice 6 changes tests/documentation only; all 143 production files match the start-of-closeout source hashes. No commit/push or user-app restart was requested/performed. Build into temporary output using [current commands](PROJECT_STATUS.md); never build concurrently against shared obj directories.

No Reader implementation slice remains. Address reported regressions within these safeguards or separately scope the next feature. Additional sorts/filtering, Browse/Gallery search and external file drag/import/export remain future scope. Gallery acceptance and earlier project histories remain independently recorded; their superseded next-step instructions are historical.


## Temporary Reader command-chrome policy (2026-11-12)

The user requested hiding the seven toolbar actions listed above without removing their implementations, and commenting out their equivalent Reader shortcuts. Reader-specific Enter/external bindings are commented out; a Reader-only shortcut guard also bypasses the shared F2/Delete/Ctrl+Z/Ctrl+Y bindings while retaining those bindings in Browse/Gallery. The guard returns unclaimed so native focused-control behavior can remain, and owned dialogs still retain normal text editing. Hidden groups' separators collapse; Previous/Next, numeric Go, sizing, arrows/scroll/retry, tree pin, refresh and fullscreen remain. Accessibility help no longer advertises Enter handoff. Failed-page Retry/Open in Browse recovery buttons and tree-destination moves were not removed.

Regression coverage checks all seven hidden controls, dormant keys and repeats without prompts/file mutations/app launches, retained external/operation/history/handoff APIs, and restored control visibility outside Reader. The existing full Browse/Gallery and native keyboard tests remain required. Search is explicitly deferred to a future phase rather than the next implementation step.


Temporary Reader chrome verification (2026-11-12): solution build zero warnings/errors; 362 unit/filesystem tests and 221 full native checks passed, zero failures/skips, including all 27 Reader-runner checks. Hidden-key regression covers bare Enter, Ctrl+Enter, Ctrl+Shift+Enter, F2, Delete, Ctrl+Z and Ctrl+Y with initial/repeated input; no prompt/navigation/file mutation/external launch occurs. Direct operation/history/external/handoff APIs, failed-page recovery and Browse/Gallery remain verified. Full evidence `%TEMP%\MediFiler3Integration-516e430d8ead4687b2b1da1540fd26bb`; log `%TEMP%\MediFiler3Reader-hidden-commands-full.log`. The earlier slice 6 measurements/focused repeat remain historical baseline evidence. Preserve this temporary visibility/key policy until the user requests otherwise; search remains deferred to a future phase.


Gallery chrome follow-up (2026-11-12): Open in default application and Choose an application, plus their separator, are hidden in Gallery as well as Reader. Browse restores both buttons/separator. Shared visibility derives from both view flags so switching Reader/Gallery/Browse in either order cannot restore hidden external controls accidentally. Gallery rename/delete/history and all underlying functions/shortcut policies remain unchanged. The existing native mode-switch regression now checks Browse -> Gallery -> Browse restoration.


Gallery external-button follow-up verification: build zero warnings/errors; focused Reader/view-switch runner repeat **27 passed**, zero failures/skips. Evidence `%TEMP%\MediFiler3Integration-bdb55a3705f844beada871a1c15dd110`; log `%TEMP%\MediFiler3Gallery-hidden-external-focused-repeat.log`. The first focused attempt passed the visibility regression but had one existing width-reflow timing failure (fractional scroll stayed at the page top: offset 1665 versus target 1776); its log is `%TEMP%\MediFiler3Gallery-hidden-external-focused.log`. The unchanged-code/assertion repeat passed. Keep this timing sensitivity as a verification limit; no scrolling behavior or assertion was changed for this visibility-only follow-up. The preceding full 362/221 verification predates this follow-up and was not repeated.
