# Product specification

This is the current product contract and decision record. Requirements below supersede older experiments. Items explicitly marked **deferred** are not authorization to implement them. Implementation and resource mechanics belong in [ARCHITECTURE.md](ARCHITECTURE.md); milestone scope belongs in [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md).

## 1. Purpose and platform

MediFiler3 is a local Windows desktop application for browsing, viewing and safely organizing personal file/media collections in one workflow. Prioritize immediate navigation, predictable context-sensitive input, responsive large-folder handling and preservation of user data. Core use must not require a server/cloud service.

Use C# with a WPF application/window host and suitable WinUI controls through Windows App SDK XAML Islands. Retain the native Windows caption/system buttons. Additional dependencies and feature scope require an explicit reason; use the existing architecture rather than redesigning it from this document.

## 2. Workspace and navigation

| Concept | Required behavior |
|---|---|
| Roots | Load one or more real folders as separate roots; never merge their files into a synthetic listing. Add folders, Home activation and folder drops use the same load workflow. |
| Hierarchy | Publish shallow roots promptly; discover descendants/counts progressively with cancellation. Do not recurse through junctions/symbolic links or owned backup storage. Preserve accessible results and report failures. |
| File listing | Direct files of the active folder only, excluding Windows Hidden/System attributes. Include unsupported extensions. Use shared Windows Explorer-style natural filename order (`page 2` before `page 10`); other sort/filter choices are deferred. |
| Shared context | Browse, Gallery and Reader share loaded roots, active folder, full file list and current file. Switching modes does not rescan roots or start inactive rendering/playback. Mode/root/folder changes clear mode-local selection; Home/Settings retain Gallery selection and Reader anchors. |
| Folder selection | Left-click navigates. Explicitly clicking the already active row reloads that listing and starts at its first file/first Reader image, clearing shared current/selection while loading. Internal selection notifications, F5 and reconciliation retain their separate position-preservation rules. Menu buttons/expanders are not navigation resets. |
| Browse boundaries | Five consecutive attempts beyond the same edge enter the adjacent nonempty folder in loaded tree preorder, skipping empties. Forward entry selects first file; backward entry selects last. Successful navigation/direction change resets the counter. Reader never implicitly crosses folders. |
| Current title | Native title is `(position/total) filename`, using the full folder listing; application name when no current file. |
| Counts | Prefix folders with `(direct/total) - Name`. Count unsupported files but exclude Hidden/System files and untraversed links. Unknown values show `?`; failures/incomplete discovery must not appear complete. Update both sides after moves/history. |

The tree is independently scrollable and expanded as descendants first arrive, retaining later user collapse. Unpinned reveal uses one continuous area spanning the sidebar and the tree's full pinned width, including while the tree is hidden. Use the same horizontal bounds to open and retain it in normal/fullscreen modes; hide after leaving that area. There must not be separate sidebar/content-edge reveal hitboxes. Pin state persists; pin reverses button/icon colors. Full-width rows, selected highlighting and bottom clearance keep the last row reachable above the carousel. Tree resizing/persisted user-adjustable width is **deferred**; current width is fixed.

Folder menus provide New, Rename, Move to, Recycle, Collapse child folders, Refresh tree, Explorer and Properties. Collapse child folders changes only direct children's expandos, retaining the clicked folder's expando and deeper expansion state. Favorites belong in Home. Copy-path menu and further tree customization are deferred.

## 3. Views, toolbar and controls

The compact icon-only sidebar contains Home, a separator, Browse, Gallery and Reader, with Settings at the bottom. There is no navigation-pane expand toggle. Toolbar/sidebar backgrounds are black. Use a full-width 48px toolbar with left context/metadata, centered commands and right contextual controls; primary AppBarButtons are 32px, with overflow hidden. The viewer fills the remaining canvas. WPF tree/carousel overlays remain reachable over native surfaces.

### Current command visibility

