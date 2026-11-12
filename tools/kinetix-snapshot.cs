// Kinetix snapshot: copies everything a test session could change, says afterwards exactly what did change, and puts it back.
//
//   dotnet run tools\kinetix-snapshot.cs -- size     <plan.json>
//   dotnet run tools\kinetix-snapshot.cs -- snapshot <plan.json> <new snapshot folder>
//   dotnet run tools\kinetix-snapshot.cs -- compare  <snapshot folder> [--quick]
//   dotnet run tools\kinetix-snapshot.cs -- restore  <snapshot folder> [--quick]
//
// The plan names the folders (with anything to leave out), registry keys, settings to keep at their newest, and programs that
// must be closed. A snapshot keeps each distinct file once, by its SHA-256, so files linked or identical in several places cost
// nothing extra, plus a manifest of every file's size, date, attributes, fingerprint and which files are hard links of each
// other. Compare goes by the fingerprint, so a change that kept the size and the date is still found; --quick trusts size and
// date instead. Restore never deletes anything it finds: whatever it takes away goes into the snapshot's set-aside folder.
// It prints paths and counts only, never what is inside a file.

#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;

return Snapshot.Run(args);

static class Snapshot
{
	static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
	const FileAttributes Compared = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;
	static readonly byte[] Buffer = new byte[1 << 20];

	internal static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		IncludeFields = true,
		PropertyNameCaseInsensitive = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public static int Run(string[] args)
	{
		bool quick = args.Contains("--quick", Ci);
		string[] a = args.Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToArray();
		try
		{
			return (a.Length >= 2 ? a[0].ToLowerInvariant() : "") switch
			{
				"size" => Size(LoadPlan(a[1])),
				"snapshot" when a.Length >= 3 => Take(LoadPlan(a[1]), Path.GetFullPath(a[2])),
				"compare" => CompareCommand(Path.GetFullPath(a[1]), quick),
				"restore" => Restore(Path.GetFullPath(a[1]), quick),
				_ => Usage()
			};
		}
		catch (Stop stop)
		{
			Console.Error.WriteLine(stop.Message);
			return 2;
		}
	}

	static int Usage()
	{
		Console.Error.WriteLine("Use: size <plan.json> | snapshot <plan.json> <new folder> | compare <snapshot folder> [--quick] | restore <snapshot folder> [--quick]");
		return 2;
	}

	// ---------------------------------------------------------------- plan, manifest

