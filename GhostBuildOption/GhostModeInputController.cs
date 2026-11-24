namespace GhostBuildOption;

[GlobalDependency(RegistrationMode.AsSelf)]
public sealed class GhostModeInputController : IToolbarItemController, IUnityInputController, IHotReloadUi
{
    private readonly IUnityInputMgr m_inputManager;
    private readonly IInputScheduler m_inputScheduler;
    private readonly GhostPlanningAccess m_planningAccess;
    private static readonly Type s_planningController = typeof(UiContext).Assembly.GetType(
        "Mafi.Unity.Ui.Controllers.Tools.PlanningModeInputController", true);

    public bool IsVisible => m_planningAccess.IsUnlocked;
    public bool DeactivateShortcutsIfNotVisible => true;
    public ControllerConfig Config => ControllerConfig.Mode;

    public event Action<IToolbarItemController>? VisibilityChanged;

    public GhostModeInputController(ToolbarHud toolbar, UiContext context, GhostPlanningAccess planningAccess)
    {
        m_inputManager = context.InputMgr;
        m_inputScheduler = context.InputScheduler;
        m_planningAccess = planningAccess;
        m_planningAccess.Changed += OnVisibilityChanged;
        m_inputManager.ControllerActivated += OnControllerActivated;
        toolbar.AddToolButton(Tr.GhostMode, this,
            GhostIcons.Build, 21f,
            MyShortcutsStorage.ToggleGhostMode, null);
    }

    public void Activate()
    {
        var controllers = m_inputManager.ActiveControllers;
        for (int i = controllers.Count - 1; i >= 0; i--)
            if (controllers[i].GetType() == s_planningController)
                m_inputManager.DeactivateController(controllers[i]);
        GhostModeManager.IsGhostModeEnabled = true;
        m_inputScheduler.ScheduleInputCmd(new SetGhostBuildModeCmd(true));
        Logger.Info("Ghost Build enabled. New buildings and transports remain unbuilt ghosts.");
    }

    public void Deactivate()
    {
        GhostModeManager.IsGhostModeEnabled = false;
        m_inputScheduler.ScheduleInputCmd(new SetGhostBuildModeCmd(false));
        Logger.Info("Ghost Build disabled.");
    }

    public void DeactivateSelf() => m_inputManager.DeactivateController(this);

    public bool InputUpdate() => false;

    private void OnVisibilityChanged() => VisibilityChanged?.Invoke(this);

    private void OnControllerActivated(IUnityInputController controller)
    {
        if (controller.GetType() == s_planningController && GhostModeManager.IsGhostModeEnabled)
            DeactivateSelf();
    }

    public void DisposeForHotReload()
    {
        m_planningAccess.Changed -= OnVisibilityChanged;
        m_inputManager.ControllerActivated -= OnControllerActivated;
        m_inputManager.RemoveGlobalShortcut(this);
        Deactivate();
    }
}
