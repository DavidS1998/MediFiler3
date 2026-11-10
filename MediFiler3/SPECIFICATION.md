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

Display folders ranked by how frequently the user opens them.

The frequency counter should persist between application sessions.

### Favorites

Display folders explicitly marked as favorites by the user.

Users should be able to add and remove favorites directly from the folder tree or folder context menu.

### Home screen behavior

- Clicking a folder opens it in the main application interface.
- Each entry should display its folder name and optionally its full path.
- Missing or inaccessible folders should be identified rather than silently removed.
- Users should be able to remove entries from recent history.
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
- Filesystem changes made outside the application should eventually be reflected in the interface.

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
- Initially support common WIC formats including JPEG, PNG, BMP, GIF, and TIFF. WebP also routes through the image viewer and thumbnail decoder using the installed Windows WIC codec. Missing codec/decoding errors retain external-opening actions. The initial GIF and WebP viewers display the first frame; animated playback remains future scope.

Zooming should support viewing the original image resolution without unnecessarily upscaling small images.

### Video

The backend is WinUI MediaPlayerElement with Windows.Media.Playback.MediaPlayer, hosted in a XAML Island within the WPF application. It uses installed Windows media components without a bundled codec runtime. Routed video targets are MP4/M4V, WMV, WebM and MKV; audio targets are MP3, WAV, WMA, M4A/AAC, ADTS AAC and Opus. Extensions route to handlers; codec/container combinations may depend on Windows media extensions. Unsupported variants show an error and retain external-opening actions. FLAC and other extensions remain deferred.

Media opens paused, with Space/right-click or a WinUI toolbar control toggling playback. The right toolbar provides play/pause, stop, back/forward 5-second jumps, a seekbar that stretches to the available toolbar width, current/total playback time, and a compact horizontal volume popup with a clickable speaker/mute icon on the left, a 0-100 slider, and a numeric level on the right. Keep the popup consistent with the compact toolbar: 32-pixel controls, a 16-pixel speaker glyph, 12-point level text, and 8-pixel presenter padding. Jump buttons use Segoe Fluent Rewind (U+EB9E) and FastForward (U+EB9D). Right-clicking the volume button toggles mute without opening the popup. Mute and volume are shared by all media in the current session; muting preserves the selected volume. When settings persistence is implemented, restore/save mute state across application sessions. Playback speed controls are removed. Duration metadata is bracketed, such as `[02:05]` or `[1:02:05]`. Close media before file/folder mutations, undo/redo, navigation, leaving Browse, and shutdown; reload paused when returning or when a cancelled/failed operation leaves the same file selected. Media opening has a cancellable 30-second timeout and clear external fallback on failure. Native video must preserve access to WPF folder/carousel overlays and their reveal edges.

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

Provide distinct Open in default application and Open with actions, including when no thumbnail exists or an integrated preview fails. Show the filename and full path alongside the fallback; retain file size in the toolbar. Ctrl+Enter opens the current file in its default application; Ctrl+Shift+Enter shows the native Windows application chooser. Capture the selected file when invoked, prevent duplicate in-flight requests, and show completion/errors only for the matching active file. Treat chooser cancellation as a normal outcome. Missing associations should explain that Open with can select an application, without automatically launching a second action.

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
| Unsupported | Navigate files | Optional context menu / open externally |

The application should communicate the current interaction mode through subtle visual indicators where necessary.

Avoid relying on right-click alone for essential functionality. All primary interactions should also have keyboard shortcuts or accessible alternatives.

The exact interaction model should be implemented centrally and remain consistent across viewing modes.

# 4. File carousel

A horizontal thumbnail carousel should be displayed along the bottom of the main viewer.

Its purpose is to provide visual context for the currently selected file and allow quick navigation to nearby files.

### Requirements

- Display a configurable number of previous and upcoming files.
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

