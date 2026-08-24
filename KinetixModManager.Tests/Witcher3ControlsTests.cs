using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers the grouping that makes The Witcher 3's bindings readable.
///
/// The case that matters is the one the game actually produces: the interact key carries seventy-five actions
/// because the game binds every interaction verb it has to it, and it declares the same bindings again in every
/// context they apply in. Gathered by key alone that is one seventy-five-item sentence; what these tests pin
/// down is that the situation each binding came from survives the read, and is what the list is built on.
/// </summary>
public class Witcher3ControlsTests
{
    private static Witcher3Binding Bind(string key, string action, string context, string mod = "") =>
        new() { Key = key, Action = action, Context = context, ModName = mod };

    // ---------------------------------------------------------------------
    // Naming the situations
    // ---------------------------------------------------------------------

    [Fact]
    public void AContextIsNamedAfterWhatThePlayerIsDoing()
    {
        Assert.Equal("Exploring on foot", Witcher3Controls.SituationFor("Exploration"));
        Assert.Equal("On horseback", Witcher3Controls.SituationFor("Horse"));
        Assert.Equal("Casting signs", Witcher3Controls.SituationFor("BASE_Signs"));
    }

    [Fact]
    public void ContextsTheGameSplitsForItsOwnReasonsFoldIntoOneSituation()
    {
        // Six attack contexts and three interaction ones are engine bookkeeping — a player knows neither
        // BASE_ATTACKS_NO_LIGHT nor why it is separate from BASE_ATTACK_HEAVY.
        Assert.Equal("Attacking", Witcher3Controls.SituationFor("BASE_ALL_ATTACKS"));
        Assert.Equal("Attacking", Witcher3Controls.SituationFor("BASE_SPECIAL_ATTACK_LIGHT"));

        Assert.Equal("Interacting with things", Witcher3Controls.SituationFor("BASE_Interactions"));
        Assert.Equal("Interacting with things", Witcher3Controls.SituationFor("BASE_INTERACTIONS_KEYBOARD"));
    }

    [Fact]
    public void TheEnginesOwnScaffoldingIsNotASituation()
    {
        // These are not places a player is ever in, and a row named after one cannot be acted on.
        Assert.Equal("", Witcher3Controls.SituationFor("EMPTY_CONTEXT"));
        Assert.Equal("", Witcher3Controls.SituationFor("SCENE_IS_STARTING_HACK"));
        Assert.Equal("", Witcher3Controls.SituationFor("BASE_DEBUG"));
        Assert.Equal("", Witcher3Controls.SituationFor("FakeAxisInput"));
    }

    [Fact]
    public void AnUnknownContextIsTidiedRatherThanDropped()
    {
        // A mod may add its own context. A binding under an odd heading is findable; a binding that is not
        // listed at all is not.
        Assert.Equal("Some New Context", Witcher3Controls.SituationFor("SomeNewContext"));
        Assert.Equal("Grapple Hook", Witcher3Controls.SituationFor("Grapple_Hook"));
    }

    [Fact]
    public void AHiddenContextsBindingsAreLeftOutOfBothViews()
    {
        var bindings = new List<Witcher3Binding>
        {
            Bind("E", "Open", "Exploration"),
            Bind("Numpad 1", "Debug Teleport", "BASE_DEBUG"),
        };

        Assert.DoesNotContain(Witcher3Controls.BySituation(bindings).SelectMany(s => s.Keys), k => k.Key == "Numpad 1");
        Assert.DoesNotContain(Witcher3Controls.ByKey(bindings), k => k.Key == "Numpad 1");
    }

    // ---------------------------------------------------------------------
    // By situation
    // ---------------------------------------------------------------------

    [Fact]
    public void KeysAreGroupedUnderTheSituationTheyApplyIn()
    {
        var bindings = new List<Witcher3Binding>
        {
            Bind("E", "Mount Horse", "Exploration"),
            Bind("F", "Dismount", "Horse"),
            Bind("Left Shift", "Gallop", "Horse"),
        };

        List<Witcher3Situation> grouped = Witcher3Controls.BySituation(bindings);

        Assert.Equal(new[] { "Exploring on foot", "On horseback" }, grouped.Select(s => s.Name));
        Assert.Equal(new[] { "F", "Left Shift" }, grouped[1].Keys.Select(k => k.Key));
        Assert.Equal(2, grouped[1].Count);
    }

