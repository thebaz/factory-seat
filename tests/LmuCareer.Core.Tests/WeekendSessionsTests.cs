using System.Text.Json.Nodes;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public class WeekendSessionsTests
{
    [Fact]
    public void Defaults_preserve_every_existing_session_setting()
    {
        var options = JsonNode.Parse("""{"practice length":90,"qualifying length":25}""")!.AsObject();
        var conditions = JsonNode.Parse("""{"Run Practice1":true,"Run Practice2":true,"Num Qual Sessions":3,"PrivateQualifying":2}""")!.AsObject();
        var before = conditions.DeepClone();
        var sessions = new WeekendSessions();
        sessions.Apply(options, conditions);
        Assert.True(JsonNode.DeepEquals(before, conditions));
        Assert.Empty(sessions.Changes(options, conditions));
        Assert.Empty(sessions.Checks(options, conditions));
    }

    [Theory]
    [InlineData("other", 30, null)]
    [InlineData("private", 0, null)]
    [InlineData("open", 1441, null)]
    [InlineData("open", 30, "24:00")]
    [InlineData("private", 30, "09:60")]
    public void Invalid_active_options_fail_before_mutating_a_preset(string mode, int minutes, string? time)
    {
        var conditions = new JsonObject();
        var options = new JsonObject();
        var sessions = new WeekendSessions { Practice = new(mode, minutes, time) };
        Assert.Throws<ArgumentException>(() => sessions.Apply(options, conditions));
        Assert.Empty(options);
        Assert.Empty(conditions);
    }

    [Theory]
    [InlineData(1, "00:00", 0)]
    [InlineData(1440, "23:59", 1439)]
    public void Duration_and_start_time_boundaries_are_supported(int minutes, string time, int expectedStart)
    {
        var options = new JsonObject();
        var conditions = new JsonObject();
        var sessions = new WeekendSessions { Practice = new("open", minutes, time) };
        sessions.Apply(options, conditions);
        Assert.Equal(minutes, options["practice length"]!.GetValue<int>());
        Assert.Equal(expectedStart, conditions["Practice1StartingTime"]!.GetValue<int>());
        Assert.All(sessions.Checks(options, conditions), check => Assert.True(check.Matches));
    }

    [Fact]
    public void Missing_readback_data_is_not_treated_as_a_verified_skip()
    {
        var sessions = new WeekendSessions { Practice = new("skip"), Qualifying = new("skip") };
        Assert.All(sessions.Checks(new(), new()), check => Assert.False(check.Matches));
    }
}
