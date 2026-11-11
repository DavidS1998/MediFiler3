# 1. Project overview

## 1.1 Product description

Develop a Windows desktop application that combines file management, media viewing, and file browsing into a single, cohesive interface.

The application should allow users to load one or more folders, navigate their contents through an automatically generated folder tree, inspect files using an integrated viewer, and organize files through intuitive mouse and keyboard interactions.

The primary design philosophy is:

**Browsing and organizing files should be part of the same workflow.**

Rather than opening files individually in external applications, users should be able to navigate through large collections of files with minimal interaction. The application should provide specialized viewing experiences depending on the file type, while maintaining a consistent navigation and organization system.

The application should prioritize:
- Fast and intuitive file browsing.
- Minimal interaction required to navigate between files.
- Context-sensitive file interaction.
- Efficient file organization through drag-free mouse interactions.
- Support for large collections of files.
- A clean, configurable, and information-dense interface.
- Extensibility for additional file formats and viewing modes.

The application is intended primarily for personal file and media collections, but its architecture should not impose unnecessary limitations on collection size or folder structure.

## 1.2 Core concepts

The application consists of five primary components:

1. **Library / Home screen:** Provides quick access to recently opened, frequently opened, and favorited folders.
2. **Folder tree:** Displays the folder hierarchy of the currently loaded collection.
3. **Main viewer:** Displays and allows interaction with the currently selected file.
4. **File carousel:** Provides thumbnail previews of files surrounding the currently selected file.
5. **Alternative viewing modes:** Provide different ways of browsing and interacting with files, including gallery and continuous reader modes.

These components should work together while remaining independently configurable.

# 2. Application structure

## 2.1 Library / Home screen

Upon launching the application, display a home screen containing three distinct collections of folders.

### Recently opened

Display the folders most recently opened by the user, ordered chronologically, newest first.

### Frequently opened

Display root folders ranked by how many times the user explicitly opens that specific root. Do not persist visit/history information for its subfolders; subfolder navigation and overscroll do not count as root opens.

The frequency counter and last-visited timestamp persist between sessions. Every successful explicit root load updates them, whether initiated by drag-and-drop, Add Folders or Home. Equivalent normalized absolute Windows paths share one record; duplicate aliases within one load count once. Refresh, subfolder navigation, overscroll and availability probing do not count.

### Favorites

Display folders explicitly marked as favorites by the user.

Favorites apply only to saved root folders and are added/removed from Home, not the folder tree or its menus. A star next to an entry in Recents or Frequently opened toggles the shared favorite state. Favorites are ordered alphabetically by folder name (path breaks ties). The same root shows the same filled/unfilled star in all two or three Home sections where it appears. Enabled stars are filled solid white; disabled stars use the gray outline.

### Home layout

Use a native WinUI card layout within the WPF host: Recent, Frequent and Favorites sections, standard Windows spacing (16 px card insets, 12 px content spacing, 8 px action spacing) and responsive columns. Show compact root names and visit counts with relative last-visited times. Full paths and exact local timestamps belong in hover text. Relative times use seconds, minutes, hours, days, 30-day months and 365-day years, with singular/plural forms and live updates while Home is open. Retain clear empty states and Add folders. Large saved lists may use explicit Show more controls without removing persisted history.

Keep the header compact: omit the library subtitle, use a 16 px top inset and a 12 px gap between commands and cards. Center the header/card block in the viewport at every width, capped at 1600 px with 32 px side margins.

### Home screen behavior

- Clicking a folder opens it in the main application interface.
- Each entry should display its folder name and optionally its full path.
- Missing, renamed, or inaccessible roots remain listed. Let the user locate the folder manually or remove its saved entry from all lists. Do not automatically infer a replacement path.
- A single Remove entry action removes a saved root from Recents, Frequent and Favorites; it never deletes the source folder.
- Favorites should persist between sessions.
- The home screen should provide an option to load a new folder or collection.

The home screen should be designed so that additional collections or shortcuts can be introduced later.

## 2.2 Loading folders and collections

The application must support loading:

- A single folder.
- Multiple folders as separate roots in one loaded session. Each root retains its own filesystem hierarchy; files from different roots are not merged into a single folder list.
- A previously loaded collection.

When a folder is loaded, the application should scan its directory structure and construct a navigable folder tree.

Publish the selected roots without waiting for their descendants or initial file listing. Discover and append subfolders progressively in the background, keeping input and rendering responsive through bounded UI update batches. Cancel old hierarchy discovery when loading or refreshing another context. Show unknown counts while child discovery or file counting is incomplete.

The folder tree should reflect the actual filesystem hierarchy.

Files should be indexed sufficiently to support fast navigation, sorting, thumbnail generation, and file operations.

### Important considerations

- Avoid loading every file's full contents into memory during indexing.
- Exclude files marked with the Windows Hidden or System attribute from the browsing file list.
- Large directory structures must not freeze the UI.
- Thumbnail generation should happen asynchronously.
- The application should handle inaccessible directories and files gracefully.
- Symbolic links and directory junctions should be handled safely, avoiding infinite traversal loops.
- The user should be able to refresh or rescan a collection.
- Filesystem changes made outside the application should eventually be reflected in the interface. While Browse or Gallery is active, watch all loaded-root hierarchies with duplicate/nested coverage reduced. Coalesce events with 500 ms quiet debounce/2 s maximum batching delay and a 1,024-path bound; overflow/error triggers rescan. Stop watchers outside Browse/Gallery and on root replacement/shutdown. Reconcile on return/reconnection, defer during commands/dialogs, preserve selection/expansion and unaffected presentation, reopen affected media paused, and never count automatic work as root visits. Unavailable coverage reports degraded status, retries up to 60 s and polls the active folder; F5 remains available.

# 3. Main viewer

The main viewer is the primary interface and should receive particular attention during development.

It should provide a focused browsing experience where the user can move through files sequentially without having to open each file individually.

## 3.1 Default file navigation

When a folder is selected, display its supported files in the main viewer.

Files should initially be sorted by filename using Windows Explorer-style natural ordering, so numeric portions sort numerically (for example, `page 2` before `page 10`). The same ordering should be used by all viewing modes.

The main viewer should:

- Display one file at a time.
- Allow the user to move to the next or previous file through mouse-wheel scrolling.
- Update the file carousel and current selection immediately when navigating.
- Support keyboard navigation.
- Indicate the current file's position within the folder in the native application title bar, formatted as `(5/16) FileName.jpg`, where the position is one-based and the total is the number of files in the active folder. Show the application name when no file is active.
- Handle reaching the beginning or end of the file list predictably.
- Allow the user to change sorting preferences.

The file list should be based on the contents of the currently selected folder, not recursively include files from its subdirectories.

