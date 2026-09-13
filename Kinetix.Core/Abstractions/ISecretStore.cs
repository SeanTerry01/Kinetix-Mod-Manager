using System;

namespace KinetixModManager;

/// <summary>
/// Keeping a secret — the Nexus API key, and any AI provider keys — out of a plain-text file.
///
/// <para>
/// The Windows implementation is DPAPI, which encrypts against the logged-in user account so that the
/// stored value is useless on another machine or to another user on the same one. It has no equivalent
/// anywhere else: <c>ProtectedData</c> is Windows-only and throws off it, so this is one of the small number
/// of places that genuinely blocks the program from running elsewhere until something implements it. On
/// Linux the counterpart is the Secret Service — libsecret, which is what GNOME Keyring and KWallet both
/// answer to.
/// </para>
///
/// <para>
/// Both methods take and return strings rather than bytes because what is being protected is always a key
/// the user pasted in, and what is stored is always a line in a JSON file. Making that explicit keeps the
/// encoding decision inside the implementation, where the platform's own idea of it belongs.
/// </para>
/// </summary>
public interface ISecretStore
{
	/// <summary>
	/// Turns <paramref name="plainText"/> into something safe to write to disk.
	///
	/// Must not throw. An implementation that cannot protect the value should return it unchanged rather
	/// than fail: a manager that will not start because a keyring is unavailable is worse than one that
	/// stores a key the user can revoke, and the user chose to save a key in the first place. That is the
	/// existing Windows behaviour and it is deliberate — see the comment on the DPAPI implementation.
	/// </summary>
	string Protect(string plainText);

	/// <summary>
	/// Reads back what <see cref="Protect"/> wrote.
	///
	/// Must not throw, and must cope with being handed a value that was never protected at all — that is
	/// exactly what a settings file written before encryption existed looks like, and returning it unchanged
	/// is what makes those upgrade without the user noticing.
	/// </summary>
	string Unprotect(string cipherText);
}
