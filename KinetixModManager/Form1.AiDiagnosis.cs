using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic;

namespace KinetixModManager;

/// <summary>
/// AI-assisted help: diagnose a log, explain a failed install or a report finding, or ask a free-form modding
/// question — then keep chatting about the answer. Opt-in; does nothing unless the user has enabled AI and
/// entered a provider API key on the Settings AI tab. Complements the offline, rule-based <see cref="LogAnalyzer"/>.
/// </summary>
public partial class Form1
{
	/// <summary>Diagnoses the current log with the configured AI provider (F9), then opens the chat window.</summary>
	private async void DiagnoseWithAi()
	{
		if (!EnsureAiReady()) return;
		string logText = GetLogTextForAi();
		if (string.IsNullOrWhiteSpace(logText))
		{
			Speak(Loc.T("ai.noLog"));
			return;
		}
		string system = Loc.T("ai.systemPrompt");
		string user = Loc.T("ai.userPrompt", GameDisplayName(), EnabledModList(), logText);
		await RunAiAndChat(Loc.T("ai.resultTitle"), system, user);
	}

	/// <summary>
	/// Asks the AI about an arbitrary topic (a failed install, a report finding, a typed question). Wraps the
	/// question with the game name and enabled mod list so the AI has context, then opens the chat window.
	/// </summary>
	private async void AskAiAbout(string title, string question)
	{
		if (!EnsureAiReady()) return;
		string system = Loc.T("ai.systemPromptGeneral");
		string user = Loc.T("ai.aboutPrompt", GameDisplayName(), EnabledModList(), question);
		await RunAiAndChat(title, system, user);
	}

	/// <summary>Prompts for a free-form question (Tools menu), then asks the AI.</summary>
	private void AskFreeformAi()
	{
		if (!EnsureAiReady()) return;
		string q = Interaction.InputBox(Loc.T("ai.askPrompt"), Loc.T("ai.askTitle"));
		if (string.IsNullOrWhiteSpace(q)) return;
		AskAiAbout(Loc.T("ai.askTitle"), q.Trim());
	}

