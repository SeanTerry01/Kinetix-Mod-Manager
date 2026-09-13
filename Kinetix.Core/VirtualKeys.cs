using System;
using System.Collections.Generic;

namespace KinetixModManager;

/// <summary>
/// Turns a Windows virtual-key code into something a person would want read to them: "Page Up" rather
/// than "33", "Shift plus F5" rather than "116,4".
///
/// This lives in the core, and stays Windows-shaped, because the codes are not the host's - they are what
/// the games write into their own configuration files. Skyrim's MCM, a BepInEx config and a Fabric keybind
/// all record a binding the way Windows numbers keys, whatever machine the game is being played on, so a
/// manager reading those files has to speak that numbering wherever it happens to be running.
///
/// It was previously private to Form1, which meant the one place in the program that knows how to say a key
/// out loud could not be reached by anything else - including the MCM row that needed exactly this.
/// </summary>
public static class VirtualKeys
{
	/// <summary>Decodes a Windows virtual-key code plus an MCM modifier bitfield (1=shift, 2=ctrl, 4=alt) into a
	/// readable combo like "Ctrl + Shift + Page Down".</summary>
	public static string DecodeVirtualKey(int vk, int modifiers)
	{
		var parts = new List<string>();
		if ((modifiers & 2) != 0) parts.Add("Ctrl");
		if ((modifiers & 1) != 0) parts.Add("Shift");
		if ((modifiers & 4) != 0) parts.Add("Alt");
		parts.Add(VirtualKeyName(vk));
		return string.Join(" + ", parts);
	}

	/// <summary>Maps a Windows virtual-key code to a readable key name, covering the keys mods actually bind.</summary>
	public static string VirtualKeyName(int vk)
	{
		if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();          // A-Z
		if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();          // 0-9 (top row)
		if (vk >= 0x60 && vk <= 0x69) return "Numpad " + (vk - 0x60);        // Numpad 0-9
		if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);              // F1-F24

		return vk switch
		{
			0x01 => "Left Mouse Button",
			0x02 => "Right Mouse Button",
			0x04 => "Middle Mouse Button",
			0x05 => "Mouse Button 4",
			0x06 => "Mouse Button 5",
			0x08 => "Backspace",
			0x09 => "Tab",
			0x0D => "Enter",
			0x10 => "Shift",
			0x11 => "Ctrl",
			0x12 => "Alt",
			0x13 => "Pause",
			0x14 => "Caps Lock",
			0x1B => "Escape",
			0x20 => "Space",
			0x21 => "Page Up",
			0x22 => "Page Down",
			0x23 => "End",
			0x24 => "Home",
			0x25 => "Left Arrow",
			0x26 => "Up Arrow",
			0x27 => "Right Arrow",
			0x28 => "Down Arrow",
			0x2C => "Print Screen",
			0x2D => "Insert",
			0x2E => "Delete",
			0x6A => "Numpad Multiply",
			0x6B => "Numpad Plus",
			0x6D => "Numpad Minus",
			0x6E => "Numpad Decimal",
			0x6F => "Numpad Divide",
			0x90 => "Num Lock",
			0x91 => "Scroll Lock",
			0xBA => ";",
			0xBB => "=",
			0xBC => ",",
			0xBD => "-",
			0xBE => ".",
			0xBF => "/",
			0xC0 => "`",
			0xDB => "[",
			0xDC => "\\",
			0xDD => "]",
			0xDE => "'",
			_ => "Key " + vk
		};
	}
}
