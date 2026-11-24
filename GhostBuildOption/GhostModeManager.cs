namespace GhostBuildOption;

public static class GhostModeManager
{
    private static volatile bool s_enabled;

    public static bool IsGhostModeEnabled
    {
        get => s_enabled;
        internal set => s_enabled = value;
    }
}
