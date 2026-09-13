using System;
using System.Security.Cryptography;
using System.Text;

namespace KinetixModManager;

/// <summary>
/// The Windows <see cref="ISecretStore"/>: DPAPI, encrypting against the logged-in user account so a stolen
/// settings file is useless on another machine, or to another user on the same one.
///
/// This is one of the genuinely Windows-only pieces of the program — <see cref="ProtectedData"/> does not
/// exist off Windows and throws rather than degrading — which is precisely why it now sits behind an
/// interface instead of inside <c>AppSettings</c>.
/// </summary>
internal sealed class DpapiSecretStore : ISecretStore
{
	public string Protect(string plainText)
	{
		if (string.IsNullOrEmpty(plainText)) return "";

		try
		{
			byte[] data = Encoding.UTF8.GetBytes(plainText);
			byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
			return Convert.ToBase64String(encrypted);
		}
		catch
		{
			// If DPAPI is unavailable for any reason, fall back to plain text so the app remains functional
			// (e.g., in a sandbox without a user profile). Refusing to start over a key the user chose to
			// save, and can revoke, would be the worse trade.
			return plainText;
		}
	}

	public string Unprotect(string cipherText)
	{
		if (string.IsNullOrEmpty(cipherText)) return "";

		try
		{
			byte[] data = Convert.FromBase64String(cipherText);
			byte[] decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(decrypted);
		}
		catch
		{
			// Fallback: treat as plain text. This is also what a settings file written before encryption
			// existed looks like, which is how those upgrade without the user noticing.
			return cipherText;
		}
	}
}
