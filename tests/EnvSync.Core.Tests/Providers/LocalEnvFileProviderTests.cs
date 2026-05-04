using EnvSync.Core.Model;
using EnvSync.Core.Providers.Local;
using System.Security.AccessControl;

namespace EnvSync.Core.Tests.Providers;

public sealed class LocalEnvFileProviderTests : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "envsync-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadAsync_ParsesAssignments()
    {
        Directory.CreateDirectory(_directory);
        var path = System.IO.Path.Combine(_directory, ".env");
        await File.WriteAllTextAsync(path, "APP_ENV=dev\nPORT=3000\n");

        var provider = new LocalEnvFileProvider(path);
        var snapshot = await provider.ReadAsync();

        Assert.Equal("dev", snapshot.Values["APP_ENV"].Value);
        Assert.Equal("3000", snapshot.Values["PORT"].Value);
    }

    [Fact]
    public async Task ReadAsync_ReturnsEmptySnapshot_WhenFileDoesNotExist()
    {
        var path = System.IO.Path.Combine(_directory, "missing.env");
        var provider = new LocalEnvFileProvider(path);

        var snapshot = await provider.ReadAsync();

        Assert.Equal($"local:{path}", snapshot.SourceName);
        Assert.Empty(snapshot.Values);
    }

    [Fact]
    public async Task WriteAsync_PreservesExistingCommentsAndAppendsMissingKeys()
    {
        Directory.CreateDirectory(_directory);
        var path = System.IO.Path.Combine(_directory, ".env");
        await File.WriteAllTextAsync(path, "# existing\nAPP_ENV=dev\n");

        var provider = new LocalEnvFileProvider(path);
        await provider.WriteAsync([
            new ResolvedEnvironmentValue("APP_ENV", "prod", false),
            new ResolvedEnvironmentValue("PORT", "3000", false)
        ]);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("# existing", content, StringComparison.Ordinal);
        Assert.Contains("APP_ENV=prod", content, StringComparison.Ordinal);
        Assert.Contains("PORT=3000", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_CreatesParentDirectoryWhenMissing()
    {
        var path = System.IO.Path.Combine(_directory, "nested", ".env");
        var provider = new LocalEnvFileProvider(path);

        await provider.WriteAsync([new ResolvedEnvironmentValue("APP_ENV", "production", false)]);

        Assert.True(File.Exists(path));
        Assert.Contains("APP_ENV=production", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_RejectsInvalidKeys()
    {
        Directory.CreateDirectory(_directory);
        var path = System.IO.Path.Combine(_directory, ".env");
        var provider = new LocalEnvFileProvider(path);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.WriteAsync([new ResolvedEnvironmentValue("BAD-NAME", "value", false)]));
    }

    [Fact]
    public async Task WriteAsync_RestrictsFilePermissionsOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        var path = System.IO.Path.Combine(_directory, ".env");
        var provider = new LocalEnvFileProvider(path);

        await provider.WriteAsync([new ResolvedEnvironmentValue("API_KEY", "secret", true)]);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public async Task WriteAsync_RestrictsFilePermissionsOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        var path = System.IO.Path.Combine(_directory, ".env");
        var provider = new LocalEnvFileProvider(path);

        await provider.WriteAsync([new ResolvedEnvironmentValue("API_KEY", "secret", true)]);

        var security = new FileInfo(path).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
