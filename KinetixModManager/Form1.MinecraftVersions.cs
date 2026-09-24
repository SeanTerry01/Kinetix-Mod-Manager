using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Removing Minecraft versions nothing uses any more — on request, and when a modpack that brought one is deleted.
/// Deciding what is unused is <see cref="MinecraftVersionCleanup"/>, in the core; this is the asking and the doing.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// The versions something the manager runs needs: the player's own Minecraft, with every Fabric build for it,
	/// and each modpack's. Their parents are kept by the cleanup itself.
	///
	/// <para>
	/// ⚠️ When the player's own version is not known, every Fabric folder counts as theirs. "I do not know which of
	/// these you play" must never turn into "so none of them is yours" — that would remove the game they play.
	/// </para>
	/// </summary>
	private List<string> MinecraftVersionsInUse(string root)
	{
		var inUse = new List<string>();
		string own = OwnMinecraftVersion();

		IReadOnlyList<string> fabric = FabricInstaller.InstalledVersionIds(root);
		if (own.Length == 0) inUse.AddRange(fabric);
		else
		{
			inUse.Add(own);
			inUse.AddRange(fabric.Where(v => FabricInstaller.GameVersionOf(v) == own));
		}

		foreach (MinecraftPack pack in MinecraftModpacks.FindInstalled(MinecraftModpacks.PacksFolder))
		{
			inUse.Add(pack.MinecraftVersion);
			inUse.Add(pack.FabricVersionId);
		}

		return inUse;
	}

	/// <summary>
	/// Lists the versions nothing uses and removes the ones chosen — all of them at once, or one at a time. The list
	/// stays open after a removal while there is anything left in it, the way every list the player works through does.
	/// </summary>
	private async Task RemoveUnusedMinecraftVersionsAsync()
	{
		string root = MinecraftRootFolder();

		while (true)
		{
			IReadOnlyList<UnusedMinecraftVersion> unused =
				await Task.Run(() => MinecraftVersionCleanup.FindUnused(root, MinecraftVersionsInUse(root)));

			if (unused.Count == 0)
			{
				Speak(Loc.T("mc.versions.none"));
				return;
			}

			var choices = new Dictionary<string, IReadOnlyList<UnusedMinecraftVersion>>(StringComparer.Ordinal);
			if (unused.Count > 1)
				choices[Loc.T("mc.versions.all", unused.Count, FormatBytes(unused.Sum(v => v.Bytes)))] = unused;
			foreach (UnusedMinecraftVersion version in unused)
				choices[VersionRow(version)] = new[] { version };

			string? picked = ShowChoiceList(Loc.T("mc.versions.title"), Loc.T("mc.versions.listName"),
				choices.Keys.ToList(), choices.Keys.First(), Loc.T("mc.versions.hint"));
			if (picked is null || !choices.TryGetValue(picked, out IReadOnlyList<UnusedMinecraftVersion>? chosen))
			{
				Speak(Loc.T("common.changesCancelled"));
				return;
			}

			if (!await RemoveMinecraftVersionsAsync(root, chosen, Loc.T("mc.versions.confirm",
					string.Join(", ", chosen.Select(v => v.Id)), FormatBytes(chosen.Sum(v => v.Bytes)))))
				return;
		}
	}

	private string VersionRow(UnusedMinecraftVersion version)
	{
		string kind = version.IsFabric
			? Loc.T("mc.versions.rowFabric", version.Id, FormatBytes(version.Bytes))
			: Loc.T("mc.versions.row", version.Id, FormatBytes(version.Bytes));

		return version.LauncherInstallations.Count > 0
			? Loc.T("mc.versions.rowLauncher", kind, string.Join(", ", version.LauncherInstallations))
			: kind;
	}

	/// <summary>
	/// Asks, then removes the given version folders — and the official launcher's installations that start them,
	/// which would otherwise be left pointing at nothing. Returns false when the player said no or it could not be
	/// done, having said why.
	/// </summary>
	private async Task<bool> RemoveMinecraftVersionsAsync(string root, IReadOnlyList<UnusedMinecraftVersion> versions, string question)
	{
		List<string> installations = versions.SelectMany(v => v.LauncherInstallations)
			.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (installations.Count > 0)
			question += "\n\n" + Loc.T("mc.versions.launcherToo", string.Join(", ", installations));

		if (SpeakBox(question, Loc.T("mc.versions.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return false;
		}

		// The launcher rewrites its installation list when it closes, so a change made under it is undone.
		if (installations.Count > 0 && FabricInstaller.IsLauncherRunning())
		{
			SpeakBox(Loc.T("mc.fabric.closeLauncherBox"), Loc.T("mc.fabric.closeLauncherTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return false;
		}

		var failed = new List<string>();
		long freed = await Task.Run(() =>
		{
			long bytes = 0;
			foreach (UnusedMinecraftVersion version in versions)
			{
				try
				{
					Directory.Delete(Path.Combine(root, "versions", version.Id), recursive: true);
					bytes += version.Bytes;
				}
				catch (Exception ex)
				{
					// Most often a version the game is running from right now, holding its jar open.
					DiagnosticLog.WriteException("Minecraft", $"removing the version {version.Id}", ex);
					failed.Add(version.Id);
				}
			}

			try
			{
				MinecraftVersionCleanup.RemoveLauncherInstallations(root,
					versions.Where(v => !failed.Contains(v.Id)).Select(v => v.Id).ToList());
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", "removing launcher installations", ex); }

			return bytes;
		});

		if (failed.Count > 0)
			SpeakBox(Loc.T("mc.versions.someFailed", string.Join(", ", failed)), Loc.T("mc.versions.title"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
		else
			Speak(Loc.T("mc.versions.removed", string.Join(", ", versions.Select(v => v.Id)), FormatBytes(freed)));

		return failed.Count == 0;
	}

	/// <summary>
	/// After a pack is deleted: offers to remove the Minecraft version it brought, when nothing else — the player's
	/// own Minecraft, another pack — still uses it. Says nothing when something does.
	/// </summary>
	private async Task OfferToRemovePacksVersionAsync(MinecraftPack deleted)
	{
		string root = MinecraftRootFolder();
		var its = new HashSet<string>(new[] { deleted.MinecraftVersion, deleted.FabricVersionId }, StringComparer.OrdinalIgnoreCase);

		List<UnusedMinecraftVersion> unused = (await Task.Run(() =>
				MinecraftVersionCleanup.FindUnused(root, MinecraftVersionsInUse(root))))
			.Where(v => its.Contains(v.Id)).ToList();
		if (unused.Count == 0) return;

		await RemoveMinecraftVersionsAsync(root, unused,
			Loc.T("mc.versions.afterPack", deleted.MinecraftVersion, deleted.Name, FormatBytes(unused.Sum(v => v.Bytes))));
	}
}
