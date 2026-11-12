# Continuous Reader handover

Updated 2026-11-12. The user approved slices 1-4. Shared Reader state, the lazy native page surface and persisted Reader settings are implemented. Slices 5-6 require separate authorization; Gallery 5.5 acceptance remains recorded in [PHASE5_ACCEPTANCE.md](PHASE5_ACCEPTANCE.md).

## Implemented scope

- Reader is a third sidebar destination sharing the existing root, active folder, naturally ordered file list and current file. Its separate image projection never removes unsupported files from that shared list. Enter at the current image, otherwise the next supported image, then the previous image if no next image exists. Do not cross folders.
- One file is one page: JPG/JPEG/JFIF, PNG, BMP, GIF, TIF/TIFF and WebP. Animated GIF and multipage TIFF use their first frame. Decode failures retain named error placeholders so other pages remain usable. Empty image projections show an explicit empty state.
- Native WinUI ScrollViewer with a virtualized Canvas realizes visible pages plus one previous page and the configured upcoming pages (default two). Defaults are fit width, preserved aspect ratio, zero page spacing, two upcoming pages and a centered column with the page counter on. Scaling and width percentage are in the top-right Reader toolbar; spacing, preload and counter visibility remain in Settings. Page identity follows a reading line 25% down the viewport.
- Keep an in-memory anchor consisting of the image path and fraction within the image at the viewport top. Restore it through size/metadata changes and inactive-view return. Actual wheel/pointer/key interaction takes precedence over a pending restoration. Sharing the current file does not start Browse playback or carousel work.
- Decode source images independently of the shared Windows thumbnail cache. The infrastructure service opens a disposable stream, respects EXIF orientation and decodes at the requested DPI/display width, without source upscaling. Width requests are bucketed to 128 pixels and capped at 8192. Decoded output is capped at 4,194,304 pixels (16 MiB BGRA per page); two jobs run at once. The native decoded-image LRU is capped at 128 MiB. These are output/cache bounds, not a guarantee on total process or internal codec allocations.
- Leaving Reader cancels/removes realized rows and rejects late decode/current-page publication, retaining only lightweight geometry and the bounded warm cache. Returning restores the anchor. Shutdown releases the island. Existing WPF folder/shell overlays use HWND exclusions; pinned tree width contributes to column geometry.
- Current Reader file move, rename, recycle and Undo/Redo are disabled and guarded pending slice 5. Existing folder menus retain their existing safeguards. Folder/root navigation and baseline refresh/watcher invalidation continue through existing services; detailed operation-aware anchor reconciliation remains later work. Browse and Gallery retain their existing commands and presentation.

## Code map

- `Application/Navigation/ReaderSessionState.cs`: image projection, path/fraction anchor, prefix geometry and visible/preload window; no decoded pixels.
- `Application/Files/IReaderImageService.cs` and `Infrastructure/Files/FileSystemReaderImageService.cs`: bounded cancellable source first-frame decoding.
- `App/Interop/WinUiReaderIsland.cs`: native scrolling, lazy rows, decoded LRU, indicator, clipping and async lifetime validation. A Canvas with explicit row realization avoids introducing another native observable collection notification path.
- `App/Views/BrowseView.Reader.cs`: shared navigation and active-view adapter. MainWindow/navigation rail/composition root wire the third mode. Existing Browse/Gallery adapters explicitly suppress inactive viewer/carousel work.
- `tests/MediFiler3.Infrastructure.Tests/ReaderTests.cs`: 32 projection/geometry/window/mode/source-decode/cancellation/settings/navigation cases.
- `tests/MediFiler3.App.Integration/ReaderChecks.cs`: seventeen actual-island checks, including rendered pixels, format fixtures, recoverable corrupt pages, large-sequence bounds and inactive/stale work.

## Remaining slices and verification limits

5. Reader organization/history commands, operation-aware anchor/current-page reconciliation and comprehensive live-change behavior. Keep existing overwrite/recycle/journal/compensation rules.
6. Reader acceptance, real-library/scale measurements and physical input/accessibility/DPI/fullscreen review.

