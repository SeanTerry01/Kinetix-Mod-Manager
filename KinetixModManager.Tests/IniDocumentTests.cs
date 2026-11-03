using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="IniDocument"/> — the parse/edit model behind the accessible INI editor. The load-bearing
/// promise is that editing one setting leaves every other line (comments, blanks, order, unrelated keys) untouched.
/// </summary>
public class IniDocumentTests
{
    private static IniDocument Doc(params string[] lines) => new IniDocument(lines);

    /// <summary>The lines a save leaves on disk, which is what the promise about untouched lines is a promise about.</summary>
    private static string[] Saved(IniDocument doc)
    {
        string path = Path.Combine(Path.GetTempPath(), "KMM_Ini_" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            doc.Save(path);
            return File.ReadAllLines(path);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Entries_ParsesSectionsKeysAndValues_SkippingCommentsAndBlanks()
    {
        var doc = Doc(
            "; a comment",
            "[Display]",
            "iSizeW=1920",
            "iSizeH = 1080",
            "",
            "# another comment",
            "[General]",
            "sLanguage=ENGLISH");

        var entries = doc.Entries();

        Assert.Equal(3, entries.Count);
        Assert.Equal(("Display", "iSizeW", "1920"), (entries[0].Section, entries[0].Key, entries[0].Value));
        Assert.Equal(("Display", "iSizeH", "1080"), (entries[1].Section, entries[1].Key, entries[1].Value)); // trimmed
        Assert.Equal(("General", "sLanguage", "ENGLISH"), (entries[2].Section, entries[2].Key, entries[2].Value));
    }

    [Fact]
    public void SetValue_ReplacesInPlace_AndPreservesEverythingElse()
    {
        var doc = Doc(
            "; keep me",
            "[Display]",
            "iSizeW=1920",
            "iSizeH=1080");

        doc.SetValue("Display", "iSizeW", "2560");

        Assert.Equal(new[] { "; keep me", "[Display]", "iSizeW=2560", "iSizeH=1080" }, Saved(doc));
    }

    [Fact]
    public void SetValue_IsCaseInsensitive_OnSectionAndKey()
    {
        var doc = Doc("[Display]", "iSizeW=1920");
        doc.SetValue("display", "isizew", "800");
        var entry = Assert.Single(doc.Entries()); // replaced, not duplicated
        Assert.Equal("Display", entry.Section);
        Assert.Equal("800", entry.Value);
    }

    [Fact]
    public void SetValue_AddsKeyToExistingSection_AtEndOfThatSection()
    {
        var doc = Doc("[Display]", "iSizeW=1920", "[General]", "sLanguage=ENGLISH");

        doc.SetValue("Display", "bFull Screen", "0");

        Assert.Equal(
            new[] { "[Display]", "iSizeW=1920", "bFull Screen=0", "[General]", "sLanguage=ENGLISH" },
            Saved(doc));
    }

    [Fact]
    public void SetValue_CreatesSection_WhenAbsent()
    {
        var doc = Doc("[Display]", "iSizeW=1920");

        doc.SetValue("Archive", "bInvalidateOlderFiles", "1");

        Assert.Contains(doc.Entries(), e => e.Section == "Archive" && e.Key == "bInvalidateOlderFiles" && e.Value == "1");
        Assert.Contains("[Archive]", Saved(doc));
        Assert.Contains("bInvalidateOlderFiles=1", Saved(doc));
    }

    [Fact]
    public void RemoveKey_DropsOnlyThatKey()
    {
        var doc = Doc("[Display]", "iSizeW=1920", "iSizeH=1080");

        doc.RemoveKey("Display", "iSizeW");

        Assert.Equal(new[] { "[Display]", "iSizeH=1080" }, Saved(doc));
        Assert.DoesNotContain(doc.Entries(), e => e.Key == "iSizeW");
    }

    [Fact]
    public void Entries_KeepTheSameKeyApartInEachSection()
    {
        var entries = Doc("[A]", "x=1", "[B]", "x=2").Entries();

        Assert.Equal(2, entries.Count);
        Assert.Equal(("A", "x", "1"), (entries[0].Section, entries[0].Key, entries[0].Value));
        Assert.Equal(("B", "x", "2"), (entries[1].Section, entries[1].Key, entries[1].Value));
    }

    [Fact]
    public void EmptyValueIsPreserved_ForKeysLikeResourceDirs()
    {
        var doc = Doc("[Archive]", "sResourceDataDirsFinal=");
        var entry = Assert.Single(doc.Entries());
        Assert.Equal("sResourceDataDirsFinal", entry.Key);
        Assert.Equal("", entry.Value);
    }
}
