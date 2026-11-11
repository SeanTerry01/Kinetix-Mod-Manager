using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>Pins <see cref="ModScanner.ParseNexusId"/>: which update keys name a Nexus page the update check can ask about.</summary>
public class ParseNexusIdTests
{
	[Theory]
	[InlineData("[\"Nexus:2400\"]", "2400")]
	[InlineData("[\"Nexus: 2400\"]", "2400")]
	[InlineData("[\"Nexus:2400@main\"]", "2400")]
	[InlineData("\"Nexus:2400\"", "2400")]
	[InlineData("[\"GitHub:a/b\", \"Nexus:2400\"]", "2400")]
	// A placeholder first, then the real key: the real one is used.
	[InlineData("[\"Nexus:-1\", \"Nexus:2400\"]", "2400")]
	public void ARealPageIsRead(string keys, string expected)
	{
		Assert.Equal(expected, ModScanner.ParseNexusId(JToken.Parse(keys)));
	}

	[Theory]
	// Authors write these for a part with no page of its own. Asked about, Nexus gave no answer, and every update check said a
	// mod site had failed to answer.
	[InlineData("[\"Nexus:-1\"]")]
	[InlineData("[\"Nexus:0\"]")]
	[InlineData("[\"Nexus:\"]")]
	[InlineData("[\"Nexus:abc\"]")]
	[InlineData("[\"GitHub:a/b\"]")]
	[InlineData("[]")]
	public void APlaceholderIsNoPage(string keys)
	{
		Assert.Null(ModScanner.ParseNexusId(JToken.Parse(keys)));
	}
}
