# Project status

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


Updated 2026-11-11 for continuation. **This handover changes documentation only.** Current implementation includes Phase 5.1-5.3 and subsequent Gallery/thumbnail refinements; Phase 5.4 is not implemented. Read [PHASE5_HANDOVER.md](PHASE5_HANDOVER.md) for the code map and detailed caveats. The previous running status log is preserved in [PROJECT_STATUS_HISTORY.md](PROJECT_STATUS_HISTORY.md); its superseded experiments and next-step instructions are historical.

## Current implementation

- WPF host with WinUI XAML Islands, .NET 10/Windows SDK 10.0.19041.0, Windows App SDK 2.6.4. Existing viewer/GIF/media/text, Home, settings, window layout, root history/favorites, safe operations and SQLite schema-7 persistence remain intact.
- Browse and Gallery are separate sidebar destinations sharing roots/folder/current file. Expensive presentation is applied when requested; switching modes does not rescan roots or count another root visit. Home/Settings stop watchers; healthy return reconciles metadata without invalidating unchanged previews.
- Gallery uses a retained virtualized native grid, centered rows, two-pixel tile gaps, gradient filename captions and hover-to-fit/hide-caption. Current crop policy remains carousel-style center-square framing, clipped into the requested rectangle.
- Width and Height sliders are each 150 logical pixels wide; values independently persist from 100 to 400 in steps of 50, default 150. These are display sizes, not thumbnail source-resolution requests.
- Native Extended selection supports Ctrl-click, Shift ranges, keyboard navigation, Ctrl+A, Escape and focused-file Enter/double-click Browse handoff. **Alt-hover** paints selection: the first valid file chooses add/remove, with one visit per path. Ctrl-hover does not paint. Mode changes clear selection; Home/Settings retain it; refresh prunes removed paths and preserves surviving current records.
- Gallery selected tiles and the carousel current item have a **solid five-pixel bright-pink #FF1493 border**. No dash pattern, OS accent binding or inner stroke remains. Carousel normal/current/fading items have square corners. The Gallery selected counter has a black text outline and 24-pixel right clearance.
- **No manual blur remains.** Gallery and carousel share Windows-default-size PNG previews using `GetDefaultThumbnailPngAsync`; normal generation calls `GetThumbnailAsync(ThumbnailMode.SingleItem)` with no explicit size/options and preserves the returned dimensions. Carousel uses raw frozen PNG decoding and existing WPF HighQuality display scaling. Gallery loads that same PNG directly into native BitmapImage.
- Default previews have a separate private `0x0` cache-key bucket; old fixed-size cache entries are retained. Initial requests can regenerate once; warm visits reuse the shared cache. Default results retain creation-time dimensions, including across later DPI changes.
- Known invalidated images still bypass stale Windows thumbnail pixels via fresh WinRT source decoding, bounded at 256px for default requests. Explicit-size callers retain their previous sizing/clamping contract. F5/freshness markers, cancellation, per-key coalescing and ownership safeguards remain.
- Pinned-tree visibility/clipping, selected-file reveal after Home/Settings, deferred/coalesced native list updates and cancellation of obsolete positioning are implemented. Wholly disjoint/empty native listings publish one atomic Reset on the retained collection; shared-path changes stay incremental.
- Gallery file rename/delete/tree-move and Undo/Redo commands remain disabled/guarded pending grouped-operation integration. Existing folder-management ellipsis commands retain their prior behavior.

## Completed and remaining scope

| Slice | State |
|---|---|
| Phase 1-3 | Implemented; earlier Phase 3 manual acceptance remains recorded |
| Phase 4 | Implemented and automated acceptance complete; user tentatively accepted, physical limits remain in PHASE4_ACCEPTANCE.md |
| 5.1 | Shared viewing-mode/session/selection foundation complete |
| 5.2 | Gallery UI, virtualized thumbnails, independent persisted dimensions and subsequent resume/UX fixes complete |
| 5.3 | Selection/input/accessibility and safe command-scope presentation complete, with later Alt-hover refinements |
| 5.4 | Next: existing grouped moves plus journal-backed grouped Recycle Bin integration, collisions/partial outcomes/Undo-Redo validation |
| 5.5 | Remaining Gallery integration/performance and targeted physical acceptance |
| Later | Continuous reader, drag/drop and additional sorting/filtering; natural filename ordering remains the MVP |

The latest Windows-default/no-blur appearance has **not** received physical user acceptance. The user's earlier "Great" acknowledged the preceding border refinement, not full Phase 5 acceptance.

## Git and workspace

Branch `master`; HEAD `0880e94` (`Gallery view selection initial`). Earlier commits: `3735cf9` (Gallery bug fixes), `f6986e1` (Gallery view initial), `aa68870` (Fullscreen support). Source/test refinements after HEAD are **uncommitted** and must be preserved. No commit/push was made by this handover; remote divergence was not verified.

