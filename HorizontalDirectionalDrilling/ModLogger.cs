using System.Reflection;

namespace HorizontalDirectionalDrilling;

internal static class ModLogger
{
    private static readonly string Prefix =
        Assembly.GetExecutingAssembly().GetName().Name + " : ============> ";

    public static void Info(string message)
    {
        Log.Info(Prefix + message);
    }

    public static void Warning(string message)
    {
        Log.Warning(Prefix + message);
    }

    public static void Error(string message)
    {
        Log.Error(Prefix + message);
    }

    public static void Exception(Exception exception, string message)
    {
        Log.Exception(exception, Prefix + message);
    }
}
