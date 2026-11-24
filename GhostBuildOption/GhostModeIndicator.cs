using HarmonyLib;
using Mafi.Unity.Ui.Controllers;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiStatic;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;

namespace GhostBuildOption;

internal sealed class GhostModeIndicator : IDisposable
{
    private readonly UiContext m_context;
    private readonly BuildModesStripsActivator m_buildModes;
    private readonly TransportBuildController m_transportBuild;
    private readonly Panel m_panel;
    private readonly UiComponent m_background;
    private readonly IComponentWithBackground m_planningBackground;
    private ColorRgba? m_tint;
    private ColorRgba? m_fill;

    internal GhostModeIndicator(ToolbarHud toolbar, UiContext context,
        BuildModesStripsActivator buildModes, TransportBuildController transportBuild)
    {
        m_context = context;
        m_buildModes = buildModes;
        m_transportBuild = transportBuild;
        var modes = ComponentField(toolbar, "m_specialModePanel");
        m_planningBackground = ComponentField(ComponentField(modes, "m_planningModePanel"), "m_background");
        m_panel = new Panel(true).IgnoreInputPicking(true, false)
            .BackgroundStyle(DisplayState.Important).PanelStyleHud();
        m_background = ComponentField(m_panel, "m_background");
        var row = new Row(1.pt()).Tooltip(Tr.GhostModeActive__Tooltip);
        row.Add(new Icon(GhostIcons.Build), new Label(Tr.GhostModeActive__Title),
            new KeyBindUi().SetKeys(MyShortcutsStorage.ToggleGhostMode));
        m_panel.Body.PaddingTopBottom(6.px()).PaddingLeftRight(2.pt()).Add(row);
        modes.Add(m_panel.Hide());
        context.GameLoopEvents.RenderUpdate.AddNonSaveable(this, OnRender);
    }

    private static UiComponent ComponentField(object owner, string name)
    {
        var field = AccessTools.Field(owner.GetType(), name) ??
            throw new MissingFieldException(owner.GetType().FullName, name);
        return field.GetValue(owner) is UiComponent component ? component :
            throw new InvalidOperationException($"Native HUD field {name} has no UI component.");
    }

    private void OnRender(GameTime time)
    {
        bool visible = GhostModeManager.IsGhostModeEnabled && m_buildModes.IsActive &&
            !m_transportBuild.ShouldShowDisableSnappingInfo;
        m_panel.Visible(visible);
        if (!visible)
            return;
        // Keep the native HUD texture, transparency and frame; change only its tint.
        var nativeStyle = m_planningBackground.GetElementForBackground().resolvedStyle;
        Color32 native = nativeStyle.unityBackgroundImageTintColor;
        var tint = GhostColors.Apply(new ColorRgba(native.r, native.g, native.b, native.a));
        if (!m_tint.HasValue || m_tint.Value != tint)
        {
            m_background.BackgroundTint(tint);
            m_tint = tint;
        }
        Color32 nativeFill = nativeStyle.backgroundColor;
        var fill = GhostColors.Apply(new ColorRgba(nativeFill.r, nativeFill.g, nativeFill.b, nativeFill.a));
        if (!m_fill.HasValue || m_fill.Value != fill)
        {
            m_background.Background(fill);
            m_fill = fill;
        }
    }

    public void Dispose()
    {
        m_context.GameLoopEvents.RenderUpdate.RemoveNonSaveable(this, OnRender);
        m_panel.RemoveFromHierarchy();
    }
}
