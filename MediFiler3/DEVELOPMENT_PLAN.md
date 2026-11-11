# Development plan

## Phase 5.4 implemented (2026-11-11)

Gallery grouped moves, confirmed journal-backed grouped Recycle Bin commands and root-scoped Undo/Redo are implemented with explicit partial/cancelled/unattempted outcomes. See PROJECT_STATUS.md and PHASE5_HANDOVER.md for command scope and safeguards. Phase 5.5 physical/performance acceptance remains next. Bulk rename is not planned. Earlier 5.4-pending entries below are historical.

## Current continuation state (2026-11-11)

Phase 5.1-5.3 and the subsequent Gallery UX/resume/thumbnail refinements are implemented. Current rendering uses Windows-default shared PNG sizes with no manual blur, solid pink outlines, square carousel frames and Alt-hover selection; native Ctrl/Shift behavior stays standard. Full/disjoint native source changes batch one Reset, while shared-path changes remain incremental. Slider display dimensions remain independently persisted at 100-400/50, with fixed 150-pixel control widths.

Current branch/HEAD: master/0880e94; subsequent source/tests remain uncommitted. Fresh handover verification: 316 unit/filesystem tests and 181 native checks passed without failures/skips; build zero warnings/errors. Current snapshot and next actions: [PROJECT_STATUS.md](PROJECT_STATUS.md); implementation caveats and pending decisions: [PHASE5_HANDOVER.md](PHASE5_HANDOVER.md). Earlier "not started", baseline hashes and intermediate counts below describe their original milestones.

| Next work | Boundary |
|---|---|
| Thumbnail review | Latest Windows-default/no-blur appearance and DPI behavior await user feedback; crop changes were not adopted |
| 5.4 | Plan concrete command scope/partial outcomes/cancellation, reuse existing grouped moves, add journal-backed grouped recycle and validate collisions/Undo/Redo; retain current guards until ready |
| 5.5 | Continue targeted reconciliation/performance/native stability checks and physical acceptance; existing automation alone does not close these limits |
| Later | Continuous reader, additional sorts/filtering and drag/drop remain separately scoped |

This final turn is documentation/verification only, not authorization to implement the next slice or to commit/push.

## Dated milestones

## Phase 5.3 implemented (2026-11-11)

Native Gallery Extended selection now wires Ctrl/Shift pointer and native keyboard behavior to session selection, current-file focus and accessible borders/count/file information. Ctrl+A, Escape, focused Enter and a keyboard/mouse context menu provide validated selection/open commands. Reconciliation preserves current selected records and prunes stale old-grid input; Home/Settings preserve selection while Browse clears it. Select All remains virtualized for 10,000 files, with bulk clearing. Existing Gallery mutation/history guards remain until 5.4, which is the next implementation slice. No storage/provider/watcher/operation-policy changes were made. Build clean; 308 unit/filesystem tests and 176 native checks passed without failures/skips. Physical input, Narrator and mixed-DPI acceptance remains open; see PROJECT_STATUS.md for evidence and runner limits.

## Pinned tree on Gallery return fixed (2026-11-11)

Gallery reapplies its native clipping region after reattachment/layout so Home/Settings returns cannot cover the pinned WPF tree. Both return paths now check the actual HWND region as well as pin presentation and tile inset. Build clean; 306 unit/filesystem tests and 169 native checks passed, zero failures/skips. Existing retention, selection, watcher and operation behavior is preserved; remaining 5.3/5.4 scope and physical review limits are unchanged.

## Gallery selection positioning on return fixed (2026-11-11)

Gallery now defers the shared current-file reveal until native loading/layout is ready, retaining a guarded pending request until the target is visible and yielding to newer navigation. Both Home/Settings return paths were reproduced with a distant selection in 150 files. First entry, both return paths, user override and thumbnail preservation pass. Clean build; 306 unit/filesystem tests and 167 native checks, zero failures/skips. No cache/provider/operation-policy changes were introduced by this fix; remaining 5.3/5.4 scope and physical review limits are unchanged.

