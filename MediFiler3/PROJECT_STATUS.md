# Project status

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
