using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// What a test copy of the manager has been told to use. See <see cref="TestMode"/>.
/// </summary>
public sealed class TestModeSettings
{
	/// <summary>The folder everything lives under. Only folders inside it, or named in <see cref="Games"/>, are ever used.</summary>
	public required string Root { get; init; }

	/// <summary>Copies of games, by game id, kept wherever there was room for them.</summary>
	public IReadOnlyDictionary<string, string> Games { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Whether the manager acts as though the Nexus account is not Premium. The account itself is not touched.</summary>
	public bool PretendFreeNexus { get; set; }

	/// <summary>Where the test copy's own settings, mods, downloads and backups live.</summary>
	public string DataFolder => Path.Combine(Root, "Data");

	/// <summary>Reads the file's text. A file that can't be read still means test mode, because the mistake to avoid is a test copy using real data.</summary>
	public static TestModeSettings Parse(string? json, string baseFolder)
	{
		JObject doc;
		try { doc = JObject.Parse(json ?? "{}"); }
		catch (Exception ex) when (ex is Newtonsoft.Json.JsonException)
		{
			DiagnosticLog.WriteException("TestMode", "reading the test mode file", ex);
			doc = new JObject();
		}

		string root = (string?)doc["Root"] is { Length: > 0 } written ? written : ".";
		var games = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (doc["Games"] is JObject listed)
			foreach (var game in listed.Properties())
				if ((string?)game.Value is { Length: > 0 } folder) games[game.Name] = Path.GetFullPath(folder, baseFolder);

		return new TestModeSettings
		{
			Root = Path.GetFullPath(root, baseFolder),
			Games = games,
			PretendFreeNexus = (bool?)doc["PretendFreeNexus"] ?? false
		};
	}

	/// <summary>Whether a test copy may use this folder: inside the test folder, or a game copy it was given.</summary>
	public bool Allows(string? path)
	{
		if (string.IsNullOrWhiteSpace(path)) return true;

		string full;
		try { full = Path.GetFullPath(path); }
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }

		return IsInside(Root, full) || Games.Values.Any(game => IsInside(game, full));
	}

	/// <summary>The copy of a game this test copy was given, or null. Goes by the game, so a second copy's key finds it too.</summary>
	public string? GameFolderFor(string gameId) =>
		Games.TryGetValue(GameProfiles.BaseId(gameId), out string? folder) ? folder : null;

	/// <summary>Takes out every recorded folder that points outside the test folder, so nothing can use one by default.</summary>
	public void ForgetFoldersOutside(AppSettings settings)
	{
		foreach (Dictionary<string, string> paths in new[] { settings.GamePaths, settings.GameModsPaths })
			foreach (string key in paths.Keys.ToList())
				if (!Allows(paths[key])) paths[key] = "";

		if (!Allows(settings.ModsPath)) settings.ModsPath = "";
		settings.GameInstalls?.RemoveAll(install => !Allows(install.Folder));
	}

	private static bool IsInside(string folder, string path)
	{
		string root = Path.GetFullPath(folder);
		if (!Path.EndsInDirectorySeparator(root)) root += Path.DirectorySeparatorChar;
		return (path + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.OrdinalIgnoreCase);
	}
}

/// <summary>
/// Lets a test copy of the manager run beside the real one, using only its own folder.
///
/// <para>
/// A file called <c>test-mode.json</c> beside the program turns it on, and only the tester zip has one, so an installed copy
/// can't be in test mode. It redirects where the manager itself reads and writes. It can't reach a game that gets started: a
/// Skyrim or Stardew copy still reads your real saves, because the game decides that, not the manager. Minecraft runs from its
/// own folder, so it is the one game wholly inside.
/// </para>
/// </summary>
public static class TestMode
{
	public const string FileName = "test-mode.json";

	private static readonly Lazy<TestModeSettings?> Loaded = new(() =>
	{
		string file = Path.Combine(AppContext.BaseDirectory, FileName);
		if (!File.Exists(file)) return null;

		string? text = null;
		try { text = File.ReadAllText(file); }
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			DiagnosticLog.WriteException("TestMode", "reading the test mode file", ex);
		}
		return TestModeSettings.Parse(text, AppContext.BaseDirectory);
	});

	/// <summary>What this copy was told to use, or null when it is an ordinary copy.</summary>
	public static TestModeSettings? Current => Loaded.Value;

	public static bool IsOn => Current != null;

	/// <summary>
	/// The name a running manager answers to. A test copy has its own, or starting it beside the real one would quietly do
	/// nothing, and a Nexus link meant for one could reach the other.
	/// </summary>
	public static string HandlerName => IsOn ? "KinetixModManager-Nexus-Handler-Test" : "KinetixModManager-Nexus-Handler";

	/// <summary>Remembers the free-account switch in the test mode file, so it survives a restart.</summary>
	public static void SetPretendFreeNexus(bool value)
	{
		if (Current is not { } current) return;
		current.PretendFreeNexus = value;

		string file = Path.Combine(AppContext.BaseDirectory, FileName);
		try
		{
			JObject doc = File.Exists(file) ? JObject.Parse(File.ReadAllText(file)) : new JObject();
			doc["PretendFreeNexus"] = value;
			AtomicFile.WriteAllText(file, doc.ToString());
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
		{
			DiagnosticLog.WriteException("TestMode", "saving the free-account switch", ex);
		}
	}
}

/// <summary>
/// The folders that belong to the person using the computer. Everything goes through here rather than asking Windows directly,
/// so a test copy stays inside its test folder.
/// </summary>
public static class UserFolders
{
	public static string ApplicationData => Resolve(Environment.SpecialFolder.ApplicationData, "AppData", "Roaming");
	public static string LocalApplicationData => Resolve(Environment.SpecialFolder.LocalApplicationData, "AppData", "Local");
	public static string Documents => Resolve(Environment.SpecialFolder.MyDocuments, "Documents");
	public static string UserProfile => Resolve(Environment.SpecialFolder.UserProfile, "Profile");

	private static string Resolve(Environment.SpecialFolder real, params string[] underData) =>
		TestMode.Current is { } test
			? Path.Combine(new[] { test.DataFolder }.Concat(underData).ToArray())
			: Environment.GetFolderPath(real);
}
