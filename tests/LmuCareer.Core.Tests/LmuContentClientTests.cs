using System.Net;
using System.Text.Json;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public class LmuContentClientTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Parse("""
        {
          "packs": [{"id":"base","name":"Base"},{"id":"dlc","name":"DLC"},{"id":"future","name":"Future"}],
          "tracks": [{"folder":"Track_2024","name":"Track","location":"","country":"","lengthKm":1,"pack":"dlc","layouts":{}}],
          "cars": [{"folder":"Car_2024","name":"Car","class":"GT3","pack":"dlc","carTypes":[]}],
          "events": []
        }
        """);

    private static string Tracks(bool owned) => JsonSerializer.Serialize(new[] { new { cmpName = "Track_2024", isOwned = owned } });
    private static string Cars(bool owned) => JsonSerializer.Serialize(new[] {
        new { vehFile = @"D:\SteamLibrary\Installed\Vehicles\Car_2024\1.0\CAR.VEH", isOwned = owned }
    });

    [Fact]
    public async Task Owned_cars_and_tracks_enable_pack_but_not_base_or_unreleased_packs()
    {
        using var http = Client(Tracks(true), Cars(true));
        var result = await new LmuContentClient(http).DetectAsync(Catalog);
        Assert.Equal(["dlc"], result.OwnedPacks);
        Assert.Equal(["dlc"], result.CheckedPacks);
        Assert.Equal(["future"], result.UnresolvedPacks);
    }

    [Fact]
    public async Task Unowned_content_is_checked_but_not_enabled()
    {
        using var http = Client(Tracks(false), Cars(false));
        var result = await new LmuContentClient(http).DetectAsync(Catalog);
        Assert.Empty(result.OwnedPacks);
        Assert.Equal(["dlc"], result.CheckedPacks);
        Assert.DoesNotContain("dlc", result.UnresolvedPacks);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Partial_pack_does_not_unlock_unavailable_content(bool track, bool car)
    {
        using var http = Client(Tracks(track), Cars(car));
        var result = await new LmuContentClient(http).DetectAsync(Catalog);
        Assert.Empty(result.OwnedPacks);
        Assert.Contains("dlc", result.CheckedPacks);
        Assert.Contains("dlc", result.UnresolvedPacks);
    }

    [Fact]
    public async Task Accessible_livery_and_layout_win_over_locked_variants_case_insensitively()
    {
        using var http = Client("""
            [{"cmpName":"track_2024","isOwned":true},{"cmpName":"TRACK_2024","isOwned":false}]
            """, """
            [{"vehFile":"D:/Installed/Vehicles/car_2024/1/car.veh","isOwned":false},
             {"vehFile":"D:/Installed/Vehicles/CAR_2024/1/other.veh","isOwned":true}]
            """);
        var result = await new LmuContentClient(http).DetectAsync(Catalog);
        Assert.Equal(["dlc"], result.OwnedPacks);
    }

    [Fact]
    public async Task Missing_pack_content_is_unresolved_and_does_not_replace_manual_selection()
    {
        var catalog = Catalog with { Cars = [..Catalog.Cars, new("MissingCar", "Missing", "GT3", "dlc", [])] };
        using var http = Client(Tracks(true), Cars(true));
        var result = await new LmuContentClient(http).DetectAsync(catalog);
        Assert.Empty(result.CheckedPacks);
        Assert.Empty(result.OwnedPacks);
        Assert.Contains("dlc", result.UnresolvedPacks);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[{\"cmpName\":\"Track_2024\"}]")]
    [InlineData("[{\"cmpName\":\"Track_2024\",\"isOwned\":\"true\"}]")]
    [InlineData("[{\"cmpName\":\"Unknown\",\"isOwned\":true}]")]
    public async Task Empty_or_unrecognised_track_response_fails_without_a_detection_result(string tracks)
    {
        using var http = Client(tracks, Cars(true));
        await Assert.ThrowsAsync<InvalidDataException>(() => new LmuContentClient(http).DetectAsync(Catalog));
    }

    [Fact]
    public async Task Invalid_vehicle_path_does_not_prove_ownership()
    {
        using var http = Client(Tracks(true), """[{"vehFile":"Car_2024/car.veh","isOwned":true}]""");
        await Assert.ThrowsAsync<InvalidDataException>(() => new LmuContentClient(http).DetectAsync(Catalog));
    }

    [Fact]
    public async Task Invalid_json_fails_without_a_detection_result()
    {
        using var http = Client("not json", Cars(true));
        await Assert.ThrowsAnyAsync<JsonException>(() => new LmuContentClient(http).DetectAsync(Catalog));
    }

    [Fact]
    public async Task Service_unavailable_is_not_treated_as_no_owned_content()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.ServiceUnavailable))) { BaseAddress = new("http://localhost:6397") };
        await Assert.ThrowsAsync<HttpRequestException>(() => new LmuContentClient(http).DetectAsync(Catalog));
    }

    [Fact]
    public async Task Connection_failure_is_not_treated_as_no_owned_content()
    {
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("Connection refused"))) { BaseAddress = new("http://localhost:6397") };
        await Assert.ThrowsAsync<HttpRequestException>(() => new LmuContentClient(http).DetectAsync(Catalog));
    }

    [Fact]
    public async Task Requests_are_read_only_and_cancellable()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var http = Client(Tracks(true), Cars(true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LmuContentClient(http).DetectAsync(Catalog, source.Token));
    }

    private static HttpClient Client(string tracks, string cars) => new(new Handler(request =>
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("localhost", request.RequestUri!.Host);
        Assert.Null(request.Content);
        var json = request.RequestUri.AbsolutePath switch
        {
            "/rest/sessions/getTracksAll" => tracks,
            "/rest/sessions/getAllVehicles" => cars,
            _ => throw new InvalidOperationException("Unexpected endpoint"),
        };
        return new(HttpStatusCode.OK) { Content = new StringContent(json) };
    })) { BaseAddress = new("http://localhost:6397") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
