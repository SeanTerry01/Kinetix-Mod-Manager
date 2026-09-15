using System;

namespace KinetixModManager;

/// <summary>
/// One entry in a settings dropdown that stands for an enum value: the value itself, plus the wording shown on
/// screen and read out by the screen reader. <see cref="ToString"/> is what a list control displays and what the
/// reader announces, so the display text lives here rather than in a parallel array that could fall out of step
/// with the values.
///
/// <para>
/// Public rather than internal now that it lives in the core, for the same reason <see cref="PlatformInfo"/> is:
/// internal does not cross an assembly boundary, and both front ends need to put the same words against the same
/// enum value. Two settings screens disagreeing about what an option is called would be worse than either.
/// </para>
/// </summary>
public sealed class SettingChoice<T> where T : struct, Enum
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
