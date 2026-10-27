using System.Text.Json;
using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public class WeekendConfigurationTests
{
    private static WeekendConfiguration Valid() => new(45, 2, 1, 12, "09:30", new()
    {
        Practice = new("skip", 60), Qualifying = new("private", 15, "08:00"),
    });

    [Fact]
    public void Settings_update_the_round_matched_against_XML_without_changing_event_or_points()
    {
        var round = new Round { TrackFolder = "Qatar_2024", LayoutFile = "layoutQatar", PointsWeight = 2, RaceMinutes = 30 };
        Valid().Apply(round, "GT3");
        Assert.Equal(45, round.RaceMinutes);
        Assert.Equal(2, round.FuelMultiplier);
        Assert.Equal(1, round.TireMultiplier);
        Assert.Equal(12, round.TimeScale);
        Assert.Equal("09:30", round.StartTime);
        Assert.Equal("private", round.WeekendSessions!.Qualifying.Mode);
        Assert.Equal("Qatar_2024", round.TrackFolder);
        Assert.Equal(2, round.PointsWeight);
        Assert.True(round.Stints >= 1);
    }

    [Theory]
    [InlineData(RoundState.Armed)]
    [InlineData(RoundState.Completed)]
    [InlineData(RoundState.Skipped)]
    public void Active_and_past_rounds_are_immutable(RoundState state)
    {
        var round = new Round { State = state, RaceMinutes = 30 };
        Assert.Throws<InvalidOperationException>(() => Valid().Apply(round, "GT3"));
        Assert.Equal(30, round.RaceMinutes);
        Assert.Null(round.WeekendSessions);
    }

    [Fact]
    public void Invalid_values_fail_atomically()
    {
        var invalid = new[]
        {
            Valid() with { RaceMinutes = 31 }, Valid() with { FuelUsage = 4 }, Valid() with { TyreWear = 0 },
            Valid() with { TimeScale = 61 }, Valid() with { StartTime = "24:00" }, Valid() with { Sessions = null! },
            Valid() with { Sessions = new() { Practice = new("private", 0) } },
        };
        foreach (var configuration in invalid)
        {
            var round = new Round { RaceMinutes = 30 };
            var before = JsonSerializer.Serialize(round);
            Assert.Throws<ArgumentException>(() => configuration.Apply(round, "GT3"));
            Assert.Equal(before, JsonSerializer.Serialize(round));
        }
    }

    [Fact]
    public void Old_rounds_without_session_settings_still_deserialize()
    {
        var round = JsonSerializer.Deserialize<Round>("""{"RaceMinutes":30}""")!;
        Assert.Null(round.WeekendSessions);
        Assert.Equal(30, round.RaceMinutes);
    }
}
