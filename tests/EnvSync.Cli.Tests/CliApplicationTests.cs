using System.Text;
using EnvSync.Cli.Commands;

namespace EnvSync.Cli.Tests;

/// <summary>
/// Integration tests for <see cref="CliApplication"/>. These exercise the full command routing,
/// provider-spec parsing, option validation, and output without touching any real external APIs
/// by using local:.env specs backed by temp files.
/// </summary>
public sealed class CliApplicationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "envsync-cli-tests", Guid.NewGuid().ToString("N"));

    public CliApplicationTests() => Directory.CreateDirectory(_dir);

    // Helpers

    private string TempPath(string filename) => Path.Combine(_dir, filename);

    private string SchemaPath => TempPath("schema.yaml");

    private static (CliApplication App, StringWriter Out, StringWriter Err) BuildApp()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        return (new CliApplication(stdout, stderr), stdout, stderr);
    }

    private async Task WriteSchemaAsync(string content) =>
        await File.WriteAllTextAsync(SchemaPath, content);

    private async Task WriteEnvAsync(string filename, string content) =>
        await File.WriteAllTextAsync(TempPath(filename), content);

    // Help / unknown command

    [Fact]
    public async Task RunAsync_WithNoArgs_PrintsHelp_AndReturnsSuccess()
    {
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([]);
        Assert.Equal(0, code);
        Assert.Contains("EnvSync", stdout.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task RunAsync_WithHelpArg_PrintsHelp_AndReturnsSuccess(string arg)
    {
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([arg]);
        Assert.Equal(0, code);
        Assert.Contains("Usage:", stdout.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    public async Task RunAsync_WithVersionArg_PrintsVersion_AndReturnsSuccess(string arg)
    {
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([arg]);
        Assert.Equal(0, code);
        Assert.StartsWith("EnvSync ", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_UnknownCommand_WritesError_AndReturnsCode1()
    {
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync(["notacommand"]);
        Assert.Equal(1, code);
        Assert.Contains("notacommand", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_UnknownOption_ReturnsCode1()
    {
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync(["validate", "--dryrun"]);

        Assert.Equal(1, code);
        Assert.Contains("Unknown option", stderr.ToString(), StringComparison.Ordinal);
    }

    // init

    [Fact]
    public async Task Init_CreatesSchemaFile()
    {
        var (app, stdout, _) = BuildApp();
        var path = TempPath("envsync.schema.yaml");
        var code = await app.RunAsync(["init", "--schema", path]);
        Assert.Equal(0, code);
        Assert.True(File.Exists(path));
        Assert.Contains(path, stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Init_ReturnsCode1_IfFileExistsAndNoForceFlag()
    {
        await WriteSchemaAsync("existing: true");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync(["init", "--schema", SchemaPath]);
        Assert.Equal(1, code);
        Assert.Contains("--force", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Init_Overwrites_WhenForceFlag()
    {
        await WriteSchemaAsync("old: content");
        var (app, _, _) = BuildApp();
        var code = await app.RunAsync(["init", "--schema", SchemaPath, "--force"]);
        Assert.Equal(0, code);
        var written = await File.ReadAllTextAsync(SchemaPath);
        Assert.DoesNotContain("old: content", written, StringComparison.Ordinal);
    }

    // validate

    [Fact]
    public async Task Validate_ReturnsSuccess_WhenAllRequiredValuesPresent()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n");
        await WriteEnvAsync(".env", "APP_ENV=dev\n");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync(["validate", "--schema", SchemaPath, "--provider", $"local:{TempPath(".env")}"]);
        Assert.Equal(0, code);
        Assert.Contains("succeeded", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_ReturnsDriftCode_WhenRequiredValueMissing()
    {
        await WriteSchemaAsync("DATABASE_URL:\n  type: string\n  required: true\n");
        await WriteEnvAsync(".env", "");
        var (app, _, _) = BuildApp();
        var code = await app.RunAsync(["validate", "--schema", SchemaPath, "--provider", $"local:{TempPath(".env")}"]);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Validate_ReturnsSuccess_WhenOnlyWarnings()
    {
        // A hidden (secret) value should produce a warning, not a failure.
        await WriteSchemaAsync("API_KEY:\n  type: string\n  required: false\n  secret: true\n");
        await WriteEnvAsync(".env", "API_KEY=plaintext\n");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync(["validate", "--schema", SchemaPath, "--provider", $"local:{TempPath(".env")}"]);
        Assert.Equal(0, code);
    }

    // diff

    [Fact]
    public async Task Diff_ReturnsDriftCode_WhenProvidersDiffer()
    {
        await WriteSchemaAsync("PORT:\n  type: number\n");
        await WriteEnvAsync("left.env", "PORT=3000\n");
        await WriteEnvAsync("right.env", "PORT=8080\n");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([
            "diff",
            "--schema", SchemaPath,
            "--left", $"local:{TempPath("left.env")}",
            "--right", $"local:{TempPath("right.env")}",
        ]);
        Assert.Equal(2, code);
        Assert.Contains("PORT", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_ReturnsSuccess_WhenProvidersMatch()
    {
        await WriteSchemaAsync("PORT:\n  type: number\n");
        await WriteEnvAsync("left.env", "PORT=3000\n");
        await WriteEnvAsync("right.env", "PORT=3000\n");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([
            "diff",
            "--schema", SchemaPath,
            "--left", $"local:{TempPath("left.env")}",
            "--right", $"local:{TempPath("right.env")}",
        ]);
        Assert.Equal(0, code);
        Assert.Contains("No drift", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_MissingLeft_ReturnsCode1()
    {
        await WriteSchemaAsync("PORT:\n  type: number\n");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync(["diff", "--schema", SchemaPath, "--right", "local:.env"]);
        Assert.Equal(1, code);
        Assert.Contains("--left", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_DryRunFlag_ReturnsCode1()
    {
        await WriteSchemaAsync("PORT:\n  type: number\n");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync([
            "diff",
            "--schema", SchemaPath,
            "--left", "local:left.env",
            "--right", "local:right.env",
            "--dry-run",
        ]);

        Assert.Equal(1, code);
        Assert.Contains("Unknown option", stderr.ToString(), StringComparison.Ordinal);
    }

    // pull / push

    [Fact]
    public async Task Pull_CopiesValuesFromSourceToTarget()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n");
        await WriteEnvAsync("source.env", "APP_ENV=production\n");
        var targetPath = TempPath("target.env");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([
            "pull",
            "--schema", SchemaPath,
            "--from", $"local:{TempPath("source.env")}",
            "--to", $"local:{targetPath}",
        ]);
        Assert.Equal(0, code);
        Assert.Contains("1 values written", stdout.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(targetPath));
        Assert.Contains("APP_ENV=production", await File.ReadAllTextAsync(targetPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Push_MissingFromOption_ReturnsCode1()
    {
        await WriteSchemaAsync("X:\n  type: string\n");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync(["push", "--schema", SchemaPath, "--to", "local:.env"]);
        Assert.Equal(1, code);
        Assert.Contains("--from", stderr.ToString(), StringComparison.Ordinal);
    }

    // generate

    [Fact]
    public async Task Generate_TypeScript_WritesFile()
    {
        await WriteSchemaAsync("PORT:\n  type: number\n  default: '3000'\n");
        var outputPath = TempPath("env.generated.ts");
        var (app, _, _) = BuildApp();
        var code = await app.RunAsync([
            "generate",
            "--schema", SchemaPath,
            "--language", "typescript",
            "--output", outputPath,
        ]);
        Assert.Equal(0, code);
        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("port", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_CSharp_WritesFile()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n");
        var outputPath = TempPath("Env.Generated.cs");
        var (app, _, _) = BuildApp();
        var code = await app.RunAsync([
            "generate",
            "--schema", SchemaPath,
            "--language", "csharp",
            "--output", outputPath,
        ]);
        Assert.Equal(0, code);
        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("AppEnv", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_UnknownLanguage_ReturnsCode1()
    {
        await WriteSchemaAsync("X:\n  type: string\n");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync(["generate", "--schema", SchemaPath, "--language", "cobol"]);
        Assert.Equal(1, code);
        Assert.Contains("cobol", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("typescript", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // Provider spec parsing errors

    [Fact]
    public async Task Validate_BadGitHubSpec_ReturnsCode1WithGuidance()
    {
        await WriteSchemaAsync("X:\n  type: string\n");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync([
            "validate", "--schema", SchemaPath,
            "--provider", "github:just-owner",
            "--github-token", "tok",
        ]);
        Assert.Equal(1, code);
        Assert.Contains("github:", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_UnknownProviderTypo_ReturnsCode1()
    {
        await WriteSchemaAsync("X:\n  type: string\n");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync([
            "validate", "--schema", SchemaPath,
            "--provider", "gihub:owner/repo",
        ]);

        Assert.Equal(1, code);
        Assert.Contains("Unknown provider", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_GitHubMissingToken_ReturnsCode1()
    {
        await WriteSchemaAsync("X:\n  type: string\n");
        // Ensure no ambient token bleeds in from the test environment.
        var saved = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
        try
        {
            var (app, _, stderr) = BuildApp();
            var code = await app.RunAsync([
                "validate", "--schema", SchemaPath,
                "--provider", "github:owner/repo",
            ]);
            Assert.Equal(1, code);
            Assert.Contains("token", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", saved);
        }
    }

    [Fact]
    public async Task Validate_BadAzureDevOpsSpec_ReturnsCode1WithGuidance()
    {
        await WriteSchemaAsync("X:\n  type: string\n");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync([
            "validate", "--schema", SchemaPath,
            "--provider", "azuredevops:org/project",   // missing group segment
            "--azuredevops-token", "tok",
        ]);
        Assert.Equal(1, code);
        Assert.Contains("azuredevops:", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_BadAzureAppServiceSpec_ReturnsCode1WithGuidance()
    {
        await WriteSchemaAsync("X:\n  type: string\n");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync([
            "validate", "--schema", SchemaPath,
            "--provider", "azureappservice:subscription/resource-group",
        ]);

        Assert.Equal(1, code);
        Assert.Contains("azureappservice:", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_BadSsmSpec_ReturnsCode1()
    {
        await WriteSchemaAsync("X:\n  type: string\n");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync([
            "validate", "--schema", SchemaPath,
            "--provider", "ssm:",   // empty path prefix
        ]);
        Assert.Equal(1, code);
        Assert.Contains("ssm:", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_VaultMissingToken_ReturnsCode1()
    {
        await WriteSchemaAsync("X:\n  type: string\n");
        var saved = Environment.GetEnvironmentVariable("VAULT_TOKEN");
        Environment.SetEnvironmentVariable("VAULT_TOKEN", null);
        try
        {
            var (app, _, stderr) = BuildApp();
            var code = await app.RunAsync([
                "validate", "--schema", SchemaPath,
                "--provider", "vault:secret/myapp",
            ]);
            Assert.Equal(1, code);
            Assert.Contains("vault-token", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VAULT_TOKEN", saved);
        }
    }

    [Fact]
    public async Task Validate_BadVaultSpec_NoSlash_ReturnsCode1()
    {
        await WriteSchemaAsync("X:\n  type: string\n");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync([
            "validate", "--schema", SchemaPath,
            "--provider", "vault:noseparator",
            "--vault-token", "tok",
        ]);
        Assert.Equal(1, code);
        Assert.Contains("vault:", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_BadAzureKeyVaultSpec_EmptyName_ReturnsCode1()
    {
        await WriteSchemaAsync("X:\n  type: string\n");
        var (app, _, stderr) = BuildApp();
        var code = await app.RunAsync([
            "validate", "--schema", SchemaPath,
            "--provider", "azurekeyvault:",
        ]);
        Assert.Equal(1, code);
        Assert.Contains("azurekeyvault:", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // --dry-run

    [Fact]
    public async Task Pull_DryRun_DoesNotWriteTargetFile()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n");
        await WriteEnvAsync("source.env", "APP_ENV=production\n");
        var targetPath = TempPath("target-dryrun.env");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([
            "pull",
            "--schema", SchemaPath,
            "--from", $"local:{TempPath("source.env")}",
            "--to", $"local:{targetPath}",
            "--dry-run",
        ]);
        Assert.Equal(0, code);
        Assert.False(File.Exists(targetPath), "Dry-run must not create the target file.");
        Assert.Contains("dry run", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pull_MisspelledDryRunFlag_ReturnsCode1AndDoesNotWriteTargetFile()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n");
        await WriteEnvAsync("source.env", "APP_ENV=production\n");
        var targetPath = TempPath("target-misspelled-dryrun.env");
        var (app, _, stderr) = BuildApp();

        var code = await app.RunAsync([
            "pull",
            "--schema", SchemaPath,
            "--from", $"local:{TempPath("source.env")}",
            "--to", $"local:{targetPath}",
            "--dryrun",
        ]);

        Assert.Equal(1, code);
        Assert.False(File.Exists(targetPath), "Misspelled dry-run must fail before writing.");
        Assert.Contains("Unknown option", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pull_DryRun_DoesNotRequireTargetCredentials()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n");
        await WriteEnvAsync("source.env", "APP_ENV=production\n");
        var saved = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);

        try
        {
            var (app, stdout, _) = BuildApp();
            var code = await app.RunAsync([
                "pull",
                "--schema", SchemaPath,
                "--from", $"local:{TempPath("source.env")}",
                "--to", "github:owner/repo",
                "--dry-run",
            ]);

            Assert.Equal(0, code);
            Assert.Contains("github:owner/repo", stdout.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", saved);
        }
    }

    [Fact]
    public async Task Pull_DryRun_DescribesAzureAppServiceTargetWithoutCredentials()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n");
        await WriteEnvAsync("source.env", "APP_ENV=production\n");
        var (app, stdout, _) = BuildApp();

        var code = await app.RunAsync([
            "pull",
            "--schema", SchemaPath,
            "--from", $"local:{TempPath("source.env")}",
            "--to", "azureappservice:00000000-0000-0000-0000-000000000001/my-rg/my-api",
            "--dry-run",
        ]);

        Assert.Equal(0, code);
        Assert.Contains(
            "azureappservice:00000000-0000-0000-0000-000000000001/my-rg/my-api",
            stdout.ToString(),
            StringComparison.Ordinal);
    }

    // IDisposable

    // push command

    [Fact]
    public async Task Push_CopiesValuesFromSourceToTarget()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n");
        await WriteEnvAsync("source.env", "APP_ENV=staging\n");
        var targetPath = TempPath("target.env");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([
            "push",
            "--schema", SchemaPath,
            "--from", $"local:{TempPath("source.env")}",
            "--to", $"local:{targetPath}",
        ]);
        Assert.Equal(0, code);
        Assert.Contains("1 values written", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("APP_ENV=staging", await File.ReadAllTextAsync(targetPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Push_ReturnsZeroWritten_WhenSourceAndTargetAlreadyMatch()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n");
        await WriteEnvAsync("source.env", "APP_ENV=prod\n");
        await WriteEnvAsync("target.env", "APP_ENV=prod\n");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([
            "push",
            "--schema", SchemaPath,
            "--from", $"local:{TempPath("source.env")}",
            "--to", $"local:{TempPath("target.env")}",
        ]);
        Assert.Equal(0, code);
        Assert.Contains("0 values written", stdout.ToString(), StringComparison.Ordinal);
    }

    // diff --all

    [Fact]
    public async Task Diff_All_ShowsMatchingEntriesToo()
    {
        await WriteSchemaAsync("PORT:\n  type: number\n");
        await WriteEnvAsync("left.env", "PORT=3000\n");
        await WriteEnvAsync("right.env", "PORT=3000\n");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([
            "diff",
            "--schema", SchemaPath,
            "--left", $"local:{TempPath("left.env")}",
            "--right", $"local:{TempPath("right.env")}",
            "--all",
        ]);
        Assert.Equal(0, code);
        Assert.Contains("PORT", stdout.ToString(), StringComparison.Ordinal);
    }

    // generate --name and --namespace

    [Fact]
    public async Task Generate_CSharp_RespectsCustomNameAndNamespace()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n");
        var outputPath = TempPath("CustomEnv.g.cs");
        var (app, _, _) = BuildApp();
        var code = await app.RunAsync([
            "generate",
            "--schema", SchemaPath,
            "--language", "csharp",
            "--output", outputPath,
            "--name", "MyEnvConfig",
            "--namespace", "MyApp.Config",
        ]);
        Assert.Equal(0, code);
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("MyEnvConfig", content, StringComparison.Ordinal);
        Assert.Contains("MyApp.Config", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_TypeScript_RespectsCustomName()
    {
        await WriteSchemaAsync("PORT:\n  type: number\n");
        var outputPath = TempPath("env.ts");
        var (app, _, _) = BuildApp();
        var code = await app.RunAsync([
            "generate",
            "--schema", SchemaPath,
            "--language", "typescript",
            "--output", outputPath,
            "--name", "AppEnvironment",
        ]);
        Assert.Equal(0, code);
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("AppEnvironment", content, StringComparison.Ordinal);
    }

    // JSON schema

    [Fact]
    public async Task Validate_WorksWithJsonSchema()
    {
        var jsonSchema = """
            {
              "APP_ENV": { "type": "string", "required": true }
            }
            """;
        var schemaPath = TempPath("envsync.schema.json");
        await File.WriteAllTextAsync(schemaPath, jsonSchema);
        await WriteEnvAsync(".env", "APP_ENV=dev\n");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([
            "validate",
            "--schema", schemaPath,
            "--provider", $"local:{TempPath(".env")}",
        ]);
        Assert.Equal(0, code);
        Assert.Contains("succeeded", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // init --format json

    [Fact]
    public async Task Init_JsonFormat_CreatesJsonSchemaFile()
    {
        var path = TempPath("envsync.schema.json");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync(["init", "--schema", path, "--format", "json"]);
        Assert.Equal(0, code);
        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        Assert.StartsWith("{", content.TrimStart(), StringComparison.Ordinal);
    }

    // inline comment support

    [Fact]
    public async Task Validate_InlineCommentsInDotenv_DoNotCorruptValues()
    {
        await WriteSchemaAsync("PORT:\n  type: number\n  required: true\n");
        await WriteEnvAsync(".env", "PORT=3000 # HTTP port\n");
        var (app, stdout, _) = BuildApp();
        var code = await app.RunAsync([
            "validate",
            "--schema", SchemaPath,
            "--provider", $"local:{TempPath(".env")}",
        ]);
        Assert.Equal(0, code);
        Assert.Contains("succeeded", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // generate emits descriptions

    [Fact]
    public async Task Generate_CSharp_EmitsXmlDocForDescription()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n  description: Deployment environment\n");
        var outputPath = TempPath("Env.g.cs");
        var (app, _, _) = BuildApp();
        await app.RunAsync(["generate", "--schema", SchemaPath, "--language", "csharp", "--output", outputPath]);
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Deployment environment", content, StringComparison.Ordinal);
        Assert.Contains("/// <summary>", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_TypeScript_EmitsJsDocForDescription()
    {
        await WriteSchemaAsync("APP_ENV:\n  type: string\n  required: true\n  description: Deployment environment\n");
        var outputPath = TempPath("env.ts");
        var (app, _, _) = BuildApp();
        await app.RunAsync(["generate", "--schema", SchemaPath, "--language", "typescript", "--output", outputPath]);
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Deployment environment", content, StringComparison.Ordinal);
        Assert.Contains("/**", content, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
