# Changelog

User-visible changes per version. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] – first public release

### Added
- **Extract:** finds every photo and video in nested backups by content – also renamed, extension-less,
  wrong-extension and recovered (`FILE0001.CHK`) files – and copies each unique one once (SHA-256).
  Skips caches, thumbnails, `Windows\`, macOS `._` files and files that only look like photos.
- **Organize:** preview, then sort into `Continent\Year` using capture dates (EXIF, QuickTime/MP4, AVI),
  dates in file names and years in folder names (backup-folder years ignored); GPS looked up offline.
- Optional **country folders** (`Europe\Italy\2015`) and **Swedish county folders**
  (`Europe\Sweden\Skåne län\2015`); switching layouts later just moves files.
- Same name, different photo → both kept; same photo, many names → one copy with the best name.
- Resume after cancel or crash, dry run, verify copies, CSV logs, catalog in the destination.
- Portable single exe, Fluent UI following Windows light/dark, app icon.
- Release builds made by GitHub Actions with a signed build attestation and SHA-256 checksums.
