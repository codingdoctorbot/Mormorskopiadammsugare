# Mormorskopiadammsugare – Task checklist

Tick boxes as you go (`[x]`). See `ARCHITECTURE.md` for the spec of every component.

## Phase 0 – Setup  ✅
- [x] Install .NET SDK (now .NET 10, machine-wide; see HANDOFF "Dev environment")
- [x] Create solution: Core lib, WPF app, xUnit tests
- [x] Retarget to .NET 10, xUnit v3 + Microsoft.Testing.Platform, `global.json`
- [x] Publish profile `Portable.pubxml`; scripts run without `-ExecutionPolicy Bypass`
- [x] GitHub repo (private) + Actions CI (build, test, portable artifact)
- [x] `ArchitectureTests`: Core must not reference UI assemblies
- [x] NuGet: MetadataExtractor, Microsoft.Data.Sqlite, CommunityToolkit.Mvvm
- [x] `Directory.Build.props`, `.gitignore`, `app.manifest` (longPathAware)

## Phase 1 – Extract photos + videos  ✅ prototype (2026-10-05)
Core (`src/PhotoSorter.Core`):
- [x] 1.1 `Models/MediaFormat.cs` – `MediaKind`, `MediaFormat`, families (TIFF/RAW, MP4/MOV/3GP, MKV/WebM)
- [x] 1.2 `Models/ScanOptions.cs`
- [x] 1.3 `Models/ScanProgress.cs` (+ `ExtractionResult`, `RunOutcome`)
- [x] 1.4 `Detection/ExtensionRegistry.cs` – media exts, known-other exts (incl. audio), ambiguous `.ts`
- [x] 1.5 `Detection/SignatureSniffer.cs` – 512-byte header; `ftyp` brand decides photo/video/audio
- [x] 1.6 `Detection/FileClassifier.cs` – `Media | Suspect | Other`; weak signatures need a matching ext
- [x] 1.7 `Scanning/FileEnumerator.cs` – `FileSystemEnumerable`, exclusions, destination excluded, `Count` pre-pass
- [x] 1.8 `Hashing/FileHasher.cs` – one open per file: header read, then hash from position 0
- [x] 1.9 `Catalog/CatalogDb.cs` – schema §4.6, WAL, resume index, dry-run in-memory copy
- [x] 1.10 `Extraction/NameResolver.cs`
- [x] 1.11 `Extraction/CopyService.cs` – `.partial` + rename, timestamps, verify, disk-full detection
- [x] 1.12 `Logging/CsvRunLog.cs`
- [x] 1.13 `Extraction/ExtractionPipeline.cs` – Channels pipeline, crash-recovery reuse of unrecorded copies

Tests: `DetectionTests.cs`, `ExtractionPipelineTests.cs` (1.14–1.16) ✅

App (`src/PhotoSorter.App`):
- [x] 1.17 `ViewModels/MainViewModel*.cs` (one VM, partial files per tab)
- [x] 1.18 `MainWindow.xaml` – Extract + Organize tabs, Fluent theme (follows Windows light/dark)
- [x] 1.19 Folder pickers (multi-select) + drag-and-drop folders from Explorer
- [x] 1.20 `Services/SettingsService.cs` – `settings.json` next to exe
- [x] 1.21 Summary line + "Open destination" / "Show log" buttons (status text instead of a dialog)
- [x] 1.22 Manual test on a real-world pile – done on a generated 10 GB pile (see HANDOFF); real-world feedback welcome

## Phase 2 – Organize: Continent → Year  ✅ prototype (2026-10-05)
- [x] 2.1 `Metadata/MetadataReader.cs` – EXIF, QuickTime/MP4 (creation date + ISO 6709 GPS), AVI
- [x] 2.2 `FileNameDatePatterns` (in `Metadata/DateResolver.cs`)
- [x] 2.3 `Metadata/DateResolver.cs` – metadata → file name → folder name → file time (opt-in)
- [x] 2.4 `tools/GeoPrep` → `src/PhotoSorter.Core/Geo/countries.gz` (embedded)
- [x] 2.5 `Geo/ContinentLocator.cs` + tests (22 places incl. Istanbul both sides, Chukotka, French overseas)
- [x] 2.6 `FolderLayout` (in `Organizing/Organizer.cs`)
- [x] 2.7 `Organizing/Organizer.cs` – `AnalyzeAsync` (preview) + `ApplyAsync` (move)
- [x] 2.8 Organize tab with preview tree + "Organize automatically afterwards"

## Next – from real-world testing
- [x] 10 GB end-to-end test against an independent answer key, incl. cancel + resume
- [x] Real sample media (58 files from the public metadata-extractor test corpus: iPhone/Android/camera JPEG, HEIC, RAW, MOV, MP4, AVI): 56/58 dated from metadata, all files with real GPS placed correctly (MOV + MP4 GPS included). Files without GPS were confirmed to have none (void/empty/0,0 GPS blocks)
- [ ] Camcorder MTS/M2TS samples – still untested (none in the corpus)
- [ ] XMP dates (`photoshop:DateCreated`, `xmp:CreateDate`) – not read yet (ARCHITECTURE §5.2 step 2)
- [x] Best name among copies when organizing (`Nokia 6.1.mp4` beats `FILE0043.mp4`, `(1)`/`- Copy`/`kopia` lose) – `NameResolver.PreferredBaseName`
- [ ] Progress by bytes rather than file count (big videos make the bar uneven)

## Phase 4 – Polish (optional)
- [ ] Thumbnail preview, duplicate review screen, pause/resume button
- [ ] Guess location from folder names (`Thailand 2012`) for photos without GPS
- [ ] ZIP scanning
