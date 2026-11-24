using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Collections.ReadonlyCollections;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Terrain;
using Mafi.Unity.InputControl.AreaTool;
using Mafi.Unity.Entities;
using Mafi.Unity.Terrain;
using Mafi.Unity.Ui.Controllers.Tools;
using Mafi.Unity.Ui.Library;
using Mafi.Localization;
using Mafi.Core.GameLoop;
using Mafi.Unity.Audio;
using Mafi.Unity.UiStatic;
using Mafi.Unity.UiStatic.Cursors;
using EntityId = Mafi.Core.EntityId;

namespace GhostBuildOption;

[GlobalDependency(RegistrationMode.AsSelf)]
public sealed class GhostConversionInputController : BaseEntityCursorInputController<IStaticEntity>, IHotReloadUi
{
    private readonly CursorMessage m_feedback;
    private readonly Queue<InputCommand<int>> m_pending = new Queue<InputCommand<int>>();

    internal static GhostConversionInputController? Current { get; private set; }

    public GhostConversionInputController(
        ToolbarHud toolbar, UiContext context, CursorPickingManager picker, CursorManager cursors,
        AreaSelectionToolFactory selectionFactory, NewInstanceOf<TerrainAreaOutlineRenderer> outline,
        IEntitiesManager entities, NewInstanceOf<EntityHighlighter> highlighter,
        NewInstanceOf<CursorMessage> feedback)
        : base(toolbar, context, picker, cursors, selectionFactory, outline, entities, highlighter,
            Option.None, Ids.Technology.GhostPlansTool,
            new CursorStyle("GhostBuildOption.Planner", GhostIcons.ConvertCursor, Vector2.zero),
            UiSound.AssignClick, Option<FilterToolbox>.None)
    {
        m_feedback = feedback.Instance;
        IgnorePlacedSurfaces = true;
        SetInstaActionDisabled(true);
        SetEdgeSizeLimit(new RelTile1i(int.MaxValue));
        InitHighlightColors(GhostColors.Apply(ColorRgba.Yellow), GhostColors.Apply(ColorRgba.Yellow));
        toolbar.AddToolButton(Tr.ConvertGhostsToPlans, this, GhostIcons.Convert, 1071f,
            MyShortcutsStorage.ConvertGhostsToPlans, null);
        context.GameLoopEvents.SyncUpdate.AddNonSaveable(this, OnSync);
        Current = this;
    }

    protected override bool Matches(IStaticEntity entity, bool isAreaSelection, bool isLeftClick) =>
        !entity.IsDestroyed && (GhostWorldState.IsGhost(entity) || GhostConversionProcessor.IsPlan(entity));

    protected override bool OnFirstActivated(IStaticEntity hoveredEntity, Lyst<IStaticEntity> selectedEntities,
        Lyst<SubTransport> selectedPartialTransports) => false;

    protected override void OnEntitiesSelected(IIndexable<IStaticEntity> selectedEntities,
        IIndexable<SubTransport> selectedPartialTransports, ImmutableArray<TileSurfaceCopyPasteData> selectedSurfaces,
        ImmutableArray<TileSurfaceCopyPasteData> selectedDecals, bool isAreaSelection, bool isLeftMouse,
        RectangleTerrainArea2i? area)
    {
        if (selectedEntities.Count == 0)
            return;
        m_feedback.Hide();
        m_pending.Enqueue(Context.InputScheduler.ScheduleInputCmd(
            new ToggleGhostsCmd(selectedEntities.ToImmutableArray(entity => entity.Id))));
    }

    public override void Activate()
    {
        base.Activate();
        m_feedback.Hide();
    }

    public override void Deactivate()
    {
        base.Deactivate();
        m_feedback.Hide();
    }

    public void DeactivateSelf() => Context.InputMgr.DeactivateController(this);

    public override bool InputUpdate()
    {
        if (m_pending.Count > 0)
        {
            if (ShortcutsManager.IsSecondaryActionUp)
                DeactivateSelf();
            return true;
        }
        return base.InputUpdate();
    }

    private void OnSync(GameTime time)
    {
        while (m_pending.Count > 0 && m_pending.Peek().IsProcessedAndSynced)
        {
            var completed = m_pending.Dequeue();
            GhostConversionSounds.PlayCompletionSound(Context.UiSounds, completed);
            if (completed.HasError)
                m_feedback.MessageError(completed.ErrorMessage.AsLoc());
            else
                m_feedback.MessageInfo(Tr.GhostPlannerSummary.Format(completed.Result.ToString()));
            m_feedback.Show();
        }
    }

    internal void ConvertEntity(EntityId id)
    {
        m_feedback.Hide();
        m_pending.Enqueue(Context.InputScheduler.ScheduleInputCmd(new ConvertGhostCmd(id)));
    }

    internal void HideCompletionFeedback() => m_feedback.Hide();

    public void DisposeForHotReload()
    {
        if (Current != this)
            return;
        Current = null;
        Context.GameLoopEvents.SyncUpdate.RemoveNonSaveable(this, OnSync);
        m_pending.Clear();
        Context.InputMgr.RemoveGlobalShortcut(this);
        Deactivate();
    }
}

internal static class GhostConversionSounds
{
    internal static void PlayCompletionSound(UiSoundsPlayer sounds, InputCommand<int> completed) =>
        sounds.Play(completed.HasError ? UiSound.InvalidOp : UiSound.AssignClick);
}
