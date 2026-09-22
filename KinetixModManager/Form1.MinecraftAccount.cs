using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Signing in to Minecraft, and choosing whether the game starts online or offline.
///
/// <para>
/// Offline is the everyday mode and stays the default: singleplayer needs no account at all, and the manager
/// has started the game that way since Minecraft support shipped. Online is a deliberate switch, for servers,
/// Realms and skins, and it can be thrown either way at any time. Both are first-class — this is not a
/// fallback arrangement.
/// </para>
///
/// <para>
/// The sign-in is Microsoft's device-code flow, chosen because it is the accessible one. Nothing is embedded
/// and no password passes through the manager: it is handed a short code, says it, and the player types it
/// into their own browser where their screen reader already works. The code is spoken one word per character
/// — <see cref="PhoneticSpelling"/> — because that is the only way it can be heard without ambiguity.
/// </para>
///
/// <para>
/// ⚠️ Whichever mode is used, the player is the SAME character: both carry the real uuid. See
/// <see cref="MinecraftAccounts"/>. A mode switch that changed who you were in your own world would be the
/// exact failure this game's support exists to prevent.
/// </para>
/// </summary>
public partial class Form1
{
	/// <summary>What the account view was closed to go and do.</summary>
	private enum MinecraftAccountAction { Close, SignIn, SignOut }

	/// <summary>
	/// The Minecraft Account view: who is signed in, which mode launches use, and the ways to change either.
	///
	/// A loop, because signing in or out changes what the view has to say. Rather than rebuild the screen
	/// underneath the player — which moves focus and leaves a reader describing controls that have just been
	/// replaced — the view closes, the job is done, and the view opens again saying the new state.
	/// </summary>
	private async Task ShowMinecraftAccountAsync()
	{
		while (true)
		{
			MinecraftAccountAction action = MinecraftAccountAction.Close;

			ShowInlineView(Loc.T("mcAccount.viewTitle"), (container, closeView) =>
			{
				TableLayoutPanel layout = new TableLayoutPanel
				{
					Dock = DockStyle.Fill,
					Padding = new Padding(15),
					ColumnCount = 1,
					RowCount = 2
				};
				layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
				layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));

				TextBox tbState = new TextBox
				{
					Dock = DockStyle.Fill,
					Multiline = true,
					ReadOnly = true,
					ScrollBars = ScrollBars.Vertical,
					Font = new Font("Segoe UI", 12f),
					Text = MinecraftAccountSummary(),
					// Named for what it holds. An unnamed read-only box borrows a name from whatever is behind
					// the view — see Form1.Donate, where one announced itself as "Search".
					AccessibleName = Loc.T("mcAccount.stateName"),
					TabStop = true
				};
				tbState.GotFocus += delegate { tbState.Select(0, 0); };

				FlowLayoutPanel buttons = new FlowLayoutPanel
				{
					Dock = DockStyle.Fill,
					FlowDirection = FlowDirection.LeftToRight,
					WrapContents = false,
					AccessibleName = " "
				};

				if (_settings.MinecraftAccount is null)
				{
					buttons.Controls.Add(MakeViewButton(Loc.T("mcAccount.signIn"), Loc.T("mcAccount.signInName"),
						delegate { action = MinecraftAccountAction.SignIn; closeView(); }));
				}
				else
				{
					// The button says what pressing it does, not which mode is on — the state is in the text
					// above, and a button named for a state leaves the player working out which way it toggles.
					bool online = _settings.MinecraftPlayOnline;
					bool connecting = false;
					Button mode = MakeViewButton(
						Loc.T(online ? "mcAccount.switchToOffline" : "mcAccount.switchToOnline"),
						Loc.T(online ? "mcAccount.switchToOfflineName" : "mcAccount.switchToOnlineName"),
						onClick: null);
					mode.Click += async delegate
					{
						// A second press while Microsoft is still being asked would switch straight back.
						if (connecting) return;

						bool nowOnline = !_settings.MinecraftPlayOnline;
						_settings.MinecraftPlayOnline = nowOnline;
						_settings.Save();
						_minecraftOnlineWorking = null;

						// Relabelled in place rather than closing the view: this is a switch the player may want
						// to flip and hear, and the announcement is the confirmation either way.
						mode.Text = Loc.T(nowOnline ? "mcAccount.switchToOffline" : "mcAccount.switchToOnline");
						mode.AccessibleName = Loc.T(nowOnline ? "mcAccount.switchToOfflineName" : "mcAccount.switchToOnlineName");

						if (nowOnline)
						{
							// Switching on is only worth announcing once it is known to work — Sean heard "online"
							// and then nothing to say anything had connected. The check says the mode, the account
							// and the cue together, or says why not.
							connecting = true;
							try { await ConnectMinecraftAccountAsync(sayConnecting: false); }
							finally { connecting = false; }
						}
						else
						{
							_soundEngine.Play("disconnect");
							ResetStatus();
							Speak(Loc.T("mcAccount.nowOffline"));
						}

						// The player may have closed the view while Microsoft was answering.
						if (!tbState.IsDisposed)
						{
							tbState.Text = MinecraftAccountSummary();
							tbState.Select(0, 0);
						}
					};
					buttons.Controls.Add(mode);

					buttons.Controls.Add(MakeViewButton(Loc.T("mcAccount.signOut"), Loc.T("mcAccount.signOutName"),
						delegate { action = MinecraftAccountAction.SignOut; closeView(); }));
				}

				Button close = MakeViewButton(Loc.T("common.close"), Loc.T("mcAccount.closeName"),
					delegate { closeView(); });
				buttons.Controls.Add(close);

				layout.Controls.Add(tbState, 0, 0);
				layout.Controls.Add(buttons, 0, 1);
				container.Controls.Add(layout);

				return tbState;
			});

