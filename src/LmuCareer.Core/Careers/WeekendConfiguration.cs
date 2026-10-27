using System.Globalization;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <summary>The editable settings of one upcoming round, also used by XML result matching.</summary>
public sealed record WeekendConfiguration(int RaceMinutes, int FuelUsage, int TyreWear, int TimeScale,
    string? StartTime, WeekendSessions Sessions)
{
    public void Apply(Round round, string carClass)
    {
        if (round.State != RoundState.Upcoming)
            throw new InvalidOperationException("Cancel the race weekend before changing its settings.");
        if (!RaceFormats.RaceLengths.Contains(RaceMinutes) || !RaceFormats.Multipliers.Contains(FuelUsage) ||
            !RaceFormats.Multipliers.Contains(TyreWear) || TimeScale is < 1 or > RaceFormats.MaxTimeScale || Sessions is null)
            throw new ArgumentException("Choose supported event settings.");
        if (!string.IsNullOrEmpty(StartTime) && !TimeOnly.TryParseExact(StartTime, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            throw new ArgumentException("Use HH:mm for the race start time, or leave it blank for the circuit default.");
        Sessions.Validate();
        // Validate the whole configuration before mutating any saved value.
        var tank = TankTimes.For(carClass, round.TrackFolder, round.LayoutFile) / FuelUsage;
        round.RaceMinutes = RaceMinutes;
        round.FuelMultiplier = FuelUsage;
        round.TireMultiplier = TyreWear;
        round.TimeScale = TimeScale;
        round.StartTime = StartTime ?? "";
        round.WeekendSessions = Sessions;
        round.Stints = Math.Max(Features.AiDriverSwaps ? 2 : 1, (int)Math.Ceiling(RaceMinutes / tank));
    }
}
