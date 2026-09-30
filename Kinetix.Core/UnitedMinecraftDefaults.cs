using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace KinetixModManager;

/// <summary>
/// United Minecraft's DEFAULT keys, read out of the installed mod itself.
///
/// <para>
/// ⚠️ Needed because the mod's keybind file does not exist until the player changes a key. United Minecraft writes
/// <c>config\united_minecraft_keybinds.json</c> only when a binding is changed (or migrated) — confirmed on Sean's
/// own machine, where the file appeared mid-session and three earlier sessions with the mod installed had not
/// written it. So every new player, who has changed nothing, pressed Ctrl+H and found the accessibility mod's keys
/// missing entirely: the very keys a blind player most needs to learn.
/// </para>
///
/// <para>
/// The defaults live only in the mod's compiled code, as a run of constructor calls in
/// <c>ClientKeyBindings</c>'s static initialiser — <c>new KeybindAction("narrate_health", category,
/// new Keybind(11, 0))</c>, or <c>Keybind.UNBOUND</c> for an action with no key. That is read straight from the jar,
/// so the list follows whichever version of the mod is installed rather than a copy that goes stale on its next
/// release. Anything that does not match the expected shape is skipped, and a jar that cannot be read at all gives
/// an empty list — which is exactly what Ctrl+H showed before, never something worse.
/// </para>
/// </summary>
public static class UnitedMinecraftDefaults
{
	private const string Package = "com/nibblenerds/unitedminecraft/client/";
	private const string BindingsClassEntry = Package + "ClientKeyBindings.class";
	private const string ConfigClassEntry = Package + "KeybindConfig.class";
	private const string ActionClass = Package + "KeybindAction";
	private const string KeybindClass = Package + "Keybind";