| Action/control | Browse | Gallery | Reader |
|---|---|---|---|
| Add folders / Refresh | Available with appropriate context | Same | Same |
| Rename current/selected file | Available | Exactly one selected file | Temporarily hidden |
| Recycle | Current file, no confirmation | One selected: no confirmation; multiple: confirm | Temporarily hidden |
| Undo / Redo | Active-root history | Active-root history | Temporarily hidden |
| Default application / Choose an application | Visible; disabled when unavailable/busy/no file | Hidden, including separator | Temporarily hidden |
| Open current Reader image in Browse | Not a Reader toolbar action | Gallery handoff uses Enter/double-click/context menu | Temporarily hidden |
| Image zoom / text / media controls | Follow current content capabilities | Hidden | Hidden |
| Width / Height thumbnail sliders | Hidden | Available | Hidden |
| Previous image / Next image / numeric Go / Reader scaling | Hidden | Hidden | Available |
| Failed-page Retry / Open in Browse recovery | Not applicable | Not applicable | Retained |

Reader is read-only for files. Destination-folder right-clicks and move/rename/recycle/Undo/Redo APIs do nothing in Reader. Folder-tree folder commands remain available for consistency. External/Browse recovery remains implemented; hidden controls and dormant shortcuts must not be restored incidentally. Gallery file operations remain available.

### Keyboard contract

Shortcuts apply in their view regardless of viewer/tree/toolbar focus. Modal dialogs retain text editing/confirmation input. Active native drag owns Escape/modifiers. Preserve native Gallery Ctrl/Shift navigation and focus semantics.

| Input | Browse | Gallery | Reader |
|---|---|---|---|
| Arrows | File navigation when interaction policy permits | Native grid movement/ranges; route from other controls to grid | Left/Right previous/next image; Up/Down scroll 48 logical pixels |
| Wheel | Navigate files, or image zoom/text scroll in interaction mode | Native gallery scroll | Continuous vertical scroll |
| Enter | Native/control behavior | Open focused file in Browse | Global handoff shortcut dormant |
| Ctrl+Enter / Ctrl+Shift+Enter | Default application / native chooser | No added external-open binding | Dormant |
| F2 / Delete | Rename / recycle current | Rename one / recycle selection | Dormant |
| Ctrl+Z / Ctrl+Y | Root-scoped Undo / Redo | Same | Dormant |
| Ctrl+A / Escape | Native/content behavior; fullscreen Escape exits | Select all / clear selection; fullscreen Escape exits | No Gallery selection behavior; fullscreen Escape exits |
| Page Up/Down, Home/End, Space | Content interaction where defined; Space toggles playback | Native grid behavior | 90% viewport up/down; sequence start/end; Space down, Shift+Space up |
| Ctrl+G / Ctrl+R | No Reader action | No Reader action | Numeric Go / retry current page |
| F3 / F5 / F11 | Pin tree / fresh refresh / fullscreen | Same | Same |
| F4 | Toggle text scrolling when text is current | No text action | No text action |
| Numpad +/- | Image zoom by multiplicative 25% steps, 20%-500% | No image zoom | No Browse zoom |
| Ctrl+Shift+N | New subfolder in active folder | Same | Same |

Reader dormant mappings are Enter, F2, Delete, Ctrl+Z, Ctrl+Y, Ctrl+Enter and Ctrl+Shift+Enter, in Reader only. Do not globally disable Browse/Gallery bindings. Configurable shortcuts/access keys beyond this contract are deferred.

F11/rightmost toolbar button enters borderless fullscreen on the current monitor. Reveal caption/toolbar at the top, sidebar plus offset tree at the left, and enabled carousel at the bottom. Retain title-bar interaction across nonclient boundaries, selection/playback/interaction state and normal/maximized restore bounds. Fullscreen has no carousel peek and is not persisted; Escape exits. Closing fullscreen preserves the previous normal restart layout.

## 4. Browse content and format matrix

Extension routing is case-insensitive, not a universal codec guarantee. Decode failures retain a clear fallback and Browse external-open actions. Gallery can show Windows thumbnails/placeholders for every listed type; Reader projects images only.

