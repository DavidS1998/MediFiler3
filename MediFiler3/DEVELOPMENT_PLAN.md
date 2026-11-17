# Development plan

Long-term milestone tracker, updated 2026-11-15. Requirements and current product decisions are in [SPECIFICATION.md](SPECIFICATION.md). Active issues/next actions belong in [PROJECT_STATUS.md](PROJECT_STATUS.md); implementation belongs in [ARCHITECTURE.md](ARCHITECTURE.md). A completed MVP with recorded verification limits does not imply every hardware/storage/accessibility scenario passed.

## 1. Milestones

| Milestone | Delivered scope | State / acceptance |
|---|---|---|
| WinUI title and navigation updates (2026-11-17) | AppWindowTitleBar colors/extended native title content, native Reader PagerControl on approved experimental SDK, combined Browse zoom AppBarButton, folder-menu icons and text clearance | Implemented; clean build, 411 unit/filesystem and 234 full native checks passed, zero failures/skips. Physical user review pending. Breadcrumbs accepted by user. |
| Phase 1: foundation | WPF shell/WinUI islands, separate roots/progressive tree/listing, natural order, centralized current file, images, interaction policy and carousel | Complete |
| Phase 2: safe organization | File/folder move/rename/create/recycle, durable intents/events, identity checks, grouped compensating Undo/Redo and recovery | Complete; safety remains a permanent requirement |
| Phase 3: content | Native audio/video, windowed Unicode text, GIF animation, metadata, external fallback, cross-content release/cancellation | Complete; user accepted physical viewer/input/audio/external/Browse-operation review on 2026-11-10, for representative formats |
| Phase 4.1-4.3: persisted state | Schema-7 typed settings/layout/root/cache state, root-only visits/favorites, manual Locate/Remove, root-scoped history and preserving collisions/backups | Implemented |
| Phase 4.4-4.6: application integration | Native Home, monitor-aware layout, settings restoration/flush, root watchers, thumbnail freshness/maintenance, explicit backup cleanup | Implemented; user tentatively accepted overall, dialog appearance and fixed label flicker explicitly confirmed |
| Phase 4.7: restart/acceptance | Three-process persisted settings/layout/history/backup/cache/watcher scenarios and full regression | Automated acceptance complete; dedicated physical display/storage/chooser matrices remain limits |
| Gallery 5.1-5.3 | Shared mode/selection foundation, retained virtualized Gallery, independent stepped dimensions, native selection/input/accessibility and focus-independent shortcuts | Complete |
| Gallery 5.4 | Grouped moves/recycle/root history, partial/cancel outcomes, single rename, incremental counters/tree/pixels, Browse Undo/Redo current-file fixes | Complete; bulk rename excluded |
| Gallery drag extension | Threshold-safe click/drag, selected/unselected snapshot scope, cropped translucent pointer preview, tree destinations, hover expansion and edge/native-wheel scrolling | Complete; user confirmed nested destinations, edge and wheel scrolling during drag |
| Gallery 5.5 | Regression, two scale samples and targeted acceptance | Closed with recorded limits on 2026-11-11 |
| Reader slices 1-2 | Shared image projection/anchors, lazy native source pages, bounded cache/jobs and recoverable placeholders | Complete; owned-pixel lifetime fix and source crash regression included |
| Reader slice 3 | Persisted fit width/height, width percentage, spacing/preload/counter, shared counter style | Complete |
| Reader slice 4 | Numeric page navigation, scoped keyboard controls, accessible retry/Browse recovery | Complete; earlier list/grid/filename picker designs superseded by bounded numeric field |
| Reader slice 5 | Originally delivered single-page organization/history, mapped anchors and live reconciliation | File mutation/history subsequently disabled by the read-only Reader decision; folder mapping and reconciliation retained |
| Reader slice 6 | Fresh full regression, two actual-file resource samples and user acceptance | Closed with limits on 2026-11-12; user confirmed arrow fix after reporting rest of Reader in order |
| Subsequent UI decisions | Reader hides seven command buttons/matching shortcuts; Gallery hides external-app pair; Browse restores shared controls; explicit active-folder click resets listing position; shallow folder-child collapse; one sidebar/full-tree-width reveal area in normal/fullscreen modes | Implemented; these are current requirements, not unfinished Reader slices |
| Documentation consolidation | Six authoritative documents with no phase/history/runner-readme duplicates; preserve current AGENTS | Complete, 2026-11-13; docs-only build/audits passed |
| Reader restoration follow-up | Preserve accepted native fractional scrolls through late dimension discovery; deterministic gated-source native regression | Complete, 2026-11-14; original assertion failures reproduced before fix; existing input/anchor/rendering assertions preserved |
| Folder collision totals | Recursive file counts and summed file sizes on Incoming/Existing cards, cancellable background scanning and explicit incomplete states | Implemented, 2026-11-14; includes hidden/system files, excludes reparse traversal |
| Folder text depth shading | White roots/first-level folders, bounded depth palette, live persisted native Settings toggle and independent color policy | Implemented, 2026-11-15; palette strengthened to 128 at depth 5 with even interpolation; 373 unit and 33 focused checks passed on the stronger palette. Depth palette editing remains future scope |
| Folder-name rules | Ordered literal name matching, RGB and expansion actions, native draggable priorities/editor, async cached projections and manual-state protection | Implemented, 2026-11-15; extended by count rules below |
| Scrollable tree footer / Gallery reveal | Full-height tree viewport with scrollable 175px/48px footer; Gallery hidden tree opens only from sidebar and retains over shown tree | Implemented, 2026-11-15; clean build, 43 focused and 233 full native checks passed |
| Carousel full-area reveal | One full shown-panel hover rectangle for opening/retaining, WPF/native probes and fullscreen; narrow bottom target removed | Implemented, 2026-11-15; clean build and 43 focused native checks passed |
| Browse endpoint shortcuts | Home selects first file and End last file in current folder through WPF/native forwarded routes, with no boundary crossing | Implemented, 2026-11-15; clean build, 42 focused native checks passed |
| File name/color rules | Persisted ordered filename rules, native Files editor with live saved previews, Browse native caption backgrounds and realized Gallery filename gradients; Reader excluded | Implemented, 2026-11-15; 411 unit, 41 focused and 231 full native checks passed; zero failures/skips |
| Rule action precedence / immediate application | Per-action priority, Leave expansion unchanged fallthrough/no-op, explicit saves reapply existing tree state, compact validation/Save spacing | Implemented, 2026-11-15; 404 unit, 39 focused and 229 full native checks passed; zero failures/skips |
| Structure actions / saved-rule previews | Root-excluded expansion/Hide, disabled All folders collapse default, committed-vs-preview isolation for every rule and scrollbar clearance | Implemented, 2026-11-15; 401 unit, 38 focused and 228 full native checks passed; zero failures/skips |
| Rule drafts / tick alignment | Accent selected border, selected live Unsaved placeholders isolated from matching/persistence, zero-origin carousel ticks and self-describing aligned expansion control | Implemented, 2026-11-15; clean build and 227 full native checks passed |
| Settings compact layout | Compact 480px rules list, paired editor fields, condition radios, integrated color-action header, carousel tick snapping and tick-free reader spacing | Implemented, 2026-11-15; clean build and 37 focused native checks passed |
| Count rules / Settings cards | Mixed name/count priorities, inclusive ranges, editable empty-folder default, inline enable/color/caret previews and four Settings groups | Implemented, 2026-11-15; direct counts, unknown-count safety and persisted removal covered |

