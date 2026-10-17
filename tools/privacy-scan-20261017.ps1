# Privacy scan: refuses a push that would publish a key, a personal folder path or a personal email.
# Runs before every push on Astra's PC (git pre-push hook) and on GitHub for every push (.github/workflows).
# Claude Opus 5.5 (claude-opus-5-5), 2026-10-17.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\privacy-scan-20261017.ps1
#   ... -InstallHook   installs it as this clone's pre-push hook
param([switch]$InstallHook)
$ErrorActionPreference = 'Stop'
$root = (git rev-parse --show-toplevel).Trim()
Set-Location $root

if ($InstallHook) {
  $hook = Join-Path $root '.git\hooks\pre-push'
  "#!/bin/sh`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/privacy-scan-20261017.ps1 || exit 1`n" | Set-Content -Path $hook -Encoding ascii -NoNewline
  Write-Output "Installed: every push from this folder runs the privacy scan first."
  exit 0
}

# Made-up example keys in tests/Tests.cs are fixtures, not secrets.
$skip = @('tests/Tests.cs')
$rules = @(
  @{ Name = 'API key';        Rx = '(?<![A-Za-z0-9+/=_\-])(sk-(proj|ant|svcacct|admin|or-v1)-[A-Za-z0-9_\-]{20,}|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AIza[0-9A-Za-z_\-]{35}|hf_[A-Za-z0-9]{30,}|r8_[A-Za-z0-9]{20,}|xox[abposr]-[A-Za-z0-9\-]{10,}|(sk|rk)_live_[A-Za-z0-9]{10,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----)' },
  @{ Name = 'personal path';  Rx = '(?i)[A-Z]:[\\/]+Users[\\/]+(?!you[\\/]|public[\\/]|default[\\/]|<)[A-Za-z0-9._\-]+' },
  @{ Name = 'personal email'; Rx = '(?i)[A-Z0-9._%+\-]+@(?!users\.noreply\.github\.com|anthropic\.com|example\.com)[A-Z0-9.\-]+\.(com|net|org|me|io)\b' }
)
$found = 0
foreach ($file in (git ls-files)) {
  if ($skip -contains $file) { continue }
  $path = Join-Path $root $file
  if (-not (Test-Path $path -PathType Leaf) -or (Get-Item $path).Length -gt 2MB) { continue }
  $bytes = [IO.File]::ReadAllBytes($path)
  if ($bytes -contains 0) { continue }
  $lines = [Text.Encoding]::UTF8.GetString($bytes) -split "`n"
  for ($i = 0; $i -lt $lines.Count; $i++) {
    foreach ($r in $rules) {
      if ($lines[$i] -match $r.Rx) { Write-Output ("{0}:{1}: {2}" -f $file, ($i + 1), $r.Name); $found++ }
    }
  }
}
foreach ($mail in (git log --format='%ae%n%ce' | Sort-Object -Unique)) {
  if ($mail -and $mail -notmatch '(?i)@users\.noreply\.github\.com$|^noreply@anthropic\.com$') { Write-Output "commit history: personal email"; $found++ }
}
if ($found -gt 0) { Write-Output "Privacy scan: $found problem(s). Nothing was pushed. Remove them and try again."; exit 1 }
Write-Output 'Privacy scan: clean.'
