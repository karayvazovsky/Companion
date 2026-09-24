using System.IO;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class AppPathsTests
{
    [Fact]
    public void Resolve_walks_up_to_the_folder_that_contains_skins()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.PathOf("skins"));
        var start = tmp.PathOf(Path.Combine("app", "bin"));
        Directory.CreateDirectory(start);

        var paths = AppPaths.Resolve(start);

        Assert.Equal(Path.GetFullPath(tmp.Root), paths.Root);
        Assert.Equal(Path.Combine(paths.Root, "data"), paths.Data);
        Assert.Equal(Path.Combine(paths.Root, "data", "logs"), paths.Logs);
        Assert.Equal(Path.Combine(paths.Root, "data", "secrets.bin"), paths.SecretsFile);
    }

    [Fact]
    public void Resolve_prefers_the_override_root()
    {
        using var tmp = new TempDir();
        var paths = AppPaths.Resolve(@"C:\somewhere\else", tmp.Root);
        Assert.Equal(Path.GetFullPath(tmp.Root), paths.Root);
    }

    [Fact]
    public void EnsureCreated_creates_data_logs_and_workspace()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Root);

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.Data));
        Assert.True(Directory.Exists(paths.Logs));
        Assert.True(Directory.Exists(paths.Workspace));
    }
}
