using System;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="LibSecretStore"/> — the Linux answer to DPAPI.
///
/// <para>
/// Written to pass whether or not the machine running them has a keyring, because both are real. A desktop
/// has gnome-keyring or KWallet; a CI runner, a container and a terminal over SSH have none, and
/// <see cref="ISecretStore"/> says in as many words that an implementation which cannot protect a value must
/// hand it back rather than fail. A manager that will not start because a keyring is unavailable is worse
/// than one that stores a key the user can revoke — so both outcomes are correct, and what is asserted is
/// that whichever happens is coherent.
/// </para>
/// </summary>
public class LinuxSecretStoreTests
{
	private static readonly LibSecretStore Store = new();

	[Fact]
	public void NothingIsNothingInBothDirections()
	{
		Assert.Equal("", Store.Protect(""));
		Assert.Equal("", Store.Unprotect(""));
	}

	[Fact]
	public void AValueThatWasNeverProtectedComesBackUntouched()
	{
		// The important compatibility case, and it must hold with or without a keyring: a settings file
		// written before encryption existed, one carried over from Windows, or a key the user pasted
		// straight into the JSON. All of those have to keep working rather than being read as corrupt.
		foreach (string plain in new[] { "abc123", "not base64 at all !!", "{\"looks\":\"like json\"}" })
			Assert.Equal(plain, Store.Unprotect(plain));
	}

	[Fact]
	public void AProtectedValueReadsBackAsItself()
	{
		const string secret = "nexus-0123456789abcdef";

		string stored = Store.Protect(secret);

		Assert.Equal(secret, Store.Unprotect(stored));
	}

	[Fact]
	public void WhetherItIsEncryptedFollowsWhetherThereIsAKeyring()
	{
		const string secret = "nexus-0123456789abcdef";

		string stored = Store.Protect(secret);

		if (Store.IsAvailable)
		{
			// The property DPAPI provides and the reason any of this exists: the settings file alone is not
			// enough to recover the key.
			Assert.NotEqual(secret, stored);
			Assert.DoesNotContain(secret, stored);
		}
		else
		{
			Assert.Equal(secret, stored);
		}
	}

	[Fact]
	public void TheSameSecretProtectedTwiceDoesNotLookTheSame()
	{
		if (!Store.IsAvailable) return;   // nothing to say when nothing is encrypted

		// A fresh nonce each time. Without it, two settings files would reveal that they hold the same key.
		Assert.NotEqual(Store.Protect("same"), Store.Protect("same"));
	}

	[Fact]
	public void ABlobThatHasBeenEditedIsRefusedRatherThanDecodedToRubbish()
	{
		if (!Store.IsAvailable) return;

		string stored = Store.Protect("nexus-0123456789abcdef");
		string tampered = stored[..^6] + "AAAAA=";

		// AES-GCM authenticates as well as encrypts, so this fails to decrypt rather than producing
		// plausible-looking nonsense that would then be sent to Nexus as an API key.
		Assert.Equal(tampered, Store.Unprotect(tampered));
	}

	[Fact]
	public void ASecondStoreReadsWhatTheFirstWrote()
	{
		if (!Store.IsAvailable) return;

		// The whole reason the keyring is involved: the master key outlives the process. If this fails, the
		// key is being held in memory and every restart would invalidate the user's stored credentials.
		string stored = Store.Protect("nexus-0123456789abcdef");

		Assert.Equal("nexus-0123456789abcdef", new LibSecretStore().Unprotect(stored));
	}

	[Fact]
	public void TheCoreKnowsWhichStoreIsInUse()
	{
		// The gate that had to move out of AppSettings: it is in the WinForms project, so the GTK head could
		// not reach the one switch deciding whether a key is encrypted before it is written.
		ISecretStore before = Secrets.Current;
		try
		{
			Secrets.Current = new LibSecretStore();
			Assert.Equal("abc", Secrets.Unprotect(Secrets.Protect("abc")));
		}
		finally { Secrets.Current = before; }
	}

	[Fact]
	public void TheDefaultStoreProtectsNothingAndSaysSo()
	{
		// Deliberate, and the reason a head that stores a key must assign a real one first.
		var plain = new PlainTextSecretStore();

		Assert.Equal("abc", plain.Protect("abc"));
	}
}
