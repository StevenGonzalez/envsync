# Maintainer Release Notes

EnvSync publishes from GitHub Actions with `.github/workflows/cd.yml`.

## One-time setup

- Create a NuGet API key with push permission for the `EnvSync` package.
- Add it to GitHub repository secrets as `NUGET_API_KEY`.

## Versioning

- Use SemVer tags as the source of truth: `vMAJOR.MINOR.PATCH`.
- The release workflow strips the leading `v` and packs with that exact version.
- Keep normal commits version-agnostic; only tags create published versions.
- Release tags must point to commits already contained in `main` or `master`.

## Publish

```powershell
git tag v1.0.1
git push origin v1.0.1
```

The tag triggers restore, formatting verification, tests, package vulnerability audit, pack, NuGet publish, and GitHub Release creation.

## Local Package Smoke Test

```powershell
dotnet pack src/EnvSync.Cli/EnvSync.Cli.csproj -c Release -o artifacts/packages /p:Version=1.0.1-local
dotnet tool install --tool-path .tmp/envsync-tool --add-source artifacts/packages EnvSync --version 1.0.1-local
.tmp/envsync-tool/envsync --version
.tmp/envsync-tool/envsync --help
```
