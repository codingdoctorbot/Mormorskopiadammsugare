# Next session – agenda

*Written at the end of session 9 (2026-10-06). Rewrite this file at the end of every session.*

Read [HANDOFF.md](../HANDOFF.md) first. Then work through this list top to bottom; ask the owner about the
open questions before starting the optional work.

## 1. v0.2.0 is out – collect feedback

Released 2026-10-06 (verified); the owner's desktop runs the official build. Ask how move mode went on the real
pile (KeptInSource reasons in the log – any surprising ones?), and how do the best guesses and
`_Stock & memes` look (Preview → status line counts)?

### Releasing (for the next version)
1. Move CHANGELOG *Unreleased* items under `## [x.y.z] – <date>`; write `docs/releases/vx.y.z.md`; bump
   `<Version>` in `Directory.Build.props`.
2. Commit, push, wait for a green `build` run (`gh run watch <id> --exit-status`). If GitHub Actions is down
   (githubstatus.com → Actions), wait – never build releases locally.
3. `git tag -a vx.y.z -m "Mormorskopiadammsugare vx.y.z"` → `git push origin vx.y.z` → watch `release.yml`.
4. Verify like a stranger: `gh release download vx.y.z`, `gh attestation verify <exe> --repo
   codingdoctorbot/Mormorskopiadammsugare` (exit code 0; `--format json` shows the details; the plain output
   may not be visible in some shells), `Get-FileHash` vs `SHA256SUMS.txt`, start it once.

## 2. Open questions for the owner

1. **Was "Use the file's modified date" ticked** in the first real run? The year distribution suggests so
   (every file dated, spikes in a few years, 12 files in 1980). If yes: untick → Preview → Organize fixes it
   (files without a real date move to `_Unknown year`; nothing is copied again).
2. **May we read the first 32 bytes** of the 43 suspect `.mov`/`.mp4` files in `Extracted\_suspect`?
   (Only the header, never the content.) If they play in a video player, our detection misses a container
   variant – add its signature + a test.
3. **Still want a "Check all copies" button?** Move mode already checks each kept copy before deleting a
   duplicate, so this is now mainly for peace of mind before deleting backups by hand.
4. **How did the new best guesses look on the real pile?** (Preview with the option on; the status line
   reports how many got `~Place`, album folders, screenshots/graphics/downloads.) Any wrong album names
   → extend the generic/device folder lists in `BestGuess.cs`.

## 3. Candidate work (in suggested order – confirm with the owner)

a0. **Move mode: remove emptied folders** – after a move run the sources keep thousands of empty folder
   shells. Remove a folder only if it is empty *and* every file it ever held is recorded as removed (catalog),
   bottom-up; never the source folders themselves.

a. **Check all copies** – re-hash every file in the destination and compare with the SHA-256 in the catalog;
   report missing/changed files. The safety step before anyone deletes their old backups. Small: Core method
   + button + tests.

b. **Suspect videos** – depends on question 2. Classic QuickTime files with an unusual first atom, or other
   container variants, would be a real detection gap.

c. **Location from folder names** – `Thailand 2012`, `Rom 2009`, `Skåne` → a second source for the
   `~Place` best guess (same-time GPS borrowing exists since session 8). Needs a place-name list (countries in
   English + Swedish, Swedish län and big cities), a word-boundary matcher, and the "nearest folder wins,
   skip backup folders" rule. Record it in `guess_reason`.

c2. **Year guesses** for `_Unknown year`: dominant year of the other photos in the same source folder, or the
   oldest file date among all copies – as `~2009` subfolders, same marking rules as place guesses.

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
