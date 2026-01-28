# CLAUDE.md - PEX Connector for Aplos

## Overview

PEX Connector for Aplos is a marketplace integration application that synchronizes expense data between PEX (corporate card management platform) and Aplos (fund accounting software for nonprofits and churches). The application is built with a .NET 8.0 backend and Angular 19 frontend, with Azure Functions for background synchronization.

**Purpose**: Enable organizations to connect their PEX business accounts with Aplos accounting systems, automating transaction and vendor card syncing.

## Quick Reference

```bash
# Build
dotnet build src/AplosConnector.sln

# Run tests
dotnet test tests/**/*.csproj

# Run web app locally
cd src/AplosConnector.Web && dotnet run

# Frontend development
cd src/AplosConnector.Web/ClientApp && npm start

# Publish for deployment
dotnet publish src --configuration Release
```

## Project Structure

```
pex-connector-aplos/
├── src/
│   ├── AplosConnector.sln               # Solution file
│   ├── AplosConnector.Web/              # ASP.NET Core web app + Angular SPA
│   │   ├── Controllers/                 # API endpoints
│   │   ├── ClientApp/                   # Angular 19 frontend
│   │   ├── Models/                      # Request/response DTOs
│   │   ├── Startup.cs                   # DI and middleware config
│   │   └── appsettings*.json            # Configuration files
│   ├── AplosConnector.Common/           # Shared business logic
│   │   ├── Services/                    # Business services
│   │   ├── Storage/                     # Azure Table/Queue storage
│   │   ├── Models/                      # Domain models
│   │   ├── VendorCards/                 # Vendor card management
│   │   ├── Entities/                    # Storage entities
│   │   └── Const/                       # Constants
│   ├── AplosConnector.SyncWorker/       # Azure Functions for background sync
│   ├── Aplos.Api.Client/                # Aplos API client library
│   └── PexCard.App.Infrastructure.AzureServiceBus/  # Service Bus wrapper
├── tests/
│   ├── Aplos.Api.Client.Tests/          # API client unit tests
│   ├── AplosConnector.Common.Tests/     # Business logic tests
│   └── AplosConnector.Web.Tests/        # Controller tests
├── azure-pipelines.yml                  # CI/CD pipeline
└── nuget.config                         # NuGet feed configuration
```

## Technology Stack

### Backend (.NET 8.0)

| Component | Package | Version |
|-----------|---------|---------|
| Framework | .NET | 8.0 |
| Web | ASP.NET Core | 8.0.11 |
| Azure Functions | Worker Model | 2.0.0 |
| Table Storage | Azure.Data.Tables | 12.9.1 |
| Queue Storage | Azure.Storage.Queues | 12.21.0 |
| Blob Storage | Azure.Storage.Blobs | 12.23.0 |
| Data Protection | DataProtection.AzureStorage | 3.1.24 |
| Key Vault | Azure.Identity | 1.13.1 |
| Retry/Resilience | Polly | 8.5.0 |
| Caching | LazyCache.AspNetCore | 2.4.0 |
| JSON | Newtonsoft.Json | 13.0.3 |
| PEX API Client | PexCard.Api.Client | 1.90.0 |
| Encryption | PexCard.Shared.Encryption.AspNetCore | 2025.5.21.1 |

### Frontend (Angular 19)

| Component | Version |
|-----------|---------|
| Angular | 19.2.18 |
| Node.js | >=22.0.0 |
| npm | >=10.0.0 |
| Clarity Angular | 17.0.0 |
| Clarity UI | 17.0.0 |
| CDS Core | 6.16.1 |
| TypeScript | 5.8.0 |
| RxJS | 7.8.1 |

### Testing

| Component | Package | Version |
|-----------|---------|---------|
| Test Framework | xUnit | 2.9.2 |
| Mocking | Moq | 4.20.72 |
| Coverage | coverlet.collector | 6.0.2 |
| Test SDK | Microsoft.NET.Test.Sdk | 17.11.1 |
| Frontend Tests | Karma + Jasmine | 6.4.4 / 5.5.0 |

## Build Commands

### .NET Backend

```bash
# Restore NuGet packages
dotnet restore src/AplosConnector.sln

# Build all projects
dotnet build src/AplosConnector.sln

# Build specific configuration
dotnet build src/AplosConnector.sln -c Release
dotnet build src/AplosConnector.sln -c Debug

# Run the web application
cd src/AplosConnector.Web
dotnet run

# Publish for deployment (builds both Web and SyncWorker)
dotnet publish src --configuration Release
```

### Angular Frontend

```bash
cd src/AplosConnector.Web/ClientApp

# Install dependencies
npm install

# Development server (http://localhost:4200)
npm start

# Production build
npm run build

# Run tests
npm test

# Linting
npm run lint
```

### Testing