## Gallery watcher-resume regression fixed (2026-11-11)

Healthy coverage startup now reconciles metadata without treating the entire root as changed. Home/Settings returns preserve unchanged native thumbnail objects; offline metadata edits/additions/removals reconcile selectively. Strong event/F5/overflow/reconnection invalidation remains. Build clean; 306 unit/filesystem tests and 163 native checks pass without failures/skips, including real-watcher view-return regressions and unavailable-root recovery. Same-size/timestamp edits without a delivered hint still require F5. Existing uncommitted provider changes remain preserved; 5.3/5.4 scope is unchanged.

## StorageItemThumbnail backend implemented (2026-11-11)

Normal cache misses now use StorageFile.GetThumbnailAsync/StorageItemThumbnail, with WinRT PNG normalization and fresh-source image fallback for invalidated/system-icon-only cases. Existing cache keys, memory retention, shared views and file-operation safety remain. Optional freshness metadata preserves known invalidation through restart and explicit image-cache clear without a schema change. SDK targets align to Windows 10.0.19041. Build clean; 303 unit/filesystem tests and 160 native checks pass, zero failures/skips. No performance improvement or physical acceptance is inferred; remaining Gallery 5.3/5.4 scope is unchanged.

## Center framing/memory retention refinement complete (2026-11-11)

Gallery matches carousel-style center-square framing and clips to rectangular tile bounds, retaining the same original-aspect bitmap for hover. Idle disk maintenance no longer blanket-flushes encoded memory; native decoded retention now permits 512 entries/128 MiB while explicit clearing and targeted stale-data safeguards remain. Current thumbnail provider remains WIC + Shell; StorageItemThumbnail/GetThumbnailAsync was researched as a benchmark candidate rather than assumed faster. Build clean; 299 unit/filesystem tests and 160 native checks pass, zero failures/skips. The local fixed 150-pixel slider-width change is preserved; physical input/performance/DPI review and 5.3/5.4 remain pending.

## Shared-thumbnail and hover refinement complete (2026-11-11)

Width/Height sliders now expand to 110 pixels (responsive minimum 55). The sidebar is the sole normal-mode unpinned-tree opening trigger. Carousel and Gallery share a 400-by-400 PNG cache source, with per-key concurrent generation deduplication and targeted memory/in-flight/carousel invalidation. Cancellation, same-metadata refresh and cache ownership safeguards remain verified. Clean build; 297 unit/filesystem tests and 160 native checks pass without failures/skips. Physical performance/hover/DPI review remains open; 5.3/5.4 scope is unchanged.

## Gallery navigation/retention refinement complete (2026-11-11)

Compact Width/Height sliders, centered Gallery rows, selected borders, carousel double-click handoff and whole-sidebar unpinned-tree hover are implemented. Reload regressions now preserve incremental native listings and decoded previews during recycling, with bounded native retention and unchanged watcher/F5 invalidation safety. Build clean; 292 unit/filesystem tests and 160 native checks pass without failures/skips. Physical input/hover/DPI review remains open; 5.3/5.4 scope remains pending.

## Gallery UX refinement complete (2026-11-11)

Gallery now retains its grid/realized previews across tabs, reveals the shared current file, uses tightly spaced fill-cropped tiles with gradient filename overlays and hover-to-fit/caption-hide behavior, and reflows beside a pinned tree. Both sliders persist 100–400 in steps of 50; legacy continuous values normalize without losing unrelated settings or writing merely on read. Retained previews preserve watcher/F5 same-metadata invalidation. Build clean; 292 unit/filesystem tests and 156 native checks pass with zero failures/skips. Physical input/hover/DPI review remains open. This supersedes the earlier 64–384/dispose-on-deactivation description; the remaining 5.3/5.4 scope is unchanged.

## Phase 5.2 implemented (2026-11-11)

