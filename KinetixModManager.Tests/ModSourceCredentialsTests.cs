using System;
using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModSourceCredentials"/> — the screen that says which mod sites want a key and whether
/// the user has given them one.
///
/// <para>
/// The rule worth holding here is that a row never carries the key. The list is arrowed through, so every
/// row is spoken in passing, and a credential read aloud on the way past is read aloud in whatever room the
/// user is sitting in. What a row says is whether one is stored; the key itself is shown only for the one row
/// the user asks about.
/// </para>
/// </summary>
public class ModSourceCredentialsTests
{
	// The shipped catalogue, so what is asserted below is the sentence the user hears rather than the key
	// that stands in for one. Idempotent, and every test here wants the same language.
	static ModSourceCredentialsTests() => Loc.Init("en");

	private static IReadOnlyList<ModSourceKeyRow> Rows(params (string Source, string Key)[] stored)
	{
		var keys = stored.ToDictionary(s => s.Source, s => s.Key, StringComparer.OrdinalIgnoreCase);
		return ModSourceCredentials.Rows(id => keys.TryGetValue(id, out string? k) ? k : null);
	}

	// ---------------------------------------------------------------------
	// Which sites are listed
	// ---------------------------------------------------------------------

	[Fact]
	public void OnlyTheSitesThatWantOneAreListed()
	{
		var listed = Rows().Select(r => r.Source.Id).ToList();

		Assert.Contains(ModSources.Nexus, listed);
		Assert.Contains(ModSources.CurseForge, listed);

		// Modrinth needs no account at all, GitHub needs none for public releases, and ModDrop is only ever
		// a page to open. Listing them would be three rows saying "nothing to do here".
		Assert.DoesNotContain(ModSources.Modrinth, listed);
		Assert.DoesNotContain(ModSources.GitHub, listed);
		Assert.DoesNotContain(ModSources.ModDrop, listed);
	}

	[Fact]
	public void TheGameThatHappensToBeLoadedDoesNotComeIntoIt()
	{
		// A key belongs to the user's account with a site. A screen that hid Nexus because they were playing
		// Minecraft would be a screen they could not find their way back to.
		Assert.Equal(
			ModSources.NeedingCredentials().Select(s => s.Id),
			Rows().Select(r => r.Source.Id));
	}

	[Fact]
	public void EverySiteListedSaysWhereToGetAKey()
	{
		// "You need an API key" is not useful without saying where from.
		Assert.All(Rows(), r => Assert.StartsWith("https://", r.Source.ApiKeyUrl));
	}

	// ---------------------------------------------------------------------
	// What a row says
	// ---------------------------------------------------------------------

	[Fact]
	public void ARowKnowsWhetherAKeyIsStored()
	{
		IReadOnlyList<ModSourceKeyRow> rows = Rows((ModSources.Nexus, "abc123"));

		Assert.True(rows.Single(r => r.Source.Id == ModSources.Nexus).HasKey);
		Assert.False(rows.Single(r => r.Source.Id == ModSources.CurseForge).HasKey);
	}

	[Fact]
	public void AKeyOfNothingButSpacesIsNoKey()
	{
		Assert.False(Rows((ModSources.Nexus, "   ")).Single(r => r.Source.Id == ModSources.Nexus).HasKey);
	}

	[Fact]
	public void ARowNeverContainsTheKey()
	{
		// The one that matters. Arrowing down this list must not read anybody's credentials out loud.
		const string secret = "nexus-abcdef0123456789";
		ModSourceKeyRow row = Rows((ModSources.Nexus, secret)).Single(r => r.Source.Id == ModSources.Nexus);

		Assert.DoesNotContain(secret, row.Describe());
		Assert.DoesNotContain(secret, row.ToString());
		Assert.Contains("Nexus Mods", row.Describe());
	}

	[Fact]
	public void ARowSaysWhatEnterWillDoToIt()
	{
		IReadOnlyList<ModSourceKeyRow> rows = Rows((ModSources.Nexus, "abc123"));

		Assert.Contains("Press Enter", rows.Single(r => r.Source.Id == ModSources.Nexus).Describe());
		Assert.Contains("Press Enter", rows.Single(r => r.Source.Id == ModSources.CurseForge).Describe());
	}

	// ---------------------------------------------------------------------
	// The line at the top
	// ---------------------------------------------------------------------

	[Fact]
	public void TheSummaryCountsWhatIsStillMissing()
	{
		Assert.Equal(2, ModSourceCredentials.Missing(Rows()));
		Assert.Equal(1, ModSourceCredentials.Missing(Rows((ModSources.Nexus, "abc123"))));
		Assert.Equal(0, ModSourceCredentials.Missing(
			Rows((ModSources.Nexus, "abc123"), (ModSources.CurseForge, "def456"))));
	}

	[Fact]
	public void TheSummarySaysSomethingDifferentWhenEverythingIsSetUp()
	{
		string someMissing = ModSourceCredentials.Summarise(Rows());
		string allSet = ModSourceCredentials.Summarise(
			Rows((ModSources.Nexus, "abc123"), (ModSources.CurseForge, "def456")));

		Assert.NotEqual(someMissing, allSet);
		Assert.All(new[] { someMissing, allSet }, line => Assert.False(string.IsNullOrWhiteSpace(line)));
	}

	// ---------------------------------------------------------------------
	// What is accepted as a key
	// ---------------------------------------------------------------------

	[Fact]
	public void AKeyIsAcceptedWithoutBeingSecondGuessed()
	{
		// Deliberately shallow. A key's real test is whether the site takes it, and a pattern guessed at here
		// would one day reject a perfectly good key the site had started issuing in a new shape, with the user
		// having no way to argue.
		Assert.Null(ModSourceCredentials.WhyNotUsable("abc123"));
		Assert.Null(ModSourceCredentials.WhyNotUsable("$2a$10$verylongbcryptlookingthing.with.dots"));
		Assert.Null(ModSourceCredentials.WhyNotUsable("  padded-but-fine  "));
	}

	[Fact]
	public void NothingTypedIsRefusedWithAReason()
	{
		Assert.NotNull(ModSourceCredentials.WhyNotUsable(""));
		Assert.NotNull(ModSourceCredentials.WhyNotUsable("   "));
		Assert.NotNull(ModSourceCredentials.WhyNotUsable(null));
	}

	[Fact]
	public void AKeyWithASpaceInTheMiddleIsRefused()
	{
		// Almost always half a paste, or a sentence pasted in place of the key.
		Assert.NotNull(ModSourceCredentials.WhyNotUsable("abc 123"));
		Assert.NotNull(ModSourceCredentials.WhyNotUsable("my key is abc123"));
	}

	// ---------------------------------------------------------------------
	// How a key is come by
	// ---------------------------------------------------------------------

	[Fact]
	public void NexusCanAlsoSignTheUserInRatherThanBeingTyped()
	{
		// The better way round, because the manager never sees a password and the user can revoke the key
		// without changing one. It is what ApiKeyOrSignIn exists to say.
		Assert.Equal(ModSourceCredential.ApiKeyOrSignIn, ModSources.Find(ModSources.Nexus)!.Credential);
	}

	[Fact]
	public void CurseForgeIsAKeyAndNothingElse()
	{
		Assert.Equal(ModSourceCredential.ApiKey, ModSources.Find(ModSources.CurseForge)!.Credential);
	}

	[Fact]
	public void ASiteThatNeedsNothingSaysSo()
	{
		Assert.Equal(ModSourceCredential.None, ModSources.Find(ModSources.Modrinth)!.Credential);
		Assert.False(ModSources.Find(ModSources.Modrinth)!.NeedsCredential);
	}
}