Reader **slice 6** is its acceptance slice, not completion/authorization of the proposed **project Phase 6** optional refinements. No Reader implementation slice remains.


## 2. Verification and user acceptance evidence

These are milestone baselines, not an accumulating list of every intermediate run. Counts are evidence of those builds, not a promise of current/future totals. Current verification gaps and regression concerns are in PROJECT_STATUS; commands/fixture interpretation are in ARCHITECTURE.

| Baseline | Build | Unit/filesystem | Full native | Focused / interpretation |
|---|---|---:|---:|---|
| Phase 3 integration | Clean | 141 | 81 | User accepted representative physical checks; codec variants remain conditional |
| Phase 4 final baseline | Clean | 276 | 144 | Restart workers included; broader physical scenarios not inferred |
| Gallery 5.5 closeout | Clean | 327 | 192 | Gallery repeat 31; zero failures/skips |
| Reader 6 closeout | Clean | 362 | 221 | Reader repeat 27; zero failures/skips; all 143 production files unchanged during that test/docs-only closeout |
| Reader hidden-command follow-up | Clean | 362 | 221 | All 27 Reader checks included; zero failures/skips |
| Gallery external-button follow-up | Clean | Not repeated | Not repeated | Focused repeat 27 passed; first attempt had the known Reader width-reflow timing failure, unchanged-code repeat passed |
| Scrollable tree footer / Gallery reveal (2026-11-15) | Clean | Not repeated (App/UI-only) | 233 | Focused UI/Gallery 43; full-height viewport, real scroll-to-end clearance, sidebar-only hidden Gallery reveal, 10,102-node recycling and drag/Reader regressions; zero failures/skips |
| Filename/color rules (2026-11-15) | Clean | 411 | 231 | Focused UI/Gallery 41; native editor/live previews/priority/SQLite, accepted DWM caption RGB, Gallery gradients, Reader/toolbar exclusions and retained folder/Reader checks; zero failures/skips |

The user confirmed Phase 3 interactive review, Phase 4 dialog appearance/root-label fix, Gallery nested/edge/wheel drag behavior, Reader crash appearing fixed and Reader arrow keys working. Reader closeout follows their report that other Reader behavior seemed in order. None of this supplies an unspecified DPI/Narrator/network/codec matrix.

Useful retained run identifiers (temporary directories may disappear):

