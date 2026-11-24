namespace GhostBuildOption;

public static class MyShortcutsStorage
{
    public static readonly KbHandle ToggleGhostMode = KbHandleBuilder
        .Start("GhostBuildOption.ToggleGhostMode", Tr.GhostMode)
        .SetGroup(KbGroups.Tools)
        .SetPrimary(KbModifiers.Shift, KeyCode.B)
        .SetTooltip(Tr.GhostModeActive__Tooltip)
        .Build();

    public static readonly KbHandle ConvertGhostsToPlans = KbHandleBuilder
        .Start("GhostBuildOption.ConvertGhostsToPlans", Tr.ConvertGhostsToPlans)
        .SetGroup(KbGroups.Tools)
        .SetPrimary(KbModifiers.Shift, KeyCode.G)
        .SetTooltip(Tr.ConvertGhostsToPlans__Tooltip)
        .Build();
}
