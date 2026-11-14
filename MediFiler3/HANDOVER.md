# Handover

Updated 2026-11-14. Latest task: user reported missing Browse thumbnails and suspected fallback text overwriting them; expects PSD association icons and ICO source previews.

## Findings and changes

- Initial real ViewerHostView PSD/ICO check passed locally; message rendering itself did not hide successful bitmaps. No claim that the user's specific PSD/environment failure was reproduced.
- A null fallback result did erase an already available bitmap. ViewerHostView now preserves matching-file bitmaps/immediate previews, with identity tracking to reject old-file pixels. ThumbnailViewerHandler uses the default Windows thumbnail bucket instead of viewport-sized requests.
- FileSystemThumbnailService retries Shell ListView file/type icons when miscellaneous SingleItem thumbnails are absent or cannot decode. PSD icon reflects the installed association; valid ICO test verifies the icon's actual green pixels.
- New BrowseFallbackChecks uses a real WPF host/native thumbnails and checks source and rendered surface pixels, message coexistence, resize, retained previews and previous-file rejection. Run --browse-fallback-only; included in full runs. PSD fixture is synthetic invalid PSD content for association-icon fallback, not PSD decoding/Photoshop-installation evidence.
- Preserve all earlier uncommitted Reader read-only/restoration, unified tree reveal, folder collision totals, diagnostics and documentation on master (HEAD 17afe93). Search remains deferred; Gallery selection latency remains accepted. No next feature is selected.

## Validation

Clean build, 363 unit/filesystem tests and 222 full native checks passed (zero failures/skips). Full log %TEMP%\MediFiler3BrowseFallback-full.log, artifacts MediFiler3Integration-9f7b0cacac0646a297935352c0986e14. Final matching-file guard/negative stale-preview check was added afterward and passed the focused display check (1/1), log MediFiler3BrowseFallback-focused.log, artifacts MediFiler3Integration-f6875a19f8db49948c65167e0dd08aa6. Full suite was not repeated for that small guard change. Output path %TEMP%\MediFiler3ReflowInvestigationBuild. Temp paths may disappear; physical/environment limits remain.

AGENTS unchanged: SHA-256 60bf6cfcbc3e37a3a278a9942803ce0f23b26d3420c8e006f45c91a2f08d80a3. Keep six authoritative documents. No commit/push or user-app restart; filesystem changes confined to isolated fixtures.
