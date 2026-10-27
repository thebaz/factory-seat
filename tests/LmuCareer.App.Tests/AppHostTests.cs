using System.Text.Json;
using System.IO;
using System.Windows.Threading;
using LmuCareer.App;
using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.App.Tests;

[Collection("App host")]
public sealed class AppHostTests : IDisposable
{
    private readonly string? _previousHome = Environment.GetEnvironmentVariable("LMUCAREER_HOME");
    private readonly string _home = Path.Combine(Path.GetTempPath(), "factory-seat-host-tests", Guid.NewGuid().ToString("N"));
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;
    private readonly MainWindow _window;
    private readonly ApiHost _api;

    public AppHostTests()
    {
        Environment.SetEnvironmentVariable("LMUCAREER_HOME", _home);
        // Exercise the actual Windows backend on its owning STA dispatcher, without showing
        // a window, initializing WebView2, opening dialogs or requiring the game.
        var ready = new TaskCompletionSource<(Dispatcher, MainWindow, ApiHost)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                ready.SetResult((Dispatcher.CurrentDispatcher, window, new ApiHost(window)));
                Dispatcher.Run();
            }
            catch (Exception error) { ready.TrySetException(error); }
        }) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        (_dispatcher, _window, _api) = ready.Task.GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Desktop_backend_initializes_without_showing_the_window()
    {
        using var reply = await Call("init");
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        var result = reply.RootElement.GetProperty("result");
        Assert.Empty(result.GetProperty("careers").EnumerateArray());
        Assert.Contains(result.GetProperty("locale").GetProperty("available").EnumerateArray(),
            language => language.GetProperty("code").GetString() == "de");
    }

    [Fact]
    public async Task Italian_is_shipped_and_loaded_by_the_Windows_backend()
    {
        var locales = Locales.Load();
        Assert.Contains(locales.Available(), language => language.Code == "it" && language.Name == "Italiano");
        Assert.Equal("it", locales.Resolve("it-IT", null));
        Assert.Equal("Seleziona la cartella di Le Mans Ultimate", locales.Text("it", "Choose your Le Mans Ultimate folder"));
        using var saved = await Call("setPrefs", new { language = "it" });
        Assert.True(saved.RootElement.GetProperty("ok").GetBoolean());
        using var reply = await Call("init");
        var locale = reply.RootElement.GetProperty("result").GetProperty("locale");
        Assert.Equal("it", locale.GetProperty("language").GetString());
        Assert.Equal("Prove libere e qualifiche", locale.GetProperty("strings").GetProperty("Practice and qualifying").GetString());
    }

    [Fact]
    public async Task Settings_are_saved_only_in_the_isolated_test_home()
    {
        using var reply = await Call("setPrefs", new { muted = true, checkForUpdates = false });
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(AppSettings.Load().Muted);
        Assert.False(AppSettings.Load().CheckForUpdates);
        Assert.Equal(Path.Combine(_home, "settings.json"), AppSettings.FilePath);
    }

    [Fact]
    public async Task Session_preferences_can_be_saved_and_loaded_without_the_game()
    {
        using var reply = await Call("setPrefs", new { weekendSessions = new {
            practice = new { mode = "skip", minutes = 60 },
            qualifying = new { mode = "private", minutes = 15, startTime = "09:30" },
        } });
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("skip", AppSettings.Load().WeekendSessions.Practice.Mode);
        Assert.Equal("09:30", AppSettings.Load().WeekendSessions.Qualifying.StartTime);
        using var init = await Call("init");
        Assert.Equal(15, init.RootElement.GetProperty("result").GetProperty("settings").GetProperty("weekendSessions")
            .GetProperty("qualifying").GetProperty("minutes").GetInt32());
    }

    [Fact]
    public async Task Invalid_session_preferences_do_not_overwrite_saved_preferences()
    {
        using var reply = await Call("setPrefs", new { muted = true, weekendSessions = new {
            practice = new { mode = "open", minutes = 0 }, qualifying = new { mode = "keep", minutes = 20 },
        } });
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(AppSettings.Load().Muted);
        Assert.Equal("keep", AppSettings.Load().WeekendSessions.Practice.Mode);
    }

    [Fact]
    public async Task Weekend_configuration_is_saved_per_round_without_changing_global_defaults_or_other_rounds()
    {
        var career = WeekendCareer();
        new CareerStore(_home).Save(career);
        using var reply = await SaveWeekend(career.Id, 1);
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        var saved = new CareerStore(_home).Load(career.Id).Career;
        Assert.Equal(45, saved.CurrentSeason.Rounds[0].RaceMinutes);
        Assert.Equal("skip", saved.CurrentSeason.Rounds[0].WeekendSessions!.Practice.Mode);
        Assert.Equal(30, saved.CurrentSeason.Rounds[1].RaceMinutes);
        Assert.Null(saved.CurrentSeason.Rounds[1].WeekendSessions);
        Assert.Equal("keep", AppSettings.Load().WeekendSessions.Practice.Mode);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task Armed_or_stale_weekend_edits_are_rejected(bool armed, int expectedRound)
    {
        var career = WeekendCareer();
        if (armed) RoundFlow.Arm(career.CurrentSeason, DateTimeOffset.Now);
        new CareerStore(_home).Save(career);
        using var reply = await SaveWeekend(career.Id, expectedRound);
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(30, new CareerStore(_home).Load(career.Id).Career.CurrentSeason.Rounds[0].RaceMinutes);
    }

    [Fact]
    public async Task Stale_preview_round_is_rejected_before_contacting_LMU()
    {
        var career = WeekendCareer();
        new CareerStore(_home).Save(career);
        using var reply = await Call("previewLmuWeekend", new { id = career.Id, roundNumber = 2 });
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("The next round changed. Reopen its configuration.", reply.RootElement.GetProperty("error").GetString());
    }

    private static Career WeekendCareer() => new()
    {
        Name = "Weekend settings test",
        CurrentSeason = new Season
        {
            Number = 1,
            Car = new CareerCar("Ford Mustang LMGT3", "GT3", "", "") { Folder = "Ford_Mustang_GT3_2024" },
            Rounds = [new Round { Number = 1, RaceMinutes = 30 }, new Round { Number = 2, RaceMinutes = 30 }],
        },
    };

    private Task<JsonDocument> SaveWeekend(Guid id, int roundNumber) => Call("saveWeekendConfiguration", new
    {
        id, roundNumber,
        configuration = new { raceMinutes = 45, fuelUsage = 2, tyreWear = 1, timeScale = 10, startTime = "10:00",
            sessions = new { practice = new { mode = "skip", minutes = 60 }, qualifying = new { mode = "private", minutes = 15 } } },
    });

    [Fact]
    public async Task Install_folder_is_validated_and_the_same_XML_results_folder_is_used()
    {
        var game = Path.Combine(_home, "game");
        Directory.CreateDirectory(Path.Combine(game, "Installed", "Locations"));
        Directory.CreateDirectory(Path.Combine(game, "UserData", "Log", "Results"));
        using var saved = await Call("setLmuRoot", new { path = game });
        Assert.True(saved.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(game, AppSettings.Load().LmuRoot);
        Assert.Equal(Path.Combine(game, "UserData", "Log", "Results"), LmuInstall.Scan(game).ResultsFolder);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task Malformed_messages_return_an_error_envelope_instead_of_crashing_the_host(string message)
    {
        using var reply = JsonDocument.Parse(await Handle(message));
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reply.RootElement.GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task Unknown_methods_preserve_request_id_and_return_an_error()
    {
        using var reply = await Call("notAnAppMethod");
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, reply.RootElement.GetProperty("id").GetInt32());
    }

    [Theory]
    [InlineData("applyLmuWeekend")]
    [InlineData("startLmuWeekend")]
    public async Task Weekend_commands_require_a_valid_career_and_prepared_preview(string method)
    {
        using var reply = await Call(method, new { id = Guid.NewGuid().ToString(), confirmed = true, token = "invented" });
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData("applyLmuWeekend")]
    [InlineData("startLmuWeekend")]
    public async Task Invented_preview_token_is_rejected_for_an_existing_career_without_touching_LMU(string method)
    {
        var career = new Career
        {
            Name = "Local test",
            CurrentSeason = new Season
            {
                Number = 1,
                Car = new CareerCar("Ford Mustang LMGT3", "GT3", "", "") { Folder = "Ford_Mustang_GT3_2024" },
                Rounds = [new Round { Number = 1, TrackFolder = "Qatar_2024", LayoutFile = "layoutQatar", RaceMinutes = 30 }],
            },
        };
        new CareerStore(_home).Save(career);
        using var reply = await Call(method, new { id = career.Id, token = "invented", confirmed = true });
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("preview", reply.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(new CareerStore(_home).Load(career.Id).Career.CurrentSeason.ArmedRound);
    }

    private async Task<JsonDocument> Call(string method, object? args = null) =>
        JsonDocument.Parse(await Handle(JsonSerializer.Serialize(new { id = 1, method, args = args ?? new { } })));

    private Task<string> Handle(string message) => _dispatcher.InvokeAsync(() => _api.HandleAsync(message)).Task.Unwrap();

    public void Dispose()
    {
        _dispatcher.Invoke(() =>
        {
            _api.Dispose();
            _window.Close();
            _dispatcher.InvokeShutdown();
        });
        _thread.Join();
        Environment.SetEnvironmentVariable("LMUCAREER_HOME", _previousHome);
    }

}
