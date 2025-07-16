namespace GhostBuildOption;

public class MyShortcutsStorage
{
    public static MyShortcutsStorage MyDefaults = new();
    public static MyShortcutsStorage MyCurrent = new();

    [Kb(KbCategory.Tools, "ToggleGhostMode", "Toggle ghost mode", null, false, false)]
    public KeyBindings ToggleGhostMode { get; set; } = KeyBindings.FromKeys(KbCategory.Tools, ShortcutMode.Game, KeyCode.LeftShift , KeyCode.B);
}

[HarmonyPatch(typeof(ShortcutsStorage))]
class ShortcutsStoragePatch
{
    static KeyValuePair<PropertyInfo, KbAttribute>[] AnEmtyArray = [];

    [LogMethod]
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ShortcutsStorage.GetAllAttributes))]
    static KeyValuePair<PropertyInfo, KbAttribute>[] GetAllAttributes(KeyValuePair<PropertyInfo, KbAttribute>[] __result)
    {
        var myKeyBindings = (from x in typeof(MyShortcutsStorage).GetProperties()
                 where x.HasAttribute<KbAttribute>()
                 select Make.Kvp(x, (KbAttribute)x.GetCustomAttributes(typeof(KbAttribute), inherit: false).First())).ToArray();
        return [.. __result, .. myKeyBindings];
    }

    [LogMethod]
    [HarmonyPrefix]
    [HarmonyPatch(MethodType.StaticConstructor)]
    static void ShortcutsStorageCtor()
    {
        var array = GetAllAttributes(AnEmtyArray);
        for (int i = 0; i < array.Length; i++)
        {
            KeyValuePair<PropertyInfo, KbAttribute> keyValuePair = array[i];
            KbAttribute value = keyValuePair.Value;
            PropertyInfo key = keyValuePair.Key;
            KeyBindings keyBindings = (KeyBindings)key.GetValue(MyShortcutsStorage.MyCurrent);
            KeyBinding primary;
            if (PlayerPrefs.HasKey(value.PrefsIdPrimary))
            {
                string @string = PlayerPrefs.GetString(value.PrefsIdPrimary, null);
                primary = keyBindings.Primary.UpdateSelfFrom(@string);
            }
            else
            {
                primary = keyBindings.Primary;
            }

            KeyBinding secondary;
            if (PlayerPrefs.HasKey(value.PrefsIdSecondary))
            {
                string string2 = PlayerPrefs.GetString(value.PrefsIdSecondary, null);
                secondary = keyBindings.Secondary.UpdateSelfFrom(string2);
            }
            else
            {
                secondary = keyBindings.Secondary;
            }

            key.SetValue(MyShortcutsStorage.MyCurrent, new KeyBindings(keyBindings.Mode, primary, secondary));
        }
    }

    [LogMethod]
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ShortcutsStorage.FindById))]
    static bool FindById(string groupId, ref KeyBindings __result)
    {
        KeyValuePair<PropertyInfo, KbAttribute>[] allAttributes = GetAllAttributes(AnEmtyArray);
        PropertyInfo propertyInfo = (from x in allAttributes
                                     where x.Value.GroupId == groupId
                                     select x.Key).FirstOrDefault();
        if (propertyInfo == null)
        {
            return true;
        }

        __result = (KeyBindings)propertyInfo.GetValue(MyShortcutsStorage.MyCurrent);
        return false;
    }

    [LogMethod]
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ShortcutsStorage.ApplyChanges))]
    static bool ApplyChanges()
    {
        KeyValuePair<PropertyInfo, KbAttribute>[] array = GetAllAttributes(AnEmtyArray);
        for (int i = 0; i < array.Length; i++)
        {
            KeyValuePair<PropertyInfo, KbAttribute> keyValuePair = array[i];
            KbAttribute value = keyValuePair.Value;
            PropertyInfo key = keyValuePair.Key;
            KeyBindings keyBindings = (KeyBindings)key.GetValue(MyShortcutsStorage.MyCurrent);
            KeyBindings keyBindings2 = (KeyBindings)key.GetValue(MyShortcutsStorage.MyDefaults);
            if (keyBindings.Primary.AreSame(keyBindings2.Primary))
            {
                if (PlayerPrefs.GetString(value.PrefsIdPrimary, null) != null)
                {
                    PlayerPrefs.DeleteKey(value.PrefsIdPrimary);
                }
            }
            else
            {
                PlayerPrefs.SetString(value.PrefsIdPrimary, keyBindings.Primary.ToString());
            }

            if (keyBindings.Secondary.AreSame(keyBindings2.Secondary))
            {
                if (PlayerPrefs.GetString(value.PrefsIdSecondary, null) != null)
                {
                    PlayerPrefs.DeleteKey(value.PrefsIdSecondary);
                }
            }
            else
            {
                PlayerPrefs.SetString(value.PrefsIdSecondary, keyBindings.Secondary.ToString());
            }
        }
        return true;
    }

    [LogMethod]
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ShortcutsStorage.RestoreDefaults))]
    static bool RestoreDefaults()
    {
        KeyValuePair<PropertyInfo, KbAttribute>[] allAttributes = GetAllAttributes(AnEmtyArray);
        foreach (KeyValuePair<PropertyInfo, KbAttribute> keyValuePair in allAttributes)
        {
            PropertyInfo key = keyValuePair.Key;
            KeyBindings keyBindings = (KeyBindings)key.GetValue(MyShortcutsStorage.MyDefaults);
            key.SetValue(MyShortcutsStorage.MyCurrent, keyBindings);
        }
        return true;
    }
}