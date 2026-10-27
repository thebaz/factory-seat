using System.Net;
using System.Text.Json.Nodes;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public class LmuWeekendClientTests
{
    private static readonly WeekendTarget Target = new("Qatar_2024", "layoutQatar", "Ford_Mustang_GT3_2024", null, false, [], 30, 3, 3, 20, "11:00");

    [Fact]
    public async Task Preview_does_not_write_and_resolves_exact_owned_content()
    {
        using var game = new Game();
        var plan = await game.Client.PreviewAsync(Target);
        Assert.Equal("qatar", plan.TrackId);
        Assert.Equal("mustang", plan.CarId);
        Assert.Equal(8, plan.Changes.Count);
        Assert.Empty(game.Writes);
    }

    [Fact]
    public async Task Apply_sets_absolute_values_verifies_all_fields_and_never_launches()
    {
        using var game = new Game();
        Assert.All(await game.Client.ApplyAsync(await game.Client.PreviewAsync(Target)), c => Assert.True(c.Matches, c.Key));
        Assert.Equal(30, game.Options["Race Time"]!.GetValue<int>());
        Assert.Equal(2, game.Options["Race Finish Criteria"]!.GetValue<int>());
        Assert.Equal(3, game.Options["Fuel Consumption Multiplier"]!.GetValue<int>());
        Assert.Equal(660, game.Conditions["RaceStartingTime"]!.GetValue<int>());
        Assert.Equal(20, game.Conditions["RaceTimeScale"]!.GetValue<int>());
        Assert.DoesNotContain(game.Writes, p => p.Contains("loadGame") || p.Contains("generateSave") || p.EndsWith("/settings"));
        Assert.Equal(98, game.Options["AI Driver Strength"]!.GetValue<int>());
        Assert.Equal(60, game.Options["practice length"]!.GetValue<int>());
        Assert.Equal(20, game.Options["qualifying length"]!.GetValue<int>());
        Assert.True(game.Conditions["Run Practice1"]!.GetValue<bool>());
        Assert.Equal("opponent", game.Preset["Grid"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Fresh_preview_and_repeat_application_do_not_increment_values()
    {
        using var game = new Game();
        await game.Client.ApplyAsync(await game.Client.PreviewAsync(Target));
        var first = game.Preset.DeepClone();
        game.Writes.Clear();
        await game.Client.ApplyAsync(await game.Client.PreviewAsync(Target));
        Assert.True(JsonNode.DeepEquals(first, game.Preset));
        Assert.Equal(["/rest/sessions/SessionPresets/applyPreset"], game.Writes);
    }

    [Fact]
    public async Task Stale_preview_is_rejected_before_any_write()
    {
        using var game = new Game();
        var plan = await game.Client.PreviewAsync(Target);
        game.Options["AI Driver Strength"] = 100;
        await Assert.ThrowsAsync<InvalidOperationException>(() => game.Client.ApplyAsync(plan));
        Assert.Empty(game.Writes);
    }

    [Fact]
    public async Task Partial_write_failure_does_not_launch()
    {
        using var game = new Game { FailApply = true };
        await Assert.ThrowsAsync<HttpRequestException>(async () => await game.Client.ApplyAsync(await game.Client.PreviewAsync(Target)));
        Assert.Contains("/rest/race/track", game.Writes);
        Assert.DoesNotContain(game.Writes, p => p.EndsWith("loadGame"));
    }

    [Fact]
    public async Task Failed_readback_does_not_claim_a_setting_is_verified()
    {
        using var game = new Game { IgnoreFuel = true };
        var checks = await game.Client.ApplyAsync(await game.Client.PreviewAsync(Target));
        Assert.Contains(checks, c => c.Key == "fuel" && !c.Matches);
        Assert.Contains(checks, c => c.Key == "length" && c.Matches);
    }

    [Theory]
    [InlineData("SETTING_MULTIPLAYER", "GSTATE_SETUP", false)]
    [InlineData("SETTING_GRANDPRIX", "GSTATE_RUNNING", false)]
    [InlineData("SETTING_GRANDPRIX", "GSTATE_SETUP", true)]
    public async Task Online_running_and_loading_sessions_are_not_touched(string mode, string state, bool loading)
    {
        using var game = new Game { Mode = mode, State = state, Loading = loading };
        await Assert.ThrowsAsync<InvalidOperationException>(() => game.Client.PreviewAsync(Target));
        Assert.Empty(game.Writes);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Locked_content_is_rejected(bool trackOwned, bool carOwned)
    {
        using var game = new Game { TrackOwned = trackOwned, CarOwned = carOwned };
        await Assert.ThrowsAsync<InvalidOperationException>(() => game.Client.PreviewAsync(Target));
        Assert.Empty(game.Writes);
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData("layoutQatar", "999")]
    public async Task Unknown_layout_or_unavailable_number_never_falls_back_to_other_content(string layout, string? number)
    {
        using var game = new Game();
        await Assert.ThrowsAsync<InvalidOperationException>(() => game.Client.PreviewAsync(Target with { LayoutFile = layout, Number = number }));
        Assert.Empty(game.Writes);
    }

    [Fact]
    public async Task Default_start_time_is_minus_one_not_midnight()
    {
        using var game = new Game();
        Assert.All(await game.Client.ApplyAsync(await game.Client.PreviewAsync(Target with { StartTime = null })), c => Assert.True(c.Matches));
        Assert.Equal(-1, game.Conditions["RaceStartingTime"]!.GetValue<int>());
    }

    [Fact]
    public async Task Explicit_launch_arms_before_one_load_command()
    {
        using var game = new Game();
        var plan = await game.Client.PreviewAsync(Target);
        await game.Client.ApplyAsync(plan);
        await game.Client.StartAsync(plan, () => game.Writes.Add("ARM"));
        Assert.Equal(["/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset", "ARM", "/rest/sessions/SaveLoad/loadGame"], game.Writes.TakeLast(3));
    }

    [Fact]
    public async Task Launch_rechecks_settings_and_refuses_changes_after_apply()
    {
        using var game = new Game();
        var plan = await game.Client.PreviewAsync(Target);
        await game.Client.ApplyAsync(plan);
        game.Options["Race Time"] = 60;
        await Assert.ThrowsAsync<InvalidOperationException>(() => game.Client.StartAsync(plan, () => throw new Exception("Must not arm")));
        Assert.DoesNotContain(game.Writes, p => p.Contains("generateSave") || p.EndsWith("loadGame"));
    }

    [Fact]
    public async Task Invalid_generated_session_never_arms_or_loads()
    {
        using var game = new Game { InvalidSave = true };
        var plan = await game.Client.PreviewAsync(Target);
        await game.Client.ApplyAsync(plan);
        await Assert.ThrowsAsync<InvalidDataException>(() => game.Client.StartAsync(plan, () => throw new Exception("Must not arm")));
        Assert.DoesNotContain(game.Writes, p => p.EndsWith("loadGame"));
    }

    [Fact]
    public async Task A_timed_out_launch_is_not_retried()
    {
        using var game = new Game { TimeoutLaunch = true };
        var plan = await game.Client.PreviewAsync(Target);
        await game.Client.ApplyAsync(plan);
        var armed = 0;
        await Assert.ThrowsAsync<TaskCanceledException>(() => game.Client.StartAsync(plan, () => armed++));
        Assert.Equal(1, armed);
        Assert.Equal(1, game.Writes.Count(p => p.EndsWith("loadGame")));
    }

    [Fact]
    public async Task Changes_while_generating_a_session_prevent_loading_it()
    {
        using var game = new Game { ChangeDuringGenerate = true };
        var plan = await game.Client.PreviewAsync(Target);
        await game.Client.ApplyAsync(plan);
        await Assert.ThrowsAsync<InvalidOperationException>(() => game.Client.StartAsync(plan, () => throw new Exception("Must not arm")));
        Assert.DoesNotContain(game.Writes, p => p.EndsWith("loadGame"));
    }

    [Fact]
    public void Every_catalog_layout_has_an_explicit_API_mapping()
    {
        foreach (var track in ContentCatalog.Default.Tracks)
            foreach (var layout in track.Layouts.Keys) Assert.NotNull(LmuTrackLayouts.Scene(track.Folder, layout));
    }

    public static IEnumerable<object[]> SessionModes =>
        from practice in new[] { "keep", "skip", "open", "private" }
        from qualifying in new[] { "keep", "skip", "open", "private" }
        select new object[] { practice, qualifying };

    [Theory]
    [MemberData(nameof(SessionModes))]
    public async Task Every_practice_and_qualifying_combination_is_applied_and_read_back(string practice, string qualifying)
    {
        using var game = new Game();
        var sessions = new WeekendSessions
        {
            Practice = new(practice, 45, "08:15"), Qualifying = new(qualifying, 15, null),
        };
        var plan = await game.Client.PreviewAsync(Target with { Sessions = sessions });
        Assert.Equal(8 + (practice == "keep" ? 0 : 1) + (qualifying == "keep" ? 0 : 1), plan.Changes.Count);
        var checks = await game.Client.ApplyAsync(plan);
        Assert.All(checks, c => Assert.True(c.Matches, c.Key));
        if (practice == "keep") Assert.Equal(60, game.Options["practice length"]!.GetValue<int>());
        if (practice is "open" or "private")
        {
            Assert.Equal(45, game.Options["practice length"]!.GetValue<int>());
            Assert.Equal(495, game.Conditions["Practice1StartingTime"]!.GetValue<int>());
            Assert.Equal(practice == "private", game.Conditions["PrivatePractice"]!.GetValue<bool>());
        }
        if (qualifying is "open" or "private")
        {
            Assert.Equal(15, game.Options["qualifying length"]!.GetValue<int>());
            Assert.Equal(-1, game.Conditions["QualifyingStartingTime"]!.GetValue<int>());
            Assert.Equal(qualifying == "private" ? 1 : 0, game.Conditions["PrivateQualifying"]!.GetValue<int>());
        }
        Assert.DoesNotContain(game.Writes, p => p.Contains("loadGame"));
    }

    [Fact]
    public async Task Skipping_practice_disables_all_four_sessions_and_preserves_hidden_settings()
    {
        using var game = new Game();
        game.Conditions["Run Practice2"] = true;
        game.Conditions["Run Practice3"] = true;
        game.Conditions["Run Practice4"] = true;
        var plan = await game.Client.PreviewAsync(Target with { Sessions = new() { Practice = new("skip") } });
        await game.Client.ApplyAsync(plan);
        for (var i = 1; i <= 4; i++) Assert.False(game.Conditions[$"Run Practice{i}"]!.GetValue<bool>());
        Assert.Equal(60, game.Options["practice length"]!.GetValue<int>());
        Assert.Equal(1, game.Conditions["Num Qual Sessions"]!.GetValue<int>());
    }

    [Fact]
    public async Task Changed_qualifying_options_after_apply_block_launch()
    {
        using var game = new Game();
        var plan = await game.Client.PreviewAsync(Target with { Sessions = new() { Qualifying = new("private", 15, "09:30") } });
        await game.Client.ApplyAsync(plan);
        game.Conditions["PrivateQualifying"] = 0;
        var checks = await game.Client.VerifyAsync(plan);
        Assert.Contains(checks, c => c.Key == "qualifying" && !c.Matches);
        await Assert.ThrowsAsync<InvalidOperationException>(() => game.Client.StartAsync(plan, () => throw new Exception("Must not arm")));
        Assert.DoesNotContain(game.Writes, p => p.Contains("loadGame"));
    }

    private sealed class Game : HttpMessageHandler
    {
        public bool TrackOwned { get; init; } = true;
        public bool CarOwned { get; init; } = true;
        public bool IgnoreFuel { get; init; }
        public bool FailApply { get; init; }
        public bool InvalidSave { get; init; }
        public bool TimeoutLaunch { get; init; }
        public bool ChangeDuringGenerate { get; init; }
        public bool Loading { get; init; }
        public string Mode { get; init; } = "SETTING_GRANDPRIX";
        public string State { get; init; } = "GSTATE_SETUP";
        public List<string> Writes { get; } = [];
        public JsonObject Preset { get; private set; } = JsonNode.Parse("""
            {"Grid":["opponent"],"Weather":{"Race":{"Sky":1}},"Player":{
              "Game Options":{"Race Time":45,"Race Finish Criteria":-1,"Fuel Consumption Multiplier":1,"Tire Wear Multiplier":1,
                "AI Driver Strength":98,"practice length":60,"qualifying length":20},
              "Race Conditions":{"RaceStartingTime":900,"RaceTimeScale":1,"Num Race Sessions":1,"Run Practice1":true,"Num Qual Sessions":1}}}
            """)!.AsObject();
        public JsonObject Options => Preset["Player"]!["Game Options"]!.AsObject();
        public JsonObject Conditions => Preset["Player"]!["Race Conditions"]!.AsObject();
        private string _track = "old-track", _car = "old-car";
        private readonly HttpClient _http;
        public LmuWeekendClient Client { get; }
        public Game() { _http = new(this, false) { BaseAddress = new("http://localhost:6397") }; Client = new(_http); }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            JsonNode? result;
            if (request.Method == HttpMethod.Get) result = path switch
            {
                "/navigation/state" => new JsonObject { ["loadingStatus"] = new JsonObject { ["loading"] = Loading },
                    ["state"] = new JsonObject { ["navigationState"] = "NAV_MAIN_MENU", ["settingMode"] = Mode, ["gameState"] = State } },
                "/rest/sessions/getTracksAll" => new JsonArray(Track()),
                "/rest/sessions/getAllVehicles" => new JsonArray(Car()),
                "/rest/garage/UIScreen/SessionSetup" => new JsonObject {
                    ["trackInfo"] = _track == "qatar" ? Track() : new JsonObject { ["id"] = _track, ["eventName"] = "Old track", ["sceneDesc"] = "OLD", ["cmpName"] = "Old" },
                    ["selectedCar"] = _car == "mustang" ? Car() : new JsonObject { ["id"] = _car, ["desc"] = "Old car" } },
                _ => throw new InvalidOperationException($"Unexpected read {path}"),
            };
            else
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal(path is "/rest/race/track" or "/rest/garage/SetCurrentVehicle" ? "text/plain" : "application/json",
                    request.Content!.Headers.ContentType!.MediaType);
                var body = await request.Content!.ReadAsStringAsync(ct);
                if (path == "/rest/sessions/SessionPresets/requestPreset") result = Preset.DeepClone();
                else
                {
                    Writes.Add(path);
                    switch (path)
                    {
                        case "/rest/race/track": _track = body; result = null; break;
                        case "/rest/garage/SetCurrentVehicle": _car = JsonNode.Parse(body)!["id"]!.GetValue<string>(); result = null; break;
                        case "/rest/sessions/SessionPresets/applyPreset":
                            if (FailApply) return new(HttpStatusCode.ServiceUnavailable);
                            Preset = JsonNode.Parse(body)!.AsObject();
                            if (IgnoreFuel) Options["Fuel Consumption Multiplier"] = 1;
                            result = null; break;
                        case "/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset":
                            result = InvalidSave ? new JsonObject() : new JsonObject { ["SessionPreset"] = JsonNode.Parse(body) };
                            if (ChangeDuringGenerate) Options["Race Time"] = 60;
                            break;
                        case "/rest/sessions/SaveLoad/loadGame":
                            Assert.True(JsonNode.DeepEquals(Preset, JsonNode.Parse(body)!["save"]!["SessionPreset"]));
                            if (TimeoutLaunch) throw new TaskCanceledException("The response was lost; the game may have received the command.");
                            result = null; break;
                        default: throw new InvalidOperationException($"Unexpected write {path}");
                    }
                }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(result?.ToJsonString() ?? "null") };
        }
        private JsonObject Track() => new() { ["id"] = "qatar", ["cmpName"] = "Qatar_2024", ["sceneDesc"] = "QATARWEC", ["eventName"] = "Qatar", ["isOwned"] = TrackOwned };
        private JsonObject Car() => new() { ["id"] = "mustang", ["desc"] = "Ford Mustang #77", ["number"] = "77", ["isOwned"] = CarOwned,
            ["vehFile"] = @"D:\Installed\Vehicles\Ford_Mustang_GT3_2024\1.0\car.VEH" };
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }
}
