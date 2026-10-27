namespace LmuCareer.Core.Content;

/// <summary>Catalog layout filenames mapped to LMU's API scene descriptions. Never guess from display names.</summary>
public static class LmuTrackLayouts
{
    private static readonly IReadOnlyDictionary<string, string> Scenes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["LeMans_2023/layoutLeMans"] = "LEMANSWEC",
        ["LeMans_2023/layoutMulsanne"] = "LEMANSWEC_MULSANNE",
        ["Spa_2023/layoutSpa"] = "SPAWEC",
        ["Spa_2023/layoutSpaELMS"] = "SPAELMS",
        ["Spa_2023/layoutSpaEnd"] = "SPAWEC_ENDCE",
        ["Monza_2023/layoutMonza"] = "MONZAWEC",
        ["Monza_2023/layoutGrande"] = "MONZAWEC_GRANDE",
        ["Sebring_2023/layoutSebring"] = "SEBRINGWEC",
        ["Sebring_2023/layoutSchool"] = "SEBRINGWEC_SCHOOL",
        ["BahrainWEC_2023/layoutBahrain"] = "BAHRAINWEC",
        ["BahrainWEC_2023/layoutEndurance"] = "BAHRAINWEC_ENDCE",
        ["BahrainWEC_2023/layoutOuter"] = "BAHRAINWEC_OUTER",
        ["BahrainWEC_2023/layoutPaddock"] = "BAHRAINWEC_PADDOCK",
        ["PortimaoWEC_2023/layoutPortimao"] = "PORTIMAOWEC",
        ["PortimaoWEC_2023/layoutPortimaoELMS"] = "PORTIMAOELMS",
        ["FujiWEC_2023/layoutFuji"] = "FUJIWEC",
        ["FujiWEC_2023/layoutFujiCl"] = "FUJIWEC_CL",
        ["ImolaWEC_2024/layoutImola"] = "IMOLAWEC",
        ["ImolaWEC_2024/layoutImolaELMS"] = "IMOLAELMS",
        ["CotAWEC_2024/layoutCota"] = "COTAWEC",
        ["CotAWEC_2024/layoutNational"] = "COTAWEC_NATIONAL",
        ["Interlagos_2024/layoutInterlagos"] = "INTERLAGOSWEC",
        ["Qatar_2024/layoutQatar"] = "QATARWEC",
        ["Qatar_2024/layoutQatarShort"] = "QATARWEC_SHORT",
        ["Silverstone_2025/layoutELMS"] = "SILVERSTONEELMS",
        ["Silverstone_2025/layoutWEC"] = "SILVERSTONEWEC",
        ["Silverstone_2025/layoutInternational"] = "SILVERSTONE_INTERNATIONAL",
        ["Silverstone_2025/layoutNational"] = "SILVERSTONE_NATIONAL",
        ["PaulRicard_2025/layoutELMS"] = "PAULRICARDELMS",
        ["PaulRicard_2025/layout1A"] = "PAULRICARD1A",
        ["PaulRicard_2025/layout1AV2"] = "PAULRICARD1A-V2",
        ["PaulRicard_2025/layout1AV2Short"] = "PAULRICARD1A-V2-SHORT",
        ["PaulRicard_2025/layout3A"] = "PAULRICARD3A",
        ["Barcelona_2025/layoutELMS"] = "BARCELONAELMS",
        ["Daytona_2026/layoutDaytona"] = "DAYTONARC",
        ["LagunaSeca_2026/layoutLaguna"] = "LAGUNASECA",
        ["RoadAtlanta_2026/layoutRoadAtlanta"] = "ROADATLANTA",
        ["LongBeach_2026/layoutLongBeach"] = "LONGBEACH",
    };

    public static string? Scene(string folder, string layout) => Scenes.GetValueOrDefault($"{folder}/{layout}");
}
