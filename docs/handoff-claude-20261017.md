# Handoff: the Windows desktop app, 2026-10-17

From Claude Opus 5.5 (`claude-opus-5-5`), the chat that built the desktop app ("API Fairy - Launch", started 2026-10-07).
Hub card: `findastra/astras-pet-os` `handoffs/017-any-api-fairy-desktop-verify-20261017.md`.

## What I did

**2026-10-07 (the original build):**
- Wrote the Windows desktop app: `src/` (Providers, Dotenv, Scanner, Store, Core, Sprite, Theme, Native,
  FairyWindow, DashboardWindow, Snapshot, App), `tests/Tests.cs`, `build.cmd`, `Start API Fairy.cmd`,
  `assets/fairy.ico`, README.md, SECURITY.md, LICENSE, .gitignore. C# 5, built with the .NET Framework 4.8 compiler
  that ships with Windows; no packages.
- Commit `39cd545` "Start API Fairy: …", made locally in `%USERPROFILE%\Documents\Projects\api-fairy` (that folder
  has since moved to `%USERPROFILE%\Documents\Astra's Pet Apps\api-fairy`). Someone else pushed it on 2026-10-08.
- Malwarebytes quarantined `bin\API Fairy.exe` twice ("MachineLearning/Anomalous.100%") the moment it ran, so the app
  never ran and the README screenshots (`docs/fairy.png`, `docs/dashboard-keys.png`) were never made.

**2026-10-17 (this record), branch `claude/desktop-app-record-20261017`, not on `main`:**
- README: removed the two broken screenshot links; added a `## Limits` section (PET-FILES.md requires one).
- AGENTS.md and pet.json `built_with`: recorded the desktop app's model and date.
- This file.

**Checked on Astra's PC, read only (nothing changed there):**
- Malwarebytes detections for API Fairy: 2026-10-07 (twice, `API Fairy.exe`), 2026-10-08 (`bin\tests.exe`,
  "Trojan.Crypt.MSIL", twice), 2026-10-14 (`%USERPROFILE%\Downloads\api-fairy-main\…\bin\API Fairy.exe`).
- No `%LOCALAPPDATA%\API Fairy` folder (the app has never saved notes), no "API Fairy" Run entry, no API Fairy process.
- The PC copy is on `codex/l3-activity-20261016`, clean. `main` (`fd5adca`) and that branch have diverged.

## Waiting on Astra

1. **Allow the build in Malwarebytes.** Allow List → Add → Allow a file or folder →
   `%USERPROFILE%\Documents\Astra's Pet Apps\api-fairy\bin`. Malwarebytes is your antivirus (Windows Defender
   real-time protection is off). This is a security setting, so only you change it.
2. **Visibility.** `pet.json` says `"visibility": "private"` and docs/publications-20261008.md says "intended private
   repository", but the repo is public on GitHub (anyone can clone it) and the hub's `pets.json` says public. Make it
   private, or record it as public?
3. **Merge order.** `main` and the two Codex branches (`codex/yellow-phone-20261015`, `codex/l3-activity-20261016`)
   have diverged; this branch starts from `main`.

## Next steps after 1

1. `Start API Fairy.cmd --rebuild`, then `bin\API Fairy.exe --snapshot docs` with made-up keys, look at the four
   PNGs, and put `docs/fairy.png` and `docs/dashboard-keys.png` back in the README.
2. Run the app for real: check the welcome bubble, that a test variable added in Windows' Environment Variables
   window pops her up within a few seconds, and that the window, Settings and How-to tabs work. Do not test with
   real key values; a made-up `sk-proj-…` value is enough.
3. Update the Limits section and the hub's INTELLIGENCE-LEVELS.md row with what was verified.
