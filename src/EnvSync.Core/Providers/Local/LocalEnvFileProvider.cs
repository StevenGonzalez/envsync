using EnvSync.Core.Model;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EnvSync.Core.Providers.Local;

/// <summary>
/// Reads and writes environment variables in a local dotenv file.
/// </summary>
public sealed class LocalEnvFileProvider : IEnvironmentProvider
{
    /// <summary>
    /// Creates a local dotenv provider for the specified file path.
    /// </summary>
    /// <param name="path">The dotenv file path.</param>
    public LocalEnvFileProvider(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>
    /// Gets the dotenv file path.
    /// </summary>
    public string Path { get; }

    /// <inheritdoc />
    public string Description => $"local:{Path}";

    /// <inheritdoc />
    public async Task<EnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(Path))
        {
            return new EnvironmentSnapshot(Description, []);
        }

        var content = await File.ReadAllTextAsync(Path, cancellationToken).ConfigureAwait(false);
        var document = DotEnvDocument.Parse(content);
        var values = document.GetAssignments()
            .Select(static pair => EnvironmentValue.Available(pair.Key, pair.Value))
            .ToArray();

        return new EnvironmentSnapshot(Description, values);
    }

    /// <inheritdoc />
    public async Task<ProviderWriteResult> WriteAsync(IReadOnlyCollection<ResolvedEnvironmentValue> values, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existingContent = File.Exists(Path)
            ? await File.ReadAllTextAsync(Path, cancellationToken).ConfigureAwait(false)
            : null;

        var document = DotEnvDocument.Parse(existingContent);
        foreach (var value in values)
        {
            document.Set(value.Key, value.Value);
        }

        var fullPath = System.IO.Path.GetFullPath(Path);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = System.IO.Path.Combine(
            directory ?? Environment.CurrentDirectory,
            $".{System.IO.Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, document.Serialize(), cancellationToken).ConfigureAwait(false);
            RestrictFilePermissions(tempPath);
            File.Move(tempPath, fullPath, overwrite: true);
            RestrictFilePermissions(fullPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return new ProviderWriteResult(values.Count);
    }

    private static void RestrictFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is null)
            {
                return;
            }

            var security = new FileSecurity();
            security.SetOwner(currentUser);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            new FileInfo(path).SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