## 3.2 Context-sensitive file interaction

The main viewer should interpret mouse interaction according to the currently displayed file type.

The interaction system should be extensible so additional file types can introduce their own behaviors without modifying the central navigation system.

### Images

When displaying an image:

- Render the image at its highest available resolution, constrained by the viewport.
- When available, show the file's thumbnail in the viewer while its full image is loading, then replace it with the full image.
- Preserve the original aspect ratio.
- Scale the image to fit the viewport by default.
- Right-click to toggle between file navigation and image zoom interaction.
- In zoom interaction mode, mouse-wheel scrolling should zoom in and out around the cursor position.
- Holding the left mouse button should allow panning the image.
- Provide a way to reset the zoom and return to the default fit-to-view state.
- Preserve the zoom level and pan position when appropriate, but reset them when moving to another image.
- Initially support common WIC formats including JPEG (including .jfif), PNG, BMP, GIF, and TIFF. WebP also routes through the image viewer and thumbnail decoder using the installed Windows WIC codec. Missing codec/decoding errors retain external-opening actions. Animated GIFs play automatically using embedded frame delays and loop counts, including transparency, offset frames and disposal methods. Show animated GIF duration for one cycle (sum of effective frame delays), without multiplying by finite/infinite loop counts, after its dimensions: `1.1 MB - [128×128] - [00:14]`. Zero or absent delays use the player's 100 ms fallback; static GIFs omit duration. Keep the image zoom/pan controls and original logical-canvas resolution metadata; zoom/resize must not restart playback. Stop and release animation when changing files, leaving Browse or shutting down, and close the source file after loading so operations can proceed. Single-frame GIFs and WebP use static image decoding; animated WebP remains future scope.

Zooming should support viewing the original image resolution without unnecessarily upscaling small images.

### Video

The backend is WinUI MediaPlayerElement with Windows.Media.Playback.MediaPlayer, hosted in a XAML Island within the WPF application. It uses installed Windows media components without a bundled codec runtime. Routed video targets are MP4/M4V, WMV, WebM and MKV; audio targets are MP3, WAV, WMA, M4A/AAC, ADTS AAC and Opus. Extensions route to handlers; codec/container combinations may depend on Windows media extensions. Unsupported variants show an error and retain external-opening actions. FLAC and other extensions remain deferred.

Media opens paused, with Space/right-click or a WinUI toolbar control toggling playback. The right toolbar provides play/pause, stop, back/forward 5-second jumps, a seekbar that stretches to the available toolbar width, current/total playback time, and a compact horizontal volume popup with a clickable speaker/mute icon on the left, a 0-100 slider, and a numeric level on the right. Keep the popup consistent with the compact toolbar: 32-pixel controls, a 16-pixel speaker glyph, 12-point level text, and 8-pixel presenter padding. Jump buttons use Segoe Fluent Rewind (U+EB9E) and FastForward (U+EB9D). Right-clicking the volume button toggles mute without opening the popup. Mute and volume are shared by all media in the current session; muting preserves the selected volume. When settings persistence is implemented, restore/save volume and mute state across application sessions, retaining the selected volume while muted. Playback speed controls are removed. Duration metadata is bracketed, such as `[02:05]` or `[1:02:05]`. Close media before file/folder mutations, undo/redo, navigation, leaving Browse, and shutdown; reload paused when returning or when a cancelled/failed operation leaves the same file selected. Media opening has a cancellable 30-second timeout and clear external fallback on failure. Native video must preserve access to WPF folder/carousel overlays and their reveal edges.

When displaying a video:

- Display an embedded video player.
- Right-click to play or pause playback.
- Provide playback controls, including seeking, volume, mute, and short backward/forward jumps.
- Support audio output.
- Preserve the ability to navigate to the next or previous file.
- Avoid accidental file navigation while interacting with playback controls.

### Audio

When displaying an audio file:

- Right-click to start or pause playback.
- Provide basic playback controls.
- Display relevant metadata where available, such as title, artist, duration, and album artwork. Collapse the artwork slot when no image is available, including when the folder opens directly on an audio file; load artwork asynchronously without shifting text around an empty reserved slot.
- Indicate playback progress.
- Allow the user to navigate to another file without closing the viewer.

### Text files

When displaying a text file:

- Render its contents in a readable text viewer.
- Support vertical scrolling through the document.
- Right-click to toggle between document scrolling and file navigation.
- Preserve the document's scroll position when appropriate.
- Support common text encodings.
- Provide basic handling for large text files without loading their entire contents into memory.

Text rendering should be extensible to support additional document formats in the future.

Keep the text viewport centered with equal left/right insets matching the folder-tree panel width plus normal padding, so revealing or pinning the tree does not cover the text or shift the reading column. Adjustable text page/window sizes and further reader features are deferred.

The initial text viewer supports strict UTF-8 (with or without BOM), and BOM-marked UTF-16/UTF-32 in either byte order. Read at most 1 MiB per window, preserving Unicode code points across window boundaries; provide previous/next-window controls instead of loading entire large files. Malformed encodings, binary-looking content, and read failures show clear errors. Text extensions include ordinary documents, logs, configuration, and common source files; render them as plain text without executing content.

Right-click, F4, and a contextual toolbar button toggle navigation and document scrolling. Carousel file navigation remains available in either mode. Provide a selectable read-only text view for copying, retain the current window/scroll position when leaving and returning to Browse, and reset text state when selecting another file. Word count counts whitespace-delimited runs. For a whole-file window show the total; otherwise label the displayed window's count with `(window)` rather than implying a whole-file total. Include file size as for other file types.

### Unsupported file types

For file types without an integrated viewer:

- Use the file's thumbnail as the viewer fallback when a thumbnail is available.
- Display basic file information and an indication that no integrated viewer is available.
- Allow the user to open the file using its associated default Windows application.
- Handle missing file associations and launch failures gracefully.

Provide distinct Open in default application and Open with actions, including when no thumbnail exists or an integrated preview fails. For files without an integrated handler, right-clicking the viewer opens the native Open With chooser, whether or not a thumbnail is shown. Ignore empty/inactive viewers and prevent duplicate in-flight chooser requests through the shared external-opening action. Show the filename and full path alongside the fallback; retain file size in the toolbar. Ctrl+Enter opens the current file in its default application; Ctrl+Shift+Enter shows the native Windows application chooser. Capture the selected file when invoked, prevent duplicate in-flight requests, and show completion/errors only for the matching active file. Treat chooser cancellation as a normal outcome. Missing associations should explain that Open with can select an application, without automatically launching a second action.