	/// <summary>
	/// The switched-on United Minecraft jar in <paramref name="modsFolder"/>, or <c>null</c>. Found by what is inside
	/// it rather than by its file name, which the player or a download site may have changed.
	/// </summary>
	public static string? FindJar(string modsFolder)
	{
		if (string.IsNullOrEmpty(modsFolder) || !Directory.Exists(modsFolder)) return null;

		foreach (string jar in Directory.EnumerateFiles(modsFolder, "*.jar"))
		{
			try
			{
				using ZipArchive zip = ZipFile.OpenRead(jar);
				if (zip.GetEntry(BindingsClassEntry) != null) return jar;
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
			{
				// Not a readable jar. Another mod's problem, and not one worth failing the controls list over.
			}
		}

		return null;
	}

	/// <summary>
	/// Every action the mod declares with its default key, in the mod's own order. Empty when the jar is not United
	/// Minecraft or its layout is not the one this understands.
	/// </summary>
	public static List<MinecraftBinding> Read(string jarPath)
	{
		var bindings = new List<MinecraftBinding>();

		try
		{
			using ZipArchive zip = ZipFile.OpenRead(jarPath);

			byte[]? bindingsClass = ReadEntry(zip, BindingsClassEntry);
			if (bindingsClass is null) return bindings;

			// The key numbering changed with Minecraft 26.3's move from GLFW to SDL, and the mod's build for it is
			// the one whose keybind file carries a schemaVersion. The same fact decides both, here and in
			// MinecraftControls.ReadUnitedMinecraftBindings, so the two can never describe one key differently.
			byte[]? configClass = ReadEntry(zip, ConfigClassEntry);
			bool sdl = configClass is not null && JavaClassFile.Parse(configClass).HasString("schemaVersion");

			foreach ((string id, int key, int modifiers) in ReadDefaults(bindingsClass))
			{
				bindings.Add(new MinecraftBinding
				{
					Id        = id,
					Action    = MinecraftControls.FriendlyActionName(id),
					Key       = sdl ? MinecraftControls.DescribeSdlCombo(key, modifiers)
					                : MinecraftControls.DescribeGlfwCombo(key, modifiers),
					IsUnbound = key < 0
				});
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"reading United Minecraft's default keys from {jarPath}", ex);
			bindings.Clear();
		}

		return bindings;
	}

	/// <summary>
	/// Walks the static initialiser and pairs each action's name with its default key. Unbound is a key of -1, the
	/// same as the mod's own file.
	///
	/// <para>
	/// Read as a sequence rather than simulated: the name is the string loaded just after a <c>KeybindAction</c> is
	/// created, the key is the two numbers handed to <c>Keybind(int, int)</c>, and the action is complete when its
	/// own constructor is called. An action whose key arrived any other way is left out rather than guessed at.
	/// </para>
	/// </summary>
	public static List<(string Id, int Key, int Modifiers)> ReadDefaults(byte[] classBytes)
	{
		var found = new List<(string, int, int)>();

		JavaClassFile cls = JavaClassFile.Parse(classBytes);
		byte[]? code = cls.MethodCode("<clinit>");
		if (code is null) return found;

		string? id = null;
		bool awaitingId = false;
		(int Key, int Modifiers)? key = null;
		var ints = new List<int>();

		foreach (JavaInstruction ins in JavaClassFile.Instructions(code))
		{
			switch (ins.Kind)
			{
				case JavaInstructionKind.New when cls.ClassName(ins.Operand) == ActionClass:
					id = null;
					key = null;
					awaitingId = true;
					break;

				case JavaInstructionKind.LoadConstant when awaitingId && cls.StringConstant(ins.Operand) is { } name:
					id = name;
					awaitingId = false;
					ints.Clear();
					break;

				case JavaInstructionKind.PushInt:
					ints.Add(ins.Operand);
					break;

				case JavaInstructionKind.LoadConstant when cls.IntConstant(ins.Operand) is { } value:
					ints.Add(value);
					break;

				case JavaInstructionKind.GetStatic when id != null && cls.MemberRef(ins.Operand) is
					{ Owner: KeybindClass, Name: "UNBOUND" }:
					key = (-1, 0);
					break;

				case JavaInstructionKind.InvokeSpecial when cls.MemberRef(ins.Operand) is { } call:
					if (id != null && call is { Owner: KeybindClass, Name: "<init>", Descriptor: "(II)V" } && ints.Count >= 2)
					{
						key = (ints[^2], ints[^1]);
					}
					else if (call is { Owner: ActionClass, Name: "<init>" })
					{
						if (id != null && key is { } k) found.Add((id, k.Key, k.Modifiers));
						id = null;
						key = null;
					}
					break;
			}
		}

		return found;
	}

	private static byte[]? ReadEntry(ZipArchive zip, string name)
	{
		ZipArchiveEntry? entry = zip.GetEntry(name);
		if (entry is null) return null;

		using Stream stream = entry.Open();
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}
}

/// <summary>The few instruction kinds <see cref="UnitedMinecraftDefaults"/> reads; everything else is Other.</summary>
public enum JavaInstructionKind { Other, New, LoadConstant, PushInt, GetStatic, InvokeSpecial }

/// <summary>One decoded instruction. <c>Operand</c> is a constant-pool index, or the value for PushInt.</summary>
public readonly record struct JavaInstruction(JavaInstructionKind Kind, int Operand);

/// <summary>A class member reference, e.g. <c>Keybind.&lt;init&gt;(II)V</c>.</summary>
public sealed record JavaMemberRef(string Owner, string Name, string Descriptor);

/// <summary>
/// Just enough of the Java class file format to read constants and one method's bytecode. Not a general decoder:
/// it understands every instruction's LENGTH, so it can walk any method without losing its place, but decodes only
/// the handful <see cref="UnitedMinecraftDefaults"/> needs.
/// </summary>
public sealed class JavaClassFile
{
	private readonly object?[] _pool;
	private readonly Dictionary<string, byte[]> _methodCode;

	private JavaClassFile(object?[] pool, Dictionary<string, byte[]> methodCode)
	{
		_pool = pool;
		_methodCode = methodCode;
	}

	// Constant-pool entries, as stored. Only what is read back is kept in a usable form.
	private sealed record Utf8(string Value);
	private sealed record IntValue(int Value);
	private sealed record ClassRef(int NameIndex);
	private sealed record StringRef(int Utf8Index);
	private sealed record MemberRefEntry(int ClassIndex, int NameAndTypeIndex);
	private sealed record NameAndType(int NameIndex, int DescriptorIndex);

