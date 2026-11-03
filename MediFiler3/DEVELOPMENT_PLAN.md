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

1. Define operation records, journal states, and application-owned journal contracts. Add the SQLite dependency in Infrastructure and create/reopen the database safely.
2. Implement journal persistence for prepared, completed, failed, and interrupted operations. Persist intent before a filesystem mutation and verify the result before marking it complete. Add startup recovery/reporting for prepared operations left by a crash.
3. Add filesystem operation services behind application contracts, with conflict checks and safe move/rename/create/Recycle Bin behavior. Do not allow permanent-delete fallback when Recycle Bin operations fail.
4. Add undo/redo as verified compensating operations, batch grouping, UI refresh, and user-facing conflict/error results.
5. Add automated domain/application tests and isolated temporary-directory filesystem tests for success, conflicts, failures, restart recovery, undo/redo, and batches. Build and update project status as each step lands.

## Phase 3 — Media and text handlers

Add basic video and audio playback, bounded text viewing, unsupported-file details and open-with behavior, relevant metadata, and handler-specific interaction routing. Keep file navigation state centralized.

## Phase 4 — Home and persistence

Persist recent, frequent, and favorite folders, settings, window/panel layout, and thumbnail cache metadata. Reconcile filesystem changes and identify missing or inaccessible folders.

## Phase 5 — Alternative viewing modes

Add a virtualized gallery with mode-local multi-selection and grouped operations, then a continuous image reader with lazy loading, bounded preloading, resource release, and synchronized folder/file context.

## Phase 6 — Refinement and optional scope

Add search/filtering, drag and drop, expanded metadata, duplicate detection, additional handlers, themes, and performance/usability refinements as separately reviewable work.

## Working rules

- Keep WPF as the host. Use WinUI controls only where their built-in Windows behavior materially helps; the navigation rail is hosted in an XAML Island.
- Keep the app navigation rail permanently compact and icon-only. Full hiding remains programmatically available for future fullscreen mode.
- Build after changes. Add test projects and automated coverage for business logic and filesystem operations as those features are implemented; the repository currently has no test projects.
- Update [PROJECT_STATUS.md](PROJECT_STATUS.md) when a step changes status, and keep architecture/specification decisions synchronized.
