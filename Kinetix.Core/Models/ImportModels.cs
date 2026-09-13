// What is read out of a Mod Organizer 2 install when importing from one.
//
// Lifted out of Form1 by Phase 2 of the core split. These are plain data - what a row holds and how
// it reads aloud - so they belong beside the rules rather than inside the window that happens to
// show them today. While they were private to Form1 no second front end could name them at all.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>A regular MO2 mod read from modlist.txt: its folder name and whether it is enabled.</summary>
public readonly record struct Mo2Mod(string Name, bool Enabled);

/// <summary>One row of the profile chooser: a profile and its mod counts, read aloud on focus.</summary>
public sealed class Mo2ProfileEntry
{
	public string Dir = "";
	public string Summary = "";
	public override string ToString() => Summary;
}
