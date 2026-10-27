using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using LmuCareer.Core.Careers;

namespace LmuCareer.Core.Content;

public sealed record WeekendTarget(string TrackFolder, string LayoutFile, string CarFolder, string? Number,
    bool CustomTeam, IReadOnlyList<string> LiveryNumbers, int RaceMinutes, int FuelUsage, int TyreWear, int TimeScale, string? StartTime)
{
    public WeekendSessions? Sessions { get; init; }

    public static WeekendTarget From(BriefingRound round) => new(round.Track.Folder, round.Track.LayoutFile,
        round.Car.Folder ?? throw new InvalidOperationException("The briefing's car folder is not known yet."),
        round.Car.Number, round.Car.CustomTeam, round.Car.LiveryNumbers, round.Settings.RaceMinutes,
        round.Settings.FuelUsage, round.Settings.TyreWear, round.Settings.TimeScale, round.Settings.StartTime);
}

public sealed record WeekendChange(string Key, string Before, string After);
public sealed record WeekendCheck(string Key, bool Matches);
public sealed record WeekendPlan(WeekendTarget Target, string TrackId, string Scene, string CarId,
    string CarName, JsonObject BeforePreset, string BeforeTrackId, string BeforeCarId, IReadOnlyList<WeekendChange> Changes);

