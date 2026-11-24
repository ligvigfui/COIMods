using HarmonyLib;
using Mafi.Collections;
using Mafi.Base.Prototypes.Machines;
using Mafi.Core.Buildings;
using Mafi.Core.Buildings.Cargo;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Validators;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Factory.WellPumps;
using Mafi.Core.Terrain;
using Mafi.Localization;
using Mafi.Unity.InputControl.Factory;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.Ui.Controllers.LayoutEntityPlacing;
using System.Reflection.Emit;

namespace GhostBuildOption;

[HarmonyPatch]
internal static class GhostEnvironmentValidators
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (Type type in new[] {
            typeof(FarmFertileGroundValidator),
            typeof(WellInjectionPumpAdditionValidator), typeof(UnstableTerrainValidator),
            typeof(FloatingEntityAdditionValidator) })
            yield return AccessTools.Method(type, "CanAdd");
    }

    private static bool Prefix(ref EntityValidationResult __result)
    {
        if (!GhostPlacementContext.IgnoreTerrain)
            return true;
        __result = EntityValidationResult.Success;
        return false;
    }
}

[HarmonyPatch(typeof(ProtoWithReservedOceanValidator), nameof(ProtoWithReservedOceanValidator.CanAddV2))]
internal static class GhostCoastalPlacement
{
    private static bool Prefix(LayoutEntityAddRequest addRequest, IProtoWithReservedOceanV2 proto,
        ref EntityValidationResult __result)
    {
        if (!GhostPlacementContext.IgnoreTerrain)
            return true;
        if ((proto is IDockEntityProto || proto is TradeDockProto || proto is OceanWaterPumpProto) &&
            (addRequest.Origin.Height < proto.MinGroundHeight || addRequest.Origin.Height > proto.MaxGroundHeight))
            return true;
        __result = EntityValidationResult.Success;
        return false;
    }
}

[HarmonyPatch]
internal static class GhostBuildingElevation
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(LayoutEntityPreview), nameof(LayoutEntityPreview.CanMoveUpDownIfValid));
        yield return AccessTools.Method(typeof(LayoutEntityPreview), nameof(LayoutEntityPreview.CanMoveUpDownIfInvalid));
    }

    private static void Postfix(bool ___m_isForRemoval, ref bool __result)
    {
        if (GhostModeManager.IsGhostModeEnabled && !___m_isForRemoval)
            __result = true;
    }
}

[HarmonyPatch("Mafi.Unity.Ui.Controllers.LayoutEntityPlacing.StaticEntityMassPlacer", "lowerEntity")]
internal static class GhostLowerInvalidBuilding
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var native = AccessTools.PropertyGetter(typeof(EntityValidationResult), nameof(EntityValidationResult.IsSuccess));
        var removal = AccessTools.Field(typeof(StaticEntityMassPlacer), "m_isInRemovalMode");
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(native))
            {
                instruction.opcode = OpCodes.Ldarg_0;
                instruction.operand = null;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Ldfld, removal);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GhostLowerInvalidBuilding), nameof(CanLower)));
                replaced++;
            }
            else
                yield return instruction;
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Expected one building lowering validity check, found {replaced}.");
    }

    private static bool CanLower(ref EntityValidationResult result, bool isForRemoval) =>
        (GhostModeManager.IsGhostModeEnabled && !isForRemoval) || result.IsSuccess;
}

internal static class GhostPlacementHeight
{
    internal static HeightTilesI Get(IStaticEntityPreview? preview, TileTransform anchor) =>
        preview is LayoutEntityPreview layout ? layout.Transform.Position.Height : anchor.Position.Height;
}