Do not treat Reader as fully accepted. Physical wheel/touchpad/key focus, Narrator, mixed DPI, fullscreen/tree-overlay feel, real large libraries, locked/remote/removable files, every codec/orientation/animation variant and process-wide memory/performance remain unverified. In particular, the deterministic 10,000-page check demonstrates lazy row/decode bounds rather than real-image throughput. Prior native Gallery crash history and large-selection clear latency remain recorded limits.

## Verification

Reader checks: seventeen passed in the full native suite; the generated-fixture focused run also passed seventeen checks. The full runner additionally requires supplied codec/media fixtures; use the build/test commands in [PROJECT_STATUS.md](PROJECT_STATUS.md) with the same temporary output path. Do not rebuild concurrently in shared obj directories or replace/restart the user's running app. Final verification: clean build; 359 unit/filesystem tests and 209 full native checks passed with zero failures/skips. Full evidence/log paths are in current status.

The starting tree already contained uncommitted Phase 5.5 documentation, archives, scale JSON and Gallery test diagnostics. Preserve these alongside Reader work. No commit or push was requested.

## Reader image-worker crash follow-up

The reported drag/right/double-click crashes were investigated using three local minidumps and matching Microsoft public symbols. All failing thread stacks contain `AsyncCopyToSurfaceTask::CopyOperation` / `Execute` and the WER reports record `RO_E_CLOSED`. Reader closed a `SoftwareBitmap` after `SoftwareBitmapSource.SetBitmapAsync` while deferred native rendering could still need it. Reader now copies the bounded decoded BGRA bytes into a XAML-owned `WriteableBitmap` pixel buffer, removing that disposable-input lifetime dependency. Page/file cancellation, cache bounds and shared navigation are preserved. Image tooltips have been removed; accessibility names remain.

The additional native regression renders 1536x2048 pages through repeated resize/scroll, forced collection, invalidation, warm return and cache pressure, plus targeted pointer messages to the hidden runner island. It asserts retained image pixels, no tooltip and no file/mode mutation. The user reports that the crash appears fixed after interactive use; broader physical/DPI/accessibility coverage remains open. Exact dump identifiers, symbolized addresses, analysis method and limits are retained in [the investigation record](verification/reader-crash-2026-11-11.txt). This does not close the earlier unrelated native Gallery crash history.

The follow-up also preserves the anchor while native ChangeView is pending. Previously a reattached old offset could be interpreted using newly measured page sizes and replace the intended path. Realization now uses the intended viewport until restoration has actually applied; pointer/key input still supersedes pending restoration. The return regression waits for restored native layout and reports the expected/actual anchors on failure.

## Slice 3: persisted settings and shared counter style

The top-right Reader toolbar exposes Resize to width / Resize to height. Width mode also shows a 10-100% slider (step 1, default 100%). Settings exposes Page spacing (0-64 logical pixels, step 4), Upcoming pages to preload (0-6) and Show page counter. Defaults remain fit width / 0 / 2 / on. Fit height keeps each page within the available height and width, preserving aspect ratio and centering narrower pages; there is no horizontal clipping or new horizontal scrolling. A 24px minimum placeholder/row height remains for degenerate very wide images.

ApplicationSettings stores the Reader fields in its existing versioned JSON payload: older payloads receive the MVP defaults, existing preferences are retained and the SQLite schema remains 7. Existing debounced writes and shutdown flush apply. Startup applies saved Reader options before presentation. Settings changes do not start decoding in inactive Reader. Active spacing/scaling changes retain the file/fraction anchor, including a normalized gap fraction when the viewport top lies between pages. Spacing is absent after the final page. Preload changes cancel rows outside the new window; the previous-page allowance remains one.

Per-page decode widths follow displayed size and DPI, retaining the pixel/job/cache bounds and the XAML-owned WriteableBitmap crash fix. Width-bucket survivors can reuse warm pixels. Counter visibility changes keep existing image objects and do not restart decoding.

