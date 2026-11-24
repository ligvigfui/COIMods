using HarmonyLib;
using Mafi.Core.Entities.Static;
using Mafi.Core.Syncers;
using Mafi.Unity.Ui.Inspectors;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;

namespace GhostBuildOption;

internal static class GhostConstructionActions
{
    internal static IStaticEntity? GetGhost(IConstructionProgress? progress)
    {
        if (progress is not IEntityConstructionProgress single || progress is GroupedConstructionProgressForUi)
            return null;
        var entity = single.Entity;
        return entity != null && GhostWorldState.IsGhost(entity) ? entity : null;
    }

    internal static void Convert(ConstructionUi ui)
    {
        var entity = GetGhost(ui.Progress);
        if (entity == null)
        {
            Logger.Warning("The inspected entity is no longer a ghost; conversion was not scheduled.");
            return;
        }
        var controller = GhostConversionInputController.Current ??
            throw new InvalidOperationException("Ghost conversion UI is not initialized.");
        controller.ConvertEntity(entity.Id);
    }
}

[HarmonyPatch]
internal static class GhostConstructionQuickActions
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ConstructionUi), nameof(ConstructionUi.AddQuickBuildBtn));
        yield return AccessTools.Method(typeof(ConstructionUi), nameof(ConstructionUi.AddQuickRemoveBtn));
    }

    private static void Postfix(ConstructionUi __instance)
    {
        var button = __instance.Buttons.AllChildren.Last();
        int index = __instance.Buttons.ChildrenCount - 1;
        button.RemoveFromHierarchy();
        var container = new Row();
        container.Add(button);
        __instance.Buttons.InsertAt(index, container, false);
        // Keep native cost visibility inside the ghost-only visibility gate.
        __instance.Observe(() => GhostConstructionActions.GetGhost(__instance.Progress) == null && button.IsVisible())
            .Do(visible => container.Visible(visible));
    }
}

[HarmonyPatch(typeof(ConstructionUi), nameof(ConstructionUi.AddPauseBtn))]
internal static class GhostConstructionPause
{
    private static void Postfix(ConstructionUi __instance)
    {
        var pause = __instance.Buttons.AllChildren.Last();
        int index = __instance.Buttons.ChildrenCount - 1;
        var convert = new ButtonIcon(Button.Warning, GhostIcons.Convert, () => GhostConstructionActions.Convert(__instance))
            .Compact().Tooltip(Tr.ConvertGhostEntityTooltip).Hide();
        __instance.Buttons.InsertAt(index, convert, false);
        __instance.Observe(() => GhostConstructionActions.GetGhost(__instance.Progress) != null).Do(ghost =>
        {
            pause.Visible(!ghost);
            convert.Visible(ghost);
        });
    }
}
