using HarmonyLib;
using Mafi.Collections;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Validators;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Factory.Zippers;

namespace GhostBuildOption;

[HarmonyPatch(typeof(TransportsManager), nameof(TransportsManager.CanBuildMiniZipperAt))]
internal static class GhostConnectorValidation
{
    [ThreadStatic]
    private static Mafi.Core.EntityId? s_ignoredConnector;

    internal static EntityValidationResult Validate(IEntitiesManager entities, ILayoutEntity entity,
        LayoutEntityAddRequest request, Lyst<IEntityPreAddValidator> preparation)
    {
        var previous = s_ignoredConnector;
        try
        {
            s_ignoredConnector = entity is MiniZipper ? entity.Id : null;
            return entities.CanAdd(request, true, preparation);
        }
        finally
        {
            s_ignoredConnector = previous;
        }
    }

    private static Predicate<Mafi.Core.EntityId> IgnoreConvertingConnector(Predicate<Mafi.Core.EntityId> ignored)
    {
        var connector = s_ignoredConnector;
        return connector.HasValue ? id => id == connector.Value || ignored(id) : ignored;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(TransportsManager), nameof(TransportsManager.IgnorePillarsPredicate));
        int replacements = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.Calls(getter))
            {
                // This native connector check does not use the add request's self-exclusion.
                yield return CodeInstruction.Call(typeof(GhostConnectorValidation), nameof(IgnoreConvertingConnector));
                replacements++;
            }
        }
        if (replacements != 1)
            throw new InvalidOperationException("Could not locate the native connector occupancy predicate.");
    }
}
