using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// The Linux <see cref="ISecretStore"/>: the Secret Service, which is what GNOME Keyring and KWallet both
/// answer to, reached through libsecret.
///
/// <para>
/// It does not store the user's keys in the keyring. It keeps <em>one</em> key there — a random 256-bit one,
/// made the first time it is needed — and encrypts everything else with it. That is deliberate, and it is
/// what makes this a drop-in for DPAPI rather than a different shape wearing the same interface:
/// <see cref="ISecretStore"/> is a transform, not a store. <c>Protect</c> is handed a string and must return
/// something to write into <c>settings.json</c>; it is never told what the string is <em>for</em>. A keyring
/// used directly would need a name per secret, and with no name to use, every save would leave another
/// orphaned entry in the user's keyring with nothing ever cleaning them up.
/// </para>
///
/// <para>
/// So the keyring holds the one thing that genuinely belongs in it — a secret that must not be on disk — and
/// the settings file holds self-contained blobs, exactly as it does on Windows. Move the settings file to
/// another machine and it is useless there, which is the property DPAPI provides and the reason any of this
/// exists.
/// </para>
///
/// <para>
/// AES-GCM rather than CBC: it authenticates as well as encrypts, so a blob that has been edited by hand
/// fails to decrypt rather than decrypting to rubbish that then gets sent to Nexus as an API key.
/// </para>
/// </summary>
public sealed class LibSecretStore : ISecretStore
{
	private const string Library = "libsecret-1.so.0";

	/// <summary>
	/// How long to wait for the keyring before giving up on it for this session.
	///
	/// The Secret Service is D-Bus, and a locked keyring answers by putting a password prompt on screen. On
	/// a desktop that is fine and expected. On a machine with no session bus at all — a terminal over SSH, a
	/// container, a CI runner — the call can simply never come back, and a mod manager that hangs on startup
	/// because of a keyring is worse than one that stores a key the user can revoke. That is the same trade
	/// the Windows implementation makes for the same reason.
	/// </summary>
	private static readonly TimeSpan KeyringTimeout = TimeSpan.FromSeconds(5);

	/// <summary>Marks the blobs this class wrote, so anything else is left alone. See <see cref="Unprotect"/>.</summary>
	private const string Prefix = "secretservice:v1:";

	private const int NonceBytes = 12;   // AES-GCM's own size; not a choice
	private const int TagBytes = 16;

	private readonly Lazy<byte[]?> _key;

	public LibSecretStore() => _key = new Lazy<byte[]?>(LoadOrCreateKey, LazyThreadSafetyMode.ExecutionAndPublication);

	public string Protect(string plainText)
	{
		if (string.IsNullOrEmpty(plainText)) return "";

		try
		{
			byte[]? key = _key.Value;
			if (key == null) return plainText;   // no keyring; see the contract on ISecretStore

			byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
			byte[] plain = Encoding.UTF8.GetBytes(plainText);
			byte[] cipher = new byte[plain.Length];
			byte[] tag = new byte[TagBytes];

			using (var aes = new AesGcm(key, TagBytes))
				aes.Encrypt(nonce, plain, cipher, tag);

			byte[] packed = new byte[NonceBytes + TagBytes + cipher.Length];
			Buffer.BlockCopy(nonce, 0, packed, 0, NonceBytes);
			Buffer.BlockCopy(tag, 0, packed, NonceBytes, TagBytes);
			Buffer.BlockCopy(cipher, 0, packed, NonceBytes + TagBytes, cipher.Length);

			return Prefix + Convert.ToBase64String(packed);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Secrets", "protecting a value with the keyring", ex);
			return plainText;
		}
	}

