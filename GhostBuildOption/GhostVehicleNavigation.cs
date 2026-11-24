using HarmonyLib;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.PathFinding;
using Mafi.Core.Terrain;
using Mafi.Core.Vehicles;
using Mafi.Core.Buildings.Cargo;
using Mafi.Core.Buildings.Shipyard;
using System.Reflection.Emit;
using EntityId = Mafi.Core.EntityId;

namespace GhostBuildOption;

[HarmonyPatch(typeof(TerrainOccupancyManager), nameof(TerrainOccupancyManager.GetHeightClearance))]
internal static class GhostVehicleClearance
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code = instructions.ToList();
        Type record = typeof(TerrainOccupancyManager).GetNestedType("OccupancyData", BindingFlags.NonPublic);
        FieldInfo next = AccessTools.Field(record, "OverflowIndex");
        FieldInfo entityId = AccessTools.Field(record, "EntityId");
        MethodInfo from = AccessTools.PropertyGetter(record, "From");
        MethodInfo isGhost = AccessTools.Method(typeof(GhostWorldState), nameof(GhostWorldState.IsGhost), new[] { typeof(EntityId) });
        int headNext = code.FindIndex(instruction => instruction.LoadsField(next)) - 1;
        int tailNext = code.FindLastIndex(instruction => instruction.LoadsField(next)) - 1;
        int headCheck = code.FindIndex(instruction => instruction.Calls(from)) - 2;
        int tailCheck = code.FindIndex(headNext + 2, instruction => instruction.Calls(from)) - 2;
        if (headNext < 0 || tailNext <= headNext || headCheck < 0 || tailCheck <= headNext ||
            code[headCheck].opcode != OpCodes.Ldarg_2 || code[tailCheck].opcode != OpCodes.Ldarg_2)
            throw new InvalidOperationException("The native height-clearance record traversal changed.");

        Label skipHead = generator.DefineLabel();
        Label skipTail = generator.DefineLabel();
        code[headNext].labels.Add(skipHead);
        code[tailNext].labels.Add(skipTail);
        for (int i = 0; i < code.Count; i++)
        {
            if (i == headCheck || i == tailCheck)
            {
                CodeInstruction load = code[i == headCheck ? headNext : tailNext];
                var check = new CodeInstruction(load.opcode, load.operand);
                check.labels.AddRange(code[i].labels);
                code[i].labels.Clear();
                yield return check;
                yield return new CodeInstruction(OpCodes.Ldfld, entityId);
                yield return new CodeInstruction(OpCodes.Call, isGhost);
                yield return new CodeInstruction(OpCodes.Brtrue, i == headCheck ? skipHead : skipTail);
            }
            yield return code[i];
        }
    }
}

[HarmonyPatch]
internal static class GhostVehicleSurfaces
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(VehicleSurfaceProvider), "entityAdded");
        yield return AccessTools.Method(typeof(VehicleSurfaceProvider), "entityRemoved");
    }

    private static bool Prefix(IStaticEntity entity) => !GhostWorldState.IsGhost(entity);
}

[HarmonyPatch]
internal static class GhostShipClearance
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ShipsClearancePathabilityProvider), "IsOceanTileBlocked",
            new[] { typeof(Tile2iIndex), typeof(Tile2i), typeof(HeightTilesF), typeof(ThicknessTilesI) });
        yield return AccessTools.Method(typeof(ShipsClearancePathabilityProvider), "isChunkBlockedExcludingArea");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo native = AccessTools.Method(typeof(TerrainOccupancyManager), nameof(TerrainOccupancyManager.IsOccupiedInRange));
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(native))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(GhostShipClearance), nameof(IsOccupied));
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Expected one ship occupancy check, found {replaced}.");
    }

    private static bool IsOccupied(TerrainOccupancyManager occupancy, Tile3i position,
        ThicknessTilesI height, Predicate<EntityId>? ignoredIds) =>
        occupancy.IsOccupiedInRange(position, height, CombineIgnoredIds(ignoredIds));

    private static readonly Predicate<EntityId> s_ignoreGhosts = GhostWorldState.IsGhost;

    private static Predicate<EntityId> CombineIgnoredIds(Predicate<EntityId>? ignoredIds) =>
        ignoredIds == null ? s_ignoreGhosts : id => s_ignoreGhosts(id) || ignoredIds(id);
}

[HarmonyPatch]
internal static class GhostOceanReservations
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string method in new[] { "registerNewEntity", "removeRegisteredEntity", "NotifyEntityAreaChanged" })
            yield return AccessTools.Method(typeof(StaticEntityOceanReservationManagerV2), method);
    }

    private static bool Prefix(IStaticEntityWithReservedOceanV2 __0) => !GhostWorldState.IsGhost(__0);
}

[HarmonyPatch]
internal static class GhostDockBlockedArea
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (Type type in new[] { typeof(Shipyard), typeof(CargoDepot), typeof(TradeDock) })
            yield return AccessTools.PropertyGetter(type, "OceanAreaBlocked");
    }

    private static bool Prefix(IStaticEntity __instance, ref RectangleTerrainArea2i __result)
    {
        if (!GhostWorldState.IsGhost(__instance))
            return true;
        __result = default;
        return false;
    }
}

[HarmonyPatch]
internal static class GhostDockBlockedFlag
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.PropertyGetter(typeof(Shipyard), "IsOceanAreaBlocked");
        yield return AccessTools.PropertyGetter(typeof(CargoDepot), "IsOceanAreaBlocked");
    }

    private static bool Prefix(IStaticEntity __instance, ref bool __result)
    {
        if (!GhostWorldState.IsGhost(__instance))
            return true;
        __result = false;
        return false;
    }
}

internal static class GhostVehicleNavigation
{
    private static readonly Action<ClearancePathabilityProvider, Tile2i> s_recompute =
        AccessTools.MethodDelegate<Action<ClearancePathabilityProvider, Tile2i>>(
            AccessTools.Method(typeof(ClearancePathabilityProvider), "RecomputeDataAt"));

    internal static void RefreshExistingGhosts(GhostWorldState state, IEntitiesManager entities,
        ClearancePathabilityProvider pathability, ShipsClearancePathabilityProvider ships, TerrainManager terrain)
    {
        var ghosts = new List<IStaticEntity>();
        foreach (EntityId id in state.GhostIds)
        {
            if (!entities.TryGetEntity<IStaticEntity>(id, out var entity))
            {
                Logger.Warning($"Saved ghost {id} no longer exists.");
                continue;
            }
            ghosts.Add(entity);
        }
        RefreshEntities(ghosts, pathability, ships, terrain);
    }

    internal static void RefreshEntities(IEnumerable<IStaticEntity> entities,
        ClearancePathabilityProvider pathability, ShipsClearancePathabilityProvider ships, TerrainManager terrain)
    {
        var changed = new HashSet<Tile2i>();
        foreach (var entity in entities)
        {
            foreach (var tile in entity.OccupiedTiles)
            {
                Tile2i coord = entity.CenterTile.Xy + tile.RelCoord;
                for (int x = -1; x <= 0; x++)
                    for (int y = -1; y <= 0; y++)
                    {
                        Tile2i affected = coord + new RelTile2i(x, y);
                        if (terrain.IsValidCoord(affected))
                            changed.Add(affected);
                    }
            }
        }
        foreach (Tile2i coord in changed)
        {
            s_recompute(pathability, coord);
            ships.NotifyTileHeightChanged(coord);
        }
    }
}
