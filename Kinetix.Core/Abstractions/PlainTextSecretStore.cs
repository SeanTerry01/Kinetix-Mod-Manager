using System;

namespace KinetixModManager;

/// <summary>
/// The <see cref="ISecretStore"/> that does not actually protect anything: it hands back what it was given.
///
/// It exists so that <c>AppSettings</c> has a working default before startup has chosen a platform
/// implementation, and so the tests can read and write settings without a keyring. It is deliberately not
/// the thing any shipped build uses — <c>Program.cs</c> replaces it with DPAPI on the first line that can.
///
/// The name says what it does on purpose. A class called something reassuring, that stores a key in the
/// clear, is how a build ships doing exactly that without anyone noticing.
/// </summary>
public sealed class PlainTextSecretStore : ISecretStore
{
	public string Protect(string plainText) => plainText ?? "";

	public string Unprotect(string cipherText) => cipherText ?? "";
}
