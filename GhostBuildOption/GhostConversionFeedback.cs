using HarmonyLib;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.GameLoop;
using Mafi.Core.Gfx;
using Mafi.Unity.Entities;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.UiToolkit.Library.FloatingPanel;
using UnityEngine.EventSystems;
using EntityId = Mafi.Core.EntityId;

namespace GhostBuildOption;

internal sealed class GhostConversionFeedback : IDisposable
{
    private static readonly IconSpec s_icon = new IconSpec(GhostIcons.Warning, ColorRgba.White, 1f);
    private readonly GhostWorldState m_state;
    private readonly UiContext m_context;
    private readonly CursorPickingManager m_picker;
    private readonly EntitiesIconRenderer m_icons;
    private readonly CursorMessage m_hover;
    private readonly GhostFailureGrid m_grid;
    private readonly Dictionary<EntityId, IStaticEntity> m_displayed = new Dictionary<EntityId, IStaticEntity>();
    private readonly Dictionary<GameObject, IStaticEntity> m_iconObjects = new Dictionary<GameObject, IStaticEntity>();
    private readonly Dictionary<IEntityInspector, (PanelWithHeader Panel, InspectorStatusDisplay Status)> m_sections =
        new Dictionary<IEntityInspector, (PanelWithHeader, InspectorStatusDisplay)>();
    private GhostConversionFailure? m_hovered;
    internal static GhostConversionFeedback? Current { get; private set; }

    internal GhostConversionFeedback(GhostWorldState state, UiContext context, CursorPickingManager picker,
        EntitiesIconRenderer icons, CursorMessage hover, AssetsDb assets)
    {
        m_state = state;
        m_context = context;
        m_picker = picker;
        m_icons = icons;
        m_hover = hover;
        m_hover.Label.TextOverflow(TextOverflow.Wrap).MaxWidth(480.px());
        m_grid = new GhostFailureGrid(assets, context.TerrainManager, context.TerrainRenderer);
        Current = this;
        context.GameLoopEvents.SyncUpdate.AddNonSaveable(this, OnSync);
        context.GameLoopEvents.RenderUpdate.AddNonSaveable(this, OnRender);
    }

    private void OnSync(GameTime time)
    {
        foreach (var pair in m_displayed.ToArray())
            if (pair.Value.IsDestroyed || !m_state.TryGetFailure(pair.Key, out _))
            {
                if (!pair.Value.IsDestroyed)
                    m_icons.RemoveIcon(s_icon, pair.Value);
                m_displayed.Remove(pair.Key);
                foreach (var icon in m_iconObjects.Where(icon => icon.Value.Id == pair.Key).ToArray())
                    m_iconObjects.Remove(icon.Key);
            }
        foreach (var pair in m_state.Failures)
            if (!m_displayed.ContainsKey(pair.Key) &&
                m_context.EntitiesManager.TryGetEntity<IStaticEntity>(pair.Key, out var entity) && !entity.IsDestroyed)
            {
                m_icons.AddIcon(s_icon, entity);
                m_displayed.Add(pair.Key, entity);
            }

        foreach (var section in m_sections.Values)
            section.Panel.Visible(false);
        foreach (var controller in m_context.InputMgr.ActiveControllers)
        {
            if (!(controller is IEntityInspector inspector) || !inspector.IsActive ||
                inspector.EntityUntyped == null || !m_state.TryGetFailure(inspector.EntityUntyped.Id, out var failure))
                continue;
            if (!m_sections.TryGetValue(inspector, out var section))
            {
                if (!(inspector is Window window))
                    throw new InvalidOperationException("The inspector has no window for ghost conversion errors.");
                var status = new InspectorStatusDisplay().FlexShrink(0);
                status.SetTextOverflow(TextOverflow.Wrap);
                var panel = new PanelWithHeader(Tr.GhostConversionFailedSection).FlexShrink(0);
                panel.Body.PaddingTopBottom(8.px()).PaddingLeftRight(8.px()).Add(status);
                // Connector inspectors remove their normal header/status column.
                window.Body.InsertAt(0, panel);
                section = (panel, status);
                m_sections.Add(inspector, section);
            }
            section.Panel.Title(Tr.GhostConversionFailedSection).Visible(true);
            section.Status.As(failure.ErrorList, DisplayState.Danger);
        }
    }

    internal void IconCreated(IconSpec spec, IRenderedEntity entity, GameObject go)
    {
        m_iconObjects.Remove(go);
        if (spec.Path == GhostIcons.Warning && entity is IStaticEntity building)
            m_iconObjects[go] = building;
    }

    private void OnRender(GameTime time)
    {
        GhostConversionFailure? failure = null;
        if (!GhostUnityTool.IsActive(m_context.InputMgr) &&
            (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            if (m_picker.TryPickEntity<IStaticEntity>(out var picked))
                m_state.TryGetFailure(picked.Id, out failure);
            if (failure == null && m_icons.AllIconsVisible)
            {
                float nearest = float.MaxValue;
                foreach (var pair in m_iconObjects)
                {
                    if (!pair.Key || !pair.Key.activeInHierarchy || pair.Value.IsDestroyed)
                        continue;
                    Vector3 screen = m_picker.MainCamera.WorldToScreenPoint(pair.Key.transform.position);
                    if (screen.z > 0 && screen.z < nearest &&
                        Mathf.Abs(screen.x - Input.mousePosition.x) <= 16 &&
                        Mathf.Abs(screen.y - Input.mousePosition.y) <= 16 &&
                        m_state.TryGetFailure(pair.Value.Id, out var iconFailure))
                    {
                        failure = iconFailure;
                        nearest = screen.z;
                    }
                }
            }
        }
        if (!ReferenceEquals(failure, m_hovered))
        {
            m_hovered = failure;
            if (failure == null)
                m_hover.Hide();
            else
                m_hover.MessageError(failure.Text);
        }
        if (failure != null)
            GhostConversionInputController.Current?.HideCompletionFeedback();
        m_grid.Render(failure);
    }

    public void Dispose()
    {
        m_context.GameLoopEvents.SyncUpdate.RemoveNonSaveable(this, OnSync);
        m_context.GameLoopEvents.RenderUpdate.RemoveNonSaveable(this, OnRender);
        foreach (var entity in m_displayed.Values)
            if (!entity.IsDestroyed)
                m_icons.RemoveIcon(s_icon, entity);
        foreach (var section in m_sections.Values)
            section.Panel.RemoveFromHierarchy();
        m_displayed.Clear();
        m_iconObjects.Clear();
        m_sections.Clear();
        m_hover.Hide();
        m_grid.Dispose();
        Current = null;
    }
}

[HarmonyPatch(typeof(EntitiesIconRenderer), "createIconGo")]
internal static class GhostFailureIconPicking
{
    private static void Postfix(IconSpec iconSpec, IRenderedEntity entity, GameObject __result) =>
        GhostConversionFeedback.Current?.IconCreated(iconSpec, entity, __result);
}
