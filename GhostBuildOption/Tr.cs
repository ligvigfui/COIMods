using Mafi.Localization;

namespace GhostBuildOption;

public class Tr
{
    public static readonly LocStr GhostMode = Loc.Str("GhostMode", "Ghost mode", "title of a button that enabled or disables Ghost mode (mode in which buildings can be built where ever)");

    public static readonly LocStr GhostModeActive__Title = Loc.Str("GhostModeActive__Title", "Ghost mode on", "label showing that Ghost mode is currently enabled, should be reasonably short");

    public static readonly LocStr GhostModeActive__Tooltip = Loc.Str("GhostModeActive__Tooltip", "Ghost mode is enabled which means buildings can be built almost anywhere and construction is paused", "tooltip");
}
