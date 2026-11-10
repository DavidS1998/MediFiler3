# Windows viewer integration runner

Run explicitly on Windows 11 x64 with .NET 10 and the Windows App Runtime required by the application:

```powershell
dotnet run --project tests/MediFiler3.App.Integration
dotnet run --project tests/MediFiler3.App.Integration -- C:\path\to\fixtures
```

This executable is built with the solution but is deliberately separate from `dotnet test`: it initializes a real WPF dispatcher and WinUI XAML Islands in a transparent, non-taskbar window. Playback runs muted at zero volume. External launches and application chooser calls are stubbed. No existing user files are renamed or changed: supplied fixtures are copied into a new temporary directory, whose path is printed for inspection. Generated fixtures are retained there.

The default run creates JPEG/JPEG alias, PNG, BMP, GIF, TIFF/TIFF alias, every supported text extension and README/LICENSE, all six text encodings, large text, silent PCM WAV, and invalid/unsupported files. Supply one real file per additional extension: `.webp`, `.mp4`, `.m4v`, `.wmv`, `.webm`, `.mkv`, `.mp3`, `.wma`, `.m4a`, `.aac`, `.opus`. An optional supplied `.wav` replaces the generated WAV. Use valid contents matching each extension; renaming an unrelated container does not establish format support. Missing fixtures are reported as skips; decoder/playback/assertion failures set exit code 1. A complete format pass must have zero skips.

Checks exercise real image decoding and PNG thumbnails, metadata and surfaces, text paging/restoration, mixed-folder enumeration and centralized navigation, WPF wheel input, contextual native toolbar groups, playback progress/seek/stop, shared mute, native player lifetime, operation suspension/rename/reload, tab changes, resizing, interrupted opens, stale artwork/metadata, unsupported thumbnails, missing/corrupt files, and clearing selection. Private toolbar/session fields are inspected only in this test runner to verify the existing UI boundary without adding production testing APIs.

Twelve additional generated animated-GIF checks verify frame delays, transparency/offsets, previous/background disposal, finite/infinite looping, one-cycle duration including zero/missing-delay defaults, logical-canvas dimensions, zoom/fit/resize continuity, file release during playback, tab reload, rapid-selection cleanup, and malformed-GIF fallback. Single-frame GIFs retain the frozen static-image path; animation intentionally uses a mutable canvas.

The runner does not establish subjective appearance, physical mouse/keyboard focus transfer, multiple-monitor DPI behavior, audible output, actual Windows Open With/default-application launching, or every codec/container combination. See [the Phase 3 report](../../MediFiler3/PHASE3_INTEGRATION.md) for the current evidence and remaining manual checks.
