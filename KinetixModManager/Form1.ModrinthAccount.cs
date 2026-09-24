using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// The optional Modrinth key: checking it when it is saved, listing what the user follows on the search tab, and
/// following or unfollowing a result. See <see cref="ModrinthAccount"/> for why it is a key and not a sign-in.
/// </summary>
public partial class Form1
{
	/// <summary>Who the saved key belongs to, once it has been checked this session.</summary>
	private ModrinthUser? _modrinthUser;

	/// <summary>The slugs of what the user follows, once fetched this session. Kept current as they follow things.</summary>
	private HashSet<string>? _modrinthFollowed;

	private string ModrinthToken => _settings.ModSourceApiKey(ModSources.Modrinth);

	/// <summary>True when the search tab is set to what the user follows on Modrinth.</summary>
	private bool SearchingFollowed =>
		GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.Minecraft) && cmbDiscoveryContent?.SelectedIndex == 2;

	/// <summary>
	/// Checks the saved key with Modrinth and says whose it is — straight after it is saved, so a mistyped or
	/// expired key is found out then and not the next time something quietly lists nothing.
	/// </summary>
	private async Task CheckModrinthKeyAsync()
	{
		_modrinthUser = null;
		_modrinthFollowed = null;
		if (ModrinthToken.Length == 0) return;

		try
		{
			ModrinthUser? user = await ModrinthAccount.GetUserAsync(ModrinthToken);
			if (user is null)
			{
				_soundEngine.Play("error");
				Speak(Loc.T("mc.follow.keyRefused"));
				return;
			}

			_modrinthUser = user;
			_soundEngine.Play("connect");
			Speak(Loc.T("mc.follow.keyAccepted", user.Username));
		}
		catch (Exception ex)
		{
			LogFailure("Modrinth", "Could not check the Modrinth key", ex);
			Speak(Loc.T("mc.follow.keyUnchecked"));
		}
	}

	/// <summary>The user the key belongs to, asking Modrinth the first time. <c>null</c>, having said why, when there is none.</summary>
	private async Task<ModrinthUser?> ModrinthUserAsync()
	{
		if (_modrinthUser != null) return _modrinthUser;

		if (ModrinthToken.Length == 0)
		{
			Speak(Loc.T("mc.follow.noKey"));
			return null;
		}

		await CheckModrinthKeyAsync();
		return _modrinthUser;
	}

	/// <summary>What the user follows, for the search tab. Empty, having said why, when it cannot be had.</summary>
	private async Task<(List<GameMod> Results, int Total)> ListFollowedAsync()
	{
		if (await ModrinthUserAsync() is not { } user) return (new List<GameMod>(), 0);

		try
		{
			List<GameMod> followed = await ModrinthAccount.GetFollowedAsync(ModrinthToken, user.Id);
			_modrinthFollowed = new HashSet<string>(followed.Select(f => f.ModrinthId ?? ""), StringComparer.OrdinalIgnoreCase);
			return (followed, followed.Count);
		}
		catch (Exception ex)
		{
			LogFailure("Modrinth", "Could not list what the user follows", ex);
			Speak(Loc.T("mc.follow.listFailed"));
			return (new List<GameMod>(), 0);
		}
	}

	/// <summary>
	/// The follow or unfollow action for a search result, or <c>null</c> when there is no key — an action that
	/// could only answer "you need a key" is not worth offering on every result.
	/// </summary>
	private async Task<(string Label, Func<Task> Run)?> FollowActionForAsync(GameMod result)
	{
		if (ModrinthToken.Length == 0 || string.IsNullOrEmpty(result.ModrinthId)) return null;

		if (_modrinthFollowed is null && await ModrinthUserAsync() is { } user)
		{
			try
			{
				_modrinthFollowed = new HashSet<string>(
					(await ModrinthAccount.GetFollowedAsync(ModrinthToken, user.Id)).Select(f => f.ModrinthId ?? ""),
					StringComparer.OrdinalIgnoreCase);
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Modrinth", "listing what the user follows", ex); }
		}

		bool following = _modrinthFollowed?.Contains(result.ModrinthId!) == true;
		return (Loc.T(following ? "mc.follow.unfollow" : "mc.follow.follow"), () => SetFollowingAsync(result, !following));
	}

	private async Task SetFollowingAsync(GameMod result, bool follow)
	{
		try
		{
			if (!await ModrinthAccount.SetFollowingAsync(ModrinthToken, result.ModrinthId!, follow))
			{
				Speak(Loc.T("mc.follow.refused"));
				return;
			}

			if (follow) _modrinthFollowed?.Add(result.ModrinthId!);
			else _modrinthFollowed?.Remove(result.ModrinthId!);
			Speak(Loc.T(follow ? "mc.follow.followed" : "mc.follow.unfollowed", result.Name));
		}
		catch (Exception ex)
		{
			LogFailure("Modrinth", $"Could not change whether {result.Name} is followed", ex);
			Speak(Loc.T("mc.follow.failed", result.Name));
		}
	}
}
