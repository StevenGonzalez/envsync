# EnvSync
<img width="512" height="512" alt="EnvSync" src="https://github.com/user-attachments/assets/7257506f-9c69-446e-a362-c48f3dd3f00e" />

EnvSync is a CLI-first universal environment variable manager. It keeps a single schema file as the source of truth for environment variables and synchronises values across local `.env` files, GitHub Actions, Azure DevOps Library variable groups, AWS SSM Parameter Store, HashiCorp Vault, Azure Key Vault, and generated typed bindings for TypeScript and C#.

## What it does

- Defines all environment variables in a YAML or JSON schema (names, types, required/optional, allowed values, defaults, secret flags).
- Validates any provider's current state against that schema.
- Diffs two providers to surface drift between environments.
- Synchronises schema-managed values from one provider to another.
- Generates strongly-typed bindings so application code never accesses raw strings directly.

## Quick start

```powershell
# 1. Create a schema in the current directory
envsync init

# 2. Edit envsync.schema.yaml to match your variables, then validate your .env
envsync validate --provider local:.env

# 3. Push local values to GitHub Actions
envsync push --from local:.env --to github:myorg/myrepo

# 4. Generate a TypeScript interface
envsync generate --language typescript --output src/env.generated.ts
```

## Commands

| Command | Purpose |
|---------|---------|
| `init` | Create a sample schema file |
| `validate` | Check a provider's values against the schema |
| `diff` | Compare two providers and report drift |
| `pull` | Copy values from one provider to another |
| `push` | Alias for `pull` with reversed mental model |
| `generate` | Emit typed bindings for TypeScript or C# |

### Full syntax

```
envsync init [--schema <path>] [--format yaml|json] [--force]
envsync validate [--schema <path>] [--provider <spec>] [--github-token <t>] [--azuredevops-token <t>] [--vault-token <t>]
envsync diff --left <spec> --right <spec> [--schema <path>] [--all] [--github-token <t>] [--azuredevops-token <t>] [--vault-token <t>]
envsync pull --from <spec> --to <spec> [--schema <path>] [--dry-run] [--github-token <t>] [--azuredevops-token <t>] [--vault-token <t>]
envsync push --from <spec> --to <spec> [--schema <path>] [--dry-run] [--github-token <t>] [--azuredevops-token <t>] [--vault-token <t>]
envsync generate --language typescript|csharp [--schema <path>] [--output <path>] [--name <type>] [--namespace <ns>]
```

## Providers

| Spec | Description |
|------|-------------|
| `local:<path>` | Reads and writes a dotenv file. Preserves existing comments and ordering. Writes are atomic and permissions are restricted for secret safety. |
| `github:<owner>/<repo>` | GitHub Actions variables (readable) and secrets (write-only, encrypted with libsodium). |
| `azuredevops:<org>/<project>/<group>` | Azure DevOps Library variable group. Secret variables are write-only. |
| `ssm:<path-prefix>` | AWS Systems Manager Parameter Store. `SecureString` parameters surface as hidden. |
| `vault:<mount>/<secret-path>` | HashiCorp Vault KV v2. Writes use read-merge-write to preserve unreferenced keys. |
| `azurekeyvault:<vault-name>` | Azure Key Vault. Uses `DefaultAzureCredential`. Underscore-to-hyphen translation is applied automatically. |

Any combination of providers works with `diff`, `pull`, and `push`:

```powershell
# Compare local to Azure DevOps
envsync diff --left local:.env --right azuredevops:myorg/myproject/production

# Push from GitHub to Azure DevOps
envsync push --from github:myorg/myrepo --to azuredevops:myorg/myproject/staging

# Sync from AWS SSM to local
envsync pull --from ssm:/myapp/prod --to local:.env.local

# Promote Vault staging secret to production
envsync push --from vault:secret/myapp/staging --to vault:secret/myapp/production --vault-token $env:VAULT_TOKEN
```

## Authentication

| Provider | Flag | Environment variable | Notes |
|----------|------|---------------------|-------|
| GitHub | `--github-token` | `GITHUB_TOKEN` | Personal access token or Actions token with `repo` scope |
| Azure DevOps | `--azuredevops-token` | `AZURE_DEVOPS_TOKEN` | Personal access token with Variable Groups read/write |
| AWS SSM | `--aws-region` | `AWS_DEFAULT_REGION` / `AWS_REGION` | Credentials from standard AWS chain (env vars, `~/.aws/credentials`, IAM role) |
| HashiCorp Vault | `--vault-token` | `VAULT_TOKEN` | Server address via `--vault-address` or `VAULT_ADDR` (default: `http://127.0.0.1:8200`) |
| Azure Key Vault | -- | -- | `DefaultAzureCredential`: env vars to workload identity to managed identity to `az login` |

