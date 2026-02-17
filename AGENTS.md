# AGENTS.md

This file provides guidance to AI coding agents (Claude Code, Copilot, etc.) when working with code in this repository.

## Project Overview

**PEX Connector for Aplos** is a web application and Azure Functions worker that synchronizes PEX Card financial transactions, expenses, and account data with Aplos nonprofit accounting software. It provides a connector between the PEX Card expense management platform and Aplos fund accounting, enabling automatic or manual syncing of transactions, contacts, accounts, funds, and tags for nonprofit organizations and churches.

The application consists of two main runtime components:
1. **Web Application** - ASP.NET Core 8.0 API with Angular 19 SPA for connector configuration and management
2. **Sync Worker** - Azure Functions v4 for background sync processing, scheduled jobs, and token management

## Solution Structure

```
pex-connector-aplos/
├── src/
│   ├── AplosConnector.sln                           # Main solution file
│   ├── AplosConnector.Web/                          # ASP.NET Core 8.0 Web API + Angular SPA
│   │   ├── Controllers/                             # REST API controllers (5 controllers)
│   │   ├── ClientApp/                               # Angular 19 frontend application
│   │   ├── Startup.cs                               # DI container, middleware, service registration
│   │   ├── Program.cs                               # Host builder configuration
│   │   └── appsettings*.json                        # Environment configurations
│   ├── AplosConnector.SyncWorker/                   # Azure Functions v4 worker
│   │   ├── SyncTimer.cs                             # Scheduled daily sync trigger
│   │   ├── SyncProcessor.cs                         # Queue-triggered sync processing
│   │   ├── TokenRefresher.cs                        # Service Bus token refresh handler
│   │   ├── PopulateAplosAccountIds.cs               # Account ID population function
│   │   ├── ValidateAplosAccountIds.cs               # Account validation function
│   │   └── Program.cs                               # Azure Functions host setup
│   ├── AplosConnector.Common/                       # Shared business logic library
│   │   ├── Models/                                  # Domain models, DTOs, settings
│   │   ├── Services/                                # Core business services
│   │   │   ├── AplosIntegrationService.cs           # Primary sync orchestration
│   │   │   ├── AplosIntegrationMappingService.cs    # PEX↔Aplos data mapping
│   │   │   ├── StorageMappingService.cs             # Encrypted storage operations
│   │   │   └── VendorCardService.cs                 # Vendor card management
│   │   └── Storage/                                 # Azure Table/Queue storage abstractions
│   │       ├── PexOAuthSessionStorage.cs            # OAuth session persistence
│   │       ├── Pex2AplosMappingStorage.cs           # Account mapping persistence
│   │       ├── SyncHistoryStorage.cs                # Sync history tracking
│   │       ├── VendorCardStorage.cs                 # Vendor card data
│   │       └── Pex2AplosMappingQueue.cs             # Sync job queue
│   ├── Aplos.Api.Client/                            # Custom Aplos API HTTP client
│   │   ├── AplosApiClient.cs                        # Aplos REST API client implementation
│   │   ├── AplosApiClientFactory.cs                 # Factory for creating configured clients
│   │   └── Models/                                  # Aplos API request/response models
│   └── PexCard.App.Infrastructure.AzureServiceBus/  # Azure Service Bus messaging
├── tests/
│   ├── Aplos.Api.Client.Tests/                      # Aplos client unit tests
│   ├── AplosConnector.Common.Tests/                 # Common services unit tests
│   └── AplosConnector.Web.Tests/                    # Controller unit tests
├── azure-pipelines.yml                              # CI/CD build pipeline (master trigger)
├── azure-pipelines-1.yml                            # PR validation pipeline
└── nuget.config                                     # NuGet package sources
```

## Build Commands

### .NET Build

```bash
# Restore NuGet packages (requires Azure DevOps artifacts token for PexCard feed)
dotnet restore src/AplosConnector.sln --configfile nuget.config

# Build solution (Debug)
dotnet build src/AplosConnector.sln --configuration Debug

# Build solution (Release)
dotnet build src/AplosConnector.sln --configuration Release
```

