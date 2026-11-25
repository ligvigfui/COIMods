namespace HorizontalDirectionalDrilling;

internal sealed class HddSettings
{
    public bool EnableVerboseLogging { get; }

    private HddSettings(bool enableVerboseLogging)
    {
        EnableVerboseLogging = enableVerboseLogging;
    }

    public static HddSettings Load(ModJsonConfig config)
    {
        return new HddSettings(config.GetBool("enable_verbose_logging", false));
    }
}
