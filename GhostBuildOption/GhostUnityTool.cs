using HarmonyLib;
using Mafi.Core.Entities.Static;

namespace GhostBuildOption;

[HarmonyPatch]
internal static class GhostUnityTool
{
    private const string ControllerType = "Mafi.Unity.Ui.Controllers.Tools.UpointsToolInputController";

    private static MethodBase TargetMethod() =>
        AccessTools.Method(ControllerType + ":Matches");

    internal static bool IsActive(IUnityInputMgr input)
    {
        foreach (var controller in input.ActiveControllers)
            if (controller.GetType().FullName == ControllerType)
                return true;
        return false;
    }

    private static bool Prefix(IStaticEntity entity, ref bool __result)
    {
        if (!GhostWorldState.IsGhost(entity))
            return true;
        __result = false;
        return false;
    }
}
