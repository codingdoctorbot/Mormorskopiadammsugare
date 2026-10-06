# Session log

One entry per work session, newest first: **what** was done, **decisions** taken (and why), and
**lessons** worth knowing next time. Current state lives in [HANDOFF.md](../HANDOFF.md); this file is
the history behind it. Keep entries short and free of personal details.

---

## 2026-10-05 · Session 8 – Best guesses inside the unknown folders

**Done**
- `Organizing/BestGuess.cs` + organizer in four passes: (1) year and GPS place per file, (2) GPS anchors with
  capture times, (3) target folder incl. guesses, (4) album size rule, save, plan.
- Three techniques, all inside `_Unknown…`: `~Place` from GPS photos within ±3 h (must agree), album folders
  (≥3 files), `_Screenshots` / `_Graphics` / `_Downloads`. Option in the UI (on by default), summary in the
  status line, reason in the organize log, catalog schema v2 with guess columns (v1 upgrades in place).
- Preview tree is now built from folder paths (any depth). 191 tests.

**Decisions**
- Guesses never leave the unknown folders and are always marked (`~` / `_`) – trusted folders stay trusted.
- Place guesses only from real capture times (EXIF, QuickTime, AVI), never file dates; disagreement → no guess.
- Album folders only for ≥3 files; never the user's source folders, backups, devices, user profiles, or our
  own folders.

- Later in the session: **`_Stock & memes`** subfolder in every final folder for stock-site and meme/web file
  names (ARCHITECTURE §5.6). 213 tests.

**Lessons**
- Check value against real data before building, but weigh the **cost of a wrong guess** too: the owner's real
  run had only ~85 matching names (0.2 %), yet a wrong guess here only means "one subfolder deeper in the same
  folder", so it was worth building anyway.
- The 10 GB test pile caught three album mistakes the unit tests missed: the **source folder** itself
  ("pile"), **Downloads** as an album, and **device names** ("Camera SD card (2)"). Also: a photo is only a
  "download" if *every* copy is in Downloads. Realistic data before shipping pays off.
- Inline Python in bash heredocs keeps breaking on backslashes (octal escapes like `\201`). Write helper
  scripts to a file, use raw strings or `chr(92)`, or use the editor directly.

## 2026-10-05 · Session 7 – First real-world run, docs

**Done**
- The owner ran the app on part of their own backups and shared **only the CSV logs** (photos and folders
  were not looked at). Analysed them in aggregate: ~100 GB / 52,078 unique files copied in under 8 min,
  9,459 duplicates, 46,874 files organized in 44 s, **0 errors**. Details in HANDOFF *Current status*.
- Docs: session log, CHANGELOG, user manual (EN + SV, also as a desktop `.txt`), NEXT_SESSION agenda.
- Release still blocked: GitHub Actions outage all evening; re-runs cancelled without a runner.

**Decisions**
- Keep *Copy suspect files* and *Verify copies* off by default: the real run showed suspects are almost all
  software resources (still listed in the log), and verify mainly matters before deleting originals.