Gallery is now a separate sidebar tab alongside Browse, using a native virtualized GridView and independent persisted right-toolbar X/Y sliders (64–384, default 150). Shared collection/watcher context stays current; inactive Browse rendering and Gallery realization are deferred until requested. Native tiles own cancellable bounded thumbnail work, recycled images release, F5/live reconciliation reuse existing services, and native clipping preserves tree/fullscreen reachability. Single selection and double-click/Enter handoff are available; 5.3 multi-selection/input and 5.4 grouped commands remain next. Earlier 5.2-next/no-Gallery-UI entries below describe the previous checkpoint.

Verification: clean solution build; 289 unit/filesystem tests and 151 native checks, zero failures/skips; diff formatting clean. Physical Gallery input/DPI/slider/hover review remains open, alongside all recorded Phase 4 limits.

## Phase 5 approved slices (2026-11-11)

- **5.1 complete:** Application viewing-mode/session and Gallery selection foundations, validated Browse handoff, refresh reconciliation and eight business-logic regressions. At that foundation-only milestone, 284 unit/filesystem tests and 144 native checks passed before 5.2 exposed the Gallery UI.
- **5.2 complete:** Virtualized Gallery UI, retained bounded thumbnail work, shared collection/watcher lifecycle and two persisted independent right-toolbar width/height sliders (100–400, step 50), including the subsequent documented UX/resume fixes.
- **5.3 complete:** Mouse/keyboard/accessibility and command scope wiring; Gallery mutations remain guarded pending 5.4.
- **5.4 next:** Safe grouped operations, existing grouped moves and journal-backed grouped Recycle Bin support, partial-result/collision/Undo checks.
- **5.5:** Full reconciliation/performance/regression and targeted physical acceptance.

Retain shared natural filename ordering for the MVP; further sort options, drag/drop and continuous reader follow separately. Earlier Phase 5-not-started notes below describe the handover baseline and are superseded by this entry.

This plan tracks the agreed implementation sequence. Product requirements and full acceptance criteria are in [SPECIFICATION.md](SPECIFICATION.md); architecture decisions are in [ARCHITECTURE.md](ARCHITECTURE.md); the current implementation snapshot is in [PROJECT_STATUS.md](PROJECT_STATUS.md).

## Phase 1 — Foundation and core browsing (complete)

Deliver a responsive folder browser with an image viewer. Check the feature acceptance criteria while implementing each step; 1.8 is for end-to-end integration and refinement.

| Step | Deliverable | Status |
|---|---|---|
| 1.1 | Solution structure, dependency injection, logging, and WPF application startup | Complete |
| 1.2 | Main shell, Home/Browse navigation, high-contrast styling, full-width WinUI CommandBar and compact WinUI navigation rail in WPF XAML Islands | Complete; UI reviewed |
| 1.3 | Load one or more separate folder roots and display their folder tree asynchronously | Complete |
| 1.4 | Enumerate only the active folder's files, apply Windows Explorer-style numeric sorting, and centralize active folder/file navigation state | Complete |
| 1.5 | Display common images at fit-to-view, preserve aspect ratio, and release viewer resources | Complete |
| 1.6 | Shared asynchronous thumbnail service, caching, placeholders, and file carousel | Complete; UI reviewed |
| 1.7 | Keyboard and mouse file navigation, image right-click zoom toggle, and Numpad plus/minus zoom | Complete; UI reviewed |
| 1.8 | Integration checks and refinement for responsiveness, errors, and usability | Complete; user reports no UI issues |

**Phase 1 acceptance:** load separate roots; browse a folder tree; show that folder's sorted files; display and navigate images; update a carousel; preserve UI responsiveness for large folders. File operations, media playback, persistence, gallery, and continuous reader are later phases.

## Phase 2 — Safe file organization

Establish the persistent SQLite operation journal before implementing broad filesystem mutations. Add move, rename, create-folder, and send-to-Recycle-Bin operations, followed by undo/redo and interrupted-operation recovery. Confirm non-empty folder deletion with Yes/No, never overwrite silently, and show best-effort Recycle Bin restore outcomes. Add tests with isolated temporary directories for domain/application logic and Windows filesystem integration where feasible.

