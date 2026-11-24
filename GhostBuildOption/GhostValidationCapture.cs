using HarmonyLib;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Validators;
using System.Reflection.Emit;

namespace GhostBuildOption;

[HarmonyPatch(typeof(EntityValidators), nameof(EntityValidators.CallAllAddValidatorsFor))]
internal static class GhostValidationCapture
{
    [ThreadStatic]
    private static Capture? s_current;

    internal sealed class Capture : IDisposable
    {
        internal readonly IEntityAddRequest Request;
        internal readonly GhostConversionErrors Errors;
        private readonly Capture? m_previous;

        internal Capture(IEntityAddRequest request, GhostConversionErrors errors)
        {
            Request = request;
            Errors = errors;
            m_previous = s_current;
            s_current = this;
        }

        public void Dispose() => s_current = m_previous;
    }

    private static EntityValidationResult Record(EntityValidationResult result, IEntityAddRequest request)
    {
        if (s_current != null && ReferenceEquals(request, s_current.Request))
            s_current.Errors.Add(result);
        return result;
    }

    internal static GhostConversionErrors? ErrorsFor(IEntityAddRequest request) =>
        s_current != null && ReferenceEquals(request, s_current.Request) ? s_current.Errors : null;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int matches = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.operand is MethodInfo method && method.Name == "CanAdd" &&
                method.DeclaringType?.Name == "IAddValidatorWrapper")
            {
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return CodeInstruction.Call(typeof(GhostValidationCapture), nameof(Record));
                matches++;
            }
        }
        if (matches != 1)
            throw new InvalidOperationException("Could not locate native per-validator conversion diagnostics.");
    }
}
