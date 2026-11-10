# Phase 3 integration results

Date: 2026-11-10. Platform: this Windows 11 x64 development machine, existing WPF host and Windows App SDK 2.6.4 XAML Islands.

Status: automated native integration has passed; the user confirmed Phase 3 passes manual interactive review on 2026-11-10. The initial integration pass reproduced no production-code defects. The subsequent animated GIF implementation adds automatic playback with twelve additional native regression checks, including one-cycle duration metadata.

The manual acceptance below supersedes earlier pending-check notes: popup/high-DPI appearance, focus/input, video overlay reachability, audible output/artwork, native external opening, and Browse mutations/undo/redo with active media are accepted by the user. Codec/fixture coverage limitations remain as documented.

## Root-history regression run

195 unit/filesystem tests and 101 native checks passed, zero failures/skips. RootOperationHistoryTests covers isolated undo/redo/branch invalidation, cross-root batches, identity-preserving root rename, overlapping nodes and schema-4 upgrade/rollback. RootHistoryChecks.cs constructs a real MainWindow and verifies startup/root-switch toolbar availability, scoped tooltips and Browse command guards. Native build/run used a temporary BaseOutputPath because the user app locks normal output; physical acceptance of the new behavior remains open.

## Home refinement regression run

188 unit/filesystem tests and 99 native checks passed, zero failures/skips. Added formatter boundaries/timezone cases, native card containment during grow/shrink up to 3000 px, tooltip-only paths/exact dates, Locate/Remove-only actions and unified all-list removal. Native compact Home capture inspected. Earlier Ignore/recent-only checks below are historical and were replaced. Physical resize/DPI review remains open.

## Phase 4.4 Home regression run

167 unit/filesystem tests and 99 native checks passed, zero failures/skips. HomeChecks.cs exercises real native cards and automation peers for star/unstar/open/Ignore/Locate/Remove, cancellation/conflict preservation, recent-only removal, responsive spacing, empty Add folders, reattachment/reopen and slow/stale availability. Native RenderTargetBitmap capture home-cards.png is saved with isolated runner fixtures and was inspected for layout/contrast. Physical new Home keyboard/focus/DPI/picker/drop and visual review remain open. Existing codec fixture limits still apply.

## Phase 4.3 regression run

167 unit/filesystem tests and 89 native checks passed, zero failures/skips. Native root-history coverage verifies normalized duplicate roots count once per explicit load, reopened roots update shared usage, failed loads are not recorded, and refresh/subfolder selection never create visits or child history. Home controls remain 4.4.

## Rendering regression run

88 native checks passed with zero failures/skips after retaining the opaque matching Settings slider background and reverting the experimental folder-tree popup at the user's request. Original WPF tree/video clipping behavior is restored; the popup-specific test was removed.

## Phase 4.2 regression run

157 unit/filesystem tests and 87 native checks passed with zero failures/skips. Added SettingsAndRefreshChecks.cs covers first-media restored volume/mute with paused opening, carousel/pin restoration, refresh after external insertion/removal, nested file retention and missing-folder/root fallback. Existing native fixture/codec limits still apply. New physical F5/button interaction and cross-process UI restart appearance have not been manually reviewed.

## Verification

- Existing unit/filesystem suite: 141 passed, zero failures.
- Explicit Windows integration runner: 81 passed, zero failures, zero skips with the complete local fixture set.
- Solution build: zero warnings/errors. Diff whitespace checks pass.

The repeatable runner is [tests/MediFiler3.App.Integration](../tests/MediFiler3.App.Integration/README.md). It uses the actual ViewerHostView, real WIC decoding/thumbnail generation, filesystem/text services, centralized navigation, native toolbar, and Windows MediaPlayer. Application association and launching are stubbed to avoid opening other applications. The existing external-opening service tests cover those contracts independently. The solution builds the runner; execute it explicitly as documented rather than through `dotnet test`.

## Format matrix

| Content | Routed formats exercised | Native result |
|---|---|---|
| Images | JPG, JPEG, PNG, BMP, GIF, TIF, TIFF, WebP | Viewport decoding, original dimensions, frozen bitmap, image capabilities, PNG thumbnail, exclusive file reopening passed. Animated GIF playback additionally passes timing, looping, transparency, offset/disposal composition, lifecycle and one-cycle duration metadata checks. Animated WebP is outside current scope. |
| Text | TXT, MD, LOG, CSV, JSON, XML, YAML, YML, INI, CFG, CONFIG, CS, XAML, HTML, HTM, CSS, JS, TS, SQL, PS1, SH, SRT, NFO, TOML, README, LICENSE | Plain-text content, word count, contextual controls and surface selection passed. These are text routing fixtures, not parsers for each syntax. |
| Encodings | UTF-8 without/with BOM; BOM-marked UTF-16 LE/BE and UTF-32 LE/BE | Unicode text preserved; six encodings passed. |
| Video | MP4, M4V, WMV, WebM, MKV | Paused opening, duration/video height, muted playback progress, jump/stop, suspension/rename/reopen and tab restoration passed. |
| Audio | MP3, PCM WAV, WMA, M4A, ADTS AAC, Opus | Paused opening, duration without video resolution, muted playback progress, jump/stop, suspension/rename/reopen and tab restoration passed. |
| Fallback | Unknown extension with/without thumbnail; corrupt PNG/text/MP4; missing image | Clear fallback with external-opening availability, correct capabilities, and no stale media/text surface passed. |