### Angular Frontend

```bash
cd src/AplosConnector.Web/ClientApp

# Install dependencies (requires Node.js 22+)
npm install

# Start development server
npm start

# Production build
npm run build -- --configuration production

# Run unit tests (Karma/Jasmine)
npm test

# Lint
npm run lint
```

### Running Tests

```bash
# Run all .NET unit tests
dotnet test src/AplosConnector.sln --configuration Debug

# Run specific test project
dotnet test tests/Aplos.Api.Client.Tests/
dotnet test tests/AplosConnector.Common.Tests/
dotnet test tests/AplosConnector.Web.Tests/
```

### Running Locally

```bash
# Run the Web API (includes Angular SPA in development mode)
cd src/AplosConnector.Web
dotnet run

# Run the SyncWorker locally (requires Azure Functions Core Tools)
cd src/AplosConnector.SyncWorker
func start
```

## Architecture

### Dual Runtime Architecture

```
┌─────────────────────────────────────────────────────┐
│                    Web Application                    │
│  Angular 19 SPA ←→ ASP.NET Core 8.0 Controllers     │
│       ↓                    ↓                          │
│  OAuth Flow          REST API Endpoints               │
│       ↓                    ↓                          │
│  PEX/Aplos Auth    AplosIntegrationService            │
│                           ↓                           │
│                    Azure Table Storage                 │
│                    Azure Queue Storage                 │
└─────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────┐
│               Azure Functions SyncWorker              │
│  SyncTimer (Cron) → Pex2AplosMappingQueue             │
│  SyncProcessor (Queue) → PEX API → Aplos API         │
│  TokenRefresher (Service Bus) → Token Renewal         │
└─────────────────────────────────────────────────────┘
```

### Controller Layer (`AplosConnector.Web/Controllers/`)

| Controller | Route Prefix | Purpose |
|---|---|---|
| `SessionController` | `/api/session` | OAuth flow orchestration (PEX + Aplos login URLs, token creation) |
| `MappingController` | `/api/mapping` | Sync settings management (CRUD for PEX↔Aplos mappings) |
| `AplosController` | `/api/aplos` | Aplos data queries (accounts, contacts, funds, tags) |
| `PEXController` | `/api/pex` | PEX data queries (tags, validity status) |
| `HealthController` | `/api/health` | System health checks (PEX, Aplos, Storage connectivity) |

All controllers except `HealthController` require a valid session ID (`Guid`) passed as a header or parameter.

### Service Layer (`AplosConnector.Common/Services/`)

| Service | Interface | Purpose |
|---|---|---|
| `AplosIntegrationService` | `IAplosIntegrationService` | Core sync orchestration: fetches PEX transactions, maps to Aplos format, creates Aplos entries |
| `AplosIntegrationMappingService` | `IAplosIntegrationMappingService` | Data transformation between PEX and Aplos models (tags, contacts, accounts, funds) |
| `StorageMappingService` | `IStorageMappingService` | Encrypted read/write operations for sensitive data (tokens, credentials) via Data Protection API |
| `VendorCardService` | `IVendorCardService` | Vendor card sync operations between PEX and Aplos contacts |

### Storage Layer (`AplosConnector.Common/Storage/`)

Uses **Azure Table Storage** as the primary data store (not SQL Server):

| Storage Class | Table/Queue | Purpose |
|---|---|---|
| `PexOAuthSessionStorage` | Table | OAuth session tracking (session ID, tokens, business account) |
| `Pex2AplosMappingStorage` | Table | Central mapping configuration (PEX↔Aplos account links, sync settings, tag mappings) |
| `SyncHistoryStorage` | Table | Sync operation history and results |
| `VendorCardStorage` | Table | Vendor card information |
| `Pex2AplosMappingQueue` | Queue | Async sync job queue for worker processing |

All storage classes extend `AzureTableStorageAbstract` or `AzureQueueAbstract` base classes.

### Azure Functions (`AplosConnector.SyncWorker/`)

