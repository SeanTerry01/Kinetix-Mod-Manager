using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>One setting a Content Patcher pack lets the player change.</summary>
public sealed class CpConfigOption
{
	/// <summary>The setting's name — the key in both <c>ConfigSchema</c> and the generated config file.</summary>
	public string Name { get; init; } = "";

	/// <summary>
	/// The values the author allows, or empty when the setting takes free text (a number, a name, a co-ordinate).
	/// </summary>
	public List<string> AllowedValues { get; init; } = new();

	/// <summary>The author's default, used when the config file has no answer for this setting.</summary>
	public string Default { get; init; } = "";

	/// <summary>The author's explanation of the setting, or <c>""</c> when they didn't write one.</summary>
	public string Description { get; init; } = "";

	/// <summary>The author's grouping label for related settings, or <c>""</c>.</summary>
	public string Section { get; init; } = "";

	/// <summary>True when several of <see cref="AllowedValues"/> may be chosen at once, comma-separated.</summary>
	public bool AllowMultiple { get; init; }

	/// <summary>True when the setting may be left empty.</summary>
	public bool AllowBlank { get; init; }

	/// <summary>True when the author listed the values this setting accepts, so it can be offered as a choice.</summary>
	public bool HasChoices => AllowedValues.Count > 0;
}

/// <summary>
/// Reads what a Content Patcher content pack lets the player configure.
///
/// A Content Patcher pack is not code: it is a <c>content.json</c> describing changes to the game's assets. An
/// author can declare configurable settings there under <c>ConfigSchema</c>, each with the values it accepts and
/// its default. When the pack first loads, Content Patcher generates a plain <c>config.json</c> beside it
/// holding only the chosen values — and that stripped-down file is the one a player is left editing.
///
/// Which is the problem this class exists to solve. The config file says <c>"ObeliskOptions": "vanilla"</c> and
/// nothing else; that the answer must be one of vanilla, glass, garden, Yri or Juffuffles is knowledge that
/// stayed behind in <c>content.json</c>. Reading the schema puts the two back together, so a setting can be
/// offered as a list of the values the author actually allows instead of a string typed blind.
///
/// These files are written by hand and are not strict JSON: comments, trailing commas and erratic indentation
/// are all normal, and a real pack was found with a trailing comma inside a value (<c>"True, False,"</c>). The
/// reader is deliberately lenient about all of it — refusing to read an author's file over a stray comma would
/// fail exactly the packs this feature is for.
/// </summary>
public static class ContentPatcherConfig
{
	/// <summary>The unique id of Content Patcher itself, which every pack of this kind declares it is for.</summary>
	public const string ContentPatcherId = "Pathoschild.ContentPatcher";

