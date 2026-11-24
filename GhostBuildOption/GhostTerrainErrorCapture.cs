using HarmonyLib;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Validators;
using Mafi.Core.Terrain;
using Mafi.Localization;
using System.Reflection.Emit;

namespace GhostBuildOption;

[HarmonyPatch(typeof(StaticEntitiesTerrainInteractionManager), nameof(StaticEntitiesTerrainInteractionManager.CanAdd))]
internal static class GhostGroundErrorCapture
{
    private static LocStr Record(LocStr message, IEntityWithOccupiedTilesAddRequest request)
    {
        GhostValidationCapture.ErrorsFor(request)?.Add(message);
        return message;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int matches = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field &&
                field.DeclaringType == typeof(Mafi.Core.Tr) && field.FieldType == typeof(LocStr))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return CodeInstruction.Call(typeof(GhostGroundErrorCapture), nameof(Record));
                matches++;
            }
        }
        if (matches != 5)
            throw new InvalidOperationException("The native ground validation error branches changed.");
    }
}

[HarmonyPatch]
internal static class GhostTerrainErrorCapture
{
    private delegate bool CheckTile(LayoutEntityTerrainValidator validator, OccupiedTileRelative tile,
        Tile3i origin, out LocStrFormatted message, out string debug);
    private delegate bool CheckVertex(LayoutEntityTerrainValidator validator, OccupiedVertexRelative vertex,
        Tile3i origin, bool ignoreOcean, ILayoutEntityAddRequest request, out LocStrFormatted message, out string debug);
    private static readonly CheckTile s_checkTile = AccessTools.MethodDelegate<CheckTile>(
        AccessTools.Method(typeof(LayoutEntityTerrainValidator), "isValidTile"));
    private static readonly CheckVertex s_checkVertex = AccessTools.MethodDelegate<CheckVertex>(
        AccessTools.Method(typeof(LayoutEntityTerrainValidator), "isValidVertex"));

    private static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(LayoutEntityTerrainValidator))
        .Single(method => method.Name.EndsWith(".CanAdd", StringComparison.Ordinal));

    private static void Postfix(LayoutEntityTerrainValidator __instance, ILayoutEntityAddRequest addRequest,
        TerrainManager ___m_terrain, EntityValidationResult __result)
    {
        var errors = GhostValidationCapture.ErrorsFor(addRequest);
        if (errors == null || __result.IsSuccess || __result.ValidationStatus == EntityValidationResultStatus.FatalError)
            return;
        var origin = addRequest.Transform.Position;
        for (int i = 0; i < addRequest.OccupiedTiles.Length; i++)
        {
            var tile = addRequest.OccupiedTiles[i];
            if (___m_terrain.IsValidCoord(origin.Xy + tile.RelCoord) &&
                !s_checkTile(__instance, tile, origin, out var message, out _))
            {
                errors.Add(message);
                addRequest.SetTileError(i);
            }
        }
        bool ignoreOcean = addRequest.IgnoreOceanValidation ||
            (addRequest.ReasonToAdd == EntityAddReason.Move && addRequest.Proto is Mafi.Core.Buildings.Shipyard.ShipyardProto);
        foreach (var vertex in addRequest.OccupiedVertices)
            if (___m_terrain.IsValidCoord(origin.Xy + vertex.RelCoord) &&
                !s_checkVertex(__instance, vertex, origin, ignoreOcean, addRequest, out var message, out _))
            {
                errors.Add(message);
                addRequest.SetTileError(vertex.LowestTileIndex);
            }
    }
}
