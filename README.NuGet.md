<p align="center">
  <img width="512" alt="EnvSync logo" src="https://raw.githubusercontent.com/StevenGonzalez/envsync/main/assets/brand/envsync-logo.png" />
</p>

EnvSync is a CLI-first universal environment variable manager. It keeps a single schema file as the source of truth for environment variables and synchronises values across local `.env` files, GitHub Actions, Azure DevOps Library variable groups, AWS SSM Parameter Store, HashiCorp Vault, Azure Key Vault, and generated typed bindings for TypeScript and C#.

## Install

```powershell
dotnet tool install --global EnvSync
```

## Quick Start

```powershell
# 1. Create a schema in the current directory
envsync init

# 2. Edit envsync.schema.yaml to match your variables, then validate your .env
envsync validate --provider local:.env

# 3. Push local values to GitHub Actions
envsync push --from local:.env --to github:myorg/myrepo
```

## Docs

Full documentation and examples are available in the GitHub repository README:

https://github.com/StevenGonzalez/envsync
