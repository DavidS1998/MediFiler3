# Continuous Reader acceptance report

Date: 2026-11-12. **Reader slices 1-6 are complete for the approved MVP, with the verification limits below.** Slice 6 closes automated regression, measured scale/resource characterization and the user's acceptance in their tested environment. It does not claim exhaustive hardware, accessibility, codec or storage acceptance. See [current status](PROJECT_STATUS.md), [active handover](READER_HANDOVER.md) and [historical implementation notes](READER_HANDOVER_HISTORY.md).

Current presentation follow-up: after this closeout the user temporarily hid Reader's Rename/Delete/Undo/Redo, default/custom application and Open-current-image-in-Browse toolbar actions, and disabled their Reader-only shortcuts. Functions and failed-page recovery actions remain implemented. A further presentation follow-up also hides the two external-application buttons in Gallery while retaining them in Browse; Gallery file operations and shortcuts remain unchanged. Search is deferred to a future phase. The original measurements and acceptance coverage below remain the closeout baseline; [current status](PROJECT_STATUS.md) and [handover](READER_HANDOVER.md) record the follow-up verification.

## Baseline and changes

Closeout started on `master` at `b1cd13b` (`Reader mode file operations`), with the uncommitted native arrow-key fix, its regression check and documentation already present. Those changes are preserved. Slice 6 adds one test-only acceptance scenario, retained JSON measurements and documentation. A SHA-256 comparison confirms all **143 production source/project/assets files** under `src` (excluding build output) are unchanged from the start of closeout. Existing assertions, source ownership, schema 7, codec dependencies and operation policy were not changed. No commit/push, deployment, installer check or running-app restart was performed.

## Fresh verification

| Check | Result |
|---|---|
| Solution build using temporary output | Zero warnings/errors |
| Full unit/filesystem suite | 362 passed; zero failures/skips |
| Full WPF/WinUI native runner with supplied codec/media fixtures | 221 passed; zero failures/skips |
| Focused Reader repeat with generated fixtures | 27 passed; zero failures/skips |
| Production source hash audit | All 143 files unchanged during slice 6 |
| Diff formatting and modified Markdown relative links | Pass |

Full evidence: `%TEMP%\MediFiler3Integration-4605bff9714048a481b75f22331c1143`; log `%TEMP%\MediFiler3Reader-slice6-full.log`. Focused repeat: `%TEMP%\MediFiler3Integration-caf7ba762b904f259f8f8e627c8f6cb8`; log `%TEMP%\MediFiler3Reader-slice6-focused.log`. Reproduction uses the commands in [PROJECT_STATUS.md](PROJECT_STATUS.md) and the [runner README](../tests/MediFiler3.App.Integration/README.md). Build once before no-build checks, never concurrently against shared obj directories. Generated focused fixtures do not replace full supplied-codec acceptance.

## Accepted MVP behavior and evidence

| Area | Evidence and accepted scope |
|---|---|
| Workspace and page projection | Shared root/folder/current file and natural ordering; unsupported files remain in the shared list; image-only projection, nearest-image entry, empty state and folder boundaries pass unit/native checks. |
| Source images and native lifetime | Representative JPEG/JFIF, PNG, BMP, GIF, TIFF and supplied WebP decode as static first-frame pages. Corrupt/missing images retain named placeholders and guarded retry/Browse recovery. Pixel rendering, cancellation, resize, warm return and invalidation regressions pass. |
| Layout and settings | Width/height sizing, 10-100% width, spacing/preload/counter persistence, centered aspect-preserving pages, image/fraction/gap anchors and Gallery-style counter pass. |
| Navigation and input | Numeric bounded Go/Cancel/stale results; previous/next/line/viewport/start/end navigation; native HWND key delivery, repeats, queued-key single execution and Reader/toolbar/sidebar/tree focus pass. The user physically confirms the arrow-key fix. |
| Organization and history | Single-image rename/recycle/tree-destination move, image-only next/previous fallback, restored current page on Undo, Redo, counters/node retention, explicit collision Cancel/Rename/Overwrite and backup restoration, stale-page/view guards pass. No Reader bulk selection/rename or new drag gesture was added. |
| Live changes and continuity | Insertions/removals, same-size/timestamp replacement and recovery, known rename/ancestor mappings, overflow-style rescans, F5 and inactive return pass. Unaffected pixels remain warm; actual content/rescan invalidation remains authoritative. |
| Shell and view integration | Open in Browse, default/custom application buttons and shortcuts, first-file explicit active-folder reload, shallow child-folder collapse, pinned tree, owned rounded dialogs and fullscreen/tree virtualization regressions pass. Launch services are recorded/stubbed in Reader UI checks; no external applications are launched by that runner. |
| Existing application behavior | Full Browse/Gallery, native drag/hover/edge/wheel, watcher, root-history, safe-operation, persistence and window-layout suites pass. Prior acceptance reports remain in force. |

The source decoder still has two concurrent jobs, at most 4,194,304 output pixels (16 MiB BGRA) per page, 128px width buckets capped at 8192, and a 128 MiB native decoded LRU. Visible rows plus one previous/configured upcoming window remain lazy. These are output/cache/work bounds, **not** total process or internal codec allocation ceilings. Preserve the XAML-owned WriteableBitmap pixel handoff; the earlier prematurely disposed SoftwareBitmap caused `RO_E_CLOSED` native failures. [Crash investigation](verification/reader-crash-2026-11-11.txt) and older Gallery/native faults remain recorded.

## Scale and resource measurements