	/// <summary>Parses a class file. Throws <see cref="InvalidDataException"/> on anything that is not one.</summary>
	public static JavaClassFile Parse(byte[] data)
	{
		var r = new Reader(data);
		if (r.U4() != 0xCAFEBABE) throw new InvalidDataException("Not a Java class file.");
		r.Skip(4); // minor and major version

		int count = r.U2();
		var pool = new object?[count];
		for (int i = 1; i < count; i++)
		{
			byte tag = r.U1();
			switch (tag)
			{
				case 1:  pool[i] = new Utf8(DecodeModifiedUtf8(r.Bytes(r.U2()))); break;
				case 3:  pool[i] = new IntValue(unchecked((int)r.U4())); break;
				case 4:  r.Skip(4); break;                          // float
				case 5:
				case 6:  r.Skip(8); i++; break;                     // long and double take two slots
				case 7:  pool[i] = new ClassRef(r.U2()); break;
				case 8:  pool[i] = new StringRef(r.U2()); break;
				case 9:
				case 10:
				case 11: pool[i] = new MemberRefEntry(r.U2(), r.U2()); break;
				case 12: pool[i] = new NameAndType(r.U2(), r.U2()); break;
				case 15: r.Skip(3); break;                          // method handle
				case 16:
				case 19:
				case 20: r.Skip(2); break;                          // method type, module, package
				case 17:
				case 18: r.Skip(4); break;                          // dynamic, invokedynamic
				default: throw new InvalidDataException($"Unknown constant pool tag {tag}.");
			}
		}

		r.Skip(6);                    // access flags, this class, super class
		r.Skip(2 * r.U2());           // interfaces

		int fields = r.U2();
		for (int i = 0; i < fields; i++) { r.Skip(6); SkipAttributes(r); }

		var code = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		int methods = r.U2();
		for (int i = 0; i < methods; i++)
		{
			r.Skip(2);
			string name = Utf8At(pool, r.U2());
			r.Skip(2);
			int attributes = r.U2();
			for (int a = 0; a < attributes; a++)
			{
				string attributeName = Utf8At(pool, r.U2());
				int length = checked((int)r.U4());
				byte[] body = r.Bytes(length);
				if (attributeName == "Code" && !code.ContainsKey(name))
				{
					// max_stack (2), max_locals (2), code_length (4), then the code itself.
					var cr = new Reader(body);
					cr.Skip(4);
					code[name] = cr.Bytes(checked((int)cr.U4()));
				}
			}
		}

		return new JavaClassFile(pool, code);
	}

	/// <summary>The bytecode of the first method with this name, or <c>null</c>.</summary>
	public byte[]? MethodCode(string name) => _methodCode.TryGetValue(name, out byte[]? code) ? code : null;

	/// <summary>True when a string constant or a name in the pool equals <paramref name="value"/>.</summary>
	public bool HasString(string value) => _pool.OfType<Utf8>().Any(u => u.Value == value);

	/// <summary>The class name at a Class entry, e.g. <c>java/lang/String</c>, or <c>null</c>.</summary>
	public string? ClassName(int index) =>
		At(index) is ClassRef c ? Utf8At(_pool, c.NameIndex) : null;

	/// <summary>The value of a String constant, or <c>null</c> when the entry is anything else.</summary>
	public string? StringConstant(int index) =>
		At(index) is StringRef s ? Utf8At(_pool, s.Utf8Index) : null;

	/// <summary>The value of an Integer constant, or <c>null</c> when the entry is anything else.</summary>
	public int? IntConstant(int index) => At(index) is IntValue v ? v.Value : null;

	/// <summary>A field or method reference, or <c>null</c> when the entry is anything else.</summary>
	public JavaMemberRef? MemberRef(int index)
	{
		if (At(index) is not MemberRefEntry m) return null;
		if (At(m.NameAndTypeIndex) is not NameAndType nt) return null;
		string? owner = ClassName(m.ClassIndex);
		return owner is null ? null : new JavaMemberRef(owner, Utf8At(_pool, nt.NameIndex), Utf8At(_pool, nt.DescriptorIndex));
	}

	private object? At(int index) => index > 0 && index < _pool.Length ? _pool[index] : null;