	/// <summary>
	/// The pack's <c>content.json</c>, or <c>null</c> when this mod folder isn't a Content Patcher pack.
	/// </summary>
	public static string? FindContentJson(string modFolder)
	{
		try
		{
			if (string.IsNullOrEmpty(modFolder)) return null;
			string path = Path.Combine(modFolder, "content.json");
			return File.Exists(path) ? path : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// The settings a pack declares, in the order the author wrote them, or an empty list when it declares none.
	/// Most packs have nothing to configure, which is not a failure — it just means there is nothing to offer.
	/// </summary>
	public static List<CpConfigOption> ReadSchema(string contentJsonPath)
	{
		var options = new List<CpConfigOption>();
		try
		{
			if (!File.Exists(contentJsonPath)) return options;

			JObject? content = ParseLenient(File.ReadAllText(contentJsonPath));
			if (content?["ConfigSchema"] is not JObject schema) return options;

			foreach (JProperty property in schema.Properties())
			{
				if (property.Value is not JObject entry) continue;

				options.Add(new CpConfigOption
				{
					Name = property.Name,
					AllowedValues = SplitAllowedValues((string?)entry["AllowValues"]),
					Default = TokenAsText(entry["Default"]),
					Description = (string?)entry["Description"] ?? "",
					Section = (string?)entry["Section"] ?? "",
					AllowMultiple = (bool?)entry["AllowMultiple"] ?? false,
					AllowBlank = (bool?)entry["AllowBlank"] ?? false
				});
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Content Patcher", "reading a mod's config schema", ex); }

		return options;
	}

	/// <summary>
	/// The pack's current answers, read from the <c>config.json</c> Content Patcher generated. Missing file or
	/// missing setting both mean "no answer yet", which the caller fills in from the schema's default.
	/// </summary>
	public static Dictionary<string, string> ReadValues(string configJsonPath)
	{
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			if (!File.Exists(configJsonPath)) return values;

			JObject? config = ParseLenient(File.ReadAllText(configJsonPath));
			if (config == null) return values;

			foreach (JProperty property in config.Properties())
				values[property.Name] = TokenAsText(property.Value);
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Content Patcher", $"reading the settings in {configJsonPath}", ex); }

		return values;
	}

	/// <summary>
	/// Writes the player's answers back to <paramref name="configJsonPath"/>, optionally deleting the settings
	/// named in <paramref name="remove"/>.
	///
	/// The existing file is edited rather than replaced, so anything in it the manager doesn't know about
	/// survives. A value's JSON type is kept too: a pack that stored <c>true</c> as a boolean gets a boolean
	/// back, not the string "true" — Content Patcher accepts either, but rewriting a file's types wholesale is
	/// the sort of change that makes a diff look alarming and an author's bug report harder to read.
	///
	/// Deleting a setting is how "put it back to the author's default" is done. Writing the current default in
	/// its place would look identical today and be wrong tomorrow: Content Patcher regenerates a missing setting
	/// from the schema each time it loads, so a deleted one follows the author if they change their mind in a
	/// later version, while a written-out copy would silently pin the old value forever.
	/// </summary>
	public static bool WriteValues(string configJsonPath, IReadOnlyDictionary<string, string> values,
		IReadOnlyCollection<string>? remove = null)
	{
		try
		{
			JObject config = (File.Exists(configJsonPath)
				? ParseLenient(File.ReadAllText(configJsonPath))
				: null) ?? new JObject();

			foreach ((string name, string value) in values)
				config[name] = MatchExistingType(config[name], value);

			foreach (string name in remove ?? Array.Empty<string>())
			{
				// Matched without regard to case, because the config file's spelling of a setting need not be
				// the schema's — and a "removal" that missed would silently leave the old answer in place.
				JProperty? property = config.Properties()
					.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
				property?.Remove();
			}

			// Written through a temporary file and moved into place, so an interrupted write cannot leave the
			// pack with a half-written config that Content Patcher would refuse at startup.
			string temporary = configJsonPath + ".tmp";
			File.WriteAllText(temporary, config.ToString(Formatting.Indented));
			File.Move(temporary, configJsonPath, overwrite: true);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Keeps <paramref name="newValue"/> in the same JSON type the file already used for that setting, where it
	/// still fits. Anything else becomes a string, which is what Content Patcher writes itself.
	/// </summary>
	private static JToken MatchExistingType(JToken? existing, string newValue)
	{
		if (existing?.Type == JTokenType.Boolean && bool.TryParse(newValue, out bool asBool))
			return new JValue(asBool);

		if (existing?.Type == JTokenType.Integer && long.TryParse(newValue, out long asLong))
			return new JValue(asLong);

		if (existing?.Type == JTokenType.Float &&
			double.TryParse(newValue, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out double asDouble))
			return new JValue(asDouble);

		return new JValue(newValue);
	}

	/// <summary>
	/// Splits an <c>AllowValues</c> string into the values it lists. Blanks are dropped, which is what makes a
	/// trailing comma harmless — <c>"True, False,"</c> is two choices, not three with an empty one on the end.
	/// </summary>
	private static List<string> SplitAllowedValues(string? allowValues)
	{
		if (string.IsNullOrWhiteSpace(allowValues)) return new List<string>();

		return allowValues
			.Split(',')
			.Select(value => value.Trim())
			.Where(value => value.Length > 0)
			.ToList();
	}

	/// <summary>A token's text without JSON quoting, so <c>true</c> and <c>"true"</c> both read as "true".</summary>
	private static string TokenAsText(JToken? token)
	{
		if (token == null || token.Type == JTokenType.Null) return "";
		return token.Type == JTokenType.String ? (string?)token ?? "" : token.ToString(Formatting.None).Trim('"');
	}

	/// <summary>
	/// Parses hand-written JSON: comments and trailing commas allowed, because Stardew's own loader allows them
	/// and authors therefore use them freely.
	/// </summary>
	private static JObject? ParseLenient(string json)
	{
		try
		{
			using var reader = new JsonTextReader(new StringReader(json));
			return JObject.Load(reader, new JsonLoadSettings
			{
				CommentHandling = CommentHandling.Ignore,
				LineInfoHandling = LineInfoHandling.Ignore
			});
		}
		catch
		{
			return null;
		}
	}
}