| Content | Routed extensions/names | Browse behavior | Reader / limitations |
|---|---|---|---|
| Images | `.jpg`, `.jpeg`, `.jfif`, `.png`, `.bmp`, `.gif`, `.tif`, `.tiff`, `.webp` | Aspect-preserving fit; viewport/source-aware detail; zoom/pan; GIF animation | One source file/page; first frame of GIF/TIFF, static WebP. WebP requires installed WIC codec. Animated WebP is deferred. |
| Video | `.mp4`, `.m4v`, `.wmv`, `.webm`, `.mkv` | Windows-backed embedded playback, paused on open | Not projected. Installed codecs determine variants. MKV fixture coverage is only WebM/Matroska subset, not arbitrary tracks/subtitles/attachments. |
| Audio | `.mp3`, `.wav`, `.wma`, `.m4a`, `.aac`, `.opus` | Paused playback, title/artist/artwork when available | Not projected. Verified WAV is PCM and AAC fixture is ADTS; FLAC is deferred. |
| Text | `.txt`, `.md`, `.log`, `.csv`, `.json`, `.xml`, `.yaml`, `.yml`, `.ini`, `.cfg`, `.config`, `.cs`, `.xaml`, `.html`, `.htm`, `.css`, `.js`, `.ts`, `.sql`, `.ps1`, `.sh`, `.srt`, `.nfo`, `.toml`; extensionless `README`, `LICENSE` | Read-only selectable plain text, no execution or syntax-specific parser | Not projected. Strict UTF-8 (optional BOM), BOM-marked UTF-16/32 LE/BE; malformed/binary-looking input errors. |
| Other/missing/corrupt | Other extensions, failed integrated content | Unsupported/miscellaneous files show Windows thumbnails, including generic/type icons supplied by Explorer, alongside the no-integrated-viewer message and external-open recovery. Missing/unreadable files retain a placeholder. | Missing/corrupt supported images keep Reader placeholders so the remaining sequence works. |

### Content interaction

| Content | Default wheel | Right-click | Additional contract |
|---|---|---|---|
| Image | File navigation | Toggle zoom interaction | Wheel zoom around pointer in zoom mode; left-drag pans above fit. Reset on a new file or return to navigation; show available thumbnail while source loads. Do not upscale small sources merely to decode. |
| Audio/video | File navigation | Play/pause | Space and toolbar also toggle playback; interactive playback controls do not navigate files. |
| Text | File navigation | Toggle document scrolling | F4/toolbar alternative; carousel still navigates. Keep text window/scroll on Browse return, reset on another file. |
| Unsupported | File navigation | Native Open With chooser | Works with or without thumbnail; ignore empty/inactive context and duplicate requests. |

GIF playback honors frame delays, transparency, offsets/disposal and embedded loops, and releases the source file after loading. Zero/absent delays use 100ms. Duration metadata is one effective cycle, independent of loop count. Zoom/resize must not restart animation. Reader intentionally displays only its first frame.

Media provides play/pause, stop, +/-5-second jumps, stretching seekbar, time display, and compact horizontal volume/mute popup. Right-clicking the volume button toggles mute without opening it. Volume/mute are shared/persisted independently; mute preserves level. Collapse absent artwork without an empty slot. Open paused; release media before mutations/history/navigation/deactivation/shutdown and reopen paused after return or cancelled/failed operations. Opening times out after 30 seconds. No bundled codec runtime or playback-speed control is planned.

Text reads at most 1 MiB per window without splitting Unicode code points; previous/next-window controls handle larger files. Center the text with symmetric insets allowing for the tree. Word counts count whitespace-delimited runs and explicitly label a partial window `(window)`, rather than implying a whole-file total.

Metadata is shown only in Browse's left toolbar: top line for values, bottom line for full folder path with only the final folder name white. File size uses 1024-based KB/MB/GB, one decimal digit and a period; image dimensions show original canvas resolution, video resolution shows pixel height (`[1080p]`), and duration is bracketed (`[00:14]`, `[1:02:05]`). Clear stale metadata immediately; retain known dimensions during same-file zoom/resize. Unknown values are omitted. Richer metadata and decimal-separator options are deferred.

Browse's external actions use the selected path and owner window, prevent duplicate in-flight requests, treat chooser cancellation normally and publish results only in the matching active context. Resolve default-app icon/name asynchronously; unknown lookup uses a generic icon/label. A known missing association disables Default but keeps Choose available. Do not automatically launch a second action on failure.

## 5. Carousel and Gallery

### Carousel

