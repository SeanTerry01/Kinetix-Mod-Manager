using System;

namespace KinetixModManager;

/// <summary>
/// The one <see cref="ISecretStore"/> the program is using.
///
/// <para>
/// Here, in the core, rather than on <c>AppSettings</c> where it started — because <c>AppSettings</c> is in
/// the WinForms project and the GTK head cannot see it. That was not a tidiness problem. The default store
/// does not protect anything at all, and only the Windows startup was replacing it, so the first time a
/// second front end grew somewhere to type a key that key would have been written into <c>settings.json</c>
/// in clear text with nothing anywhere to notice. A gate every head can reach is the difference.
/// </para>
///
/// <para>
/// A mutable static, which is worth the wince: it is assigned once at startup before anything reads it, and
/// the alternative is threading a store through every constructor between the program's entry point and the
/// two places that actually encrypt something.
/// </para>
/// </summary>
public static class Secrets
{
	/// <summary>
	/// What protects a secret before it is written. Defaults to the store that does not, which keeps the
	/// type usable in tests and on a platform nothing has been written for yet — see
	/// <see cref="PlainTextSecretStore"/>, which says plainly that it is not security.
	/// </summary>
	public static ISecretStore Current { get; set; } = new PlainTextSecretStore();

	/// <summary>Protects a value with whatever store is in use.</summary>
	public static string Protect(string plainText) => Current.Protect(plainText);

	/// <summary>Reads one back.</summary>
	public static string Unprotect(string cipherText) => Current.Unprotect(cipherText);
}
