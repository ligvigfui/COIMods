using HarmonyLib;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Validators;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Terrain;
using Mafi.Localization;
using System.Runtime.CompilerServices;
using Mafi.Unity.InputControl.Factory;

namespace GhostBuildOption;

[HarmonyPatch]
internal static class GhostPlacementCommandScope
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(EntitiesCommandsProcessor), "Invoke", new[] { typeof(BatchCreateStaticEntitiesCmd) });
        yield return AccessTools.Method(typeof(EntitiesCommandsProcessor), "Invoke", new[] { typeof(CreateStaticEntityCmd) });
        yield return AccessTools.Method(typeof(TransportsCommandsProcessor), "Invoke", new[] { typeof(BuildTransportCmd) });
    }

    private static void Prefix(out (bool, bool) __state) =>
        __state = GhostPlacementContext.Enter(GhostWorldState.Current?.IsModeEnabled == true, true);

    private static void Finalizer((bool, bool) __state) => GhostPlacementContext.Exit(__state);
}

[HarmonyPatch]
internal static class GhostPreviewScope
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method("Mafi.Unity.InputControl.Factory.LayoutEntityPreview:SimUpdate");
        yield return AccessTools.Method("Mafi.Unity.InputControl.Factory.StaticTransportPreview:SimUpdate");
        yield return AccessTools.Method("Mafi.Unity.Ui.Controllers.PathFindingTransportPreview:simUpdate");
    }

    private static void Prefix(out (bool, bool) __state) =>
        __state = GhostPlacementContext.Enter(GhostModeManager.IsGhostModeEnabled, false);

    private static void Finalizer((bool, bool) __state) => GhostPlacementContext.Exit(__state);
}

[HarmonyPatch(typeof(StaticEntitiesTerrainInteractionManager), nameof(StaticEntitiesTerrainInteractionManager.CanAdd))]
internal static class GhostGroundContact
{
    private static bool Prefix(IEntityWithOccupiedTilesAddRequest addRequest, TerrainManager ___m_terrainManager,
        ref EntityValidationResult __result)
    {
        if (!GhostPlacementContext.IgnoreTerrain)
            return true;
        foreach (var vertex in addRequest.OccupiedVertices)
            if (!___m_terrainManager.IsValidCoord(addRequest.Origin.Xy + vertex.RelCoord))
                return true;
        __result = EntityValidationResult.Success;
        return false;
    }
}

[HarmonyPatch]
internal static class GhostTerrainConstraints
{
    private static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(LayoutEntityTerrainValidator))
        .Single(method => method.Name.EndsWith(".CanAdd", StringComparison.Ordinal));

    private static bool Prefix(ILayoutEntityAddRequest addRequest, TerrainManager ___m_terrain,
        ref EntityValidationResult __result)
    {
        if (!GhostPlacementContext.IgnoreTerrain)
            return true;
        foreach (var tile in addRequest.OccupiedTiles)
            if (!___m_terrain.IsValidCoord(addRequest.Transform.Position.Xy + tile.RelCoord))
                return true;
        foreach (var vertex in addRequest.OccupiedVertices)
            if (!___m_terrain.IsValidCoord(addRequest.Transform.Position.Xy + vertex.RelCoord))
                return true;
        __result = EntityValidationResult.Success;
        return false;
    }
}

[HarmonyPatch(typeof(ILayoutEntityProtoWithElevationValidator), nameof(ILayoutEntityProtoWithElevationValidator.CanAdd))]
internal static class GhostElevatedEntity
{
    private static bool Prefix(ref EntityValidationResult __result)
    {
        if (!GhostPlacementContext.IgnoreTerrain)
            return true;
        __result = EntityValidationResult.Success;
        return false;
    }
}

[HarmonyPatch(typeof(ILayoutEntityProtoWithElevationValidator), nameof(ILayoutEntityProtoWithElevationValidator.PrepareForAdd))]
internal static class GhostNoElevatedSupports
{
    private static bool Prefix() => !GhostPlacementContext.CreateGhosts;
}