	public string Unprotect(string cipherText)
	{
		if (string.IsNullOrEmpty(cipherText)) return "";

		// Anything this class did not write is handed back untouched. That covers a settings file written
		// before encryption existed, one carried over from Windows, and a key the user pasted straight into
		// the JSON — all of which must keep working rather than being read as corrupt.
		if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal)) return cipherText;

		try
		{
			byte[]? key = _key.Value;
			if (key == null) return cipherText;

			byte[] packed = Convert.FromBase64String(cipherText.Substring(Prefix.Length));
			if (packed.Length < NonceBytes + TagBytes) return cipherText;

			byte[] nonce = packed[..NonceBytes];
			byte[] tag = packed[NonceBytes..(NonceBytes + TagBytes)];
			byte[] cipher = packed[(NonceBytes + TagBytes)..];
			byte[] plain = new byte[cipher.Length];

			using (var aes = new AesGcm(key, TagBytes))
				aes.Decrypt(nonce, cipher, tag, plain);

			return Encoding.UTF8.GetString(plain);
		}
		catch (Exception ex)
		{
			// Includes the authentication failing, which means the blob was edited or the keyring now holds
			// a different master key. Either way there is nothing to hand back but what we were given.
			DiagnosticLog.WriteException("Secrets", "reading a value back from the keyring", ex);
			return cipherText;
		}
	}

	/// <summary>True when the keyring answered and a master key is in hand.</summary>
	public bool IsAvailable => _key.Value != null;

	// -------------------------------------------------------------------------
	// The master key
	// -------------------------------------------------------------------------

	private byte[]? LoadOrCreateKey()
	{
		try
		{
			// Run the D-Bus work off the calling thread so a keyring that never answers costs a timeout
			// rather than the program's startup. See KeyringTimeout.
			Task<byte[]?> work = Task.Run(FetchOrStoreKey);
			return work.Wait(KeyringTimeout) ? work.Result : null;
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Secrets", "asking the keyring for the manager's master key", ex);
			return null;
		}
	}

	private static byte[]? FetchOrStoreKey()
	{
		IntPtr schema = IntPtr.Zero;
		try
		{
			schema = BuildSchema();

			string? existing = Lookup(schema);
			if (!string.IsNullOrEmpty(existing))
			{
				try { return Convert.FromBase64String(existing!); }
				catch (FormatException)
				{
					// Something else wrote under our name. Replacing it would throw away whatever that was,
					// so back off to plain text instead and say so in the log.
					DiagnosticLog.Write("Secrets", "the keyring entry for the master key is not one of ours");
					return null;
				}
			}

			byte[] fresh = RandomNumberGenerator.GetBytes(32);
			return Store(schema, Convert.ToBase64String(fresh)) ? fresh : null;
		}
		catch (DllNotFoundException)
		{
			// No libsecret on this machine. Not an error worth a stack trace: it is a normal state for a
			// minimal install, and the contract's answer is to carry on unprotected.
			DiagnosticLog.Write("Secrets", "libsecret is not installed, so secrets are stored unprotected");
			return null;
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Secrets", "reading or creating the master key", ex);
			return null;
		}
		finally
		{
			if (schema != IntPtr.Zero) FreeSchema(schema);
		}
	}

	// -------------------------------------------------------------------------
	// libsecret
	// -------------------------------------------------------------------------

	private const string SchemaName = "com.audiventuregames.KinetixModManager";
	private const string AttributeName = "purpose";
	private const string AttributeValue = "settings-master-key";

	[DllImport(Library, EntryPoint = "secret_password_store_sync", CallingConvention = CallingConvention.Cdecl,
		CharSet = CharSet.Ansi)]
	private static extern bool StoreSync(
		IntPtr schema, IntPtr collection, string label, string password,
		IntPtr cancellable, out IntPtr error, string attribute, string value, IntPtr terminator);

	[DllImport(Library, EntryPoint = "secret_password_lookup_sync", CallingConvention = CallingConvention.Cdecl,
		CharSet = CharSet.Ansi)]
	private static extern IntPtr LookupSync(
		IntPtr schema, IntPtr cancellable, out IntPtr error, string attribute, string value, IntPtr terminator);

	[DllImport(Library, EntryPoint = "secret_password_free", CallingConvention = CallingConvention.Cdecl)]
	private static extern void PasswordFree(IntPtr password);

	private static string? Lookup(IntPtr schema)
	{
		IntPtr result = LookupSync(schema, IntPtr.Zero, out IntPtr error, AttributeName, AttributeValue, IntPtr.Zero);
		if (error != IntPtr.Zero) return null;
		if (result == IntPtr.Zero) return null;

		try { return Marshal.PtrToStringAnsi(result); }
		finally { PasswordFree(result); }
	}

	private static bool Store(IntPtr schema, string value)
	{
		// A null collection means the user's default keyring, which is the one that unlocks at login.
		bool stored = StoreSync(schema, IntPtr.Zero, "Kinetix Mod Manager", value,
			IntPtr.Zero, out IntPtr error, AttributeName, AttributeValue, IntPtr.Zero);

		return stored && error == IntPtr.Zero;
	}

	/// <summary>
	/// Builds a <c>SecretSchema</c> by hand.
	///
	/// libsecret's schema is a plain C struct — a name, flags, and a fixed array of 32 attribute slots
	/// terminated by a null name — and every function here wants a pointer to one. Writing the fields into
	/// zeroed memory is less code than declaring the struct in C# and easier to be sure of: only three
	/// fields are non-zero, and the terminator is the zero that is already there.
	/// </summary>
	private static IntPtr BuildSchema()
	{
		// Comfortably larger than the struct (a name, flags, 32 × 16-byte attributes, and eight reserved
		// slots), and zeroed, which is what makes the unwritten attributes act as the terminator.
		const int size = 1024;
		IntPtr schema = Marshal.AllocHGlobal(size);
		for (int i = 0; i < size; i++) Marshal.WriteByte(schema, i, 0);

		Marshal.WriteIntPtr(schema, 0, Marshal.StringToHGlobalAnsi(SchemaName));
		Marshal.WriteInt32(schema, IntPtr.Size, 0);                        // SECRET_SCHEMA_NONE

		// attributes[0]: { name, SECRET_SCHEMA_ATTRIBUTE_STRING }. The array starts after the name pointer
		// and the flags, both padded to pointer alignment.
		int attributes = IntPtr.Size * 2;
		Marshal.WriteIntPtr(schema, attributes, Marshal.StringToHGlobalAnsi(AttributeName));
		Marshal.WriteInt32(schema, attributes + IntPtr.Size, 0);

		return schema;
	}

	private static void FreeSchema(IntPtr schema)
	{
		try
		{
			Marshal.FreeHGlobal(Marshal.ReadIntPtr(schema, 0));
			Marshal.FreeHGlobal(Marshal.ReadIntPtr(schema, IntPtr.Size * 2));
			Marshal.FreeHGlobal(schema);
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Secrets", "releasing the keyring schema", ex); }
	}
}