			switch (action)
			{
				case MinecraftAccountAction.SignIn:
					await SignInToMinecraftAsync();
					continue;
				case MinecraftAccountAction.SignOut:
					SignOutOfMinecraft();
					continue;
				default:
					return;
			}
		}
	}

	/// <summary>
	/// What the view reads out: which mode, who is signed in, and what a launch will therefore do.
	///
	/// ⚠️ The mode comes FIRST, on a line of its own. A screen reader landing in this box reads the line the caret
	/// is on and no more — the mode used to be in the third paragraph, so arriving here said only the name.
	/// </summary>
	private string MinecraftAccountSummary()
	{
		string text = Loc.T("mcAccount.stateLine", MinecraftAccountState())
			+ "\n\n"
			+ (_settings.MinecraftAccount is null
				? Loc.T("mcAccount.summarySignedOut")
				: Loc.T(_settings.MinecraftPlayOnline ? "mcAccount.summaryOnline" : "mcAccount.summaryOffline"));

		return text.Replace("\n", Environment.NewLine);
	}

	/// <summary>
	/// The mode, the account and whether the online sign-in is working, in a few words — "online play, connected
	/// as Sean". Shared by the menu item and the first line of the view, so the two can never disagree.
	/// </summary>
	private string MinecraftAccountState()
	{
		if (_settings.MinecraftAccount is not { } account) return Loc.T("mcAccount.stateSignedOut");
		if (!_settings.MinecraftPlayOnline) return Loc.T("mcAccount.stateOffline", account.Username);

		return _minecraftOnlineWorking switch
		{
			true => Loc.T("mcAccount.stateConnected", account.Username),
			false => Loc.T("mcAccount.stateNotWorking", account.Username),
			null => Loc.T("mcAccount.stateOnline", account.Username)
		};
	}

	/// <summary>The Game and Maintenance menu's entry, which says the state before it is even opened.</summary>
	private string MinecraftAccountMenuText() => Loc.T("menu.minecraftAccount", MinecraftAccountState());

	// -------------------------------------------------------------------------
	// Being connected
	// -------------------------------------------------------------------------

	/// <summary>
	/// Whether this run has proved the online sign-in works: <c>null</c> until it has been tried (or since the
	/// mode or account last changed), then what Microsoft said.
	/// </summary>
	private bool? _minecraftOnlineWorking;

	/// <summary>
	/// Minecraft's resting title: "Connected to Minecraft as Sean", as a Nexus game rests on "Connected as", and
	/// otherwise the mode — Sean found offline play resting on a bare "Ready", which says nothing about which
	/// mode F5 will use. Null for every other game.
	/// </summary>
	private string? MinecraftRestingStatus()
	{
		if (!GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.Minecraft)) return null;

		return _minecraftOnlineWorking == true && _settings.MinecraftPlayOnline && _settings.MinecraftAccount is { } account
			? Loc.T("mcAccount.connectedStatus", account.Username)
			: MinecraftAccountState();
	}

	/// <summary>
	/// Checks the online sign-in with Microsoft now, and says how it went — the connect cue and who you are, or
	/// the error cue and why not. Does nothing in offline play.
	///
	/// <para>
	/// Online play used to be invisible until F5: the manager opened, nothing sounded, nothing was said, and the
	/// session looked exactly like an offline one. This is the Nexus games' "Connecting… connected as" for
	/// Minecraft, whose account is Microsoft's.
	/// </para>
	///
	/// <para>
	/// A failure is spoken, never boxed. Nothing has to be decided yet — the launch still asks whether to start
	/// offline if it cannot sign in — and a box at startup would take the floor for a question nobody asked.
	/// </para>
	/// </summary>
	private async Task ConnectMinecraftAccountAsync(bool sayConnecting = true)
	{
		if (!_settings.MinecraftPlayOnline || _settings.MinecraftAccount is null) return;

		SetStatus(Loc.T("mcAccount.connecting"), speak: sayConnecting);
		try
		{
			(MinecraftIdentity identity, bool refreshed) = await MinecraftAccounts.ConnectAsync(_settings);

			// ⚠️ Microsoft rotates the refresh token: see ResolveMinecraftIdentityAsync.
			if (refreshed) _settings.Save();

			_minecraftOnlineWorking = true;
			_soundEngine.Play("connect");
			SetStatus(Loc.T("mcAccount.connectedStatus", identity.Username), speak: false);
			Speak(Loc.T("mcAccount.connected", identity.Username));
		}
		catch (MinecraftAuthException ex)
		{
			LogFailure("Minecraft", "Could not check the online sign-in", ex);

			_minecraftOnlineWorking = false;
			_soundEngine.Play("error");
			ResetStatus();
			Speak(Loc.T("mcAccount.notWorking", MinecraftAuthMessage(ex)));
		}
	}

	/// <summary>One button in a view's row. The same shape the other in-window views use.</summary>
	private static Button MakeViewButton(string text, string accessibleName, EventHandler? onClick)
	{
		Button b = new Button
		{
			Text = text,
			AutoSize = true,
			Height = 45,
			Margin = new Padding(6, 0, 6, 0),
			Font = new Font("Segoe UI", 12f, FontStyle.Bold),
			AccessibleName = accessibleName
		};
		if (onClick != null) b.Click += onClick;
		return b;
	}

	// -------------------------------------------------------------------------
	// Signing in
	// -------------------------------------------------------------------------

	/// <summary>
	/// The device-code sign-in, from asking Microsoft for a code to storing the session.
	///
	/// The view stays up while the player is off in their browser and closes itself the moment Microsoft says
	/// they are done — the waiting is the manager's job, not something to be checked on and pressed again.
	/// </summary>
	private async Task SignInToMinecraftAsync()
	{
		MinecraftDeviceCode code;
		try
		{
			SetStatus(Loc.T("mcAccount.asking"));
			code = await MinecraftAuth.RequestDeviceCodeAsync();
		}
		catch (MinecraftAuthException ex)
		{
			ReportMinecraftAuthFailure(ex);
			return;
		}
		finally
		{
			ResetStatus();
		}

		string spelled = PhoneticSpelling.Spell(code.UserCode);

		MinecraftSession? session = null;
		MinecraftAuthException? failure = null;
		using var cancelWaiting = new CancellationTokenSource();

		// Started before the view opens so not a second of the code's life is spent waiting for it to be built.
		Task<MinecraftSession> waiting = MinecraftAuth.WaitForSignInAsync(code, cancelWaiting.Token);

		ShowInlineView(Loc.T("mcAccount.signInTitle"), (container, closeView) =>
		{
			TableLayoutPanel layout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(15),
				ColumnCount = 1,
				RowCount = 2
			};
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));

			TextBox tbCode = new TextBox
			{
				Dock = DockStyle.Fill,
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Vertical,
				Font = new Font("Segoe UI", 12f),
				Text = Loc.T("mcAccount.codeBody", code.VerificationUri, code.UserCode, spelled)
					.Replace("\n", Environment.NewLine),
				AccessibleName = Loc.T("mcAccount.codeName"),
				TabStop = true
			};
			tbCode.GotFocus += delegate { tbCode.Select(0, 0); };

			FlowLayoutPanel buttons = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				AccessibleName = " "
			};

			// The code goes to the clipboard with the page, so the browser trip is paste-and-go rather than
			// typing eight characters heard once.
			buttons.Controls.Add(MakeViewButton(Loc.T("mcAccount.openPage"), Loc.T("mcAccount.openPageName"),
				delegate { OpenMicrosoftSignInPage(code); }));

			buttons.Controls.Add(MakeViewButton(Loc.T("mcAccount.copyCode"), Loc.T("mcAccount.copyCodeName"),
				delegate
				{
					if (TryCopy(code.UserCode)) Speak(Loc.T("mcAccount.copied", spelled));
					else Speak(Loc.T("mcAccount.copyFailed"));
				}));

			buttons.Controls.Add(MakeViewButton(Loc.T("mcAccount.sayAgain"), Loc.T("mcAccount.sayAgainName"),
				delegate { Speak(Loc.T("mcAccount.codeSpoken", spelled), interrupt: true); }));

			buttons.Controls.Add(MakeViewButton(Loc.T("common.cancel"), Loc.T("mcAccount.cancelName"),
				delegate { closeView(); }));

			layout.Controls.Add(tbCode, 0, 0);
			layout.Controls.Add(buttons, 0, 1);
			container.Controls.Add(layout);

			// The view closes itself when Microsoft answers. The continuation runs on the UI thread, and the
			// overlay's loop pumps messages, so closing from here is no different from a button press.
			waiting.ContinueWith(finished =>
			{
				if (finished.IsCompletedSuccessfully) session = finished.Result;
				else failure = finished.Exception?.GetBaseException() as MinecraftAuthException
					?? new MinecraftAuthException(MinecraftAuthFailure.Unexpected,
						finished.Exception?.GetBaseException().Message ?? "The sign-in ended unexpectedly.");

				closeView();
			}, TaskScheduler.FromCurrentSynchronizationContext());

			return tbCode;
		}, hint: Loc.T("mcAccount.codeSpoken", spelled));

		// Escape closes the view too, and a sign-in nobody is waiting for should not carry on in the background.
		cancelWaiting.Cancel();

		if (session is not null) { CompleteMinecraftSignIn(session); return; }
		if (failure is not null && failure.Failure != MinecraftAuthFailure.Cancelled)
		{
			ReportMinecraftAuthFailure(failure);
			return;
		}

		// Everything else is the player stopping: the Cancel button, Escape, or the cancellation those cause
		// arriving back as an exception. Saying so matters because the view closes by itself on success, and
		// silence here would be indistinguishable from having succeeded.
		Speak(Loc.T("mcAccount.cancelled"));
	}

	/// <summary>Stores the session, says who it belongs to, and offers to make online play the default.</summary>
	private void CompleteMinecraftSignIn(MinecraftSession session)
	{
		MinecraftAccounts.Remember(_settings, session);
		_settings.Save();
		_minecraftOnlineWorking = null;

		// A sign-in that finished IS a connection to Microsoft, whichever mode is chosen next. The cue is a sound,
		// not speech, so the box below cannot flush it.
		_soundEngine.Play("connect");

		// ⚠️ Worlds keep a character per uuid. Signing in as someone other than the account the launcher has been
		// playing as is legitimate, but it means different characters in the same worlds — said plainly, here,
		// rather than discovered as an empty inventory later.
		string warning = MinecraftAccounts.DiffersFromLauncher(session, MinecraftRootFolder())
			? Environment.NewLine + Environment.NewLine + Loc.T("mcAccount.differentPlayer", session.Username)
			: "";

		// One question, and the box carries the news with it: a message box flushes the speech queue, so
		// speaking the result first would lose half of it to the box's own announcement.
		if (SpeakBox(Loc.T("mcAccount.signedInAsk", session.Username) + warning, Loc.T("mcAccount.signedInTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
		{
			_settings.MinecraftPlayOnline = true;
			_settings.Save();

			// Proved a moment ago, by the sign-in itself; asking Microsoft again would only repeat the cue.
			_minecraftOnlineWorking = true;
			ResetStatus();
		}
	}

	private void SignOutOfMinecraft()
	{
		string name = _settings.MinecraftAccount?.Username ?? "";
		if (name.Length == 0) return;

		if (SpeakBox(Loc.T("mcAccount.signOutConfirm", name), Loc.T("mcAccount.signOutTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
			return;

		MinecraftAccounts.Forget(_settings);
		_settings.Save();
		_minecraftOnlineWorking = null;
		_soundEngine.Play("disconnect");
		ResetStatus();
		Speak(Loc.T("mcAccount.signedOut", name));
	}

	/// <summary>
	/// Opens microsoft.com/link with the code already on the clipboard. When no browser can be started — under
	/// Wine there may be none registered — the address is copied instead, which is at least something to paste.
	/// </summary>
	private void OpenMicrosoftSignInPage(MinecraftDeviceCode code)
	{
		bool copied = TryCopy(code.UserCode);
		try
		{
			Process.Start(new ProcessStartInfo(code.VerificationUri) { UseShellExecute = true });
			Speak(Loc.T(copied ? "mcAccount.openedWithCode" : "mcAccount.opened"));
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", "Could not open the Microsoft sign-in page", ex);
			if (TryCopy(code.VerificationUri))
				SpeakBox(Loc.T("mcAccount.openFailedCopied", code.VerificationUri, code.UserCode), Loc.T("mcAccount.signInTitle"));
			else
				SpeakBox(Loc.T("mcAccount.openFailed", code.VerificationUri, code.UserCode), Loc.T("mcAccount.signInTitle"));
		}
	}

	// -------------------------------------------------------------------------
	// What a launch plays as
	// -------------------------------------------------------------------------

	/// <summary>
	/// Works out who to start the game as, refreshing the stored sign-in when online play is switched on.
	/// Returns <c>null</c> when the launch should not go ahead, having already said why.
	///
	/// <para>
	/// An online launch that cannot sign in does NOT quietly become an offline one. The player is told what
	/// went wrong and asked whether to start offline this time — offline is a perfectly good answer, but it is
	/// a different game session and it is theirs to choose. The mode is not changed by answering yes: the
	/// setting still says online, and the next launch tries again.
	/// </para>
	/// </summary>
	private async Task<MinecraftIdentity?> ResolveMinecraftIdentityAsync(string root)
	{
		if (_settings.MinecraftPlayOnline && _settings.MinecraftAccount is not null)
		{
			try
			{
				// Silent: nothing is spoken for a token that is still good, which is the usual case, and the
				// launch announcement a moment later says which mode this turned out to be.
				SetStatus(Loc.T("mcAccount.signingIn"), speak: false);

				(MinecraftIdentity identity, bool refreshed) = await MinecraftAccounts.OnlineAsync(_settings);

				// ⚠️ Saved because Microsoft has already retired the refresh token that was just used. Skipping
				// this leaves a stored sign-in that fails next time for no visible reason.
				if (refreshed)
				{
					_settings.Save();
					_minecraftOnlineWorking = true;
				}

				return identity;
			}
			catch (MinecraftAuthException ex)
			{
				LogFailure("Minecraft", "Could not start an online session", ex);
				_minecraftOnlineWorking = false;
				ResetStatus();

				if (SpeakBox(Loc.T("mc.launch.onlineFailed", MinecraftAuthMessage(ex)), Loc.T("mc.launch.onlineFailedTitle"),
						MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
					return null;
			}
			finally
			{
				ResetStatus();
			}
		}

		MinecraftIdentity? offline = MinecraftAccounts.Offline(_settings, root);
		if (offline is null)
		{
			Speak(Loc.T("mc.launch.noAccountSpeak"));
			SpeakBox(Loc.T("mc.launch.noAccountBox"), Loc.T("mc.launch.noAccountTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		return offline;
	}

	// -------------------------------------------------------------------------
	// Reporting
	// -------------------------------------------------------------------------

	/// <summary>
	/// Turns a failure into a sentence about what to do next. Each one is a different thing to do, which is why
	/// they are not one message with a code in it.
	/// </summary>
	private void ReportMinecraftAuthFailure(MinecraftAuthException ex)
	{
		// The exception's own message names the step and the status and never carries a credential — see
		// MinecraftAuth. It goes to the log, not to the player.
		LogFailure("Minecraft", "Sign-in failed", ex);

		SpeakBox(MinecraftAuthMessage(ex), Loc.T("mcAccount.failTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
	}

	/// <summary>The sentence a player is given for a failure — what happened and what to do about it.</summary>
	private static string MinecraftAuthMessage(MinecraftAuthException ex)
	{
		string key = ex.Failure switch
		{
			MinecraftAuthFailure.NetworkFailed => "mcAccount.failNetwork",
			MinecraftAuthFailure.Declined => "mcAccount.failDeclined",
			MinecraftAuthFailure.CodeExpired => "mcAccount.failExpired",
			MinecraftAuthFailure.SignInAgain => "mcAccount.failSignInAgain",
			MinecraftAuthFailure.NoXboxAccount => "mcAccount.failNoXbox",
			MinecraftAuthFailure.ChildAccount => "mcAccount.failChild",
			MinecraftAuthFailure.RegionBlocked => "mcAccount.failRegion",
			MinecraftAuthFailure.NeedsAgeVerification => "mcAccount.failAgeCheck",
			MinecraftAuthFailure.NoJavaProfile => "mcAccount.failNoJava",
			MinecraftAuthFailure.AppNotAllowed => "mcAccount.failAppBlocked",
			_ => "mcAccount.failUnexpected"
		};

		return Loc.T(key);
	}
}
