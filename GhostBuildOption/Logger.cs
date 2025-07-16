namespace GhostBuildOption;

[GlobalDependency(RegistrationMode.AsSelf, false)]
public class Logger
{
    private static int msgCount = 0;
    private static readonly string Prefix = Assembly.GetExecutingAssembly().GetName().Name + " : ============> ";

    public static void Info(string message) => Log.Info($"{Prefix} {++msgCount} {message}");

    public static void Warning(string message) => Log.Warning($"{Prefix} {++msgCount} {message}");

    public static void WarningOnce(string message) => Log.WarningOnce($"{Prefix} {++msgCount} {message}");

    public static void Error(string message) => Log.Error($"{Prefix} {++msgCount} {message}");

    public static void Exception(Exception e, string message) => Log.Exception(e, $"{Prefix} {++msgCount} {message}");
}
