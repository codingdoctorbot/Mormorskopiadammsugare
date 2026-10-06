# Manual

*[På svenska](MANUAL.sv.md)*

Mormorskopiadammsugare searches your old backups, finds every photo and video, and copies each one
**once** to a new folder (the *destination*). Then it sorts the copies into folders by continent and
year, for example `Europe\2015`.

**Your original files are never changed, moved or deleted.** Everything is copied – if something goes
wrong, your backups are exactly as before.

It works in two steps, one per tab: **1 Extract** (find and copy every unique photo/video) and
**2 Organize** (sort the copies into folders).

## Tab 1 – Extract

| Option | What it does | Why |
|---|---|---|
| **Folders to search** | The folders with your old backups. *Add folders…* (several at once) or drag them in from Explorer. *Remove* only takes a folder off the list. | You decide exactly where it looks. |
| **Destination** | Where the copies go. Pick an **empty** folder on a drive with plenty of space. It gets `Extracted\` (copies land here first) and `_Mormorskopiadammsugare\` (catalog and logs). | Everything new ends up in one place, separate from your backups. |
| **Photos / Videos** | What to look for. Both on by default. | Only want photos right now? Untick Videos. |
| **Skip app caches** | Skips AppData, browser caches and thumbnail folders. On by default. | They're full of small images apps and websites stored automatically – not your photos. |
| **Skip files under 10 KB** | Ignores tiny files. On by default. | Icons, web graphics and thumbnails. Lower it if you have very old, very small pictures to keep. |
| **Verify copies** | Reads every copy back and checks it's identical to the original. Off by default. | Extra safety; slower. |
| **Dry run** | Counts what *would* be copied, copies nothing. | A safe first test before using disk space. |
| **Copy suspect files** | Files named like photos (`.jpg`) that contain something else – often a saved web page – go to `Extracted\_suspect` instead of being left out. | To be sure nothing real is missed. |
| **Organize automatically afterwards** | Starts sorting (tab 2) when extraction finishes. | One click does everything. Leave it off to see the preview first. |
| **Parallel reads** | How many files are read at the same time. Default 4. | 4 is fast on SSDs. Use **1** for old hard drives (HDD) and USB disks – several at once makes them slower. |

**Buttons:** *Start* begins. *Cancel* stops safely – press Start again later and it continues where it
stopped; nothing is copied twice. *Open destination* opens the folder. *Show log* shows everything that
happened, including errors.

**Result tiles:** *Photos/Videos found* in your backups · *New copies* made this time · *Duplicates*
(content already copied – skipped) · *Done earlier* (handled in an earlier run, not even re-read) ·
*Copied* (data) · *Errors* (turns orange – see the log) · *Time*. The line below lists files left out
as too small or as not real photos.

## Tab 2 – Organize

**The year** – first match wins:
1. The date the camera or phone saved inside the photo/video.
2. A date in the file name, e.g. `IMG_20150612_143005`.
3. A year in the nearest folder name, e.g. `Rome 2009`. Folders named like backups (`Backup 2019`,
   `Säkerhetskopia`) are ignored – that's when the backup was made, not when the photo was taken.
4. Only if you tick the option below: the file's *modified* date.

**The place** comes from the GPS position stored in the photo/video, looked up offline. Photos without
GPS go to `_Unknown location` – often the biggest folder, because older cameras, scans and pictures sent
through messaging apps have no GPS. That's normal.

| Option | What it does | Why |
|---|---|---|
| **Add country folders** | `Europe\Sweden\2015` instead of `Europe\2015`. | Easier to find a trip. Switch any time – the next Organize just moves the sorted files. |
| **…and Swedish counties** | (Needs country folders.) Photos from Sweden get a län level: `Europe\Sweden\Skåne län\2015`. Other countries unaffected. | For Swedish photos the country alone is too broad. |
| **Stock photos and memes in a subfolder** | In every folder, files whose names look like stock-site downloads (Shutterstock, iStock, Getty…) or memes and web images (meme sites, Discord, Reddit, Twitter, Tumblr, saved from Facebook/Messenger) go to a `_Stock & memes` subfolder, e.g. `Europe\Sweden\Skåne län\2016\_Stock & memes\`. On by default. | Keeps your own photos uncluttered. Names can mislead, so they stay in the same folder, one level down – easy to check. |
| **Best guesses inside the unknown folders** | Files without GPS or year stay in `_Unknown location` / `_Unknown year`, but are sorted further: **`~Sweden`** when GPS photos taken within 3 hours all agree on the place (e.g. a camera without GPS next to a phone with GPS), the **original album folder** (`Midsommar 2018`) when 3+ files share it, and **screenshots, graphics and downloads** in their own folders. On by default. | The unknown folders are often the biggest – this makes them browsable. The `~` means "best guess"; trusted folders stay trusted. |
| **Use the file's modified date** | Uses that date when nothing better is known. Off by default. | Copying into backups often resets it to the backup day, putting old photos in the wrong year. Without a real date, a photo goes to `_Unknown year` instead. |

**Buttons:** *Preview* shows the tree of how files **will** be sorted – nothing moves yet. *Organize*
moves them. *Cancel* stops; press Preview and Organize again to continue.

## The folders you get

```
Europe\2015\                      known place and year
Europe\_Unknown year\             known place, no year
_Unknown location\2009\           no place, known year
_Unknown location\_Unknown year\  nothing known
_Unknown location\2018\~Sweden\   best guess: same time as GPS photos from Sweden
_Unknown location\2018\Midsommar 2018\   best guess: original album folder
_Unknown location\_Screenshots\   screenshots (also _Graphics, _Downloads)
…\2016\_Stock & memes\            in any folder: stock photos and memes, by file name
Extracted\                        waiting to be sorted (empty after Organize)
_Mormorskopiadammsugare\          catalog + logs – keep it while you still use the program on this folder
```

Same name, different photo → both kept (`IMG_0001.jpg`, `IMG_0001 (2).jpg`).
Same photo, several names → one copy, with the best name.

## Recommended way to use it

1. Start small: one backup folder of a few GB, into a new, empty destination.
2. Optional: tick **Dry run** first to see the numbers.
3. Run it for real, then **Preview** on the Organize tab. Check **Show log** for errors.
4. Happy? Add the rest of your backups and press Start again with the **same destination** – already
   copied photos are recognised and not copied again.
5. Until you're completely done, let the program manage the destination (don't move files in it by hand).
6. When everything is sorted, back up the destination properly – it now holds one copy of each photo and video.

## Limitations

- Only **exact** duplicates are merged; a resized or compressed copy (e.g. sent via WhatsApp) is kept separately.
- ZIP files are not opened.
- MTS, MKV, WMV and MPG videos have no readable date inside; they're sorted by file and folder names.
