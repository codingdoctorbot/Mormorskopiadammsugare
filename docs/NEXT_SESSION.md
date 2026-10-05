# Next session – agenda

*Written at the end of session 7 (2026-10-05). Rewrite this file at the end of every session.*

Read [HANDOFF.md](../HANDOFF.md) first. Then work through this list top to bottom; ask the owner about the
open questions before starting the optional work.

## 1. Publish release v0.1.0 (blocked last time by a GitHub outage)

```powershell
# is GitHub Actions healthy again?  (expect "operational")
curl -s https://www.githubstatus.com/api/v2/components.json   # look at "Actions"
gh run list --limit 3                                          # latest build on main
gh run rerun <id of the latest build>                          # if it failed with "not acquired by Runner"
gh run watch <id> --exit-status                                # must end green
git tag v0.1.0
git push origin v0.1.0                                         # triggers .github/workflows/release.yml
gh run watch <release run id> --exit-status
gh release download v0.1.0 -D $env:TEMP\rel                    # then verify:
gh attestation verify $env:TEMP\rel\Mormorskopiadammsugare.exe --repo codingdoctorbot/Mormorskopiadammsugare
Get-FileHash $env:TEMP\rel\Mormorskopiadammsugare.exe          # compare with SHA256SUMS.txt
```

Then: move CHANGELOG *Unreleased* items (if any) under a new version, and update HANDOFF.

## 2. Open questions for the owner

1. **Was "Use the file's modified date" ticked** in the first real run? The year distribution suggests so
   (every file dated, spikes in a few years, 12 files in 1980). If yes: untick → Preview → Organize fixes it
   (files without a real date move to `_Unknown year`; nothing is copied again).
2. **May we read the first 32 bytes** of the 43 suspect `.mov`/`.mp4` files in `Extracted\_suspect`?
   (Only the header, never the content.) If they play in a video player, our detection misses a container
   variant – add its signature + a test.
3. **Want a "Check all copies" button?** (See 3a.) Matters if the old backups will ever be deleted.

## 3. Candidate work (in suggested order – confirm with the owner)

a. **Check all copies** – re-hash every file in the destination and compare with the SHA-256 in the catalog;
   report missing/changed files. The safety step before anyone deletes their old backups. Small: Core method
   + button + tests.

b. **Suspect videos** – depends on question 2. Classic QuickTime files with an unusual first atom, or other
   container variants, would be a real detection gap.

c. **Location from folder names** – `Thailand 2012`, `Rom 2009`, `Skåne` → continent/country/län for photos
   without GPS (~90 % of the real run). Needs a place-name list (countries in English + Swedish, Swedish
   län and big cities), a word-boundary matcher, and the same "nearest folder wins, skip backup folders"
   rule as for years. Show it as a separate source in the catalog (`location_source = FolderName`).

d. **Smarter small-file filter** – skip images below N×N pixels instead of below N KB, so 160×120 early
   camera-phone photos survive while icons are dropped. Header-only read (MetadataExtractor has the
   dimensions).

e. **Skip program/game folders** – the real run's suspects and tiny files concentrated in `lib`, `res`,
   `data`, `steamapps` style folders. Consider an option (default on) that skips folders that look like
   installed software. Be careful: `data` is too generic on its own.

f. **Year transparency** – show in the preview how many years came from metadata / file name / folder name
   / file date, so a wrongly ticked file-date option is visible at a glance.

## 4. Housekeeping

- The private predecessor repo (`PhotoSorter`, old history with personal details) is no longer developed;
  the owner may archive it.
- The local 10 GB regression test set lives in a temporary folder and may disappear; rebuild per SESSIONS.md
  session 5 if needed.