- Files under 10 KB stay skipped by default; the log lists them, and a later run with a lower limit into the
  same destination adds only those (skipped files aren't recorded as done).

**Lessons**
- Logs alone answer most "did it work" questions – the CSV is enough for counts, errors, formats and
  folder-type patterns without touching anyone's photos.
- A year histogram with 0 unknowns and spikes is a red flag for the *file modified date* option.
- Junk concentrates in software folders (`lib`, `res`, `data`, game libraries): both the "suspect" files and
  the tiny `.3gp`/`.mov` came from there.

## 2026-10-05 · Session 6 – Layout options, icon, readability

**Done**
- Optional **country folders** (`Continent\Country\Year`) and, for photos from Sweden, **län folders**
  (`Europe\Sweden\Skåne län\Year`). Geo data format PSGEO3: per-part names/codes for overseas territories
  (Réunion, French Guiana, Caribbean Netherlands…), friendly country names, Natural Earth admin-1 regions
  for Sweden (21 län, Swedish names).
- App **icon** (vector, `tools/IconGen`), statistics shown as **number tiles**, larger tab headers.
- Test guard against double-encoded source files.
- User manual (English + Swedish).

**Decisions**
- Country and län levels are **options**, not replacements – switching later only moves files.
- Län only below a country folder, and only for Sweden (asked for; other countries unaffected).
- Archipelago/boat points inside Sweden take the **nearest** län (no distance limit – the country is known).

**Lessons**
- Windows PowerShell 5.1 `Get-Content`/`Set-Content` garbled å/ä/ö in XAML (read UTF-8 as Windows-1252).
  Caught in screenshots; now guarded by a test.
- `sed` replacement text turns `\a` into a bell character; XML comments can't contain `--`.
- GitHub Actions outage: jobs failed with "not acquired by Runner". Release tagging postponed – see HANDOFF.

## 2026-10-05 · Session 5 – Scale test, review, public release prep

**Done**
- 10 GB end-to-end test on a generated "backups of backups" pile from Wikimedia Commons featured
  pictures (5,974 files, hard-linked copies, 595-char paths) against an independent answer key – exact match,
  also after cancel + resume.
- Two code reviews; fixed: timestamp failures no longer fail a copy, catalog-update failure moves the file
  back, files whose source was edited keep their name, stale preview discarded when settings change,
  Organize errors reported.
- Software edit date (`ExifModified`) now ranks below file/folder-name dates.
- Renamed to **Mormorskopiadammsugare**, MIT license, third-party notices, release workflow
  (build on GitHub + signed build attestation + SHA256SUMS), scrubbed docs, fresh public repository.

**Decisions**
- Public repo starts from one clean commit (no personal details in history).
- Releases are only built by GitHub Actions, never uploaded from a personal machine – that's what makes
  the attestation meaningful.
- Code names stay `PhotoSorter.*`; only user-visible names changed. Old `_PhotoSorter` folders migrate.

**Lessons**
- UI Automation from PowerShell can stall for minutes; texts on hidden tabs aren't visible to it. End-to-end
  checks now drive the Core engine or read the catalog's `runs` table instead.
- An app's own timestamps (catalog `runs`, CSV logs) beat a test harness's stopwatch.

## 2026-10-05 · Session 4 – Real sample media

**Done**
- Verified metadata reading on 58 real camera/phone files from a public test corpus (JPEG, HEIC, RAW,
  MOV, MP4, AVI): 56/58 dated from metadata; iPhone MOV and Android MP4 GPS read.
- Organizer picks the **best name among copies** (`Nokia 6.1.mp4` beats `FILE0043.mp4`).

**Decisions**
- **Never use anyone's personal photos for testing**; download public samples to a temp folder; never
  commit media.

**Lessons**
- Files "without GPS" were checked tag by tag: they genuinely had none (void fix, empty block, 0,0).

## 2026-10-05 · Session 3 – Prototype

**Done**
- Phase 1 (Extract) and Phase 2 (Organize) end to end: detection by content, SHA-256 de-duplication,
  SQLite catalog with resume, `.partial` copies, crash recovery, metadata, offline continent lookup
  (Natural Earth, embedded), WPF UI. 116 tests.

**Lessons**
- An embedded resource named `countries.bin.gz` lands in a satellite assembly – MSBuild reads `bin` as the
  culture "Bini". Renamed to `countries.gz`.
- MTP test runner: no `-nologo`; zero tests = exit code 8.

## 2026-10-05 · Session 2 – Environment and .NET 10

**Done**
- Proper dev setup instead of workarounds (machine-wide SDK, normal script execution policy, long paths).
- Retargeted to **.NET 10 LTS** (.NET 8 support ends Nov 2026), xUnit v3 on Microsoft.Testing.Platform,
  `global.json`, publish profile, CI.

## 2026-10-05 · Session 1 – Plan

**Done**
- Requirements and plan: copy-only extraction of every unique photo/video from ~300 GB of nested backups,
  then sorting into `Continent\Year`, portable exe, catalog for resume. Spec in `docs/ARCHITECTURE.md`,
  checklist in `docs/TASKS.md`.

**Decisions**
- Copy, never move. Videos included, mixed with photos. ZIP archives ignored for now.
- File modified time is not a trusted year source (backups reset it).
