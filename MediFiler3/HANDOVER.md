# Handover

Updated 2026-11-15. Latest task: full-height tree with scrollable trailing spacing; sidebar-only hidden tree reveal in Gallery.

## Delivered behavior

- Removed the fixed TreeBottomClearance grid row. The TreeView and scrollbar now use the full available height. TreeScrollClearance is 175px (CarouselOverlay.Height) in Browse with carousel enabled, 48px otherwise. TreeFooterMarginConverter applies that trailing margin only to the last visible root container, after its whole subtree, so it contributes to real scroll extent. No fake folder nodes or nonvirtualizing wrapper panels.
- BrowseView.TreeFooter observes roots and root Hide changes to maintain TreeFooterRoot; removing/hiding roots transfers the footer appropriately. Root subscriptions detach at StopBackgroundFolderCounts. Full tree virtualization/recycling remains enabled.
- FolderRevealBounds in Gallery uses only the sidebar while the unpinned tree is hidden; hovering Gallery files cannot open it. Once shown, its combined sidebar/tree bounds retain it for interaction. Pinned trees and Browse/Reader retain their previous full-width behavior. The same computed bounds feed normal WPF/native probes and fullscreen coordination.
- Previous carousel full-footprint reveal remains, as do Browse Home/End endpoint shortcuts, filename/color rules, folder rule action precedence/Hide/root exclusions and Reader read-only/scroll restoration safeguards.

## Verification / continuation

Build clean, zero warnings/errors. 43 focused and 233 full native checks passed, zero failures/skips. Logs %TEMP%\MediFiler3TreeFooter-final.log and MediFiler3TreeFooter-full.log; full artifacts MediFiler3Integration-6b04413995174d9da49882b0d2b0ca15. Build output %TEMP%\MediFiler3FolderRulesBuild. Geometry checks verify that the viewport reaches the bottom while the actual final row scrolls above the carousel hover area in a 10,102-node tree; Gallery sidebar opening/retention, native clipping, recycling/fullscreen, drag and Reader pixel/fraction regressions passed. Application/business code was unchanged; unit suite was not repeated (prior filename/color baseline: 411 passed).

Physical OLE drag, exhaustive Narrator/DPI/display/storage testing remain documented limits. Earlier Gallery dwell timeout remains independently unresolved in PROJECT_STATUS; this successful full run does not establish its root cause. Native caption tests check accepted DWM writes because caption-color readback is rejected in the test host; file-color behavior is retained.

Current branch master, HEAD 736fadc (Minor UI adjustments), observed during this task. Current footer/Gallery-reveal changes remain uncommitted; preserve the working tree and inspect actual Git state. No commit/push or user-app restart. AGENTS unchanged: SHA-256 60bf6cfcbc3e37a3a278a9942803ce0f23b26d3420c8e006f45c91a2f08d80a3. Keep six authoritative documents. Tests use isolated fixtures; next feature is unselected. Search/depth-palette editing remain deferred and selection-clear latency remains accepted.