Browse's horizontal carousel shows the sorted neighborhood around current, centered with spacers representing omitted files. It has square 150px cropped tiles, one-line names, no scrollbar, stable surviving items and direct click navigation. Double-click enters Gallery and highlights that file. Rapid navigation must not lose steps while visual updates coalesce; entering/departing edge tiles fade and honor Windows animation preference.

The carousel normally peeks at one-eighth height and 65% opacity, revealing near the bottom; disable removes the carousel, peek, reveal target and its thumbnail requests without disabling Browse navigation. Density is an odd total count including current, 1-25 in steps of 2, default 11. Peek/size/reveal customization beyond current controls is deferred.

### Gallery appearance and selection

- Independent Width/Height sliders: 100-400 logical px, step 50, default 150; persist separately. Slider width is 150px. Center rows, use 2px gaps and reflow beside a pinned tree.
- Idle image framing is centered square crop followed by rectangular tile fill. Hover fits the full original aspect and hides the bottom gradient filename; leaving restores crop/caption. No manual blur or display-only pixel filter.
- Selected tiles and carousel current have a solid square 5px `#FF1493` border, no pattern/secondary stroke. Counter uses white semibold text with black outline, bottom-right clear of scrollbar, one accessible status label. Carousel/fading images have square corners.
- Retain the grid, source and unchanged previews across visits. On return, reveal shared current after layout; preserve an explicitly cleared selection and let newer input supersede pending reveals.
- Native Extended selection owns Ctrl-click, Shift range, Ctrl+Shift and focus-only movement. Alt-hover paints add/remove once per path; the first tile chooses the direction. Reset on release/exit/deactivation/context change. Ctrl-hover does not paint.
- Home/Settings retain selection; root/folder/mode changes clear it. Reconciliation prunes removed members without reselecting survivors automatically. Context menu: Open in Browse, Select all, Clear selection; selected target keeps the set, unselected target replaces it, blank Open is disabled. Commands reject stale context. Double-click/Enter hands current to Browse without rescan.
- Selected-file moves/recycle/history are grouped. Rename is single-selection only and preserves extension; **bulk rename is not planned**. Single-file recycle skips confirmation; multi-file recycle requires it. Do not silently mutate one current file instead of a selected batch.

### Internal Gallery drag

Dragging an unselected origin moves only it; a selected origin snapshots the full natural-order selection before native selection changes. Ordinary press/release and Ctrl/Shift clicks remain selection; start only beyond Windows drag thresholds. Show Move/count feedback, reveal tree and highlight valid destinations. Use cropped previews, 80% opacity, top-left pointer hotspot, at most three realized tiles; do not introduce hover letterboxing or offscreen preview loads.

Drop on a loaded tree folder invokes safe grouped moves. Reject same-folder, stale/foreign/blank targets; cancel changes nothing. Expand collapsed headers after 600ms hover without navigating, including same-folder headers to access descendants. Support edge and wheel tree scrolling during drag; scrolling resets dwell/highlight, and leave/drop/cancel/context change stops drag work. This is not Explorer file import/export. Folder drops for loading roots already work; external file import/export and cross-panel dragging are deferred.

## 6. Continuous Reader

- Vertically arrange the image projection with preserved aspect ratio and centered column. Fit width/height through Switch (`E8AB`)/Sort (`E8CB`) toggle buttons, width slider on their left; sidebar uses ReadingMode (`E736`).
- Defaults: width fit, 100% width, zero spacing, two upcoming pages, counter on. Width is 10-100% in steps of 1; height mode remembers but ignores it. Spacing is 0-64px in steps of 4; upcoming preload 0-6. Counter matches Gallery styling. Changes persist and preserve image/fraction/gap anchors subject to sequence-end bounds.
- Only realize visible pages plus a bounded prior/upcoming window; don't decode the whole folder. Retain a bounded warm cache, stop inactive work and reject late publication. Source pages are independent of thumbnail cache pixels.
- Current page follows a reading line 25% down the viewport during actual scrolling. Explicit jumps retain their requested image even when many small pages fit; clicks/Tab without scrolling do not casually override it. End stays at the tail through late dimension discovery.
- Go is a single focused/select-all numeric field limited to 1 through image count. Empty intermediate text disables Go; invalid/non-numeric/out-of-range edits are rejected. Go/Enter confirms; Cancel/Escape and stale root/folder/projection/view results do not navigate. Filename/path search, clickable grids/lists and their tooltips were rejected/superseded.
- Keep in-memory anchor path/fraction/gap during reflow/inactive return; this is not a persisted bookmark/automatic root reopening feature. Known owned file/ancestor renames follow mappings atomically. Removing current chooses next image, then previous at end, skipping unsupported files.
- Decode failures retain accessible named placeholders with Retry and Open in Browse recovery buttons. Loaded pages expose number/count/filename; decorative outlines are not duplicate live-region announcements. Image tooltips remain absent.
- Reader is read-only for files: move, rename, recycle and Undo/Redo are rejected by both UI and callable entry points. Folder-tree folder modifications remain available. External/Browse-handoff recovery remains implemented with toolbar actions/shortcuts dormant as listed in section 3.