[HarmonyPatch(typeof(TransportsConstructionHelper), nameof(TransportsConstructionHelper.CheckTerrainCollision))]
internal static class GhostTransportTerrain
{
    private static bool Prefix(ref bool __result)
    {
        if (!GhostPlacementContext.IgnoreTerrain)
            return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(TransportsConstructionHelper), nameof(TransportsConstructionHelper.TryFindPillarsForTrajectory))]
internal static class GhostNoTransportSupports
{
    private static bool Prefix(ref ImmutableArray<Tile3i> __2, ref int __3, ref int __4, ref bool __result)
    {
        if (!GhostPlacementContext.IgnoreTerrain)
            return true;
        __2 = ImmutableArray<Tile3i>.Empty;
        __3 = 0;
        __4 = 0;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(TransportPathFinder), "isCollidingWithTerrain")]
internal static class GhostPathfinderTerrain
{
    private static bool Prefix(ref bool __result)
    {
        if (!GhostPlacementContext.IgnoreTerrain)
            return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(TransportPathFinder), "canBeSupportedByPillar")]
internal static class GhostPathfinderSupports
{
    private static bool Prefix(int nodeIndex, ref bool __result)
    {
        if (!GhostPlacementContext.IgnoreTerrain)
            return true;
        // Preserve the game's encoded-node bounds check.
        __result = nodeIndex < 262144;
        return false;
    }
}

[HarmonyPatch(typeof(EntitiesManager), "addEntityInternal")]
internal static class GhostRegisterEntity
{
    private static void Prefix(IEntity entity, EntityAddReason reasonToAdd)
    {
        if (GhostPlacementContext.CreateGhosts && reasonToAdd == EntityAddReason.New && entity is IStaticEntity)
        {
            var state = GhostWorldState.Current ??
                throw new InvalidOperationException("Ghost placement state is not initialized.");
            state.Add(entity.Id);
        }
    }
}

[HarmonyPatch(typeof(EntitiesManager), "removeEntityInternal")]
internal static class GhostForgetEntity
{
    private static void Postfix(IEntity entity) => GhostWorldState.Current?.Remove(entity.Id);
}

[HarmonyPatch(typeof(ConstructionManager), nameof(ConstructionManager.StartConstruction))]
internal static class GhostPauseConstruction
{
    private static void Prefix(IStaticEntity staticEntity, ref bool? constructionPauseOverride)
    {
        if (GhostWorldState.IsGhost(staticEntity) || GhostPlacementContext.CreatePlans)
            constructionPauseOverride = true;
    }
}

[HarmonyPatch(typeof(ConstructionManager), nameof(ConstructionManager.TrySetConstructionPause))]
internal static class GhostPreventUnpause
{
    private static bool Prefix(IStaticEntity entity, bool pause, ref bool __result)
    {
        if (pause || !GhostWorldState.IsGhost(entity))
            return true;
        Logger.Warning("Ghosts cannot start construction before validation. Use the Ghost planner tool first.");
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ConstructionManager), nameof(ConstructionManager.TryPerformQuickDeliveryOrRemoval),
    new[] { typeof(IStaticEntity), typeof(bool) })]
internal static class GhostPreventQuickBuild
{
    private static bool Prefix(IStaticEntity entity, ref bool __result)
    {
        if (!GhostWorldState.IsGhost(entity) || entity.ConstructionState != ConstructionState.InConstruction)
            return true;
        Logger.Warning("Ghosts cannot be quick-built before conversion to validated plans.");
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ConstructionManager), "finishConstruction")]
internal static class GhostPreventCompletion
{
    private static bool Prefix(IStaticEntity staticEntity)
    {
        if (!GhostWorldState.IsGhost(staticEntity))
            return true;
        Logger.Warning("Blocked construction completion for an unconverted ghost.");
        return false;
    }
}

[HarmonyPatch(typeof(TransportsManager), "checkTransportSupportedAt")]
internal static class GhostPreventSupportCollapse
{
    private static bool Prefix(Transport transport) => !GhostWorldState.IsGhost(transport);
}

[HarmonyPatch(typeof(StaticEntitiesTerrainInteractionManager), "processTileHeightChange")]
internal static class GhostIgnoreTerrainChanges
{
    private static bool Prefix(IStaticEntity __0) => !GhostWorldState.IsGhost(__0);
}

