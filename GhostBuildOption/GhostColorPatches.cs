using HarmonyLib;
using Mafi.Core.Entities.Static;
using Mafi.Core.Mods;
using Mafi.Unity.Entities.Static;
using Mafi.Unity.InputControl.Factory;

namespace GhostBuildOption;

internal static class GhostColors
{
    private static ColorRgba s_color = Blend(ColorRgba.Yellow, 20);

    internal static void Configure(ModJsonConfig config)
    {
        string hex = config.GetString("ghost_color", "#FFFF00");
        if (!ColorRgba.TryParseHex(hex, out var color))
            throw new FormatException($"Invalid Ghost Build ghost_color '{hex}'; expected #RRGGBB.");
        double opacity = config.GetDouble("ghost_tint_opacity", 20);
        if (double.IsNaN(opacity) || double.IsInfinity(opacity) || opacity < 0 || opacity > 100)
            throw new ArgumentOutOfRangeException(nameof(config), opacity, "Ghost tint opacity must be between 0 and 100.");
        s_color = Blend(color, opacity);
    }

    private static ColorRgba Blend(ColorRgba color, double opacity)
    {
        var neutral = InstancedChunkBasedLayoutEntitiesRenderer.BLUEPRINT_PAUSED_COLOR;
        byte Channel(byte background, byte foreground) =>
            (byte)Math.Round((background * (100 - opacity) + foreground * opacity) / 100,
                MidpointRounding.AwayFromZero);
        return new ColorRgba(Channel(neutral.R, color.R), Channel(neutral.G, color.G),
            Channel(neutral.B, color.B), neutral.A);
    }

    internal static ColorRgba Apply(ColorRgba original) =>
        new ColorRgba(s_color.R, s_color.G, s_color.B, original.A);
}

[HarmonyPatch]
internal static class GhostBlueprintColor
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(InstancedChunkBasedLayoutEntitiesRenderer), "GetBlueprintColor");
        yield return AccessTools.Method("Mafi.Unity.Factory.Transports.InstancedChunkBasedTransportsRenderer:GetBlueprintColor");
    }

    private static void Postfix(IStaticEntity entity, ref ColorRgba color, bool __result)
    {
        if (__result && GhostWorldState.IsGhost(entity))
            color = GhostColors.Apply(color);
    }
}

[HarmonyPatch]
internal static class GhostConstructionCubeColor
{
    private static MethodBase TargetMethod() => AccessTools.Method(
        "Mafi.Unity.Entities.Static.ConstructionCubesRenderer+CubesForEntityData:getCubesColor");

    private static void Postfix(IStaticEntity ___Entity, ref ColorRgba __result)
    {
        if (GhostWorldState.IsGhost(___Entity))
            __result = GhostColors.Apply(__result);
    }
}

[HarmonyPatch(typeof(LayoutEntityPreview), "highlight")]
internal static class GhostBuildingPreviewColor
{
    private static void Prefix(ref ColorRgba __0, bool ___m_isForRemoval)
    {
        if (GhostModeManager.IsGhostModeEnabled && !___m_isForRemoval && __0 == ColorRgba.Green)
            __0 = GhostColors.Apply(__0);
    }
}

[HarmonyPatch]
internal static class GhostTransportPreviewColor
{
    private static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(TransportPreviewVisualizer))
        .Single(method => method.Name.StartsWith("<ShowPreview>g__tryShowTransport|", StringComparison.Ordinal));

    private static void Prefix(ref ColorRgba? __1, bool __2)
    {
        if (GhostModeManager.IsGhostModeEnabled && !__2 && __1.HasValue && __1.Value == HighlightColors.OK_GREEN)
            __1 = GhostColors.Apply(__1.Value);
    }
}
