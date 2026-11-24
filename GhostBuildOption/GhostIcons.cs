using System.IO;

namespace GhostBuildOption;

internal static class GhostIcons
{
    internal const string Build = "Assets/GhostBuildOption/ghost_icon.png";
    internal const string Convert = "Assets/GhostBuildOption/ghost_convert_icon.png";
    internal const string Warning = "Assets/GhostBuildOption/exclamation.png";
    internal const string ConvertCursor = "Assets/GhostBuildOption/ghost_planner_cursor.png";

    // The .NET 4.8 compiler cannot resolve Unity's newer ReadOnlySpan overloads.
    private static readonly Func<Texture2D, byte[], bool, bool> s_loadImage =
        (Func<Texture2D, byte[], bool, bool>)Delegate.CreateDelegate(typeof(Func<Texture2D, byte[], bool, bool>),
            typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) })
            ?? throw new MissingMethodException(typeof(ImageConversion).FullName, "LoadImage(Texture2D, byte[], bool)"));

    internal static void Register(AssetsDb assets, string modDirectory)
    {
        Register(assets, Build, Path.Combine(modDirectory, "assets", "ghost_icon.png"));
        Register(assets, Convert, Path.Combine(modDirectory, "assets", "ghost_convert_icon.png"));
        Register(assets, Warning, Path.Combine(modDirectory, "assets", "exclamation.png"));
        Register(assets, ConvertCursor, Path.Combine(modDirectory, "assets", "ghost_planner_cursor.png"), false);
    }

    private static void Register(AssetsDb assets, string assetPath, string filePath, bool markNonReadable = true)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        // AssetsDb caches sprites, so keep their shared texture alive and reuse it across games.
        Texture2D texture;
        bool reuse = false;
        if (assets.TryGetRuntimeOverride(assetPath, out var existing) && existing is Texture2D cached && cached)
        {
            texture = cached;
            reuse = true;
        }
        else
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!s_loadImage(texture, bytes, markNonReadable))
        {
            if (!reuse)
                UnityEngine.Object.Destroy(texture);
            throw new InvalidDataException($"Unable to decode ghost icon: {filePath}");
        }
        texture.name = assetPath;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        assets.SetRuntimeOverride(assetPath, texture);
    }
}