| Function | Trigger | Purpose |
|---|---|---|
| `SyncTimer` | Timer (`0 16 3 * * *` - daily 3:16 AM UTC) | Enqueues all auto-sync mappings for processing |
| `SyncProcessor` | Queue (`Pex2AplosMappingQueue`) | Executes actual PEX→Aplos transaction sync |
| `TokenRefresher` | Service Bus (`token.expired` topic) | Refreshes expired access tokens |
| `PopulateAplosAccountIds` | HTTP | Populates Aplos account IDs for partner verification |
| `ValidateAplosAccountIds` | HTTP | Validates Aplos account ID configurations |

## Key Domain Models

### Central Entity: `Pex2AplosMappingModel`

The core configuration entity that links a PEX business account to an Aplos organization:

- PEX Business Account ID and authentication tokens
- Aplos Organization ID and authentication mode (Partner vs Client)
- Sync settings (`AutomaticSync`, `SyncApprovedOnly`, `SyncTags`, etc.)
- Expense account mappings (PEX accounts → Aplos accounts/funds)
- Tag mappings (`TagMappingModel[]` - PEX tags → Aplos tags/funds)
- Vendor card configurations
- Feature flags (`SyncTransactions`, `SyncTransfers`, `SyncFees`, `SyncInvoices`, `SyncRebates`)

### Sync Flow

1. User authenticates via OAuth (PEX + Aplos) through the Angular SPA
2. `Pex2AplosMappingModel` created/updated with account links and sync settings
3. Sync triggered (manual via UI or scheduled via `SyncTimer`)
4. `SyncProcessor` dequeues mapping from `Pex2AplosMappingQueue`
5. `AplosIntegrationService` fetches transactions from PEX API
6. `AplosIntegrationMappingService` maps transactions to Aplos format
7. Mapped transactions created in Aplos via `AplosApiClient`
8. Sync results recorded in `SyncHistoryStorage`

## External Integrations

### PEX Card API

- **SDK**: `PexCard.Api.Client` v1.90.0 (internal NuGet package)
- **Base URLs**: `https://coreapi.pexcard.com` (prod), `https://sandbox-coreapi.pexcard.com` (sandbox)
- **Auth**: OAuth 2.0 with API Client ID + Secret
- **Operations**: Transactions, tags, business info, vendor cards, cardholder data

### Aplos API

- **Client**: Custom `Aplos.Api.Client` (in-repo implementation)
- **Base URL**: `https://app.aplos.com/hermes/api/v1/`
- **Auth**: OAuth 2.0 (Partner or Client authentication mode)
- **Operations**: Transactions, contacts, accounts, funds, tags, partner verification
- **Partner Verification URL**: `https://aplos.com/aws/accounting/pex`

### Azure Services

| Service | Usage |
|---|---|
| Azure Table Storage | Primary data persistence (sessions, mappings, history, vendor cards) |
| Azure Queue Storage | Async sync job queue |
| Azure Blob Storage | Data protection key storage |
| Azure Key Vault | Secret management, encryption certificates |
| Azure Service Bus | Token expiration event messaging (`token.expired` topic) |
| Application Insights | Application telemetry and monitoring |
| Azure Functions v4 | Background worker hosting |

## Authentication & Security

### OAuth Flow

1. User initiates login from Angular SPA
2. `SessionController` provides PEX OAuth URL → user authenticates with PEX
3. Callback returns PEX access token, stored in `PexOAuthSessionStorage`
4. `SessionController` provides Aplos OAuth URL → user authenticates with Aplos
5. Aplos token stored in encrypted `Pex2AplosMappingStorage`

### Authentication Modes

- **Partner Authentication**: PEX acts as a verified partner in Aplos (preferred)
- **Client Authentication**: Individual Aplos client credentials (legacy)

### Data Protection

- ASP.NET Core Data Protection API for encrypting sensitive values (tokens, secrets)
- Keys stored in Azure Blob Storage
- Key encryption via Azure Key Vault
- `PexCard.Shared.Encryption.AspNetCore` for additional encryption
- `StorageMappingService` handles all encrypt/decrypt operations

