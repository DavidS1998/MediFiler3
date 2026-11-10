# Project status

**Snapshot date:** 2026-11-10

**Phase 4:** steps 4.1 through 4.5 implemented; step 4.6 (external filesystem reconciliation and cache maintenance) is next. Home visual/input acceptance remains open.

**Phase 3:** complete, including user-confirmed manual interactive acceptance on 2026-11-10.

## Handover: resume here

Continue the existing application. Read AGENTS.md, SPECIFICATION.md, ARCHITECTURE.md, DEVELOPMENT_PLAN.md and this file. PHASE4_HANDOVER.md provides the remaining implementation sequence and code map.

Step 4.1 adds typed Application persistence contracts/models, registered Infrastructure stores and a transactional SQLite migrations; current schema 7 upgrades versions 1/2/3/4/5/6. Settings, root usage/favorites, layout and cache-index records round-trip across reopening. Home now presents live native WinUI cards backed by the root-history service; root opening uses the same Browse publication/counting boundary as other entry points. Cache services do not consume the index store yet. Step 4.2 consumes settings storage.

Approved policies: count explicit opens of each root folder only; no subfolder history. Keep renamed/missing roots until the user manually locates or removes them. Persist volume and mute independently. Step 4.2 applies both before viewer/handler initialization and media opening.

Pre-existing documentation changes were preserved. No commit was created. Inspect the current working tree before editing.

## Current architecture

- .NET 10 C#/WPF host targeting Windows 11 x64. App owns DI, WPF views, dispatch and the WinUI boundary; Application owns navigation, viewer contracts, playback state and operation use cases; Domain owns models; Infrastructure owns filesystem, Shell and SQLite adapters.
- Windows App SDK 2.6.4 XAML Islands host the compact navigation rail, toolbar, native controls and video surface. Keep the WPF shell and native Windows caption.
- `NavigationSessionState`/`NavigationController` centralize folder/file context and selection. `ViewerCoordinator<T>` owns cancellation, stale-result rejection and disposable presentation lifetime. `ViewerHostView` presents image/GIF/text/media/fallback content.
- SQLite database: `%LOCALAPPDATA%\MediFiler3\medi-filer.db`, schema version 7, WAL and FULL durability. Separate state tables now accompany immutable operation intents and append-only journal events; startup recovery never retries ambiguous operations automatically.
- Tests are in `MediFiler3.Infrastructure.Tests`; `MediFiler3.App.Integration` is a separate executable built with the solution and run explicitly with a real WPF dispatcher/WinUI environment.

## Implemented baseline

