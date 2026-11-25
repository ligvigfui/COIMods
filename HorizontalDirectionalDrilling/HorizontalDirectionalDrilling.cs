namespace HorizontalDirectionalDrilling;

public sealed class HorizontalDirectionalDrilling : IMod
{
    public ModManifest Manifest { get; }

    public ModJsonConfig JsonConfig { get; }

    public bool IsUiOnly => false;

    public Option<IConfig> ModConfig => Option<IConfig>.None;

    internal HddSettings Settings { get; private set; } = null!;

    public HorizontalDirectionalDrilling(ModManifest manifest)
    {
        Manifest = manifest;
        JsonConfig = new ModJsonConfig(this);
    }

    public void EarlyInit(DependencyResolver resolver)
    {
        Settings = HddSettings.Load(JsonConfig);
    }

    public void Initialize(DependencyResolver resolver, bool gameWasLoaded)
    {
        ModLogger.Info($"Horizontal Directional Drilling initialized. Version={Manifest.Version}, loadedGame={gameWasLoaded}.");
        if (Settings.EnableVerboseLogging)
        {
            ModLogger.Info($"Startup configuration: enable_verbose_logging={Settings.EnableVerboseLogging}.");
        }
    }

    public void RegisterDependencies(DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool gameWasLoaded)
    {
    }

    public void RegisterPrototypes(ProtoRegistrator registrator)
    {
        ModLogger.Info("No gameplay prototypes are registered in this foundation release.");
    }

    public void MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues)
    {
    }

    public void Dispose()
    {
    }
}