	/// <summary>
	/// Reports an install failure and, when AI is configured, offers to ask the provider what went wrong. When AI
	/// isn't set up it just shows the failure as before.
	/// </summary>
	private void AiInstallFailure(string message, string modName)
	{
		if (_aiService.IsConfigured)
		{
			if (SpeakBox(message + "\n\n" + Loc.T("ai.offerExplain"), Loc.T("ai.title"),
					MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
				AskAiAbout(Loc.T("ai.title"), Loc.T("ai.installFailQuestion", modName, message));
		}
		else
		{
			SpeakBox(message);
		}
	}

	private bool EnsureAiReady()
	{
		if (_aiService.IsConfigured) return true;
		Speak(Loc.T("ai.notConfigured"));
		SpeakBox(Loc.T("ai.notConfiguredBox"), Loc.T("ai.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
		return false;
	}

	private string EnabledModList()
	{
		string mods = string.Join(", ", _allInstalledMods.Where(m => !m.IsGroup && m.IsEnabled).Select(m => m.Name));
		return string.IsNullOrEmpty(mods) ? "(none detected)" : mods;
	}

	/// <summary>Sends the first prompt, then opens the multi-turn chat window seeded with that exchange.</summary>
	private async Task RunAiAndChat(string title, string system, string firstUser)
	{
		Speak(Loc.T("ai.working"));
		SetStatus(Loc.T("ai.working"), speak: false);
		try
		{
			string answer = await _aiService.AskAsync(system, firstUser, maxTokens: 1200);
			ResetStatus();
			_soundEngine.Play("load_complete");
			var turns = new List<AiTurn> { new AiTurn(true, firstUser), new AiTurn(false, answer) };
			ShowAiConversation(title, system, turns);
		}
		catch (Exception ex)
		{
			ResetStatus();
			_soundEngine.Play("error");
			LogError("AI", "Request failed: " + ex.Message);
			SpeakBox(Loc.T("ai.failed", ex.Message), Loc.T("ai.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	/// <summary>
	/// Collects log text to diagnose from whichever log tab is active: a window of lines around the selected
	/// line, or the tail of the log when nothing is selected (errors usually cluster near the end). Capped in
	/// size to keep the request cheap and fast. Returns empty when no log tab is active or the log is empty.
	/// </summary>
	private string GetLogTextForAi()
	{
		ListBox? list = CurrentTab() switch
		{
			AppTab.SmapiLog => listLog,
			AppTab.GameLog => listGameLog,
			_ => null,
		};
		if (list == null || list.Items.Count == 0) return "";

		var lines = list.Items.Cast<object>().Select(o => o?.ToString() ?? "").ToList();
		int sel = list.SelectedIndex;
		const int window = 60;
		int start, count;
		if (sel >= 0)
		{
			start = Math.Max(0, sel - window / 2);
			count = Math.Min(lines.Count - start, window);
		}
		else
		{
			count = Math.Min(lines.Count, 200);
			start = lines.Count - count;
		}

		string joined = string.Join("\n", lines.GetRange(start, count));
		const int maxChars = 6000;
		if (joined.Length > maxChars) joined = joined.Substring(joined.Length - maxChars);
		return joined;
	}

	/// <summary>
	/// The multi-turn chat window: a scrollable read-only transcript, a follow-up box, and a Send button, so the
	/// user can keep asking about the AI's answer. <paramref name="turns"/> carries the full context (its first
	/// user turn — the seeding log/question — is kept for the AI but hidden from the transcript, since it can be
	/// large). Speaks each new answer as it arrives.
	/// </summary>
	private void ShowAiConversation(string title, string system, List<AiTurn> turns)
	{
		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(title, (container, closeView) =>
		{
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // transcript
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // follow-up box
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // buttons

		var transcript = new TextBox
		{
			Multiline = true,
			ReadOnly = true,
			WordWrap = true,
			ScrollBars = ScrollBars.Vertical,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = title,
		};
		// Build the initial visible transcript, hiding the very first user turn (the seeding context).
		var sb = new StringBuilder();
		for (int i = 0; i < turns.Count; i++)
		{
			if (i == 0 && turns[i].IsUser) continue;
			sb.AppendLine(turns[i].IsUser ? Loc.T("ai.chatYou") : Loc.T("ai.chatAi"));
			sb.AppendLine(turns[i].Text);
			sb.AppendLine();
		}
		transcript.Text = NormalizeNewlines(sb.ToString().TrimEnd());
		layout.Controls.Add(transcript, 0, 0);

		var input = new TextBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = Loc.T("ai.chatInput"),
		};
		layout.Controls.Add(input, 0, 1);

		var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
		var send = new Button { Text = Loc.T("ai.chatSend"), AutoSize = true };
		var close = new Button { Text = Loc.T("ai.chatClose"), AutoSize = true };
		buttons.Controls.Add(send);
		buttons.Controls.Add(close);
		layout.Controls.Add(buttons, 0, 2);
		container.Controls.Add(layout);

		async void SendFollowUp()
		{
			string q = input.Text.Trim();
			if (string.IsNullOrEmpty(q)) return;
			send.Enabled = input.Enabled = false;
			turns.Add(new AiTurn(true, q));
			transcript.AppendText(Environment.NewLine + Environment.NewLine + Loc.T("ai.chatYou") + Environment.NewLine + NormalizeNewlines(q));
			input.Clear();
			Speak(Loc.T("ai.working"));
			try
			{
				string answer = await _aiService.ChatAsync(system, turns, maxTokens: 1200);
				turns.Add(new AiTurn(false, answer));
				transcript.AppendText(Environment.NewLine + Environment.NewLine + Loc.T("ai.chatAi") + Environment.NewLine + NormalizeNewlines(answer));
				_soundEngine.Play("load_complete");
				SpeakLong(answer, interrupt: false);
			}
			catch (Exception ex)
			{
				turns.RemoveAt(turns.Count - 1); // drop the unanswered question so a retry starts clean
				_soundEngine.Play("error");
				SpeakBox(Loc.T("ai.failed", ex.Message), Loc.T("ai.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			finally
			{
				send.Enabled = input.Enabled = true;
				input.Focus();
			}
		}
		send.Click += delegate { SendFollowUp(); };
		close.Click += delegate { closeView(); };
		// Enter in the question box sends it. The form's AcceptButton did this before; a view deliberately
		// clears the window's default button, so the key is wired to the box it belongs to.
		input.KeyDown += (_, e) =>
		{
			if (e.KeyCode != Keys.Enter) return;
			e.Handled = e.SuppressKeyPress = true;
			SendFollowUp();
		};
		// Escape is handled by the view itself (see Form1.InlineView).

		transcript.SelectionStart = 0;
		transcript.SelectionLength = 0;
		string firstAnswer = turns.LastOrDefault(t => !t.IsUser)?.Text ?? "";
		SpeakLong(firstAnswer, interrupt: false);
		return transcript;
		});
	}
}