```bash
# Run all tests
dotnet test tests/**/*.csproj

# Run tests with configuration
dotnet test tests/**/*.csproj --configuration Release

# Run specific test project
dotnet test tests/AplosConnector.Common.Tests/AplosConnector.Common.Tests.csproj

# Frontend tests
cd src/AplosConnector.Web/ClientApp && npm test
```

## Architecture

### Core Components

**AplosConnector.Web** - Main web application
- ASP.NET Core Web API with Angular SPA
- Controllers: `SessionController`, `MappingController`, `AplosController`, `PEXController`, `HealthController`
- Serves Angular frontend from `ClientApp/dist`

**AplosConnector.SyncWorker** - Azure Functions background workers
- `SyncTimer` - Daily CRON trigger (3:16 AM UTC) to enqueue sync jobs
- `SyncProcessor` - Queue-triggered transaction sync processor
- `TokenRefresher` - Periodic Aplos OAuth token refresh
- `PopulateAplosAccountIds` - Account ID population
- `ValidateAplosAccountIds` - Account validation

**AplosConnector.Common** - Shared library
- Business logic services (`AplosIntegrationService`, `StorageMappingService`)
- Azure Storage abstractions (`Pex2AplosMappingStorage`, `SyncHistoryStorage`, `PexOAuthSessionStorage`)
- Queue handling (`Pex2AplosMappingQueue`)
- Vendor card management (`VendorCardService`, `VendorCardStorage`)

**Aplos.Api.Client** - Aplos API client
- HTTP client with factory pattern
- OAuth token decryption
- Polly retry policies

### Data Storage

All data is stored in Azure Storage (no SQL Server):

| Storage Type | Purpose |
|--------------|---------|
| Azure Table Storage | Sessions, mappings, sync history, vendor cards |
| Azure Queue Storage | Async sync job queue |
| Azure Blob Storage | Data Protection keys |
| Azure Key Vault | Encryption key management |

Table Names (defined in storage classes):
- `PexOAuthSessionStorage.TABLE_NAME`
- `Pex2AplosMappingStorage.TABLE_NAME`
- `SyncHistoryStorage.TABLE_NAME`
- `VendorCardStorage.TABLE_NAME`

### Angular Routes

| Route | Component | Auth |
|-------|-----------|------|
| `/connect` | ConnectComponent | Yes |
| `/sync-connect` | SyncConnectComponent | Yes |
| `/sync-manage` | SyncManageComponent | Yes |
| `/vendors-select` | VendorsSelectComponent | Yes |
| `/vendors-manage` | VendorsManageComponent | Yes |
| `/sync-history` | SyncHistoryComponent | Yes |
| `/login` | LoginComponent | No |
| `/finish-pex-login/:sessionId` | FinishPexLoginComponent | No |
| `/finish-aplos-login` | FinishAplosLoginComponent | No |
| `/handle-pex-jwt` | HandlePexJwtComponent | No |
| `/health` | HealthComponent | No |

### Dependency Injection Pattern

Services are registered in `Startup.cs`:
- `IAplosIntegrationService` - Core sync operations
- `IAplosIntegrationMappingService` - Data mapping
- `IStorageMappingService` - Data protection wrapper
- `IVendorCardService` - Vendor card management
- `IAplosApiClientFactory` - Aplos API client creation
- `IPexApiClient` - PEX API communication

### HTTP Client Configuration

PEX API client uses Polly retry policies:
```csharp
services.AddHttpClient<IPexApiClient, PexApiClient>((client) =>
{
    client.BaseAddress = appSettings.PEXAPIBaseURL;
    client.Timeout = TimeSpan.FromSeconds(appSettings.PEXAPITimeout);
})
.UsePexRetryPolicies<PexApiClient>();
```

## Configuration

### Configuration Files

| File | Purpose |
|------|---------|
| `appsettings.json` | Base configuration |
| `appsettings.Development.json` | Local development |
| `appsettings.QA.json` | QA environment |
| `appsettings.Production.json` | Production values |

### Key Settings (appsettings.json)

```json
{
  "ConnectionStrings": {
    "StorageConnectionString": "Azure Storage connection"
  },
  "ApplicationInsightsKey": "Instrumentation key",
  "AppSettings": {
    "PexConnectorBaseURL": "https://oauth.pexcard.com",
    "PexApiClientId": "OAuth client ID",
    "PexApiClientSecret": "OAuth secret",
    "AplosConnectorBaseURL": "This app's base URL",
    "AplosApiClientId": "Aplos OAuth client ID",
    "AplosApiClientSecret": "Aplos OAuth secret",
    "AplosApiBaseURL": "https://app.aplos.com/hermes/api/v1/",
    "PEXAPIBaseURL": "https://coreapi.pexcard.com",
    "PEXAPITimeout": 100,
    "DataProtectionApplicationName": "AplosConnector",
    "DataProtectionBlobContainer": "data-protection",
    "DataProtectionBlobName": "keys.xml",
    "DataProtectionKeyIdentifier": "Key Vault key URI",
    "SyncTransactionsIntervalDays": 60
  }
}
```