## 7. Safe organization and history

Right-clicking a destination tree folder moves Browse's current file or Gallery's selected set; it does nothing in Reader. After removal choose the next file or previous at end. Undo selects restored files; Redo removing current advances naturally. Retain tree nodes, expansion and previews, and update counts. Folder menus act on the clicked folder's owning root.

| Operation | Required safeguards |
|---|---|
| File/folder rename | Edit file basename and reattach original extension; folder name fully editable. Recheck conflicts after dialogs. |
| New/move folder | Native destination picker starts at source parent; reject unsafe ancestry, links, type mismatches and unsupported volume combinations. |
| Recycle | Use Windows Recycle Bin; warn/confirm nonempty folder removal and multi-file removal. Never fall back to permanent deletion. Restoration is best effort. |
| Collision | Owned WinUI comparison cards, async thumbnail/size, paths/exact bytes on hover. Folder cards show recursive total file counts and summed file sizes for both Incoming and Existing, including Hidden/System files. Scan off UI with cancellation; show calculating/unavailable states and clearly label partial totals if entries are unreadable or links are skipped. Never traverse reparse points. Rename / explicit Overwrite / Cancel, default Cancel; disable unsafe Overwrite. Folder replacement is whole-tree, never merge. |
| Collision Rename | Apply next available numbered incoming name directly, e.g. `Test (1).png`; skip file/folder collisions, continue suffixes and recheck races. No extra rename dialog. |
| Overwrite | Preserve destination in verified same-volume managed backup storage, journal it with incoming action; one Undo restores both versions. Refuse if safe storage unavailable. No destructive overwrite shortcut. |
| Undo/Redo | Persistent independent stacks per root filesystem identity; actual tree ancestry defines ownership, including overlapping roots. Cross-root file moves belong to source root. A new operation invalidates redo only in its root. Verify source identity/type/location before acting. |
| History collision | Rename preserves the blocking existing item under another name so recorded paths remain usable; Overwrite also preserves it. Opposite history action restores displaced data. Choices do not bypass changed/missing source or unsafe deletion checks. |
| Batch/partial/cancel | Explicit per-item outcomes, cancelled flag and unattempted count. Ordinary failures may continue; cancellation/recovery uncertainty stops remaining work. Completed members form one history group. Never report failure/ambiguity as verified success. |
| Backup cleanup | Settings confirmation explains permanent removal of saved versions/affected history. Durably retire the whole dependent root/batch before deleting verified backups. Keep unrelated/current/restored/changed-identity/link/recovery data; report retained/failed items and allow retry. No automatic backup expiry. |

Prepared journal intent precedes mutation; successful status follows verification. Preserve partial/recovery evidence after interruption. Once data has been displaced or compensation begun, finish verification/rollback rather than abandon it halfway for cancellation. Cross-volume copy/delete must verify copy state and retain recoverable evidence.

Use dedicated same-volume app-owned storage: `%LOCALAPPDATA%\MediFiler3\UndoBackups` when on the destination volume, otherwise a hidden user-specific root folder on that volume. Do not fall back to sibling backups for new replacements; legacy backups remain usable and explicitly cleanable. Cache maintenance must never delete backups, sources, settings or journals.

## 8. Home, settings and persistence decisions