| Area | Current behavior |
|---|---|
| Browsing | Separate folder roots; shallow roots publish first, then cancellable breadth-first hierarchy discovery and progressive `(direct/total)` counts with unknown placeholders. Active-folder files use Explorer-style natural sorting. |
| Navigation | Arrows/wheel/carousel select files; five consecutive boundary attempts skip empty folders. Backward folder entry selects the last file; forward entry selects the first. |
| Images | JPEG/PNG/BMP/GIF/TIFF and WebP through WIC; viewport-bounded static decoding, original dimensions, image-only zoom/pan and static PNG thumbnails. WebP needs the installed codec. |
| Animated GIF | Autoplay with delays/loop counts, transparency, frame offsets and disposal; XamlAnimatedGif 2.3.2 behind App-owned GifPresentation. Zoom/fit/resize preserve animation. Files close after compressed bytes load, and navigation/deactivation releases animation. Duration metadata is one cycle, using 100 ms for zero/missing delays. |
| Text | Strict UTF-8 and BOM-marked UTF-16/UTF-32; 1 MiB Unicode-safe windows, previous/next controls, read-only selection/copying, navigation/scroll toggle and scoped word counts. Equal side clearances avoid the folder overlay. |
| Audio/video | WinUI MediaPlayerElement/Windows MediaPlayer; MP4/M4V/WMV/WebM/MKV and MP3/WAV/WMA/M4A/AAC/Opus routing, subject to installed codecs. Paused opening, progress/seek/stop, +/-5 second jumps, compact 0-100 volume popup and speaker mute. Playback speed is removed. |
| Media lifetime | Persisted volume/mute restored before opening; release before mutations/navigation/deactivation/shutdown. Audio artwork loads asynchronously and does not reserve empty space. |
| External opening | Compact center-toolbar default-app/Open With actions for every selected file, async association/icon lookup and stale-result guards. Unsupported viewer right-click opens the existing chooser action, with/without a thumbnail. |
| Metadata | File size with one decimal and KB/MB labels; bracketed image dimensions, video height, audio/video/GIF duration, text word counts. Metadata-only top line, full path below with the current folder white. |
| File organization | Independent persistent root-scoped Undo/Redo; journal-backed move/rename/create-folder/Recycle Bin actions, grouped undo/redo, strict identity/conflict checks and conservative recovery. Nonempty folder deletion requires confirmation; no permanent-delete fallback or silent overwrite. Shell restore was confirmed by the user. |
| History previews | Undo/Redo tooltips list up to eight planned changes and a remaining count. Corrupted separators were replaced with ASCII `->`, `-` and `...`; file paths remain verbatim. |
| Settings/layout | Volume, mute, carousel density and folder pin survive restart; normal window bounds and maximized state survive restart with monitor-aware placement. The compact rail stays icon-only; Home has live recent/frequent/favorite cards with synchronized stars and unavailable-root actions. |
| Cache | Shared PNG disk cache and 32 MB/512-entry memory LRU, three thumbnail workers, source/dimension-based keys. No disk pruning/index policy yet. |

## Step 4.2 behavior

ApplicationSettingsService coalesces preference writes (250 ms), serializes snapshots and retries dirty state on shutdown flush without UI dispatch. Only user preference changes schedule writes, never playback progress. Startup loads local settings before constructing MainWindow/Browse/viewer handlers; corrupt/invalid payloads log a warning and use safe defaults without overwriting until a user change. Shutdown save failure logs and reports a warning.

The center toolbar now includes Refresh (F5), enabled in Browse with loaded roots. It rescans roots/hierarchy, active files, counts and previews, preserves a matching current file path, and uses the nearest bounded previous index if the file disappeared. Nested restoration waits for progressive discovery; superseding loads cancel old work and user selection changes prevent late restoration. Missing folders fall back to an accessible root; no accessible roots clears selection. This is explicit refresh, not filesystem watching. New physical F5/toolbar interactions and cross-process UI restart appearance have not been manually reviewed in this pass.

## Phase 4 implementation priorities

1. **4.1 complete:** typed persistence contracts/stores, schema-4 migration and reopen/upgrade/rollback tests preserving journal/history.
2. **4.2 complete:** save/restore existing settings, restoring cross-session volume and mute before media opens; carousel density and folder pin.
3. **4.3-4.4 implemented:** root-only history/favorites and live Home cards, synchronized stars, root opening and unavailable-root actions. New Home visual/input/DPI/picker acceptance remains open. No folder-menu favorites.
4. **4.5 implemented:** monitor-aware window size/position/maximized persistence; folder pin already persists. Physical multi-monitor/DPI acceptance remains open.
5. **4.6:** bounded external-change reconciliation and cache metadata/maintenance.
6. **4.7:** restart/integration acceptance, with regression checks for the accepted Phase 3 interactions.

Details and unresolved policies are in [PHASE4_HANDOVER.md](PHASE4_HANDOVER.md). Root-only visit counting, user-directed unavailable-root handling, and volume/mute persistence are approved. Retention limits, watcher scope and extra settings remain decisions for their steps.

## Step 4.3 behavior and next UI workflow

