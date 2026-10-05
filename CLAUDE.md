# Mormorskopiadammsugare – agent notes

Start with [HANDOFF.md](HANDOFF.md) (status, decisions, design rules, dev environment), then
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/TASKS.md](docs/TASKS.md) and the latest entry in
[docs/SESSIONS.md](docs/SESSIONS.md), and [docs/NEXT_SESSION.md](docs/NEXT_SESSION.md) for the agenda.
At the end of every work session: update HANDOFF.md (Current status), tick TASKS.md, add a SESSIONS.md
entry, rewrite NEXT_SESSION.md, and note user-visible changes under *Unreleased* in CHANGELOG.md.

- Build + test: `dotnet test --solution PhotoSorter.sln` (or `.\scripts\build.ps1`). The tests run on
  Microsoft.Testing.Platform, which is configured in `global.json`.
- Portable publish: `.\scripts\publish.ps1` → `dist\Mormorskopiadammsugare\Mormorskopiadammsugare.exe`.
- No workarounds: don't use `-ExecutionPolicy Bypass`, don't install private SDKs, don't set `DOTNET_ROOT`.
  If a tool is missing, say so and install it properly.
- Write helper tools in C# (e.g. `tools/GeoPrep`), not Python.
- Never use personal photos as test data; generate files or download public samples, and never commit media.
