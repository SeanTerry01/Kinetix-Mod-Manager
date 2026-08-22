<#
.SYNOPSIS
    Publishes your curated Suggested Mods list as the default list that ships with the manager.

.DESCRIPTION
    The Suggested Mods viewer reads two layers and shows them merged:

        data\suggested-mods.json   the list that SHIPS, replaced wholesale by each release
        %APPDATA%\...\suggested-mods.json   the user's own marks, which win over the shipped ones

    Curating happens in the personal layer (Ctrl+Shift+F7 to mark a mod, F7 to view). This script copies that
    layer into the repository's shipped file, so the next release carries it to everybody.

    Nothing else needs changing to make it ship: the project already copies data\** to the build output, and
    the installer already installs data\*. Committing the regenerated file is the whole job.

    Run it whenever you have marked a mod you want everyone to get:

        powershell -ExecutionPolicy Bypass -File tools\update-suggested-defaults.ps1

    then commit data\suggested-mods.json with the rest of the release.

.PARAMETER Games
    Only publish entries for these games (e.g. -Games MoonlightPeaks,SkyrimSE). Default: every game.

.PARAMETER Name
    The list's name, recorded in the file. Default: "Kinetix Mod Manager suggested mods".

.PARAMETER Author
    The list's author, recorded in the file. Default: "Kinetix Mod Manager".

.PARAMETER DryRun
    Report what would be published and write nothing.
#>
[CmdletBinding()]
param(
    [string[]] $Games,
    [string]   $Name   = "Kinetix Mod Manager suggested mods",
    [string]   $Author = "Kinetix Mod Manager",
    [switch]   $DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot  = Split-Path -Parent $PSScriptRoot
$appData   = Join-Path $env:APPDATA "AudiVentureGames\KinetixModManager"
$srcMods   = Join-Path $appData "suggested-mods.json"
$srcCats   = Join-Path $appData "suggestion-categories.json"
$outFile   = Join-Path $repoRoot "KinetixModManager\data\suggested-mods.json"

if (-not (Test-Path $srcMods)) {
    throw "No curated list found at $srcMods. Mark a mod with Ctrl+Shift+F7 in the manager first."
}

# -- Read the personal layer -------------------------------------------------------------------------------
# Assigned first, then wrapped. ConvertFrom-Json writes a JSON array to the pipeline as ONE object rather than
# enumerating it, so @(Get-Content ... | ConvertFrom-Json) collects a single item holding the whole array and
# every count after it reads 1. Assigning to a variable keeps the array, and @() on the variable unrolls it.
$entriesRaw = Get-Content $srcMods -Raw | ConvertFrom-Json
$entries = @($entriesRaw)
if ($entries.Count -eq 0) { throw "The curated list at $srcMods is empty." }

$allCategories = @()
if (Test-Path $srcCats) {
    $categoriesRaw = Get-Content $srcCats -Raw | ConvertFrom-Json
    $allCategories = @($categoriesRaw)
}

if ($Games) {
    $entries = @($entries | Where-Object { $Games -contains $_.Game })
    if ($entries.Count -eq 0) { throw "No entries for: $($Games -join ', ')" }
}

# -- Check what is about to ship --------------------------------------------------------------------------
# These are warnings, not errors. A list is one person's judgement and the manager copes with a thin entry;
# but an entry with no reason is the one thing the whole feature exists to provide, so it is worth seeing.
$problems = @()
foreach ($e in $entries) {
    $who = "$($e.Game)/$($e.Name)"
    if ([string]::IsNullOrWhiteSpace($e.Reason))          { $problems += "$who has no reason written" }
    if ([string]::IsNullOrWhiteSpace($e.Name))            { $problems += "$who has no name" }
    if ([string]::IsNullOrWhiteSpace($e.NexusId) -and
        [string]::IsNullOrWhiteSpace($e.GitHubRepo))      { $problems += "$who has neither a Nexus id nor a GitHub repo, so nobody can install it from the list" }
}

# Only the categories the entries actually use travel with them, so private headings and any renaming of the
# built-ins are not pushed onto everyone. This mirrors SuggestionSharing.CategoriesUsedBy.
$usedIds   = @($entries | ForEach-Object { $_.Category } | Sort-Object -Unique)
$categories = @($allCategories | Where-Object { $usedIds -contains $_.Id })

foreach ($id in $usedIds) {
    if (-not ($allCategories | Where-Object { $_.Id -eq $id })) {
        $problems += "category '$id' is used but not defined in suggestion-categories.json"
    }
}

# -- Report -------------------------------------------------------------------------------------------------
Write-Host ""
Write-Host "Publishing $($entries.Count) suggestion(s) from your curated list:" -ForegroundColor Cyan
$entries | Group-Object Game | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-16} {1}" -f $_.Name, $_.Count)
}
Write-Host ("  categories:      {0}" -f ($usedIds -join ", "))

if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Host "Worth a look before you commit:" -ForegroundColor Yellow
    $problems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
}

if ($DryRun) {
    Write-Host ""
    Write-Host "Dry run - nothing written." -ForegroundColor DarkGray
    return
}

# -- Write the shipped file ---------------------------------------------------------------------------------
# The wrapped shape (name, author, categories, entries), which is what SuggestionSharing.Parse reads and what
# the manager's own Export writes. A bare array would also load, but then the shipped list could not carry the
# categories its entries are filed under.
$payload = [ordered]@{
    Name       = $Name
    Author     = $Author
    CreatedUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")
    AppVersion = ([xml](Get-Content (Join-Path $repoRoot "KinetixModManager\KinetixModManager.csproj") -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    Categories = $categories
    Entries    = $entries
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outFile) | Out-Null
# UTF-8 without a BOM: the manager reads these with File.ReadAllText, and a BOM on a JSON file is the kind of
# thing that parses fine everywhere until it doesn't.
[IO.File]::WriteAllText($outFile, ($payload | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))

Write-Host ""
Write-Host "Wrote $outFile" -ForegroundColor Green
Write-Host "Commit it with the release. Nothing else is needed - the build copies data\** and the installer ships data\*." -ForegroundColor DarkGray
