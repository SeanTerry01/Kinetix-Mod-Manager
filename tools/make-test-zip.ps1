<#
.SYNOPSIS
    Packs the published build into a portable zip for a tester, instead of an installer.

.DESCRIPTION
    A tester should not have to install a test build over the release they actually use. This produces a folder
    they unzip and run, leaving their installed copy's program files untouched.

    The layout matches what setup.iss installs, so the zip behaves like an installed copy:
      * the native speech DLLs (Tolk, NVDA controller) sit in lib\ where Program.cs's SetDllDirectory looks, and
        also at the root, exactly as the installer places them;
      * lang, docs, data and sounds come across whole;
      * the empty profiles, downloads and backups folders the installer creates are created here too.

    What it CANNOT isolate, and the README inside the zip says so plainly:
      * settings, staged mods, downloads and backups live in %AppData%\AudiVentureGames\KinetixModManager and are
        shared with the installed copy;
      * the manager re-registers the nxm:// handler to its own exe every time it starts, so whichever copy ran
        last owns Nexus's "Mod Manager Download" button.

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

# The installer puts the native DLLs at the app root as well as in lib\; mirror that so the zip cannot behave
# differently from an installed copy. Missing these means no speech at all, which is not a subtle failure here.
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

$readme = @"
Kinetix Mod Manager $short - TEST BUILD (portable)
==================================================

This is a test build. It does not install anything: unzip this folder wherever you like and run
KinetixModManager.exe. Your normally installed copy of the manager is left completely alone, and you can delete
this folder when you are finished.

Two things it does share with your installed copy, which are worth knowing before you start:

1. YOUR SETTINGS AND MODS ARE SHARED.
   Both copies read and write %AppData%\AudiVentureGames\KinetixModManager - the same settings file, the same
   staged mods, downloads and backups. That is deliberate, so you are testing against your real setup, but it
   does mean this build can change things your installed copy will then see.
   Please copy settings.json out of that folder before you start - and PUT IT BACK before you open your
   installed copy again. An older installed copy may not start at all with a settings file this build has
   written, because this build knows about games and settings the older one has never heard of. Close this
   build first, then copy your saved settings.json back over the new one.

2. THE "MOD MANAGER DOWNLOAD" BUTTON FOLLOWS WHICHEVER COPY RAN LAST.
   The manager points Nexus's nxm:// links at itself every time it starts. While you are testing, those downloads
   will come to this build - which is what you want. When you are done, just run your installed copy once and it
   takes them back.

Only one copy can run at a time. If this build appears to do nothing when you start it, the other copy is
probably already running - close it first.

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
