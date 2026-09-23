using Binoc.Core.Changelog;
using Xunit;

namespace Binoc.Tests;

/// <summary>
/// The changelog splitter + "unseen since" range logic that drives the in-app "what's new" popup. Pure and
/// UI-free, so these assert the real rules without an Avalonia window.
/// </summary>
public class ChangelogParserTests
{
    private const string Sample = """
# Changelog

Intro line.

---

## [Unreleased]

---

## [v0.3.0] - 2026-09-23

- Newest thing
- Another new thing

---

## [v0.2.0] - 2026-09-20

- Middle thing

---

## [v0.1.0] - 2026-09-10

- First thing
""";

    [Fact]
    public void Parse_returns_sections_newest_first_including_unreleased()
    {
        var sections = ChangelogParser.Parse(Sample);

        Assert.Equal(4, sections.Count);
        Assert.Equal("[Unreleased]", sections[0].Heading);
        Assert.Null(sections[0].Version);              // unversioned
        Assert.Equal("v0.3.0", sections[1].Display);
        Assert.Equal(new Version(0, 3, 0), sections[1].Version);
        Assert.Equal("v0.1.0", sections[3].Display);
    }

    [Fact]
    public void Parse_trims_trailing_rule_and_blank_lines_from_a_block()
    {
        var sections = ChangelogParser.Parse(Sample);
        var v030 = sections[1].Block;

        // Heading is kept as the first line; the trailing "---" and blank lines are dropped.
        Assert.Equal("## [v0.3.0] - 2026-09-23", v030[0]);
        Assert.Equal("- Another new thing", v030[^1]);
        Assert.DoesNotContain("---", v030);
    }

    [Fact]
    public void UnseenSince_returns_only_versions_between_lastSeen_and_current()
    {
        var unseen = ChangelogParser.UnseenSince(Sample, lastSeen: "0.1.0", current: "0.3.0");

        Assert.Equal(2, unseen.Count);                 // v0.3.0 and v0.2.0, not v0.1.0 (already seen)
        Assert.Equal("v0.3.0", unseen[0].Display);
        Assert.Equal("v0.2.0", unseen[1].Display);
        Assert.DoesNotContain(unseen, s => s.Version is null); // never the Unreleased section
    }

    [Fact]
    public void UnseenSince_is_empty_on_a_fresh_install()
    {
        // No prior version on record -> nothing to diff against, so no popup.
        Assert.Empty(ChangelogParser.UnseenSince(Sample, lastSeen: null, current: "0.3.0"));
        Assert.Empty(ChangelogParser.UnseenSince(Sample, lastSeen: "", current: "0.3.0"));
    }

    [Fact]
    public void UnseenSince_is_empty_when_already_on_the_current_version()
    {
        Assert.Empty(ChangelogParser.UnseenSince(Sample, lastSeen: "0.3.0", current: "0.3.0"));
    }

    [Fact]
    public void UnseenSince_respects_the_current_upper_bound()
    {
        // Updated only to v0.2.0: v0.3.0 exists in the file but hasn't been reached yet.
        var unseen = ChangelogParser.UnseenSince(Sample, lastSeen: "0.1.0", current: "0.2.0");

        Assert.Single(unseen);
        Assert.Equal("v0.2.0", unseen[0].Display);
    }

    [Theory]
    [InlineData("[v0.2.11] - 2026-07-21", true, "v0.2.11")]
    [InlineData("[Unreleased]", false, "[Unreleased]")]
    public void TryParseVersion_reads_the_version_out_of_a_heading(string heading, bool ok, string display)
    {
        bool parsed = ChangelogParser.TryParseVersion(heading, out _, out var actualDisplay);

        Assert.Equal(ok, parsed);
        Assert.Equal(display, actualDisplay);
    }
}
