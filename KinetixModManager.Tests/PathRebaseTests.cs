using System;
using System.IO;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="PathRebase"/> — moving a path from under one folder to under another.
///
/// <para>
/// Written after a sabotage pass found <c>file.Replace(sourceFolder, destinationFolder)</c> in **four**
/// places, including the code that moves a user's entire mods folder. It is a string operation on something
/// that is not a string, and both of its failure modes install a mod "successfully" into the wrong place
/// without throwing.
/// </para>
/// </summary>
public class PathRebaseTests
{
	private static string P(params string[] parts) => Path.Combine(parts);

	[Fact]
	public void AFileMovesUnderTheNewRoot()
	{
		Assert.Equal(
			P("/dest", "mod", "file.txt"),
			PathRebase.To(P("/source"), P("/source", "mod", "file.txt"), P("/dest")));
	}

	[Fact]
	public void TheRootItselfBecomesTheNewRoot()
	{
		// What makes copying a tree's own top-level folder work without the caller special-casing it.
		Assert.Equal(Path.GetFullPath(P("/dest")), PathRebase.To(P("/source"), P("/source"), P("/dest")));
	}

	[Fact]
	public void ARepeatedFolderNameIsNotRewrittenInTheMiddle()
	{
		// The first failure the old spelling had. Replace() rewrites EVERY occurrence, so a path carrying the
		// source folder's spelling twice is mangled in the middle as well as at the front — and the file
		// lands somewhere nobody asked for, with nothing thrown to say so.
		string source = P("/games", "Mods");
        string file = P("/games", "Mods", "Pack", "games", "Mods", "texture.png");

		string rebased = PathRebase.To(source, file, P("/dest"));

		Assert.Equal(P("/dest", "Pack", "games", "Mods", "texture.png"), rebased);
		Assert.NotEqual(file.Replace(source, P("/dest")), rebased);
	}

	[Fact]
	public void ATrailingSeparatorOnTheRootChangesNothing()
	{
		// The second failure: Replace() matched nothing at all when the two spellings differed by a trailing
		// separator, so the file was copied to itself.
		string withSlash = P("/source") + Path.DirectorySeparatorChar;

		Assert.Equal(
			PathRebase.To(P("/source"), P("/source", "a.txt"), P("/dest")),
			PathRebase.To(withSlash, P("/source", "a.txt"), P("/dest")));
	}

	[Fact]
	public void AnUntidyPathIsStillPlacedCorrectly()
	{
		Assert.Equal(
			P("/dest", "a.txt"),
			PathRebase.To(P("/source"), P("/source", "sub", "..", "a.txt"), P("/dest")));
	}

	[Fact]
	public void APathOutsideTheRootIsRefusedLoudly()
	{
		// Deliberately loud. Every caller is copying a tree it has just enumerated, so this can only mean the
		// roots have got muddled — and carrying on would write files outside the folder being filled.
		Assert.Throws<ArgumentException>(() => PathRebase.To(P("/source"), P("/elsewhere", "a.txt"), P("/dest")));
	}

	[Fact]
	public void ASiblingThatMerelyStartsTheSameIsNotInside()
	{
		// The same mistake as the common-prefix bug: "Mods/Auto" is not a parent of "Mods/AutoFish".
		Assert.Throws<ArgumentException>(() =>
			PathRebase.To(P("/games", "Auto"), P("/games", "AutoFish", "a.txt"), P("/dest")));
	}

	[Fact]
	public void NothingIsRefusedRatherThanGuessed()
	{
		Assert.Throws<ArgumentException>(() => PathRebase.To("", P("/a"), P("/b")));
		Assert.Throws<ArgumentException>(() => PathRebase.To(P("/a"), "", P("/b")));
		Assert.Throws<ArgumentException>(() => PathRebase.To(P("/a"), P("/a", "x"), ""));
	}

	[Fact]
	public void TheForgivingFormAnswersNullInsteadOfThrowing()
	{
		// For a caller walking a tree it did not build, which would rather skip an odd entry than stop.
		Assert.Null(PathRebase.TryTo(P("/source"), P("/elsewhere", "a.txt"), P("/dest")));
		Assert.NotNull(PathRebase.TryTo(P("/source"), P("/source", "a.txt"), P("/dest")));
	}
}
