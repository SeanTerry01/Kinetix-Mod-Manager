using System;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// One installed Minecraft mod, and the sentence that describes it.
///
/// The sentence is the whole row as far as a screen reader is concerned, which is why it is built here in
/// one piece rather than assembled from several labels sitting beside each other. Orca reads a row's
/// contents in order, and three separate labels become three stops to arrow through instead of one fact.
/// This is the same reasoning behind every <c>ToString()</c> on the row types in Kinetix.Core/Models.
/// </summary>
public sealed class ModRow
{
	public required string JarPath { get; init; }
	public required string Name { get; init; }
	public required string Version { get; init; }
	public required bool Enabled { get; init; }

	/// <summary>What the reader says for this row.</summary>
	public string Spoken
	{
		get
		{
			string name = string.IsNullOrWhiteSpace(Name) ? System.IO.Path.GetFileName(JarPath) : Name;
			string version = string.IsNullOrWhiteSpace(Version) ? "" : " " + Version;
			// State first. The user is arrowing a list to find what is switched off, and putting it last
			// means hearing the whole name before the one word that was being listened for.
			string state = Enabled ? "" : "disabled, ";
			return $"{state}{name}{version}";
		}
	}
}
