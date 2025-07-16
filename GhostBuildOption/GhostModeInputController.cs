namespace GhostBuildOption;

[GlobalDependency(RegistrationMode.AsEverything, false, false)]
internal class GhostModeInputController : IToolbarItemController, IUnityInputController, IHotReloadUi
{
    private readonly UnlockedProtosDbForUi m_unlockedProtosDb;

    private readonly MessagesWindow.Controller m_messagesCenterController;

    private readonly IUnityInputMgr m_inputManager;

    private readonly IInputScheduler m_inputScheduler;

    private bool m_doNotSendCommand;

    private Option<MessageProto> m_planningModeTutorial;

    private readonly Proto m_lockedByProto;

    public bool IsVisible { get; private set; }

    public bool DeactivateShortcutsIfNotVisible => true;

    public ControllerConfig Config => ControllerConfig.Mode;

    public event Action<IToolbarItemController> VisibilityChanged;

    public GhostModeInputController(ToolbarHud toolbar, UiContext context, GhostModeManager GhostModeManager, MessagesWindow.Controller messagesCenterController)
    {
        m_unlockedProtosDb = context.UnlockedProtosDbForUi;
        m_messagesCenterController = messagesCenterController;
        m_inputManager = context.InputMgr;
        m_inputScheduler = context.InputScheduler;
        m_lockedByProto = context.ProtosDb.GetOrThrow<Proto>(IdsCore.Technology.PlanningTool);
        IsVisible = m_unlockedProtosDb.IsUnlocked(m_lockedByProto);
        if (!IsVisible)
        {
            m_unlockedProtosDb.OnUnlockedSetChangedForUi += updateIsVisible;
        }
        toolbar.AddToolButton(Tr.GhostMode, this, "assets/ghost_icon.svg", 20f, (ShortcutsManager m) => m.ToggleGhostMode(), null); //TODO: todo switch this null to a wiki page link
        if (GhostModeManager.IsGhostModeEnabled)
        {
            m_doNotSendCommand = true;
            context.InputMgr.ActivateNewController(this);
            m_doNotSendCommand = false;
        }
    }

    private void updateIsVisible()
    {
        bool flag = m_unlockedProtosDb.IsUnlocked(m_lockedByProto);
        if (IsVisible != flag)
        {
            IsVisible = flag;
            this.VisibilityChanged?.Invoke(this);
        }
    }

    public void Activate()
    {
        if (!m_doNotSendCommand)
        {
            if (m_planningModeTutorial.HasValue)
            {
                m_messagesCenterController.ForceOpenMessageIfNotRead(m_planningModeTutorial.Value);
                m_planningModeTutorial = Option<MessageProto>.None;
            }
            m_inputScheduler.ScheduleInputCmd(new SetGhostModeEnableCmd(isEnabled: true));
        }
    }

    public void Deactivate()
    {
        if (!m_doNotSendCommand)
        {
            m_inputScheduler.ScheduleInputCmd(new SetGhostModeEnableCmd(isEnabled: false));
        }
    }

    public void DeactivateSelf()
    {
        m_inputManager.DeactivateController(this);
        m_inputScheduler.ScheduleInputCmd(new SetGhostModeEnableCmd(isEnabled: false));
    }

    public bool InputUpdate()
    {
        return false;
    }

    public void DisposeForHotReload()
    {
        if (!IsVisible)
        {
            m_unlockedProtosDb.OnUnlockedSetChangedForUi -= updateIsVisible;
        }
    }
}