Reader and Gallery now use one OutlinedStatus factory: white semibold foreground, eight black one-pixel outline copies, bottom-right alignment and margin (8,8,24,8). It is noninteractive and has one polite accessibility label; outline copies use Raw accessibility view. Factory rebuilds replace the outline list, avoiding stale copies across native content reattachment. Reader image tooltips remain absent.

Ten additional unit cases cover scaling/spacing/gap anchors, configurable preload bounds, invalid settings and legacy/default plus real SQLite restart persistence. Two additional native checks drive the actual Settings controls, inactive-work boundary, file/fraction return, counter styling/visibility and unchanged image identity. Physical Settings/focus/DPI/Narrator review remains pending.

Slice 3 final verification: clean build (zero warnings/errors); 343 unit/filesystem and 201 full native checks passed, zero failures/skips. Focused Reader: nine passed. Evidence: `%TEMP%\MediFiler3Integration-801c4582dec2424d95539d68f1c798ff`; log `%TEMP%\MediFiler3Reader-slice3-full.log`. Prior Gallery acceptance evidence and crash investigation remain preserved. Next feature slice is 4 (explicit Reader navigation/accessibility), pending separate authorization.

## Reader toolbar sizing follow-up

Scaling has moved out of Settings into the contextual top-right Reader toolbar. Two native toggle buttons offer Resize to width / Resize to height using the Switch (E8AB) and Sort (E8CB) glyphs. The width slider and percentage label sit to their left. Only width mode shows the 10-100% slider and live percentage label; narrower images remain centered. Percentage applies to the same available image column as before (excluding existing tree/padding reservations), so 100% preserves the former full-width default. Resize to height retains its full-page fit and ignores, but remembers, the saved width percentage.

ReaderWidthPercent extends the existing settings JSON with a default of 100 for older payloads; schema remains 7. Toolbar and Settings share an updated options snapshot so changing spacing/preload/counter later cannot overwrite scaling or width. Controls appear only in active Reader; hidden/stale native edits are guarded. Width reflows retain the image/fraction anchor subject to normal end-of-sequence scroll limits, retain centered geometry, cancel obsolete decode rows and reuse valid width buckets. Decoder/cache bounds, owned pixels, missing/corrupt placeholders and operation guards remain unchanged.

Five additional unit cases cover 10/50/100% geometry, height-mode independence and invalid limits; persistence coverage includes a non-default percentage and legacy defaults. A new native check covers toolbar context, slider endpoints and label, centering, anchor reflow, height/width switching, SQLite reopen and Settings snapshot synchronization. Focused Reader now has ten checks. Physical slider/focus/mixed-DPI review remains pending.

Toolbar follow-up verification: build clean (zero warnings/errors), 348 unit/filesystem tests and 202 full native checks passed with zero failures/skips; ten focused Reader checks passed. Full evidence: `%TEMP%\MediFiler3Integration-0e76cd06a9b548fea936661239b42779`; log `%TEMP%\MediFiler3Reader-toolbar-full.log`. Physical toolbar/pointer/focus/DPI acceptance remains open.

## Reader buttons and tree pin shortcut (2026-11-12)

The Reader sizing dropdown is replaced by two mutually exclusive native AppBarToggleButtons, keeping the active mode selected when pressed again. Switch (E8AB) selects horizontal/width sizing; Sort (E8CB) selects vertical/height sizing. The 10-100% slider and live label precede both buttons and remain visible only in width mode. The Reader sidebar destination uses ReadingMode (E736). Persistence, centering, anchored reflow and rendering bounds remain unchanged.

F3 toggles the existing tree Pin/Unpin action in Browse, Gallery and Reader, independent of WPF/native control focus, through the existing window keyboard routing. The same pin setter updates the button, glyph, panel behavior and persisted FolderTreePinned preference. The tooltip and accessibility accelerator expose F3. Auto-repeat is consumed without toggling repeatedly; owned/disabled dialogs, active Gallery dragging and stale page/mode changes reject the action. Home and Settings do not operate the hidden tree. File/current-page changes within the same view do not cancel a queued pin action.

The toolbar regression now uses the actual native Toggle automation pattern and checks glyphs, active states, slider order and sidebar icon. One additional native check covers Ctrl+T across all three views, control focus, exact modifiers, repeat suppression, dialog guards, stale page changes and SQLite reopen. Physical native-key travel and mixed-DPI/fullscreen review remain open.