| Evidence | Location |
|---|---|
| Reader closeout full | `%TEMP%\MediFiler3Integration-4605bff9714048a481b75f22331c1143`; `MediFiler3Reader-slice6-full.log` |
| Reader closeout focused | `%TEMP%\MediFiler3Integration-caf7ba762b904f259f8f8e627c8f6cb8`; `MediFiler3Reader-slice6-focused.log` |
| Hidden Reader commands full | `%TEMP%\MediFiler3Integration-516e430d8ead4687b2b1da1540fd26bb`; `MediFiler3Reader-hidden-commands-full.log` |
| Gallery visibility repeat | `%TEMP%\MediFiler3Integration-bdb55a3705f844beada871a1c15dd110`; `MediFiler3Gallery-hidden-external-focused-repeat.log` |
| Current full | %TEMP%\MediFiler3Integration-6b04413995174d9da49882b0d2b0ca15; MediFiler3TreeFooter-full.log |
| Tree reveal UI/Gallery focused | `%TEMP%\MediFiler3Integration-21c5b81453834420ae483e9eda39467d`; `MediFiler3TreeReveal-focused.log` |

## 3. Scale characterization retained from acceptance

### Reader actual-file scenario

1,000 independent PNG files copied from a deterministic 1024x1536 raster, 47,893,000 bytes total. Real folder listing and Windows source decoding, default Reader options; visit indexes 0, 125, 500, 999, 250, 0, 500, then Home/return. Fixture creation is outside entry timing; OS/filesystem caches are not forced cold. Generated identical content is reproducible, not a diverse user library.

| Observation | Full suite sample | Focused repeat |
|---|---:|---:|
| Listing plus Reader entry ready | 208.0 ms | 167.9 ms |
| Six subsequent jumps, min-max | 36.3-176.8 ms | 31.8-69.9 ms |
| Warm Home return ready | 110.1 ms | 108.7 ms |
| Maximum sampled realized rows | 4 | 4 |
| Maximum sampled decoded cache | 68.91 MiB | 68.91 MiB |
| Maximum concurrent source decodes in Reader run | 2 | 2 |
| Scenario source-decode calls | 18 | 19 |
| Requests during 150ms inactive observation | 0 | 0 |
| Current source bitmap reused | Yes | Yes |
| Whole-process sampled working set | 617.5-697.6 MiB | 441.0-558.7 MiB |
| Whole-process sampled private memory | 506.6-587.8 MiB | 360.8-479.3 MiB |

Ready means selected source assigned and native restoration/Reader queue settled, not a compositor frame. Polling granularity is 25ms; times include validation/reattachment work, not just decode. Memory includes the whole hidden integration process, preceding tests and fixture construction. Samples aren't continuous peaks, isolated Reader allocations, FPS/percentiles, cold-I/O or total-memory guarantees. The existing 10,000-missing-path Reader cases measure lazy structure/jump/cancellation, not actual-image throughput. ReaderAcceptanceChecks can regenerate raw `reader-acceptance.json` artifacts.

### Gallery and tree structural scenarios

| Scenario | Retained observations | Limit |
|---|---|---|
| Gallery 10,000 generated records, two runs | Publish/initial realization 86-108ms; Select All 91-189ms; Clear 764-1,034ms; 28 initial tiles, 60 after distant scroll; zero requests in 100ms inactive; four disjoint Resets/no individual notifications, retained ItemsSource | Missing/generated paths, not real-thumbnail throughput. User accepted the roughly one-second clear on 2026-11-14; optimization is unnecessary. Two return rounds 2.785-3.654s include fixed waits/validation. |
| Gallery cache sample points | 58-70 decoded entries, 475,136-573,440 bytes | Sample values, not worst-case/process memory |
| Expanded tree 10,102 nodes | 22 initial controls, 102 after distant scroll; fullscreen entry/reveal/layout 92.66ms, exit/layout 28.15ms | One hidden-window synthetic sample, not every real tree/display |

These summarize the relevant former JSON reports; the runner can regenerate artifacts. No benchmark threshold or existing assertion was softened for closeout. Keep separate resource limits (ARCHITECTURE), these observations, and untested physical/real-library performance.

## 4. Remaining long-term scope

No next feature is selected or authorized by this list. Discuss scope before implementing.

| Candidate | Status / constraints |
|---|---|
| Browse/Gallery search and filtering | Explicitly deferred by user to a future phase, not the next active slice. Matching, filtering vs navigation, scope, selection and keyboard UX need design. Reader Go stays numeric. |
| Additional sorting | Deferred; preserve shared natural filename order until separately scoped |
| External file drag/import/export or cross-panel dragging | Deferred; do not confuse with implemented folder drops/internal Gallery moves; retain safe operations |
| Richer metadata, saved collections/bookmarks, more handlers/view modes | Deferred; reference existing files rather than silently duplicating/mutating them |
| Duplicate detection | Optional future scope; must never automatically delete files |
| Themes, configurable shortcuts/tree width/peek, further usability/performance work | Deferred; any work must preserve existing safety and native lifetime/input contracts |
| Packaging/deployment | No installer/self-contained acceptance yet; decide runtime/distribution requirements separately |

Bulk rename is **not planned**. Extra codecs/FLAC/animated WebP, PDF/document/3D viewing and universal codec support are not implicit requirements of the accepted MVP. Fix reported regressions within existing behavior; do not reopen completed implementation slices solely because exhaustive environmental matrices remain untested.