[HarmonyPatch(typeof(TransportsManager), "tryMergeTransports")]
internal static class GhostPreventMerging
{
    private static bool Prefix(Transport __0, Transport __1, ref bool __result)
    {
        if (!GhostWorldState.IsGhost(__0) && !GhostWorldState.IsGhost(__1))
            return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(TransportsManager), "cutOutTransport")]
internal static class GhostSplitTransportScope
{
    private static void Prefix(CanCutOutTransportResult canCutResult, out (bool, bool) __state) =>
        __state = GhostPlacementContext.EnterTransportSplit(GhostWorldState.IsGhost(canCutResult.ReplacedTransport));

    private static void Finalizer((bool, bool) __state) => GhostPlacementContext.Exit(__state);
}

[HarmonyPatch]
internal static class GhostDisableReplacementPreview
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method("Mafi.Unity.InputControl.Factory.LayoutEntityPreview:tryGetEntityToReplace");

    private static bool Prefix(ref Mafi.Unity.InputControl.Factory.ReplacementStatus __result)
    {
        if (!GhostPlacementContext.IgnoreTerrain)
            return true;
        __result = Mafi.Unity.InputControl.Factory.ReplacementStatus.None;
        return false;
    }

    [HarmonyPatch(typeof(LayoutEntityPreview), "get_ValidationResult")]
    internal static class GhostRejectOccupiedIdentity
    {
        private static void Postfix(LayoutEntityPreview __instance, bool ___m_isForRemoval,
            ref EntityValidationResult? __result)
        {
            if (GhostModeManager.IsGhostModeEnabled && !___m_isForRemoval && __instance.IsResultIdentity)
                __result = EntityValidationResult.CreateError(Tr.GhostBuildingCollision, "Ghost placement cannot replace an existing building.");
        }
    }

    internal static class GhostPreviewModeCache
    {
        private sealed class Mode
        {
            public bool Enabled;
        }

        private static readonly ConditionalWeakTable<object, Mode> s_modes = new ConditionalWeakTable<object, Mode>();

        internal static bool HasChanged(object preview)
        {
            Mode mode = s_modes.GetValue(preview, _ => new Mode());
            bool enabled = GhostModeManager.IsGhostModeEnabled;
            if (mode.Enabled == enabled)
                return false;
            mode.Enabled = enabled;
            return true;
        }
    }

    [HarmonyPatch(typeof(LayoutEntityPreview), "SimUpdate")]
    internal static class GhostRefreshLayoutPreview
    {
        private static void Prefix(LayoutEntityPreview __instance, ref int ___m_simProcessedVersion)
        {
            if (GhostPreviewModeCache.HasChanged(__instance))
                ___m_simProcessedVersion = -1;
        }
    }

    [HarmonyPatch(typeof(StaticTransportPreview), "SimUpdate")]
    internal static class GhostRefreshTransportPreview
    {
        private static void Prefix(StaticTransportPreview __instance)
        {
            if (GhostPreviewModeCache.HasChanged(__instance))
                __instance.Invalidate();
        }
    }

    [HarmonyPatch]
    internal static class GhostDisableTransportReplacementPreview
    {
        private static readonly AccessTools.FieldRef<StaticTransportPreview, ReplacementStatus> s_replacement =
            AccessTools.FieldRefAccess<StaticTransportPreview, ReplacementStatus>("<ReplacementStatus>k__BackingField");

        [HarmonyPatch(typeof(StaticTransportPreview), "get_ReplacementStatus")]
        [HarmonyPostfix]
        private static void Replacement(bool ___m_isForRemoval, ref ReplacementStatus __result)
        {
            if (GhostModeManager.IsGhostModeEnabled && !___m_isForRemoval)
                __result = ReplacementStatus.None;
        }

        [HarmonyPatch(typeof(StaticTransportPreview), "get_ValidationResult")]
        [HarmonyPostfix]
        private static void Validation(StaticTransportPreview __instance, bool ___m_isForRemoval,
            ref EntityValidationResult? __result)
        {
            if (GhostModeManager.IsGhostModeEnabled && !___m_isForRemoval && s_replacement(__instance).HasValue)
                __result = EntityValidationResult.CreateError(Tr.GhostBuildingCollision, "Ghost placement cannot replace an existing transport.");
        }
    }
}
