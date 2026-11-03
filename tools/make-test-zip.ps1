<#
.SYNOPSIS
    Packs the published build into a portable zip for a tester, instead of an installer.

.DESCRIPTION
    A tester should not have to install a test build over the release they actually use. This produces a folder
    they unzip and run, leaving their installed copy's program files untouched.

    The layout follows what setup.iss installs, with two differences:
      * the native speech DLLs (Tolk, NVDA controller) sit at the root, as the installer places them, and also in
        lib\, which the publish folder has and an installed copy does not. Program.cs's SetDllDirectory only looks
        at lib\ when it exists, so this zip finds them there and an installed copy finds them at the root;
      * empty profiles, downloads and backups folders are created here, though the manager never uses them: its
        own are in %AppData%, and the installer does not create these.
    Otherwise lang, docs, data and sounds come across whole.

    The zip is in test mode: the test-mode.json it carries keeps the copy's settings, mods, downloads and backups in a Data
    folder beside it, stops it looking for real games, and leaves Nexus's download button with the real copy. See TestMode.cs.
    tools\set-up-test-folder.ps1 sets up a fuller test folder, with copies of games.

.PARAMETER Configuration
    Build configuration to pack. Defaults to Release.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\make-test-zip.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path -Parent $PSScriptRoot
$appDir     = Join-Path $repoRoot "KinetixModManager"
$publishDir = Join-Path $appDir "bin\$Configuration\net10.0-windows\win-x64\publish"
$outputDir  = Join-Path $appDir "Setup"

if (-not (Test-Path $publishDir)) {
    throw "No published build at $publishDir. Run: dotnet publish KinetixModManager\KinetixModManager.csproj -c $Configuration -r win-x64 --self-contained true"
}

$exePath = Join-Path $publishDir "KinetixModManager.exe"
if (-not (Test-Path $exePath)) { throw "No KinetixModManager.exe in $publishDir." }

$version = (Get-Item $exePath).VersionInfo.FileVersion
$short   = ($version -split '\.')[0..2] -join '.'

# The version in the project file is the one that was meant; the exe's is the one that was built. A Debug build
# does not pick up a version bump, and neither does a publish that was never re-run - so compare them here rather
# than discover it after the zip is in someone else's hands.
$csprojVersion = ([xml](Get-Content (Join-Path $appDir "KinetixModManager.csproj"))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if ($csprojVersion -and $short -ne $csprojVersion) {
    throw "The published exe is $short but the project says $csprojVersion. Re-publish before packing."
}

$stageName = "KinetixModManager-$short-portable"
$stageDir  = Join-Path ([System.IO.Path]::GetTempPath()) $stageName
$zipPath   = Join-Path $outputDir "$stageName.zip"

if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "Packing $short from $Configuration..."

Copy-Item -Path (Join-Path $publishDir "*") -Destination $stageDir -Recurse -Force

# The installer puts the native DLLs at the app root and has no lib\; the publish folder only has them in lib\.
# Copy them to the root as well, so they are where an installed copy has them. Missing these means no speech at
# all, which is not a subtle failure here.
foreach ($native in @("Tolk.dll", "nvdaControllerClient.dll", "nvdaControllerClient64.dll")) {
    $source = Join-Path $publishDir "lib\$native"
    if (-not (Test-Path $source)) { throw "Missing $native in $publishDir\lib - the build would start up silent." }
    Copy-Item $source (Join-Path $stageDir $native) -Force
}

foreach ($folder in @("profiles", "downloads", "backups", "sounds")) {
    New-Item -ItemType Directory -Path (Join-Path $stageDir $folder) -Force | Out-Null
}

# The checklist travels with the build. Sending it separately means a tester can end up holding one without the
# other, and the README used to promise it was in here when nothing put it in here.
$checklist = Join-Path $repoRoot "docs\TESTER_CHECKLIST.md"
if (-not (Test-Path $checklist)) { throw "Missing $checklist - the zip would go out with nothing to test against." }
Copy-Item $checklist (Join-Path $stageDir "TESTER-CHECKLIST.md") -Force

# This file is what puts the copy in test mode. Only the zip has it: setup.iss never ships it.
[IO.File]::WriteAllText((Join-Path $stageDir "test-mode.json"), '{ "Root": "." }', (New-Object Text.UTF8Encoding($false)))

$readme = @"
Kinetix Mod Manager $short - TEST BUILD (portable)
==================================================

This is a test build. It does not install anything: unzip this folder wherever you like and run
KinetixModManager.exe. Your normally installed copy of the manager is left completely alone, and you can delete
this folder when you are finished.

IT IS IN TEST MODE, SO YOUR REAL SETUP IS SAFE.
It keeps its settings, mods, downloads and backups in a Data folder here, and it can run while your installed copy is
open. It says "Test mode" as it starts, and the title bar says so too.

- It does not look for your real games. Give it a copy: put a copy of the game's folder inside this folder, then use
  Locate when it says the game isn't installed. It refuses a folder outside this one, so your real game stays untouched.
- Nexus's "Mod Manager Download" button stays with your installed copy. To send a link here, copy the button's link, then
  choose Test Mode, then Use Nexus Link from Clipboard, on the File menu.
- Test Mode on the File menu can also make it act as if your Nexus account is free. Your account is not changed.
- It does not update itself.
- A game you start from here still uses your real saves, because the game decides that. If you play, do not save. Minecraft
  is the exception: it runs entirely from this folder.

To test against your real setup instead, close it and delete test-mode.json from this folder. It then shares everything with
your installed copy, so make a copy of %AppData%\AudiVentureGames\KinetixModManager\settings.json first, and put it back when
you have finished, because an older installed copy may not start with a settings file this build has written.

Check Help - About: it should say $short. If it says anything else, tell Sean before going further.

WHAT TO TEST
------------
Open TESTER-CHECKLIST.md, in this same folder. It opens in Notepad, and it is ordered by what matters most, so
working down it from the top is the most useful thing you can do. Section 1 is worth reading before you start the
program.

Thank you for doing this.
"@
Set-Content -Path (Join-Path $stageDir "README-TESTING.txt") -Value $readme -Encoding UTF8

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $stageDir -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item $stageDir -Recurse -Force

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "Built $zipPath ($sizeMb MB, version $version)"
