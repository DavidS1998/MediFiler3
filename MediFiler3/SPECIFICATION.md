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
- Initially support common WIC formats including JPEG, PNG, BMP, GIF, and TIFF. The initial GIF viewer may display its first frame; a future version must support animated GIF playback and WebP images.

Zooming should support viewing the original image resolution without unnecessarily upscaling small images.

### Video

When displaying a video:

- Display an embedded video player.
- Right-click to play or pause playback.
- Provide playback controls, including seeking, volume, and playback speed where supported.
- Support audio output.
- Preserve the ability to navigate to the next or previous file.
- Avoid accidental file navigation while interacting with playback controls.

### Audio

When displaying an audio file:

- Right-click to start or pause playback.
- Provide basic playback controls.
- Display relevant metadata where available, such as title, artist, duration, and album artwork.
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

### Unsupported file types

For file types without an integrated viewer:

- Use the file's thumbnail as the viewer fallback when a thumbnail is available.
- Display basic file information and an indication that no integrated viewer is available.
- Allow the user to open the file using its associated default Windows application.
- Handle missing file associations and launch failures gracefully.

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
- Use generated thumbnails where possible.
- Highlight the currently selected file.
- Update the carousel whenever the current file changes.
- Allow clicking a thumbnail to navigate directly to that file.
- Support horizontal scrolling if the number of displayed thumbnails exceeds available space.
- Handle files without thumbnails using suitable placeholder icons.

The carousel should be generated from the current folder's sorted file list.

Thumbnail generation and retrieval should be cached to avoid unnecessary processing.

# 5. Folder tree and file organization

The folder tree should remain accessible alongside the main viewer.

It should display the hierarchy of the loaded collection and allow the user to perform file and folder operations.

## 5.1 Folder navigation

Left-clicking a folder should load that folder's contents into the main viewer.

The currently selected folder should be visually highlighted.

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

The main application window should consist of independently adjustable panels.

### Main layout

Suggested layout:

- Main viewer occupying the largest portion of the window.
- Folder tree positioned to the left.
- File carousel positioned at the bottom of the viewer.
- Optional toolbar containing navigation, sorting, and viewing-mode controls.

The layout should adapt to different window sizes.

### Windows UI framework integration

- The application remains a WPF desktop application, with WPF owning the top-level window, application startup, and composition root.
- WinUI controls may be hosted through Windows App SDK XAML Islands when their built-in Windows behavior or consistency provides a clear benefit. Use WinUI `NavigationView` for the primary navigation rail; keep ordinary views in WPF unless there is a specific reason to host them.
- Keep the application navigation rail in its compact icon-only state at all times; do not provide a pane expand/collapse toggle. The entire rail must remain programmatically hideable so fullscreen mode can remove it.
- The WPF and WinUI boundary must preserve shared application navigation state and support keyboard focus, resizing, DPI changes, accessibility, and clean shutdown.
- Prefer the native Windows caption and caption buttons. Its caption and text colors should be changeable at runtime, defaulting to black with white text. Use Windows 11 DWM caption color attributes for the WPF top-level window; WinUI `SetTitleBar` applies only to a WinUI top-level window.

### Customization

The user should be able to:
- Show or hide the folder tree.
- Resize the folder tree.
- Resize the main viewer.
- Show or hide the carousel.
- Adjust carousel size and number of thumbnails.
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

The detailed milestone breakdown and current completion status are maintained in the repository-level [DEVELOPMENT_PLAN.md](../DEVELOPMENT_PLAN.md) and [PROJECT_STATUS.md](../PROJECT_STATUS.md).

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