## Safety notes

- Unknown CLI options fail fast. This is intentional so a misspelled safety flag such as `--dryrun` cannot turn into a real write.
- Unknown provider schemes fail fast. A typo such as `gihub:owner/repo` is not treated as a local file path.
- `pull --dry-run` and `push --dry-run` validate the target provider spec but do not require target credentials or write to the target.
- Secret values are redacted in validation errors. Non-secret values may still appear in validation output.
- Local dotenv writes are atomic. On Unix, EnvSync writes local dotenv files as `0600`; on Windows, it protects the file ACL for the current user.
- `.env` and `.env.*` files are ignored by the project `.gitignore` by default. Commit `.env.example` files for templates.

## Schema file

The schema file (`envsync.schema.yaml` by default) is the single source of truth. It is YAML or JSON and lives in source control.

Variable names must start with an ASCII letter or underscore and contain only ASCII letters, digits, and underscores. This avoids invalid dotenv output, generated-code collisions, and provider name-translation ambiguity.

```yaml
APP_ENV:
  type: string
  required: true
  description: Deployment environment
  allowed: [dev, staging, prod]

DATABASE_URL:
  type: string
  required: true
  secret: true
  description: Primary database connection string

PORT:
  type: number
  required: false
  default: 3000
  description: HTTP server port
```

Supported types: `string`, `number`, `boolean`.

## Generated bindings

**TypeScript** - an interface and a key-constant map:

```ts
export interface EnvSyncEnvironment {
  appEnv: "dev" | "staging" | "prod";
  databaseUrl: string;
  port: number;
}
```

**C#** - a class with `required` properties, typed defaults, and nullable optionals:

```csharp
public sealed class EnvSyncEnvironment
{
    public required string AppEnv { get; init; }
    public required string DatabaseUrl { get; init; }
    public double Port { get; init; } = 3000;
}
```

## Write-only secret limitation

GitHub Actions, Azure DevOps, and AWS SSM (`SecureString`) redact secret values over their APIs. Azure Key Vault returns 403 on individual secrets if the caller lacks `Get` permission. EnvSync models all of these explicitly as `ValueAvailability.Hidden`:

- `validate` confirms the secret key exists but skips value inspection.
- `diff` reports hidden-value comparisons as `Unknown` rather than guessing.
- `pull`/`push` skips hidden values and emits a warning per skipped key.

This is a platform API constraint, not an EnvSync limitation.

## Exit codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | Unexpected error or bad usage |
| `2` | Validation errors or drift detected (useful for CI gating) |

## Architecture

```
src/
  EnvSync.Core/
    Model/          - domain types (EnvSchema, EnvironmentSnapshot, EnvironmentValue, ...)
    Schema/         - YAML/JSON parsing, schema loader, template factory
    Validation/     - SchemaValidator
    Diffing/        - SchemaDiffEngine
    Providers/
      Local/        - LocalEnvFileProvider, DotEnvDocument
      GitHub/       - GitHubActionsProvider
      AzureDevOps/  - AzureDevOpsVariableGroupProvider
      AwsSsm/       - AwsSsmProvider
      Vault/        - VaultKvProvider
      AzureKeyVault/ - AzureKeyVaultProvider
    Sync/           - EnvironmentSyncService
    CodeGeneration/ - BindingGenerator (TypeScript + C#)
  EnvSync.Cli/
    Commands/       - CliApplication, CommandLineOptions, ExitCodes
tests/
  EnvSync.Core.Tests/
    SchemaLoaderTests, SchemaValidatorTests, SchemaDiffEngineTests,
    BindingGeneratorTests, LocalEnvFileProviderTests, EnvironmentSyncServiceTests,
    GitHubActionsProviderTests, AzureDevOpsVariableGroupProviderTests,
    AwsSsmProviderTests, VaultKvProviderTests, AzureKeyVaultProviderTests
```

The core library has no dependency on the CLI. Adding a new provider means implementing `IEnvironmentProvider` in `EnvSync.Core` and adding one `if` block to `CreateProviderLease` in the CLI.

## Build and test

```powershell
dotnet test EnvSync.slnx
```

## Install

EnvSync is packaged as a .NET global tool. Install it from NuGet with the .NET SDK.

### Recommended (NuGet)

```powershell
dotnet tool install --global EnvSync
```

Then verify:

```powershell
envsync --version
envsync --help
```

### Update

```powershell
dotnet tool update --global EnvSync
```

### Uninstall

```powershell
dotnet tool uninstall --global EnvSync
```