Use compact WinUI AppBarButtons in the center toolbar for these actions, matching the existing 32-pixel primary-command styling. Both actions apply to all selected file types, including images and text, and remain visible but disabled when no file is selected. Image/text controls remain in the right toolbar independently. Resolve the effective default application asynchronously, displaying its application icon on the button and `Open in ApplicationName` in the tooltip/accessibility label where available. Keep a generic open label/icon if lookup fails; when Windows reports no association, show `No default application`, disable that action, and retain Open with. Refresh on file changes and after returning from external-opening actions, reject obsolete results, and keep lookup work bounded. The fallback's information/status panel remains WPF.

## 3.3 File navigation and interaction states

The viewer should maintain a clear distinction between navigation and interaction modes.

For example:

| File type | Default scroll behavior | Right-click behavior |
|---|---|---|
| Image | Navigate files | Toggle zoom interaction |
| Video | Navigate files | Play / pause |
| Audio | Navigate files | Play / pause |
| Text | Navigate files | Toggle document scrolling |
| Unsupported | Navigate files | Open the native Open With chooser |

The application should communicate the current interaction mode through subtle visual indicators where necessary.

Avoid relying on right-click alone for essential functionality. All primary interactions should also have keyboard shortcuts or accessible alternatives.

The exact interaction model should be implemented centrally and remain consistent across viewing modes. Five consecutive navigation attempts past a folder boundary enter the adjacent nonempty folder in loaded tree order, skipping empty folders. Backward overscroll selects the previous folder's last file; forward overscroll selects the next folder's first file.

# 4. File carousel

A horizontal thumbnail carousel should be displayed along the bottom of the main viewer.

Its purpose is to provide visual context for the currently selected file and allow quick navigation to nearby files.

### Requirements

- Display a configurable number of previous and upcoming files. A persisted Show carousel setting (default on) disables the entire carousel, its peek/reveal interaction and carousel thumbnail work without disabling viewer navigation. Retain the configured density while off.
- Use 150 × 150 pixel square thumbnail tiles by default; fill each tile while preserving aspect ratio and cropping from the center as needed.
- Display the current file distinctly.
- Keep the current file centered in the visible carousel viewport and animate recentering during file navigation to avoid abrupt shifts. Provide blank space before the first file or after the last file as needed to preserve this centered position.
- Keep spacing between adjacent carousel items compact.
- Make each thumbnail item clickable across its tile area, use a pointer cursor, and avoid additional hover or selection fills that compete with the current-file highlight.
- Show each carousel filename on a single line, truncating it when necessary.
- Preserve mouse-wheel file navigation while the pointer is over the carousel.
- Hide the horizontal scrollbar while preserving horizontal navigation.
- In the default layout, keep the carousel mostly hidden while allowing about one eighth of its height to peek above the bottom edge. The initial peek is slightly transparent, with a translucent gradient at the exposed edge to hint at the carousel. Hovering near the bottom fully reveals it; leaving the carousel returns it to the peek state. Make the peek amount and reveal behavior adjustable in future settings.
- Use generated thumbnails where possible.
- Highlight the currently selected file.
- Update the carousel whenever the current file changes.
- Allow clicking a thumbnail to navigate directly to that file.
- Support horizontal scrolling if the number of displayed thumbnails exceeds available space.
- Handle files without thumbnails using suitable placeholder icons.

The carousel should be generated from the current folder's sorted file list.

Thumbnail generation and retrieval should be cached to avoid unnecessary processing.

# 5. Folder tree and file organization

The folder tree should remain accessible alongside the main viewer. In the default browsing layout, it floats above the media canvas and slides into view when the pointer approaches the left edge; it hides again when the pointer leaves the tree.

It should display the hierarchy of the loaded collection and allow the user to perform file and folder operations.

## 5.1 Folder navigation

Left-clicking a folder should load that folder's contents into the main viewer.

Folder rows should provide a full-width click and hover/selection area so the tree remains easy to use when the folder name is short. Reserve about 48 pixels at the bottom of the tree viewport so its last entries can scroll clear of the floating carousel without sacrificing excessive tree height.

Each folder row should include a menu button aligned to its right edge. Activating this button opens that folder's WinUI context menu for folder-management actions. Rename file/folder, new-folder naming, and non-empty folder deletion confirmation use WinUI ContentDialog controls in an owned XAML Island host. Move-folder destination selection uses the basic native folder picker, initially showing the source folder's parent. Keep the row's left-click behavior for folder navigation and its right-click behavior for moving the currently displayed file to that destination folder; the menu button provides a distinct target for folder interactions so those gestures do not conflict. The folder panel has a top-right Pin/Unpin button using the matching Windows UI icons; pinning prevents automatic hiding and reverses the selected button colors while keeping the icons unchanged. Persist the pin state between sessions.

The currently selected folder should be visually highlighted.

Prefix each folder name with `(direct/total) - `, for example `(7/10) - SomeFolderName`. The first count is the number of browsing files directly in that folder; the second includes those files and all files in its traversed subfolders. Exclude Hidden and System files, count unsupported file types, and do not follow directory junctions or symbolic links. Refresh counts after application file operations and explicit tree refreshes. Inaccessible entries remain reported through the existing scan warnings; counts represent successfully enumerated files.

File counting must not delay publishing the folder hierarchy. Show `(?/?)` while counts are unloaded and `(7/?)` when only the direct count is known. Populate counts progressively in the background, keeping incomplete or failed counts unknown and reporting read failures. Cancel superseded work and prevent older counts from replacing newer file-operation/listing results.

The tree should support:
- Expanding and collapsing folders.
- Navigating deeply nested structures.
- Scrolling independently of the main viewer.
- Hiding and showing the entire folder tree.
- Resizing the folder tree panel.
- Remembering its visibility and width between sessions.

## 5.2 Moving files

Right-clicking a destination folder in the folder tree should move the currently displayed file into that folder.

After a successful move:

- Remove the file from the current folder's file list.
- Update the file carousel.
- Select a sensible neighboring file as the new current file.
- Refresh the relevant folder contents.
- Update the folder tree if necessary.
- Provide visual confirmation of the operation.

The move operation should be atomic where possible and handle errors such as insufficient permissions, locked files, and filename conflicts.

On a name collision, offer Rename, Overwrite, or Cancel. Rename automatically applies the next available numbered incoming name and rechecks it; Overwrite requires an explicit choice and preserves the existing item for Undo; Cancel leaves both items unchanged. Never overwrite silently.

### Multiple-file operations

The file organization system should eventually support moving multiple selected files simultaneously.

This is particularly important for the gallery view described later.

## 5.3 Folder management

The folder tree should support standard folder operations:

- Create a new folder.
- Rename a folder.
- Move a folder to another location.
- Delete a folder by moving it to the Windows Recycle Bin.
- Root favorites are managed from Home only; folder-tree menus do not expose favorite actions.
- Refresh a folder.
- Open a folder in Windows Explorer.
- Copy a folder path.
- View folder properties.

Destructive operations should require confirmation when appropriate.

Moving a folder to the Recycle Bin should display a warning explaining what will happen, particularly if the folder contains files or subfolders.

The application should use native Windows filesystem operations wherever practical.

## 5.4 Drag and drop

Although right-click interactions are central to the application, drag and drop should also be considered.

Implemented internal Gallery drag-and-drop:
- Drag an unselected file alone; drag a selected file together with the full selected set in natural order.
- Drop onto any loaded destination folder in the tree to move through the existing safe grouped-operation service. Same-folder and stale drops are rejected; collisions, partial outcomes and grouped Undo/Redo keep their existing safeguards.
- Preserve normal click, Ctrl-click and Shift-click selection. Begin a drag only after pointer movement crosses the Windows drag threshold; a press/release alone never initiates dragging. Preview tiles use their cropped presentation, without hover-fit letterboxing.
- Show a native drag operation and explicit moving-file count, reveal the tree while dragging, and highlight valid targets with Move feedback. Use an 80%-opaque dragged-tile preview stack with its top-left corner anchored to the pointer. The preview is bounded to three realized tiles; the full selected set remains the move payload. Cancellation does not mutate files.

Potential later functionality:
- Drag files from Windows Explorer into the application.
- Drag folders into the application to load them.
- Drag selected files between application panels.

Drag and drop should complement, rather than replace, the primary interaction model.

# 6. Alternative viewing modes

When Gallery is requested after Home or Settings, reveal the shared Browse current file after the native viewport is loaded/measured. Preserve the selection and decoded thumbnails; deferred positioning must yield to newer navigation or user interaction rather than overriding it later.

Returning to Browse/Gallery from Home or Settings must reconcile the filesystem without invalidating unchanged thumbnails merely because watcher coverage restarted. Healthy startup compares metadata; actual change hints, F5, overflow and failed-coverage recovery retain explicit invalidation. Changes made while coverage was stopped with unchanged size/timestamp require F5.

Thumbnail generation on application-cache misses uses StorageFile.GetThumbnailAsync/StorageItemThumbnail in SingleItem mode, normalized into shared aspect-correct PNGs. Gallery/carousel use Windows' default sizing/options and preserve the returned dimensions, with a separate shared default-size cache bucket; keep explicit-size entries usable without a purge. Known invalidated images must bypass stale Windows system thumbnails using a fresh WinRT image decode, preserving same-size/timestamp refresh and restart handling (default fallback bounded at 256px). Main viewer image decoding remains unchanged.

Browse and Gallery appear as separate tabs/destinations in the compact sidebar. They share folder/file context, but apply changes to the other view's expensive presentation when that view is requested. Switching tabs must not rescan the collection or load inactive viewer content/thumbnails.

The application should support multiple viewing modes that share the same underlying file collection and organization functionality. Switching viewing modes is a context change: preserve the loaded roots and folder context, but clear mode-specific file selections.

Switching between viewing modes should not require rescanning or reloading the collection.

## 6.1 Gallery view

The gallery view provides an overview of a folder's contents through a grid of thumbnails.

### Requirements

