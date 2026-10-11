# Consuming Pheme

Two ways: the published packages from nuget.org, or local packages packed from this repository to try unreleased code.

## From nuget.org

The packages are on [nuget.org](https://www.nuget.org/packages/Pheme), so no account, token or NuGet configuration is
needed:

```sh
dotnet add package Pheme
dotnet add package Pheme.FluentValidation      # validation behavior
dotnet add package Pheme.EntityFrameworkCore   # domain event dispatch on save, outbox
dotnet add package Pheme.Testing               # outbox helpers and layer rules for tests
```

`Pheme.Abstractions` (the contracts) comes with `Pheme`; reference it alone from a project that only declares messages
and handlers. With central package management, put the versions in `Directory.Packages.props` instead.

Packages are signed by nuget.org, and each links to the commit it was built from (SourceLink), so a debugger can step
into Pheme's code.

## Local packages

How to try unreleased Pheme in an application without publishing anything and without changing the machine's or the
application's NuGet configuration.

### 1. Pack

From the Pheme repository root, with a version that cannot clash with a published one:

```sh
for project in src/*/*.csproj; do
    dotnet pack "$project" --configuration Release --output /tmp/pheme/packages -p:Version=2.1.0-local.1
done
```

Every pack of changed code needs a new number (`-local.2`, ...): NuGet caches a restored version, so the same
number would keep the old content.

### 2. A NuGet configuration for the restore only

Outside both repositories, for example `/tmp/pheme/nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="pheme-local" value="/tmp/pheme/packages" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="pheme-local">
      <package pattern="Pheme" />
      <package pattern="Pheme.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

A file passed with `--configfile` replaces the configuration of the machine and of the application for that
command, so private feeds and source mappings elsewhere do not interfere. The mapping takes the Pheme packages
only from the local folder.

### 3. Reference the version, restore, build and test

In the application's `Directory.Packages.props` (or `PackageReference`):

```xml
<PackageVersion Include="Pheme" Version="2.1.0-local.1" />
<PackageVersion Include="Pheme.FluentValidation" Version="2.1.0-local.1" />
<PackageVersion Include="Pheme.EntityFrameworkCore" Version="2.1.0-local.1" />
<PackageVersion Include="Pheme.Testing" Version="2.1.0-local.1" />
```

```sh
dotnet restore --configfile /tmp/pheme/nuget.config
dotnet build --no-restore
dotnet test --no-build
```

Packages the application needs from other private feeds go into the same file, as further sources.

### Cleaning up

Revert the versions, delete `/tmp/pheme`, and, if disk space matters, the local versions in the global packages
folder: `rm -rf ~/.nuget/packages/pheme*/*-local.*`.