	/// <summary>
	/// Every instruction in a method body, in order. Each one's full length is known — including the padded switch
	/// instructions and <c>wide</c> — so the walk never lands in the middle of one and misreads its operands as code.
	/// </summary>
	public static IEnumerable<JavaInstruction> Instructions(byte[] code)
	{
		int pc = 0;
		while (pc < code.Length)
		{
			byte op = code[pc];
			JavaInstruction ins = op switch
			{
				>= 0x02 and <= 0x08 => new JavaInstruction(JavaInstructionKind.PushInt, op - 0x03),   // iconst_m1..5
				0x10 => new JavaInstruction(JavaInstructionKind.PushInt, (sbyte)code[pc + 1]),        // bipush
				0x11 => new JavaInstruction(JavaInstructionKind.PushInt, (short)U2At(code, pc + 1)),  // sipush
				0x12 => new JavaInstruction(JavaInstructionKind.LoadConstant, code[pc + 1]),          // ldc
				0x13 => new JavaInstruction(JavaInstructionKind.LoadConstant, U2At(code, pc + 1)),    // ldc_w
				0xB2 => new JavaInstruction(JavaInstructionKind.GetStatic, U2At(code, pc + 1)),
				0xB7 => new JavaInstruction(JavaInstructionKind.InvokeSpecial, U2At(code, pc + 1)),
				0xBB => new JavaInstruction(JavaInstructionKind.New, U2At(code, pc + 1)),
				_ => new JavaInstruction(JavaInstructionKind.Other, 0)
			};

			yield return ins;
			pc += InstructionLength(code, pc);
		}
	}

	/// <summary>How many bytes the instruction at <paramref name="pc"/> occupies, operands included.</summary>
	public static int InstructionLength(byte[] code, int pc)
	{
		byte op = code[pc];
		switch (op)
		{
			case 0x10: case 0x12: case 0xBC: case 0xA9:                     // bipush, ldc, newarray, ret
			case >= 0x15 and <= 0x19:                                        // iload..aload with an index
			case >= 0x36 and <= 0x3A:                                        // istore..astore with an index
				return 2;
			case 0x11: case 0x13: case 0x14: case 0x84:                     // sipush, ldc_w, ldc2_w, iinc
			case >= 0x99 and <= 0xA8:                                        // if*, goto, jsr
			case >= 0xB2 and <= 0xB8:                                        // field access, invokes
			case 0xBB: case 0xBD: case 0xC0: case 0xC1: case 0xC6: case 0xC7: // new, anewarray, casts, ifnull
				return 3;
			case 0xC5:                                                       // multianewarray
				return 4;
			case 0xB9: case 0xBA: case 0xC8: case 0xC9:                     // invokeinterface/dynamic, goto_w, jsr_w
				return 5;
			case 0xC4:                                                       // wide
				return code[pc + 1] == 0x84 ? 6 : 4;
			case 0xAA:                                                       // tableswitch
			{
				int p = Padded(pc);
				int low = S4At(code, p + 4), high = S4At(code, p + 8);
				return p + 12 + 4 * (high - low + 1) - pc;
			}
			case 0xAB:                                                       // lookupswitch
			{
				int p = Padded(pc);
				int pairs = S4At(code, p + 4);
				return p + 8 + 8 * pairs - pc;
			}
			default:
				return 1;
		}

		// Switch operands start on the next four-byte boundary after the opcode.
		static int Padded(int pc) => (pc + 4) & ~3;
	}

	private static int U2At(byte[] b, int i) => (b[i] << 8) | b[i + 1];

	private static int S4At(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

	private static string Utf8At(object?[] pool, int index) =>
		index > 0 && index < pool.Length && pool[index] is Utf8 u ? u.Value : "";

	private static void SkipAttributes(Reader r)
	{
		int count = r.U2();
		for (int i = 0; i < count; i++) { r.Skip(2); r.Skip(checked((int)r.U4())); }
	}

	/// <summary>
	/// Java's "modified UTF-8" is standard UTF-8 for everything an identifier or key name contains; the differences
	/// (an encoded NUL, surrogate pairs) cannot occur in the strings read here, so plain UTF-8 decoding is enough.
	/// </summary>
	private static string DecodeModifiedUtf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);

	private sealed class Reader
	{
		private readonly byte[] _data;
		private int _pos;

		public Reader(byte[] data) => _data = data;

		private void Need(int n)
		{
			if (n < 0 || _pos + n > _data.Length) throw new InvalidDataException("The class file ends early.");
		}

		public byte U1() { Need(1); return _data[_pos++]; }
		public int U2() { Need(2); int v = (_data[_pos] << 8) | _data[_pos + 1]; _pos += 2; return v; }
		public uint U4()
		{
			Need(4);
			uint v = ((uint)_data[_pos] << 24) | ((uint)_data[_pos + 1] << 16) | ((uint)_data[_pos + 2] << 8) | _data[_pos + 3];
			_pos += 4;
			return v;
		}
		public void Skip(int n) { Need(n); _pos += n; }
		public byte[] Bytes(int n) { Need(n); byte[] b = _data[_pos..(_pos + n)]; _pos += n; return b; }
	}
}