Home is always startup; restoring layout/settings must not reopen roots or start playback/watchers. Saved roots remain after disappearance/rename until the user chooses Locate or Remove; never infer a replacement automatically. Locate preserves counts/timestamps/favorite state and rejects key collisions. Remove affects all saved lists, never the filesystem.

| Home section | Ordering and meaning |
|---|---|
| Recent | Last explicit open descending, then path |
| Frequent | Explicit open count descending, last-opened descending, then path |
| Favorites | Name alphabetically, then path; root favorites only, shared stars in all lists |

Only successful explicit root loads count visits, once per normalized case-insensitive full path per request, regardless of entry point. Subfolder navigation, overscroll, refresh, automatic reconciliation and probes never count. Stars toggle saved favorites, enabled solid white and disabled gray outline.

Center compact header/cards within a maximum 1600px block and 32px side margins. Use 16px card padding/gutters, 12px content spacing, 8px actions; header top inset 16px with 12px before cards. Show names/counts/relative time, paths/exact timestamps on hover. Cards adapt across three/two/one columns, and Show more grows by 25 without pruning saved records. Relative times use whole seconds/minutes/hours/days, fixed 30-day months and 365-day years, with correct singular/plural/future clamping.

| Preference | Default / persistence contract |
|---|---|
| Volume / mute | 50% / unmuted; independent, restored before media opens |
| Carousel visibility / density | On / 11 icons; density retained while hidden |
| Folder pin | Unpinned; persisted |
| Gallery width / height | 150 / 150px; independent stepped values; normalize older continuous values without losing other settings |
| Reader scaling / width | Width / 100%; width retained in height mode |
| Reader spacing / preload / counter | 0 / 2 / on |
| Window | Normal bounds plus last normal/maximized state and monitor-relative logical offsets; never reopen minimized/fullscreen |

Restore window fully on a current work area before show, with primary fallback for removed monitors. Preserve normal restore bounds when closing minimized/maximized/fullscreen. Reconcile display/DPI/work-area changes; defer work-area changes during move/resize, temporarily relax minimums only when necessary. Invalid state uses safe defaults with logged/reportable errors; do not silently overwrite unreadable stored state without a real change. Settings exposes separate thumbnail-cache clear and confirmed overwrite-backup cleanup.

## 9. Freshness, responsiveness and accessibility

Treat notifications as hints and reconcile actual filesystem contents. Watch all loaded roots, reducing duplicate/nested coverage, only in Browse/Gallery/Reader; stop in Home/Settings and restart/reconcile on return. Preserve unaffected playback, zoom, text position, Reader anchors and thumbnails; reopen affected media paused. Defer changes during commands/dialogs/external actions. Report degraded/unavailable coverage, retry with bounds and retain F5. Same-size/timestamp edits made without delivered hints while coverage was stopped require F5.

Gallery/carousel use shared Windows-default `SingleItem` thumbnails without explicit display-sized requests. Preserve returned dimensions/aspect/transparency; normal provider output is not manually blurred. Explicit-size clients remain compatible. Strong content hints/F5/overflow/recovery invalidate their scope; healthy watcher startup compares metadata without flushing unchanged previews. Known stale images bypass Windows cached pixels using a fresh bounded source decode; freshness survives restart/cache housekeeping. Display dimensions/DPI do not rewrite default-cache keys or force a blanket purge.

Virtualize large visual collections, bound work/memory, cancel obsolete jobs and release native/stream resources. Preserve focus, accessible names/selection/status and native modal input; don't rely only on right-click for supported visible actions. Smooth responsive large-folder use is a goal, not a claim of universal codec, process-memory or FPS limits. Validation coverage and open limits are recorded in [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) and [PROJECT_STATUS.md](PROJECT_STATUS.md).

## 10. Explicitly deferred scope

Search/filtering (especially Browse/Gallery search) is deferred to a future phase, not the next active slice. Further sorts, saved named collections/bookmarks, custom shortcut/reveal/tree-size controls, extra document/media/3D handlers, animated WebP/FLAC, richer metadata, duplicate detection, themes, external file import/export and cross-panel drag require separate scope. No duplicate detector may automatically delete files. Bulk rename is not planned. Existing internal Gallery moves and folder drops for root loading are already part of the current product.
