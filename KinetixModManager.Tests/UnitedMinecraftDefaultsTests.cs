using System.IO.Compression;
using System.Text;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="UnitedMinecraftDefaults"/>: reading United Minecraft's default keys out of its jar, so Ctrl+H
/// lists them for a player who has never changed one — the case where the mod has written no keybind file at all.
///
/// The class files here are built by hand, in the exact shape the real mod compiles to (checked against
/// United Minecraft 1.3.0: all 84 actions read back matching a keybind file the mod wrote itself). Building them
/// keeps the tests free of the real jar, and lets each one hold precisely the shape it is about.
/// </summary>
public class UnitedMinecraftDefaultsTests : IDisposable
{
	private const string Pkg = "com/nibblenerds/unitedminecraft/client/";

	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-umdefaults-" + Guid.NewGuid().ToString("N"));

	public UnitedMinecraftDefaultsTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
	}

	// -------------------------------------------------------------------------
	// Reading the defaults
	// -------------------------------------------------------------------------

	[Fact]
	public void EachActionIsPairedWithTheKeyHandedToItsKeybind()
	{
		byte[] cls = new BindingsClass()
			.Action("narrate_health", key: 11, modifiers: 0)
			.Action("narrate_experience", key: 11, modifiers: 3)
			.Action("narrate_armor_and_effects", key: 11, modifiers: 768)   // sipush
			.Build();

		Assert.Equal(new[]
		{
			("narrate_health", 11, 0),
			("narrate_experience", 11, 3),
			("narrate_armor_and_effects", 11, 768),
		}, UnitedMinecraftDefaults.ReadDefaults(cls));
	}

	[Fact]
	public void AnActionWithNoDefaultKeyIsReadAsUnbound()
	{
		byte[] cls = new BindingsClass()
			.Action("narrate_coordinates", key: 6, modifiers: 0)
			.UnboundAction("narrate_coordinate_x")
			.Build();

		Assert.Equal(new[] { ("narrate_coordinates", 6, 0), ("narrate_coordinate_x", -1, 0) },
			UnitedMinecraftDefaults.ReadDefaults(cls));
	}

	[Fact]
	public void AKeyTooLargeForAnInstructionIsReadFromTheConstantPool()
	{
		// Anything past 32,767 has to be an ldc of an Integer constant rather than a push. Not used by the mod
		// today; the point is that a key arriving that way is not silently lost.
		byte[] cls = new BindingsClass().Action("far_key", key: 70000, modifiers: 0, keyFromPool: true).Build();

		Assert.Equal(new[] { ("far_key", 70000, 0) }, UnitedMinecraftDefaults.ReadDefaults(cls));
	}

	[Fact]
	public void AnActionWhoseKeyArrivesSomeOtherWayIsLeftOutRatherThanGuessed()
	{
		// A shape this does not know — here, no Keybind at all — must not borrow the previous action's key.
		byte[] cls = new BindingsClass()
			.Action("known", key: 6, modifiers: 0)
			.ActionWithoutKey("mystery")
			.Action("also_known", key: 11, modifiers: 0)
			.Build();

		Assert.Equal(new[] { ("known", 6, 0), ("also_known", 11, 0) }, UnitedMinecraftDefaults.ReadDefaults(cls));
	}

	[Fact]
	public void ASwitchBeforeTheActionsDoesNotThrowTheReadingOffCourse()
	{
		// Switch instructions are padded and variable-length. Walking one a byte at a time lands inside its jump
		// table, and every "instruction" after it is garbage — which would quietly lose every key that follows.
		byte[] cls = new BindingsClass()
			.TableSwitch()
			.LookupSwitch()
			.Action("narrate_bearing", key: 5, modifiers: 0)
			.Build();

		Assert.Equal(new[] { ("narrate_bearing", 5, 0) }, UnitedMinecraftDefaults.ReadDefaults(cls));
	}

	[Fact]
	public void SomethingThatIsNotAClassFileIsRefused()
	{
		Assert.Throws<InvalidDataException>(() => UnitedMinecraftDefaults.ReadDefaults(Encoding.ASCII.GetBytes("not a class")));
	}

	// -------------------------------------------------------------------------
	// The jar as a whole
	// -------------------------------------------------------------------------

	[Fact]
	public void TheModsBuildFor263IsReadWithSdlKeyNumbers()
	{
		// 6 is C and 11 is H under SDL; under GLFW both are unprintable control codes.
		string jar = WriteJar("united.jar", sdl: true,
			new BindingsClass().Action("narrate_coordinates", 6, 0).Action("narrate_experience", 11, 3).Build());

		List<MinecraftBinding> bindings = UnitedMinecraftDefaults.Read(jar);

		Assert.Equal(new[] { "C", "Shift plus H" }, bindings.Select(b => b.Key));
		Assert.Equal("Narrate coordinates", bindings[0].Action);
	}

	[Fact]
	public void AnOlderBuildIsReadWithGlfwKeyNumbers()
	{
		// Before 26.3 letters were their ASCII codes: 67 is C, and GLFW's Shift bit is 1.
		string jar = WriteJar("united-old.jar", sdl: false,
			new BindingsClass().Action("narrate_coordinates", 'C', 0).Action("narrate_experience", 'H', 1).Build());

		Assert.Equal(new[] { "C", "Shift plus H" }, UnitedMinecraftDefaults.Read(jar).Select(b => b.Key));
	}

	[Fact]
	public void AnUnboundDefaultIsMarkedSoItIsListedLast()
	{
		string jar = WriteJar("united.jar", sdl: true, new BindingsClass().UnboundAction("narrate_coordinate_y").Build());

		MinecraftBinding only = Assert.Single(UnitedMinecraftDefaults.Read(jar));
		Assert.True(only.IsUnbound);
		Assert.Equal("Not bound", only.Key);
	}

	[Fact]
	public void AJarThatCannotBeReadGivesAnEmptyListNotAnError()
	{
		string broken = Path.Combine(_dir, "broken.jar");
		File.WriteAllText(broken, "this is not a zip");

		Assert.Empty(UnitedMinecraftDefaults.Read(broken));
	}

	[Fact]
	public void TheModIsFoundByWhatIsInsideItNotByItsName()
	{
		string mods = Path.Combine(_dir, "mods");
		Directory.CreateDirectory(mods);
		WriteJar(Path.Combine("mods", "some-other-mod.jar"), sdl: true, classBytes: null);
		string renamed = WriteJar(Path.Combine("mods", "renamed-by-a-download-site.jar"), sdl: true,
			new BindingsClass().Action("a", 4, 0).Build());

		Assert.Equal(renamed, UnitedMinecraftDefaults.FindJar(mods));
	}

	[Fact]
	public void ASwitchedOffCopyOfTheModIsNotRead()
	{
		// The manager switches a mod off by renaming it .disabled; the game does not load it, so neither should
		// its keys be listed as though it were playing.
		string mods = Path.Combine(_dir, "mods");
		Directory.CreateDirectory(mods);
		string jar = WriteJar(Path.Combine("mods", "united.jar"), sdl: true, new BindingsClass().Action("a", 4, 0).Build());
		File.Move(jar, jar + ".disabled");

		Assert.Null(UnitedMinecraftDefaults.FindJar(mods));
	}

	[Fact]
	public void NoModsFolderMeansNoJar()
	{
		Assert.Null(UnitedMinecraftDefaults.FindJar(Path.Combine(_dir, "nowhere")));
		Assert.Null(UnitedMinecraftDefaults.FindJar(""));
	}

	// -------------------------------------------------------------------------
	// Defaults and the player's own file together
	// -------------------------------------------------------------------------

	[Fact]
	public void WithNoKeybindFileEveryDefaultIsListed()
	{
		// The bug itself: a new player has changed nothing, so the mod has written no file.
		var defaults = new[] { Binding("narrate_health", "H"), Binding("narrate_time", "V") };

		Assert.Equal(new[] { "H", "V" },
			MinecraftControls.MergeUnitedMinecraft(defaults, Array.Empty<MinecraftBinding>()).Select(b => b.Key));
	}

	[Fact]
	public void AKeyThePlayerChangedWinsOverTheDefault()
	{
		var defaults = new[] { Binding("narrate_health", "H"), Binding("narrate_time", "V") };
		var file = new[] { Binding("narrate_time", "T") };

		List<MinecraftBinding> merged = MinecraftControls.MergeUnitedMinecraft(defaults, file);

		Assert.Equal(new[] { ("narrate_health", "H"), ("narrate_time", "T") }, merged.Select(b => (b.Id, b.Key)));
	}

	[Fact]
	public void AnActionOnlyTheFileKnowsIsKept()
	{
		// A file from another build of the mod may name an action this build does not declare. Dropping it would
		// hide a key the player really has.
		var defaults = new[] { Binding("narrate_health", "H") };
		var file = new[] { Binding("brand_new_feature", "Q") };

		Assert.Equal(new[] { "narrate_health", "brand_new_feature" },
			MinecraftControls.MergeUnitedMinecraft(defaults, file).Select(b => b.Id));
	}

	[Fact]
	public void WithNoJarTheFileIsListedAsBefore()
	{
		var file = new[] { Binding("narrate_health", "H"), Binding("narrate_time", "V") };

		Assert.Equal(new[] { "narrate_health", "narrate_time" },
			MinecraftControls.MergeUnitedMinecraft(Array.Empty<MinecraftBinding>(), file).Select(b => b.Id));
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	private static MinecraftBinding Binding(string id, string key) =>
		new() { Id = id, Action = MinecraftControls.FriendlyActionName(id), Key = key };

	/// <summary>
	/// Writes a jar holding a ClientKeyBindings class (when given) and a KeybindConfig class that does or does not
	/// mention schemaVersion — the fact that separates the mod's SDL build from its GLFW one.
	/// </summary>
	private string WriteJar(string relativePath, bool sdl, byte[]? classBytes)
	{
		string path = Path.Combine(_dir, relativePath);
		using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
		{
			if (classBytes != null) AddEntry(zip, Pkg + "ClientKeyBindings.class", classBytes);
			AddEntry(zip, Pkg + "KeybindConfig.class", ConfigClass(sdl));
			AddEntry(zip, "fabric.mod.json", Encoding.UTF8.GetBytes("{}"));
		}
		return path;
	}

	private static void AddEntry(ZipArchive zip, string name, byte[] bytes)
	{
		using Stream s = zip.CreateEntry(name).Open();
		s.Write(bytes);
	}

	private static byte[] ConfigClass(bool sdl)
	{
		var b = new ClassBuilder(Pkg + "KeybindConfig");
		b.Utf8(sdl ? "schemaVersion" : "somethingElse");
		return b.Build(clinit: null);
	}

	/// <summary>Builds ClientKeyBindings' static initialiser in the shape the real mod compiles to.</summary>
	private sealed class BindingsClass
	{
		private readonly ClassBuilder _b = new(Pkg + "ClientKeyBindings");
		private readonly List<byte> _code = new();

		public BindingsClass Action(string id, int key, int modifiers, bool keyFromPool = false)
		{
			StartAction(id);
			// new Keybind; dup; <key>; <modifiers>; invokespecial Keybind.<init>(II)V
			Op(0xBB); U2(_b.Class(Pkg + "Keybind"));
			Op(0x59);
			if (keyFromPool) { Op(0x13); U2(_b.Integer(key)); } else PushInt(key);
			PushInt(modifiers);
			Op(0xB7); U2(_b.Method(Pkg + "Keybind", "<init>", "(II)V"));
			EndAction(id);
			return this;
		}

		public BindingsClass UnboundAction(string id)
		{
			StartAction(id);
			Op(0xB2); U2(_b.Field(Pkg + "Keybind", "UNBOUND", "L" + Pkg + "Keybind;"));
			EndAction(id);
			return this;
		}

		public BindingsClass ActionWithoutKey(string id)
		{
			StartAction(id);
			Op(0x01); // aconst_null
			EndAction(id);
			return this;
		}

		public BindingsClass TableSwitch()
		{
			PushInt(0);
			Op(0xAA);
			Pad();
			// Jump distances of 178, which is also the byte for getstatic — a three-byte instruction. A walk that
			// misreads the table as code takes that last byte as an opcode and swallows the start of the next real
			// instruction; all-zero offsets would read as harmless one-byte nops and hide the fault entirely.
			S4(178);     // default offset
			S4(1);       // low
			S4(3);       // high: three offsets follow
			S4(178); S4(178); S4(178);
			return this;
		}

		public BindingsClass LookupSwitch()
		{
			PushInt(0);
			Op(0xAB);
			Pad();
			S4(178);      // default offset — see TableSwitch for why 178
			S4(2);        // two match/offset pairs
			S4(1); S4(178);
			S4(2); S4(178);
			return this;
		}

		public byte[] Build()
		{
			Op(0xB1); // return
			return _b.Build(_code.ToArray());
		}

		private void StartAction(string id)
		{
			// new KeybindAction; dup; ldc "id"; getstatic KeybindCategory.NARRATION
			Op(0xBB); U2(_b.Class(Pkg + "KeybindAction"));
			Op(0x59);
			int str = _b.String(id);
			if (str < 256) { Op(0x12); _code.Add((byte)str); } else { Op(0x13); U2(str); }
			Op(0xB2); U2(_b.Field(Pkg + "KeybindCategory", "NARRATION", "L" + Pkg + "KeybindCategory;"));
		}

		private void EndAction(string id)
		{
			Op(0xB7); U2(_b.Method(Pkg + "KeybindAction", "<init>",
				"(Ljava/lang/String;L" + Pkg + "KeybindCategory;L" + Pkg + "Keybind;)V"));
			Op(0xB3); U2(_b.Field(Pkg + "ClientKeyBindings", id.ToUpperInvariant(), "L" + Pkg + "KeybindAction;"));
		}

		private void PushInt(int v)
		{
			if (v >= -1 && v <= 5) Op((byte)(0x03 + v));
			else if (v >= sbyte.MinValue && v <= sbyte.MaxValue) { Op(0x10); _code.Add((byte)(sbyte)v); }
			else { Op(0x11); U2((ushort)(short)v); }
		}

		// Switch operands start on a four-byte boundary counted from the start of the method body.
		private void Pad() { while (_code.Count % 4 != 0) _code.Add(0); }

		private void Op(byte op) => _code.Add(op);
		private void U2(int v) { _code.Add((byte)(v >> 8)); _code.Add((byte)v); }
		private void S4(int v) { _code.Add((byte)(v >> 24)); _code.Add((byte)(v >> 16)); _code.Add((byte)(v >> 8)); _code.Add((byte)v); }
	}

	/// <summary>A minimal class file writer: a constant pool and, optionally, one static initialiser.</summary>
	private sealed class ClassBuilder
	{
		private readonly List<byte[]> _entries = new();
		private readonly Dictionary<string, int> _index = new();
		private readonly int _this;

		public ClassBuilder(string name) => _this = Class(name);

		public int Utf8(string s) => Intern("u:" + s, () =>
		{
			byte[] bytes = Encoding.UTF8.GetBytes(s);
			return new byte[] { 1, (byte)(bytes.Length >> 8), (byte)bytes.Length }.Concat(bytes).ToArray();
		});

		public int Integer(int v) => Intern("i:" + v,
			() => new byte[] { 3, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

		public int Class(string name) { int n = Utf8(name); return Intern("c:" + name, () => Tag2(7, n)); }

		public int String(string s) { int n = Utf8(s); return Intern("s:" + s, () => Tag2(8, n)); }

		public int Field(string owner, string name, string desc) => Member(9, owner, name, desc);

		public int Method(string owner, string name, string desc) => Member(10, owner, name, desc);

		private int Member(byte tag, string owner, string name, string desc)
		{
			int c = Class(owner);
			int n = Utf8(name), d = Utf8(desc);
			int nt = Intern($"nt:{name}:{desc}", () => new byte[] { 12, (byte)(n >> 8), (byte)n, (byte)(d >> 8), (byte)d });
			return Intern($"m{tag}:{owner}.{name}{desc}",
				() => new byte[] { tag, (byte)(c >> 8), (byte)c, (byte)(nt >> 8), (byte)nt });
		}

		public byte[] Build(byte[]? clinit)
		{
			int clinitName = Utf8("<clinit>"), voidDesc = Utf8("()V"), codeName = Utf8("Code");
			int objectClass = Class("java/lang/Object");

			var o = new List<byte>();
			void U2(int v) { o.Add((byte)(v >> 8)); o.Add((byte)v); }
			void U4(int v) { o.Add((byte)(v >> 24)); o.Add((byte)(v >> 16)); o.Add((byte)(v >> 8)); o.Add((byte)v); }

			U4(unchecked((int)0xCAFEBABE));
			U2(0); U2(65);
			U2(_entries.Count + 1);
			foreach (byte[] e in _entries) o.AddRange(e);
			U2(0x0021); U2(_this); U2(objectClass);
			U2(0);                         // interfaces
			U2(0);                         // fields
			if (clinit is null) { U2(0); }
			else
			{
				U2(1);
				U2(0x0008); U2(clinitName); U2(voidDesc);
				U2(1);                     // one attribute: Code
				U2(codeName);
				U4(12 + clinit.Length);    // max_stack, max_locals, code_length, code, exceptions, attributes
				U2(8); U2(0);
				U4(clinit.Length);
				o.AddRange(clinit);
				U2(0); U2(0);
			}
			U2(0);                         // class attributes
			return o.ToArray();
		}

		private int Intern(string key, Func<byte[]> make)
		{
			if (_index.TryGetValue(key, out int i)) return i;
			_entries.Add(make());
			i = _entries.Count;
			_index[key] = i;
			return i;
		}

		private static byte[] Tag2(byte tag, int v) => new[] { tag, (byte)(v >> 8), (byte)v };
	}
}
