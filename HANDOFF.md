# 🔁 HANDOFF – Mormorskopiadammsugare

> **Read this first** (developers and AI coding agents alike). Then **`docs/NEXT_SESSION.md`** (the agenda
> for the next session), `docs/ARCHITECTURE.md` (the spec), `docs/TASKS.md` (the checklist) and the latest
> entry in `docs/SESSIONS.md` (what happened last time).
>
> **At the end of every work session:** update this file's *Current status*, tick `docs/TASKS.md`,
> add an entry to `docs/SESSIONS.md`, rewrite `docs/NEXT_SESSION.md`, and add user-visible changes to
> `CHANGELOG.md` (*Unreleased*).
>
> Code and project names are `PhotoSorter.*` (the working title); the product is called
> Mormorskopiadammsugare ("grandma's copy vacuum cleaner").

## What it does
A **portable** (no install, runs from a folder) Windows desktop app that:
1. **Extract:** scans huge piles of nested "backups of backups" (designed for ~300 GB / ~100k files /
   ~30k folders), finds every **photo and video** (any format, also renamed / extension-less ones),
   de-duplicates by SHA-256 and **copies** each unique file once into a destination folder – or, in the
   optional **move mode**, moves it and removes the duplicates from the sources (undoable).
2. **Organize:** moves the extracted copies into **`Continent\Year\`** (EXIF/QuickTime GPS → offline
   continent lookup; capture date → file-name date → folder-name year). Fallback buckets:
   `Continent\_Unknown year\`, `_Unknown location\Year\`, `_Unknown location\_Unknown year\`.

## Product decisions
| Topic | Decision |
|---|---|
| Copy vs move | **Copy by default.** Optional **move mode** (owner's request, 2026-10-06: "it's redundant in 40k folders, non-findable in disarray – I can back up my new organized stack"): off at every start, confirmed per run, removes an original only after its kept copy is checked, never in program/cloud/read-only places, undoable with *Restore originals* (ARCHITECTURE §4.9). Source files are never *modified*. |
| Layout | **Continent → Year** by default; optional **Continent → Country → Year**, and for Sweden optionally **→ Län →** Year |
| Unknowns | Stay in `_Unknown location` / `_Unknown year`, but get **best-guess** subfolders (option, on by default): `~Place` from same-time GPS photos, album folders, `_Screenshots`/`_Graphics`/`_Downloads`. Guesses are always marked `~` or `_` and never mixed into trusted folders. |
| Videos | Included, mixed with photos in the same folders. Audio is not included. |
| ZIP / archives | Ignored for now |
| Install | End product must need **no install**: one self-contained exe |
| Source folders | Chosen in the UI (folder pickers, drag & drop). Never hardcode a drive. |
| Target | .NET 10 (LTS, supported to Nov 2028) |
| UI language | English |

## Dev environment
| Tool | Notes |
|---|---|
| .NET 10 SDK | Pinned by `global.json` (10.0.x, latest feature band) |
| Tests | xUnit v3 on **Microsoft.Testing.Platform** (opted in via `global.json`, required on the .NET 10 SDK). `dotnet test --solution PhotoSorter.sln` |
| IDE | Optional: Visual Studio 2026 (WPF designer, debugger, Test Explorer) or any editor |
| Scripts | PowerShell 5.1 or 7. Run with a normal `RemoteSigned` execution policy – no bypass needed |
| CI | `.github/workflows/build.yml` (every push), `release.yml` (tags `v*` → release with attestation) |

## Current status  (2026-10-05)
- ✅ **v0.1.0 feature-complete: Extract and Organize both work end to end** in the portable exe
  (`dist\Mormorskopiadammsugare\Mormorskopiadammsugare.exe`, ~64 MB, Fluent UI following Windows light/dark,
  own icon, run statistics as number tiles).
- ✅ Layout options: `Continent\Year` (default), `Continent\Country\Year`, and for Sweden `…\Sweden\<län>\Year`.
  Switching later just moves files.
- ✅ **Best guesses inside the unknown folders** (`Organizing/BestGuess.cs`, ARCHITECTURE §5.5): same-time GPS
  borrowing (±3 h, unanimous), album folders (≥3 files; never source/backup/device/user-profile names),
  screenshots/graphics/downloads apart. Catalog schema v2 (auto-upgrade from v1) records guesses.
  Checked on the 10 GB test pile: only meaningful albums ("Rome 2009", "Fjällen 2012"…).
- ✅ **Stock photos and memes** (by file name) → `…\_Stock & memes\` inside every final folder (option, on by
  default; ARCHITECTURE §5.6). False positives accepted: they stay in the same folder, one level deeper.
- ✅ **Move mode + Restore originals** (ARCHITECTURE §4.9, catalog schema v3). Checked end to end on a 3.1 GB
  real copy of part of the test pile: 316 originals removed, 0 lost, read-only and program-folder photos
  kept, restore gave a byte- and date-identical tree, a second move run removed the same 316 again.
- ✅ 223 tests: format detection, pipeline (dupes, resume, dry run, crash recovery, cancel,
  destination inside source), EXIF/ISO 6709 parsing, date rules, continent/country/län lookups, organizer incl.
  layout switching, best guesses, move mode and restore, schema migration, and a guard against double-encoded source files.
- ✅ **Released v0.1.0** on 2026-10-06 (tag `v0.1.0` = commit `21d3983`), after the 2026-10-05 GitHub Actions
  outage cleared. Built by `release.yml` on GitHub; download, `gh attestation verify` and SHA256SUMS all checked
  against the published exe. **v0.2.0** (move mode + Restore originals) released the same way on 2026-10-06,
  tag `v0.2.0` = commit `1a28d92`, verified. How to make the next release: `docs/NEXT_SESSION.md` → "Releasing".
- ✅ **First real-world run** (owner's own backups, aggregate numbers only): ~100 GB / 52,078 unique
  photos+videos copied in under 8 min, 9,459 duplicates skipped, 46,874 files organized in 44 s,
  **0 errors** in both logs. Findings → `docs/NEXT_SESSION.md` → 2–3:
  - 5,204 "suspect" files (copied because *Copy suspect files* was on) were almost all app/game resources
    (`lib`/`res`/`data` folders); 43 of them are `.mov`/`.mp4` worth a header check.
  - 57,443 files under 10 KB skipped – mostly icons and app resources (incl. 1,914 tiny `.3gp`/`.mov`
    bundled with software); ~50 tiny JPEGs sat in camera-like folders.
  - 100 % of files got a year with spikes in a few years and 12 files in 1980 → the *file modified date*
    option was most likely on (unconfirmed).
  - ~90 % had no GPS → `_Unknown location` – expected; motivates folder-name location guessing.
- ✅ Verified on a generated 10 GB "backups of backups" pile built from Wikimedia Commons
  *featured pictures* (5,974 files: hard-linked backup copies, renamed/extension-less/recovered files,
  595-character paths, junk, caches, fake `.jpg`s): result matched an independent answer key exactly
  (1,055 unique, 1,467 duplicates, nothing missing/extra/doubled, sources untouched), also after
  cancelling mid-run and resuming.
- ✅ Verified on 58 real camera/phone files (JPEG, HEIC, RAW, MOV, MP4, AVI) from the public
  metadata-extractor test corpus: 56/58 dated from metadata; iPhone MOV and Android MP4 GPS read.
- ⚠️ Known gaps: video metadata only from MOV/MP4/3GP (QuickTime) and AVI – MTS/MKV/WMV/MPG rely on
  file/folder names; XMP dates not read; `Extracted\` stays (empty) after organizing; exact
  duplicates only (resized/re-compressed copies are kept).
- ⏭️ Next: **`docs/NEXT_SESSION.md`** (agenda), `docs/TASKS.md` → "Next" (backlog).
- 🧪 A regression test set (10 GB Wikimedia pile + scripts `garble.py`/`verify.py`, real camera samples) was
  built in a temporary folder on the development machine; it may not survive. To rebuild it, see
  `docs/SESSIONS.md` session 5 (Commons *featured pictures* of clean subjects, hard-linked backup copies,
  independent answer key).

### Testing policy
- **Never use anyone's personal photos as test data**, and never commit test media. Use generated files
  (`tests/.../TestFiles.cs`) or public sample sets downloaded to a temp folder.

### Gotchas found while building (don't repeat them)
- An embedded resource named `countries.bin.gz` silently ends up in a *satellite* assembly:
  MSBuild reads `bin` as a culture (the Bini language). The file is `Geo/countries.gz`.
- MTP test runner: `dotnet test` must not get `-nologo` (exit code 5 = invalid argument);
  zero tests = exit code 8.
- `Select-Object -First N` in PowerShell kills the upstream `dotnet build` early – don't pipe builds into it.
- File-based C# apps (`dotnet run x.cs`) default to AOT/trimming; WPF scripts need
  `#:property PublishAot=false`.