RootFolderHistoryService stores each explicitly opened root's count and UTC last-visited timestamp, using normalized case-insensitive absolute paths. Every distinct accepted root is recorded once per LoadRootsAsync call; Add Folders and drag-and-drop share that boundary, and Home now uses it too. No subfolder navigation history is persisted. Lists share the same favorite flag, Favorites sort alphabetically, and Remove entry deletes the saved record from all three lists. No history retention cap is applied.

Availability is checked off-thread with at most two concurrent probes and a three-second caller timeout, retaining blocked OS worker slots until completion. Missing/access-denied/temporarily unavailable entries remain saved. Service actions support Locate preserving history/favorite state (reject existing destination entries), and Remove deleting only saved data. Locate itself is not an open/visit. Home provides root-only stars with consistent state in all two/three lists; no tree-menu favorite actions.

## Step 4.4 Home behavior

The WPF Home page hosts one WinUI XAML Island with responsive Recent/Frequent/Favorites cards (three/two/one columns), opaque matching background, 32 px outer margin, 16 px card padding/gutters, 12 px section content spacing and 8 px action spacing. Compact entries show folder names and visit counts with live relative times; hover text includes the full path and exact local timestamp. Shared stars and empty states remain. Each section initially renders up to 25 entries with an explicit Show more control; storage/history are never pruned. Add folders and native Home folder drops route to the existing shell root loader; opening an entry uses that loader exactly once. Favorite stars synchronize across all sections, and Favorites stay alphabetical.

Local lists appear before progressive bounded availability checks. Home applies callbacks on the WPF dispatcher, cancels superseded/unloaded scans and rejects obsolete results. An activation generation also prevents a delayed open from changing context after leaving Home. Missing/access-denied/temporarily unavailable roots show Locate/Remove. The per-row menu provides Locate folder and one Remove entry action that removes the saved root from all lists. Locate uses an owned native folder picker; cancellation leaves data intact, and collisions report errors without merging. Actions disable duplicate input, use InfoBar feedback and restore persisted star state after errors. Shutdown unsubscribes service events/cancels work.

Verification: 188 unit/filesystem tests and 99 native checks passed with zero failures/skips; solution build clean. Native automation covers cards/resizing, synchronized star/unstar, Home-to-Browse counts, locate cancellation/conflict/success and all-list entry removal, restart/reattachment, empty Add folders and stale/slow probes. A native RenderTargetBitmap capture was inspected for layout, contrast and spacing. Real folder-drop/picker interaction, physical keyboard/focus, high DPI and overall Home appearance remain for user review.

## Home compactness and resize follow-up

Cards and header now use explicit viewport-bound widths (up to 1600 px of content) and left alignment instead of capped Stretch sizing; native tests grow/shrink through 700-3000 px and check every card stays in view. Entries omit the inline path and use two compact lines: name, then visit count/relative time. Hover text provides the full path and exact local timestamp. A UI-only one-second timer updates relative labels without rebuilding cards or writing storage; it stops when the native surface is destroyed. Months are 30 days and years are 365 days; future timestamps clamp to zero seconds.

Ignore and recent-only removal have been removed from both service/UI. Missing roots offer Locate and Remove. Overflow menus contain Locate folder and one Remove entry action, which removes the saved root from every list without touching files. Existing schema-4 records stay compatible; no journal changes. Current verification: 188 unit/filesystem tests, 99 native checks, zero skips; build clean. Native screenshot inspected; physical wide-window/DPI behavior still awaits user review.

## Rendering fixes after step 4.2

Settings slider host paints an opaque #0B0C0E background matching Settings. The experimental video folder-tree popup changes were reverted at the user's request; the original WPF tree and native video clipping behavior are restored. Settings persistence (including folder pin) and Refresh/F5 remain intact.

Latest rendering verification: 88 native checks passed, zero failures/skips with full local fixtures; solution build succeeded with zero warnings/errors. Existing 157 unit/filesystem tests last passed for 4.2; no business logic changed in this rendering fix/revert.