## Configuration

### Environment Files

- `appsettings.json` - Base configuration with tokenized placeholders
- `appsettings.Development.json` - Local development (sandbox credentials)
- `appsettings.QA.json` - QA environment
- `appsettings.Production.json` - Production (encrypted)

### Key Configuration Sections (`AppSettings`)

| Setting | Purpose |
|---|---|
| `PexConnectorBaseURL` | PEX OAuth base URL |
| `PexApiClientId` / `PexApiClientSecret` | PEX API credentials |
| `AplosConnectorBaseURL` | This application's base URL |
| `AplosApiClientId` / `AplosApiClientSecret` | Aplos API credentials |
| `AplosApiBaseURL` | Aplos REST API base URL |
| `AplosPartnerVerificationUrl` | Aplos partner verification endpoint |
| `PEXAPIBaseURL` | PEX Card API base URL |
| `PEXAPITimeout` | PEX API timeout in seconds |
| `DataProtectionApplicationName` | Data protection app discriminator |
| `DataProtectionBlobContainer` / `DataProtectionBlobName` | Key storage location |
| `DataProtectionKeyIdentifier` | Azure Key Vault key URI |
| `AzureServiceBusUrl` | Service Bus connection |
| `AzureServiceBusTopicName` | Token expiration topic name |
| `SyncTransactionsIntervalDays` | Default sync lookback window (60 days) |

### Connection Strings

- `StorageConnectionString` - Azure Storage account connection string (Tables, Queues, Blobs)

## Frontend (Angular 19)

### Technology Stack

- **Angular**: 19.2.x
- **UI Framework**: Clarity Design System (@clr/angular 17.0.0, @clr/ui 17.0.0, @cds/core 6.16.x)
- **TypeScript**: 5.8.x
- **Node.js**: 22+
- **RxJS**: 7.8.x
- **Testing**: Jasmine + Karma

### Key Components

| Component | Purpose |
|---|---|
| `login` | Initial entry point, OAuth flow start |
| `connect` | PEX/Aplos account connection setup |
| `finish-pex-login` | PEX OAuth callback handling |
| `finish-aplos-login` | Aplos OAuth callback handling |
| `handle-pex-jwt` | PEX JWT token processing |
| `sync-connect` | Configure sync connections |
| `sync-manage` | Manage sync settings and manual triggers |
| `sync-history` | View past sync operation results |
| `vendors-manage` | Manage vendor card mappings |
| `vendors-select` | Select vendor cards for sync |
| `select-list` | Reusable selection list component |
| `loading-placeholder` | Loading state UI |
| `health` | System health dashboard |

### Angular Services

| Service | Purpose |
|---|---|
| `AuthService` | Session management, OAuth state |
| `AplosService` | Aplos API calls (accounts, contacts, funds, tags) |
| `PexService` | PEX API calls (tags, validity) |
| `MappingService` | Sync configuration read/write |
| `HealthService` | Health check endpoint calls |
| `CacheRepositoryService` | Client-side caching |

### Route Guard

- `AuthGuard` protects routes requiring active session

## CI/CD Pipelines

### Build Pipeline (`azure-pipelines.yml`)

- **Trigger**: Push to `master` branch
- **Agent**: Ubuntu-latest
- **Steps**:
  1. Decrypt encryption certificate from Azure Key Vault
  2. NuGet authentication for private PexCard feed
  3. `dotnet publish` (Release configuration, .NET 8.0)
  4. `dotnet test` (all test projects)
  5. Angular production build (`ng build --configuration production`)
  6. Mend CLI dependency/security scanning (SCA + SAST)
  7. Publish artifacts (Web + SyncWorker)

### PR Validation Pipeline (`azure-pipelines-1.yml`)

- **Trigger**: Pull requests to `master`
- **Configuration**: Debug build
- **Steps**: Build, test, Angular build, dependency scanning

## NuGet Package Sources