Generated static image fixtures are 64x32 WPF-encoded bitmaps. Animated GIF fixtures are tiny deterministic GIF89a streams with explicit delays, palettes, transparency, frame offsets, disposal flags and loop extensions, generated by GifChecks.cs without another encoding dependency. WebP is Google's 550x368 gallery sample (`https://www.gstatic.com/webp/gallery/1.webp`), previously downloaded and decoded on this machine. Media fixtures reuse the earlier W3C Sintel MP4/WebM and official Opus sample. MP3, WMA, M4A, ADTS AAC, and WMV fixtures were generated from the MP4 through Windows MediaTranscoder, trimmed to two seconds. The M4V fixture is the valid MP4 container under its M4V alias. The MKV fixture is the WebM/Matroska subset under an MKV extension; this does not establish support for arbitrary MKV codecs, tracks, subtitles, attachments, or container variants. No codecs were installed.

The local complete fixture set is retained in `$env:TEMP\MediFiler3Phase3Fixtures`. Reproduce this machine's full run with:

```powershell
dotnet run --project tests/MediFiler3.App.Integration -- "$env:TEMP\MediFiler3Phase3Fixtures"
```

Supplied media was copied into temporary runner-owned directories before mutation checks. The runner does not perform permanent deletion, mutate the original samples, or launch external applications. Actual playback progress was required, rather than relying only on controller state.

## Cross-content results

- Rapid image/text/audio/fallback selections rejected stale metadata and presentations.
- Interrupting a native media open left the next text document intact and released the old file.
- Delayed artwork completion after leaving audio did not update the new file.
- Mixed folder enumeration and centralized selection/next-file navigation kept file identity and viewer metadata synchronized. Backward folder overscroll presents the previous folder's last file; asynchronous unit coverage verifies empty-folder skipping, last-file position and continued reverse navigation, with forward entry unchanged.
- WPF wheel events navigated in navigation mode, zoomed without navigation for images, and did not navigate in text scrolling mode.
- Native image/text/media contextual groups followed capabilities; Open With stayed enabled for all selected file types.
- Large text remained bounded to 1 MiB windows, previous/next windows worked, and leaving/returning to Browse preserved the current window.
- Resizing retained the native media session; text insets remained symmetric and clear of the folder-panel allowance.
- Media suspension released files for rename and reopened paused with shared mute intact. Tab deactivation allowed exclusive file reopening and returned paused.
- Clearing selection removed metadata, contextual controls and external-opening availability.
- Unsupported viewer right-click forwarded ChooseApplication with the captured file path, with/without a thumbnail; image/text/media right-click remained functional and empty/inactive viewers did not launch. The launch service was a recording stub; the user subsequently accepted the real native chooser during manual review.

The existing unit/filesystem suite additionally covers navigation boundaries, coordinator lifetime/cancellation, malformed text and Unicode window boundaries, shared audio settings, operation conflict/journal/undo/redo behavior, and external-opening/association contracts. The native runner's rename checks exercise the viewer suspension seam; the user subsequently accepted full Browse dialogs, folder operations and journal undo/redo with active media during manual review.

## Interactive acceptance (accepted by the user on 2026-11-10)

1. Review popup scale, clipping and positioning, seekbar sizing, metadata/path alignment and text readability at ordinary and high DPI, including movement between monitors.
2. Use physical mouse/keyboard input to verify WPF/WinUI focus transfer, Tab, Space, arrows, F4, wheel, right-click mute/playback, jumps, seek dragging and text selection/copying.
3. Reveal/pin the folder tree and carousel over video, animate them, resize the window, and verify their input/reveal edges remain reachable. Native region clipping does not composite WPF translucency over video.
4. Verify audible output, volume, mute, natural playback completion and representative files from the user's library, including additional MKV/WebM codec combinations and audio artwork.
5. Exercise Windows default-app launching and Open With selection/cancellation from integrated and fallback content; confirm association refresh and focus return.
6. Exercise Browse rename/move/Recycle Bin, folder operations, conflicts/cancellation and undo/redo while video/audio is selected or playing. Confirm release, paused reload and selection refresh.

The user confirms these interactive checks pass. Phase 3 is complete. Cross-session volume/mute/settings persistence remains Phase 4.

The [Phase 4 handover](PHASE4_HANDOVER.md) records step 4.1 completion and the remaining persistence work. Native fixture/codec limits above remain unchanged by user interactive acceptance.
