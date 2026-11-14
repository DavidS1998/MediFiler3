# Project status

Updated 2026-11-14. Short-term issues, unresolved decisions and immediate actions only. Product behavior: [SPECIFICATION.md](SPECIFICATION.md). Completed milestones/acceptance/measurements: [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md). Build and diagnostic mechanics: [ARCHITECTURE.md](ARCHITECTURE.md).

## Outstanding issues

| Issue | Evidence / present position | Next action when pursued |
|---|---|---|
| Historical native Gallery instability not conclusively eliminated | Earlier collection 0xC0000005 and later XAML transition 0xc000027b; no universal root-cause closure despite targeted fixes/successful runs. Reader's owned-pixel fix addresses a separate diagnosed lifetime defect. | On recurrence preserve WER/dumps, native stack and shared-path/disjoint/transition/input context; use ARCHITECTURE's failure knowledge. |

## Verification limits still carried

- Dedicated Narrator/screen-reader speech, exhaustive modifier/focus/access-key/touchpad/touch behavior, mixed-DPI/monitor transitions, fullscreen overlay feel and dialog edges/shadows lack a complete physical matrix. User confirmations apply only to their tested workflows.
- Diverse real-library/cold/network image throughput, sustained frame pacing and prolonged process/native memory behavior remain unmeasured. Generated local samples and output/cache bounds are not total-process ceilings or codec guarantees.
- Locked/permission-denied/remote/removable roots, disconnect/reconnect, cross-volume backup failure, live notification storms, every orientation/container/animation variant, current actual picker/chooser/app-launch behavior and installer/deployment need targeted environment review. Earlier representative physical acceptance is not evidence for every later configuration.
- Blocked OS calls cannot be forcibly cancelled; watcher/cache failures can need retry/F5. Same-size/timestamp edits without hints while coverage stopped need F5. These known platform limits are not permission to remove guards.

## Decisions still open

No new feature is selected. Search is explicitly deferred to a future phase; do not treat the earlier search suggestion as an active task. Other optional work is tracked in DEVELOPMENT_PLAN. Current hidden Reader commands/hotkeys and Gallery external buttons are intentional requirements, not missing implementation to restore.

The user accepted the measured roughly one-second clear of 10,000 Gallery selections on 2026-11-14. It is not an outstanding performance issue; optimization is unnecessary.

## Immediate next steps

1. Follow the user's next request within the current specification. Reader file read-only guards, miscellaneous Windows icon fallback, folder collision totals and tree reveal changes are complete. Do not automatically start search or a new Reader slice.
2. On native crash recurrence preserve diagnostics before changing behavior. Keep the documented Reader scroll/dimension regression and exact pixel/fraction assertions.
3. Before any implementation/release claim, inspect actual Git state and rerun checks appropriate to the change. Never restart the user's running app or commit/push without instruction.

## Current validation confidence

Browse fallback follow-up: clean build, 363 unit/filesystem and 222 full native checks passed, zero failures/skips. Full log %TEMP%\MediFiler3BrowseFallback-full.log, artifacts MediFiler3Integration-9f7b0cacac0646a297935352c0986e14. Final matching-file retention guard and previous-file rejection were then verified by --browse-fallback-only (1 passed), log MediFiler3BrowseFallback-focused.log, artifacts MediFiler3Integration-f6875a19f8db49948c65167e0dd08aa6; full suite was not repeated for that small guard follow-up. The real WPF surface check covers PSD association fallback, actual ICO green pixels, rendered bitmap pixels, message coexistence, resize and empty-result retention. Photoshop-specific association and diverse real PSD thumbnail codecs are environment-dependent and not established by the synthetic PSD fixture. Temporary paths are diagnostic hints only.

The deterministic gated-decode regression failed before the fix with tracing enabled and disabled, then passed after it and in focused/full suites. Existing waits, tolerances and pixel/fraction assertions are intact. Root cause, guard mechanics and reproduction instructions are in ARCHITECTURE; milestone evidence is in DEVELOPMENT_PLAN.
