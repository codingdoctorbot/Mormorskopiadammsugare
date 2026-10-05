# Mormorskopiadammsugare – agent notes

Start with [HANDOFF.md](HANDOFF.md) (status, decisions, design rules, dev environment), then
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/TASKS.md](docs/TASKS.md).
Update HANDOFF.md + TASKS.md at the end of every work session.

- Build + test: `dotnet test --solution PhotoSorter.sln` (or `.\scripts\build.ps1`). The tests run on
  Microsoft.Testing.Platform, which is configured in `global.json`.
- Portable publish: `.\scripts\publish.ps1` → `dist\Mormorskopiadammsugare\Mormorskopiadammsugare.exe`.
- No workarounds: don't use `-ExecutionPolicy Bypass`, don't install private SDKs, don't set `DOTNET_ROOT`.
  If a tool is missing, say so and install it properly.
- Write helper tools in C# (e.g. `tools/GeoPrep`), not Python.
- Never use personal photos as test data; generate files or download public samples, and never commit media.
