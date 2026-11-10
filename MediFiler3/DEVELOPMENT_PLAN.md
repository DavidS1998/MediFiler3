# Development plan

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

**Steps 4.1 through 4.5 implemented; 4.6 is next.** New Home visual/input/DPI/picker acceptance remains open. Phase 3 features, automated checks and user manual acceptance are complete. See [PHASE3_INTEGRATION.md](PHASE3_INTEGRATION.md). See [PHASE4_HANDOVER.md](PHASE4_HANDOVER.md) for code entry points, current database details, proposed policies and acceptance requirements.

Recommended implementation sequence:

| Step | Deliverable | Status |
|---|---|---|
| 4.1 | Application persistence contracts/models and journal-preserving SQLite schema migration, with upgrade/reopen tests | Complete; 151 unit/filesystem tests pass |
| 4.2 | Existing settings persistence, restoring volume and mute before media opens, plus carousel density and folder pin | Complete; coalesced saves, shutdown flush, restart and native checks |
| 4.3 | Root-only usage/favorites service, ordering, bounded availability checks and user-directed locate/remove handling | Complete; root-load counting connected, service actions tested |
| 4.4 | Working Home sections, root activation/recent removal, synchronized star toggles and unavailable-root actions | Implemented; 99 native checks pass, interactive review pending |
| 4.5 | Window size/position/maximized persistence validated against monitor work areas and DPI; existing panel pin persists | Implemented; automated restart/display checks pass, physical multi-monitor review pending |
| 4.6 | External filesystem reconciliation and thumbnail cache metadata/maintenance | Not started |
| 4.7 | Restart/integration acceptance and relevant Phase 3 carryover checks | Not started |

Keep contracts in Application, SQLite/watchers in Infrastructure and UI dispatch in App. The database is schema version 7 with separate versioned-payload settings, root history/favorites, layout and thumbnail-index tables alongside the operation journal. Infrastructure stores are registered; settings UI wiring is complete. Home and window layout are wired; cache wiring remains pending; root usage is connected to successful explicit root loading. Define migration behavior for fresh databases and every supported old schema; do not change the version constant without updating creation/migration paths. Journal intents/events, compensation links and undo/redo candidates must survive. Visits count explicit opens of the specific root only; subfolder history is not persisted. Unavailable/renamed roots remain for user-directed manual location or entry removal. Cache retention and watcher scope remain to be settled during their steps. Proposed handover defaults are not yet implemented requirements.

Step 4.2 also adds the user-requested center-toolbar Refresh action (F5), preserving the file path or nearest valid position across external changes and progressively restoring nested context. Automatic watchers remain 4.6. Verification: 157 unit/filesystem tests, 87 native checks with zero skips, and a clean solution build. Physical new-control acceptance remains to be reviewed.

Step 4.3 counts distinct normalized successful roots once per explicit load across all entry points; subfolder selection, refresh and overscroll are excluded. RootFolderHistoryService serializes visits/favorites, returns deterministically ordered shared lists and provides atomic Locate (no implicit merge) and full saved-entry removal. Availability work is capped at two probes with three-second caller timeouts. No history is silently pruned. Verification: 167 unit/filesystem tests and 89 native checks, zero skips; build clean. Step 4.4 manages favorites only from Home, with matching stars in Recents/Frequent/Favorites and alphabetical Favorites. Native WinUI cards use responsive columns, standard spacing, empty states and Show more (25 entries per section initially). Local data appears before progressive checks. Home entry opening, Add folders and drops use the existing root loader. Locate/Remove entry are wired with clear errors/cancellation and duplicate-input guards. Verification: 167 unit/filesystem tests, 99 native checks, zero skips; native capture inspected. Physical input/DPI/picker/drop and visual acceptance remain open.

## Phase 5 — Alternative viewing modes

Add a virtualized gallery with mode-local multi-selection and grouped operations, then a continuous image reader with lazy loading, bounded preloading, resource release, and synchronized folder/file context.

## Phase 6 — Refinement and optional scope

Add search/filtering, drag and drop, expanded metadata, duplicate detection, additional handlers, themes, and performance/usability refinements as separately reviewable work.

## Working rules

- Keep WPF as the host. Use WinUI controls only where their built-in Windows behavior materially helps; the navigation rail is hosted in an XAML Island.
- Keep the app navigation rail permanently compact and icon-only. Full hiding remains programmatically available for future fullscreen mode.
- Build after changes. Maintain automated coverage for business logic and filesystem operations in `MediFiler3.Infrastructure.Tests` as features are implemented.
- Update [PROJECT_STATUS.md](PROJECT_STATUS.md) when a step changes status, and keep architecture/specification decisions synchronized.

Home follow-up: compact relative-time captions (seconds/minutes/hours/days/30-day months/365-day years), exact time/full path on hover, no Ignore, and unified Remove entry across all lists. Viewport-bound left-aligned content fixes wide-window card positioning; native containment checked through 3000 px. Latest verification: 188 unit/filesystem tests and 99 native checks, zero skips; build clean.

Root-history follow-up: independent Undo/Redo scopes keyed by root filesystem identity; source-root ownership for cross-root file moves, target-root ownership for folder-menu actions, scoped redo invalidation/batches/compensation and stale-context cancellation. Schema 5 migration preserves legacy history as unscoped; active-root UI excludes legacy entries instead of inferring ownership. Verified 195 unit/filesystem tests and 101 native checks, zero skips; clean build in temporary output while the user app runs.

Collision workflow follow-up: user-resolved Rename/Overwrite/Cancel for move, rename, folder creation and history collisions. Overwrite preserves whole destination items in dedicated app-managed backup storage; one root-scoped Undo reverses the incoming operation and restores the old version. Folder overwrite is whole-folder replacement, not merging. History replay renames/preserves the blocking existing item and restores it with the opposite action. Schema 6 guards these semantics against older builds while preserving existing records. Verification: 241 unit/filesystem tests and 116 native checks, zero skips; clean isolated build. Manual native dialog keyboard/DPI/long-path review remains.

Backup management follow-up complete: dedicated app-managed same-volume storage, Settings cleanup confirmation and outcome reporting, durable retirement of dependent root/batch Undo/Redo before deletion, conservative legacy/recovery/identity handling and serialized mutations/cleanup. Schema 7 preserves existing records. Verified 241 unit/filesystem tests and 116 native checks; physical Settings/other-volume review remains.

ContentDialog follow-up complete: all app-owned prompts and available application warnings use WinUI ContentDialog through a shared owned XAML Island host. Actions/results remain unchanged; Windows pickers/choosers and emergency WinUI-unavailable error reporting retain their platform behavior. Native checks cover text submit/cancel, close/Escape, warnings, collisions and cleanup. Current verification: 241 unit/filesystem tests and 116 native checks; clean build.

Step 4.5 implemented: logical normal bounds, monitor metadata/offsets and last normal/maximized state; clamp/fallback for current work areas and DPI, runtime reconciliation, deferred adjustment during move/resize, coalesced writes and capture/flush before shutdown. Existing WindowLayout JSON/table is reused with additive optional metadata; schema stays 7. Verified 258 unit/filesystem tests and 122 native checks. Physical monitor/DPI/taskbar review remains; 4.6 is next.