    [Fact]
    public void SituationsComeInTheOrderAPlayerMeetsThem()
    {
        // Not alphabetical: "After dying" and "Attacking" would lead the list, and the keys someone needs
        // first would be somewhere in the middle of it.
        var bindings = new List<Witcher3Binding>
        {
            Bind("P", "Photo Mode", "Photomode"),
            Bind("W", "Move Forward", "BASE_CharacterMovement"),
            Bind("Left Mouse Button", "Attack", "BASE_ALL_ATTACKS"),
        };

        Assert.Equal(
            new[] { "Moving around", "Attacking", "Photo mode" },
            Witcher3Controls.BySituation(bindings).Select(s => s.Name));
    }

    [Fact]
    public void OneBindingDeclaredInSeveralContextsOfTheSameSituationIsListedOnce()
    {
        // The game declares its interaction bindings in both BASE_Interactions and BASE_INTERACTIONS_KEYBOARD.
        // Both fold to one situation, and saying "Open" twice under E there would be noise, not information.
        var bindings = new List<Witcher3Binding>
        {
            Bind("E", "Open", "BASE_Interactions"),
            Bind("E", "Open", "BASE_INTERACTIONS_KEYBOARD"),
            Bind("E", "Take", "BASE_Interactions"),
        };

        Witcher3KeyControls key = Assert.Single(Witcher3Controls.BySituation(bindings)[0].Keys);
        Assert.Equal(new[] { "Open", "Take" }, key.Bindings.Select(b => b.Action));
    }

    [Fact]
    public void TheSameKeyInTwoSituationsStaysTwoEntries()
    {
        // This is the difference the flat list destroyed: E opens a door on foot and dismounts on a horse, and
        // which one it does depends on where you are.
        var bindings = new List<Witcher3Binding>
        {
            Bind("E", "Open", "Exploration"),
            Bind("E", "Dismount", "Horse"),
        };

        List<Witcher3Situation> grouped = Witcher3Controls.BySituation(bindings);

        Assert.Equal(2, grouped.Count);
        Assert.Equal("Open", grouped[0].Keys.Single().Bindings.Single().Action);
        Assert.Equal("Dismount", grouped[1].Keys.Single().Bindings.Single().Action);
    }

    [Fact]
    public void ABindingCopiedOutOfASharedBlockIsListedUnderTheSharedHeadingOnly()
    {
        // The Witcher 3 does not reference its shared blocks, it copies them: all seventy-five interaction
        // bindings are written again under Exploration, Combat, Swimming, Diving and both Ciri contexts.
        // Repeating them in each place buries whatever is actually particular to swimming.
        var bindings = new List<Witcher3Binding>
        {
            Bind("E", "Open", "BASE_Interactions"),
            Bind("E", "Open", "Exploration"),
            Bind("E", "Open", "Swimming"),
            Bind("Spacebar", "Dive", "Swimming"),
        };

        List<Witcher3Situation> grouped = Witcher3Controls.BySituation(bindings);

        Assert.Equal("Open", grouped.Single(s => s.Name == "Interacting with things").Keys.Single().Bindings.Single().Action);
        // What is left under Swimming is what is true of swimming and nowhere else.
        Assert.Equal("Dive", grouped.Single(s => s.Name == "Swimming").Keys.Single().Bindings.Single().Action);
        Assert.DoesNotContain(grouped, s => s.Name == "Exploring on foot");
    }

    // ---------------------------------------------------------------------
    // By key
    // ---------------------------------------------------------------------

    [Fact]
    public void OneThingAKeyDoesIsOneLineHoweverManyContextsCopyIt()
    {
        // Counting declarations instead is what made the interact key read as "454 things it does" against the
        // real file: seventy-five interactions, written out again in each of six contexts that inherit them.
        var bindings = new List<Witcher3Binding>
        {
            Bind("E", "Open", "BASE_Interactions"),
            Bind("E", "Open", "BASE_INTERACTIONS_KEYBOARD"),
            Bind("E", "Open", "Exploration"),
            Bind("E", "Open", "Combat"),
            Bind("E", "Open", "Swimming"),
        };

        Witcher3Binding only = Assert.Single(Assert.Single(Witcher3Controls.ByKey(bindings)).Bindings);

        // And it is named after the general heading, not after whichever context happened to be read first.
        Assert.Equal("Interacting with things", Witcher3Controls.SituationFor(only.Context));
    }

