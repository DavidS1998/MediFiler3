# Development plan

This plan tracks the agreed implementation sequence. Product requirements and full acceptance criteria are in [SPECIFICATION.md](SPECIFICATION.md); architecture decisions are in [ARCHITECTURE.md](ARCHITECTURE.md); the current implementation snapshot is in [PROJECT_STATUS.md](PROJECT_STATUS.md).

## Phase 1 — Foundation and core browsing (complete)

Deliver a responsive folder browser with an image viewer. Check the feature acceptance criteria while implementing each step; 1.8 is for end-to-end integration and refinement.

| Step | Deliverable | Status |
|---|---|---|
| 1.1 | Solution structure, dependency injection, logging, and WPF application startup | Complete |
| 1.2 | Main shell, Home/Browse navigation, high-contrast styling, full-width WinUI CommandBar and compact WinUI navigation rail in WPF XAML Islands | Complete; UI reviewed |
| 1.3 | Load one or more separate folder roots and display their folder tree asynchronously | Complete |
| 1.4 | Enumerate only the active folder's files, apply Windows Explorer-style numeric sorting, and centralize active folder/file navigation state | Complete |
| 1.5 | Display common images at fit-to-view, preserve aspect ratio, and release viewer resources | Complete |
| 1.6 | Shared asynchronous thumbnail service, caching, placeholders, and file carousel | Complete; UI reviewed |
| 1.7 | Keyboard and mouse file navigation, image right-click zoom toggle, and Numpad plus/minus zoom | Complete; UI reviewed |
| 1.8 | Integration checks and refinement for responsiveness, errors, and usability | Complete; user reports no UI issues |

**Phase 1 acceptance:** load separate roots; browse a folder tree; show that folder's sorted files; display and navigate images; update a carousel; preserve UI responsiveness for large folders. File operations, media playback, persistence, gallery, and continuous reader are later phases.

## Phase 2 — Safe file organization

Establish the persistent SQLite operation journal before implementing broad filesystem mutations. Add move, rename, create-folder, and send-to-Recycle-Bin operations, followed by undo/redo and interrupted-operation recovery. Confirm non-empty folder deletion with Yes/No, never overwrite silently, and show best-effort Recycle Bin restore outcomes. Add tests with isolated temporary directories for domain/application logic and Windows filesystem integration where feasible.

Recommended order:

| Step | Deliverable | Status |
|---|---|---|
| 2.1 | Define operation records/states and journal contract; add SQLite in Infrastructure and initialize/reopen a versioned database safely | Complete |
| 2.2 | Persist append-only journal events and inspect/report prepared operations left by a crash | Complete |
| 2.3 | Add journal/recovery tests before filesystem mutations | Complete |
| 2.4 | Add safe filesystem operation services, conflict handling, and folder/file actions in the UI | Complete |
| 2.5 | Add verified undo/redo, batch grouping, UI refresh/results, and filesystem-operation coverage | Complete: Shell restore, Undo/Redo hover previews, and automated coverage implemented; Windows behavior confirmed by the user |

Implementation order:

1. Define operation records, journal states, and application-owned journal contracts. Add the SQLite dependency in Infrastructure and create/reopen the database safely.
2. Implement journal persistence for prepared, completed, failed, and interrupted operations. Persist intent before a filesystem mutation and verify the result before marking it complete. Add startup recovery/reporting for prepared operations left by a crash.
3. Add automated tests for journal creation/reopen/migration, event transitions, redo invalidation, and recovery classifications before filesystem mutations.
4. Add filesystem operation services behind application contracts, with conflict checks and safe move/rename/create/Recycle Bin behavior. Do not allow permanent-delete fallback when Recycle Bin operations fail. **Complete:** file/folder move and rename, folder creation, Recycle Bin actions, and UI integration are implemented; cross-volume folder moves are rejected safely.
5. Add undo/redo as verified compensating operations, batch grouping, UI refresh, user-facing conflict/error results, and isolated temporary-directory filesystem tests. **Complete:** move/rename and safe folder-creation compensation, grouped file moves, toolbar controls, and filesystem coverage are implemented. Undo and redo button tooltips show the planned changes before the user clicks; large batches show the first eight entries and the remaining count. Clicking a button or using its keyboard shortcut applies immediately, with the existing conflict checks. Recycle Bin restore uses a Windows Shell namespace adapter with strict path/type/identity matching and a file-content fingerprint fallback when restore changes the Windows file ID. The user confirmed the Shell restore and tooltip behavior.

## Phase 3 — Media and text handlers

The pre-Phase-3 performance prerequisites now publish shallow roots first, populate the hierarchy through a cancellable breadth-first background stream with bounded UI updates, and then populate file counts progressively. Unknown placeholders, child-discovery completion state, cancellation, and stale-count protection are implemented. Tree virtualization and large expanded-tree rendering/memory remain future performance concerns.

Add basic video and audio playback, bounded text viewing, unsupported-file details and open-with behavior, relevant metadata, and handler-specific interaction routing. Keep file navigation state centralized.

Recommended order:

1. Review the current Browse viewer and define the handler capability boundary for file detection, loading, presentation, metadata, resource lifetime, and input routing.
2. Resolve the playback backend and initial video/audio format set. Prefer a small Windows-compatible solution for basic playback; avoid a broad codec runtime unless a demonstrated format requirement justifies it.
3. Resolve text-viewer encoding and size/read-window limits. Implement bounded reads, scrolling, and clear handling for unreadable or binary files.
4. Implement basic audio/video playback and controls, route interaction so playback gestures do not trigger file navigation, and preserve central navigation state.
5. Add handler metadata and unsupported-file details with an Open With fallback. Test cancellation, resource release, large text files, unsupported formats, and navigation while interacting with content.

Open decisions before implementation: the video/audio backend and supported formats, text encodings and size limits, and the placement of file metadata in the toolbar or a details view. No Phase 3 backend or limits have been selected yet.

Agreed scope after the read-only design review: metadata comprises file size for all files, image resolution, and duration for video/audio. Modularize the toolbar zoom group so it is visible only for images and replaced by appropriate controls for other content types. Before Step 1, implement folder-tree `(direct/total) - Name` prefixes; this prerequisite is complete. Handler implementation has not started; backend, text limits, and metadata placement remain pending approval.

## Phase 4 — Home and persistence

Persist recent, frequent, and favorite folders, settings, window/panel layout, and thumbnail cache metadata. Reconcile filesystem changes and identify missing or inaccessible folders.

## Phase 5 — Alternative viewing modes

Add a virtualized gallery with mode-local multi-selection and grouped operations, then a continuous image reader with lazy loading, bounded preloading, resource release, and synchronized folder/file context.

## Phase 6 — Refinement and optional scope

Add search/filtering, drag and drop, expanded metadata, duplicate detection, additional handlers, themes, and performance/usability refinements as separately reviewable work.

## Working rules

- Keep WPF as the host. Use WinUI controls only where their built-in Windows behavior materially helps; the navigation rail is hosted in an XAML Island.
- Keep the app navigation rail permanently compact and icon-only. Full hiding remains programmatically available for future fullscreen mode.
- Build after changes. Maintain automated coverage for business logic and filesystem operations in `MediFiler3.Infrastructure.Tests` as features are implemented.
- Update [PROJECT_STATUS.md](PROJECT_STATUS.md) when a step changes status, and keep architecture/specification decisions synchronized.