Important untracked implementation files are `src/MediFiler3.Application/Navigation/GalleryHoverSelection.cs`, `tests/MediFiler3.App.Integration/GalleryHoverSelectionTests.cs` and `tests/MediFiler3.App.Integration/ThumbnailRenderingChecks.cs`. Include them when reviewing/checkpointing the working tree. `SoftThumbnailDecoder.cs` and `SoftThumbnailChecks.cs` were temporary experiments and are removed. Do not restore them from historical descriptions.

## Fresh handover verification

On 2026-11-11, against the current working tree:

- Solution build: **zero warnings/errors**, using temporary output to preserve the user's running app.
- Unit/filesystem suite: **316 passed, zero failures/skips**.
- Full explicit native runner with supplied codec fixtures: **181 passed, zero failures/skips**, including Gallery/input/thumbnail checks, Home/Settings resume/pinned-tree checks, large-list reset checks, safe-operation regressions and three fresh-process restart workers.
- The full run includes the 31-check Gallery subset and 9-check Gallery resume subset; they were not separately repeated for this documentation handover.
- Final native evidence: `%TEMP%\MediFiler3Integration-6e42b77c512642eabed5c157de2a51cb`.
- Diff formatting passes; Git may print LF-to-CRLF normalization notices for existing working files. Those are not compiler warnings or whitespace-check failures.
- Documentation handover audit: hashes of all 164 implementation/project files are unchanged; local links in the nine updated/new Markdown files resolve. Only documentation was edited in this final turn.

Run from the solution root, building once before `--no-build` checks. Never build concurrently against shared `obj` directories.

```powershell
dotnet build MediFiler3.sln --no-restore --verbosity minimal "-p:BaseOutputPath=$env:TEMP\MediFiler3ContinuationBuild\"
dotnet test tests/MediFiler3.Infrastructure.Tests --no-build --no-restore --verbosity minimal "-p:BaseOutputPath=$env:TEMP\MediFiler3ContinuationBuild\"
dotnet run --project tests/MediFiler3.App.Integration --no-build --no-restore "-p:BaseOutputPath=$env:TEMP\MediFiler3ContinuationBuild\" -- "$env:TEMP\MediFiler3Phase3Fixtures"
git diff --check
```

The two runners may run independently after the build. Focused options are `--gallery-only` and `--gallery-resume-only`, using generated fixtures. Full codec acceptance requires the supplied fixture folder and zero skips; see [runner instructions](../tests/MediFiler3.App.Integration/README.md). Temporary files/fixtures are local and may disappear on another machine or after cleanup. Building temporary output does not replace an already-running Rider/app binary.

## Known issues and verification limits

1. **Native collection crash history:** earlier runs intermittently hit `0xC0000005` inside WinRT ObservableCollection notifications. Layout deferral alone did not eliminate it. Latest disjoint-list batching addresses the reproduced path; repeated 80/10,000-file returns pass. This is not proof that all native crash scenarios are eliminated. Preserve exact traces/context if it recurs.
2. **Timing-sensitive automation:** the 110ms carousel fade assertion has occasionally failed, then passed unchanged on rerun. The fresh handover run passed on its first attempt. ShellIpcClient cancellation diagnostics have also appeared in earlier successful runs; their real-library impact is not measured.
3. **Physical checks remain:** Alt/Control/Shift key and pointer travel, focus/access-key behavior, Narrator announcements, subjective thumbnail quality/loading speed, live mixed-DPI/monitor changes, fullscreen/popup reachability, actual Windows launching/pickers, network/removable roots and permission-denied backup storage. Keep the remaining [Phase 4 acceptance limits](PHASE4_ACCEPTANCE.md) and [codec limits](PHASE3_INTEGRATION.md).
4. **Default-size exception:** F5/known invalidation uses the fresh-source fallback, and its requirement persists across restart/cache clearing. F5 is a correctness test, not a test of normal Windows thumbnail retrieval. Use new/uninvalidated runner-owned files to compare the normal provider.
5. **Crop/size decision remains:** Windows-default sources may be smaller than a large tile or its high-DPI display area; the current square-first crop can magnify rectangular-tile content. V2-style direct rectangular fill was investigated but not adopted. Do not change crop geometry incidentally.
6. No physical acceptance, installer/deployment check, performance benchmark or new grouped-operation implementation was performed during this handover.

## Immediate next tasks

1. Read this snapshot, AGENTS.md, SPECIFICATION.md, ARCHITECTURE.md, DEVELOPMENT_PLAN.md and PHASE5_HANDOVER.md, plus the Phase 4 safety/acceptance references; inspect current Git state before changes.
2. Review the user's feedback on Windows-default/no-blur thumbnails first. If a concrete regression is reported, reproduce it with identical files/tile sizes/DPI and preserve the current cache/freshness/native-lifetime safeguards.
3. If moving forward, propose the concrete 5.4 command/partial-result/cancellation/Undo behavior before coding. Reuse `MoveFilesAsync`; inspect the existing journal and grouped-history semantics before designing grouped Recycle Bin support. Keep current Gallery mutation guards until the safe integration is ready.
4. Keep 5.5 physical/performance limits open and continuous reader/additional sorts deferred. Checkpointing implementation may be useful, but this handover does not authorize a commit or push.
