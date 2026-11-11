using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Pins <see cref="BethesdaPlugins.LoadablePluginFiles"/>: only a plugin at the top of a stored mod folder deploys into
/// <c>Data</c>, where the game loads it. One in a subfolder deploys to <c>Data\Optional\</c> and never loads, so it must
/// not reach plugins.txt, the plugin limit, or the missing-masters check as though it were there.
/// </summary>
public class LoadablePluginFilesTests : IDisposable
{
	private readonly string _mod = Path.Combine(Path.GetTempPath(), "kinetix-plugins-" + Guid.NewGuid().ToString("N"));

	public LoadablePluginFilesTests()
	{
		Directory.CreateDirectory(Path.Combine(_mod, "Optional"));
		Directory.CreateDirectory(Path.Combine(_mod, "Root"));
		Directory.CreateDirectory(Path.Combine(_mod, ".kinetix_docs"));
		File.WriteAllText(Path.Combine(_mod, "Foo.esp"), "");
		File.WriteAllText(Path.Combine(_mod, "Foo Light.esl"), "");
		File.WriteAllText(Path.Combine(_mod, "readme.txt"), "");
		File.WriteAllText(Path.Combine(_mod, "Optional", "Bar.esp"), "");
		File.WriteAllText(Path.Combine(_mod, "Root", "NotData.esp"), "");
		File.WriteAllText(Path.Combine(_mod, ".kinetix_docs", "Kept.esp"), "");
	}

	public void Dispose()
	{
		try { Directory.Delete(_mod, recursive: true); } catch { }
	}

	[Fact]
	public void OnlyTopLevelPluginsLoad()
	{
		var names = BethesdaPlugins.LoadablePluginFiles(_mod).Select(Path.GetFileName).OrderBy(n => n).ToList();

		Assert.Equal(new[] { "Foo Light.esl", "Foo.esp" }, names);
	}

	[Fact]
	public void ModDependenciesAgree()
	{
		var names = ModDependencies.PluginFiles(new GameMod { Name = "Foo", FolderPath = _mod })
			.Select(Path.GetFileName).OrderBy(n => n).ToList();

		Assert.Equal(new[] { "Foo Light.esl", "Foo.esp" }, names);
	}

	[Fact]
	public void NestedPluginsAreTheOnesInOrdinarySubfolders()
	{
		// Root\ deploys beside the exe on purpose, and dot folders are the manager's own, so neither is an option folder.
		string only = Assert.Single(BethesdaPlugins.NestedPluginFiles(_mod));

		Assert.Equal(Path.Combine("Optional", "Bar.esp"), only);
	}

	[Fact]
	public void AMissingFolderHasNone()
	{
		string gone = Path.Combine(_mod, "no such folder");

		Assert.Empty(BethesdaPlugins.LoadablePluginFiles(gone));
		Assert.Empty(BethesdaPlugins.NestedPluginFiles(gone));
	}
}
