namespace GhostBuildOption;

public static class ShortcutsManagerExtensions
{
    public static KeyBindings ToggleGhostMode(this ShortcutsManager _)
    {
        return MyShortcutsStorage.MyCurrent.ToggleGhostMode;
    }
}
