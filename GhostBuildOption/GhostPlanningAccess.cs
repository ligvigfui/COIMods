namespace GhostBuildOption;

[GlobalDependency(RegistrationMode.AsSelf)]
public sealed class GhostPlanningAccess : IHotReloadUi
{
    private readonly UnlockedProtosDbForUi m_unlockedProtos;
    private readonly Proto m_planningTechnology;

    public bool IsUnlocked { get; private set; }
    public event Action? Changed;

    public GhostPlanningAccess(UiContext context)
    {
        m_unlockedProtos = context.UnlockedProtosDbForUi;
        m_planningTechnology = context.ProtosDb.GetOrThrow<Proto>(IdsCore.Technology.PlanningTool);
        IsUnlocked = m_unlockedProtos.IsUnlocked(m_planningTechnology);
        m_unlockedProtos.OnUnlockedSetChangedForUi += Update;
    }

    private void Update()
    {
        bool unlocked = m_unlockedProtos.IsUnlocked(m_planningTechnology);
        if (unlocked == IsUnlocked)
            return;
        IsUnlocked = unlocked;
        Changed?.Invoke();
    }

    public void DisposeForHotReload() => m_unlockedProtos.OnUnlockedSetChangedForUi -= Update;
}
