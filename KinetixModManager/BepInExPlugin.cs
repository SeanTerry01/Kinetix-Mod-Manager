using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>What a BepInEx plugin says about itself: its GUID, display name and version.</summary>
public sealed class BepInExPluginInfo
{
	/// <summary>The plugin's stable GUID, e.g. <c>com.moonlightaccess.core</c>.</summary>
	public string Guid { get; init; } = "";

	/// <summary>The plugin's display name, e.g. "Moonlight Access".</summary>
	public string Name { get; init; } = "";

	/// <summary>The plugin's version as the author declared it, e.g. "0.1.0".</summary>
	public string Version { get; init; } = "";
}

/// <summary>
/// Reads the identity of BepInEx plugins (Moonlight Peaks' mods).
///
/// A BepInEx plugin declares itself with a <c>[BepInPlugin(guid, name, version)]</c> attribute on its main
/// class, and that attribute is the only reliable source for the three things the manager needs. The obvious
/// alternatives are both wrong often enough to matter: the DLL's own file version is frequently left at
/// 0.0.0.0 (several of the plugins on a normal Moonlight Peaks install report exactly that), and the folder
/// name is whatever the archive happened to unpack as.
///
/// Reading the attribute is done with <see cref="System.Reflection.Metadata"/> rather than by loading the
/// assembly, because loading a plugin DLL would run its module initializer and pull in game and BepInEx
/// dependencies the manager doesn't have. Metadata reading only ever reads bytes.
///
/// Two fallbacks cover plugins the attribute can't be read from (obfuscated, packed, or built against an
/// unusual BepInEx): the BepInEx log records "Loading [Name Version]" for every plugin it loads, and a
/// plugin with settings writes its name, version and GUID into the header of its config file.
/// </summary>
public static class BepInExPlugin
{
	/// <summary>The attribute a BepInEx plugin's main class carries.</summary>
	private const string PluginAttributeName = "BepInPlugin";

	/// <summary>
	/// Reads the <c>[BepInPlugin]</c> attribute from <paramref name="dllPath"/>, or returns <c>null</c> when the
	/// file isn't a managed assembly, carries no such attribute, or can't be read. Never throws.
	/// </summary>
	public static BepInExPluginInfo? ReadFromAssembly(string dllPath)
	{
		try
		{
			if (!File.Exists(dllPath)) return null;

			using var stream = File.OpenRead(dllPath);
			using var peReader = new PEReader(stream);
			if (!peReader.HasMetadata) return null;

			MetadataReader reader = peReader.GetMetadataReader();

			foreach (CustomAttributeHandle handle in reader.CustomAttributes)
			{
				CustomAttribute attribute = reader.GetCustomAttribute(handle);
				if (!IsPluginAttribute(reader, attribute)) continue;

				BepInExPluginInfo? info = DecodePluginAttribute(reader, attribute);
				if (info != null) return info;
			}
		}
		catch (Exception ex)
		{
			// A corrupt, packed or native DLL is a normal thing to meet in a mods folder — the callers fall
			// back to the log and the config file, and finally to the folder name. Still recorded, because it is
			// also what a half-written download looks like.
			DiagnosticLog.WriteException("BepInEx", $"reading the plugin details out of {dllPath}", ex);
		}

		return null;
	}

