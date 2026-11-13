# Project status

Updated 2026-11-13. Short-term issues, unresolved decisions and immediate actions only. Product behavior: [SPECIFICATION.md](SPECIFICATION.md). Completed milestones/acceptance/measurements: [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md). Build and diagnostic mechanics: [ARCHITECTURE.md](ARCHITECTURE.md).

## Outstanding issues

| Issue | Evidence / present position | Next action when pursued |
|---|---|---|
| Reader width-reflow check is timing-sensitive | Latest Gallery-visibility focused attempt left fractional scroll at page top (offset 1665 vs target 1776), although its visibility check passed. Unchanged-code/assertion repeat passed 27/27. This doesn't establish the timing issue harmless or resolved. | Capture actual/target offsets, path/fraction, native ViewChanged/restoration/queue/focus/dimension timing; retain exact fractional/pixel assertions. Don't weaken tests or change unrelated UI to conceal it. |
| Historical native Gallery instability not conclusively eliminated | Earlier collection 0xC0000005 and later XAML transition 0xc000027b; no universal root-cause closure despite targeted fixes/successful runs. Reader's owned-pixel fix addresses a separate diagnosed lifetime defect. | On recurrence preserve WER/dumps, native stack and shared-path/disjoint/transition/input context; use ARCHITECTURE's failure knowledge. |
| Large Gallery selection clear latency | 10,000 selected records took 0.76-1.03s synchronously in acceptance samples. | Profile if prioritized; preserve selection semantics, atomic native diffs and safety. No optimization is authorized merely by recording it here. |

Latest timing evidence: failing `%TEMP%\MediFiler3Gallery-hidden-external-focused.log`; unchanged-code repeat `%TEMP%\MediFiler3Gallery-hidden-external-focused-repeat.log`, artifacts `%TEMP%\MediFiler3Integration-bdb55a3705f844beada871a1c15dd110`. Temp paths are diagnostic hints, not durable dependencies.

## Verification limits still carried

- Dedicated Narrator/screen-reader speech, exhaustive modifier/focus/access-key/touchpad/touch behavior, mixed-DPI/monitor transitions, fullscreen overlay feel and dialog edges/shadows lack a complete physical matrix. User confirmations apply only to their tested workflows.
- Diverse real-library/cold/network image throughput, sustained frame pacing and prolonged process/native memory behavior remain unmeasured. Generated local samples and output/cache bounds are not total-process ceilings or codec guarantees.
- Locked/permission-denied/remote/removable roots, disconnect/reconnect, cross-volume backup failure, live notification storms, every orientation/container/animation variant, current actual picker/chooser/app-launch behavior and installer/deployment need targeted environment review. Earlier representative physical acceptance is not evidence for every later configuration.
- Blocked OS calls cannot be forcibly cancelled; watcher/cache failures can need retry/F5. Same-size/timestamp edits without hints while coverage stopped need F5. These known platform limits are not permission to remove guards.

## Decisions still open

No new feature is selected. Search is explicitly deferred to a future phase; do not treat the earlier search suggestion as an active task. Other optional work is tracked in DEVELOPMENT_PLAN. Current hidden Reader commands/hotkeys and Gallery external buttons are intentional requirements, not missing implementation to restore.

## Immediate next steps

1. Follow the user's next request within the current specification. For a new feature, agree scope and slices first; do not automatically start search or a new Reader slice.
2. If addressing the reflow/native concerns, reproduce with retained diagnostics and the documented native runner before changing behavior.
3. Before any implementation/release claim, inspect actual Git state and rerun checks appropriate to the change. A documentation-only build is not a fresh functional regression run. Never restart the user's running app or commit/push without instruction.

## Current validation confidence

The latest full functional baseline before Gallery's final visibility-only follow-up was a clean build, **362 unit/filesystem and 221 native checks**, zero failures/skips, including 27 Reader checks (`%TEMP%\MediFiler3Reader-hidden-commands-full.log`, artifacts `MediFiler3Integration-516e430d8ead4687b2b1da1540fd26bb`). That visibility follow-up had a clean build and **27 focused checks passing on repeat**, with the failure above retained; full suites were not repeated for it. Documentation consolidation does not retroactively refresh those results. Use ARCHITECTURE's commands for a fresh run; do not substitute generated focused fixtures for full supplied-codec coverage.
