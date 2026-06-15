using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Lightweight localization catalog. Every user-facing string the program shows or speaks lives
/// in a JSON file under the app's <c>lang/</c> folder (<c>lang/en.json</c>, <c>lang/es.json</c>, …),
/// a flat object of <c>"key": "text"</c> pairs. English (<c>en.json</c>) is the canonical source of
/// truth and is always loaded as a fallback, so a partially-translated language never shows blank
/// text — any key it is missing reads in English. Translators only ever edit JSON; adding or
/// updating a language needs no recompile, just a new file dropped into <c>lang/</c>.
/// </summary>
public static class Loc
{
	// Active language catalog, then the English fallback. Ordinal keys: these are identifiers, not text.
	private static Dictionary<string, string> _strings = new(StringComparer.Ordinal);
	private static Dictionary<string, string> _fallback = new(StringComparer.Ordinal);
	private static string _langDir = "";

	/// <summary>The culture actually loaded; assigned to the UI thread so .NET's own formatting matches.</summary>
	public static CultureInfo ActiveCulture { get; private set; } = CultureInfo.GetCultureInfo("en");

	/// <summary>Two-letter code of the active language (e.g. "en", "es").</summary>
	public static string ActiveLanguage { get; private set; } = "en";

	/// <summary>
	/// Loads the catalog. <paramref name="preferred"/> is the user's saved choice: a language code
	/// like "es", or empty/"auto" to follow the Windows display language. Falls back to English when
	/// the requested language ships no file. Safe to call again to switch languages at runtime.
	/// </summary>
	public static void Init(string preferred)
	{
		_langDir = Path.Combine(AppContext.BaseDirectory, "lang");
		_fallback = LoadFile("en");

		string code = ResolveLanguage(preferred);
		_strings = code == "en" ? _fallback : LoadFile(code);

		// A missing or empty language file falls back to English so the UI always reads.
		if (_strings.Count == 0)
		{
			_strings = _fallback;
			code = "en";
		}

		ActiveLanguage = code;
		try { ActiveCulture = CultureInfo.GetCultureInfo(code); }
		catch (CultureNotFoundException) { ActiveCulture = CultureInfo.GetCultureInfo("en"); }
	}

	/// <summary>
	/// Decides which language code to load: the explicit choice when given and shipped, otherwise the
	/// Windows display language when we ship it, otherwise English.
	/// </summary>
	private static string ResolveLanguage(string preferred)
	{
		if (!string.IsNullOrWhiteSpace(preferred) &&
			!preferred.Equals("auto", StringComparison.OrdinalIgnoreCase))
		{
			return preferred.Trim().ToLowerInvariant();
		}

		// Auto: follow the user's Windows display language (its two-letter code, e.g. "es").
		string os = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
		return FileExists(os) ? os : "en";
	}

	/// <summary>
	/// Looks up <paramref name="key"/> in the active language, then English. When <paramref name="args"/>
	/// are supplied the result is run through <see cref="string.Format(IFormatProvider, string, object[])"/>,
	/// so catalog values use <c>{0}</c>, <c>{1}</c>… placeholders (which a translation may reorder).
	/// A key missing from both catalogs returns the key itself, making the gap obvious during testing.
	/// </summary>
	public static string T(string key, params object[] args)
	{
		if (!_strings.TryGetValue(key, out string? value))
			_fallback.TryGetValue(key, out value);
		value ??= key;

		if (args == null || args.Length == 0) return value;
		try { return string.Format(ActiveCulture, value, args); }
		catch (FormatException) { return value; }
	}

	/// <summary>
	/// The languages shipped in <c>lang/</c>, as (code, display name) pairs for the Settings picker.
	/// The display name is read from each file's optional <c>"_name"</c> entry, falling back to the code.
	/// </summary>
	public static IEnumerable<LanguageChoice> AvailableLanguages()
	{
		if (!Directory.Exists(_langDir)) yield break;
		foreach (string path in Directory.GetFiles(_langDir, "*.json").OrderBy(p => p))
		{
			string code = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
			string name = code;
			try
			{
				JObject obj = JObject.Parse(File.ReadAllText(path));
				name = obj["_name"]?.ToString() ?? code;
			}
			catch { /* unparseable file: fall back to showing the bare code */ }
			yield return new LanguageChoice { Code = code, Display = name };
		}
	}

	private static bool FileExists(string code) =>
		File.Exists(Path.Combine(_langDir, code + ".json"));

	private static Dictionary<string, string> LoadFile(string code)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		try
		{
			string path = Path.Combine(_langDir, code + ".json");
			if (!File.Exists(path)) return result;
			JObject obj = JObject.Parse(File.ReadAllText(path));
			foreach (JProperty prop in obj.Properties())
			{
				if (prop.Name.StartsWith("_")) continue; // metadata (e.g. "_name"), not a UI string
				result[prop.Name] = prop.Value.ToString();
			}
		}
		catch { /* a broken file leaves the dict empty; the caller falls back to English */ }
		return result;
	}
}

/// <summary>A language entry for the Settings picker; <see cref="Code"/> is empty for "automatic".</summary>
public sealed class LanguageChoice
{
	public string Code { get; init; } = "";
	public string Display { get; init; } = "";
	public override string ToString() => Display;
}