Recommended order:

| Step | Deliverable | Status |
|---|---|---|
| 2.1 | Define operation records/states and journal contract; add SQLite in Infrastructure and initialize/reopen a versioned database safely | Complete |
| 2.2 | Persist append-only journal events and inspect/report prepared operations left by a crash | Complete |
| 2.3 | Add journal/recovery tests before filesystem mutations | Complete |
| 2.4 | Add safe filesystem operation services, conflict handling, and folder/file actions in the UI | Complete |
| 2.5 | Add verified undo/redo, batch grouping, UI refresh/results, and filesystem-operation coverage | Complete: Shell restore, Undo/Redo hover previews, and automated coverage implemented; Windows behavior confirmed by the user |

Implementation order:

1. Define operation records, journal states, and application-owned journal contracts. Add the SQLite dependency in Infrastructure and create/reopen the database safely.
2. Implement journal persistence for prepared, completed, failed, and interrupted operations. Persist intent before a filesystem mutation and verify the result before marking it complete. Add startup recovery/reporting for prepared operations left by a crash.
3. Add automated tests for journal creation/reopen/migration, event transitions, redo invalidation, and recovery classifications before filesystem mutations.
4. Add filesystem operation services behind application contracts, with conflict checks and safe move/rename/create/Recycle Bin behavior. Do not allow permanent-delete fallback when Recycle Bin operations fail. **Complete:** file/folder move and rename, folder creation, Recycle Bin actions, and UI integration are implemented; cross-volume folder moves are rejected safely.
5. Add undo/redo as verified compensating operations, batch grouping, UI refresh, user-facing conflict/error results, and isolated temporary-directory filesystem tests. **Complete:** move/rename and safe folder-creation compensation, grouped file moves, toolbar controls, and filesystem coverage are implemented. Undo and redo button tooltips show the planned changes before the user clicks; large batches show the first eight entries and the remaining count. Clicking a button or using its keyboard shortcut applies immediately, with the existing conflict checks. Recycle Bin restore uses a Windows Shell namespace adapter with strict path/type/identity matching and a file-content fingerprint fallback when restore changes the Windows file ID. The user confirmed the Shell restore and tooltip behavior.

## Phase 3 — Media and text handlers

The pre-Phase-3 performance prerequisites now publish shallow roots first, populate the hierarchy through a cancellable breadth-first background stream with bounded UI updates, and then populate file counts progressively. Unknown placeholders, child-discovery completion state, cancellation, and stale-count protection are implemented. Tree virtualization and large expanded-tree rendering/memory remain future performance concerns.

Add basic video and audio playback, bounded text viewing, unsupported-file details and open-with behavior, relevant metadata, and handler-specific interaction routing. Keep file navigation state centralized.

Agreed implementation order (metadata moved ahead of text/media):

1. **Complete:** viewer/navigation extraction, handler capability and metadata/resource contracts, input policy, and modular image-only toolbar controls.
2. **Complete:** metadata presentation in the toolbar's left section: file size for all current files and original image resolution after decoding. Use a metadata-only top line and a full-path bottom line with the current folder name in white, clear stale metadata on selection changes, and preserve known resolution during same-file zoom/resize reloads. Duration formatting is ready for later media handlers.
3. **Complete:** bounded text viewing with strict UTF-8 and BOM-marked UTF-16/UTF-32, 1 MiB windows, previous/next-window controls, navigation/scrolling toggle (right-click, F4, toolbar), and whole-file or clearly scoped window word counts.
4. **Complete:** unsupported-file fallback retains thumbnail previews and adds filename/path details, separate default-application and native Open With actions, keyboard shortcuts, and clear missing-file/association/access/cancellation results. Failed integrated previews also offer external opening. Unsupported-file viewer right-click opens the existing native Open With chooser, with or without a thumbnail. Native launch/chooser behavior was accepted in the user manual review.

   Follow-up complete: external-open actions are now compact WinUI AppBarButtons in the center toolbar, available for every selected file type while image/text controls remain on the right. Async default-application lookup supplies the application name/icon, handles packaged AppsFolder identities, distinguishes absent associations from lookup failures, refreshes after chooser completion, and rejects stale results. Real read-only probes verified Notepad/Brave names/icons; interactive WinUI appearance and launching were accepted in the user manual review.