### NuGet Configuration

Private NuGet feed configured in `nuget.config`:
```xml
<packageSources>
  <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  <add key="PexCard.PpsFeed" value="https://pexcard.pkgs.visualstudio.com/_packaging/PpsFeed/nuget/v3/index.json" />
</packageSources>
```

## Azure Functions Configuration

### host.json

```json
{
  "version": "2.0",
  "aggregator": { "batchSize": 1 },
  "functionTimeout": "00:10:00",
  "queues": {
    "maxDequeueCount": 1,
    "batchSize": 1
  },
  "logging": {
    "logLevel": { "default": "Information" },
    "applicationInsights": {
      "samplingSettings": { "isEnabled": false }
    }
  }
}
```

### Function Triggers

| Function | Trigger | Schedule/Config |
|----------|---------|-----------------|
| SyncTimer | Timer | `0 16 3 * * *` (3:16 AM UTC daily) |
| SyncProcessor | Queue | From `Pex2AplosMappingQueue` |
| TokenRefresher | Timer | Periodic refresh |

## CI/CD Pipeline

### Azure DevOps Pipeline (`azure-pipelines.yml`)

**Trigger**: Commits to `master` branch

**Steps**:
1. Retrieve encryption certificate from Azure Key Vault
2. Install .NET 8.0 SDK
3. Authenticate to private NuGet feeds
4. Install Node.js 22.x
5. Build and publish (`dotnet publish src --configuration Release`)
6. Run tests (`dotnet test tests/**/*.csproj`)
7. Copy artifacts (Web and SyncWorker)
8. Run Mend dependency scan
9. Publish build artifacts

**Variables**:
- `appName`: 'pex-connector-aplos'
- `buildConfiguration`: 'Release'
- `dotnetVersion`: '8.0'

## Security

### Data Protection

- Application Insights for telemetry (sampling disabled)
- Data Protection API with Azure Key Vault key encryption
- Keys persisted to Azure Blob Storage
- Configuration encryption via `PexCard.Shared.Encryption.AspNetCore` (non-DEBUG builds)
- Certificate (`cert.pfx`) embedded as resource and retrieved from Key Vault in CI/CD

### Security Headers

Added via middleware in `Startup.cs`:
```
Content-Security-Policy: script-src 'self' 'unsafe-eval'
X-Frame-Options: DENY
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer
Feature-Policy: camera 'none'; microphone 'none'; ...
```

### Authentication Flow

1. OAuth 2.0 for both PEX and Aplos
2. Session-based with GUID session IDs
3. JWT token handling for PEX (`HandlePexJwtComponent`)
4. Angular route guards (`AuthGuard`) for protected routes

## Development Notes

### Local Development

1. Ensure Node.js 22.x is installed
2. Angular dependencies install automatically on first build (via MSBuild target)
3. DEBUG builds bypass configuration encryption
4. Use Azure Storage Emulator or Azurite for local storage

### Common Issues

**Missing node_modules**:
```bash
cd src/AplosConnector.Web/ClientApp && npm install
```

**Certificate not found** (Release builds):
- `cert.pfx` must be in `src/AplosConnector.Web/`
- In CI/CD, retrieved from Azure Key Vault

**NuGet authentication**:
```bash
dotnet restore --interactive
# Or use Azure Artifacts Credential Provider
```

### Naming Conventions

- Storage classes: `{Entity}Storage` (e.g., `Pex2AplosMappingStorage`)
- Queue classes: `{Entity}Queue` (e.g., `Pex2AplosMappingQueue`)
- Services: `I{Name}Service` + `{Name}Service`
- Controllers: `{Feature}Controller`
- Angular components: `{feature}.component.ts`

### Error Handling

- `PexApiClientException` - Handled in Startup.cs exception middleware
- Returns appropriate HTTP status codes
- Application Insights captures exceptions

## Key Files Reference

| File | Purpose |
|------|---------|
| `src/AplosConnector.Web/Startup.cs` | DI configuration, middleware |
| `src/AplosConnector.Web/Controllers/*.cs` | API endpoints |
| `src/AplosConnector.Common/Services/AplosIntegrationService.cs` | Core sync logic |
| `src/AplosConnector.SyncWorker/SyncTimer.cs` | Daily sync trigger |
| `src/AplosConnector.SyncWorker/SyncProcessor.cs` | Transaction processor |
| `src/AplosConnector.Web/ClientApp/src/app/app-routing.module.ts` | Angular routes |
| `azure-pipelines.yml` | CI/CD configuration |
