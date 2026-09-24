namespace Companion.Core;

/// <summary>Every folder and file the app uses, relative to one portable root.</summary>
public sealed record AppPaths(string Root)
{
    public string Data => Path.Combine(Root, "data");
    public string Logs => Path.Combine(Data, "logs");
    public string Skins => Path.Combine(Root, "skins");
    public string Workspace => Path.Combine(Root, "workspace");
    public string SettingsFile => Path.Combine(Data, "settings.json");
    public string SecretsFile => Path.Combine(Data, "secrets.bin");
    public string MemoryFile => Path.Combine(Data, "memory.json");
    public string PetFile => Path.Combine(Data, "pet.json");

    /// <summary>
    /// The root is the first folder at or above <paramref name="startDir"/> that has a "skins" folder.
    /// This works both for the published app (root\app\Companion.exe) and for dev builds (root\src\...\bin\...).
    /// </summary>
    public static AppPaths Resolve(string startDir, string? overrideRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot)) return new AppPaths(Path.GetFullPath(overrideRoot));
        for (var dir = new DirectoryInfo(Path.GetFullPath(startDir)); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "skins"))) return new AppPaths(dir.FullName);
        }
        return new AppPaths(Path.GetFullPath(startDir));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Workspace);
    }
}