- Tests that sort file names must use `StringComparer.Ordinal` – culture sorting differs per machine.
- **Never rewrite text files with Windows PowerShell 5.1** `Get-Content`/`Set-Content`: it reads UTF-8 without
  BOM as Windows-1252 and saves double-encoded text (every å/ä/ö/… becomes two garbage characters). Use an editor, `pwsh` 7, or .NET/Python
  with explicit UTF-8. `ArchitectureTests.Source_files_have_no_double_encoded_text` catches it.
- XML comments (csproj, XAML) can't contain `--`. And `sed` turns `\a` in a replacement into a bell character.
- UI Automation from PowerShell can be very slow, and texts on a hidden tab aren't visible to it. For
  end-to-end checks, drive the Core engine directly or read the catalog (`runs` table) instead.

## How to build / run
Plain `dotnet` commands work. The scripts are thin conveniences around them.

```powershell
# from the repo root
dotnet test --solution PhotoSorter.sln                        # build + tests   (= .\scripts\build.ps1)
dotnet publish src\PhotoSorter.App -p:PublishProfile=Portable # -> dist\Mormorskopiadammsugare\   (= .\scripts\publish.ps1, which also clears dist first)
dotnet run --project src\PhotoSorter.App                      # run the app
```

## Project map
```
PhotoSorter.sln
global.json                – pins .NET SDK 10.0.x + Microsoft.Testing.Platform test runner
src/PhotoSorter.Core/      net10.0          – all logic (no UI), unit-testable
  Detection/   ExtensionRegistry, SignatureSniffer, FileClassifier
  Scanning/    FileEnumerator            Hashing/  FileHasher
  Extraction/  ExtractionPipeline (Extract), CopyService, NameResolver
  Catalog/     CatalogDb (SQLite)        Logging/  CsvRunLog
  Metadata/    MetadataReader, DateResolver + FileNameDatePatterns
  Geo/         ContinentLocator + countries.gz (embedded Natural Earth countries + Swedish län)
  Organizing/  Organizer (AnalyzeAsync preview, ApplyAsync move) + FolderLayout
src/PhotoSorter.App/       net10.0-windows  – WPF shell (MainWindow + MainViewModel*.cs), settings, Assets/app.ico
  Properties/PublishProfiles/Portable.pubxml – portable single-file publish settings
tests/PhotoSorter.Core.Tests/  xUnit v3     – TestFiles.cs builds real JPEG/EXIF/MP4 bytes
tools/GeoPrep/             – regenerates Geo/countries.gz from Natural Earth admin-0 + admin-1 (dotnet run --project tools/GeoPrep)
tools/IconGen/IconGen.cs   – draws the app icon (dotnet run tools/IconGen/IconGen.cs, then copy app.ico to Assets)
docs/ARCHITECTURE.md       – full spec, diagrams, signature table, DB schema
docs/TASKS.md              – phased checklist
docs/SESSIONS.md           – session log: what each work session did, decided and learned
docs/NEXT_SESSION.md       – agenda for the next session (rewritten every session)
docs/MANUAL.md, MANUAL.sv.md – user manual (English / Swedish)
CHANGELOG.md               – user-visible changes per version
docs/releases/             – release notes per tag
scripts/                   – build.ps1, publish.ps1
```

