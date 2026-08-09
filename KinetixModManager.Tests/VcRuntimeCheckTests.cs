using System;
using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="VcRuntimeCheck"/>, whose leading case is recorded from the machine that prompted it rather
/// than invented.
///
/// On 2026-08-08 a Skyrim install stopped loading Skyrim Access (SKSE reported error 1114, a failed DLL
/// initialisation) and Sound Record Distributor, while every other plugin loaded. The Witcher 3's access mod had
/// gone completely silent at the same time with no error at all. The cause was one set of files in System32:
/// msvcp140, vcruntime140 and vcruntime140_1 were stranded at 14.28.29910 from 2023 while every other file of the
/// same redistributable had moved on to 14.51.36247. Windows' uninstall list reported only the newer version, so
/// nothing about the machine looked wrong.
///
/// That shape — a few core files behind their own satellites — is what these tests pin down, along with the two
/// ways the check could cry wolf: a coherent older install, and a developer machine carrying debug and CLR
/// variants that legitimately differ.
/// </summary>
public class VcRuntimeCheckTests
{
    private static VcRuntimeFile F(string name, string? version) =>
        new VcRuntimeFile { Name = name, Version = version == null ? null : Version.Parse(version) };

    /// <summary>The exact version set read out of System32 before the repair.</summary>
    private static List<VcRuntimeFile> TheBrokenMachine() => new()
    {
        F("msvcp140.dll",              "14.28.29910"),
        F("vcruntime140.dll",          "14.28.29910"),
        F("vcruntime140_1.dll",        "14.28.29910"),
        F("msvcp140_1.dll",            "14.51.36247"),
        F("msvcp140_2.dll",            "14.51.36247"),
        F("msvcp140_atomic_wait.dll",  "14.51.36247"),
        F("msvcp140_codecvt_ids.dll",  "14.51.36247"),
        F("vcruntime140_threads.dll",  "14.51.36247"),
        F("concrt140.dll",             "14.51.36247")
    };

    /// <summary>The same machine after the repair, which is what a healthy install looks like.</summary>
    private static List<VcRuntimeFile> TheRepairedMachine() =>
        TheBrokenMachine().Select(f => F(f.Name, "14.51.36247")).ToList();

    // -------------------------------------------------------------------------
    // The incident
    // -------------------------------------------------------------------------

    [Fact]
    public void Flags_the_mixed_runtime_that_silenced_the_access_mods()
    {
        VcRuntimeVerdict v = VcRuntimeCheck.Evaluate(TheBrokenMachine());

        Assert.False(v.IsConsistent);
        Assert.Equal(new Version(14, 51, 36247), v.Expected);
        Assert.Equal(
            new[] { "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll" },
            v.Stale.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Reports_the_repaired_machine_as_healthy()
    {
        Assert.True(VcRuntimeCheck.Evaluate(TheRepairedMachine()).IsConsistent);
    }

    [Fact]
    public void Does_not_report_the_core_as_missing_when_it_is_merely_stale()
    {
        // The distinction matters to which sentence the user hears: "your files do not match" is a repair,
        // "it is not installed" is an install.
        Assert.False(VcRuntimeCheck.Evaluate(TheBrokenMachine()).CoreMissing);
    }

    // -------------------------------------------------------------------------
    // Not crying wolf
    // -------------------------------------------------------------------------

    [Fact]
    public void Accepts_an_older_install_whose_files_all_agree()
    {
        // 14.16 predates msvcp140_atomic_wait.dll entirely. Absent is not stale.
        var files = new List<VcRuntimeFile>
        {
            F("msvcp140.dll",             "14.16.27033"),
            F("msvcp140_1.dll",           "14.16.27033"),
            F("vcruntime140.dll",         "14.16.27033"),
            F("msvcp140_atomic_wait.dll", null),
            F("vcruntime140_1.dll",       null),
            F("vcruntime140_threads.dll", null)
        };

        Assert.True(VcRuntimeCheck.Evaluate(files).IsConsistent);
    }

    [Fact]
    public void Ignores_the_revision_component()
    {
        // Files from one redistributable share major.minor.build; the fourth part carries no meaning here and
        // comparing it would invent mismatches.
        var files = new List<VcRuntimeFile>
        {
            new VcRuntimeFile { Name = "msvcp140.dll",     Version = new Version(14, 51, 36247, 0) },
            new VcRuntimeFile { Name = "vcruntime140.dll", Version = new Version(14, 51, 36247, 3) }
        };

        Assert.True(VcRuntimeCheck.Evaluate(files).IsConsistent);
    }

    [Fact]
    public void Never_examines_the_clr_or_debug_variants()
    {
        // A developer machine carries msvcp140_clr0400.dll at 14.29 and msvcp140d.dll alongside the real files.
        // Both are legitimately different versions, so the file list must not name them at all — otherwise the
        // check reports a fault on every machine that has Visual Studio installed, including the author's.
        Assert.DoesNotContain(VcRuntimeCheck.RuntimeFileNames,
            n => n.Contains("clr", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(VcRuntimeCheck.RuntimeFileNames,
            n => n.EndsWith("d.dll", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // Nothing there at all
    // -------------------------------------------------------------------------

    [Fact]
    public void Reports_a_missing_runtime_when_nothing_is_installed()
    {
        var files = VcRuntimeCheck.RuntimeFileNames.Select(n => F(n, null)).ToList();

        VcRuntimeVerdict v = VcRuntimeCheck.Evaluate(files);

        Assert.True(v.CoreMissing);
        Assert.False(v.IsConsistent);
    }

    [Fact]
    public void Reports_a_missing_core_even_when_satellites_survive()
    {
        // An uninstall that took msvcp140.dll but left the rest: no native mod loads, so this has to be caught
        // rather than passed as "everything present agrees".
        var files = new List<VcRuntimeFile>
        {
            F("msvcp140.dll",   null),
            F("msvcp140_1.dll", "14.51.36247"),
            F("concrt140.dll",  "14.51.36247")
        };

        VcRuntimeVerdict v = VcRuntimeCheck.Evaluate(files);

        Assert.True(v.CoreMissing);
        Assert.False(v.IsConsistent);
    }

    // -------------------------------------------------------------------------
    // The real machine
    // -------------------------------------------------------------------------

    [Fact]
    public void Inspecting_this_machine_does_not_throw_and_says_something_sensible()
    {
        VcRuntimeVerdict? v = VcRuntimeCheck.Inspect();

        // Null is a legitimate answer (Wine, or an unreadable system folder) and must not fail the suite.
        if (v == null) return;

        // Whatever it found, a verdict has to be self-consistent: stale files imply an expected build to
        // compare them against.
        if (v.Stale.Count > 0) Assert.NotNull(v.Expected);
    }
}
