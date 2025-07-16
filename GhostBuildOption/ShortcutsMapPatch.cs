using Mafi.Unity.Ui.Library;

namespace GhostBuildOption;

[HarmonyPatch(typeof(ShortcutsMap))]
class ShortcutsMapPatch
{
    [LogMethod]
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ShortcutsMap.GetKeybindings))]
    static IEnumerable<KeyValuePair<PropertyInfo, KbAttribute>> GetKeybindings(IEnumerable<KeyValuePair<PropertyInfo, KbAttribute>> __result, KbCategory category)
    {
        var myKeyBindings = from x in typeof(MyShortcutsStorage).GetProperties()
                            select Make.Kvp(x, x.GetCustomAttribute<KbAttribute>(inherit: false)) into x
                            where x.Value != null && x.Value.Category == category
                            select x;
        foreach (var myKeyBinding in myKeyBindings)
        {
            Logger.Info($"title: {myKeyBinding.Value.Title}, tooltip: {myKeyBinding.Value.Tooltip}");
        }
        return __result.Concat(myKeyBindings);
    }

    [LogMethod]
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ShortcutsMap.GetKeybindingsCount))]
    static int GetKeybindingsCount(int __result)
    {
        return typeof(MyShortcutsStorage).GetProperties().Count((PropertyInfo x) => x.HasAttribute<KbAttribute>()) + __result;
    }
}