using Mafi.Localization;

namespace GhostBuildOption;

public class Tr
{
    public static readonly LocStr GhostConversionFailedSection = Loc.Str("GhostBuildOption_ConversionFailedSection", "Conversion failed", "title of the inspector section listing ghost conversion errors");
    public static readonly LocStr GhostConversionFailedHeading = Loc.Str("GhostBuildOption_ConversionFailedHeading", "Last ghost conversion failed", "conversion error heading");
    public static readonly LocStr GhostConversionUnknownError = Loc.Str("GhostBuildOption_ConversionUnknownError", "Placement validation failed without an error description.", "conversion error fallback");
    public static readonly LocStr GhostAppearance = Loc.Str("GhostBuildOption_Appearance", "Ghost Build appearance", "heading for ghost color and tint opacity settings");

    public static readonly LocStr GhostColor = Loc.Str("GhostBuildOption_Color", "Ghost color", "ghost appearance setting label");
    public static readonly LocStr GhostColorTooltip = Loc.Str("GhostBuildOption_ColorTooltip", "Tint color as #RRGGBB. Blended over the native planning color using tint opacity.", "ghost color setting tooltip");
    public static readonly LocStr GhostTintOpacity = Loc.Str("GhostBuildOption_TintOpacity", "Ghost tint opacity", "ghost tint opacity setting label");
    public static readonly LocStr GhostTintOpacityTooltip = Loc.Str("GhostBuildOption_TintOpacityTooltip", "0% keeps the native planning color; 100% uses the full selected color. This changes tint strength, not model transparency.", "ghost tint opacity tooltip");
    public static readonly LocStr GhostAppearanceReload = Loc.Str("GhostBuildOption_AppearanceReload", "Save and reload the colony to apply appearance changes.", "appearance settings reminder");

    public static readonly LocStr GhostBuildResearch = Loc.Str("GhostBuildOption_GhostBuildResearch", "Ghost Build", "name of the ghost build technology shown in research unlocks");

    public static readonly LocStr GhostPlansResearch = Loc.Str("GhostBuildOption_GhostPlansResearch", "Ghost planner tool", "name of the ghost conversion selection tool shown in research unlocks");

    public static readonly LocStr GhostMode = Loc.Str("GhostBuildOption_GhostMode", "Ghost Build", "title of the ghost build mode toggle");

    public static readonly LocStr GhostModeActive__Title = Loc.Str("GhostBuildOption_GhostModeActive__Title", "Ghost mode on", "label showing that Ghost mode is currently enabled, should be reasonably short");

    public static readonly LocStr GhostModeActive__Tooltip = Loc.Str("GhostBuildOption_GhostModeActive__Tooltip", "Place unbuilt ghosts without terrain collision checks or transport supports. Use the Ghost planner tool to check placement and create normal plans.", "tooltip");

    public static readonly LocStr GhostBuildingCollision = Loc.Str("GhostBuildOption_BuildingCollision", "Another building already occupies this position.", "ghost placement error");

    public static readonly LocStr ConvertGhostsToPlans = Loc.Str("GhostBuildOption_ConvertGhostsToPlans", "Ghost planner tool", "title of the ghost conversion selection tool");

    public static readonly LocStr ConvertGhostsToPlans__Tooltip = Loc.Str("GhostBuildOption_ConvertGhostsToPlans__Tooltip", "Click an item or drag an area. If the selection contains ghosts, validate them into normal plans; otherwise, turn paused construction plans with no delivered materials into ghosts. Failed ghosts keep their warnings. Built structures are unchanged. Right-click or Escape to exit.", "tooltip");

    public static readonly LocStr1 GhostPlannerSummary = Loc.Str1("GhostBuildOption_PlannerSummary", "Ghost planner updated {0} item(s).", "result count for either direction of the ghost planner tool");

    public static readonly LocStr2 GhostConversionSummary = Loc.Str2("GhostBuildOption_ConversionSummary", "Converted {0} ghost(s) to plans; {1} failed and remain ghosts.", "ghost conversion result counts");
    public static readonly LocStr GhostConversionNotGhost = Loc.Str("GhostBuildOption_ConversionNotGhost", "Only ghost entities can be converted.", "ghost conversion error");
    public static readonly LocStr GhostConversionUnsupported = Loc.Str("GhostBuildOption_ConversionUnsupported", "This ghost entity type cannot be converted yet.", "ghost conversion error");
    public static readonly LocStr GhostConversionMissing = Loc.Str("GhostBuildOption_ConversionMissing", "The selected ghost no longer exists.", "ghost conversion error");
    public static readonly LocStr ConvertGhostEntityTooltip = Loc.Str("GhostBuildOption_ConvertEntityTooltip", "Convert this ghost into a normal plan, regardless of its pause or construction state. Normal placement and support checks must succeed.", "ghost construction button tooltip");
}
