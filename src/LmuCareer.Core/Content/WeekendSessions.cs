using System.Globalization;
using System.Text.Json.Nodes;

namespace LmuCareer.Core.Content;

/// <param name="Mode">keep, skip, open or private. Only explicit choices change LMU.</param>
public sealed record WeekendSession(string Mode = "keep", int Minutes = 30, string? StartTime = null);

/// <summary>Optional pre-race sessions. Race format and XML result matching remain unchanged.</summary>
public sealed record WeekendSessions
{
    public WeekendSession Practice { get; init; } = new("keep", 60);
    public WeekendSession Qualifying { get; init; } = new("keep", 20);

    public void Validate()
    {
        foreach (var session in new[] { Practice, Qualifying })
        {
            if (session is null || session.Mode is not ("keep" or "skip" or "open" or "private"))
                throw new ArgumentException("Unsupported practice or qualifying mode.");
            if (session.Mode is "open" or "private")
            {
                if (session.Minutes is < 1 or > 1440) throw new ArgumentException("Session duration must be between 1 and 1440 minutes.");
                _ = StartMinutes(session.StartTime);
            }
        }
    }

    public void Apply(JsonObject options, JsonObject conditions)
    {
        Validate();
        ApplySession(Practice, true, options, conditions);
        ApplySession(Qualifying, false, options, conditions);
    }

    public IEnumerable<WeekendCheck> Checks(JsonObject options, JsonObject conditions)
    {
        if (Practice.Mode != "keep") yield return new("practice", Matches(Practice, true, options, conditions));
        if (Qualifying.Mode != "keep") yield return new("qualifying", Matches(Qualifying, false, options, conditions));
    }

    public IEnumerable<WeekendChange> Changes(JsonObject options, JsonObject conditions)
    {
        if (Practice.Mode != "keep") yield return new("practice", Current(true, options, conditions), Describe(Practice));
        if (Qualifying.Mode != "keep") yield return new("qualifying", Current(false, options, conditions), Describe(Qualifying));
    }

    private static void ApplySession(WeekendSession session, bool practice, JsonObject options, JsonObject conditions)
    {
        if (session.Mode == "keep") return;
        if (practice)
            for (var i = 1; i <= 4; i++) conditions[$"Run Practice{i}"] = i == 1 && session.Mode != "skip";
        else conditions["Num Qual Sessions"] = session.Mode == "skip" ? 0 : 1;
        if (session.Mode == "skip") return; // Preserve hidden duration/privacy/time for future use.
        options[practice ? "practice length" : "qualifying length"] = session.Minutes;
        if (practice) conditions["PrivatePractice"] = session.Mode == "private";
        else conditions["PrivateQualifying"] = session.Mode == "private" ? 1 : 0;
        conditions[practice ? "Practice1StartingTime" : "QualifyingStartingTime"] = StartMinutes(session.StartTime);
    }

    private static bool Matches(WeekendSession session, bool practice, JsonObject options, JsonObject conditions)
    {
        var enabled = session.Mode != "skip";
        var countsMatch = practice
            ? Enumerable.Range(1, 4).All(i => Bool(conditions[$"Run Practice{i}"]) == (enabled && i == 1))
            : Number(conditions["Num Qual Sessions"]) == (enabled ? 1 : 0);
        if (!countsMatch || !enabled) return countsMatch;
        return Number(options[practice ? "practice length" : "qualifying length"]) == session.Minutes &&
            (practice ? Bool(conditions["PrivatePractice"]) == (session.Mode == "private")
                : Number(conditions["PrivateQualifying"]) == (session.Mode == "private" ? 1 : 0)) &&
            Number(conditions[practice ? "Practice1StartingTime" : "QualifyingStartingTime"]) == StartMinutes(session.StartTime);
    }

    private static string Current(bool practice, JsonObject options, JsonObject conditions)
    {
        if (practice ? !Enumerable.Range(1, 4).Any(i => Bool(conditions[$"Run Practice{i}"]) == true)
            : Number(conditions["Num Qual Sessions"]) == 0) return "Skipped";
        var privacy = practice ? Bool(conditions["PrivatePractice"]) == true : Number(conditions["PrivateQualifying"]) == 1;
        var time = Number(conditions[practice ? "Practice1StartingTime" : "QualifyingStartingTime"]);
        var clock = time is >= 0 and < 1440 ? $"{(int)time / 60:00}:{(int)time % 60:00}" : "Default";
        return $"{(privacy ? "Private" : "Open")}, {Number(options[practice ? "practice length" : "qualifying length"])} min, {clock}";
    }

    private static string Describe(WeekendSession session) => session.Mode == "skip" ? "Skipped" :
        $"{(session.Mode == "private" ? "Private" : "Open")}, {session.Minutes} min, {session.StartTime ?? "Default"}";

    private static double Number(JsonNode? value) => double.TryParse(value?.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
    private static bool? Bool(JsonNode? value) => bool.TryParse(value?.ToJsonString(), out var b) ? b : null;
    private static int StartMinutes(string? time) => string.IsNullOrEmpty(time) ? -1 :
        TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Hour * 60 + parsed.Minute : throw new ArgumentException("Invalid session start time. Use HH:mm or the default.");
}