## Verification and remaining acceptance

Current verification: **258 unit/filesystem tests passed, zero failures/skips** after step 4.5. Added coverage verifies visit timestamps/identity, concurrent increments, list ordering/shared favorites, missing-root locate/remove, collision safety, bounded checks and restart preservation. Tests include state defaults/round trips, validation, root normalization, all supported schema upgrades, rollback/retry, unknown-version rejection, and preserved journal records/events/compensation links/history candidates. Solution build succeeded with zero warnings/errors; git diff --check passed.

Native integration rerun after collision handling: 116 checks passed, zero failures/skips with the complete local fixture set. Browse checks cover restored settings, refresh behavior, slider background and shared successful-root counting. The new native check verifies duplicate normalized paths count once per load, reopening updates usage, failed roots create no entries, and refresh/subfolder activation do not count. The user confirms Phase 3 manual interactive checks pass. Fixture/codec limits remain in [PHASE3_INTEGRATION.md](PHASE3_INTEGRATION.md); MKV coverage is a WebM/Matroska subset.

## Root-scoped Undo/Redo

Each root folder has an independent persistent history, identified by its Windows filesystem identity. Rename/move within the same volume preserves the root's stack. File moves across roots belong to the source root; folder-menu actions belong to the root containing the target folder. Subfolder selection shares the owning root's stack. Overlapping loaded roots are resolved by actual node ancestry. Toolbar availability/previews and Ctrl+Z/Ctrl+Y execution use only the active Browse root; no active root means no Undo/Redo. Root/tab changes cancel pending history work; late previews cannot overwrite the new root's controls. New operations invalidate redo only within their root. Batch and compensation records retain the same scope.

Schema 5 adds nullable FileOperations.RootScope and its index transactionally; settings/history/favorites/layout/cache tables are preserved. Existing unscoped journal records/events remain intact for recovery/inspection but are excluded from active-root Undo/Redo because the original owning root was not recorded. Root identities must be readable; unavailable roots cannot supply history commands. Root deletion can leave no selectable root until that folder is restored/reloaded; another root cannot undo its actions.

Verification: 195 unit/filesystem tests, 101 native checks, zero failures/skips. New coverage includes independent branches across reopen, cross-root batches, compensation/event isolation, root rename, overlapping roots, schema-4 rollback/retry and preserved legacy/settings data. Actual MainWindow native checks verify disabled startup, root-switch buttons/tooltips and direct Browse Undo/Redo guards. Build succeeded with zero warnings/errors using a temporary BaseOutputPath because the user's running app locks normal output. The running app remains unchanged until rebuilt/restarted from updated code. New root-history physical interaction acceptance remains open.

## User-resolved name collisions

Browse move/rename/create-folder actions and Undo/Redo use an owned WinUI Rename/Overwrite/Cancel prompt. Rename selects a different incoming name; during history replay it temporarily renames the blocking existing item instead. Cancel/closing leaves data unchanged before mutations. Overwrite is explicitly chosen, disabled for unsafe/incompatible types, and preserves the old item for Undo or the opposite history action. Folder overwrite replaces the whole destination tree, not a merge; the Overwrite tooltip says so.

Forward preservation uses a unique backup in dedicated same-volume app-managed storage and the incoming change in one root-scoped journal batch. Undo restores both incoming and displaced versions, including original attributes; redo preserves the old version again. Failed installs attempt rollback; changed destination identities and intervening files are never silently overwritten. Historical blockers are recorded as compensations plus displaced-item event metadata; failed restoration/tampered backups report RecoveryRequired. Owned hidden backup directories are excluded from hierarchy scans. Backups are retained, not silently deleted or pruned; manual removal/modification can prevent Undo.

