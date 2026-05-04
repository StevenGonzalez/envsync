using System.Reflection;
using EnvSync.Core.CodeGeneration;
using EnvSync.Core.Diffing;
using EnvSync.Core.Model;
using EnvSync.Core.Providers;
using EnvSync.Core.Providers.AzureAppService;
using EnvSync.Core.Providers.AzureDevOps;
using EnvSync.Core.Providers.AzureKeyVault;
using EnvSync.Core.Providers.AwsSsm;
using EnvSync.Core.Providers.GitHub;
using EnvSync.Core.Providers.Local;
using EnvSync.Core.Providers.Vault;
using EnvSync.Core.Schema;
using EnvSync.Core.Sync;
using EnvSync.Core.Validation;

namespace EnvSync.Cli.Commands;

internal sealed class CliApplication
{
    private const string SupportedProviders = "local, github, azuredevops, azureappservice, ssm, vault, azurekeyvault";

    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly SchemaLoader _schemaLoader = new();
    private readonly SchemaValidator _schemaValidator = new();
    private readonly SchemaDiffEngine _diffEngine = new();
    private readonly BindingGenerator _bindingGenerator = new();
    private readonly EnvironmentSyncService _syncService;

    public CliApplication(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
        _syncService = new EnvironmentSyncService(_schemaValidator);
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            if (args.Length == 0 || IsHelpCommand(args[0]))
            {
                await WriteHelpAsync().ConfigureAwait(false);
                return ExitCodes.Success;
            }

            if (args.Length == 1 && IsVersionCommand(args[0]))
            {
                await _output.WriteLineAsync($"EnvSync {GetVersion()}").ConfigureAwait(false);
                return ExitCodes.Success;
            }

            var command = args[0].ToLowerInvariant();
            var options = CommandLineOptions.Parse(args[1..]);
            ValidateOptionsForCommand(command, options);

            return command switch
            {
                "init" => await ExecuteInitAsync(options, cancellationToken).ConfigureAwait(false),
                "validate" => await ExecuteValidateAsync(options, cancellationToken).ConfigureAwait(false),
                "diff" => await ExecuteDiffAsync(options, cancellationToken).ConfigureAwait(false),
                "pull" => await ExecuteSyncAsync(options, "pull", cancellationToken).ConfigureAwait(false),
                "push" => await ExecuteSyncAsync(options, "push", cancellationToken).ConfigureAwait(false),
                "generate" => await ExecuteGenerateAsync(options, cancellationToken).ConfigureAwait(false),
                _ => throw new CommandLineUsageException($"Unknown command '{command}'."),
            };
        }
        catch (CommandLineUsageException exception)
        {
            await _error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            await _error.WriteLineAsync().ConfigureAwait(false);
            await WriteHelpAsync().ConfigureAwait(false);
            return ExitCodes.Error;
        }
        catch (EnvironmentSyncException exception)
        {
            await _error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.Error;
        }
        catch (OperationCanceledException)
        {
            await _error.WriteLineAsync("Operation cancelled.").ConfigureAwait(false);
            return ExitCodes.Error;
        }
        catch (Exception exception)
        {
            await _error.WriteLineAsync($"Error: {exception.Message}").ConfigureAwait(false);
            return ExitCodes.Error;
        }
    }

    private async Task<int> ExecuteInitAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var format = ParseSchemaFormat(options.GetOptional("format"), options.GetOptional("schema"));
        var schemaPath = options.GetOptional("schema") ?? GetDefaultSchemaPath(format);

        if (File.Exists(schemaPath) && !options.HasFlag("force"))
        {
            throw new CommandLineUsageException($"File '{schemaPath}' already exists. Use '--force' to overwrite it.");
        }

        await File.WriteAllTextAsync(schemaPath, SchemaTemplateFactory.CreateSample(format), cancellationToken).ConfigureAwait(false);
        await _output.WriteLineAsync($"Created schema at {schemaPath}").ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private async Task<int> ExecuteValidateAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var schema = LoadSchema(options);
        var providerSpec = options.GetOptional("provider", "local:.env")!;

        using var providerLease = CreateProviderLease(providerSpec, options);
        var snapshot = await providerLease.Provider.ReadAsync(cancellationToken).ConfigureAwait(false);
        var result = _schemaValidator.Validate(schema, snapshot);

        if (result.Issues.Count == 0)
        {
            await _output.WriteLineAsync($"Validation succeeded for {snapshot.SourceName}.").ConfigureAwait(false);
            return ExitCodes.Success;
        }

        foreach (var issue in result.Issues)
        {
            await _output.WriteLineAsync($"[{issue.Severity}] {issue.Key}: {issue.Message}").ConfigureAwait(false);
        }

        if (result.IsSuccess)
        {
            await _output.WriteLineAsync($"Validation succeeded with warnings for {snapshot.SourceName}.").ConfigureAwait(false);
            return ExitCodes.Success;
        }

        return ExitCodes.DriftOrValidationFailure;
    }

    private async Task<int> ExecuteDiffAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var schema = LoadSchema(options);
        var leftSpec = options.GetRequired("left");
        var rightSpec = options.GetRequired("right");
        var showMatches = options.HasFlag("all");

        using var leftLease = CreateProviderLease(leftSpec, options);
        using var rightLease = CreateProviderLease(rightSpec, options);

        var leftTask = leftLease.Provider.ReadAsync(cancellationToken);
        var rightTask = rightLease.Provider.ReadAsync(cancellationToken);
        await Task.WhenAll(leftTask, rightTask).ConfigureAwait(false);

        // Validate both sides against the schema; exit 2 if either has errors so CI can gate on it.
        var leftValidation = _schemaValidator.Validate(schema, leftTask.Result);
        var rightValidation = _schemaValidator.Validate(schema, rightTask.Result);
        if (!leftValidation.IsSuccess || !rightValidation.IsSuccess)
        {
            foreach (var issue in leftValidation.Issues.Concat(rightValidation.Issues)
                         .Where(static i => i.Severity == ValidationSeverity.Error))
            {
                await _output.WriteLineAsync($"[{issue.Severity}] {issue.Key}: {issue.Message}").ConfigureAwait(false);
            }
        }

        var result = _diffEngine.Diff(schema, leftTask.Result, rightTask.Result);
        var entriesToDisplay = showMatches
            ? result.Entries
            : result.Entries.Where(static entry => entry.Status != DiffStatus.Match).ToArray();

        if (entriesToDisplay.Count == 0 && leftValidation.IsSuccess && rightValidation.IsSuccess)
        {
            await _output.WriteLineAsync("No drift detected.").ConfigureAwait(false);
            return ExitCodes.Success;
        }

        foreach (var entry in entriesToDisplay)
        {
            await _output.WriteLineAsync($"{entry.Status,-18} {entry.Key}").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(entry.Message))
            {
                await _output.WriteLineAsync($"  {entry.Message}").ConfigureAwait(false);
            }
        }

        return (result.HasChanges || !leftValidation.IsSuccess || !rightValidation.IsSuccess)
            ? ExitCodes.DriftOrValidationFailure
            : ExitCodes.Success;
    }

    private async Task<int> ExecuteSyncAsync(CommandLineOptions options, string mode, CancellationToken cancellationToken)
    {
        var schema = LoadSchema(options);
        var fromSpec = options.GetRequired("from");
        var toSpec = options.GetRequired("to");
        var dryRun = options.HasFlag("dry-run");

        using var fromLease = CreateProviderLease(fromSpec, options);
        if (dryRun)
        {
            var captureProvider = new CaptureProvider(DescribeProviderSpec(toSpec));
            var dryResult = await _syncService.SyncAsync(schema, fromLease.Provider, captureProvider, cancellationToken).ConfigureAwait(false);
            await _output.WriteLineAsync($"Dry run - {dryResult.WrittenCount} values would be written from {dryResult.Source} to {dryResult.Target}.").ConfigureAwait(false);
            foreach (var warning in dryResult.Warnings)
            {
                await _output.WriteLineAsync($"Warning: {warning}").ConfigureAwait(false);
            }
            return ExitCodes.Success;
        }

        using var toLease = CreateProviderLease(toSpec, options);
        var result = await _syncService.SyncAsync(schema, fromLease.Provider, toLease.Provider, cancellationToken).ConfigureAwait(false);
        await _output.WriteLineAsync($"{Capitalize(mode)} completed: {result.WrittenCount} values written from {result.Source} to {result.Target}.").ConfigureAwait(false);
        foreach (var warning in result.Warnings)
        {
            await _output.WriteLineAsync($"Warning: {warning}").ConfigureAwait(false);
        }

        return ExitCodes.Success;
    }

    private async Task<int> ExecuteGenerateAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var schema = LoadSchema(options);
        var language = ParseLanguage(options.GetRequired("language"));
        var typeName = options.GetOptional("name", "EnvSyncEnvironment")!;
        var scope = options.GetOptional("namespace", "EnvSync.Generated");
        var generatedFile = _bindingGenerator.Generate(schema, language, typeName, scope);
        var outputPath = options.GetOptional("output", generatedFile.FileName)!;

        await File.WriteAllTextAsync(outputPath, generatedFile.Content, cancellationToken).ConfigureAwait(false);
        await _output.WriteLineAsync($"Generated {language} bindings at {outputPath}").ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private EnvSchema LoadSchema(CommandLineOptions options)
    {
        var schemaPath = ResolveSchemaPath(options.GetOptional("schema"));
        return _schemaLoader.Load(schemaPath);
    }

    // Parses a provider spec string into a typed record. All format validation happens here so
    // CreateProviderLease and DescribeProviderSpec share a single code path.
    private static ParsedSpec ParseProviderSpec(string spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec);

        if (spec.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        {
            var path = spec[6..];
            return new ParsedSpec.Local(string.IsNullOrWhiteSpace(path) ? ".env" : path);
        }

        if (spec.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
        {
            var repositorySpec = spec[7..];
            var segments = repositorySpec.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length != 2)
            {
                throw new CommandLineUsageException("GitHub provider specs must use the form 'github:owner/repository'.");
            }

            try
            {
                return new ParsedSpec.GitHub(new GitHubRepositoryReference(segments[0], segments[1]));
            }
            catch (ArgumentException exception)
            {
                throw new CommandLineUsageException(exception.Message);
            }
        }

        if (spec.StartsWith("azuredevops:", StringComparison.OrdinalIgnoreCase))
        {
            var groupSpec = spec[12..];
            var segments = groupSpec.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length != 3)
            {
                throw new CommandLineUsageException("Azure DevOps provider specs must use the form 'azuredevops:organization/project/groupName'.");
            }

            return new ParsedSpec.AzureDevOps(new AzureDevOpsVariableGroupReference(segments[0], segments[1], segments[2]));
        }

        if (spec.StartsWith("azureappservice:", StringComparison.OrdinalIgnoreCase))
        {
            var appSpec = spec["azureappservice:".Length..];
            var segments = appSpec.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length is not (3 or 4))
            {
                throw new CommandLineUsageException(
                    "Azure App Service provider specs must use the form 'azureappservice:subscription-id/resource-group/app-name' or 'azureappservice:subscription-id/resource-group/app-name/slot-name'.");
            }

            try
            {
                return new ParsedSpec.AzureAppService(new AzureAppServiceReference(
                    segments[0],
                    segments[1],
                    segments[2],
                    segments.Length == 4 ? segments[3] : null));
            }
            catch (ArgumentException exception)
            {
                throw new CommandLineUsageException(exception.Message);
            }
        }

        if (spec.StartsWith("ssm:", StringComparison.OrdinalIgnoreCase))
        {
            var pathPrefix = spec[4..];
            if (string.IsNullOrWhiteSpace(pathPrefix) || !pathPrefix.StartsWith("/", StringComparison.Ordinal))
            {
                throw new CommandLineUsageException("AWS SSM provider specs must use the form 'ssm:/path/prefix'.");
            }

            return new ParsedSpec.Ssm(pathPrefix);
        }

        if (spec.StartsWith("vault:", StringComparison.OrdinalIgnoreCase))
        {
            var mountAndPath = spec[6..];
            var slashIndex = mountAndPath.IndexOf('/');
            if (slashIndex < 1 || slashIndex == mountAndPath.Length - 1)
            {
                throw new CommandLineUsageException("HashiCorp Vault provider specs must use the form 'vault:mount/secret-path'.");
            }

            return new ParsedSpec.Vault(mountAndPath[..slashIndex], mountAndPath[(slashIndex + 1)..]);
        }

        if (spec.StartsWith("azurekeyvault:", StringComparison.OrdinalIgnoreCase))
        {
            var vaultName = spec[14..];
            if (string.IsNullOrWhiteSpace(vaultName))
            {
                throw new CommandLineUsageException("Azure Key Vault provider specs must use the form 'azurekeyvault:vault-name'.");
            }

            return new ParsedSpec.AzureKeyVault(new AzureKeyVaultReference(vaultName));
        }

        var colonIndex = spec.IndexOf(':');
        if (colonIndex > 1 || (colonIndex == 1 && !char.IsLetter(spec[0])))
        {
            throw new CommandLineUsageException(
                $"Unknown provider '{spec[..colonIndex]}'. Supported providers: {SupportedProviders}.");
        }

        return new ParsedSpec.Local(spec);
    }

    private ProviderLease CreateProviderLease(string spec, CommandLineOptions options)
    {
        return ParseProviderSpec(spec) switch
        {
            ParsedSpec.Local local =>
                new ProviderLease(new LocalEnvFileProvider(local.FilePath)),

            ParsedSpec.GitHub github =>
                new ProviderLease(new GitHubActionsProvider(github.Repository, ResolveGitHubToken(options))),

            ParsedSpec.AzureDevOps ado =>
                new ProviderLease(new AzureDevOpsVariableGroupProvider(ado.GroupReference, ResolveAzureDevOpsToken(options))),

            ParsedSpec.AzureAppService appService =>
                new ProviderLease(new AzureAppServiceProvider(appService.Reference)),

            ParsedSpec.Ssm ssm =>
                new ProviderLease(new AwsSsmProvider(new AwsSsmReference(ssm.PathPrefix, ResolveAwsRegion(options)))),

            ParsedSpec.Vault vault =>
                new ProviderLease(new VaultKvProvider(
                    new VaultKvReference(ResolveVaultAddress(options), vault.Mount, vault.SecretPath),
                    ResolveVaultToken(options))),

            ParsedSpec.AzureKeyVault akv =>
                new ProviderLease(new AzureKeyVaultProvider(akv.Reference)),

            _ => throw new InvalidOperationException("Unhandled provider spec type."),
        };
    }

    private static string DescribeProviderSpec(string spec) => ParseProviderSpec(spec).Description;

    private static string ResolveGitHubToken(CommandLineOptions options)
    {
        var token = options.GetOptional("github-token") ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new CommandLineUsageException("GitHub operations require '--github-token' or the GITHUB_TOKEN environment variable.");
        }

        return token;
    }

    private static string ResolveAzureDevOpsToken(CommandLineOptions options)
    {
        var token = options.GetOptional("azuredevops-token") ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new CommandLineUsageException("Azure DevOps operations require '--azuredevops-token' or the AZURE_DEVOPS_TOKEN environment variable.");
        }

        return token;
    }

    private static string? ResolveAwsRegion(CommandLineOptions options) =>
        options.GetOptional("aws-region")
        ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
        ?? Environment.GetEnvironmentVariable("AWS_REGION");

    private static string ResolveVaultAddress(CommandLineOptions options) =>
        options.GetOptional("vault-address")
        ?? Environment.GetEnvironmentVariable("VAULT_ADDR")
        ?? "http://127.0.0.1:8200";

    private static string ResolveVaultToken(CommandLineOptions options)
    {
        var token = options.GetOptional("vault-token") ?? Environment.GetEnvironmentVariable("VAULT_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new CommandLineUsageException("Vault operations require '--vault-token' or the VAULT_TOKEN environment variable.");
        }

        return token;
    }

    private string ResolveSchemaPath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            return requestedPath;
        }

        foreach (var candidate in new[] { "envsync.schema.yaml", "envsync.schema.yml", "envsync.schema.json" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new CommandLineUsageException("No schema file was found. Pass '--schema <path>' or run 'envsync init'.");
    }

    private static SchemaFormat ParseSchemaFormat(string? explicitFormat, string? schemaPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitFormat))
        {
            return explicitFormat.ToLowerInvariant() switch
            {
                "yaml" or "yml" => SchemaFormat.Yaml,
                "json" => SchemaFormat.Json,
                _ => throw new CommandLineUsageException("Schema format must be 'yaml' or 'json'."),
            };
        }

        if (!string.IsNullOrWhiteSpace(schemaPath))
        {
            return SchemaLoader.DetectFormat(schemaPath);
        }

        return SchemaFormat.Yaml;
    }

    private static BindingLanguage ParseLanguage(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "typescript" or "ts" => BindingLanguage.TypeScript,
            "csharp" or "cs" => BindingLanguage.CSharp,
            _ => throw new CommandLineUsageException($"Unknown language '{value}'. Supported values: typescript, csharp."),
        };
    }

    private static void ValidateOptionsForCommand(string command, CommandLineOptions options)
    {
        if (command is not ("init" or "validate" or "diff" or "pull" or "push" or "generate"))
        {
            return;
        }

        if (options.Positionals.Count > 0)
        {
            throw new CommandLineUsageException($"Unexpected argument '{options.Positionals[0]}'. Options must use '--name value' syntax.");
        }

        var (valueOptions, flagOptions) = command switch
        {
            "init" => (
                new[] { "schema", "format" },
                new[] { "force" }),
            "validate" => (
                new[] { "schema", "provider", "github-token", "azuredevops-token", "aws-region", "vault-token", "vault-address" },
                Array.Empty<string>()),
            "diff" => (
                new[] { "left", "right", "schema", "github-token", "azuredevops-token", "aws-region", "vault-token", "vault-address" },
                new[] { "all" }),
            "pull" or "push" => (
                new[] { "from", "to", "schema", "github-token", "azuredevops-token", "aws-region", "vault-token", "vault-address" },
                new[] { "dry-run" }),
            "generate" => (
                new[] { "language", "schema", "output", "name", "namespace" },
                Array.Empty<string>()),
            _ => (Array.Empty<string>(), Array.Empty<string>()),
        };

        foreach (var optionName in options.OptionNames)
        {
            var isValueOption = valueOptions.Contains(optionName, StringComparer.OrdinalIgnoreCase);
            var isFlagOption = flagOptions.Contains(optionName, StringComparer.OrdinalIgnoreCase);

            if (!isValueOption && !isFlagOption)
            {
                throw new CommandLineUsageException($"Unknown option '--{optionName}' for command '{command}'.");
            }

            if (isValueOption && !options.HasValue(optionName))
            {
                throw new CommandLineUsageException($"Option '--{optionName}' requires a value.");
            }

            if (isFlagOption && options.HasValue(optionName))
            {
                throw new CommandLineUsageException($"Option '--{optionName}' does not accept a value.");
            }
        }
    }

    private static bool IsHelpCommand(string arg) =>
        string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase);

    private static bool IsVersionCommand(string arg) =>
        string.Equals(arg, "version", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--version", StringComparison.OrdinalIgnoreCase);

    private static string GetVersion()
    {
        var assembly = typeof(CliApplication).Assembly;
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            return assembly.GetName().Version?.ToString() ?? "unknown";
        }

        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex > 0 ? version[..metadataIndex] : version;
    }

    private async Task WriteHelpAsync()
    {
        const string helpText = """
            EnvSync - Universal Environment Variable Manager

            Usage:
              envsync init [--schema <path>] [--format yaml|json] [--force]
              envsync validate [--schema <path>] [--provider <provider-spec>] [--github-token <token>] [--azuredevops-token <token>] [--vault-token <token>]
              envsync diff --left <spec> --right <spec> [--schema <path>] [--github-token <token>] [--azuredevops-token <token>] [--vault-token <token>] [--all]
              envsync pull --from <spec> --to <spec> [--schema <path>] [--github-token <token>] [--azuredevops-token <token>] [--vault-token <token>]
              envsync push --from <spec> --to <spec> [--schema <path>] [--github-token <token>] [--azuredevops-token <token>] [--vault-token <token>]
              envsync generate --language typescript|csharp [--schema <path>] [--output <path>] [--name <type-name>] [--namespace <namespace>]

            Provider specs:
              local:.env
              local:config/.env.production
              github:owner/repository
              azuredevops:organization/project/groupName
              azureappservice:subscription-id/resource-group/app-name[/slot-name]
              ssm:/myapp/production                         (AWS SSM Parameter Store)
              vault:secret/myapp                           (HashiCorp Vault KV v2)
              azurekeyvault:my-vault-name                  (Azure Key Vault)

            Authentication:
              GitHub:        --github-token or GITHUB_TOKEN env var
              Azure DevOps:  --azuredevops-token or AZURE_DEVOPS_TOKEN env var
              AWS SSM:       standard AWS credential chain (env vars, ~/.aws/credentials, IAM role)
                             override region with --aws-region or AWS_DEFAULT_REGION
              Vault:         --vault-token or VAULT_TOKEN env var
                             override address with --vault-address or VAULT_ADDR (default: http://127.0.0.1:8200)
              Azure:         DefaultAzureCredential for App Service and Key Vault
                             (env vars, managed identity, az login, etc.)

            Notes:
              GitHub Actions secrets are write-only. EnvSync can detect their presence and upload new values,
              but cannot read secret plaintext back from GitHub.
              Azure DevOps secret variables are also write-only; the API redacts their values on read.
              AWS SSM SecureString parameters are surfaced as hidden (decryption requires kms:Decrypt permission).
              Azure App Service restarts the app when application settings are changed.
            """;

        await _output.WriteLineAsync(helpText).ConfigureAwait(false);
    }

    private static string GetDefaultSchemaPath(SchemaFormat format)
    {
        return format switch
        {
            SchemaFormat.Json => "envsync.schema.json",
            _ => "envsync.schema.yaml",
        };
    }

    private static string Capitalize(string value) => char.ToUpperInvariant(value[0]) + value[1..];

    private sealed class ProviderLease : IDisposable
    {
        public ProviderLease(IEnvironmentProvider provider)
        {
            Provider = provider;
        }

        public IEnvironmentProvider Provider { get; }

        public void Dispose()
        {
            if (Provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    // Discriminated union of parsed provider spec types. Parsing and description live here so
    // CreateProviderLease and DescribeProviderSpec share a single code path.
    private abstract record ParsedSpec(string Description)
    {
        internal sealed record Local(string FilePath) : ParsedSpec($"local:{FilePath}");
        internal sealed record GitHub(GitHubRepositoryReference Repository) : ParsedSpec($"github:{Repository}");
        internal sealed record AzureDevOps(AzureDevOpsVariableGroupReference GroupReference) : ParsedSpec($"azuredevops:{GroupReference}");
        internal sealed record AzureAppService(AzureAppServiceReference Reference) : ParsedSpec($"azureappservice:{Reference}");
        internal sealed record Ssm(string PathPrefix) : ParsedSpec($"ssm:{PathPrefix}");
        internal sealed record Vault(string Mount, string SecretPath) : ParsedSpec($"vault:{Mount}/{SecretPath}");
        internal sealed record AzureKeyVault(AzureKeyVaultReference Reference) : ParsedSpec($"azurekeyvault:{Reference}");
    }

    /// <summary>
    /// A write-only provider that captures values without persisting them, used for --dry-run.
    /// </summary>
    private sealed class CaptureProvider(string description) : IEnvironmentProvider
    {
        public string Description => description;

        public IReadOnlyList<ResolvedEnvironmentValue> CapturedValues { get; private set; } = [];

        public Task<EnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new EnvironmentSnapshot(Description, []));

        public Task<ProviderWriteResult> WriteAsync(
            IReadOnlyCollection<ResolvedEnvironmentValue> values,
            CancellationToken cancellationToken = default)
        {
            CapturedValues = values.ToArray();
            return Task.FromResult(new ProviderWriteResult(values.Count));
        }
    }
}
