### Scripts

These scripts use [dotnet-script](https://github.com/filipw/dotnet-script).

To install:
```
dotnet tool install -g dotnet-script
```

To run a script:
```
dotnet script UpdateSources.csx
```

#### UpdateSources.csx

This script downloads, copies, and organizes the precompiled slang binaries so that it can be used by the repository.