Schema 7 guards replacement/displaced-item history and backup-cleanup retirement semantics against older builds; migration preserves all existing journal records and settings. Latest verification: 241 unit/filesystem tests and 116 native checks, zero failures/skips; clean isolated solution build and whitespace check. Coverage includes file/folder/create replacements, repeated names, type mismatch, destination identity race, failed incoming rollback, history cancel/replay, Shell-restore conflicts and tampered backups. Native checks exercise Browse resolver/selection/root grouping and the real hidden owned dialog's three choices and disabled Overwrite. Real physical dialog keyboard/DPI/long-path interaction remains for manual review. Build outputs use a temporary BaseOutputPath while the user's app runs; rebuild/restart the app to load changes.

## Known limitations and deferred scope

- Home/settings/root-history wiring is implemented; new Home interactive acceptance remains open. Window layout is wired; cache-index/watcher wiring remains pending (4.6 onward).
- No FileSystemWatcher reconciliation. Browse changes require refresh/reload; Home checks saved-root availability on entry and Check folders.
- Expanded tree virtualization is disabled; very large trees can still consume substantial UI memory. Full logical-canvas GIF composition can use more CPU/memory than viewport-decoded static images.
- Native video clipping keeps WPF overlays reachable but exposes the WPF canvas beneath translucent areas rather than compositing over video.
- WebP/media support depends on installed Windows components. Animated WebP, additional codecs/FLAC and a bundled codec runtime remain outside the implemented scope.
- Disk thumbnails persist already, but there is no age/size pruning or persisted cache index. Add maintenance around the current cache.
- Adjustable text window sizes, gallery/continuous reader, broad customization and decimal-separator UI remain deferred. Carousel defaults to 11 total icons, configurable to odd counts from 1 to 25; image zoom uses 25% steps within 20%-500%.
- Preserve append-only journal history, conservative recovery, no silent overwrite, and no permanent-delete fallback during every migration and filesystem change.

## Historical verification record

The entries below describe earlier milestones and their test counts, not the current implementation snapshot. Current results and carryover are above and in PHASE3_INTEGRATION.md.

- Volume/jump/audio-first verification: all 138 tests passed; build succeeded with 0 warnings/errors; diff checks passed. A native controls probe verified the 0-100 slider, 73 -> 0.73 event conversion, FastForward/Rewind glyphs, responsive seeking, and retained mute/volume. An audio-first BrowseView folder-load probe reproduced a 220-pixel blank artwork slot (card height about 287 pixels), then verified it collapses with no artwork (card height about 51 pixels), with playback ready and a full-size viewport. Real album artwork, popup appearance and pointer interaction remain to be reviewed interactively.

- Playback controls refinement verification: all 138 tests passed; solution build succeeded with 0 warnings/errors; diff checks passed. New tests verify shared mute/volume across controller replacement, preservation of volume while muted, and clamped five-second jumps without changing playback state. Duration-format tests now expect brackets. A native transparent-window probe measured seekbar width growing from 34 to 234 pixels when the window grew from 900 to 1500 pixels, and verified a new MediaPlayer inherited mute=true and volume=0.7 before muted playback. Right-click/popup visual interaction still needs manual review.

- MediaPlayerElement migration verification (2026-11-10): all 136 tests passed; build succeeded with 0 warnings/errors; diff checks passed. Native probes initialized the existing WinUI environment in a transparent non-taskbar WPF window, opened MP4/WebM/Opus paused, verified duration, exercised muted play/seek/stop, and renamed each file after disposal. The MKV probe used the same WebM/Matroska subset fixture under a .mkv extension, not an independent full MKV sample. A native clipping region was applied to video probes. Real volume/speed popup layout, WPF overlay reveal/animation, focus/input bridging, audible output and additional codec combinations still require interactive review.