## Key design rules (don't break these)
1. **Sources are never modified.** Open with `FileAccess.Read, FileShare.ReadWrite`. Only move mode removes
   files, and only by the rules in ARCHITECTURE §4.9: record first, check the kept copy, then delete.
2. **Exclude the destination** from the scan (it could be inside a source).
3. Copy via `*.partial` then rename; preserve original timestamps.
4. Per-file error isolation: log & continue, never abort the run (except disk full).
5. All long work = `async Task RunAsync(options, IProgress<T>, CancellationToken)` in Core.
6. Catalog `catalog.db` + CSV logs live in `<Destination>\_Mormorskopiadammsugare\`.
   Re-runs must **resume** (skip source paths already recorded with same size+mtime).
7. Default parallelism = 4 (SSD); option 1–8 (use 1 for HDD/USB).
8. The `ftyp` **brand** decides the type: HEIC/AVIF/CR3 = photo, `isom`/`mp4x`/`qt  `/`3gpx` = video,
   `M4A `/`M4B ` = audio (skip). Weak signatures (ICO, old QuickTime atoms, ASF) need the matching extension.
9. Skip macOS `._*` files, `Thumbs.db`, `desktop.ini`, reparse points.
10. Settings in `settings.json` next to the exe (portable).
11. File modified time is **not** a trusted year source (backups reset it) –
    only used if the user enables the option; otherwise → `_Unknown year\`.
