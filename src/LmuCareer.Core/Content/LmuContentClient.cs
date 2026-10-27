using System.Net.Http;
using System.Text.Json;

namespace LmuCareer.Core.Content;

public sealed record OwnedContentDetection(
    IReadOnlyList<string> OwnedPacks,
    IReadOnlyList<string> CheckedPacks,
    IReadOnlyList<string> UnresolvedPacks);

/// <summary>Reads ownership from a running LMU. Never writes game settings or infers ownership from installed files.</summary>
public sealed class LmuContentClient(HttpClient http)
{
    public async Task<OwnedContentDetection> DetectAsync(ContentCatalog catalog, CancellationToken cancellationToken = default)
    {
        // A successful read of both catalogs is the readiness check: a listening port alone isn't enough.
        var tracksTask = ReadAsync("/rest/sessions/getTracksAll", cancellationToken);
        var carsTask = ReadAsync("/rest/sessions/getAllVehicles", cancellationToken);
        await Task.WhenAll(tracksTask, carsTask);
        var tracks = await tracksTask;
        var cars = await carsTask;
        var trackOwnership = Ownership(tracks, item => Text(item, "cmpName"));
        var carOwnership = Ownership(cars, item => VehicleFolder(Text(item, "vehFile")));
        if (!catalog.Tracks.Any(t => trackOwnership.ContainsKey(t.Folder)) ||
            !catalog.Cars.Any(c => carOwnership.ContainsKey(c.Folder)))
            throw new InvalidDataException("LMU returned no recognisable ownership information.");

        var owned = new List<string>();
        var checkedPacks = new List<string>();
        var unresolved = new List<string>();
        foreach (var pack in catalog.Packs.Where(p => p.Id != ContentCatalog.BasePack))
        {
            var items = catalog.Tracks.Where(t => t.Pack == pack.Id)
                .Select(t => trackOwnership.TryGetValue(t.Folder, out var value) ? (bool?)value : null)
                .Concat(catalog.Cars.Where(c => c.Pack == pack.Id)
                    .Select(c => carOwnership.TryGetValue(c.Folder, out var value) ? (bool?)value : null)).ToList();
            // A pack unlocks all its contents in the career. Partial ownership must not grant the whole pack.
            if (items.Count == 0 || items.Any(v => v is null))
                unresolved.Add(pack.Id);
            else
            {
                checkedPacks.Add(pack.Id);
                if (items.All(v => v == true)) owned.Add(pack.Id);
                if (items.Any(v => v == true) && items.Any(v => v == false)) unresolved.Add(pack.Id);
            }
        }
        return new(owned, checkedPacks, unresolved);
    }

    private async Task<JsonElement> ReadAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            throw new InvalidDataException("LMU returned an empty or unsupported content catalog.");
        return document.RootElement.Clone();
    }

    private static Dictionary<string, bool> Ownership(JsonElement items, Func<JsonElement, string> keyOf)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("isOwned", out var owned) ||
                owned.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) continue;
            var key = keyOf(item);
            if (key.Length == 0) continue;
            // Multiple layouts/liveries can refer to one catalog entry. One accessible variant is enough.
            result[key] = result.GetValueOrDefault(key) || owned.GetBoolean();
        }
        return result;
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string VehicleFolder(string path)
    {
        // Normalize both separators before extracting the vehicle folder from LMU's paths.
        var parts = path.Replace('\\', '/').Split('/');
        for (var i = 0; i + 2 < parts.Length; i++)
            if (parts[i].Equals("Installed", StringComparison.OrdinalIgnoreCase) &&
                parts[i + 1].Equals("Vehicles", StringComparison.OrdinalIgnoreCase)) return parts[i + 2];
        return "";
    }
}