Each folder row should include a menu button aligned to its right edge. Activating this button opens that folder's WinUI context menu for folder-management actions. Rename file/folder, new-folder naming, and non-empty folder deletion confirmation use standalone WinUI dialogs without a nested `ContentDialog`. Move-folder destination selection uses the basic native folder picker, initially showing the source folder's parent. Keep the row's left-click behavior for folder navigation and its right-click behavior for moving the currently displayed file to that destination folder; the menu button provides a distinct target for folder interactions so those gestures do not conflict. The folder panel has a top-right Pin/Unpin button using the matching Windows UI icons; pinning prevents automatic hiding and reverses the selected button colors while keeping the icons unchanged. Keep the pin state for the current session and persist it with the future settings store.

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

Moving a file should never overwrite an existing file silently.

### Multiple-file operations

The file organization system should eventually support moving multiple selected files simultaneously.

This is particularly important for the gallery view described later.

## 5.3 Folder management

The folder tree should support standard folder operations:

- Create a new folder.
- Rename a folder.
- Move a folder to another location.
- Delete a folder by moving it to the Windows Recycle Bin.
- Add or remove favorites.
- Refresh a folder.
- Open a folder in Windows Explorer.
- Copy a folder path.
- View folder properties.

Destructive operations should require confirmation when appropriate.

Moving a folder to the Recycle Bin should display a warning explaining what will happen, particularly if the folder contains files or subfolders.

The application should use native Windows filesystem operations wherever practical.

## 5.4 Drag and drop

Although right-click interactions are central to the application, drag and drop should also be considered.

Potential functionality:
- Drag files from the gallery onto folders in the tree to move them.
- Drag files from Windows Explorer into the application.
- Drag folders into the application to load them.
- Drag selected files between application panels.

Drag and drop should complement, rather than replace, the primary interaction model.

# 6. Alternative viewing modes

The application should support multiple viewing modes that share the same underlying file collection and organization functionality. Switching viewing modes is a context change: preserve the loaded roots and folder context, but clear mode-specific file selections.

Switching between viewing modes should not require rescanning or reloading the collection.

## 6.1 Gallery view

The gallery view provides an overview of a folder's contents through a grid of thumbnails.

### Requirements

- Display a configurable number of columns or thumbnail sizes.
- Generate thumbnails asynchronously.
- Virtualize the interface so only visible items and a reasonable surrounding buffer need to be rendered.
- Support alphabetical sorting and other sorting options.
- Support selecting individual files.
- Support CTRL+click for selecting multiple files.
- Support SHIFT+click for selecting a range of files.
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
- WinUI controls may be hosted through Windows App SDK XAML Islands when their built-in Windows behavior or consistency provides a clear benefit. Use WinUI `NavigationView` for the primary navigation rail; keep ordinary views in WPF unless there is a specific reason to host them.
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
- Use a discrete WinUI slider with visible value ticks to set the total carousel icon count, including the current file, to an odd value from 1 to 25. Default to 11 icons (five neighbors per side); each slider step changes the total by two. Apply the setting immediately; settings persistence is handled by the later persistence phase.
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
- Refresh current folder.

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

- Undo reverses the most recent successful operation.
- Redo reapplies the most recently undone operation.
- Executing a new operation after undoing clears the redo history.
- Failed operations must not be recorded as successful.
- Operations must retain sufficient information to reverse themselves.
- Filename conflicts must be handled safely.
- Undo and redo should refresh all affected views and navigation state.
- Related batch operations should be grouped into a single logical undo operation.

### Persistence

The operation history should persist across application restarts where practical.

Use a persistent operation journal, preferably backed by SQLite.

The system should handle cases where files have been externally modified, moved, or deleted since an operation was recorded. It must never silently overwrite existing files during undo or redo.

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

The agreed initial metadata scope is file size for all files, image resolution for images, video resolution expressed as pixel height (for example `[1080p]`), and duration for video and audio. Additional metadata below remains future scope.

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
- Application settings.
- Window and panel layout.
- Thumbnail cache information.
- Optional saved collections.

Use a suitable local persistence mechanism.

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
- Add favorites.

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
- Favorites overview.
- Persist settings and navigation preferences.
- Thumbnail caching.

**Goal:** Make the application convenient for repeated daily use.

## Phase 5 – Alternative viewing modes

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