	/// <summary>True when <paramref name="attribute"/> is a <c>[BepInPlugin]</c>.</summary>
	private static bool IsPluginAttribute(MetadataReader reader, CustomAttribute attribute)
	{
		try
		{
			StringHandle typeName;
			switch (attribute.Constructor.Kind)
			{
				case HandleKind.MemberReference:
				{
					MemberReference member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
					if (member.Parent.Kind != HandleKind.TypeReference) return false;
					typeName = reader.GetTypeReference((TypeReferenceHandle)member.Parent).Name;
					break;
				}
				case HandleKind.MethodDefinition:
				{
					MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
					typeName = reader.GetTypeDefinition(method.GetDeclaringType()).Name;
					break;
				}
				default:
					return false;
			}

			string name = reader.GetString(typeName);
			// The attribute class may be referenced by its bare name or with the usual "Attribute" suffix.
			return name == PluginAttributeName || name == PluginAttributeName + "Attribute";
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Decodes the three strings a <c>[BepInPlugin]</c> carries. The attribute blob is a two-byte prolog
	/// followed by the constructor's fixed arguments in order, each a length-prefixed UTF-8 string.
	/// </summary>
	private static BepInExPluginInfo? DecodePluginAttribute(MetadataReader reader, CustomAttribute attribute)
	{
		try
		{
			BlobReader blob = reader.GetBlobReader(attribute.Value);
			if (blob.Length < 2) return null;
			if (blob.ReadUInt16() != 1) return null; // prolog

			string? guid    = blob.ReadSerializedString();
			string? name    = blob.ReadSerializedString();
			string? version = blob.ReadSerializedString();

			if (string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(name)) return null;

			return new BepInExPluginInfo
			{
				Guid    = guid ?? "",
				Name    = name ?? "",
				Version = version ?? ""
			};
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Every plugin BepInEx reported loading in <paramref name="logPath"/> (its <c>LogOutput.log</c>), keyed by
	/// plugin name. The chainloader writes one <c>Loading [Name Version]</c> line per plugin, which is the only
	/// place a version shows up for a plugin whose attribute can't be read. An unreadable or absent log is not
	/// an error — it just means nothing to add.
	/// </summary>
	public static Dictionary<string, string> ParseLoadedPluginsFromLog(string logPath)
	{
		var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			if (!File.Exists(logPath)) return found;

			// Read shared: BepInEx may still hold the log open if the game is running.
			using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var text = new StreamReader(stream);

			string? line;
			while ((line = text.ReadLine()) != null)
			{
				Match m = LoadingLine.Match(line);
				if (!m.Success) continue;
				string name = m.Groups["name"].Value.Trim();
				string version = m.Groups["version"].Value.Trim();
				if (name.Length > 0) found[name] = version;
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("BepInEx", $"reading the BepInEx log at {logPath}", ex); }
		return found;
	}

	/// <summary>
	/// Matches the chainloader's own load lines, e.g.
	/// <c>[Info   :   BepInEx] Loading [Moonlight Access 0.1.0]</c>. The name may contain spaces, so the version
	/// is anchored as the last whitespace-separated token inside the brackets.
	/// </summary>
	private static readonly Regex LoadingLine = new Regex(
		@"\]\s*Loading\s*\[(?<name>.+?)\s+(?<version>\d[\w.\-+]*)\]\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	/// <summary>
	/// Reads the name, version and GUID a plugin wrote into the header of its BepInEx config file, e.g.
	/// <c>## Settings file was created by plugin Moonlight Access v0.1.0</c> followed by
	/// <c>## Plugin GUID: com.moonlightaccess.core</c>. Returns <c>null</c> when the file has no such header.
	/// </summary>
	public static BepInExPluginInfo? ReadFromConfig(string configPath)
	{
		try
		{
			if (!File.Exists(configPath)) return null;

			string name = "", version = "", guid = "";
			foreach (string line in File.ReadLines(configPath).Take(10))
			{
				Match created = ConfigCreatedBy.Match(line);
				if (created.Success)
				{
					name = created.Groups["name"].Value.Trim();
					version = created.Groups["version"].Value.Trim();
					continue;
				}
				Match id = ConfigGuid.Match(line);
				if (id.Success) guid = id.Groups["guid"].Value.Trim();
			}

			if (name.Length == 0 && guid.Length == 0) return null;
			return new BepInExPluginInfo { Guid = guid, Name = name, Version = version };
		}
		catch
		{
			return null;
		}
	}

	private static readonly Regex ConfigCreatedBy = new Regex(
		@"^##\s*Settings file was created by plugin\s+(?<name>.+?)\s+v(?<version>[\w.\-+]+)\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex ConfigGuid = new Regex(
		@"^##\s*Plugin GUID:\s*(?<guid>\S+)\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	/// <summary>
	/// The identity of the mod in <paramref name="modFolder"/>, built from the best source available: the
	/// <c>[BepInPlugin]</c> attribute of the DLLs inside it, then the BepInEx log, then the folder name.
	///
	/// A mod folder can hold more than one plugin DLL (a plugin shipping its own helper libraries, or a pack of
	/// several plugins). The one whose name best matches the folder wins, since that is the mod the user
	/// installed and named; the rest are its companions rather than separate mods.
	/// </summary>
	/// <param name="logWrittenUtc">
	/// When the BepInEx log was last written, if known. The log records what the chainloader saw the last time
	/// the game <em>ran</em>, so one older than the files it describes is out of date and is not consulted.
	/// </param>
	public static BepInExPluginInfo Identify(
		string modFolder,
		IReadOnlyDictionary<string, string>? loggedPlugins = null,
		DateTime? logWrittenUtc = null)
	{
		string folderName = Path.GetFileName(modFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

		var candidates = new List<BepInExPluginInfo>();
		DateTime newestDll = DateTime.MinValue;
		try
		{
			foreach (string dll in Directory.EnumerateFiles(modFolder, "*.dll", SearchOption.AllDirectories))
			{
				try
				{
					DateTime written = File.GetLastWriteTimeUtc(dll);
					if (written > newestDll) newestDll = written;
				}
				catch (Exception ex) { DiagnosticLog.WriteException("BepInEx", $"reading the age of {dll}", ex); }

				BepInExPluginInfo? info = ReadFromAssembly(dll);
				if (info != null) candidates.Add(info);
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("BepInEx", $"identifying the plugin in {modFolder}", ex); }

		// The log describes a past run; the DLLs describe what is installed now. Once a mod has been updated the
		// log is talking about files that no longer exist, so it must not be allowed to answer for them.
		//
		// This is not hypothetical: on a real install, three mods updated to 1.1 went on being listed as 1.0.0
		// because the last BepInEx log predated the update by six days — and the update check, which compares
		// what was downloaded rather than what the log says, quite correctly reported them up to date. The two
		// disagreed and the user was left with a mod that said it needed no update and showed the old number.
		bool logIsCurrent = loggedPlugins != null &&
			(logWrittenUtc == null || newestDll == DateTime.MinValue || logWrittenUtc >= newestDll);

		BepInExPluginInfo? best = PickBest(candidates, folderName);

		if (best != null)
		{
			// The plugin's own declaration is the answer whenever it makes one. The log is consulted only for a
			// plugin that declares a placeholder in its attribute while reporting a real version to the
			// chainloader, which does happen — but it is the exception, not the rule it used to be treated as.
			if (!IsPlaceholderVersion(best.Version)) return best;

			if (logIsCurrent &&
				loggedPlugins!.TryGetValue(best.Name, out string? loggedVersion) &&
				!string.IsNullOrEmpty(loggedVersion))
			{
				return new BepInExPluginInfo { Guid = best.Guid, Name = best.Name, Version = loggedVersion };
			}
			return best;
		}

		// No readable attribute anywhere in the folder: fall back to a logged plugin whose name looks like this
		// folder, and finally to the folder name itself with no version claim.
		if (logIsCurrent)
		{
			foreach (var kv in loggedPlugins!)
			{
				if (NamesMatch(kv.Key, folderName))
					return new BepInExPluginInfo { Guid = "", Name = kv.Key, Version = kv.Value };
			}
		}

		return new BepInExPluginInfo { Guid = "", Name = folderName, Version = "" };
	}

	/// <summary>
	/// True for a version that says nothing — an unset attribute, or the all-zero default a plugin gets when its
	/// author never filled one in. <c>1.0.0</c> is deliberately NOT one of these: it is what a great many mods
	/// genuinely are, and treating it as missing would hand their identity back to a stale log.
	/// </summary>
	private static bool IsPlaceholderVersion(string? version) =>
		string.IsNullOrWhiteSpace(version) ||
		version is "0" or "0.0" or "0.0.0" or "0.0.0.0";

	/// <summary>
	/// Chooses the plugin that the mod folder is named for. An exact-ish name match wins; failing that the
	/// first plugin found, which for the overwhelmingly common one-DLL mod is simply the only one.
	/// </summary>
	private static BepInExPluginInfo? PickBest(List<BepInExPluginInfo> candidates, string folderName)
	{
		if (candidates.Count == 0) return null;
		if (candidates.Count == 1) return candidates[0];

		BepInExPluginInfo? named = candidates.FirstOrDefault(c => NamesMatch(c.Name, folderName));
		if (named != null) return named;

		BepInExPluginInfo? byGuid = candidates.FirstOrDefault(c => NamesMatch(c.Guid, folderName));
		return byGuid ?? candidates[0];
	}

	/// <summary>
	/// Compares a plugin name against a folder name ignoring case, spaces, dots, dashes and underscores, so
	/// "Save Anywhere", "SaveAnywhere" and "save-anywhere" all count as the same mod.
	/// </summary>
	private static bool NamesMatch(string? a, string? b)
	{
		string na = Normalize(a), nb = Normalize(b);
		if (na.Length == 0 || nb.Length == 0) return false;
		return na == nb || na.EndsWith(nb, StringComparison.Ordinal) || nb.EndsWith(na, StringComparison.Ordinal);
	}

	private static string Normalize(string? value)
	{
		if (string.IsNullOrEmpty(value)) return "";
		return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
	}
}
