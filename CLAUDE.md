# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All commands should be run from the repo root unless otherwise noted.

```bash
# Build everything
dotnet build src/LegoList.sln

# Run all tests
dotnet test src/LegoList.sln

# Run API (HTTP on port 5003)
dotnet run --project src/LegoList.Api/LegoList.Api.csproj

# Run Blazor UI (default port 5258 / 7258 HTTPS)
dotnet run --project src/LegoList.Blazor/LegoList.Blazor.csproj

# Restore dependencies
dotnet restore src/LegoList.sln

# Start PostgreSQL and apply schema via Liquibase
docker-compose up

# Start only the database (detached)
docker-compose up -d postgres liquibase
```

## PowerShell Scripts

Two convenience scripts live at the repo root:

```powershell
# Initialize the database (run once, or after schema changes)
.\setup.ps1

# Build, test, then launch the Blazor UI in a browser
.\start.ps1
```

The `.http` file at `src/LegoList.Api/LegoList.http` can be used for manual endpoint testing with the VSCode REST Client or Visual Studio.

## Architecture

Two projects in one solution:

### LegoList.Api — ASP.NET Core 10.0 Web API (`net10.0`)
- **Entry point**: `src/LegoList.Api/Program.cs` — registers EF Core (PostgreSQL), CORS, controllers, OpenAPI.
- **Models**: `src/LegoList.Api/Models/` — `SetList`, `LegoSet` (includes the user's own details: built, display location, purchase price, notes), `SetMetadata` (cached Rebrickable data per set number)
- **Services**: `src/LegoList.Api/Services/` — `RebrickableService` (set lookups, rate-limited by `RebrickableRateLimiter` / `RebrickableThrottlingHandler`), `ListExportService`, `UserService`
- **Data**: `src/LegoList.Api/Data/LegoListDbContext.cs` — EF Core context
- **Controllers**: `src/LegoList.Api/Controllers/` — `ListsController` (`api/lists`), `SetsController` (`api/lists/{listId}/sets`), `AdminController` (`api/admin`, policy `Admin` — see Admin access below)
- **Configuration**: connection string in `appsettings.json` (`DefaultConnection`)
- **Packages**: `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0, `Microsoft.EntityFrameworkCore.Design` 10.0

### LegoList.Blazor — Blazor Web App (`net10.0`, Server interactivity)
- **Entry point**: `src/LegoList.Blazor/Program.cs` — registers `IHttpClientFactory` pointed at the API
- **Pages**: `src/LegoList.Blazor/Components/Pages/` — `Lists.razor` (`/`), `ListDetail.razor` (`/lists/{id}`), `SetDetail.razor` (`/lists/{listId}/sets/{id}`), `Admin.razor` (`/admin` stats), `AdminUsers.razor` (`/admin/users`), `AdminUser.razor` (`/admin/users/{id}`)
- **Components**: `src/LegoList.Blazor/Components/ImageLightbox.razor` (modal picture viewer)
- **Services**: `src/LegoList.Blazor/Services/` — `BulkSetParser`, `BulkLookupRunner` (queue for the bulk "Look up missing details"); both are linked into the test project so they can be unit-tested
- **Models**: `src/LegoList.Blazor/Models/` — `SetListDto`, `LegoSetDto`
- **Configuration**: `ApiBaseUrl` in `appsettings.json` (defaults to `http://localhost:5003/`)

## Database

PostgreSQL runs in Docker. Connection string: `Host=localhost;Database=legolist;Username=legolist;Password=legolist`

Schema is managed by **Liquibase** (not EF Core migrations). EF Core is used only for querying.

To add a migration:
1. Create a new SQL file in `liquibase/changelog/changes/` (e.g., `002-add-column.sql`)
2. Add an `<include>` entry for it in `liquibase/changelog/db.changelog-root.xml`
3. Run `docker-compose up liquibase` to apply

## Admin access

Admins are the (Google-verified) emails listed under `Admin:Emails` in the API's `appsettings.json` (currently just the owner). The API enforces this with the `Admin` authorization policy (`AdminAccess.IsAdmin`); the Blazor app has a matching policy (reading its own `Admin:Emails`) only to hide the pages and the nav link. To add an admin, add the email to both appsettings files (or set `Admin__Emails__0` env vars in production). There is no admin column in the database.