5. **Implemented:** WinUI MediaPlayerElement/Windows MediaPlayer replaces the initial WPF backend while preserving per-file sessions, paused opening, controls, duration and safe resource release. Added internal routing for .opus/.webm/.mkv; codec availability remains Windows-dependent. Volume has a root-unconstrained horizontal popup with a clickable speaker icon, a 0-100 slider/numeric level, and quick toolbar-button right-click muting. Jump buttons use FastForward/Rewind. Audio-first layout no longer reserves blank artwork space; artwork loads asynchronously when available. Speed controls are removed; the seekbar stretches with the toolbar and -5/+5 second jumps are available. Shared mute/volume carry across media; duration metadata is bracketed. Native MP4/WebM/Opus probes passed; a WebM subset fixture opened with an MKV extension. Interactive overlay/focus behavior, audible output and popup layout were accepted by the user; broad MKV variants remain subject to codec availability.
6. **Complete; user manual acceptance confirmed 2026-11-10:** automated native integration passed 81 checks with zero skips across every routed image/text/media extension, plus all 141 unit/filesystem tests. Backward folder overscroll now enters at the last file; forward entry remains at the first file, with empty-folder skipping and viewer dispatch covered. The explicit Windows runner covers real decoding/thumbnails, metadata/surfaces, mixed navigation, input routing, cancellation, stale artwork, toolbar groups, media progress and suspension/rename/reload. The user confirmed the interactive appearance/DPI, physical focus/input, audible output, native launching and Browse file-operation checks pass. See [PHASE3_INTEGRATION.md](PHASE3_INTEGRATION.md) for evidence, fixture limits, reproduction and accepted interactive checks.

Playback backend/initial format decisions are settled as above. Broader codec support requires separate evidence and discussion. Text starts with strict UTF-8 and BOM-marked UTF-16/UTF-32, with 1 MiB windows. Metadata placement is settled in the left toolbar top line; the full path remains below with its current folder name white. Metadata scope includes file size, image resolution, text word count, video pixel height (`[1080p]`), and audio/video duration. WebP now routes through the existing WIC image/thumbnail decoders; an installed Windows codec is required. Animated GIF playback is implemented with frame timing/looping/composition, one-cycle duration metadata, image zoom/pan and deterministic release; animated WebP remains deferred. Volume popup sizing is explicitly compact to match the toolbar. Text viewing is implemented; external-opening fallback and initial media handlers are implemented; Phase 3 integration and user interactive acceptance are complete.

## Phase 4 — Home and persistence

Persist recent, frequent, and favorite folders, settings (including restoring/saving shared media volume and mute across application sessions), window/panel layout, and thumbnail cache metadata. Reconcile filesystem changes and identify missing or inaccessible folders.

**Phase 4 implemented and tentatively accepted by the user; 4.7 automated acceptance complete, untested physical cases retained as limits.** New Home visual/input/DPI/picker acceptance remains open. Phase 3 features, automated checks and user manual acceptance are complete. See [PHASE3_INTEGRATION.md](PHASE3_INTEGRATION.md). See [PHASE4_HANDOVER.md](PHASE4_HANDOVER.md) for code entry points, current database details, proposed policies and acceptance requirements.

Recommended implementation sequence:

| Step | Deliverable | Status |
|---|---|---|
| 4.1 | Application persistence contracts/models and journal-preserving SQLite schema migration, with upgrade/reopen tests | Complete; journal-preserving creation/migration/reopen/rollback coverage |
| 4.2 | Existing settings persistence, restoring volume and mute before media opens, plus carousel density and folder pin | Complete; coalesced saves, shutdown flush, restart and native checks |
| 4.3 | Root-only usage/favorites service, ordering, bounded availability checks and user-directed locate/remove handling | Complete; root-load counting connected, service actions tested |
| 4.4 | Working Home sections, root activation/recent removal, synchronized star toggles and unavailable-root actions | Implemented; physical Home acceptance pending |
| 4.5 | Window size/position/maximized persistence validated against monitor work areas and DPI; existing panel pin persists | Implemented; automated restart/display checks pass, physical multi-monitor review pending |
| 4.6 | External filesystem reconciliation and thumbnail cache metadata/maintenance | Implemented; agreed watcher/cache policy and automated checks, physical acceptance pending |
| 4.7 | Restart/integration acceptance and relevant Phase 3 carryover checks | Automated checks complete; physical acceptance remains open (PHASE4_ACCEPTANCE.md) |

Keep contracts in Application, SQLite/watchers in Infrastructure and UI dispatch in App. The database is schema version 7 with separate versioned-payload settings, root history/favorites, layout and thumbnail-index tables alongside the operation journal. Infrastructure stores are registered; settings UI wiring is complete. Home and window layout are wired; cache wiring is implemented in 4.6; root usage is connected to successful explicit root loading. Define migration behavior for fresh databases and every supported old schema; do not change the version constant without updating creation/migration paths. Journal intents/events, compensation links and undo/redo candidates must survive. Visits count explicit opens of the specific root only; subfolder history is not persisted. Unavailable/renamed roots remain for user-directed manual location or entry removal. Watcher scope and cache retention were approved and implemented in 4.6; see ARCHITECTURE.md for the policy.

Step 4.2 also adds the user-requested center-toolbar Refresh action (F5), preserving the file path or nearest valid position across external changes and progressively restoring nested context. Automatic watchers remain 4.6. Verification: 157 unit/filesystem tests, 87 native checks with zero skips, and a clean solution build. Physical new-control acceptance remains to be reviewed.

Step 4.3 counts distinct normalized successful roots once per explicit load across all entry points; subfolder selection, refresh and overscroll are excluded. RootFolderHistoryService serializes visits/favorites, returns deterministically ordered shared lists and provides atomic Locate (no implicit merge) and full saved-entry removal. Availability work is capped at two probes with three-second caller timeouts. No history is silently pruned. Verification: 167 unit/filesystem tests and 89 native checks, zero skips; build clean. Step 4.4 manages favorites only from Home, with matching stars in Recents/Frequent/Favorites and alphabetical Favorites. Native WinUI cards use responsive columns, standard spacing, empty states and Show more (25 entries per section initially). Local data appears before progressive checks. Home entry opening, Add folders and drops use the existing root loader. Locate/Remove entry are wired with clear errors/cancellation and duplicate-input guards. Verification: 167 unit/filesystem tests, 99 native checks, zero skips; native capture inspected. Physical input/DPI/picker/drop and visual acceptance remain open.

## Phase 5 — Alternative viewing modes

Add a virtualized gallery with mode-local multi-selection and grouped operations, then a continuous image reader with lazy loading, bounded preloading, resource release, and synchronized folder/file context.

## Phase 6 — Refinement and optional scope

Add search/filtering, drag and drop, expanded metadata, duplicate detection, additional handlers, themes, and performance/usability refinements as separately reviewable work.

## Working rules

- Keep WPF as the host. Prefer suitable WinUI controls over WPF equivalents for native behavior and consistent styling; the navigation rail is hosted in an XAML Island.
- Keep the app navigation rail permanently compact and icon-only. Fullscreen is implemented with coordinated edge reveals; retain the compact rail in normal mode.
- Build after changes. Maintain automated coverage for business logic and filesystem operations in `MediFiler3.Infrastructure.Tests` as features are implemented.
- Update [PROJECT_STATUS.md](PROJECT_STATUS.md) when a step changes status, and keep architecture/specification decisions synchronized.

