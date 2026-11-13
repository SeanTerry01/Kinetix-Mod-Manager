namespace KinetixModManager;

/// <summary>
/// The accessible FOMOD option wizard. Presented to the user when an installing mod ships a
/// <c>fomod/ModuleConfig.xml</c>; returns the chosen <see cref="FomodSelection"/> (or <c>null</c> if
/// cancelled). Each option group is a <see cref="GroupBox"/> of native radio buttons / check boxes so
/// the screen reader announces selection state for free, with each option's description spoken on focus.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Entry point handed to <see cref="ModFileSystem.ExtractModAsync"/> as the FOMOD selector. Marshals
	/// to the UI thread and shows the modal wizard, returning the user's selection or <c>null</c> on cancel.
	/// </summary>
	private Task<FomodSelection?> ShowFomodWizardAsync(FomodConfig config)
	{
		if (InvokeRequired)
			return (Task<FomodSelection?>)Invoke(new Func<Task<FomodSelection?>>(() => ShowFomodWizardAsync(config)));
		return Task.FromResult(RunFomodWizard(config));
	}

	private FomodSelection? RunFomodWizard(FomodConfig config)
	{
		Func<string, FomodFileState> fileState = ModFileSystem.BuildFomodFileStateProvider(_allInstalledMods);

		// Selection state persists across Back/Next so the user's choices are not lost when navigating.
		// Seed it from the same auto-defaults the silent install would use.
		var selected = new Dictionary<FomodPlugin, bool>();
		FomodSelection defaults = FomodInstaller.ComputeDefaultSelection(config, fileState);
		foreach (FomodInstallStep step in config.InstallSteps)
			foreach (FomodGroup group in step.Groups)
				foreach (FomodPlugin plugin in group.Plugins)
					selected[plugin] = defaults.SelectedPlugins.Contains(plugin);

		// Accumulate flags from selected options through a given step index, honouring step visibility.
		Dictionary<string, string> BuildFlags(int throughIndex)
		{
			var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i <= throughIndex && i < config.InstallSteps.Count; i++)
			{
				FomodInstallStep step = config.InstallSteps[i];
				if (!FomodConditionEvaluator.Evaluate(step.Visible, flags, fileState)) continue;
				foreach (FomodGroup group in step.Groups)
					foreach (FomodPlugin plugin in group.Plugins)
						if (selected[plugin])
							foreach (FomodFlag flag in plugin.ConditionFlags)
								flags[flag.Name] = flag.Value;
			}
			return flags;
		}

		bool IsStepVisible(int index) =>
			FomodConditionEvaluator.Evaluate(config.InstallSteps[index].Visible, BuildFlags(index - 1), fileState);

		int NextVisible(int from)
		{
			for (int i = from + 1; i < config.InstallSteps.Count; i++)
				if (IsStepVisible(i)) return i;
			return -1;
		}
		int VisibleCount()
		{
			int n = 0;
			for (int i = 0; i < config.InstallSteps.Count; i++) if (IsStepVisible(i)) n++;
			return n;
		}
		int VisiblePosition(int index)
		{
			int n = 0;
			for (int i = 0; i <= index; i++) if (IsStepVisible(i)) n++;
			return n;
		}

		// The authoritative final selection: walk visible steps applying flags as we go, so options in
		// steps that ended up hidden are excluded and NotUsable options are never installed.
		FomodSelection BuildResult()
		{
			var result = new FomodSelection();
			foreach (FomodInstallStep step in config.InstallSteps)
			{
				if (!FomodConditionEvaluator.Evaluate(step.Visible, result.Flags, fileState)) continue;
				foreach (FomodGroup group in step.Groups)
					foreach (FomodPlugin plugin in group.Plugins)
					{
						FomodPluginType type = FomodConditionEvaluator.ResolveType(plugin, result.Flags, fileState);
						if (type != FomodPluginType.NotUsable && selected[plugin])
						{
							result.SelectedPlugins.Add(plugin);
							foreach (FomodFlag flag in plugin.ConditionFlags)
								result.Flags[flag.Name] = flag.Value;
						}
					}
			}
			return result;
		}

		int first = -1;
		for (int i = 0; i < config.InstallSteps.Count; i++)
			if (IsStepVisible(i)) { first = i; break; }

		// No interactive steps (e.g. required files only): install the computed defaults without a dialog.
		if (first == -1) return BuildResult();

		// --- Build the dialog shell ------------------------------------------------------------------
		FomodSelection? outcome = null;
		var history = new Stack<int>();
		int current = first;
		// See Render: the first step is drawn during build, so its announcement goes through the hint instead.
		bool firstRender = true;

		// Shown inside the main window rather than as one of its own — see Form1.InlineView. `outcome` stays null
		// unless the user reaches the end and confirms, so leaving any other way — Escape, Cancel — installs
		// nothing, exactly as a DialogResult other than OK did.
		ShowInlineView(Loc.T("fomod.wizardTitle", config.ModuleName), (container, closeView) =>
		{
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

		var header = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 13f, FontStyle.Bold), AutoEllipsis = true };
		var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
		var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };

		var btnCancel = new Button { Text = Loc.T("fomod.cancel"), AutoSize = true, Height = 36, Margin = new Padding(6) };
		var btnNext = new Button { AutoSize = true, Height = 36, Margin = new Padding(6) };
		var btnBack = new Button { Text = Loc.T("fomod.back"), AutoSize = true, Height = 36, Margin = new Padding(6) };
		btnCancel.AccessibleName = btnCancel.Text;
		btnBack.AccessibleName = btnBack.Text;
		// RightToLeft flow: first added sits rightmost, giving a visual Back | Next | Cancel order.
		buttonRow.Controls.Add(btnCancel);
		buttonRow.Controls.Add(btnNext);
		buttonRow.Controls.Add(btnBack);
		// Tab order is set explicitly (independent of the visual right-to-left add order) so a screen-reader user
		// tabs Back -> Next/Install -> Cancel. Otherwise tab order follows add order and Cancel comes first, which
		// put Cancel ahead of Install on the final step.
		btnBack.TabIndex = 0;
		btnNext.TabIndex = 1;
		btnCancel.TabIndex = 2;

		layout.Controls.Add(header, 0, 0);
		layout.Controls.Add(content, 0, 1);
		layout.Controls.Add(buttonRow, 0, 2);
		container.Controls.Add(layout);

		// Speaks an option's place in its group, then its description, shortly after it gains focus, so the screen
		// reader reads the control name and state first — the same pattern the main form's lists use. The place is
		// said because the arrows wrap from the last option to the first, and nothing else tells you it happened: a
		// WinForms radio button or check box reports no position to the reader (Sean, 2026-11-12).
		void AttachAnnounce(Control control, int position, int count, string? description)
		{
			string place = Loc.T("common.position", position, count);
			string line = string.IsNullOrWhiteSpace(description) ? place : Loc.T("fomod.positionThenDescription", place, description);
			control.GotFocus += async (s, e) =>
			{
				await Task.Delay(100);
				if (control.Focused) Speak(line);
			};
		}

		// Keeps the Next/Install button label, its accessible name, and the step counter in sync with the current
		// selection. Re-run after any option toggle, because a flag-setting choice can show or hide a later step
		// — so the button must read "Install" once the current step is the last visible one, and "Step N of M"
		// must reflect the new visible-step count.
		//
		// The counter used to live in the window's title bar, which is where NVDA+T read it from. There is no
		// title bar now, so it goes into the step heading instead — where it is both visible and reachable by
		// arrowing to the top of the view, rather than only via a title-reading command.
		void UpdateNav()
		{
			bool isLast = NextVisible(current) == -1;
			btnNext.Text = isLast ? Loc.T("fomod.install") : Loc.T("fomod.next");
			btnNext.AccessibleName = btnNext.Text;
			header.Text = Loc.T("fomod.windowTitle", config.ModuleName,
				VisiblePosition(current), VisibleCount(), config.InstallSteps[current].Name);
			header.AccessibleName = header.Text;
		}

		void Render()
		{
			content.SuspendLayout();
			content.Controls.Clear();

			FomodInstallStep step = config.InstallSteps[current];
			// The heading text is set by UpdateNav at the end of this method, which knows the step counter too.
			Dictionary<string, string> flags = BuildFlags(current - 1);
			int boxWidth = content.ClientSize.Width - 28;

			foreach (FomodGroup group in step.Groups)
			{
				if (group.Plugins.Count == 0) continue;

				var box = new GroupBox
				{
					Text = group.Name,
					AccessibleName = group.Name,
					AutoSize = true,
					AutoSizeMode = AutoSizeMode.GrowAndShrink,
					Width = boxWidth,
					Padding = new Padding(8),
					Margin = new Padding(3, 3, 3, 10)
				};
				var inner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
				box.Controls.Add(inner);

				bool isRadio = group.Type == FomodGroupType.SelectExactlyOne || group.Type == FomodGroupType.SelectAtMostOne;
				// What the arrows move between: the group's options, and "None" first when the group has one.
				bool hasNone = group.Type == FomodGroupType.SelectAtMostOne;
				int optionCount = group.Plugins.Count + (hasNone ? 1 : 0);

				// "None" option lets a SelectAtMostOne group be cleared.
				if (hasNone)
				{
					var none = new RadioButton
					{
						Text = Loc.T("fomod.none"),
						AccessibleName = Loc.T("fomod.none"),
						AutoSize = true,
						Checked = group.Plugins.All(p => !selected[p])
					};
					FomodGroup capturedGroup = group;
					none.CheckedChanged += (s, e) => { if (none.Checked) { foreach (FomodPlugin p in capturedGroup.Plugins) selected[p] = false; UpdateNav(); } };
					inner.Controls.Add(none);
					AttachAnnounce(none, 1, optionCount, null);
				}

				// First pass: create and add every option control so radio mutual-exclusion is wired up.
				var controls = new List<(ButtonBase ctrl, FomodPlugin plugin, FomodPluginType type)>();
				foreach (FomodPlugin plugin in group.Plugins)
				{
					FomodPluginType type = FomodConditionEvaluator.ResolveType(plugin, flags, fileState);
					ButtonBase ctrl = isRadio ? new RadioButton() : new CheckBox();
					// Status goes in both the visible text and the accessible name: the visible suffix replaces
					// the grey-out cue (forced options are no longer disabled, see below), and the accessible
					// name ensures the screen reader announces "required" / "recommended" / "not available".
					string label = type switch
					{
						FomodPluginType.Required => Loc.T("fomod.optionRequired", plugin.Name),
						FomodPluginType.Recommended => Loc.T("fomod.optionRecommended", plugin.Name),
						FomodPluginType.NotUsable => Loc.T("fomod.optionNotUsable", plugin.Name),
						_ => plugin.Name
					};
					ctrl.Text = label;
					ctrl.AutoSize = true;
					ctrl.Tag = plugin;
					ctrl.AccessibleName = label;
					inner.Controls.Add(ctrl);
					controls.Add((ctrl, plugin, type));
				}

				// Second pass: apply forced state and wire handlers. Forced options (SelectAll / Required /
				// NotUsable) are deliberately NOT disabled — a disabled WinForms control is skipped by the
				// keyboard and silent to NVDA, which would hide Required and unavailable options from a blind
				// user entirely. They stay enabled (focusable + readable, status in the label) and are simply
				// made non-toggleable via AutoCheck=false, with a revert guard for the radio-sibling case.
				foreach ((ButtonBase ctrl, FomodPlugin plugin, FomodPluginType type) in controls)
				{
					bool forcedOn = group.Type == FomodGroupType.SelectAll || type == FomodPluginType.Required;
					bool forcedOff = type == FomodPluginType.NotUsable;
					bool locked = forcedOn || forcedOff;
					bool fixedState = forcedOn && !forcedOff;
					if (locked) selected[plugin] = fixedState;

					SetChecked(ctrl, selected[plugin]);

					FomodPlugin capturedPlugin = plugin;
					if (locked)
					{
						if (ctrl is RadioButton rbL)
						{
							rbL.AutoCheck = false;
							rbL.CheckedChanged += (s, e) => { if (rbL.Checked != fixedState) rbL.Checked = fixedState; };
						}
						else if (ctrl is CheckBox cbL)
						{
							cbL.AutoCheck = false;
							cbL.CheckedChanged += (s, e) => { if (cbL.Checked != fixedState) cbL.Checked = fixedState; };
						}
					}
					else if (ctrl is RadioButton rb)
						rb.CheckedChanged += (s, e) => { selected[capturedPlugin] = rb.Checked; UpdateNav(); };
					else if (ctrl is CheckBox cb)
						cb.CheckedChanged += (s, e) =>
						{
							selected[capturedPlugin] = cb.Checked;
							// NVDA does not reliably announce the new state when a check box with a custom
							// AccessibleName is toggled with Space, so speak it ourselves. Only user toggles
							// reach here — seeding via SetChecked happens before this handler is attached.
							Speak(Loc.T(cb.Checked ? "fomod.checked" : "fomod.unchecked"));
							UpdateNav();
						};

					// The options come after "None" when there is one, in the order they were added above.
					AttachAnnounce(ctrl, inner.Controls.IndexOf(ctrl) + 1, optionCount, plugin.Description);
				}

				content.Controls.Add(box);
			}

			UpdateNav();

			content.ResumeLayout();

			// Every step but the first announces itself here, because the user pressed Next or Back to reach it.
			// The first is drawn while the view is still being built, and anything spoken then lands ahead of the
			// view's own title — so it is passed as the hint instead and spoken in its proper place. See
			// Form1.InlineView.
			if (firstRender) firstRender = false;
			else
			{
				Speak(Loc.T("fomod.stepAnnounce", step.Name, VisiblePosition(current), VisibleCount()));
				// The view did these once as it opened, to the first step's controls. These are new, so without this
				// Escape was dead on every later step, and contrast and text size were lost.
				AttachEscape(content, closeView);
				StylePromptPanel(content);
				SilenceUnnamedContainers(content);
			}
			FocusFirstOption(content, btnNext);
			// Last, once focus has moved on: Back to the first step disabled Back while it had the focus, and the reader said
			// "unavailable" and followed focus to Next before the step was announced.
			btnBack.Enabled = history.Count > 0;
		}

		bool ValidateStep(int index, out string error)
		{
			error = "";
			foreach (FomodGroup group in config.InstallSteps[index].Groups)
			{
				int count = group.Plugins.Count(p => selected[p]);
				if (group.Type == FomodGroupType.SelectExactlyOne && count != 1)
				{
					error = Loc.T("fomod.errorExactlyOne", group.Name);
					return false;
				}
				if (group.Type == FomodGroupType.SelectAtLeastOne && count < 1)
				{
					error = Loc.T("fomod.errorAtLeastOne", group.Name);
					return false;
				}
			}
			return true;
		}

		btnCancel.Click += (s, e) => closeView();
		btnBack.Click += (s, e) =>
		{
			if (history.Count == 0) return;
			current = history.Pop();
			Render();
		};
		btnNext.Click += (s, e) =>
		{
			if (!ValidateStep(current, out string error))
			{
				SpeakBox(error, Loc.T("fomod.wizardTitle", config.ModuleName), MessageBoxButtons.OK);
				return;
			}
			int next = NextVisible(current);
			if (next == -1)
			{
				outcome = BuildResult();
				Speak(Loc.T("fomod.installing"));
				LetSpeechFinish();
				closeView();
			}
			else
			{
				history.Push(current);
				current = next;
				Render();
			}
		};

		// Draw the first step now, and hand back the control Render just focused so the view doesn't focus
		// something else over the top of it.
		Render();
		return FirstFocusableOption(content) ?? (Control)btnNext;
		},
		// The first step's announcement, said after the title rather than before it — Render skips speaking it.
		hint: Loc.T("fomod.stepAnnounce", config.InstallSteps[first].Name, VisiblePosition(first), VisibleCount()));

		if (outcome == null)
		{
			Speak(Loc.T("fomod.cancelled"));
			return null;
		}
		return outcome;
	}

	private static void SetChecked(ButtonBase ctrl, bool value)
	{
		if (ctrl is RadioButton rb) rb.Checked = value;
		else if (ctrl is CheckBox cb) cb.Checked = value;
	}

	/// <summary>Focuses the first enabled option control in the rendered step, falling back to a button.</summary>
	private static void FocusFirstOption(Control container, Control fallback)
	{
		Control target = FirstFocusableOption(container) ?? fallback;
		// A control drawn a moment ago may not take focus yet; once its layout has run it does.
		if (!target.Focus() && container.IsHandleCreated)
			container.BeginInvoke(new Action(() => target.Focus()));
	}

	/// <summary>
	/// Where a step starts: the first group's selected option, or its first option when none is selected, so the step's
	/// choices are heard before anything can be confirmed (Sean, 2026-11-12).
	///
	/// ⚠️ Not <c>CanFocus</c>: it is false for every control until the view is on screen, and the first step is built
	/// before that. Asking it sent every wizard's first step to Install, one key press from installing unheard choices.
	/// </summary>
	private static Control? FirstFocusableOption(Control container)
	{
		foreach (Control child in container.Controls)
		{
			if ((child is RadioButton || child is CheckBox) && child.Enabled)
			{
				// A radio group is entered on its selected option, the one Tab would land on.
				if (child is RadioButton && child.Parent != null)
					return child.Parent.Controls.OfType<RadioButton>().FirstOrDefault(r => r.Checked && r.Enabled) ?? child;
				return child;
			}
			Control? nested = FirstFocusableOption(child);
			if (nested != null) return nested;
		}
		return null;
	}
}