- Media verification (2026-11-10): all 135 tests passed; build succeeded with 0 warnings/errors; diff checks passed. New fake-backend tests cover initial type routing, paused opening, play/pause/stop, bounds/non-finite values, duration/seek availability, ended/restart behavior, failures, disposal and late opens/events. A native WPF probe verified PCM WAV opening/duration, muted playback, seek/stop, and renaming after close; invalid MP4 failed. A W3C Sintel H.264/AAC MP4 probe opened paused at 854x480 with audio and a 52-second duration, then played muted, sought and paused successfully. Probes ran in transparent, non-taskbar windows without audible output. WinUI controls, audible output, remaining routed formats, audio artwork, file-operation UI workflows, and broad Phase 3 integration still need interactive validation.

- WinUI/default-app verification (2026-11-10): all 116 tests passed; solution build succeeded with 0 warnings/errors; diff checks passed. Added resolver supersession/refresh/clear, bounded queued cancellation, STA/name/icon forwarding, missing/unknown association states, button availability, and native unknown-extension checks. A read-only native probe returned Notepad plus a PNG icon for .txt, Brave plus a PNG icon for .pdf, and no association for an unknown extension. The actual WinUI layout/icon display and launch/chooser interactions still need interactive verification.

- External-opening verification (2026-11-10): all 108 tests passed; solution build succeeded with 0 warnings/errors; diff checks passed. Fake launch-adapter tests cover default/chooser routing, STA execution, exact owner/path forwarding including Unicode and special characters, missing/relative/directory paths, missing associations, access failures, cancellation, and unchanged file contents. Tests do not launch real external applications. Native chooser/default-app behavior and fallback layout still need interactive verification.

- Text viewing verification: all 92 tests passed; solution build succeeded with 0 warnings/errors; diff checks passed. Coverage includes six encoding variants, word counts, BOM scope, empty files, bounded reads, Unicode boundaries, re-reading windows, malformed/binary data, cancellation, disposed streams, changed-file offsets, coordinator window routing, and word-count formatting. Interactive text scrolling/copying/paging and toolbar/focus checks remain pending.

- Toolbar formatting follow-up: metadata-only top line, one decimal digit, invariant-period default, KB/MB labels, compact bracketed dimensions; current folder segment is white in the muted full-path line. Decimal-separator settings UI remains deferred; formatter supports an explicit culture override. Verification: all 76 tests passed, solution build succeeded with 0 warnings/errors, and diff checks passed. Interactive appearance remains to be reviewed.

- Metadata UI verification (2026-11-09): all 75 tests passed; solution build succeeded with 0 warnings and 0 errors; `git diff --check` passed. Coverage adds binary size boundaries/large sizes, incomplete dimensions, missing metadata, localized decimals, duration formatting, and stale decode metadata suppression. Toolbar appearance still needs interactive review.

- Phase 3 Step 1 verification (2026-11-09): all 61 tests passed; solution build succeeded with 0 warnings and 0 errors; `git diff --check` passed. Added routing/capabilities, same-file supersession, late success/failure suppression, cancellation, disposable-result lifetime, input ownership, selection validation, and boundary navigation coverage. Interactive image/toolbar/focus regression checks have not been performed in this pass.

- Progressive hierarchy verification (2026-11-09): all 47 tests passed. Added shallow-root/breadth-first ordering, discovery cancellation, dynamic child totals, user-collapse preservation, and removed-folder error isolation coverage. Solution build succeeded with 0 warnings and 0 errors; `git diff --check` passed. Interactive large-tree timing has not been measured in this pass.

- Progressive-count verification (2026-11-09): all 43 tests passed, covering unknown/partially loaded totals, deferred counting, failed-count reporting, stream cancellation, and the previous count/filtering cases. Solution build succeeded with 0 warnings and 0 errors; `git diff --check` passed. Large-tree timing and interactive appearance have not been measured/verified in this pass.

- Folder-count prerequisite verification (2026-11-09): `dotnet test MediFiler3.sln --no-restore --verbosity minimal` passed all 40 tests, including new direct/nested counts, hidden/system exclusion, unsupported files, empty folders, ancestor notification, and cancellation coverage. `dotnet build MediFiler3.sln --no-restore --verbosity minimal` succeeded with 0 warnings and 0 errors. `git diff --check` passed. The updated folder-row appearance has not been manually verified.

