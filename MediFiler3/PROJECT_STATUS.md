# Project status

Updated 2026-11-15. Short-term issues, unresolved decisions and immediate actions only. Product behavior: [SPECIFICATION.md](SPECIFICATION.md). Completed milestones/acceptance/measurements: [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md). Build and diagnostic mechanics: [ARCHITECTURE.md](ARCHITECTURE.md).

## Outstanding issues

| Issue | Evidence / present position | Next action when pursued |
|---|---|---|
| Gallery drag-hover expansion check timed out once | Folder-color full run: row000 never reached the expected expanded state within the unchanged 5s assertion (GalleryDragChecks.cs:145). Unchanged-code full repeat passed 223/223. No dwell/input/root-cause diagnosis yet; the repeat does not prove it harmless. | On recurrence capture row/container identity, children/discovery state, drag point/target, dwell timer and layout changes. Retain the 600ms dwell and existing assertions; do not modify color or drag behavior to conceal the timeout. |
| Historical native Gallery instability not conclusively eliminated | Earlier collection 0xC0000005 and later XAML transition 0xc000027b; no universal root-cause closure despite targeted fixes/successful runs. Reader's owned-pixel fix addresses a separate diagnosed lifetime defect. | On recurrence preserve WER/dumps, native stack and shared-path/disjoint/transition/input context; use ARCHITECTURE's failure knowledge. |

## Verification limits still carried

- Dedicated Narrator/screen-reader speech, exhaustive modifier/focus/access-key/touchpad/touch behavior, mixed-DPI/monitor transitions, fullscreen overlay feel and dialog edges/shadows lack a complete physical matrix. User confirmations apply only to their tested workflows.
- Diverse real-library/cold/network image throughput, sustained frame pacing and prolonged process/native memory behavior remain unmeasured. Generated local samples and output/cache bounds are not total-process ceilings or codec guarantees.
- Locked/permission-denied/remote/removable roots, disconnect/reconnect, cross-volume backup failure, live notification storms, every orientation/container/animation variant, current actual picker/chooser/app-launch behavior and installer/deployment need targeted environment review. Earlier representative physical acceptance is not evidence for every later configuration.
- Blocked OS calls cannot be forcibly cancelled; watcher/cache failures can need retry/F5. Same-size/timestamp edits without hints while coverage stopped need F5. These known platform limits are not permission to remove guards.

## Decisions still open

Ordered name/count/all-folder rules, direct-file ranges, editable exact-zero and disabled collapse-all defaults, root-aware expansion/Hide, live saved-rule previews and grouped Settings cards are implemented. File name/color rules now color the Browse native title bar and Gallery filename gradient; Reader is excluded. Arbitrary depth-palette editing remains future scope. Search remains deferred; hidden Reader/Gallery controls must not be restored incidentally.

The user accepted the measured roughly one-second clear of 10,000 Gallery selections on 2026-11-14. It is not an outstanding performance issue; optimization is unnecessary.

## Immediate next steps

1. Follow the user's next request within the current specification. Ordered name/count rules, color/expansion actions, depth fallback and Settings cards are delivered; do not automatically implement depth-palette editing or search.
2. On native crash recurrence preserve diagnostics before changing behavior. Keep the documented Reader scroll/dimension regression and exact pixel/fraction assertions.
3. Before any implementation/release claim, inspect actual Git state and rerun checks appropriate to the change. Never restart the user's running app or commit/push without instruction.

## Current validation confidence

Filename/color rules: clean temporary-output build (zero warnings/errors); 411 unit/filesystem, 41 focused UI/Gallery and 231 full native checks passed, zero failures/skips. Logs %TEMP%\MediFiler3FileColors-focused7.log and MediFiler3FileColors-full.log; full artifacts MediFiler3Integration-17dd9eae65094ed4b9780cdc4f9565b1. Coverage includes literal names/extensions, priority/disabled states, rule limits/validation/JSON/SQLite, live new/saved previews and navigation draft discard, native caption write acceptance with exact RGB, Gallery rendered gradients/update/removal, and toolbar/Reader exclusions. Existing folder-rule and Reader pixel/fraction regressions remain intact. Native probes wait for WPF reattachment and the current Gallery grid/containers; this avoids confusing retained native IsLoaded state or obsolete grids with readiness. DwmGetWindowAttribute rejects caption-color readback in the test host, so validation checks the accepted DwmSetWindowAttribute result/value and bound brush. Physical visual/accessibility/DPI review and physical OLE dragging remain limits; prior Gallery dwell timeout remains independently unresolved above.

The deterministic gated-decode regression failed before the fix with tracing enabled and disabled, then passed after it and in focused/full suites. Existing waits, tolerances and pixel/fraction assertions are intact. Root cause, guard mechanics and reproduction instructions are in ARCHITECTURE; milestone evidence is in DEVELOPMENT_PLAN.
