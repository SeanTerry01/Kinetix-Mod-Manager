using System;

namespace KinetixModManager;

/// <summary>
/// One entry in a settings dropdown that stands for an enum value: the value itself, plus the wording shown on
/// screen and read out by the screen reader. <see cref="ToString"/> is what a WinForms ComboBox displays and what
/// the reader announces, so the display text lives here rather than in a parallel array that could fall out of step
/// with the values.
/// </summary>
internal sealed class SettingChoice<T> where T : struct, Enum
{
	public T Value { get; }
	private readonly string _display;

	public SettingChoice(T value, string display)
	{
		Value = value;
		_display = display;
	}

	public override string ToString() => _display;
}
