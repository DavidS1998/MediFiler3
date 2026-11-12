# Project status

Updated 2026-11-12. **The approved Gallery MVP (5.1-5.5) and Continuous Reader MVP (slices 1-6) are complete with recorded limits.** [Reader acceptance](READER_ACCEPTANCE.md) records fresh regression, two source-image scale samples and the user-confirmed workflow/arrow fix; [Gallery acceptance](PHASE5_ACCEPTANCE.md) preserves its separate evidence and limits.

## Continuous Reader slices 1-6: closed

The user approved the proposed MVP defaults. Shared Reader navigation/projection and a native virtualized continuous image surface are implemented; see [READER_HANDOVER.md](READER_HANDOVER.md) for behavior, resource bounds, code map and continuation safeguards. The top-right Reader toolbar now offers Switch/Sort glyph buttons for Resize to width / Resize to height, with a persisted 10-100% slider in width mode (default 100%). Settings retains page spacing (0-64 px, step 4), upcoming preload (0-6) and counter visibility; defaults remain fit width / 100% / spacing 0 / preload 2 / counter on. Reader counter shares Gallery's bottom-right outlined white semibold style. The sidebar uses ReadingMode and separates Home from file views; F3 toggles the existing tree pin action in Browse/Gallery/Reader across control focus. Reader slice 4 adds previous/next image, owned numeric page jumps, view-scoped keyboard navigation and accessible retry recovery. Reader temporarily hides Rename/Delete/Undo/Redo, both external-application buttons and Open current Reader image in Browse; their Reader-only shortcuts are dormant/commented out. The underlying APIs and failed-page recovery actions remain. Both external-application buttons and their separator are also hidden in Gallery; Browse restores them, and Gallery file-operation controls remain visible. Reader slice 5 enables single-page rename/recycle/tree-destination move and root-scoped Undo/Redo, with operation-aware anchors, retained unaffected pixels and shared live-change reconciliation. Slice 6 closes acceptance with the measurements and limits in [READER_ACCEPTANCE.md](READER_ACCEPTANCE.md). The Gallery closeout below remains its preceding, separate baseline.

Reader arrow keys now route through native PreviewKeyDown before ScrollViewer/slider/sidebar defaults, retaining the shared window guards and avoiding restoration cancellation after handled input. Actual HWND key-message coverage supplements the earlier direct-router tests; the user confirms physical arrow navigation now works.

Explicit left-click on the active folder row reloads only that folder listing and starts at its first file (first image in Reader), clearing Gallery selection and retaining tree nodes/counters. Menu/expander gestures and internal refresh/selection notifications do not request that reset.

Reader Go uses a single focused page-number field, limited to 1 through the current image-page count, with Go and Cancel. The previous list/grid designs are superseded. Dialog hosts clip their outer native window to the rounded silhouette. Folder trees recycle visible containers with pixel scrolling; fullscreen no longer lays out the whole expanded hierarchy. Folder menus offer Collapse child folders, which toggles only direct children and retains deeper expansion state. Browse/Gallery search is explicitly deferred to a [future phase](DEVELOPMENT_PLAN.md), not the next active slice. The Browse F4 crash is fixed by assigning a routed event to forwarded key arguments; [crash evidence](verification/f4-crash-2026-11-12.txt) records the cause.

## Reader verification (2026-11-12)

