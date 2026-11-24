using HarmonyLib;
using Mafi.Collections;
using Mafi.Localization;
using Mafi.Unity.InputControl.GameMenu.Settings;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;

namespace GhostBuildOption;

[HarmonyPatch(typeof(MiscTab), MethodType.Constructor, typeof(Mafi.Unity.InputControl.Messages.ITutorialProgressCleaner))]
internal static class GhostAppearanceSettings
{
    private static void Postfix(MiscTab __instance)
    {
        var config = GhostBuildOption.ActiveConfig;
        if (config == null)
            return;
        var column = new Column(2.pt())
            .AlignItemsStretch()
            .PaddingLeft(4.pt())
            .Width(60.Percent());
        var colorError = new Label().Color(Theme.DangerColor).FontSize(12).Hide();
        var opacityError = new Label().Color(Theme.DangerColor).FontSize(12).Hide();
        void Set(string name, object value, Label error)
        {
            if (!config.TrySetValue(name, value, out string message))
                error.Value(message.AsLoc()).Show();
            else
                error.Hide();
        }
        var color = new TextField().Label(Tr.GhostColor).LabelWidth(50.Percent())
            .Tooltip(Tr.GhostColorTooltip).CharLimit(7)
            .Text(config.GetString("ghost_color", "#FFFF00")).ClearFocusOnReturn();
        color.OnEditEnd(value => Set("ghost_color", value, colorError));
        var opacity = new Slider().Label(Tr.GhostTintOpacity).LabelWidth(50.Percent())
            .LabelTooltip(Tr.GhostTintOpacityTooltip).Range(0, 100)
            .Value((float)config.GetDouble("ghost_tint_opacity", 20), false)
            .OnValueChanged((_, value) => Set("ghost_tint_opacity", (double)value, opacityError));
        column.Add(new SettingsTitle(Tr.GhostAppearance), color, colorError, opacity, opacityError,
            new Label(Tr.GhostAppearanceReload).Color(Theme.InactiveColor).FontSize(12));
        __instance.Add(column);
    }
}

internal static class GhostShortcutLayout
{
    private static bool IsGhostShortcut(KbHandle handle) =>
        handle.Id == "GhostBuildOption.ToggleGhostMode" || handle.Id == "GhostBuildOption.ConvertGhostsToPlans";

    internal static bool Arrange(string ownerId, Lyst<KeyValuePair<KbGroup, Lyst<KbHandle>>> sections,
        IEnumerable<KbHandle> registered)
    {
        if (ownerId == "GhostBuildOption")
            return !sections.SelectMany(section => section.Value).All(IsGhostShortcut);
        if (ownerId != "")
            return true;

        var tools = sections.First(section => section.Key == KbGroups.Tools).Value;
        foreach (var handle in registered.Where(IsGhostShortcut))
        {
            string anchor = handle.Id == "GhostBuildOption.ToggleGhostMode" ?
                nameof(Shortcuts.TogglePlanningMode) : nameof(Shortcuts.ToggleUpgradeTool);
            int index = tools.FindIndex(tool => tool.Id == anchor);
            if (index < 0)
                throw new InvalidOperationException($"Native shortcut {anchor} was not found in the Tools section.");
            if (!tools.Contains(handle))
                tools.Insert(index + 1, handle);
        }
        return true;
    }
}

[HarmonyPatch(typeof(ControlsTab), "buildOwnerBlock")]
internal static class GhostShortcutSettings
{
    private static bool Prefix(string ownerId, Lyst<KeyValuePair<KbGroup, Lyst<KbHandle>>> sections,
        ref UiComponent __result)
    {
        if (GhostShortcutLayout.Arrange(ownerId, sections, ShortcutsRegistry.All.Select(handle => handle)))
            return true;
        __result = new Column();
        return false;
    }
}