The render stress probe now captures a settled, still-realized visible row and samples its visible area, with a bounded wait for native frame completion. Capturing an offscreen preload or a row removed during RenderTargetBitmap could previously report blank pixels during full-suite timing. Permanent missing pixels still fail with sample/iteration diagnostics; rendering production code is unchanged by this probe adjustment.

Buttons/Ctrl+T final verification (2026-11-12): build zero warnings/errors; 348 unit/filesystem tests and 203 full native checks passed, zero failures/skips. Focused Reader: eleven passed. Full evidence: `%TEMP%\MediFiler3Integration-2f15c1d3ba0f40c48110c2365ae79dee`; log `%TEMP%\MediFiler3Reader-buttons-full.log`. Prior acceptance evidence and physical limits remain recorded.

Shortcut follow-up: F3 (no modifiers) replaces Ctrl+T for tree pin/unpin. Tooltip and accessibility accelerator match F3. The existing native guard/persistence check now exercises F3 and rejects the former Ctrl+T binding. Prior Ctrl+T verification entries above are historical.

F3 shortcut follow-up: clean build and all eleven focused Reader/native checks passed, zero failures/skips. The old Ctrl+T binding is rejected. Full suites were not repeated for this key-binding-only change; the full-suite evidence above predates it. Log: `%TEMP%\MediFiler3Reader-F3-focused.log`.

## Slice 4: navigation and accessible recovery (2026-11-12)

The Reader toolbar now has Previous image, Next image, Go to page/file and Open in Browse actions in the center command group while Reader is active. They replace the disabled file/history buttons for this slice, while Add folders/F5 and the existing top-right sizing controls retain their layout. Browse/Gallery restore the existing file/history group. Availability follows the Reader image projection, including the empty state and sequence ends. Go to opens an owned WinUI ContentDialog that accepts a one-based page number, exact image filename or exact full image path from the current projection. Invalid/ambiguous queries keep Go disabled and show an accessible explanation. Primary activation revalidates the input. Cancel/Escape do not navigate; a changed root, folder, projection or viewing page rejects the result. There is no filesystem lookup or folder crossing.

| Reader input | Action |
|---|---|
| Left / Right | Previous / next image, clamped to this sequence |
| Up / Down | Scroll 48 logical pixels |
| Page Up / Page Down | Scroll 90% of the viewport |
| Shift+Space / Space | Scroll up / down 90% of the viewport |
| Home / End | Start / end of this sequence |
| Ctrl+G | Go to page or file |
| Ctrl+R | Retry the current page from its source |
| Enter | Open the current image in Browse |
| F3 | Existing tree pin/unpin shortcut |

Reader commands route through the existing WPF/native window shortcut path, independent of control focus. Modal/disabled-owner and view guards remain; the jump dialog retains text input while open. Repeated navigation keys remain usable, while the existing one-shot command repeat policy applies to dialogs and F3. Undefined modifier combinations retain their existing behavior. Browse/Gallery routing remains unchanged.

Page/file jumps explicitly select their requested image even when many small images fit in the viewport or the offset clamps at the sequence end. Non-scrolling clicks/Tab retain that choice. Actual scrolling returns to the existing 25%-viewport reading-line rule; pointer/key interaction cancels obsolete pending restores. End remains at the end as nearby image dimensions arrive. Resize/settings reflow retains image/fraction/gap anchors and the bounded warm cache. Explicit selection, view callbacks and error actions validate current membership/activity before publication.

Failed pages provide native Retry and Open in Browse buttons with file-specific automation names. Retry invalidates only the selected source page and leaves unrelated decoded images warm. Open in Browse reuses the shared current-file/navigation handoff without performing any file operation. Detached/cancelled rows and inactive contexts cannot run delayed actions. Loaded image peers expose page number, count and filename; placeholders expose the failure and recovery actions. The outlined counter announces the current page/filename and the reader scroll surface exposes keyboard help. Image tooltips remain absent.

