# Phase 5.5 acceptance report

Date: 2026-11-11. **Step 5.5 is complete for the approved Gallery MVP**, including the subsequently approved internal Gallery-to-tree drag/drop work. Automated regression, bounded-scale characterization and the targeted user physical checks are recorded below. Remaining physical/environment cases stay visible as known limits. Continuous reader, extra sorts/filtering and external file drag/export are later scope; bulk rename is not planned.

## Baseline and changes

Verification started from clean `master` commit `ae272bd` (`Tree opens closed folders and allows for scrolling while dragging.`). This closeout changes documentation and adds diagnostic measurements to existing native scale checks. Production sources, project settings, assertions, schema and safeguards are unchanged. No commit, push, deployment, installer check or app restart was performed. Temporary output preserves the user's running app.

## Fresh automated acceptance

| Check | Result |
|---|---|
| Solution build | Zero warnings/errors |
| Unit/filesystem suite | 327 passed; zero failures/skips |
| Full WPF/WinUI native runner with supplied codec fixtures | 192 passed; zero failures/skips |
| Focused Gallery repeat to characterize scale variability | 31 passed; zero failures/skips |
| Diff formatting | Pass |
| Production source hash audit | All 126 production source/project files unchanged |

The focused repeat supplements the full run. It exercises existing bounds and adds a second scale observation; it does not replace codecs, operation/restart, watcher or drag acceptance.

Full evidence: `%TEMP%\MediFiler3Integration-be01e8c8971f487baad7928f343c6463`; log `%TEMP%\MediFiler3Phase55-native.log`. Focused evidence: `%TEMP%\MediFiler3Integration-3cc8af46851a4c6993aec8b79ff76997`; log `%TEMP%\MediFiler3Phase55-scale-repeat.log`. Temporary evidence may be cleaned up; the two raw performance summaries are retained in [full scale sample](verification/phase5-scale-full.json) and [focused scale sample](verification/phase5-scale-focused.json).

## Coverage and preserved behavior

| Area | Evidence |
|---|---|
| Gallery/session/input | Retained native grid/ItemsSource; lazy Browse/Gallery handoff; persisted independent tile dimensions; native Extended selection, Alt-hover, focus-only movement, Ctrl+A/Escape/Enter, accessibility peers and scoped shortcuts |
| Thumbnails/native lifetime | Shared default-size raw PNGs, crop/hover-fit, pink borders, cancellation/recycling/cache bounds, same-metadata freshness, restart/clear safety, retained previews through Home/Settings, dialogs and move/Undo echoes; actual HWND tree exclusions |
| Scale/reconciliation | 10,000-record virtualization/select-all/clear/distant scroll; two 80/10,000-file return rounds; four disjoint resets with no intermediate index changes; retained ItemsSource; inactive work suppression |
| Safe operations | Grouped moves/recycle, partial/cancelled/unattempted outcomes, collisions/backups, identity checks, source-root history, single-file rename, single versus multi-file recycle confirmation, restart recovery and partial restore/retry |
| Tree/history | Immediate counters on both sides of moves; retained root/child identity and expansion; folder rename/delete/restore reconciliation; Browse Undo selects restored file and Redo chooses next/last-neighbor; Gallery selection policy retained |
| Drag/drop | Pre-press selection snapshot, click/threshold separation, cropped 80%-opaque pointer-anchored preview, opaque per-gesture tokens, target cues, stale/cancel rejection, grouped Undo, delayed expansion, edge scrolling and scoped native wheel packet routing/cleanup |
| Carryover | Viewer/GIF/text/media lifetimes, paused reload, Home/settings/persistence, fullscreen/layout, safe backup cleanup and three fresh-process restart workers remain in the full suite |

Representative native checks are [GalleryChecks](../tests/MediFiler3.App.Integration/GalleryChecks.cs), [GalleryResumeChecks](../tests/MediFiler3.App.Integration/GalleryResumeChecks.cs), [GalleryMutationChecks](../tests/MediFiler3.App.Integration/GalleryMutationChecks.cs) and [GalleryDragChecks](../tests/MediFiler3.App.Integration/GalleryDragChecks.cs). Business/filesystem checks remain in the infrastructure test project.

## Measured scale observations