```xml
<packageSources>
  <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  <add key="PexCard.PpsFeed" value="https://pexcard.pkgs.visualstudio.com/_packaging/PpsFeed/nuget/v3/index.json" />
</packageSources>
```

## Testing

### .NET Unit Tests

- **Framework**: xUnit (Aplos.Api.Client.Tests, AplosConnector.Web.Tests), MSTest or xUnit (AplosConnector.Common.Tests)
- **Mocking**: Moq
- **Key test files**:
  - `AplosApiClientTests.cs` - Aplos API client behavior
  - `AplosApiClientFactoryTests.cs` - Client factory logic
  - `AplosAccessTokenDecryptorTests.cs` - Token decryption
  - `AplosIntegrationServiceTests.cs` - Core sync logic
  - `AplosIntegrationMappingServiceTests.cs` - Data mapping
  - `AplosControllerTests.cs`, `PexControllerTests.cs`, `MappingControllerTests.cs`, `SessionControllerTests.cs` - Controller behavior
  - `HealthControllerTests.cs` - Health check endpoints
  - `DateTimeExtensionTests.cs` - Utility extensions

### Angular Tests

- **Framework**: Jasmine + Karma
- **Run**: `npm test` from `src/AplosConnector.Web/ClientApp/`

## Development Guidelines

### Adding New API Endpoints

1. Add controller method in `AplosConnector.Web/Controllers/`
2. Implement business logic in `AplosConnector.Common/Services/`
3. Add storage operations if needed in `AplosConnector.Common/Storage/`
4. Create/update models in `AplosConnector.Common/Models/`
5. Add corresponding Angular service method and component if UI needed
6. Add unit tests in the appropriate test project

### Adding New Sync Features

1. Extend `Pex2AplosMappingModel` with new configuration properties
2. Update `AplosIntegrationService` sync logic
3. Update `AplosIntegrationMappingService` for data mapping
4. Update `SyncProcessor` if new processing steps needed
5. Update Angular `sync-manage` component for UI settings
6. Update `MappingController` endpoints if API changes needed

### Adding New Azure Functions

1. Create new function class in `AplosConnector.SyncWorker/`
2. Configure trigger (Timer, Queue, Service Bus, HTTP)
3. Register any new services in `Program.cs`
4. Add configuration in `local.settings.json` for local development

### Code Patterns

- Use `async/await` with `CancellationToken` for all async operations
- Use `IAplosApiClientFactory` to create Aplos API clients (never instantiate directly)
- Use `IStorageMappingService` for reading/writing encrypted storage data
- Use Polly retry policies for external HTTP calls (already configured in Startup.cs)
- Session validation required on all user-facing endpoints (check session ID is valid GUID)
- Follow existing storage abstraction patterns when adding new Azure Table entities

### Security Requirements

- Never log or expose PEX/Aplos access tokens, client secrets, or API keys
- Always use `StorageMappingService` for encrypting sensitive data before storage
- Use Data Protection API for all token encryption/decryption
- Follow OAuth 2.0 best practices for token handling and refresh
- Validate all user inputs (session IDs, account IDs, mapping configurations)

## Important Notes

- **Runtime**: .NET 8.0 (LTS)
- **Data Store**: Azure Table Storage (not SQL Server) - this differs from most PEX microservices
- **No Autofac**: Uses built-in ASP.NET Core DI (unlike most PEX services that use Autofac)
- **No Dapper/EF**: No relational database - all persistence is Azure Storage
- **Angular SPA**: Served via `UseSpaStaticFiles` middleware in production, Angular dev server proxy in development
- **Azure Functions v4**: Isolated worker model with .NET 8.0
- **Clarity UI**: Frontend uses VMware Clarity Design System for components
- **Dual Auth**: Application manages two separate OAuth flows (PEX + Aplos)
- **Cron Schedule**: Daily sync runs at 3:16 AM UTC
- **Source Control**: Azure DevOps Git
- **CI/CD**: Azure DevOps Pipelines
- **Pull Requests**: Always target `master` branch. Use Azure DevOps CLI: `az repos pr create --title "PR Title" --target-branch master`