- Step 2.3 verification: `dotnet test MediFiler3.sln --verbosity minimal -m:1` passed all 18 tests. `dotnet build MediFiler3.sln --verbosity minimal -m:1` succeeded with 0 warnings and 0 errors. `git diff --check` is clean.
- Before the latest interface revisions, the user verified the image viewer, original carousel thumbnails and transitions, Shell fallback, and file navigation as sufficient for the current stage.
- Step 1.8 verification: user reports no UI issues. Source review found no integration break in folder loading, state propagation, toolbar context/title updates, or selection refresh. `dotnet build MediFiler3.sln -p:BaseOutputPath=C:\Users\xofas\AppData\Local\Temp\MediFiler3Step18Final\` succeeded with 0 warnings and 0 errors. `git diff --check` is clean. No automated tests exist yet.

- Step 2.4 adds journal-backed file moves/renames, folder creation/moves/renames, and Windows Recycle Bin actions. Existing destinations are conflicts and are never overwritten. Cross-volume file moves use a temporary copy with content verification and source revalidation before source removal; cross-volume folder moves are safely rejected. Folder operations reject reparse points. Non-empty folder deletion requires explicit confirmation, and the WinUI confirmation is now implemented. Failed Recycle Bin requests never fall back to permanent deletion.
- The Browse tree has a right-aligned folder menu button for create, rename, move, Recycle Bin, refresh, Explorer, and properties actions. The ellipsis button opens a WinUI `MenuFlyout` from the existing toolbar XAML Island. File/folder rename, New Folder, and non-empty folder deletion confirmation originally used standalone WinUI windows; the ContentDialog migration below supersedes that hosting. Move Folder To uses the basic native folder picker, initially showing the source folder's parent. The existing Add Folders workflow retains its system multi-folder picker; read warnings appear in the Browse status notice. The tree panel has a session-only Pin/Unpin control with inverted selected colors and glyphs rotated 90 degrees counterclockwise; persist its state with the future settings store. Left-click remains navigation; right-click moves the current file to the clicked folder. The WinUI toolbar Island exposes current-file rename and Recycle Bin actions; F2, Delete, and Ctrl+Shift+N provide keyboard shortcuts. Operation outcomes appear in Browse.
- Step 2.4 filesystem tests cover same-volume move/rename, content preservation, conflicts, folder creation/move, nested-move rejection, confirmation requirements, and cross-volume folder-move refusal where another volume is available.
- Step 2.4 verification: dotnet test MediFiler3.sln --verbosity minimal -m:1 passed all 25 tests. dotnet build MediFiler3.sln --verbosity minimal -m:1 succeeded with 0 warnings and 0 errors. git diff --check is clean. Recycle Bin shell behavior and the new menu's interactive appearance have not been manually exercised in this verification pass.
- Step 2.5 verification (2026-11-03): `dotnet test MediFiler3.sln --no-restore --verbosity minimal` passed all 37 tests. `dotnet build MediFiler3.sln --no-restore --verbosity minimal` succeeded with 0 warnings and 0 errors. `git diff --check` is clean. The user confirmed that Shell restore and the Undo/Redo tooltips work as expected.

Rename/collision presentation refinement: file name inputs omit and preserve the original extension. Collision Rename applies the next available numbered suffix before that extension in one click, skipping occupied file/folder names without opening another dialog. The owned WinUI collision prompt compares incoming and existing files with independent asynchronous thumbnails, readable sizes, full-path and exact-byte hover text, and clear unavailable-preview placeholders. Explanatory body text is removed; the dialog fits its comparison cards/buttons with 24 px insets. Native checks assert the bottom spacing and direct Rename result. Folder sizes are not recursively scanned. Closing cancels pending previews and suppresses late updates. Verification: 241 unit/filesystem tests and 116 native checks passed, zero skips; actual WinUI checks confirm both image sources/sizes, extension-free input and numbered-name acceptance. Physical appearance/DPI/keyboard review remains pending.

## App-managed overwrite backups and explicit cleanup

New backups use %LOCALAPPDATA%\MediFiler3\UndoBackups on the original volume, or a dedicated hidden user-specific directory at another volume's root. They no longer sit alongside user files. Same-volume moves preserve IDs and folder Undo; inaccessible/linked storage refuses replacement. Legacy sibling backups remain usable at their recorded paths and can be cleared through the same action.

Settings > Clear overwrite backups confirms permanent removal and loss of the affected Undo/Redo groups. Cleanup is serialized with mutations, verifies journal ownership and identities, durably retires whole root-scoped groups (including history dependencies), then removes only their stored backups. It retains changed, linked, inaccessible or recovery-ambiguous items, reports failures and supports retry. Current/restored files and unrelated history remain. Schema 7 preserves all intents/events/settings while adding compatibility protection for HistoryDiscarded.

Verification: 241 unit/filesystem tests and 116 native checks, zero failures/skips; clean isolated build. Coverage includes cross-root group retirement, folder/readonly cleanup, Undo-restored data, history blockers, locked-file retry, changed IDs, recovery/compensation ambiguity, legacy/untracked items, unavailable storage, concurrent replacement/cleanup and schema-6 migration. Native checks exercise the real Settings button and owned confirmation for both Cancel and Clear, final status and retained current files. Physical Settings appearance and other-volume storage permissions remain manual acceptance items.

## WinUI ContentDialog migration

All app-owned naming, confirmation and collision prompts now use actual ContentDialog controls, including startup/settings-failure warnings whenever WinUI is available. Native button results preserve name/extension handling, whitespace trimming, direct numbered Rename, incompatible Overwrite disabling, thumbnails/sizes, safe default Cancel and cleanup confirmation. A shared owned borderless XAML Island host provides modality above WPF/native media, sizes to the control and maps Escape/host closing to cancellation. Folder selection, Open With and Properties remain Windows system dialogs. Startup failure still has an emergency MessageBox if WinUI itself cannot initialize or display a warning.

Verification: 241 unit/filesystem tests and 116 native checks passed; solution build clean. New native probes cover file/folder/new-folder text submit, trimmed results, Cancel, host close, Escape and ownerless warning OK. Existing collision/preview and Settings cleanup probes now inspect and operate ContentDialog template buttons. Physical keyboard/focus, DPI and visual acceptance remain manual review items. No schema or file-operation semantics changed.

## Step 4.5: window layout persistence

MainWindow restores its normal size/position and maximized state before first show. Monitor identity and logical work-area offsets preserve placement across rearrangement/scaling; missing monitors fall back to primary, bounds are kept within a current work area and minimum sizes relax on smaller displays. Closing while minimized preserves the preceding normal/maximized state and normal restore rectangle. Display/work-area/DPI changes reconcile during the session without interrupting active move/resize gestures.

Application WindowLayoutService coalesces background writes and retries dirty state; Closing captures latest geometry before App flushes. Invalid/unreadable state uses logged safe defaults without overwriting it until a real change. The existing SQLite WindowLayout payload gains optional monitor metadata; schema 7 remains unchanged. Home remains startup and no roots/media are reopened. Existing folder pin persists; adjustable panel widths and extra navigation preferences were not introduced.

Verification: 258 unit/filesystem tests and 122 native checks passed, zero failures/skips; clean isolated solution build. New coverage includes geometry/DPI/disconnected-monitor cases, coalescing/concurrent writes/failure retry, SQLite metadata restart, actual normal/maximized first show, restored normal bounds, minimized close/reopen and deferred live work-area reconciliation. Physical multi-monitor/DPI/taskbar-layout review remains pending. Next planned task is 4.6.
