using System.IO.Compression;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// WitcherAccess's USER_MANUAL.html read for F3. Built the way the real 1.0 page is: English in main#doc, each language in its
/// own template, paragraphs wrapped across source lines, a contents list in a nav, links on and off the page.
/// </summary>
public sealed class WitcherAccessGuideTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-wa-" + Guid.NewGuid().ToString("N"));

	public WitcherAccessGuideTests() => Directory.CreateDirectory(_dir);
	public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

	private const string Page = """
		<!DOCTYPE html><html lang="en"><head><title>WitcherAccess: Player's Guide</title></head><body>
		<div class="langbar"><select id="lang-select"><option value="en">English</option></select></div>
		<main id="doc" lang="en"><h1>Shown page</h1><p>Shown.</p></main>
		<template id="tpl-en">
		<h1>WitcherAccess: Player's Guide</h1>
		<p>WitcherAccess makes <strong>The Witcher 3</strong> playable
		without sight &amp; more.</p>
		<nav><h2 id="sommaire">Contents</h2><ol><li><a href="#acheter">Getting the game</a></li></ol></nav>
		<h2 id="acheter">Getting the game</h2>
		<ul>
		  <li>On Steam: <a href="https://store.steampowered.com/app/499450/">Complete Edition</a>.</li>
		  <li>See <a href="#manette">Controller shortcuts</a>.</li>
		</ul>
		<h3>Why a plain installer</h3>
		<div class="avert"><p>Close the game first.</p></div>
		</template>
		<template id="tpl-fr"><h1>WitcherAccess : Guide du joueur</h1><h2>Obtenir le jeu</h2><p>Bonjour.</p></template>
		</body></html>
		""";

	[Fact]
	public void TheEnglishGuideBecomesChaptersAndSections()
	{
		string md = WitcherAccessGuide.ToMarkdown(Page);

		Assert.StartsWith("# WitcherAccess: Player's Guide", md);
		Assert.Contains("\n## Getting the game\n", md);
		Assert.Contains("\n### Why a plain installer\n", md);
		// A wrapped paragraph is one line again, its markup gone and its entities read.
		Assert.Contains("WitcherAccess makes The Witcher 3 playable without sight & more.", md);
		Assert.Contains("- On Steam: [Complete Edition](https://store.steampowered.com/app/499450/).", md);
		Assert.Contains("- See Controller shortcuts.", md);   // a link within the page is only its words
		Assert.Contains("Close the game first.", md);
		Assert.DoesNotContain("Contents", md);                 // its own contents list is left out
		Assert.DoesNotContain("Shown page", md);               // the template, not what the page happened to show
		Assert.DoesNotContain("<", md);
	}

	[Fact]
	public void AnotherLanguageIsItsOwnTemplateAndAMissingOneIsEnglish()
	{
		Assert.Contains("## Obtenir le jeu", WitcherAccessGuide.ToMarkdown(Page, "fr"));
		Assert.Contains("## Getting the game", WitcherAccessGuide.ToMarkdown(Page, "kr"));
	}

	[Fact]
	public void APageWithNoGuideGivesNothing()
	{
		Assert.Equal("", WitcherAccessGuide.ToMarkdown("<html><body><p>Nothing here.</p></body></html>"));
	}

	[Fact]
	public void TheGuideIsFoundInTheGameFolderWhereTheInstallerPutsIt()
	{
		// Sean's 1.0 install, 2026-11-12: the game folder itself.
		File.WriteAllText(Path.Combine(_dir, WitcherAccessGuide.FileName), Page);
		Assert.Equal(Path.Combine(_dir, WitcherAccessGuide.FileName), WitcherAccessGuide.FindInstalled(_dir));
		Assert.Null(WitcherAccessGuide.FindInstalled(Path.Combine(_dir, "missing")));
	}

	[Fact]
	public void TheGuideIsReadFromTheNewestDownloadWithoutUnpackingIt()
	{
		string older = Path.Combine(_dir, "WitcherAccess 0.9 15538 0.9 old.zip");
		string newer = Path.Combine(_dir, "WitcherAccess 1.0 15538 1.0 new.zip");
		foreach ((string zip, string text) in new[] { (older, "old"), (newer, Page) })
		{
			using ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create);
			using var writer = new StreamWriter(archive.CreateEntry(WitcherAccessGuide.FileName).Open());
			writer.Write(text);
		}
		File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-2));

		Assert.Equal(Page, WitcherAccessGuide.ReadFromDownloads(_dir));
		Assert.Equal(2, Directory.GetFiles(_dir).Length);   // nothing unpacked beside them
	}
}
