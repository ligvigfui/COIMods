namespace GhostBuildOption;

internal static class GhostPlacementContext
{
    [ThreadStatic] private static bool s_ignoreTerrain;
    [ThreadStatic] private static bool s_createGhosts;
    [ThreadStatic] private static bool s_createPlans;

    internal static bool IgnoreTerrain => s_ignoreTerrain;
    internal static bool CreateGhosts => s_createGhosts;
    internal static bool CreatePlans => s_createPlans;

    internal static (bool, bool) Enter(bool enabled, bool createGhosts)
    {
        enabled &= !s_createPlans;
        var previous = (s_ignoreTerrain, s_createGhosts);
        s_ignoreTerrain |= enabled;
        s_createGhosts |= enabled && createGhosts;
        return previous;
    }

    internal static void Exit((bool, bool) previous)
    {
        (s_ignoreTerrain, s_createGhosts) = previous;
    }

    internal static (bool, bool) EnterTransportSplit(bool isGhost)
    {
        var previous = (s_ignoreTerrain, s_createGhosts);
        s_ignoreTerrain |= isGhost;
        // Fragments inherit their source, not the active placement/conversion mode.
        s_createGhosts = isGhost;
        return previous;
    }

    internal static (bool, bool, bool) EnterPlans()
    {
        var previous = (s_ignoreTerrain, s_createGhosts, s_createPlans);
        s_ignoreTerrain = false;
        s_createGhosts = false;
        s_createPlans = true;
        return previous;
    }

    internal static void ExitPlans((bool, bool, bool) previous) =>
        (s_ignoreTerrain, s_createGhosts, s_createPlans) = previous;
}
