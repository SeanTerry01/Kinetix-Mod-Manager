using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>Covers <see cref="ShortcutNames"/>' fallback, for an action the language file has no name for.</summary>
public class ShortcutNamesTests
{
	static ShortcutNamesTests() => Loc.Init("en");

	[Theory]
	[InlineData("CheckRequirements", "Check Requirements")]
	[InlineData("Manual", "Manual")]
	[InlineData("OpenAILog", "Open AI Log")]
	[InlineData("ManualID", "Manual ID")]
	public void AnUnnamedActionIsSpeltOutWordByWord(string identifier, string expected) =>
		Assert.Equal(expected, ShortcutNames.SplitWords(identifier));

	[Fact]
	public void ANamedActionReadsAsItsName() =>
		Assert.Equal("View Selected Mod's Full Description", ShortcutNames.For("ViewDescription"));

	[Fact]
	public void AnActionWithNoNameIsNeverReadAsTheKey() =>
		Assert.Equal("Some Future Command", ShortcutNames.For("SomeFutureCommand"));
}
