using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Pins <see cref="FilesPresent"/> against what <see cref="File.Exists(string)"/> says, which is what a deployment pass
/// asked before it, once for every deployed file.
/// </summary>
public class FilesPresentTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "kinetix-present-" + Guid.NewGuid().ToString("N"));

	public FilesPresentTests()
	{
		Directory.CreateDirectory(Path.Combine(_root, "Data", "textures"));
		Directory.CreateDirectory(Path.Combine(_root, "Data", "Folder.dds"));
		File.WriteAllText(Path.Combine(_root, "Data", "textures", "a.dds"), "");
		string hidden = Path.Combine(_root, "Data", "textures", "hidden.dds");
		File.WriteAllText(hidden, "");
		File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
	}

	public void Dispose()
	{
		try
		{
			string hidden = Path.Combine(_root, "Data", "textures", "hidden.dds");
			if (File.Exists(hidden)) File.SetAttributes(hidden, FileAttributes.Normal);
			Directory.Delete(_root, recursive: true);
		}
		catch { }
	}

	[Theory]
	[InlineData("Data/textures/a.dds")]
	[InlineData("Data/textures/hidden.dds")]
	[InlineData("Data/textures/missing.dds")]
	[InlineData("Data/Folder.dds")]
	[InlineData("Data/nofolder/x.dds")]
	[InlineData("Data/textures/A.DDS")]
	public void AnswersAsFileExistsDoes(string relative)
	{
		string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

		Assert.Equal(File.Exists(path), new FilesPresent().Contains(path));
	}

	[Fact]
	public void AFolderIsReadWhenFirstAskedAbout()
	{
		var present = new FilesPresent();
		Assert.True(present.Contains(Path.Combine(_root, "Data", "textures", "a.dds")));

		// A folder not yet asked about is read as it is at that moment, so what an earlier step of the pass did is seen.
		string later = Path.Combine(_root, "Data", "meshes", "b.nif");
		Directory.CreateDirectory(Path.GetDirectoryName(later)!);
		File.WriteAllText(later, "");

		Assert.True(present.Contains(later));
	}
}