/// <summary>
/// Uses the same preset/selection protocol as LMU's UI. No results API, relative setting writes,
/// game-file edits, automatic retries, or implicit race starts.
/// </summary>
public sealed class LmuWeekendClient(HttpClient http)
{
    public async Task<WeekendPlan> PreviewAsync(WeekendTarget target, CancellationToken cancellationToken = default)
    {
        Validate(target);
        await EnsureMenu(cancellationToken);
        var scene = LmuTrackLayouts.Scene(target.TrackFolder, target.LayoutFile)
            ?? throw new InvalidOperationException("This layout has no verified LMU API mapping. Set it manually for now.");
        var tracks = await Get("/rest/sessions/getTracksAll", cancellationToken) as JsonArray
            ?? throw new InvalidDataException("Unsupported track catalog.");
        var track = tracks.OfType<JsonObject>().SingleOrDefault(t => Text(t, "cmpName").Equals(target.TrackFolder, StringComparison.OrdinalIgnoreCase)
            && Text(t, "sceneDesc").Equals(scene, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The requested track/layout is not available in LMU.");
        if (track["isOwned"]?.GetValue<bool>() != true) throw new InvalidOperationException("The requested track is not owned or accessible.");
        var selection = await Selection(cancellationToken);
        var cars = await Get("/rest/sessions/getAllVehicles", cancellationToken) as JsonArray
            ?? throw new InvalidDataException("Unsupported vehicle catalog.");
        var candidates = cars.OfType<JsonObject>().Where(c => c["isOwned"]?.GetValue<bool>() == true &&
            VehicleFolder(Text(c, "vehFile")).Equals(target.CarFolder, StringComparison.OrdinalIgnoreCase)).ToList();
        if (target.CustomTeam) throw new InvalidOperationException("Custom Race Control liveries still need manual selection; automatic launch is not supported for them yet.");
        if (!string.IsNullOrEmpty(target.Number)) candidates = candidates.Where(c => Text(c, "number") == target.Number).ToList();
        else if (target.LiveryNumbers.Count > 0) candidates = candidates.Where(c => target.LiveryNumbers.Contains(Text(c, "number"))).ToList();
        var car = candidates.OrderByDescending(c => Text(c, "id") == Text(selection["selectedCar"], "id"))
            .ThenBy(c => Text(c, "id"), StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidOperationException("No accessible car/livery matches this briefing.");
        var preset = await Preset(cancellationToken);
        if (Text(track, "id").Length == 0 || Text(car, "id").Length == 0)
            throw new InvalidDataException("LMU did not return usable track and car identifiers.");
        var carLabel = $"{ContentCatalog.Default.Car(target.CarFolder)?.Name ?? target.CarFolder} — {Text(car, "desc")}";
        var options = Section(preset, "Game Options");
        var conditions = Section(preset, "Race Conditions");
        var changes = new List<WeekendChange>
        {
            new("circuit", Text(selection["trackInfo"], "eventName"), Text(track, "eventName")),
            new("layout", Text(selection["trackInfo"], "sceneDesc"), scene),
            new("car", Text(selection["selectedCar"], "desc"), carLabel),
            new("length", Display(options["Race Time"]) + " min", target.RaceMinutes.ToString(CultureInfo.InvariantCulture) + " min"),
            new("fuel", "x" + Display(options["Fuel Consumption Multiplier"]), "x" + target.FuelUsage.ToString(CultureInfo.InvariantCulture)),
            new("tyres", "x" + Display(options["Tire Wear Multiplier"]), "x" + target.TyreWear.ToString(CultureInfo.InvariantCulture)),
            new("start", Clock(conditions["RaceStartingTime"]), target.StartTime ?? "Default"),
            new("timeScale", "x" + Display(conditions["RaceTimeScale"]), "x" + target.TimeScale.ToString(CultureInfo.InvariantCulture)),
        };
        if (target.Sessions is { } sessions) changes.AddRange(sessions.Changes(options, conditions));
        return new(target, Text(track, "id"), scene, Text(car, "id"), carLabel, preset,
            Text(selection["trackInfo"], "id"), Text(selection["selectedCar"], "id"), changes);
    }

    public async Task<IReadOnlyList<WeekendCheck>> ApplyAsync(WeekendPlan plan, CancellationToken cancellationToken = default)
    {
        await EnsureMenu(cancellationToken);
        var selection = await Selection(cancellationToken);
        var current = await Preset(cancellationToken);
        if (!JsonNode.DeepEquals(current, plan.BeforePreset) || Text(selection["trackInfo"], "id") != plan.BeforeTrackId ||
            Text(selection["selectedCar"], "id") != plan.BeforeCarId)
            throw new InvalidOperationException("LMU's configuration changed after the preview. Review a new preview before applying.");
        if (plan.BeforeTrackId != plan.TrackId)
            await Post("/rest/race/track", plan.TrackId, "text/plain", cancellationToken);
        if (plan.BeforeCarId != plan.CarId)
            await Post("/rest/garage/SetCurrentVehicle", new JsonObject { ["id"] = plan.CarId }.ToJsonString(), "text/plain", cancellationToken);
        // Re-read after selections: LMU may itself adjust the grid or other defaults.
        var preset = await Preset(cancellationToken);
        var options = Section(preset, "Game Options");
        var conditions = Section(preset, "Race Conditions");
        options["Race Finish Criteria"] = 2; // Time, not % of the track's default race duration.
        options["Race Time"] = plan.Target.RaceMinutes;
        options["Fuel Consumption Multiplier"] = plan.Target.FuelUsage;
        options["Tire Wear Multiplier"] = plan.Target.TyreWear;
        conditions["RaceTimeScale"] = plan.Target.TimeScale;
        conditions["RaceStartingTime"] = StartMinutes(plan.Target.StartTime);
        conditions["Num Race Sessions"] = 1;
        plan.Target.Sessions?.Apply(options, conditions);
        await Post("/rest/sessions/SessionPresets/applyPreset", preset.ToJsonString(), "application/json", cancellationToken);
        return await VerifyAsync(plan, cancellationToken);
    }

    public async Task<IReadOnlyList<WeekendCheck>> VerifyAsync(WeekendPlan plan, CancellationToken cancellationToken = default)
    {
        await EnsureMenu(cancellationToken);
        var selection = await Selection(cancellationToken);
        var preset = await Preset(cancellationToken);
        var options = Section(preset, "Game Options");
        var conditions = Section(preset, "Race Conditions");
        var checks = new List<WeekendCheck> {
            new("circuit", Text(selection["trackInfo"], "cmpName").Equals(plan.Target.TrackFolder, StringComparison.OrdinalIgnoreCase)
                && selection["trackInfo"]?["isOwned"]?.GetValue<bool>() == true),
            new("layout", Text(selection["trackInfo"], "id") == plan.TrackId && Text(selection["trackInfo"], "sceneDesc") == plan.Scene),
            new("car", Text(selection["selectedCar"], "id") == plan.CarId && selection["selectedCar"]?["isOwned"]?.GetValue<bool>() == true),
            new("length", Number(options["Race Time"]) == plan.Target.RaceMinutes && Number(options["Race Finish Criteria"]) == 2 && Number(conditions["Num Race Sessions"]) == 1),
            new("fuel", Number(options["Fuel Consumption Multiplier"]) == plan.Target.FuelUsage),
            new("tyres", Number(options["Tire Wear Multiplier"]) == plan.Target.TyreWear),
            new("start", Number(conditions["RaceStartingTime"]) == StartMinutes(plan.Target.StartTime)),
            new("timeScale", Number(conditions["RaceTimeScale"]) == plan.Target.TimeScale),
        };
        if (plan.Target.Sessions is { } sessions) checks.AddRange(sessions.Checks(options, conditions));
        return checks;
    }

    public async Task StartAsync(WeekendPlan plan, Action beforeLoad, CancellationToken cancellationToken = default)
    {
        var checks = await VerifyAsync(plan, cancellationToken);
        if (checks.Any(c => !c.Matches)) throw new InvalidOperationException("LMU no longer matches the briefing. Configure and verify it again before starting.");
        var preset = await Preset(cancellationToken);
        var save = await PostJson("/rest/sessions/SaveLoad/generateSaveFileFromSessionPreset", preset, cancellationToken);
        if (save is not JsonObject || save["SessionPreset"] is not JsonObject)
            throw new InvalidDataException("LMU did not return a valid session to load. No launch command was sent.");
        if (!JsonNode.DeepEquals(save["SessionPreset"], preset))
            throw new InvalidDataException("The generated session differs from the verified preset. No launch command was sent.");
        if ((await VerifyAsync(plan, cancellationToken)).Any(c => !c.Matches))
            throw new InvalidOperationException("LMU changed while preparing the session. No launch command was sent.");
        beforeLoad(); // Persist the arming timestamp before the game can write any results.
        // Never retry a launch: a timeout does not prove the command wasn't received.
        await Post("/rest/sessions/SaveLoad/loadGame", new JsonObject { ["save"] = save }.ToJsonString(), "application/json", cancellationToken);
    }

    private async Task EnsureMenu(CancellationToken ct)
    {
        var state = await Get("/navigation/state", ct);
        if (state?["loadingStatus"]?["loading"]?.GetValue<bool>() == true ||
            Text(state?["state"], "navigationState") != "NAV_MAIN_MENU" || Text(state?["state"], "settingMode") != "SETTING_GRANDPRIX" ||
            Text(state?["state"], "gameState") != "GSTATE_SETUP")
            throw new InvalidOperationException("Return to LMU's offline Race Weekend menu first. A running or loading session will not be changed.");
    }

    private async Task<JsonObject> Selection(CancellationToken ct) => await Get("/rest/garage/UIScreen/SessionSetup", ct) as JsonObject
        ?? throw new InvalidDataException("Unsupported LMU session selection.");
    private async Task<JsonObject> Preset(CancellationToken ct) => await PostJson("/rest/sessions/SessionPresets/requestPreset", null, ct) as JsonObject
        ?? throw new InvalidDataException("Unsupported LMU session preset.");
    private async Task<JsonNode?> Get(string path, CancellationToken ct) => JsonNode.Parse(await http.GetStringAsync(path, ct));
    private async Task<JsonNode?> PostJson(string path, JsonNode? body, CancellationToken ct) =>
        JsonNode.Parse(await Post(path, body?.ToJsonString() ?? "null", "application/json", ct));
    private async Task<string> Post(string path, string body, string mediaType, CancellationToken ct)
    {
        using var content = new StringContent(body, Encoding.UTF8, mediaType);
        using var response = await http.PostAsync(path, content, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static JsonObject Section(JsonObject preset, string name) => preset["Player"]?[name] as JsonObject
        ?? throw new InvalidDataException($"The LMU preset is missing Player/{name}.");
    private static string Text(JsonNode? node, string key) => node?[key]?.GetValue<string>() ?? "";
    private static double Number(JsonNode? value) => value?.GetValue<double>() ?? double.NaN;
    private static string Display(JsonNode? value) => value?.ToJsonString() ?? "?";
    private static string Clock(JsonNode? value)
    {
        var minutes = Number(value);
        return minutes is >= 0 and < 1440 ? $"{(int)minutes / 60:00}:{(int)minutes % 60:00}" : "Default";
    }
    private static string VehicleFolder(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        for (var i = 0; i + 2 < parts.Length; i++)
            if (parts[i].Equals("Installed", StringComparison.OrdinalIgnoreCase) && parts[i + 1].Equals("Vehicles", StringComparison.OrdinalIgnoreCase)) return parts[i + 2];
        return "";
    }

    private static int StartMinutes(string? time) => string.IsNullOrEmpty(time) ? -1 :
        TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Hour * 60 + parsed.Minute : throw new ArgumentException("Invalid briefing start time.");
    private static void Validate(WeekendTarget target)
    {
        if (!RaceFormats.RaceLengths.Contains(target.RaceMinutes) || target.FuelUsage is < 1 or > 3 || target.TyreWear is < 1 or > 3 ||
            target.TimeScale is < 1 or > RaceFormats.MaxTimeScale) throw new ArgumentException("Unsupported briefing settings.");
        _ = StartMinutes(target.StartTime);
        target.Sessions?.Validate();
    }
}
