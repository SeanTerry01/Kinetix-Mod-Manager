using System;
using System.Threading;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// A signed-in Minecraft account as it sits in the settings file.
///
/// The name and uuid are public — they are on every server the player has joined — and are stored plainly so
/// the account can be named without decrypting anything. Both tokens are encrypted with <see cref="Secrets"/>
/// (DPAPI on Windows), so a copied settings file is useless on another account or machine.
/// </summary>
public sealed class MinecraftAccountRecord
{
	public string Username { get; set; } = "";

	/// <summary>Dashed uuid.</summary>
	public string Uuid { get; set; } = "";

	/// <summary>The Microsoft refresh token, encrypted. Replaced on every refresh — Microsoft rotates it.</summary>
	public string RefreshTokenEncrypted { get; set; } = "";

	/// <summary>
	/// The last Minecraft access token, encrypted, kept so that starting the game twice in an evening does not
	/// ask Microsoft twice.
	/// </summary>
	public string AccessTokenEncrypted { get; set; } = "";

	public DateTime AccessTokenExpiresUtc { get; set; }
}

/// <summary>
/// Who the game is told it is, for either mode, and keeping the stored sign-in current.
///
/// <para>
/// Both modes are first-class and the player moves between them at will (Prism Launcher's rule, decided
/// 2026-09-12). What differs is only whether a real session goes with the name: online carries a token, so
/// servers, Realms and skins work; offline carries none, so none of those do and singleplayer is untouched.
/// </para>
///
/// <para>
/// ⚠️ Both modes MUST give the same uuid, or switching modes walks the player into their own world as a
/// stranger. See <see cref="MinecraftIdentity.OfflineFromLauncher"/>. That is why an offline launch prefers
/// the signed-in account's uuid when there is one: it is the one online play will use.
/// </para>
/// </summary>
public static class MinecraftAccounts
{
	/// <summary>A cached access token with less than this left is refreshed rather than handed to the game.</summary>
	public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(30);

	/// <summary>Records a session, replacing whatever was stored. The caller saves the settings.</summary>
	public static void Remember(AppSettings settings, MinecraftSession session)
	{
		settings.MinecraftAccount = new MinecraftAccountRecord
		{
			Username = session.Username,
			Uuid = session.Uuid,
			RefreshTokenEncrypted = Secrets.Protect(session.RefreshToken),
			AccessTokenEncrypted = Secrets.Protect(session.AccessToken),
			AccessTokenExpiresUtc = session.AccessTokenExpiresUtc
		};
	}

	/// <summary>
	/// Signs out: the stored credentials are gone and online play is switched off with them, so the setting can
	/// never claim a mode there is no account for. The caller saves the settings.
	/// </summary>
	/// <remarks>
	/// Microsoft offers no revoke endpoint for a public client's refresh token; forgetting it is the whole of
	/// signing out. The player can also remove the app at account.live.com/consent/Manage.
	/// </remarks>
	public static void Forget(AppSettings settings)
	{
		settings.MinecraftAccount = null;
		settings.MinecraftPlayOnline = false;
	}

	/// <summary>
	/// An offline identity: the signed-in account's name and uuid if there is one, otherwise the official
	/// launcher's. <c>null</c> when neither exists.
	/// </summary>
	public static MinecraftIdentity? Offline(AppSettings settings, string root)
	{
		MinecraftAccountRecord? account = settings.MinecraftAccount;
		if (account is not null && account.Username.Length > 0 && account.Uuid.Length > 0)
		{
			return new MinecraftIdentity
			{
				Username = account.Username,
				Uuid = account.Uuid,
				AccessToken = "0",
				UserType = "legacy"
			};
		}

		return MinecraftIdentity.OfflineFromLauncher(root);
	}

	/// <summary>
	/// An online identity for the signed-in account, refreshing the token first when it is close to running
	/// out. Throws <see cref="MinecraftAuthException"/>; <see cref="MinecraftAuthFailure.SignInAgain"/> when there
	/// is no account or Microsoft has stopped accepting it.
	///
	/// <paramref name="settings"/> is updated in place when a refresh happens, and the caller must save it:
	/// Microsoft has already retired the refresh token that was used, so NOT saving the new one leaves a stored
	/// sign-in that will fail next time.
	/// </summary>
	public static async Task<(MinecraftIdentity Identity, bool Refreshed)> OnlineAsync(
		AppSettings settings, CancellationToken cancel = default)
	{
		MinecraftAccountRecord account = settings.MinecraftAccount
			?? throw new MinecraftAuthException(MinecraftAuthFailure.SignInAgain, "No Minecraft account is signed in.");

		string cached = account.AccessTokenEncrypted.Length > 0 ? Secrets.Unprotect(account.AccessTokenEncrypted) : "";
		if (cached.Length > 0 && account.AccessTokenExpiresUtc - MinecraftAuth.UtcNow() > RefreshMargin)
			return (Identity(account.Username, account.Uuid, cached), false);

		// Unprotect hands back its input unchanged when it cannot decrypt it — a settings file copied from another
		// Windows account, say. Microsoft then refuses that as a refresh token with invalid_grant, which
		// RefreshAsync reports as a sign-in to redo. That is the right answer, reached the long way round.
		string refresh = account.RefreshTokenEncrypted.Length > 0 ? Secrets.Unprotect(account.RefreshTokenEncrypted) : "";
		MinecraftSession session = await MinecraftAuth.RefreshAsync(refresh, cancel).ConfigureAwait(false);

		Remember(settings, session);
		return (Identity(session.Username, session.Uuid, session.AccessToken), true);
	}

	private static MinecraftIdentity Identity(string name, string uuid, string token) => new()
	{
		Username = name,
		Uuid = uuid,
		AccessToken = token,
		UserType = "msa"
	};

	/// <summary>
	/// True when the account just signed in to is a different player from the one the official launcher knows.
	/// Worlds keep a character per uuid, so the player needs telling: their existing characters stay with the
	/// other account.
	/// </summary>
	public static bool DiffersFromLauncher(MinecraftSession session, string root)
	{
		MinecraftIdentity? launcher = MinecraftIdentity.OfflineFromLauncher(root);
		return launcher is not null && !string.Equals(launcher.Uuid, session.Uuid, StringComparison.OrdinalIgnoreCase);
	}
}
