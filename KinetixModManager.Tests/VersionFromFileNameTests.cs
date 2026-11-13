using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// The release a Nexus download holds, read from its file name. It decides what an install records as the version you
/// have, so a wrong answer offers a mod its own version as an update.
/// </summary>
public sealed class VersionFromFileNameTests
{
	[Theory]
	// With Nexus's upload time on the end, which is not part of the version.
	[InlineData("SkyUI-12604-6-11-1778020881.zip", "12604", "6.11")]
	[InlineData("Skyrim Access-181131-1-9-3-1759610460.zip", "181131", "1.9.3")]
	// With no upload time: every part is the version. These are the two the Skyrim suite fetched on 2026-11-12.
	[InlineData("Better MessageBox Controls v1_2-1428-1-2.zip", "1428", "1.2")]
	[InlineData("UIExtensions v1-2-0-17561-1-2-0.7z", "17561", "1.2.0")]
	[InlineData("Some Mod-500-3.zip", "500", "3")]
	// A long part that is still a version, not a time.
	[InlineData("Some Mod-500-2026-1-15.zip", "500", "2026.1.15")]
	public void TheVersionIsEverythingAfterTheModIdExceptAnUploadTime(string file, string modId, string expected)
	{
		Assert.Equal(expected, ModScanner.ExtractVersionFromFileName(file, modId));
	}

	[Theory]
	[InlineData("Some Mod-500-1634567890.zip", "500")]   // only the upload time
	[InlineData("Some Mod-500-.zip", "500")]             // nothing after the id
	[InlineData("Some Mod 500 1.2 2026-08-27T15-29Z Ae46W7Fw2.zip", "500")]   // the manager's own naming, no dashed id
	[InlineData("Some Mod-500-1-2.zip", "")]
	public void NoVersionInTheNameIsNoVersion(string file, string modId)
	{
		Assert.Null(ModScanner.ExtractVersionFromFileName(file, modId));
	}
}