File move/rename/recycle/history remain guarded for slice 5. There is no schema change, cross-folder navigation, external launch, bulk rename or new file operation. Source decoding, job/pixel/cache bounds, XAML-owned WriteableBitmap crash fix, F3, Switch/Sort sizing, width percentage and counter appearance remain intact.

Code additions: Application/Navigation/ReaderNavigation.cs (bounded navigation/query resolution), App/Interop/WinUiReaderIsland.Navigation.cs (native navigation and guarded recovery), WinUiToolbarIsland.ReaderDialog.cs (owned validated jump), MainWindow.Reader.cs (context validation/toolbar state), and adapter/shortcut wiring. Eleven additional unit cases cover page/file validation and boundaries/geometry. Four additional native checks cover focus-independent commands and toolbar/accessible peers; actual jump dialogs including Ctrl+G, cancellation, stale lists/views and modal guards; retry/Browse recovery and stale actions; 10,000-page rapid jumps, bounds and small-page selection. The dialog test driver can await native validation events rather than assuming synchronous TextChanged delivery.

Physical keyboard travel, Narrator output, touchpad feel, mixed DPI/fullscreen and real-library performance remain manual acceptance. Passing automation does not close the prior native Gallery crash history. Next feature slice is 5 (Reader organization/history and operation-aware live reconciliation), pending separate authorization.

Slice 4 final verification: solution build zero warnings/errors; 359 unit/filesystem tests and 207 full native checks passed with zero failures/skips. All fifteen Reader cases are included. Final full evidence: `%TEMP%\MediFiler3Integration-900937304f3f44b9af3720f81e724209`; log `%TEMP%\MediFiler3Reader-slice4-full.log`. Diff formatting and referenced documentation links pass. Earlier verification entries remain historical. No commit/push was requested; prior uncommitted Phase 5.5/Reader work remains intact.

## Reader page picker and Browse F4 follow-up (2026-11-12)

The user chose a numbered filename list for clickable jumps. The owned Reader Go dialog now retains typed page/exact-filename/full-path entry and adds a native, fixed-height ListView of page number plus filename. Clicking/selecting a row fills the page field; Go confirms and Cancel/Escape retain the current location. Valid typed input selects and reveals its row. Native virtualization keeps realized containers bounded for 10,000 entries; there are no image thumbnails or filesystem probes in the picker. The immutable choice snapshot retains shared natural ordering, and existing post-dialog root/folder/projection/view validation rejects stale results. Versioned deferred reveals stop after closure or newer input.

Removed Reader's Open in Browse toolbar and error-placeholder buttons. Retry remains on failed pages. The existing Enter shortcut and sidebar Browse handoff remain available. Gallery's Open in Browse actions are unchanged. A native NavigationViewItemSeparator visually separates Home from Browse/Gallery/Reader without adding a selectable destination; ReadingMode and the existing icon/keyboard/pin behavior remain.

The F4 crash was a separate managed exception: synthetic KeyEventArgs lacked RoutedEvent, so ViewerHostView.HandleKey threw while setting Handled. The adapter now supplies Keyboard.PreviewKeyDownEvent. This also repairs the same route for image zoom and media Space. The [F4 investigation record](verification/f4-crash-2026-11-12.txt) retains the Windows event/stack evidence. There is no blanket exception suppression or change to text scrolling semantics.

Two additional native checks cover forwarded F4/zoom/playback and the 10,000-entry picker (rendered labels, row selection, typed reveal and container bounds). Existing jump cancellation/stale-context and Reader recovery checks remain, with Enter handoff replacing removed buttons. The dialog driver preserves original failure stacks. Physical pointer/key/Narrator/DPI acceptance remains pending.

Picker/F4 follow-up final verification: build zero warnings/errors; 359 unit/filesystem and 209 full native checks passed, zero failures/skips. Focused Reader: seventeen passed. Full evidence: `%TEMP%\MediFiler3Integration-0a8cda95903e4c2db3df98f644d7fcb9`; log `%TEMP%\MediFiler3Reader-picker-full.log`. Prior verification entries remain historical; physical/Narrator/DPI limits remain.