The new scenario creates **1,000 actual PNG files** from a deterministic 1024x1536 raster, 47,893,000 bytes total. It uses normal folder listing and real WIC source decoding, then visits page indexes 0, 125, 500, 999, 250, 0 and 500, leaves for Home and returns. The copied raster makes the experiment reproducible; it is not a diverse user library. Fixture creation is outside the entry timer. Local filesystem/OS caches are not forced cold.

Raw samples: [full suite](verification/reader-acceptance-full.json) and [focused repeat](verification/reader-acceptance-focused.json).

| Observation | Full suite | Focused repeat |
|---|---:|---:|
| Folder listing plus Reader entry ready | 208.0 ms | 167.9 ms |
| Six subsequent page jumps, min-max | 36.3-176.8 ms | 31.8-69.9 ms |
| Home-to-Reader warm return ready | 110.1 ms | 108.7 ms |
| Maximum realized rows at sample points | 4 | 4 |
| Maximum sampled decoded cache | 68.91 MiB | 68.91 MiB |
| Maximum concurrent source decodes in Reader run | 2 | 2 |
| Source decode calls in the scenario | 18 | 19 |
| Source requests during 150 ms inactive observation | 0 | 0 |
| Current source bitmap reused on return | Yes | Yes |
| Whole-process sampled working set range | 617.5-697.6 MiB | 441.0-558.7 MiB |
| Whole-process sampled private memory range | 506.6-587.8 MiB | 360.8-479.3 MiB |

Ready means the selected page has a source assigned and native offset restoration/render queue has settled. Wait polling has 25 ms granularity. Timings include the stated listing/navigation/reattachment work and validation waits; they are not compositor-frame/FPS measurements or precise decode durations. Process-memory samples cover the **entire hidden integration process**, including earlier tests and fixture construction. The difference between runs is not a measured Reader leak or a Reader-only allocation estimate. The observations are sample points, not continuous peaks, percentiles or total-memory limits. No timing threshold or existing assertion was softened for closeout.

The existing 10,000-record native scenarios remain structural lazy-realization/distant-jump/cancellation checks using nonexistent paths; they do not measure real-image throughput. The new actual-file sample complements them without relabeling them. The earlier [10,102-node tree/fullscreen sample](verification/tree-fullscreen-2026-11-12.json) also remains a separate observation.

## User-confirmed acceptance

Before the final arrow fix, the user reported that everything seemed in order except Reader arrow navigation. After the fix, the user stated **"Arrow keys work now"** and requested completion of slice 6 and documentation. Earlier feedback also reported that the Reader image-worker crash appeared fixed.

This closes the reported arrow defect and records acceptance of the Reader workflow in the user's tested setup. It does not infer an exact library size, a monitor/DPI matrix, Narrator output, touchpad hardware or storage conditions that were not specified.

## Recorded verification limits

- Dedicated benchmarks against the user's real, diverse libraries, cold/network I/O, sustained scroll frame pacing, prolonged process/native memory behavior and very large source images remain unmeasured. The local generated-file samples above are the reproducible characterization available for this closeout.
- Physical Narrator/screen-reader speech, exhaustive keyboard/modifier/focus/access-key combinations, touchpad/touch behavior and contrast usability remain unverified beyond the explicit user feedback and automation peers/native-key checks.
- Mixed-DPI/monitor transitions, fullscreen/tree-overlay interaction and visual dialog edges/shadows still need dedicated physical review; automated geometry, region and fullscreen checks do not establish that entire matrix.
- Locked/permission-denied, remote/removable roots, disconnect/reconnect, cross-volume backup storage, every orientation/codec/animation/container variant and actual Reader Windows chooser/application launches have no new exhaustive physical signoff. Existing [Phase 3 codec limits](PHASE3_INTEGRATION.md) and [Phase 4 physical limits](PHASE4_ACCEPTANCE.md) remain.
- Historical Gallery native collection/transition faults are not proven universally eliminated. The Reader owned-pixel crash fix and successful user feedback/runs are retained evidence, not a universal no-crash guarantee. Preserve WER/trace/context on recurrence. The known Gallery 10,000-selection clear latency of 0.76-1.03s remains in [Gallery acceptance](PHASE5_ACCEPTANCE.md).
- Installer/deployment and packaging were not validated. Existing data/source/backup/cache ownership, explicit overwrite choices, identity/root journals and compensation/recovery safeguards remain mandatory.

These are carried limits, not unfinished implementation slices. Reader MVP is closed. Follow up reported regressions or separately scope the next feature; Browse/Gallery search, extra sorts/filtering and external file drag/import/export remain future work. Bulk rename is not planned.


Temporary Reader chrome verification (2026-11-12): solution build zero warnings/errors; 362 unit/filesystem tests and 221 full native checks passed, zero failures/skips, including all 27 Reader-runner checks. Hidden-key regression covers bare Enter, Ctrl+Enter, Ctrl+Shift+Enter, F2, Delete, Ctrl+Z and Ctrl+Y with initial/repeated input; no prompt/navigation/file mutation/external launch occurs. Direct operation/history/external/handoff APIs, failed-page recovery and Browse/Gallery remain verified. Full evidence `%TEMP%\MediFiler3Integration-516e430d8ead4687b2b1da1540fd26bb`; log `%TEMP%\MediFiler3Reader-hidden-commands-full.log`. The earlier slice 6 measurements/focused repeat remain historical baseline evidence. Preserve this temporary visibility/key policy until the user requests otherwise; search remains deferred to a future phase.
