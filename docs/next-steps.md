# EnvSync Next Steps

This file keeps the follow-up work out of chat memory and close to the code.

## Provider Smoke Tests

Run these against throwaway resources before a public release. Use test-only names and values, then delete the resources after the run.

### Local dotenv

- Create a sample schema with `dotnet run --project src/EnvSync.Cli -- init --schema smoke.schema.yaml --force`.
- Create `smoke.source.env` with schema-managed values.
- Run `validate`, `diff`, `pull --dry-run`, and `pull` between two local files.
- Confirm `.env` files are ignored by git and, on Unix, written with `0600` permissions.

### GitHub Actions

- Create a throwaway private repository.
- Use a token with the minimum repository actions variables/secrets permissions available for the test account.
- Push one non-secret variable and one secret from `local:smoke.source.env` to `github:<owner>/<repo>`.
- Validate that variables are readable, secrets are reported as hidden, and sync skips hidden source secrets.

### Azure DevOps Variable Group

- Create a throwaway project variable group with one plain variable and one secret variable.
- Validate and diff against a local provider.
- Push a changed plain variable and confirm the unchanged secret placeholder is preserved.

### AWS SSM Parameter Store

- Create a dedicated prefix such as `/envsync/smoke/<timestamp>`.
- Push one `String` and one `SecureString` parameter.
- Validate that `SecureString` values are surfaced as hidden when read without decryption.
- Delete the prefix after the test.

### HashiCorp Vault

- Start a local dev server or use an isolated test namespace.
- Enable a KV v2 mount, push local values to `vault:<mount>/envsync/smoke`, then pull them back to a local file.
- Confirm unreferenced existing keys are preserved by read-merge-write.

### Azure Key Vault

- Use a throwaway vault or isolated test vault.
- Push schema-managed values to `azurekeyvault:<vault-name>`.
- Validate underscore-to-hyphen name translation and confirm disabled secrets are skipped.

## Release Checklist

- Run `dotnet test EnvSync.slnx`.
- Run `dotnet format EnvSync.slnx --verify-no-changes --verbosity minimal`.
- Run `dotnet list EnvSync.slnx package --vulnerable --include-transitive`.
- Run `dotnet list EnvSync.slnx package --outdated`.
- Run all provider smoke tests above.
- Update package version and release notes.
- Pack with `dotnet pack src/EnvSync.Cli/EnvSync.Cli.csproj -c Release -o artifacts/packages`.
- Install locally from the package and run a local smoke test.
- Commit intentionally, tag the release, then publish.

## Backlog

- Add scripted smoke-test helpers once real provider credentials and resource naming conventions are settled.
- Add a conventional changelog before the first public release.
- Decide whether to add a `global.json` once the minimum required SDK version for `.slnx` is fixed.
