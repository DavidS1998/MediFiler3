# Handover

Updated 2026-11-17. Latest task: user-authorized WinUI title/toolbar updates. User accepted breadcrumbs and explicitly approved the experimental Windows App SDK for actual PagerControl.

## Delivered behavior

- Windows App SDK is now `2.6.6-experimental`. Installed the matching x64 experimental framework/DDLM packages for this user from NuGet's Runtime package. Framework-dependent target machines need this runtime; stable-only installations cannot launch this build. No self-contained distribution was introduced.
- `MainWindow.TitleBar.cs` owns AppWindowTitleBar colors, extension, DPI-aware native caption-button insets and cached drag rectangles. `WinUiTitleBarIsland` renders the existing title as WinUI content. WPF WM_NCHITTEST bridges only passive title content to native HTCAPTION; the child title HWND is transparent to hit testing. Native buttons/drag/double-click/system menu remain Windows-owned. Fullscreen hides/reveals custom title content with the existing shell. Close detaches callbacks/hooks and disposes the island. Direct DWM caption writes and old diagnostics were removed; tests read AppWindow colors.
- Reader uses actual PagerControl in NumberBox mode (page/of/total and native first/previous/next/last buttons), replacing separate previous/next/Go toolbar buttons. It is hidden outside Reader and for empty projections. MainWindow defers/revalidates page requests; programmatic synchronization is guarded. Ctrl+G retains the existing bounded numeric Go dialog. File commands remain dormant/read-only.
- Browse image reset-to-fit is one AppBarButton with a zoom icon and visible zoom-percent label. This existing control belongs to Browse; Gallery retains thumbnail dimension sliders.
- Folder MenuFlyoutItems have native icons. Folder labels now occupy only their text column, with extra right padding before the menu button.
- Native transition testing exposed a detached-view PointFromScreen call in the folder-hover timer; the probe now checks PresentationSource before conversion.
- Breadcrumbs remain accepted and reversible: `FolderBreadcrumbsEnabled = false` in `WinUiToolbarIsland.Breadcrumbs.cs` restores the original location text. Loaded-root ancestry navigation and stale-context guards remain.

## Verification and continuation

Solution build clean with zero warnings/errors using `%TEMP%\MediFiler3WinUiBuild`. 411 unit/filesystem tests and 234 full native checks passed, zero failures/skips. Full log `%TEMP%\MediFiler3WinUi-full.log`; artifacts `%TEMP%\MediFiler3Integration-44ab01db94b243eda3500f2b5049cdc4`. Existing Reader pixel/fraction/wait assertions are unchanged. Native tests exercise AppWindow color readback/extension/title dragging and actual pager next-button/NumberBox interaction. A preceding focused UI/Gallery run passed 44 checks. `git diff --check` passed.

An early migration run had a native CoreMessaging shutdown failure; a later Reader run had a temporary no-settled-image failure, and another exposed the now-guarded detached-view exception. Final full verification exited normally. Preserved diagnostics and remaining uncertainty are in PROJECT_STATUS; do not infer universal crash closure. Physical caption/pager/keyboard/DPI review remains pending, alongside existing environmental limits.

Current branch `master`, HEAD `dc893cc` (`UX fixes`). Breadcrumb work was already uncommitted at the start of this task; preserve it with the new migration changes. No commit/push or user-app restart. Keep six authoritative documents and preserve AGENTS user edits. Search/depth-palette editing remain deferred; selection-clear latency remains accepted. Next step is user review of this UI change.
