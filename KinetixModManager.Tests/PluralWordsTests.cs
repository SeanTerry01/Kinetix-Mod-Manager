using Xunit;

namespace KinetixModManager.Tests;

public class PluralWordsTests
{
	[Theory]
	[InlineData(1, "1 mod is ready")]
	[InlineData(0, "0 mods are ready")]
	[InlineData(3, "3 mods are ready")]
	public void TheWordFollowsTheCount(int count, string expected)
	{
		string text = Loc.ChoosePluralWords("{0} {0|mod is|mods are} ready", new object[] { count });
		Assert.Equal(expected, string.Format(text, count));
	}

	[Fact]
	public void ItFollowsTheCountItNames()
	{
		string text = Loc.ChoosePluralWords("{1} {1|file|files} from {0}", new object[] { "Skyrim", 1 });
		Assert.Equal("1 file from Skyrim", string.Format(text, "Skyrim", 1));
	}

	[Fact]
	public void AGameNameInThatPlaceKeepsTheOrdinaryForm()
	{
		// "{0} mods" is also how "Skyrim mods" is written, so anything that isn't a count stays as it was.
		string text = Loc.ChoosePluralWords("{0} {0|mod|mods}", new object[] { "Skyrim" });
		Assert.Equal("Skyrim mods", string.Format(text, "Skyrim"));
	}

	[Fact]
	public void AMissingArgumentCannotThrow()
	{
		Assert.Equal("{1} many", Loc.ChoosePluralWords("{1} {1|one|many}", new object[] { 1 }));
	}

	[Fact]
	public void TextWithoutAnyIsUntouched()
	{
		Assert.Equal("a | b {0}", Loc.ChoosePluralWords("a | b {0}", new object[] { 1 }));
	}
}