	static Plan LoadPlan(string file)
	{
		Plan plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(file), JsonOptions) ?? throw new Stop("The plan is empty.");
		if (plan.Areas.Count == 0) throw new Stop("The plan names no folders.");
		foreach (AreaPlan area in plan.Areas)
		{
			area.Path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(area.Path));
			area.Exclude = area.Exclude.Select(Norm).ToList();
		}
		if (plan.Areas.Select(x => x.Name).Distinct(Ci).Count() != plan.Areas.Count) throw new Stop("Two folders in the plan have the same name.");
		foreach (AreaPlan x in plan.Areas)
			foreach (AreaPlan y in plan.Areas)
				if (!ReferenceEquals(x, y) && IsInside(x.Path, y.Path)) throw new Stop($"\"{y.Name}\" is inside \"{x.Name}\". List it once.");
		return plan;
	}

	static Manifest LoadManifest(string snap)
	{
		string file = Path.Combine(snap, "manifest.json");
		if (!File.Exists(file)) throw new Stop($"There's no snapshot in {snap}.");
		return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(file), JsonOptions) ?? throw new Stop("The snapshot's manifest is empty.");
	}

	static string Norm(string rel) => rel.Replace('/', '\\').Trim('\\');

	static bool IsInside(string folder, string path)
	{
		string f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + "\\";
		string p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + "\\";
		return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
	}

	static bool Excluded(string rel, List<string> exclude) =>
		exclude.Any(e => Ci.Equals(rel, e) || rel.StartsWith(e + "\\", StringComparison.OrdinalIgnoreCase));

	static string StorePath(string snap, string hash) => Path.Combine(snap, "store", hash[..2], hash);

	static KeepNewest? Rule(Manifest m, string area, string rel) =>
		m.Plan.KeepNewest.FirstOrDefault(k => Ci.Equals(k.Area, area) && Ci.Equals(Norm(k.File), rel));

	static string Size(long bytes) =>
		bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.00} GB" : $"{bytes / (double)(1L << 20):0.0} MB";

	// ---------------------------------------------------------------- reading the disk

	static LiveArea Scan(string root, List<string> exclude)
	{
		var live = new LiveArea();
		if (!Directory.Exists(root)) return live;

		var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
		var pending = new Stack<string>();
		pending.Push("");
		while (pending.Count > 0)
		{
			string relDir = pending.Pop();
			List<FileSystemInfo> entries;
			try { entries = new DirectoryInfo(relDir.Length == 0 ? root : Path.Combine(root, relDir)).EnumerateFileSystemInfos("*", options).ToList(); }
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				live.Unreadable.Add($"{(relDir.Length == 0 ? "(the folder itself)" : relDir)} ({ex.Message})");
				continue;
			}

			foreach (FileSystemInfo entry in entries)
			{
				string rel = relDir.Length == 0 ? entry.Name : Path.Combine(relDir, entry.Name);
				if (Excluded(rel, exclude)) continue;

				// A symbolic link or junction is recorded as itself and never followed. Other reparse points (a cloud file, say)
				// have no target and are ordinary files and folders.
				if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 && entry.LinkTarget is { } target)
				{
					live.Links[rel] = new LinkState { Rel = rel, Dir = entry is DirectoryInfo, Target = target };
					continue;
				}

				if (entry is DirectoryInfo)
				{
					live.Dirs.Add(rel);
					pending.Push(rel);
				}
				else if (entry is FileInfo file)
				{
					live.Files[rel] = new LiveFile
					{
						Rel = rel,
						Full = file.FullName,
						Size = file.Length,
						Mtime = file.LastWriteTimeUtc.Ticks,
						Ctime = file.CreationTimeUtc.Ticks,
						Attr = (int)(file.Attributes & Compared)
					};
				}
			}
		}
		return live;
	}

	static FileStream OpenRead(string path) => new(path, new FileStreamOptions
	{
		Mode = FileMode.Open,
		Access = FileAccess.Read,
		Share = FileShare.ReadWrite | FileShare.Delete,
		BufferSize = 0,
		Options = FileOptions.SequentialScan
	});

	static string HashOf(Stream source, Stream? copyTo = null)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		int read;
		while ((read = source.Read(Buffer, 0, Buffer.Length)) > 0)
		{
			hash.AppendData(Buffer, 0, read);
			copyTo?.Write(Buffer, 0, read);
		}
		return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
	}

	static string HashFile(string path)
	{
		using FileStream s = OpenRead(path);
		return HashOf(s);
	}

	static string? TryId(string path)
	{
		try
		{
			using FileStream s = OpenRead(path);
			return Native.Identify(s.SafeFileHandle).id;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
	}

	static string CopyIntoStore(Stream source, string snap)
	{
		string tmpDir = Path.Combine(snap, "store", "tmp");
		Directory.CreateDirectory(tmpDir);
		string tmp = Path.Combine(tmpDir, Guid.NewGuid().ToString("N"));
		string hash;
		using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 0)) hash = HashOf(source, output);

		string final = StorePath(snap, hash);
		if (File.Exists(final)) File.Delete(tmp);
		else
		{
			Directory.CreateDirectory(Path.GetDirectoryName(final)!);
			File.Move(tmp, final);
		}
		return hash;
	}

	static void RefuseIfRunning(Plan plan)
	{
		var running = new List<string>();
		foreach (Process p in Process.GetProcesses())
		{
			using (p)
			{
				if (p.Id == Environment.ProcessId) continue;
				string? exe = null;
				try { exe = p.MainModule?.FileName; } catch (Exception) { }
				if (exe == null) continue;

				bool hit = plan.Areas.Any(a => IsInside(a.Path, exe))
					|| plan.MustNotRun.Any(n => n.Contains('\\') ? Ci.Equals(Path.GetFullPath(n), exe) : Ci.Equals(n, Path.GetFileName(exe)));
				if (hit) running.Add($"{Path.GetFileName(exe)} ({exe})");
			}
		}
		if (running.Count > 0)
			throw new Stop("Close these first, then try again:\n  " + string.Join("\n  ", running.Distinct(Ci)));
	}

	// ---------------------------------------------------------------- registry

	static int Reg(params string[] args)
	{
		var info = new ProcessStartInfo("reg.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
		foreach (string a in args) info.ArgumentList.Add(a);
		using Process p = Process.Start(info)!;
		p.StandardOutput.ReadToEnd();
		p.StandardError.ReadToEnd();
		p.WaitForExit();
		return p.ExitCode;
	}

	static bool RegExists(string key) => Reg("query", key) == 0;

	// ---------------------------------------------------------------- size, snapshot

	static int Size(Plan plan)
	{
		long total = 0;
		foreach (AreaPlan area in plan.Areas)
		{
			if (!Directory.Exists(area.Path)) { Console.WriteLine($"{area.Name}: not there ({area.Path})"); continue; }
			LiveArea live = Scan(area.Path, area.Exclude);
			long size = live.Files.Values.Sum(f => f.Size);
			total += size;
			Console.WriteLine($"{area.Name}: {live.Files.Count:N0} files, {Size(size)} ({area.Path})");
			foreach (string u in live.Unreadable) Console.WriteLine($"  couldn't read {u}");
		}
		Console.WriteLine($"Altogether {Size(total)}. A snapshot needs at most that, less where files are linked or the same.");
		foreach (string root in plan.Areas.Select(a => Path.GetPathRoot(a.Path)!).Distinct(Ci))
			Console.WriteLine($"{root} has {Size(new DriveInfo(root).AvailableFreeSpace)} free.");
		return 0;
	}

	static int Take(Plan plan, string snap)
	{
		foreach (AreaPlan area in plan.Areas)
			if (IsInside(area.Path, snap) || IsInside(snap, area.Path)) throw new Stop($"The snapshot folder can't be inside or around \"{area.Name}\".");
		if (Directory.Exists(snap) && Directory.EnumerateFileSystemEntries(snap).Any()) throw new Stop("The snapshot folder already has something in it. Choose a new one.");
		RefuseIfRunning(plan);

		var scans = plan.Areas.Select(a => (area: a, existed: Directory.Exists(a.Path), live: Scan(a.Path, a.Exclude))).ToList();
		long need = scans.Sum(s => s.live.Files.Values.Sum(f => f.Size));
		int count = scans.Sum(s => s.live.Files.Count);
		Directory.CreateDirectory(snap);
		long free = new DriveInfo(Path.GetPathRoot(snap)!).AvailableFreeSpace;
		if (need + (2L << 30) > free) throw new Stop($"This needs up to {Size(need)}, and that drive has {Size(free)} free.");
		Console.WriteLine($"Copying {count:N0} files, up to {Size(need)}, into {snap}");

		var m = new Manifest { Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Plan = plan };
		var hashById = new Dictionary<string, string>(StringComparer.Ordinal);
		int done = 0;
		foreach (var (area, existed, live) in scans)
		{
			var state = new AreaState
			{
				Name = area.Name,
				Path = area.Path,
				Existed = existed,
				Dirs = live.Dirs.Order(Ci).ToList(),
				Links = live.Links.Values.OrderBy(l => l.Rel, Ci).ToList()
			};
			m.Unreadable.AddRange(live.Unreadable.Select(u => $"{area.Name}: {u}"));

			foreach (LiveFile f in live.Files.Values.OrderBy(f => f.Rel, Ci))
			{
				try
				{
					using FileStream stream = OpenRead(f.Full);
					var (id, links) = Native.Identify(stream.SafeFileHandle);
					// Hard links are one file, so the second name needs no reading.
					if (!hashById.TryGetValue(id, out string? hash))
					{
						hash = CopyIntoStore(stream, snap);
						hashById[id] = hash;
					}
					state.Files.Add(new FileState { Rel = f.Rel, Size = f.Size, Mtime = f.Mtime, Ctime = f.Ctime, Attr = f.Attr, Hash = hash, Id = id, Links = links });
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					m.Unreadable.Add($"{area.Name}: {f.Rel} ({ex.Message})");
				}
				if (++done % 5000 == 0) Console.WriteLine($"  {done:N0} files...");
			}
			m.Areas.Add(state);
		}

		for (int i = 0; i < plan.Registry.Count; i++)
		{
			string key = plan.Registry[i];
			string file = $"registry-{i}.reg";
			bool existed = RegExists(key);
			if (existed && Reg("export", key, Path.Combine(snap, file), "/y") != 0) throw new Stop($"Couldn't copy the registry key {key}.");
			m.Registry.Add(new RegState { Key = key, Existed = existed, File = existed ? file : "" });
		}

		try { Directory.Delete(Path.Combine(snap, "store", "tmp")); } catch (IOException) { }
		File.WriteAllText(Path.Combine(snap, "manifest.json"), JsonSerializer.Serialize(m, JsonOptions));

		long kept = Directory.Exists(Path.Combine(snap, "store"))
			? new DirectoryInfo(Path.Combine(snap, "store")).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
			: 0;
		Console.WriteLine($"Snapshot taken: {m.Areas.Sum(a => a.Files.Count):N0} files ({Size(kept)} stored), {m.Areas.Sum(a => a.Dirs.Count):N0} folders, "
			+ $"{m.Areas.Sum(a => a.Links.Count)} links, {m.Registry.Count} registry keys.");
		if (m.Unreadable.Count == 0) return 0;

		Console.WriteLine($"{m.Unreadable.Count} couldn't be read, so they aren't in it:");
		foreach (string u in m.Unreadable.Take(30)) Console.WriteLine("  " + u);
		return 1;
	}

	// ---------------------------------------------------------------- compare

	static Comparison Diff(Manifest m, string snap, bool quick)
	{
		var result = new Comparison();
		foreach (AreaState state in m.Areas)
		{
			AreaPlan plan = m.Plan.Areas.First(a => Ci.Equals(a.Name, state.Name));
			var d = new AreaDiff(state) { ExistsNow = Directory.Exists(state.Path) };
			d.Live = Scan(state.Path, plan.Exclude);
			d.Unchecked.AddRange(d.Live.Unreadable);

			var then = state.Files.ToDictionary(f => f.Rel, Ci);
			foreach (LiveFile f in d.Live.Files.Values.OrderBy(f => f.Rel, Ci))
			{
				if (!then.TryGetValue(f.Rel, out FileState? old)) { d.Added.Add(f.Rel); continue; }
				try
				{
					using FileStream s = OpenRead(f.Full);
					(f.Id, f.Links) = Native.Identify(s.SafeFileHandle);
					f.Hash = f.Size != old.Size ? "" : quick && f.Mtime == old.Mtime ? old.Hash : HashOf(s);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					d.Unchecked.Add($"{f.Rel} ({ex.Message})");
					continue;
				}

				if (f.Hash != old.Hash)
				{
					if (KeptOnly(m, snap, state, old, f)) d.Kept.Add(f.Rel);
					else d.Changed.Add(f.Rel);
				}
				else if (f.Mtime != old.Mtime || f.Attr != old.Attr) d.Touched.Add(f.Rel);
			}
			d.Removed.AddRange(state.Files.Where(f => !d.Live.Files.ContainsKey(f.Rel)).Select(f => f.Rel));

			var thenDirs = new HashSet<string>(state.Dirs, Ci);
			d.DirsAdded.AddRange(d.Live.Dirs.Where(x => !thenDirs.Contains(x)).Order(Ci));
			d.DirsRemoved.AddRange(state.Dirs.Where(x => !d.Live.Dirs.Contains(x)));

			var thenLinks = state.Links.ToDictionary(l => l.Rel, Ci);
			foreach (LinkState l in d.Live.Links.Values.OrderBy(l => l.Rel, Ci))
			{
				if (!thenLinks.TryGetValue(l.Rel, out LinkState? old)) d.LinksAdded.Add(l.Rel);
				else if (old.Dir != l.Dir || !Ci.Equals(old.Target, l.Target)) d.LinksChanged.Add(l.Rel);
			}
			d.LinksRemoved.AddRange(state.Links.Where(l => !d.Live.Links.ContainsKey(l.Rel)).Select(l => l.Rel));
			result.Areas.Add(d);
		}

		// Which files are hard links of which, among the files that were there before and are still there with the same contents.
		// Compared across every folder, because a mod's file in the game folder is a link to its copy in the manager's mod folder.
		var common = result.Areas.SelectMany(d => d.State.Files
				.Where(f => d.Live.Files.TryGetValue(f.Rel, out LiveFile? now) && now.Hash == f.Hash && now.Id.Length > 0)
				.Select(f => (d, old: f, now: d.Live.Files[f.Rel])))
			.ToList();
		string Key((AreaDiff d, FileState old, LiveFile now) c) => c.d.State.Name + "|" + c.old.Rel.ToLowerInvariant();
		var thenGroups = common.GroupBy(c => c.old.Id).ToDictionary(g => g.Key, g => string.Join("\n", g.Select(Key).Order(StringComparer.Ordinal)));
		var nowGroups = common.GroupBy(c => c.now.Id).ToDictionary(g => g.Key, g => string.Join("\n", g.Select(Key).Order(StringComparer.Ordinal)));
		foreach (var c in common)
			if (thenGroups[c.old.Id] != nowGroups[c.now.Id]) c.d.Relinked.Add(c.old.Rel);

		foreach (RegState r in m.Registry)
		{
			bool existsNow = RegExists(r.Key);
			bool differs = existsNow != r.Existed;
			if (existsNow && r.Existed)
			{
				string now = Path.Combine(snap, "registry-now.reg");
				Reg("export", r.Key, now, "/y");
				differs = !File.ReadAllBytes(now).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(snap, r.File)));
				File.Delete(now);
			}
			result.Registry.Add((r, differs));
		}
		return result;
	}

	/// <summary>Whether a settings file differs only in the settings the plan keeps at their newest, such as a sign-in that Microsoft replaces.</summary>
	static bool KeptOnly(Manifest m, string snap, AreaState state, FileState old, LiveFile now)
	{
		if (Rule(m, state.Name, old.Rel) is not { } rule) return false;
		JsonObject? before = ReadJson(StorePath(snap, old.Hash)), after = ReadJson(now.Full);
		if (before == null || after == null) return false;
		foreach (string p in rule.Properties) { before.Remove(p); after.Remove(p); }
		return JsonNode.DeepEquals(before, after);
	}

	static JsonObject? ReadJson(string path)
	{
		try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
	}

	static bool Write(Comparison c, TextWriter w, int cap)
	{
		bool any = false;
		foreach (AreaDiff d in c.Areas)
		{
			var parts = new (string label, List<string> items)[]
			{
				("added", d.Added),
				("changed", d.Changed),
				("gone", d.Removed),
				("same contents, but linked differently", d.Relinked),
				("same contents, but a different date or read-only, hidden or system setting", d.Touched),
				("new folders", d.DirsAdded),
				("folders gone", d.DirsRemoved),
				("new links", d.LinksAdded),
				("links gone", d.LinksRemoved),
				("links pointing somewhere else", d.LinksChanged),
				("couldn't be checked", d.Unchecked),
			};
			string header = $"{d.State.Name} ({d.State.Path}):";
			bool rootChange = d.State.Existed != d.ExistsNow;
			if (!rootChange && parts.All(p => p.items.Count == 0) && d.Kept.Count == 0) continue;

			w.WriteLine(header);
			if (rootChange) w.WriteLine(d.ExistsNow ? "  The folder wasn't there before." : "  The folder is gone.");
			foreach (var (label, items) in parts.Where(p => p.items.Count > 0))
			{
				w.WriteLine($"  {items.Count:N0} {label}:");
				foreach (string item in items.Take(cap)) w.WriteLine("    " + item);
				if (items.Count > cap) w.WriteLine($"    ...and {items.Count - cap:N0} more (all of them are in the report file).");
			}
			if (d.Kept.Count > 0)
			{
				w.WriteLine($"  {d.Kept.Count} changed only in settings kept at their newest (not a difference):");
				foreach (string item in d.Kept) w.WriteLine("    " + item);
			}
			any |= rootChange || parts.Any(p => p.items.Count > 0);
		}
		foreach (var (r, differs) in c.Registry.Where(r => r.differs))
		{
			w.WriteLine($"Registry key {r.Key}: " + (!r.Existed ? "it wasn't there before." : !RegExists(r.Key) ? "it's gone." : "changed."));
			any = true;
		}
		if (!any) w.WriteLine("Everything matches the snapshot.");
		return any;
	}

	static bool Report(Comparison c, string snap, string name)
	{
		string file = Path.Combine(snap, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
		using (var w = new StreamWriter(file)) Write(c, w, int.MaxValue);
		bool any = Write(c, Console.Out, 40);
		Console.WriteLine($"(Saved as {file})");
		return any;
	}

	static int CompareCommand(string snap, bool quick)
	{
		Manifest m = LoadManifest(snap);
		Console.WriteLine($"Comparing with the snapshot from {m.Created}{(quick ? ", trusting size and date" : "")}...");
		return Report(Diff(m, snap, quick), snap, "compare") ? 1 : 0;
	}

	// ---------------------------------------------------------------- restore

	static int Restore(string snap, bool quick)
	{
		Manifest m = LoadManifest(snap);
		RefuseIfRunning(m.Plan);
		Console.WriteLine($"Comparing with the snapshot from {m.Created}...");
		Comparison c = Diff(m, snap, quick);
		if (!Report(c, snap, "before-restore"))
		{
			Console.WriteLine("Nothing to put back.");
			return 0;
		}

		string aside = Path.Combine(snap, "set-aside", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
		var problems = new List<string>();
		int putBack = 0, setAside = 0;

		foreach (AreaDiff d in c.Areas)
		{
			string root = d.State.Path;
			string areaAside = Path.Combine(aside, string.Concat(d.State.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)));
			string Full(string rel) => Path.Combine(root, rel);
			void Try(string what, Action act)
			{
				try { act(); }
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
				{
					problems.Add($"{d.State.Name}: {what} ({ex.Message})");
				}
			}

			if (!d.State.Existed)
			{
				if (d.ExistsNow) Try("setting aside the folder that wasn't there", () => { SetAsideTree(root, areaAside); setAside++; });
				continue;
			}
			Directory.CreateDirectory(root);

			// Read a kept setting's newest value before its file is moved out of the way.
			var keptLive = new Dictionary<string, JsonObject?>(Ci);
			foreach (string rel in d.Changed)
				if (Rule(m, d.State.Name, rel) != null) keptLive[rel] = ReadJson(Full(rel));

			foreach (string rel in d.Added.Concat(d.Changed))
				Try($"setting aside {rel}", () => { SetAsideFile(Full(rel), Path.Combine(areaAside, rel)); setAside++; });
			foreach (string rel in d.LinksAdded.Concat(d.LinksChanged))
				Try($"setting aside the link {rel}", () => { SetAsideLink(Full(rel), Path.Combine(areaAside, rel)); setAside++; });
			foreach (string rel in d.DirsAdded.OrderByDescending(r => r.Length))
				Try($"taking away the new folder {rel}", () => Directory.Delete(Full(rel), false));
			foreach (string rel in d.State.Dirs)
				Try($"making the folder {rel}", () => Directory.CreateDirectory(Full(rel)));

			var then = d.State.Files.ToDictionary(f => f.Rel, Ci);
			foreach (string rel in d.Removed.Concat(d.Changed))
			{
				FileState old = then[rel];
				Try($"putting back {rel}", () =>
				{
					if (Rule(m, d.State.Name, rel) is { } rule && keptLive.TryGetValue(rel, out JsonObject? live) && live != null)
						PutBackKeeping(snap, old, Full(old.Rel), live, rule);
					else PutBack(StorePath(snap, old.Hash), Full(old.Rel), old);
					putBack++;
				});
			}
			foreach (string rel in d.Touched)
				Try($"putting back the date of {rel}", () => { SetMeta(Full(rel), then[rel]); putBack++; });

			var thenLinks = d.State.Links.ToDictionary(l => l.Rel, Ci);
			foreach (string rel in d.LinksRemoved.Concat(d.LinksChanged))
			{
				LinkState l = thenLinks[rel];
				Try($"putting back the link {rel}", () =>
				{
					if (l.Dir) Directory.CreateSymbolicLink(Full(rel), l.Target);
					else File.CreateSymbolicLink(Full(rel), l.Target);
					putBack++;
				});
			}
		}

		int relinked = Relink(m, problems);

		for (int i = 0; i < c.Registry.Count; i++)
		{
			var (r, differs) = c.Registry[i];
			if (!differs) continue;
			try
			{
				if (RegExists(r.Key))
				{
					Directory.CreateDirectory(aside);
					Reg("export", r.Key, Path.Combine(aside, $"registry-{i}.reg"), "/y");
					if (Reg("delete", r.Key, "/f") != 0) throw new IOException("reg delete failed");
				}
				if (r.Existed && Reg("import", Path.Combine(snap, r.File)) != 0) throw new IOException("reg import failed");
				putBack++;
			}
			catch (IOException ex) { problems.Add($"Registry key {r.Key}: {ex.Message}"); }
		}

		Console.WriteLine();
		Console.WriteLine($"Put back {putBack:N0}, relinked {relinked:N0}, set aside {setAside:N0}"
			+ (setAside > 0 ? $" (in {aside})." : "."));
		foreach (string p in problems) Console.WriteLine("Problem: " + p);

		Console.WriteLine();
		Console.WriteLine("Checking again...");
		bool left = Report(Diff(m, snap, false), snap, "after-restore");
		return left || problems.Count > 0 ? 1 : 0;
	}

	static void SetMeta(string path, FileState old)
	{
		var info = new FileInfo(path);
		if (info.IsReadOnly) info.IsReadOnly = false;
		File.SetCreationTimeUtc(path, new DateTime(old.Ctime, DateTimeKind.Utc));
		File.SetLastWriteTimeUtc(path, new DateTime(old.Mtime, DateTimeKind.Utc));
		File.SetAttributes(path, (File.GetAttributes(path) & ~Compared) | (FileAttributes)old.Attr);
	}

	/// <summary>A new file in place of the old, never writing into one: a file can be a hard link, and writing into it changes every name it has.</summary>
	static void PutBack(string stored, string target, FileState old)
	{
		string temp = target + ".kinetix-restore";
		try
		{
			File.Copy(stored, temp, true);
			SetMeta(temp, old);
			File.Move(temp, target);
		}
		catch
		{
			try { if (File.Exists(temp)) { File.SetAttributes(temp, FileAttributes.Normal); File.Delete(temp); } } catch (IOException) { }
			throw;
		}
	}

	static void PutBackKeeping(string snap, FileState old, string target, JsonObject live, KeepNewest rule)
	{
		JsonObject? before = ReadJson(StorePath(snap, old.Hash));
		if (before == null) { PutBack(StorePath(snap, old.Hash), target, old); return; }

		var merged = (JsonObject)before.DeepClone();
		foreach (string p in rule.Properties)
		{
			if (live.TryGetPropertyValue(p, out JsonNode? value)) merged[p] = value?.DeepClone();
			else merged.Remove(p);
		}
		if (JsonNode.DeepEquals(merged, before)) { PutBack(StorePath(snap, old.Hash), target, old); return; }

		string temp = target + ".kinetix-restore";
		File.WriteAllText(temp, merged.ToJsonString(JsonOptions));
		File.Move(temp, target);
	}

	static string Unique(string path)
	{
		string candidate = path;
		for (int n = 1; File.Exists(candidate) || Directory.Exists(candidate) || File.Exists(candidate + ".link.txt"); n++) candidate = $"{path}.{n}";
		return candidate;
	}

	static void SetAsideFile(string full, string dest)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
		var info = new FileInfo(full);
		if (info.IsReadOnly) info.IsReadOnly = false;
		File.Move(full, Unique(dest));
	}

	static void SetAsideLink(string full, string dest)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
		var info = new FileInfo(full);
		bool dir = (info.Attributes & FileAttributes.Directory) != 0;
		File.WriteAllText(Unique(dest) + ".link.txt", (dir ? "folder link to " : "file link to ") + (info.LinkTarget ?? new DirectoryInfo(full).LinkTarget));
		if (dir) Directory.Delete(full, false);
		else File.Delete(full);
	}

	static void SetAsideTree(string root, string dest)
	{
		LiveArea live = Scan(root, new List<string>());
		foreach (LiveFile f in live.Files.Values) SetAsideFile(f.Full, Path.Combine(dest, f.Rel));
		foreach (LinkState l in live.Links.Values) SetAsideLink(Path.Combine(root, l.Rel), Path.Combine(dest, l.Rel));
		foreach (string dir in live.Dirs.OrderByDescending(x => x.Length)) Directory.Delete(Path.Combine(root, dir), false);
		Directory.Delete(root, false);
	}

	/// <summary>
	/// Makes the hard links what they were: files that were one file are one again, and files that were separate are separate.
	/// Without this, a restored mod file would be a second copy instead of a link to the manager's copy of the mod.
	/// </summary>
	static int Relink(Manifest m, List<string> problems)
	{
		var all = m.Areas.Where(a => a.Existed).SelectMany(a => a.Files.Select(f => (full: Path.Combine(a.Path, f.Rel), file: f))).ToList();
		var cur = new Dictionary<string, string>(Ci);
		var members = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		void Note(string full, string id)
		{
			if (cur.TryGetValue(full, out string? was)) members[was].Remove(full);
			cur[full] = id;
			if (!members.TryGetValue(id, out HashSet<string>? set)) members[id] = set = new HashSet<string>(Ci);
			set.Add(full);
		}
		foreach (var (full, _) in all)
			if (TryId(full) is { } id) Note(full, id);

		int changed = 0;
		foreach (var group in all.GroupBy(x => x.file.Id, StringComparer.Ordinal))
		{
			var list = group.Where(x => cur.ContainsKey(x.full)).ToList();
			if (list.Count == 0) continue;
			var names = new HashSet<string>(list.Select(x => x.full), Ci);
			var (leader, leaderState) = list[0];

			try
			{
				if (members[cur[leader]].Any(p => !names.Contains(p)))
				{
					// Linked to a file it wasn't linked to before: give it a file of its own.
					string temp = leader + ".kinetix-restore";
					File.Copy(leader, temp, true);
					var info = new FileInfo(leader);
					if (info.IsReadOnly) info.IsReadOnly = false;
					File.Delete(leader);
					File.Move(temp, leader);
					SetMeta(leader, leaderState);
					Note(leader, TryId(leader) ?? "");
					changed++;
				}

				foreach (var (other, otherState) in list.Skip(1))
				{
					if (cur[other] == cur[leader]) continue;
					if (HashFile(other) != HashFile(leader))
					{
						problems.Add($"{other} was a link of {leader}, but their contents differ, so it was left as it is.");
						continue;
					}
					var info = new FileInfo(other);
					if (info.IsReadOnly) info.IsReadOnly = false;
					File.Delete(other);
					if (!Native.HardLink(other, leader))
					{
						int error = Marshal.GetLastWin32Error();
						File.Copy(leader, other);
						SetMeta(other, otherState);
						problems.Add($"{other} couldn't be linked to {leader} again (error {error}), so it is a copy.");
					}
					Note(other, TryId(other) ?? "");
					changed++;
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				problems.Add($"Relinking {leader}: {ex.Message}");
			}
		}
		return changed;
	}
}

