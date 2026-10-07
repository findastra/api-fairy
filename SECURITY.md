# Security

API Fairy handles the most sensitive things on a developer's PC, so its rule is simple: **it keeps notes about keys, never the keys.**

## What it reads

| Source | Why | What it keeps |
|---|---|---|
| `HKCU\Environment` (your environment variables) | Most API keys live here | Name, provider, fingerprint |
| `HKLM\...\Session Manager\Environment` (system variables) | Some keys are set for the whole PC | Name, provider, fingerprint |
| `.env` files in the folders you choose | Project keys | Name, file path, provider, fingerprint, git status |
| PowerShell and Git Bash history files | Keys typed into commands end up here | Fingerprints of key-like text, and which file |

A value counts as a key when it has a known provider's shape (`sk-ant-…`, `ghp_…`, `AIza…`, and others), is a URL with a password in it, or sits under a secret-sounding name (`*_API_KEY`, `*_TOKEN`, `*_SECRET`, …). Placeholders, paths, plain URLs and numbers are ignored.

## What it stores

`%LOCALAPPDATA%\API Fairy\fairy.dat`, encrypted with Windows DPAPI (current user scope) plus app-specific entropy. The folder's permissions are reset to your account and SYSTEM only, with inheritance removed. Writes go to a temporary file first and then replace the old one, so a crash can't leave half a file.

Stored per key: name, where it is, detected provider, fingerprint, git status, dates, whether you've seen it, and your notes (nickname, used by, spending limit, created date, free-text notes). There is no field for the key.

The fingerprint is the first 64 bits of HMAC-SHA256(pepper, key), where the pepper is 32 random bytes made on first run and stored inside the encrypted file. Without the pepper a fingerprint reveals nothing; even with it, reversing one means guessing the key.

## What it never does

- Store, display, log, copy to the clipboard, or transmit a key value.
- Make network connections. There is no networking code; `tests/Tests.cs` fails if any appears in `src/`.
- Parse anything it reads as code or markup. All window text is set as plain text, and the only XAML is the fixed theme.
- Open a web page you or a file supplied. **MANAGE** opens a fixed `https://` provider page from `Providers.cs`, chosen by provider id.
- Follow junctions or symlinks while looking for `.env` files, read files over 512 KB, or go deeper than 6 folders.
- Run as administrator, or change anything outside its own folder, except what you ask for:
  - **Start with Windows** writes one value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
  - **Clean history** rewrites your history files without the lines that contain keys, after asking.

## Other hardening

- Runs git with `core.fsmonitor` turned off and a 5-second timeout, so checking a repo can't start other programs.
- Removes the current folder from the DLL search path at startup.
- Every regular expression has a timeout.
- The notes form refuses text that looks like a key or matches a known key's fingerprint.
- Error logs blank out anything that looks like a key before writing.
- The desktop window can't take keyboard focus and stays out of Alt+Tab.

## Limits, honestly

- Any program running as you can read your environment variables and `.env` files directly. API Fairy can't protect a key from malware already on the PC; it helps you notice risky habits and keep track.
- .NET strings can't be wiped from memory on demand. Values are dropped right after use, but may linger in memory until garbage collection.
- The provider list is a best guess from key shapes and names. You can correct it per key.
- The app isn't code-signed, so some antivirus tools may flag a fresh build. Build it yourself from this source.

## Reporting a problem

Open a private security advisory on the GitHub repo rather than a public issue.
