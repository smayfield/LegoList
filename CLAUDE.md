# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All commands should be run from `src/LegoList.Api/` unless otherwise noted.

```bash
# Build
dotnet build src/LegoList.sln

# Run (HTTP on port 5003)
dotnet run --project src/LegoList.Api/LegoList.Api.csproj

# Restore dependencies
dotnet restore src/LegoList.sln
```

No test project exists yet. The `.http` file at `src/LegoList.Api/LegoList.http` can be used for manual endpoint testing with the VSCode REST Client or Visual Studio.

## Architecture

This is an ASP.NET Core 9.0 Web API project (`net9.0`) with nullable reference types and implicit usings enabled.

- **Entry point**: `src/LegoList.Api/Program.cs` — registers services, configures middleware (HTTPS redirect, authorization, controllers), and maps OpenAPI in Development only.
- **Controllers**: `src/LegoList.Api/Controllers/` — standard attribute-routed MVC controllers injecting `ILogger<T>`.
- **Models**: defined alongside controllers or in dedicated files at the project root (e.g., `WeatherForecast.cs`).
- **Configuration**: `appsettings.json` / `appsettings.Development.json` for environment-specific settings.

The project currently contains only the default scaffold (WeatherForecast endpoint). Real Lego list domain logic has not been added yet.
