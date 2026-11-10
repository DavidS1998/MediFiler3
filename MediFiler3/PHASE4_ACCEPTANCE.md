# Phase 4 acceptance report

Updated 2026-11-10. Automated step 4.7 is complete. The user tentatively accepted Phase 4 overall while acknowledging that not all physical use cases could be tested; unverified cases below remain known limits. Phase 3's earlier manual acceptance remains recorded separately and does not imply acceptance of the later Home/dialog/backup/layout/watcher changes.

## Automated evidence

- Solution build: zero warnings/errors, using the temporary continuation output so the running user app remains untouched.
- Unit/filesystem suite: **276 passed, zero failures/skips**.
- Explicit WPF/WinUI runner: **144 passed, zero failures/skips**, with supplied codec fixtures. This includes the existing Phase 3 viewer/input/media/resource/operation regressions and all Phase 4 checks.
- Git diff formatting check passes. Changes remain uncommitted on top of the existing documentation and step-4.6 work.

## Fresh-process restart acceptance

`tests/MediFiler3.App.Integration/Phase4RestartChecks.cs` starts three separate OS processes, each with fresh SQLite connections/services and a real transparent MainWindow/WinUI environment. They share only a runner-owned directory containing the database, source fixtures, thumbnail cache, overwrite backups and expected layout. No user library, database or backup directory is supplied.

| Process | Verified behavior |
|---|---|
| Seed | Home starts with no roots/selection/playback/watcher. The native Settings slider changes carousel density; folder pin and media volume/mute persist through the real MainWindow event wiring. Explicit root loads and favorite state are saved. Root A replacement preserves the old target for Undo; root B retains independent rename history. PNG/index metadata and final captured normal bounds are flushed on close. |
| Reopen | Settings, favorites, root counts, layout and thumbnail/index metadata survive process exit. MainWindow applies restored carousel/pin; first media opens paused with saved volume/mute. Replacement Undo/Redo restores both versions; the other root's history remains available. A real FileSystemWatcher discovers an external insertion without counting another root visit. Leaving Browse stops its watcher. Thumbnail clear preserves source files, overwrite backups and both root history stacks. |
| Verify | A third process starts at Home without roots/selection/playback/watchers. Thumbnail clear remains effective; favorite/root counts and settings remain saved. Replacement Undo still restores the old target, and the other root's independent Undo still succeeds after cache clear and another process exit. |

The child windows use the same production MainWindow and persistence services, with the production initialization/capture-before-flush order reproduced by the runner. This is not a direct test of the App host's startup/error-message code or an installer/deployment test. External launching/choosers remain stubbed; playback stays paused/muted. Existing native layout tests separately cover maximized/minimized restore and synthetic work-area changes. Existing unit tests cover migrations, invalid payloads, journal/recovery safety, cache ownership and race handling.

## Physical acceptance still required

Use a deliberately launched current build and disposable source folders for mutation checks. A temporary-output build does not replace the already-running Rider/application binary. Preserve current user work when choosing when to restart for review.

- [ ] Home: inspect spacing and resizing, tab/focus navigation, favorite stars in all lists, root opening, folder drops and actual Add/Locate folder pickers. Missing roots must retain Locate/Remove; Remove must not delete the folder.
- [x] Dialog appearance: user confirmed "Dialogs look good." Remaining physical checks below are not inferred from that visual acceptance.
- [ ] Dialog input/edge cases: physical Enter/Escape/Tab/focus, long-path comparison tooltips, scaling/DPI and modal behavior above video. Check numbered Rename, Cancel and explicit preserving Overwrite; folder replacement remains whole-tree, not merge.
- [ ] Backup storage: exercise Settings cancellation/confirmation and unavailable/permission-denied storage on another volume. Cleanup must retain unrelated/current/recovery data; verified dependent history retires before deletion. No automatic backup expiry.
- [ ] Window placement: disconnect/rearrange monitors, move between mixed-DPI displays, relocate taskbar/change work area and close while minimized/maximized. Restart must remain visible and never minimized; normal restore bounds must remain sensible. Forced monitor-enumeration failure remains an untested branch.
- [ ] Live changes: use a removable/network root, disconnect/reconnect it, and edit inactive roots. Check degraded status/retry/recovery, selection and root-history preservation. Browse-away/return must reconcile; affected media reopens paused and unrelated changes should leave playback/zoom/text interaction alone.
- [x] Root-label flicker: user confirmed the updated build no longer flickers.
- [ ] Load/storm behavior: browse a large hierarchy while creating/renaming/deleting many files. Input/rendering should remain responsive; counts/tree/carousel must converge. F5 remains the recovery action for missed notifications or same-size/timestamp edits without a delivered hint.
- [ ] Cache controls: inspect Settings usage/clear on the current build. Revisit media after clear to check regenerated thumbnails. Source media, overwrite backups and Undo/Redo must remain intact. Locked/unsafe cache entries can delay reaching the retention target and remain available for retry.

Record the environment and result of each physical item here before declaring Phase 4 fully accepted. Network/removable/monitor transitions, actual picker/Open With behavior and subjective usability cannot be inferred from transparent-window automation. Current codec/fixture limits (including MKV's WebM/Matroska subset), expanded-tree virtualization limits and deferred Phase 5/6 features remain unchanged.

## Reproduction

Use the build/test/native-run commands in [PROJECT_STATUS.md](PROJECT_STATUS.md). Build once before the `--no-build` runners; do not build concurrently against shared `obj` paths. The normal native runner launches the three restart workers automatically and prints the retained runner directory. The `--phase4-restart-worker` switch is an internal test-runner entry point, not a production application option. Physical results are recorded above; no Phase 5 implementation is included in this step.

## Large-folder label follow-up

The user reported rapid root-name flicker while browsing very large folders. Unchanged hierarchy reconciliation repeatedly reset known subtree totals to unknown and back; progressive discovery also republished DisplayName when its text had not changed. Reconciliation now returns early for unchanged loaded children and only invalidates discovery state when membership changes. DisplayName notifications are emitted only when the rendered text changes. Genuine direct/subtree count changes and incomplete-discovery placeholders remain supported.

Two 2,000-subfolder regressions verify that unchanged rescans and incomplete progressive discovery do not repeatedly notify the root label, and that a real count change still updates it. The user confirmed the updated build no longer flickers; this specific fix is physically accepted. Broader large-tree/storm acceptance remains open. Dialog appearance has been accepted by the user, separately from the remaining input/DPI/edge-case checks.

## Tentative acceptance and UX refinement

User stated that Phase 4 tentatively looks good but not all use cases can be definitively tested. This is recorded as tentative overall acceptance, not proof that unchecked physical cases passed. The subsequent requested Settings/Home/fullscreen/carousel changes are implemented and require visual/physical review on the updated build. See ARCHITECTURE.md for their policy and UiRefinementChecks.cs for automated evidence.

Fullscreen reveal follow-up: top hover now shows the actual native caption with the toolbar and remains active over nonclient title-bar controls. Left hover reveals the sidebar and folder tree together, offsetting the tree beside the sidebar and retaining both while the pointer is within their combined area. Pin state and normal-mode reveals remain unchanged; video exclusions follow the offset tree. `.jfif` routes through the WIC image viewer and WIC thumbnail generation, with a JPEG-encoded fixture covering decoding, image capabilities, metadata, thumbnails and source-file release.
