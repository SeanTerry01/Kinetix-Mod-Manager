<#
.SYNOPSIS
    Sets up a test folder, so a test copy of the manager can run beside the real one without touching it.

.DESCRIPTION
    Unpacks the tester zip, copies the games asked for, and writes the test-mode.json that keeps the copy inside this folder.
    It only reads from your real folders. Nothing outside -Root is written to.

    Layout:
      <Root>\Manager\    the test copy of the manager. Start KinetixModManager.exe here.
      <Root>\Data\       its settings, mods, downloads and backups, and its own Documents and AppData.
      <Root>\Games\      the copies of your games.

    Run it again to refresh the manager after a new zip. The games are only copied if they are not there yet, and Data is left
    alone, unless -FreshData is given.

.PARAMETER Root
    Where the test folder goes.

.PARAMETER Games
    Which games to copy, by id. Minecraft goes into Data, because that is where a Minecraft install lives.

.PARAMETER FreshData
    Throw away the test copy's settings and mods first, and start again from your real Minecraft.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\set-up-test-folder.ps1 -Root D:\KinetixTest
#>
[CmdletBinding()]
param(
    [string]$Root = "D:\KinetixTest",
    [string[]]$Games = @("Minecraft", "StardewValley", "SkyrimSE"),
    [switch]$FreshData
)

$ErrorActionPreference = "Stop"

$repoRoot  = Split-Path -Parent $PSScriptRoot
$zip       = Get-ChildItem (Join-Path $repoRoot "KinetixModManager\Setup\*-portable.zip") |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $zip) { throw "No tester zip in KinetixModManager\Setup. Run tools\make-test-zip.ps1 first." }

$realData  = Join-Path $env:APPDATA "AudiVentureGames\KinetixModManager"
$manager   = Join-Path $Root "Manager"
$data      = Join-Path $Root "Data"
$gamesDir  = Join-Path $Root "Games"
$testRoam  = Join-Path $data "AppData\Roaming"
$testLocal = Join-Path $data "AppData\Local"
$testDocs  = Join-Path $data "Documents"
$testApp   = Join-Path $testRoam "AudiVentureGames\KinetixModManager"

$fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
foreach ($real in @($env:APPDATA, $env:LOCALAPPDATA, $env:USERPROFILE, (Join-Path $env:USERPROFILE "Documents"))) {
    if ($fullRoot -eq $real.TrimEnd('\') -or $fullRoot.StartsWith($real.TrimEnd('\') + '\')) {
        throw "The test folder can't be inside your own profile, at $real."
    }
}

if ($FreshData -and (Test-Path $data)) {
    Write-Host "Clearing the test data..."
    Remove-Item $data -Recurse -Force
}

foreach ($folder in @($manager, $testRoam, $testLocal, $testDocs, $gamesDir, $testApp)) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}

# Robocopy says 8 and above for a failure, and below that for how it went.
function Copy-Tree([string]$From, [string]$To, [string[]]$SkipFolders = @()) {
    $arguments = @($From, $To, "/E", "/R:1", "/W:1", "/NFL", "/NDL", "/NJH", "/NP")
    if ($SkipFolders.Count -gt 0) { $arguments += "/XD"; $arguments += $SkipFolders }
    & robocopy @arguments | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying $From failed, robocopy code $LASTEXITCODE." }
}

Write-Host "Unpacking $($zip.Name)..."
$unpacked = Join-Path $env:TEMP ("kmm-test-" + [guid]::NewGuid().ToString("N"))
Expand-Archive -Path $zip.FullName -DestinationPath $unpacked
$inner = Get-ChildItem $unpacked -Directory | Select-Object -First 1
Copy-Tree $inner.FullName $manager
Remove-Item $unpacked -Recurse -Force

# Where the real settings say each game is. Read, never written.
$real = $null
$settingsPath = Join-Path $realData "settings.json"
if (Test-Path $settingsPath) { $real = Get-Content $settingsPath -Raw | ConvertFrom-Json }

$gameFolders = @{}
foreach ($game in $Games) {
    if ($game -eq "Minecraft") {
        $to = Join-Path $testRoam ".minecraft"
        if (Test-Path $to) { Write-Host "Minecraft is already there."; continue }
        $from = Join-Path $env:APPDATA ".minecraft"
        if (-not (Test-Path $from)) { Write-Host "No .minecraft to copy."; continue }
        Write-Host "Copying Minecraft..."
        Copy-Tree $from $to
        continue
    }

    $from = if ($real -and $real.GamePaths.PSObject.Properties[$game]) { $real.GamePaths.$game } else { "" }
    if (-not $from -or -not (Test-Path $from)) { Write-Host "$game isn't set up, so it was not copied."; continue }

    $to = Join-Path $gamesDir (Split-Path $from -Leaf)
    if (Test-Path $to) { Write-Host "$game is already there." }
    else {
        Write-Host "Copying $game. This can take a while..."
        Copy-Tree $from $to
    }
    $gameFolders[$game] = $to

    # The settings and plugin list the manager edits live outside the game. Copied so those screens have something to show.
    # Not the saves: they can be large, and nothing here needs them.
    $docName = switch ($game) { "SkyrimSE" { "Skyrim Special Edition" } "Fallout4" { "Fallout4" } default { "" } }
    if ($docName) {
        $myGames = Join-Path $env:USERPROFILE "Documents\My Games\$docName"
        if (Test-Path $myGames) { Copy-Tree $myGames (Join-Path $testDocs "My Games\$docName") @("Saves") }
        $localFolder = Join-Path $env:LOCALAPPDATA $docName
        if (Test-Path $localFolder) { Copy-Tree $localFolder (Join-Path $testLocal $docName) }
    }
}

# Written fresh, keeping any game given earlier and the free-account switch.
$modeFile = Join-Path $manager "test-mode.json"
$mode = @{ Root = $fullRoot; Games = $gameFolders; PretendFreeNexus = $false }
[IO.File]::WriteAllText($modeFile, ($mode | ConvertTo-Json -Depth 3), (New-Object Text.UTF8Encoding($false)))

# Only what the test copy needs of the real settings. Never the folders, which would point it at the real games.
$testSettings = Join-Path $testApp "settings.json"
if ($real -and -not (Test-Path $testSettings)) {
    $seed = [ordered]@{}
    foreach ($name in @("ApiKeyEncrypted", "MinecraftGameVersion", "MinecraftAccessModId", "Shortcuts", "Language", "ProgressFeedback", "SoundVolume", "EnableUiSounds")) {
        if ($real.PSObject.Properties[$name]) { $seed[$name] = $real.$name }
    }
    [IO.File]::WriteAllText($testSettings, ($seed | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
}

Write-Host ""
Write-Host "Done. Start the test copy from:"
Write-Host (Join-Path $manager "KinetixModManager.exe")