- Build: zero warnings/errors.
- Full unit/filesystem suite: **362 passed**, zero failures/skips.
- Full native suite with supplied codec/media fixtures: **221 passed**, zero failures/skips, including all **27 Reader-runner checks**. Earlier slice 6 focused Reader repeat: **27 passed**, zero failures/skips.
- Latest full native evidence: `%TEMP%\MediFiler3Integration-516e430d8ead4687b2b1da1540fd26bb`; log `%TEMP%\MediFiler3Reader-hidden-commands-full.log`. Earlier closeout repeat: `%TEMP%\MediFiler3Integration-caf7ba762b904f259f8f8e627c8f6cb8`; log `%TEMP%\MediFiler3Reader-slice6-focused.log`.
- Actual-file scale: 1,000 generated 1024x1536 PNGs; 168-208 ms listing/Reader entry, 109-110 ms warm return, at most four sampled rows/two decode jobs and 68.91 MiB sampled decoded cache. Zero source requests during 150 ms inactive observations; current pixels reused. These are local generated-corpus samples, not user-library/FPS/process-memory guarantees. [Methodology, raw samples and limits](READER_ACCEPTANCE.md).
- Source hash audit: all **143 production files unchanged** during slice 6; closeout adds tests and docs only.
- The preceding tree follow-up measured a 10,102-node synthetic expanded tree that realized 22 containers initially and 102 after distant pixel scrolling. Fullscreen entry/reveal/layout measured 92.66 ms and exit/layout 28.15 ms in this sample. Root/node identity, expansion state, counters and shallow collapse semantics passed. See [sample](verification/tree-fullscreen-2026-11-12.json); this is a structural/performance probe, not physical/real-library acceptance.
- Native corner checks verify the outer window region excludes rounded corners and follows resizing. Existing modal, cancel, rename, collision, Gallery preview/drag/hover/edge/wheel checks pass.
- Reader image-worker crash follow-up replaces the prematurely closed SoftwareBitmap handoff with XAML-owned pixels, removes image tooltips and preserves anchors during asynchronous native scroll restoration. See [investigation evidence](verification/reader-crash-2026-11-11.txt).
- User reports the Reader crash appears fixed. Slice 3 tests cover persisted settings, anchored reflow, bounded preload, counter styling/visibility and unchanged image identity.
- The user reports Reader otherwise in order and confirms the arrow fix. Dedicated Narrator, exhaustive physical input/touchpad, mixed-DPI/fullscreen, diverse user-library throughput/long-session memory and storage/deployment matrices remain carried limits. This closes the MVP rather than claiming those matrices tested; see [Reader acceptance](READER_ACCEPTANCE.md).

## Current implementation

- C#/WPF host with WinUI XAML Islands, .NET 10/Windows SDK 10.0.19041.0, existing Windows App SDK dependency and SQLite schema 7. Viewer/GIF/media/text, Home/settings/layout, root history/favorites and safe operations remain intact.
- Browse/Gallery/Reader share root/folder/current-file context with lazy presentation, retained virtualized Gallery and bounded cancellable thumbnails. Home/Settings retain Gallery selection/Reader anchors and stop watchers; healthy return reconciles metadata without flushing unchanged previews or counting root visits.
- Natural filename order, independent persisted Width/Height (100-400, step 50, default 150; 150px slider widths), centered two-pixel gaps, gradient captions, square-first crop/hover-fit, solid five-pixel #FF1493 highlights. Raw shared Windows-default PNGs; no manual blur. Fresh-source fallback and persistent invalidation markers remain.
- Native Extended Ctrl/Shift selection, Alt-hover paint, view-scoped shortcuts independent of control focus, accessible selection status and Browse handoff. Mode/folder/root changes clear mode-local selection; Home/Settings and refresh preserve surviving selections/explicit clear.
- Gallery grouped moves/recycle/history, single-selected-file rename, single-file recycle without confirmation and multi-file confirmation. Partial/cancelled/unattempted results stay explicit. Bulk rename is not planned.
- Gallery drag snapshots one unselected origin or the full selected set. Clicks remain native; movement must cross the Windows drag threshold. The cropped preview is 80% opaque with a top-left pointer hotspot and up to three realized tiles. The tree reveals, highlights destinations, expands on 600ms hover and supports edge/wheel scrolling. A foreground/tree-scoped native wheel hook exists only during the active drag and is removed on completion/cancel/context changes.
- Operation/history refresh retains unchanged tree nodes/expansion and updates both move counters. Browse Undo selects restored files; Redo chooses the next file or previous neighbor at the end. Verified structural move echoes can retain warm previews; strong content/F5/overflow/recovery invalidation remains.

## Completed scope

| Slice | State |
|---|---|
| Phases 1-3 | Implemented; prior Phase 3 manual acceptance remains recorded |
| Phase 4 | Implemented/automated acceptance complete; recorded physical limits remain |
| 5.1-5.4 | Gallery/session/UI/selection/safe grouped operations implemented |
| Approved Gallery drag/drop follow-ups | Implemented; nested-folder, edge and wheel behavior physically confirmed |
| 5.5 | Regression, scale characterization and targeted acceptance complete with recorded limits |
| Reader slices 1-6 | MVP implemented and accepted with recorded limits; fresh regression, two actual-file resource samples and user-confirmed arrow/workflow acceptance |
| Later scope | Additional sorts/filtering and external file drag/import/export; scope separately |

## Phase 5.5 closeout verification (preceding baseline)

