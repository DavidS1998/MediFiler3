# Handover

Updated 2026-11-17. Latest task: Settings overscroll control, slider consistency, overwrite-backup review and 30-day retention. User confirmed the previous carousel responsiveness improvement.

## Delivered behavior

- Settings > Files has a native overscroll toggle. ApplicationSettings.BrowseOverscrollEnabled is an optional v1 field, default true for older profiles. MainWindow applies/persists it live through BrowseView/NavigationController. Toggling resets boundary attempts; disabled overscroll does not affect ordinary within-folder navigation or Reader behavior.
- Settings Carousel slider is left-aligned at 320px, matching Reader settings, with the existing odd-total ticks/snapping. Gallery toolbar Width/Height sliders use explicit 150px widths, centered/right-aligned layout, Auto columns, 8px group spacing/margins and no protruding ticks; 100-400/50 step values and persistence remain.
- System > Overwrite backups includes Review overwrite backups. Manual Clear also shows a finite-height virtualized preview: original path, linked stored path, file/folder-and-all-contents labels, saved/expiry dates and eligible/retained reason. Explorer links reveal/select verified backups rather than execute/open file contents. Native tests record reveal callbacks, never open Explorer.
- Manual confirmation defaults to Cancel and authorizes only reviewed eligible operation IDs; later backups are excluded. Inventory is cancellable/off-UI, using the operation maintenance gate and an indexed/cached history snapshot. Cleanup still revalidates identity/type/path/tree and durably retires the dependent root/batch before removal. Link completions do not access closed dialog controls.
- Backups expire 30 days after their last successful journal Completed/Redone preservation; original file timestamps do not define retention. OperationBackupRetentionService runs after startup recovery and hourly while open, through the same verified cleanup path. HistoryDiscarded does not reset expiry, so locked/failed cleanup can retry. Restored/current/untracked/changed-identity/link/recovery data are protected. Settings explains permanent removal and affected Undo/Redo loss. This user-authorized policy supersedes the old no-expiry rule.
- Backup Changed notifications refresh active history availability, including partial/cancelled cleanup. Failed/unsafe items remain listed with reasons; automatic removal/failures are logged. No database schema bump or new dependencies.

## Verification and continuation

Clean build, zero warnings/errors, using `%TEMP%\MediFiler3SettingsBuild`. 418 unit/filesystem tests and 235 full native checks passed, zero failures/skips, normal exits. Logs `%TEMP%\MediFiler3Settings-unit-final.log` and `%TEMP%\MediFiler3Settings-full-final.log`; artifacts `%TEMP%\MediFiler3Integration-f976730c802f481087bc9ef5e5489020`. Tests cover overscroll defaults/persistence/boundary reset, native slider layout, preview virtualization/location callbacks, confirmation/cancellation/scoped cleanup, exact expiry boundary, source timestamps, Undo/Redo age, locked-file retry, recovery-group safety and startup worker/clean stop. Prior Reader pixel/fraction, format, fullscreen and operation tests remain intact. `git diff --check` passed.

Physical Settings appearance and Explorer reveal behavior await user review; automated link checks use recorded callbacks. Existing physical/DPI/storage/codec limits and historical native diagnostics remain in PROJECT_STATUS and are not declared resolved. Previous carousel improvement is accepted; physical title/button compositor timing remains unconfirmed.

Current branch `master`, HEAD `22a7e2c` (`Improved responsiveness in Browse view`) at task start; working tree was clean. Changes remain uncommitted. No commit/push or user-app restart. Keep six authoritative documents and preserve AGENTS user edits. Next step is user review of these settings and backup behavior.
