# MediFiler3

> **Disclaimer:** The entire project was coded with agentic AI as a learning exercise in using AI agents for software development.

MediFiler3 is a Windows desktop app for browsing, viewing, and organizing files already on your computer. Use it to review a photo folder, read a sequence of scanned pages, preview media, or sort a collection into folders. It works locally without an account or cloud service.

## Download and run

1. Open the [v1.1 release](https://github.com/DavidS1998/MediFiler3/releases/tag/v1.1) and download `MediFiler3-1.1.0-win-x64.zip` from **Assets**.
2. Extract the entire ZIP into a folder of your choice.
3. Open the extracted folder and run `MediFiler3.App.exe`. Keep the files and subfolders together.

This release is for 64-bit Windows on Intel or AMD PCs. The ZIP includes .NET and the Windows App SDK. Media playback and some image formats depend on the codecs available on your Windows installation.

## Open a folder and choose a view

On **Home**, click **Add folders** or drop a folder onto the window. Select a folder in the tree to see its files. You can load several folders as separate roots and switch between them in the tree. Star a folder on Home to add it to **Quick folders** in the sidebar.

| View | What you can do |
| --- | --- |
| **Browse** | View one file at a time. Use the arrow keys or click a thumbnail in the bottom carousel to switch files. Preview images, play audio and video, and read plain text. Press **Ctrl+Shift+Enter** to choose another app for the current file. |
| **Gallery** | Scan a grid of thumbnails. Use **Ctrl+click** to select individual files or **Shift+click** to select a range. Double-click a file to open it in Browse. |
| **Reader** | Scroll through the folder's images as one continuous document. Choose fit to width or height, use the page field and navigation buttons, or press **Ctrl+G** to jump to a page. File organization commands are available in Browse and Gallery. |

## Search, filter, and sort

Press **Ctrl+F** or type into the search box in the title bar. Search matches text in filenames, including extensions, without regard to case. The same results feed Browse, Gallery, and Reader, where only matching images appear as pages.

Open **Filters** to include or exclude filename text, select file categories, or sort by name, size, creation date, or modification date. Turn on **Include subfolders** to search the selected folder and its descendants. Keep **Group files by subfolder** on to see each folder's files together, or turn it off to sort all results together.

Save a filter to use it again after restarting. Clearing the search box keeps your other criteria. **Clear filter** resets the search and filter options together.

## Organize files

**To move a file, right-click the destination folder in the folder tree.** In **Browse**, this moves the file you’re currently viewing. In **Gallery**, it moves all selected files. You can also drag a Gallery selection onto the destination folder.

- Press **F2** to rename a single file or **Delete** to send the current file or Gallery selection to the Windows Recycle Bin. MediFiler3 asks for confirmation when recycling multiple files.
- Use **Ctrl+Z** and **Ctrl+Y** to undo and redo supported operations for the active root. If a file with the same name already exists, review the comparison before choosing **Rename**, **Overwrite**, or **Cancel**.
- When an overwrite can be done safely, MediFiler3 saves the replaced item in a managed backup. **Settings → System** lets you review these backups. Eligible backups expire after 30 days, and removing them can remove the associated Undo/Redo history.

## Handy shortcuts

| Shortcut | Action |
| --- | --- |
| **1–5** | Switch to Home, Browse, Gallery, Reader, or Settings |
| **Ctrl+F** | Focus file search |
| **F3** | Pin or unpin the folder tree |
| **F5** | Refresh the current view |
| **F11** | Toggle fullscreen |
| **Ctrl+A** | Select all visible files in Gallery |
| **Ctrl+G** | Go to a page in Reader |

## Troubleshooting

If a preview fails, try opening the file in its usual Windows app from Browse. If MediFiler3 itself cannot start, check `%LOCALAPPDATA%\MediFiler3\startup-error.log`. When reporting a problem, include the app version, your Windows version, and the steps that led to it.