[HarmonyPatch]
internal static class GhostTransportHeightLimits
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method("Mafi.Unity.Ui.Controllers.TransportBuildController:transportUp");
        yield return AccessTools.Method("Mafi.Unity.Ui.Controllers.TransportBuildController:transportDown");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        string clamp = original.Name == "transportUp" ? "Min" : "Max";
        MethodInfo native = AccessTools.Method(typeof(ThicknessTilesI), clamp, new[] { typeof(ThicknessTilesI) });
        MethodInfo replacement = AccessTools.Method(typeof(GhostTransportHeightLimits), clamp);
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(native))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Expected one transport elevation clamp in {original.Name}, found {replaced}.");
    }

    private static ThicknessTilesI Min(ref ThicknessTilesI height, ThicknessTilesI maximum) =>
        GhostModeManager.IsGhostModeEnabled ? height : height.Min(maximum);

    private static ThicknessTilesI Max(ref ThicknessTilesI height, ThicknessTilesI minimum) =>
        GhostModeManager.IsGhostModeEnabled ? height : height.Max(minimum);
}

[HarmonyPatch("Mafi.Unity.Ui.Controllers.TransportBuildController", "pickAtCursorPosition")]
internal static class GhostTransportCursorScope
{
    private static void Prefix(out (bool, bool) __state) =>
        __state = GhostPlacementContext.Enter(GhostModeManager.IsGhostModeEnabled, false);

    private static void Finalizer((bool, bool) __state) => GhostPlacementContext.Exit(__state);
}

[HarmonyPatch(typeof(TransportHelper), nameof(TransportHelper.GetLowestNonCollidingHeight))]
internal static class GhostTransportMinimumHeight
{
    private static bool Prefix(ref HeightTilesI __result)
    {
        if (!GhostPlacementContext.IgnoreTerrain)
            return true;
        __result = HeightTilesI.MinValue;
        return false;
    }
}

[HarmonyPatch("Mafi.Unity.Ui.Controllers.PathFindingTransportPreview", "renderUpdate")]
internal static class GhostNegativeHeightPopup
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo native = AccessTools.PropertyGetter(typeof(ThicknessTilesI), nameof(ThicknessTilesI.IsNotNegative));
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(native))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(GhostNegativeHeightPopup), nameof(IsVisible));
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 2)
            throw new InvalidOperationException($"Expected two transport height-popup visibility checks, found {replaced}.");
    }

    private static bool IsVisible(ref ThicknessTilesI height) =>
        GhostModeManager.IsGhostModeEnabled || height.IsNotNegative;
}

[GlobalDependency(RegistrationMode.AsSelf)]
public sealed class GhostBuildingHeightDisplay : IHotReloadUi
{
    private readonly HeightPopup m_popup;
    internal static GhostBuildingHeightDisplay? Current { get; private set; }

    public GhostBuildingHeightDisplay(NewInstanceOf<HeightPopup> popup)
    {
        m_popup = popup.Instance;
        Current = this;
    }

    internal void Update(bool visible, HeightTilesI height)
    {
        if (visible)
            m_popup.MessageInfo(TranslationExtensions.AsLoc(height.Value.ToString()));
        m_popup.Visible(visible);
    }

    public void DisposeForHotReload()
    {
        m_popup.Hide();
        Current = null;
    }
}

[HarmonyPatch("Mafi.Unity.Ui.Controllers.LayoutEntityPlacing.StaticEntityMassPlacer", "InputUpdate")]
internal static class GhostShowBuildingHeight
{
    private static void Postfix(StaticEntityMassPlacer __instance, bool ___m_isInRemovalMode,
        LayoutEntitySlotPlacerHelper ___m_helper,
        Lyst<KeyValuePair<IStaticEntityPreview, EntityConfigData>> ___m_entityPreviews) =>
        GhostBuildingHeightDisplay.Current?.Update(
            GhostModeManager.IsGhostModeEnabled && __instance.IsActive && !___m_isInRemovalMode,
            GhostPlacementHeight.Get(___m_entityPreviews.Count == 1 ? ___m_entityPreviews[0].Key : null, ___m_helper.Transform));
}

[HarmonyPatch("Mafi.Unity.Ui.Controllers.LayoutEntityPlacing.StaticEntityMassPlacer", "Deactivate")]
internal static class GhostHideBuildingHeight
{
    private static void Postfix() => GhostBuildingHeightDisplay.Current?.Update(false, HeightTilesI.Zero);
}