    [Fact]
    public void EveryKeyAppearsOnceWithEachThingItDoes()
    {
        var bindings = new List<Witcher3Binding>
        {
            Bind("E", "Open", "Exploration"),
            Bind("E", "Dismount", "Horse"),
            Bind("W", "Move Forward", "BASE_CharacterMovement"),
        };

        List<Witcher3KeyControls> keys = Witcher3Controls.ByKey(bindings);

        Assert.Equal(new[] { "E", "W" }, keys.Select(k => k.Key));
        Assert.Equal(new[] { "Open", "Dismount" }, keys[0].Bindings.Select(b => b.Action));
    }

    [Fact]
    public void AKeysActionsAreOrderedBySituationSoRelatedOnesReadTogether()
    {
        var bindings = new List<Witcher3Binding>
        {
            Bind("E", "Photo Thing", "Photomode"),
            Bind("E", "Open", "Exploration"),
            Bind("E", "Dismount", "Horse"),
        };

        Assert.Equal(
            new[] { "Open", "Dismount", "Photo Thing" },
            Witcher3Controls.ByKey(bindings).Single().Bindings.Select(b => b.Action));
    }

    [Fact]
    public void FunctionKeysSortByNumberRatherThanBySpelling()
    {
        // A list found by typing its first letter is only usable if F10 is where a reader expects it.
        var bindings = new List<Witcher3Binding>
        {
            Bind("F10", "Ten", "Exploration"),
            Bind("F2", "Two", "Exploration"),
            Bind("F1", "One", "Exploration"),
        };

        Assert.Equal(new[] { "F1", "F2", "F10" }, Witcher3Controls.ByKey(bindings).Select(k => k.Key));
    }

    [Fact]
    public void TheSameActionInTwoSituationsIsKeptTwice()
    {
        // Two lines that read alike are the honest answer here: the key does that in both places, and dropping
        // one would put the remaining line under whichever situation happened to be read first.
        var bindings = new List<Witcher3Binding>
        {
            Bind("Escape", "Close", "FastMenu"),
            Bind("Escape", "Close", "Photomode"),
        };

        Assert.Equal(2, Witcher3Controls.ByKey(bindings).Single().Bindings.Count);
    }

    // ---------------------------------------------------------------------
    // A mod's own bindings
    // ---------------------------------------------------------------------

    [Fact]
    public void AModsBindingsAreListedOnceEachAndKeptOutOfTheGamesViews()
    {
        var bindings = new List<Witcher3Binding>
        {
            Bind("Home", "Toggle Hud", "Exploration"),
            Bind("Home", "Announce", "Exploration", "Witcher Access"),
            Bind("Home", "Announce", "Combat", "Witcher Access"),
            Bind("F6", "Keys Toggle", "Exploration", "Witcher Access"),
        };

        List<Witcher3Binding> mod = Witcher3Controls.ForMod(bindings, "Witcher Access");

        // Declared in two contexts, listed once: a mod's action means the same thing wherever it is bound.
        Assert.Equal(new[] { "Keys Toggle", "Announce" }, mod.Select(b => b.Action));
        Assert.Equal(new[] { "F6", "Home" }, mod.Select(b => b.Key));
    }

    [Fact]
    public void TheGamesOwnViewsAreBuiltFromTheGamesOwnBindings()
    {
        // The caller filters, and this is the shape it relies on: a mod's actions carry its name, so they can
        // be told from the game's without guessing.
        var bindings = new List<Witcher3Binding>
        {
            Bind("Home", "Toggle Hud", "Exploration"),
            Bind("Home", "Announce", "Exploration", "Witcher Access"),
        };

        List<Witcher3Binding> gameOwn = bindings.Where(b => b.ModName.Length == 0).ToList();

        Assert.Equal("Toggle Hud", Witcher3Controls.ByKey(gameOwn).Single().Bindings.Single().Action);
    }

    [Fact]
    public void ModsAreListedInTheOrderTheyAreFirstMet()
    {
        var bindings = new List<Witcher3Binding>
        {
            Bind("N", "Compass", "Exploration", "Witcher Access"),
            Bind("K", "Encounter", "Exploration", "Random Encounters"),
            Bind("Home", "Announce", "Combat", "Witcher Access"),
        };

        Assert.Equal(new[] { "Witcher Access", "Random Encounters" }, Witcher3Controls.ModsWithBindings(bindings));
    }

    [Fact]
    public void AGameWithNoModsOwnsEveryBinding()
    {
        var bindings = new List<Witcher3Binding> { Bind("E", "Open", "Exploration") };

        Assert.Empty(Witcher3Controls.ModsWithBindings(bindings));
    }
}
