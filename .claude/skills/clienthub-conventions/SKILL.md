---
name: clienthub-conventions
description: Coding conventions for the Client Management Hub (ASP.NET Core 10 Razor Pages, EF Core, Tailwind, HTMX, resx localisation). Preloaded into the developer agent.
---

# Client Hub conventions

## Layout
- `src/ClientHub.Web/` — the app. `Pages/Staff/*` (roles Lawyer, Staff), `Pages/Portal/*` (role Client),
  `Pages/Account/*` (shared). `Data/` (DbContext, entities, migrations), `Services/` (AccessService,
  NotificationService, BillService, BlobService, EmailService), `Resources/` (.resx).
- `tests/ClientHub.Tests/` — xUnit. `Unit/` for services, `Integration/` for `WebApplicationFactory` tests.

## Data
- Entities in `Data/Entities/`, one class per table from the TSD data model. `Guid Id`, `DateTime CreatedAt`,
  `DateTime UpdatedAt` on every entity via a base class. snake_case table and column names with
  `EFCore.NamingConventions` (`UseSnakeCaseNamingConvention()`).
- Status and type fields are C# enums stored as strings (`HasConversion<string>()`).
- Money is `decimal` mapped to `numeric(12,2)`.
- Migrations run at startup: `db.Database.Migrate()` in `Program.cs`.

## Access
- `IAccessService.CanAccessCase(ClaimsPrincipal user, Case c)` and `.CanAccessClient(...)` are the only
  gates. Every handler that takes an id loads the entity, checks, and returns `NotFound()` on failure.
- List queries go through `IAccessService.ScopeCases(IQueryable<Case>, user)` so filtering is in SQL.
- Folder policies: `options.Conventions.AuthorizeFolder("/Staff", "StaffOnly")` and
  `AuthorizeFolder("/Portal", "ClientOnly")`.

## Notifications
- `INotificationService.NotifyAsync(IEnumerable<Guid> userIds, NotificationKind kind, string titleKey, string linkUrl)`.
  Writes rows; emails Client users via `IEmailService` in the same request with a 5-second timeout; logs and
  continues on failure.

## Bills
- `Bill.Recalculate()` sums lines into `Total`. Called in the service before every save of a Draft bill.
  Sent and Paid bills reject line edits.

## UI
- Razor Pages, Tailwind via the CLI (`npm run css:watch` in the container). HTMX for: chat message list refresh
  (poll every 5 s), document-request status after upload, bill line add/remove. Alpine.js for toggles only.
- All user-facing strings through `IStringLocalizer<SharedResource>`: `Resources/SharedResource.en.resx`
  and `SharedResource.ms.resx`. Key format `Area.Page.Element`, e.g. `Portal.Case.PendingDocuments`.
- Dates `dd/MM/yyyy`, money `RM 1,234.50`, via two helpers in `Helpers/Format.cs`.
- Mobile first. Every page checked at 390px width.

## Files
- Uploads go browser → Vercel Blob with a client token from `BlobService.CreateUploadTokenAsync(caseId, requestId?)`.
  Allowed types pdf, docx, jpg, png; 20 MB. The completion handler writes the `documents` row.
- Downloads: handler checks access, then redirects to a short-lived Blob URL.

## Tests
- Unit: `BillServiceTests`, `AccessServiceTests`, `NotificationServiceTests`.
- Integration: `AccessIntegrationTests` (404 cases), `PortalFlowTests` (client sees only own data).
- Test DB: the `db` container with a `clienthub_test` database, reset per test class.

## Commands (inside the container)
- `docker compose exec app dotnet watch --project src/ClientHub.Web`
- `docker compose exec app dotnet test`
- `docker compose exec app dotnet ef migrations add <Name> --project src/ClientHub.Web`
- `docker compose exec app dotnet run --project src/ClientHub.Web -- seed`