- Display a configurable number of columns or thumbnail sizes.
- Gallery thumbnail width (X) and height (Y) are independently adjustable through two sliders in the right toolbar. Column count responds to the available width and chosen thumbnail width; do not replace these with a single size preset.
- Each slider spans 100–400 logical pixels in steps of 50 (default 150); persist width and height independently. Older continuous sizes snap to the nearest valid step without discarding other settings.
- Retain the Gallery grid and previews across tab switches; avoid replacing unchanged listings, and try to keep the shared current file visible when Gallery is requested.
- Keep tile gaps minimal (two pixels). Crop thumbnails to cover each tile; on hover, fit the whole thumbnail at its original aspect ratio and hide the bottom filename/transparent-gradient overlay. Restore the crop and caption on exit.
- Gallery crop framing matches the carousel's centered square, then fills the chosen tile rectangle. Hover still reveals the full uncropped image. Retain decoded previews more conservatively within a 512-entry/128 MiB limit; idle disk housekeeping must not blanket-flush encoded thumbnails from memory. Explicit clearing and changed-source invalidation remain effective.
- Reflow Gallery tiles beside a pinned folder tree; an unpinned tree retains its overlay/reveal behavior.
- Returning to Gallery through Home or Settings must keep a pinned folder tree visible and reachable, with the native Gallery surface clipped around it; no pin toggle is required.
- Center Gallery rows in the available space. Selected/current tiles use a distinct visible border. Right-toolbar slider labels read Width and Height; the current locally adjusted slider width is 150 logical pixels.
- Double-clicking a Browse carousel tile opens Gallery at that file and highlights it; single-click behavior remains Browse selection.
- In normal mode, sidebar hover is the sole trigger to open the unpinned folder tree. Hovering the tree retains it once open; hide after leaving that combined area. The old content-edge hitbox must not open it. Preserve pinned-tree behavior and fullscreen's coordinated reveals.
- Carousel and Gallery reuse the same Windows-default-size cached PNG thumbnail source, with mode-specific rendering. File-specific change hints must retain unaffected thumbnails and decoder work; explicit folder/root refreshes still invalidate their scope.
- Generate thumbnails asynchronously.
- Virtualize the interface so only visible items and a reasonable surrounding buffer need to be rendered.
- Support alphabetical sorting and other sorting options.
- Support selecting individual files.
- Support CTRL+click for selecting multiple files.
- Holding Alt while hovering tiles selects each entered file immediately. The first file determines the gesture direction: start on a selected file to remove selection instead. Revisiting a file must not repeatedly toggle it. Reset the gesture on release, leaving Gallery's grid, context change or deactivation. Ctrl-hover does not paint; ordinary Ctrl-click and Ctrl+Shift range selection remain native.
- Support SHIFT+click for selecting a range of files.
- Use native extended selection and keyboard navigation, including modifier ranges/focus-only movement, Home/End, PageUp/Down and Space. Ctrl+A selects all; Escape clears selection; Enter opens the focused file in Browse. Keep native focus visuals and accessibility selection patterns.
- View keyboard shortcuts apply regardless of focused tree, toolbar or viewer control. Modal dialogs retain their own text/confirmation input; Gallery native modifier navigation remains intact.
- Display an accessible selected-file count and filename/path/basic size information. Keep a distinct border on every selected tile.
- Keep the count clear of the scrollbar and outline its text for readability over white images. Selected Gallery files and the carousel current/middle item use a solid bright-pink (#FF1493) five-pixel border, with no pattern or secondary stroke. Highlights stay noninteractive. Carousel images/current frames and fading edge items have square corners.
- Render thumbnails without manual blur or display pixel filtering. Preserve dimensions, aspect ratio, transparency and cache/source contents; full-size viewer decoding stays unchanged.
- Preserve Windows-default returned thumbnail dimensions during PNG normalization. Keep high-quality WPF carousel scaling and Fant interpolation where explicit-size/fresh-source fallback needs downsampling. Existing cached PNGs remain usable, and F5 retains freshness invalidation without changing crop geometry.
- Home/Settings visits retain Gallery selection; switching to Browse clears mode-local selection. A new Gallery mode entry selects the shared current file once. Refresh/reveal must preserve surviving selections and an explicitly cleared selection.
- A mouse/keyboard context menu offers Open in Browse, Select all and Clear selection. A selected target retains the set; an unselected target replaces it. Blank-space Open is disabled. Reject stale commands after listing/activity changes.
- Gallery uses grouped selected-file moves, journal-backed grouped Recycle Bin and root-scoped Undo/Redo. Rename is available only for exactly one selected file, preserving its extension; bulk rename is not planned. Single-file recycle bypasses confirmation; multiple-file recycle requires confirmation. Never apply a single-current-file mutation implicitly to a multi-selection.
- Provide a clear visual indication of selected files.
- Allow selected files to be moved to a destination folder in the folder tree.
- Support keyboard navigation.
- Display basic file information when useful.

### Opening a file

The user should be able to open a file in the main viewer directly from the gallery.

For example:
- Double-click a thumbnail.
- Press Enter while a file is selected.
- Use a configurable shortcut.

The main viewer should open at the selected file, preserving the folder context.

The gallery should also support standard file operations through context menus.

## 6.2 Continuous reader view

Provide an alternative reader mode intended for collections of images that should be consumed sequentially, such as comics, manga, scanned documents, and image sequences.

The reader should display files vertically in their sorted order, allowing the user to scroll continuously through the collection.

### Requirements

- Display multiple images in a continuous vertical sequence.
- Preserve image aspect ratios.
- Allow the user to resize the reader viewport.
- Support different image scaling modes, such as fit-to-width and fit-to-height.
- Support configurable spacing between images.
- Allow the user to navigate through the sequence using the mouse wheel.
- Provide an optional page indicator.
- Allow jumping to a specific file.
- Support keyboard navigation.

### Performance and memory management

The reader must support lazy loading.

Specifically:
- Load images as they approach the visible viewport.
- Preload a configurable number of upcoming images.
- Release image resources when they are sufficiently far outside the viewport.
- Cache recently displayed images where appropriate.
- Avoid loading the entire collection's full-resolution images into memory.
- Maintain smooth scrolling when dealing with large collections.

The reader should share the same file sorting, selection, and organization functionality as the main viewer.

## 6.3 Future viewing modes

The architecture should allow additional viewing modes to be introduced later.

Potential candidates include:
- Document reader.
- Video gallery.
- Audio library.
- Slideshow.
- Side-by-side comparison viewer.
- Metadata-focused file browser.

These are not required for the initial implementation.

# 7. User interface and customization

The interface should be clean, modern, and designed for extended use.

The main application window should consist of a full-width toolbar above a compact navigation rail and a media canvas. The media canvas fills the available window area beneath the toolbar, excluding the navigation rail. The folder tree and file carousel float above this canvas instead of reserving space beside or below it.

### Main layout

Suggested layout:

- Three WinUI `CommandBar` controls hosted in a XAML Island span the entire top of the WPF window, including the navigation rail column. Align them left, center, and right in a shared three-column grid, place actions in `PrimaryCommands`, use 32-pixel square `AppBarButton` controls for actions, and keep the toolbar 48 pixels tall with overflow buttons hidden. The toolbar and navigation rail backgrounds are pitch black (`#000000`).
- Divide the toolbar into three sections. The left section shows current folder context (with breadcrumb navigation where practical) and file metadata as handlers provide it, including image resolution, video duration, and file size. The middle section contains common actions such as undo, delete, and folder loading; folder loading uses an Add/Plus icon. The right section contains contextual controls and information such as adjacent zoom-in/zoom-out buttons, a reset-to-fit AppBarButton grouped with the current zoom percentage, and future media playback controls.
- The media viewer or gallery occupies the available canvas beneath the toolbar and beside the navigation rail.
- The folder tree floats over the left side of the media canvas and the carousel floats over its bottom edge. Both are slightly transparent and slide in when the pointer approaches their respective edge. The tree is hidden by default. The carousel starts mostly hidden with about one eighth of its height peeking above the bottom edge at reduced opacity. They return to their default hidden/peek state when the pointer leaves. A future setting may allow users to adjust the peek amount, keep either panel visible, or toggle this reveal behavior.

The layout should adapt to different window sizes.

### Windows UI framework integration

- The application remains a WPF desktop application, with WPF owning the top-level window, application startup, and composition root.
- Prefer suitable WinUI controls over WPF equivalents through Windows App SDK XAML Islands for native behavior and consistent styling. Retain WPF as the host and use WPF where rendering/interop needs make it appropriate. Settings buttons, toggle, slider and scroll container use WinUI; the navigation rail uses WinUI `NavigationView`.
- Keep the application navigation rail in its compact icon-only state at all times; do not provide a pane expand/collapse toggle. Keep Settings at the bottom of the rail. The entire rail must remain programmatically hideable so fullscreen mode can remove it.
- The WPF and WinUI boundary must preserve shared application navigation state and support keyboard focus, resizing, DPI changes, accessibility, and clean shutdown.
- Prefer the native Windows caption and caption buttons. Its caption and text colors should be changeable at runtime, defaulting to black with white text. Use Windows 11 DWM caption color attributes for the WPF top-level window; WinUI `SetTitleBar` applies only to a WinUI top-level window.

### Customization

The user should be able to:
- Show or hide the folder tree.
- Resize the folder tree.
- Resize the main viewer.
- Show or hide the carousel.
- Adjust carousel size and number of thumbnails.
- Use a discrete WinUI slider with visible value ticks to set the total carousel icon count, including the current file, to an odd value from 1 to 25. Default to 11 icons (five neighbors per side); each slider step changes the total by two. Apply the setting immediately; persist the value between sessions.
- Change thumbnail sizes in gallery mode.
- Change image scaling behavior.
- Configure keyboard shortcuts.
- Choose between available themes.
- Configure default sorting behavior.

Application settings should persist between sessions.

# Additional requirements: Toolbar and operation history

## Central toolbar

The main application window must contain a central toolbar providing quick access to frequently used actions.

The toolbar should be organized into logical groups.

### History
- Undo.
- Redo.
- Hovering over Undo or Redo previews the planned actions and paths. For batches, show the first eight entries in execution order and the number of remaining entries.
- Clicking Undo/Redo or using Ctrl+Z/Ctrl+Y applies the operation immediately; the tooltip is informational and does not introduce a confirmation dialog.

### File operations
- Rename selected/current file.
- Copy file.
- Move file.
- Delete file (move to Windows Recycle Bin).
- Refresh loaded context (F5): provide a center-toolbar button that checks external filesystem changes, retains the current folder/file path where possible, and selects the nearest valid prior position if the file disappeared.

### Navigation
- Previous file.
- Next file.
- Navigate to parent folder.

### Viewing
- Main viewer.
- Gallery view.
- Continuous image reader.
- Toggle folder tree.
- Toggle file carousel.
- Fullscreen.

### Fullscreen

F11 and a toggle at the far right of the toolbar enter/exit borderless fullscreen on the current monitor. Hide the caption, toolbar, sidebar and carousel peek. Hover near the top edge to reveal the native title bar and toolbar, the left edge to reveal sidebar and folder tree together, or the bottom edge to reveal the carousel; a disabled carousel stays disabled. Keep tree pin/reveal behavior. Preserve playback, selection and viewer interaction on transitions. Restore the preceding normal/maximized bounds on exit, and preserve that layout for restart even when closing fullscreen. Fullscreen itself is not persisted.

### Application
- Settings.
- Additional application controls as appropriate.

Toolbar actions should be context-sensitive. For example, image-specific controls should only be displayed when relevant.

The zoom-out, reset-to-fit/percentage, and zoom-in group must be modular and visible and available only for image content. Replace this group with relevant controls for other content types, such as playback controls for audio/video.

Actions that cannot currently be performed should be visibly disabled.

All important toolbar actions should have corresponding keyboard shortcuts. Shortcuts should be configurable where appropriate.

## Undo and redo

Undo and redo are essential application functionality.

Implement a centralized operation history system that records supported modifications to the filesystem.

The system must initially support:
- Moving files.
- Renaming files.
- Moving files to the Recycle Bin.
- Restoring files from the Recycle Bin where possible.
- Creating folders.
- Renaming folders.
- Moving folders.
- Moving folders to the Recycle Bin.
- Restoring folders where possible.

### Behavior

- Undo reverses the most recent successful operation belonging to the active root folder. Each root has an independent persistent stack; another root's actions must never be offered or executed.
- Redo reapplies the most recently undone operation in the active root only.
- Executing a new operation after undoing clears redo history only for that operation's root; redo in other roots is retained.
- Failed operations must not be recorded as successful.
- Operations must retain sufficient information to reverse themselves.
- Filename conflicts must offer Rename, Overwrite, and Cancel. Overwrite preserves the displaced item; incompatible file/folder types, links and unsafe ancestor locations cannot be overwritten.
- Undo and redo should refresh all affected views and navigation state.
- Related batch operations should be grouped into a single logical undo operation within their root scope. Cross-root file moves belong to their source root. Folder-menu actions belong to the root containing the target folder.

Use the root's filesystem identity so a root rename preserves its scope. Determine active ownership through actual tree ancestry, including overlapping roots. With no active Browse root, disable Undo/Redo. Cancel pending history work when root context changes or Browse is left; reject stale preview results. Pre-scope journal entries remain stored for recovery/inspection, but cannot be safely assigned to active-root stacks automatically.

### Persistence

The operation history should persist across application restarts where practical.

Use a persistent operation journal, preferably backed by SQLite.

The system should handle cases where files have been externally modified, moved, or deleted since an operation was recorded. It must never silently overwrite existing files during undo or redo.

### Collision choices

Use an owned WinUI ContentDialog with Rename, Overwrite and Cancel (also window-close/Escape), defaulting to cancellation. Compare incoming and existing files side by side with asynchronously loaded thumbnails and file sizes, retaining a clear placeholder when no preview is available. Include paths and exact byte counts on hover; label folders without scanning recursive sizes. Disable unsupported Overwrite. Regular operations rename the incoming item. Folder Overwrite replaces the whole destination folder, not a merge; retain its contents for Undo and describe this in the Overwrite tooltip. Create-folder collisions use the same workflow. Show only comparison cards and action buttons, with no explanatory body text. Use ContentDialog spacing and fit its host to the actual control height without surplus space. A new collision after Rename must be checked again.

File Rename fields show only the name before its extension and preserve the original extension on submission. Collision Rename applies an available numbered suffix directly, without another dialog, before the extension: Test.png, Test (1).png, Test (2).png. Skip names occupied by files or folders and continue an existing numeric suffix. Folder names remain editable in full.

Undo/Redo collisions offer the same choices after verifying the recorded source identity. In that context Rename temporarily renames the blocking existing item so the recorded operation can keep its paths; the opposite history action restores the displaced item. Overwrite also preserves the blocker for the opposite action. Keep all operations/compensation in the existing root scope. Do not offer collision choices to bypass changed/missing source identity or unsafe folder-delete checks.

Preserve replacements via verified journaled backups in dedicated app-managed storage, grouped with the incoming operation so one Undo restores both versions. Use %LOCALAPPDATA%\MediFiler3\UndoBackups when it shares the destination volume; otherwise use a dedicated hidden user-specific backup folder at that volume's root. Keep backups on the original volume to preserve identities and safe folder moves. If managed storage is unavailable, refuse replacement without falling back to sibling backups. Verify identities again after confirmation, roll back preservation when installing the incoming item fails, and report partial/recovery-required outcomes without overwriting a new intervening item.

Settings provides Clear overwrite backups. Require explicit confirmation that cleanup permanently deletes saved versions and removes affected Undo/Redo groups. Retire the whole owning root-scoped history group durably before deleting its verified backups, preserving journal evidence and unrelated history. Serialize cleanup with file operations. Never remove current/restored user files, untracked lookalikes, links, changed-identity backups or unresolved recovery data. Report retained/failed items and permit retry. Legacy sibling backups remain usable and are included in explicit cleanup; do not relocate old journal paths automatically. No automatic expiry or purge is enabled.

### Safety

- Never silently perform permanent deletion when a Recycle Bin operation fails.
- Clearly communicate operations that cannot be undone.
- Preserve original file paths and relevant metadata.
- Use rollback or recovery mechanisms for partially completed operations.
- Handle cross-volume moves safely.
- Avoid recording operations until their completion has been verified.

### Testing

Implement automated tests covering:
- Undo and redo of each supported operation.
- Multiple consecutive operations.
- Undo followed by a new operation.
- Batch operations.
- Filename conflicts.
- Missing or externally modified files.
- Application restart and history recovery.
- Failed operations and partial failures.

Use isolated temporary directories for filesystem integration tests.

## Development priority

The operation history architecture must be established before implementing extensive file modification functionality.

Undo and redo must not be treated as an optional feature added after file operations have already been implemented.

# 8. Additional functionality

The following features would improve the application's usability but should not delay the implementation of the core functionality.

## 8.1 Search and filtering

Provide the ability to search within the current folder or entire loaded collection.

Potential search filters:
- Filename.
- File extension.
- File type.
- File size.
- Date created or modified.

Search results should allow opening files in the main viewer and performing file operations.

## 8.2 Metadata

The implemented metadata scope is file size for all files, original image/GIF canvas resolution, video resolution expressed as pixel height (for example `[1080p]`), duration for video/audio and one animated GIF cycle, and text word counts with partial-window scope where needed. Additional metadata below remains future scope.

Display these values in the left toolbar alongside folder context, using the top line exclusively for metadata and the bottom line for the full folder path. Keep the path muted gray except for its final/current folder name, which is white. Show file size immediately, add original pixel dimensions after image loading, and show duration once a media handler provides it. Clear previous-file metadata immediately on selection changes. Keep known image dimensions stable during same-file zoom/resize reloads; hide file metadata outside Browse. Use KB/MB/GB labels with the existing 1024-based scaling, exactly one fractional digit, and a period by default. Format image metadata as `1.1 MB - [128×128]` and video metadata as `1.1 MB - [1080p] - [00:14]`. Omit unknown video height; audio shows duration without resolution. A future settings option may select the decimal separator.

Display relevant metadata depending on the file type.

Examples include:
- Image resolution, dimensions, and format.
- Video resolution, duration, and codec.
- Audio duration, artist, and album.
- File size and modification date.

Metadata retrieval should not block navigation.

## 8.3 Collections and bookmarks

Allow users to save collections, folder shortcuts, and potentially custom groupings of files.

These should be references to existing files rather than duplicate copies.

## 8.4 Duplicate detection

Consider a feature that identifies duplicate files based on file size, filename, or content hashing.

This should be an optional feature and should never automatically delete files.

# 9. Technical and architectural requirements

The application should be designed for long-term extensibility and maintainability.

The viewer, file navigation system, filesystem operations, thumbnail generation, and individual file handlers should be separated into distinct components.

## 9.1 File handler architecture

Implement a modular file handling system.

Each file type or group of file types should be handled by a dedicated viewer/handler that implements a shared interface.

A handler should be responsible for:
- Identifying supported file types.
- Loading the file.
- Rendering its contents.
- Handling type-specific interaction.
- Releasing resources when the file is no longer displayed.
- Providing a thumbnail where possible.
- Exposing relevant metadata.

The main viewer should not contain large amounts of file-type-specific logic.

Adding support for a new file format should ideally require implementing a new handler rather than modifying the entire application.

## 9.2 Navigation architecture

Implement a centralized navigation controller responsible for:
- Maintaining the active folder.
- Maintaining the current file.
- Maintaining the sorted file collection.
- Moving between files.
- Updating the carousel.
- Switching between viewing modes.
- Preserving relevant navigation state.

Alternative viewing modes should use the same underlying navigation data. On a mode switch, retain the loaded roots and active folder/file context where applicable, and clear selections made in the previous mode.

## 9.3 Filesystem architecture

Filesystem operations should be isolated from the UI.

Implement a dedicated service responsible for:
- Directory scanning.
- File enumeration.
- Moving files.
- Renaming files.
- Creating directories.
- Deleting directories and files.
- Refreshing directory contents.
- Detecting external filesystem changes.

All potentially expensive filesystem operations should be asynchronous.

## 9.4 Performance

Performance is a fundamental requirement.

The application should:
- Avoid loading all file contents into memory.
- Virtualize large lists and grids.
- Cache thumbnails and metadata.
- Load full-resolution images only when required.
- Use lazy loading for continuous reader mode.
- Avoid blocking the UI thread.
- Support cancellation of ongoing background operations.
- Dispose of media and image resources appropriately.

The application should remain responsive when browsing folders containing thousands of files.

## 9.5 Persistence

Persist the following between sessions:

- Recently opened folders.
- Frequently opened folders and their usage counts.
- Favorite folders.
- Application settings, including cross-session media volume and mute, carousel density and folder pin.
- Window and panel layout.
- Thumbnail cache information. Retain the existing PNG cache and memory bounds; record owned filename/key, byte size and last access with 30-second batched writes/shutdown flush. Invalidate changed paths and F5 active-folder results even when size/timestamp match. Idle maintenance removes entries unused for 90 days and, above 1 GiB, prunes least-recently-used entries toward 900 MiB. Start after 30 s idle, then at most hourly; locked/in-use entries may delay reaching the target. Settings shows usage and Clear thumbnail cache. Coordinate readers/writers/decoders and remove only verified owned cache artifacts, rejecting links and retaining unknown files. Never prune source media, overwrite backups or operation/state history as cache.
- Optional saved collections.

Use a suitable local persistence mechanism.

Persist the main window's normal size/position and last normal/maximized state. Closing while minimized retains that earlier state; never reopen minimized. Store sizes and monitor-relative offsets in logical units, retaining the monitor device name. Before the first show, resolve against current monitor work areas and DPI, keep the window fully visible, shrink oversized bounds and relax minimum dimensions only when the available work area requires it. Removed monitors fall back to the current primary monitor; rearranged monitors retain logical size and offsets where possible. Legacy layouts without monitor metadata remain readable. Reconcile display/work-area/DPI changes while running, deferring work-area adjustments during interactive move/resize gestures. Coalesce background writes and capture/flush the latest bounds before shutdown. Invalid/unreadable layout data uses safe defaults and is retained until a real change. Home remains the startup page; layout restoration does not reopen roots or start media. Existing folder-pin persistence remains; no new panel-width or navigation preference controls are introduced in step 4.5.

The application should not require a server or cloud service for its core functionality.

## 9.6 Windows integration

The application should integrate naturally with Windows.

Where practical, use native Windows functionality for:
- File associations.
- Recycle Bin operations.
- File and folder dialogs.
- Drag and drop.
- Filesystem notifications.
- Opening files with their default applications.
- File metadata.

The application should handle common Windows filesystem edge cases, including permissions, locked files, long paths, and files changing externally.

# 10. Interaction and usability principles

The following principles should guide implementation decisions:

1. **Navigation should be immediate.** Moving between files should require minimal interaction and should not trigger unnecessary reloads.
2. **Organization should be contextual.** The user should be able to move a file to another folder without leaving the current viewing workflow.
3. **Viewing modes should share navigation context.** Switching between gallery, main viewer, and reader should not reset the user's position unnecessarily; mode-specific selections are cleared when switching modes.
4. **Interactions should be predictable.** The same interaction should have consistent behavior within the same viewing context.
5. **Destructive operations should be safe.** Never silently overwrite or permanently delete user data.
6. **The interface should remain responsive.** Expensive operations must run asynchronously.
7. **The architecture should be extensible.** New file formats and viewing modes should be straightforward to add.

# 11. Development priorities

Implement the application incrementally.

The detailed milestone breakdown and current completion status are maintained in [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) and [PROJECT_STATUS.md](PROJECT_STATUS.md).

Do not attempt to implement all features simultaneously.

## Phase 1 – Foundation and core browsing

- Application window and basic layout.
- Load a folder.
- Generate and display a folder tree.
- Enumerate and alphabetically sort files.
- Main viewer with next/previous navigation.
- Image viewer.
- Basic keyboard navigation.
- Previous/next file carousel.
- Select folders from the tree.

**Goal:** Deliver a usable file browser with integrated image viewing.

## Phase 2 – Core file organization

- Move current file to a folder through right-clicking the tree.
- Create folders.
- Rename folders.
- Move folders to Recycle Bin.
- Handle file operation errors.
- Implement basic undo support.

**Goal:** Make file organization possible without leaving the viewer.

## Phase 3 – Media and text support

- Video playback.
- Audio playback.
- Text viewer.
- Context-sensitive interaction states.
- External application fallback.
- Basic metadata display.

**Goal:** Support the primary file types and their associated interactions.

## Phase 4 – Home screen and persistence

- Recently opened folders.
- Frequently opened folders.
- Root favorites overview and synchronized star toggles in Home lists, grouped with persistence in Phase 4.
- Persist settings and navigation preferences.
- Thumbnail caching.

**Goal:** Make the application convenient for repeated daily use.

Phase 4 steps 4.1 through 4.6 are implemented: typed storage, current schema-7 SQLite persistence, settings restoration/saving, root-only usage/favorites service and root-load counting. Step 4.4 implements Home cards, synchronized stars, root opening and missing-root choices; step 4.5 implements monitor-aware window layout persistence; step 4.6 implements loaded-root reconciliation and agreed thumbnail-cache maintenance; 4.7 automated acceptance is complete. The user tentatively accepted Phase 4; PHASE4_ACCEPTANCE.md retains untested physical cases as verification limits. Follow the step sequence in [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) and the current code/storage/policy handover in [PHASE4_HANDOVER.md](PHASE4_HANDOVER.md). Those documents distinguish existing behavior, proposed implementation defaults and remaining decisions; Phase 3 interactive checks were accepted by the user on 2026-11-10.

## Phase 5 – Alternative viewing modes

Current implementation: slices 5.1-5.3 are implemented, including Gallery selection/input and subsequent documented UX/resume/thumbnail refinements. Slice 5.4 grouped file mutation/history integration is next; its Gallery guards remain active. Slice 5.5 physical/performance acceptance is open. Continuous reader and additional sorts/drag-drop remain later scope. See [PROJECT_STATUS.md](PROJECT_STATUS.md), [PHASE5_HANDOVER.md](PHASE5_HANDOVER.md) and [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) for the active state; the broad requirements below are not a claim that every item is already implemented.

- Gallery view.
- Multiple file selection.
- Batch file operations.
- Continuous reader mode.
- Lazy loading and resource management.

**Goal:** Expand the application into a versatile browsing and organization tool.

## Phase 6 – Refinement and additional functionality

- Search and filtering.
- Improved metadata.
- Drag and drop.
- Duplicate detection.
- Additional file handlers.
- Further UI customization.
- Performance and usability improvements.

**Goal:** Improve the application beyond its initial feature set.

# 12. Acceptance criteria

The initial application should not be considered complete until the following scenarios work reliably:

1. A user can load a folder and see its directory structure in the folder tree.
2. Selecting a folder displays its files in alphabetical order.
3. The user can navigate through files using the mouse wheel.
4. Images are displayed correctly at their original aspect ratio and can be zoomed and panned.
5. Videos and audio files can be played through the integrated viewer.
6. Text files can be read and scrolled through.
7. Unsupported files can be opened using their default Windows applications.
8. The carousel correctly displays neighboring files and allows direct navigation.
9. Right-clicking a destination folder moves the currently displayed file into that folder.
10. The viewer refreshes correctly after file operations.
11. The user can create, rename, and safely delete folders.
12. The user can hide and show the folder tree.
13. Recent, frequent, and favorite folders are persisted.
14. The application remains responsive when browsing large folders.
15. File operations do not silently overwrite or permanently delete user data.

# 13. Instructions to the AI development agent

Treat this document as the product specification and architectural direction for the application.

Before implementing features:

1. Examine the existing project structure and identify the current technology stack, if one exists.
2. Propose an application architecture that satisfies the requirements.
3. Identify technical dependencies and potential challenges.
4. Break the implementation into manageable tasks.
5. Identify any ambiguities or conflicting interaction requirements that need a design decision.

During implementation:

- Prioritize maintainable, modular code over quick but tightly coupled implementations.
- Keep UI, navigation, file handling, and persistence logic separated.
- Avoid implementing placeholder functionality that appears complete but does not work.
- Implement and test one development phase at a time.
- Ensure existing functionality continues working when new features are introduced.
- Document important architectural decisions.
- Do not introduce unnecessary dependencies.
- Prefer established Windows APIs and maintained libraries where appropriate.

After each development phase:

- Build the application.
- Test the implemented functionality.
- Report completed features, outstanding issues, and known limitations.
- Update the development plan accordingly.

When requirements are ambiguous, propose a solution consistent with the product's core philosophy and explain the trade-offs before making significant architectural decisions.

**The ultimate goal is to create a fast, intuitive, extensible Windows application that makes browsing, viewing, and organizing large collections of files feel like one continuous activity.**

Carousel visual updates should coalesce during rapid scrolling without losing navigation steps. Keep surviving tiles stable, cancel obsolete thumbnail work and fade entering/departing edge tiles without delaying input or removal. Honor the Windows animation preference.

Fullscreen reveal follow-up: top hover now shows the actual native caption with the toolbar and remains active over nonclient title-bar controls. Left hover reveals the sidebar and folder tree together, offsetting the tree beside the sidebar and retaining both while the pointer is within their combined area. Pin state and normal-mode reveals remain unchanged; video exclusions follow the offset tree. `.jfif` routes through the WIC image viewer and WIC thumbnail generation, with a JPEG-encoded fixture covering decoding, image capabilities, metadata, thumbnails and source-file release.