Both samples ran on this Windows 11 x64 development machine (.NET 10.0.14, OS build 10.0.26300, 12 logical processors), at 100% DPI with a 1208-by-713 logical-pixel native viewport. The list contains 10,000 generated nonexistent/unsupported paths. Existing real native controls and the Windows thumbnail service are exercised, but this workload measures list/selection behavior rather than real-image decode throughput or directory scanning.

| Observation | Full run | Focused repeat |
|---|---:|---:|
| Publish list and observe initial realization | 108 ms | 86 ms |
| Select all (synchronous call plus validation) | 91 ms | 189 ms |
| Clear all 10,000 selected items | 764 ms | 1,034 ms |
| Initially realized / after select-all | 28 / 28 | 28 / 28 |
| Realized after scrolling to item 9,000 | 60 | 60 |
| Decoded cache entries / bytes at sample point | 70 / 573,440 | 58 / 475,136 |
| Thumbnail requests during 100 ms inactive observation | 0 | 0 |
| Disjoint resets / individual notifications | 4 / 0 | 4 / 0 |
| Same retained ItemsSource | Yes | Yes |
| Two return rounds, including explicit waits and validation | 2,785 ms | 3,654 ms |

All existing assertions passed: fewer than 1,000 realized tiles, at most 512 decoded entries/128 MiB, retained source identity and stopped inactive scheduling. The observed cache values are sample points, not worst-case memory measurements. Polling/layout waits and validation are included where stated; the return scenario contains fixed waits. These are two smoke samples, without percentile/FPS guarantees or a cross-version comparison.

**Known scale latency:** clearing a 10,000-item selection synchronously took 0.76-1.03 seconds. The scale check bounds work/memory, but bulk clear can visibly pause at that size. Retain this observation for future performance work. No new timing threshold or softened assertion was introduced for closeout. Real-library thumbnail throughput, scrolling frame pacing and prolonged native memory behavior remain unmeasured.

## User-confirmed physical acceptance

The user explicitly confirmed in this conversation:

- "Mouse-wheel scrolling works while dragging now."
- "Dragging into nested folders work fine, as does edge scrolling."

These close physical drag-wheel delivery, nested-folder destinations/hover access and edge scrolling for the user's tested environment. The user then requested completion of step 5.5. These confirmations do not extend to unspecified monitor/DPI, Narrator, codec or storage configurations.

## Remaining verification limits

- Mixed-DPI/monitor transitions, fullscreen drag preview/hotspot/cursor feel and long-session layout behavior remain unverified physically.
- Complete physical Ctrl/Shift/Alt/key/focus/access-key interaction, Narrator speech and general accessibility usability remain unverified beyond the recorded user feedback and automation peers.
- Latest thumbnail appearance and real-library loading/scrolling performance have no separate full physical signoff or benchmark. The 10,000-record clear latency above remains known.
- Native stability remains a risk: historical `0xC0000005` collection-notification faults and the later `Microsoft.UI.Xaml.dll` `0xc000027b` transition crash are retained in [handover history](PHASE5_HANDOVER_HISTORY.md). The final full/focused runs passed without a crash; the earlier causes are not proven universally eliminated. Preserve WER/trace/context on recurrence. The 110 ms carousel fade assertion has historical timing sensitivity; it passed in this closeout without weakening it. Earlier ShellIpcClient cancellation diagnostics have no measured real-library impact.
- Network/removable roots, permission-denied/cross-volume backup storage, actual external launches/pickers, installer/deployment and the remaining [Phase 4 physical cases](PHASE4_ACCEPTANCE.md) remain open. Keep [Phase 3 codec/container limits](PHASE3_INTEGRATION.md), including representative rather than universal codec coverage.

## Reproduction and next work

Use the build/full-suite commands in [PROJECT_STATUS.md](PROJECT_STATUS.md) and the [runner README](../tests/MediFiler3.App.Integration/README.md). Build once before no-build checks; never build concurrently against shared obj directories. A normal temporary-output build does not update an already-running Rider/app instance.

Gallery phase 5.1-5.5 is closed with these recorded limits. Follow up reported regressions or separately discuss the next feature slice. Continuous reader and additional sorts/filtering require their own scope discussion. Existing source-file/backup/cache ownership, root-scoped history and thumbnail freshness safeguards remain mandatory.
