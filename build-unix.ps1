param(
  [ValidatePattern('^(linux|osx)-(x64|arm64)$')]
  [string]$Rid = "linux-x64",
  [string]$Config = "Release"
)

# Linux and macOS builds: the Unix half of publish.ps1 -> dist/<rid>.
# Runs on GitHub's Linux and macOS runners (pwsh is preinstalled) and on a dev box for a local try.
# ReadyToRun is left off: it cannot be cross-compiled for every pair, and the Windows build is the measured one.

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$pubDir = Join-Path (Join-Path $PSScriptRoot "dist") $Rid
if (Test-Path -LiteralPath $pubDir) { Remove-Item -LiteralPath $pubDir -Recurse -Force }

Write-Host "Publishing Drops for $Rid..." -ForegroundColor Cyan
dotnet publish Drops.csproj -c $Config -r $Rid --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:PublishTrimmed=false -p:DebugType=embedded `
    -o $pubDir --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Rid" }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "LICENSE.txt") -Destination $pubDir -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "README.md") -Destination $pubDir -Force

if (-not (Test-Path (Join-Path $pubDir "Drops"))) { throw "Drops missing from $pubDir" }
Write-Host "`nDone. Output: $pubDir" -ForegroundColor Green