- Build: zero warnings/errors.
- Unit/filesystem: **327 passed**, zero failures/skips.
- Full native runner with supplied codec fixtures: **192 passed**, zero failures/skips.
- Focused Gallery scale repeat: **31 passed**, zero failures/skips.
- Scale: 10,000 records retained 28 initially realized tiles and 60 after distant scroll; four atomic resets with no individual index notifications; zero inactive requests in the observation window. Clearing the full selection measured **0.76-1.03 seconds**, a known large-selection latency. See [measurements and limits](PHASE5_ACCEPTANCE.md).
- Diff formatting passes. Production hash audit: all 126 source/project files unchanged from the clean pre-closeout baseline.

Full evidence: `%TEMP%\MediFiler3Integration-be01e8c8971f487baad7928f343c6463`; full log `%TEMP%\MediFiler3Phase55-native.log`. Repeat evidence: `%TEMP%\MediFiler3Integration-3cc8af46851a4c6993aec8b79ff76997`. Raw scale summaries are retained under [verification](verification/phase5-scale-full.json).

Run from the solution root, using the same temporary output for build and both runners:

```powershell
dotnet build MediFiler3.sln --no-restore --verbosity minimal "-p:BaseOutputPath=$env:TEMP\MediFiler3ContinuationBuild\"
dotnet test tests/MediFiler3.Infrastructure.Tests --no-build --no-restore --verbosity minimal "-p:BaseOutputPath=$env:TEMP\MediFiler3ContinuationBuild\"
dotnet run --project tests/MediFiler3.App.Integration --no-build --no-restore "-p:BaseOutputPath=$env:TEMP\MediFiler3ContinuationBuild\" -- "$env:TEMP\MediFiler3Phase3Fixtures"
git diff --check
```

The runners may execute independently after the build. Never build concurrently against shared obj directories. Temporary output preserves the running app; it does not replace its binary. Full codec acceptance requires supplied fixtures and zero skips; [runner instructions](../tests/MediFiler3.App.Integration/README.md) retain exact coverage.

## Safeguards and limits

Never silently overwrite or permanently delete media. Preserve identity/root-scoped journals, explicit preserving collisions/backups, same-volume managed storage, compensation/recovery evidence, retirement-before-backup-delete, command/context/cancellation validation and paused media reopening. Cache and backup/source/history ownership remain separate.

Keep the retained native collection and deferred visible/loaded diffs, atomic disjoint resets/incremental survivors, HWND clipping and obsolete-reveal cancellation. Default thumbnail cache keys stay separate from explicit-size entries; F5 exercises fresh-source fallback rather than normal Windows-default retrieval. Do not incidentally change crop geometry, freshness markers or DPI cache semantics.

Open limits: mixed-DPI/fullscreen/physical input/Narrator, real-library performance, native crash history, codec/environment/storage/deployment cases and bulk-clear latency. The [Reader](READER_ACCEPTANCE.md) and [Gallery](PHASE5_ACCEPTANCE.md) acceptance reports state what was checked and what remains unverified; passing automation does not remove historical native faults.

## Git and continuation

Reader closeout started on `master` at `b1cd13b` (`Reader mode file operations`) with the uncommitted arrow fix, its tests and documentation present. Preserve those changes. Slice 6 adds tests/docs only; all 143 production files match the source hashes captured at its start. The earlier Gallery baseline `ae272bd` and its counts remain historical in its acceptance report. No fetch, commit/push, deployment or running-app restart was performed.

Read [PHASE5_HANDOVER.md](PHASE5_HANDOVER.md) before continuation. Read [READER_HANDOVER.md](READER_HANDOVER.md) and [READER_ACCEPTANCE.md](READER_ACCEPTANCE.md) for current Reader behavior and closeout. No Reader implementation slice remains. The complete preceding Reader notes are preserved in [Reader handover history](READER_HANDOVER_HISTORY.md). Preserve [Phase 5 status history](PROJECT_STATUS_PHASE5_HISTORY.md), [earlier project history](PROJECT_STATUS_HISTORY.md), [implementation handover history](PHASE5_HANDOVER_HISTORY.md) and prior acceptance evidence; their superseded next-step instructions are historical.

Historical F3-only milestone (superseded by the full current runs): clean build and all eleven focused Reader/native checks passed, zero failures/skips. The old Ctrl+T binding is rejected. Full suites were not repeated for this key-binding-only change; the full-suite evidence above predates it. Log: `%TEMP%\MediFiler3Reader-F3-focused.log`.