static class Native
{
	[StructLayout(LayoutKind.Sequential)]
	struct FileInformation
	{
		public uint Attributes;
		public uint CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh;
		public uint VolumeSerial;
		public uint SizeHigh, SizeLow;
		public uint Links;
		public uint IndexHigh, IndexLow;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateHardLinkW", ExactSpelling = true)]
	static extern bool CreateHardLink(string newName, string existing, IntPtr security);

	/// <summary>Which file this is on its drive, and how many names it has. Two names with the same id are hard links of one file.</summary>
	public static (string id, int links) Identify(SafeFileHandle handle)
	{
		if (!GetFileInformationByHandle(handle, out FileInformation info))
			throw new IOException("Couldn't identify the file.", Marshal.GetLastWin32Error());
		return ($"{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}", (int)info.Links);
	}

	public static bool HardLink(string newName, string existing) => CreateHardLink(newName, existing, IntPtr.Zero);
}

sealed class Stop(string message) : Exception(message);

sealed class Plan
{
	public List<AreaPlan> Areas = new();
	public List<string> Registry = new();
	public List<KeepNewest> KeepNewest = new();
	public List<string> MustNotRun = new();
}

sealed class AreaPlan
{
	public string Name = "";
	public string Path = "";
	public List<string> Exclude = new();
}

sealed class KeepNewest
{
	public string Area = "";
	public string File = "";
	public List<string> Properties = new();
}

sealed class Manifest
{
	public string Created = "";
	public Plan Plan = new();
	public List<AreaState> Areas = new();
	public List<RegState> Registry = new();
	public List<string> Unreadable = new();
}

sealed class AreaState
{
	public string Name = "";
	public string Path = "";
	public bool Existed;
	public List<string> Dirs = new();
	public List<FileState> Files = new();
	public List<LinkState> Links = new();
}

sealed class FileState
{
	public string Rel = "";
	public long Size, Mtime, Ctime;
	public int Attr;
	public string Hash = "";
	public string Id = "";
	public int Links;
}

sealed class LinkState
{
	public string Rel = "";
	public bool Dir;
	public string Target = "";
}

sealed class RegState
{
	public string Key = "";
	public bool Existed;
	public string File = "";
}

sealed class LiveFile
{
	public string Rel = "", Full = "", Id = "", Hash = "";
	public long Size, Mtime, Ctime;
	public int Attr, Links;
}

sealed class LiveArea
{
	public Dictionary<string, LiveFile> Files = new(StringComparer.OrdinalIgnoreCase);
	public HashSet<string> Dirs = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, LinkState> Links = new(StringComparer.OrdinalIgnoreCase);
	public List<string> Unreadable = new();
}

sealed class AreaDiff(AreaState state)
{
	public AreaState State = state;
	public LiveArea Live = new();
	public bool ExistsNow;
	public List<string> Added = new(), Removed = new(), Changed = new(), Kept = new(), Touched = new(), Relinked = new();
	public List<string> DirsAdded = new(), DirsRemoved = new(), LinksAdded = new(), LinksRemoved = new(), LinksChanged = new(), Unchecked = new();
}

sealed class Comparison
{
	public List<AreaDiff> Areas = new();
	public List<(RegState r, bool differs)> Registry = new();
}