## Completed follow-up milestones

The counts below describe individual milestones. Current verification is 276 unit/filesystem tests and 144 native checks; consult PROJECT_STATUS.md for the final snapshot and physical acceptance limits.

Home follow-up: compact relative-time captions (seconds/minutes/hours/days/30-day months/365-day years), exact time/full path on hover, no Ignore, and unified Remove entry across all lists. At this earlier milestone, viewport-bound left-aligned content fixed wide-window card positioning (superseded by the later user-requested centered layout); native containment checked through 3000 px. Latest verification: 188 unit/filesystem tests and 99 native checks, zero skips; build clean.

Root-history follow-up: independent Undo/Redo scopes keyed by root filesystem identity; source-root ownership for cross-root file moves, target-root ownership for folder-menu actions, scoped redo invalidation/batches/compensation and stale-context cancellation. Schema 5 migration preserves legacy history as unscoped; active-root UI excludes legacy entries instead of inferring ownership. Verified 195 unit/filesystem tests and 101 native checks, zero skips; clean build in temporary output while the user app runs.

Collision workflow follow-up: user-resolved Rename/Overwrite/Cancel for move, rename, folder creation and history collisions. Overwrite preserves whole destination items in dedicated app-managed backup storage; one root-scoped Undo reverses the incoming operation and restores the old version. Folder overwrite is whole-folder replacement, not merging. History replay renames/preserves the blocking existing item and restores it with the opposite action. Schema 6 guards these semantics against older builds while preserving existing records. Verification: 241 unit/filesystem tests and 116 native checks, zero skips; clean isolated build. Manual native dialog keyboard/DPI/long-path review remains.

Backup management follow-up complete: dedicated app-managed same-volume storage, Settings cleanup confirmation and outcome reporting, durable retirement of dependent root/batch Undo/Redo before deletion, conservative legacy/recovery/identity handling and serialized mutations/cleanup. Schema 7 preserves existing records. Verified 241 unit/filesystem tests and 116 native checks; physical Settings/other-volume review remains.

ContentDialog follow-up complete: all app-owned prompts and available application warnings use WinUI ContentDialog through a shared owned XAML Island host. Actions/results remain unchanged; Windows pickers/choosers and emergency WinUI-unavailable error reporting retain their platform behavior. Native checks cover text submit/cancel, close/Escape, warnings, collisions and cleanup. Current verification: 241 unit/filesystem tests and 116 native checks; clean build.

Step 4.5 implemented: logical normal bounds, monitor metadata/offsets and last normal/maximized state; clamp/fallback for current work areas and DPI, runtime reconciliation, deferred adjustment during move/resize, coalesced writes and capture/flush before shutdown. Existing WindowLayout JSON/table is reused with additive optional metadata; schema stays 7. Verified 258 unit/filesystem tests and 122 native checks. Physical monitor/DPI/taskbar review remains; step 4.6 is now implemented.

## Immediate continuation

Implementation baseline is `0880e94` (`Gallery view selection initial`) on master with later source/test refinements uncommitted. The current handover updates documentation only. Phase 4 remains tentatively accepted with open physical limits; Phase 5.1-5.3 are implemented, with current defaults and verification recorded above and in PROJECT_STATUS.md.

Next conversation: read the current status and PHASE5_HANDOVER.md, inspect Git, and review feedback on the no-blur/Windows-default thumbnail experiment. If proceeding to 5.4, resolve the concrete grouped command and journal/partial-result behavior before implementation. Preserve the agreed Gallery selection/layout/watch/cache/native lifetime and operation safeguards. Continuous reader remains later; no new feature was implemented during this handover.

No automatic checkpoint is required: implementation and current handover Markdown are uncommitted, and this documentation request does not authorize staging, committing or pushing.

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

Final UI detail: enabled favorite stars are filled solid white, with inactive gray outlines. See PROJECT_STATUS.md for the consolidated completed behavior and PHASE4_HANDOVER.md for the next-conversation prompt.
