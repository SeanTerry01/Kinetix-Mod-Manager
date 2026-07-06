using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="IniDocument"/> — the parse/edit model behind the accessible INI editor. The load-bearing
/// promise is that editing one setting leaves every other line (comments, blanks, order, unrelated keys) untouched.
/// </summary>
public class IniDocumentTests
{
    private static IniDocument Doc(params string[] lines) => new IniDocument(lines);

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

        Assert.Equal(new[] { "; keep me", "[Display]", "iSizeW=2560", "iSizeH=1080" }, doc.Lines.ToArray());
    }

    [Fact]
    public void SetValue_IsCaseInsensitive_OnSectionAndKey()
    {
        var doc = Doc("[Display]", "iSizeW=1920");
        doc.SetValue("display", "isizew", "800");
        Assert.Equal("800", doc.GetValue("Display", "iSizeW"));
        Assert.Single(doc.Entries()); // replaced, not duplicated
    }

    [Fact]
    public void SetValue_AddsKeyToExistingSection_AtEndOfThatSection()
    {
        var doc = Doc("[Display]", "iSizeW=1920", "[General]", "sLanguage=ENGLISH");

        doc.SetValue("Display", "bFull Screen", "0");

        Assert.Equal(
            new[] { "[Display]", "iSizeW=1920", "bFull Screen=0", "[General]", "sLanguage=ENGLISH" },
            doc.Lines.ToArray());
    }

    [Fact]
    public void SetValue_CreatesSection_WhenAbsent()
    {
        var doc = Doc("[Display]", "iSizeW=1920");

        doc.SetValue("Archive", "bInvalidateOlderFiles", "1");

        Assert.Equal("1", doc.GetValue("Archive", "bInvalidateOlderFiles"));
        Assert.Contains("[Archive]", doc.Lines);
        Assert.Contains("bInvalidateOlderFiles=1", doc.Lines);
    }

    [Fact]
    public void RemoveKey_DropsOnlyThatKey()
    {
        var doc = Doc("[Display]", "iSizeW=1920", "iSizeH=1080");

        doc.RemoveKey("Display", "iSizeW");

        Assert.Equal(new[] { "[Display]", "iSizeH=1080" }, doc.Lines.ToArray());
        Assert.Null(doc.GetValue("Display", "iSizeW"));
    }

    [Fact]
    public void GetValue_ScopedToSection_NotAcrossSections()
    {
        var doc = Doc("[A]", "x=1", "[B]", "x=2");
        Assert.Equal("1", doc.GetValue("A", "x"));
        Assert.Equal("2", doc.GetValue("B", "x"));
        Assert.Null(doc.GetValue("C", "x"));
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
