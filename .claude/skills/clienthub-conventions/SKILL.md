---
name: clienthub-conventions
description: Coding conventions for the Client Management Hub (ASP.NET Core 10 Razor Pages, EF Core, Tailwind, HTMX, resx localisation, S3 presigned uploads). Preloaded into the developer agent.
---

# Client Hub conventions

## Layout
- `src/ClientHub.Web/` — the app. `Pages/Staff/*` (roles Lawyer, OfficeManager, Staff), `Pages/Portal/*`
  (role Client), `Pages/Account/*` (shared). `Data/` (DbContext, entities, migrations), `Services/`
  (AccessService, NotificationService, BillService, StorageService, EmailService), `Resources/` (.resx).
- `tests/ClientHub.Tests/` — xUnit. `Unit/` for services, `Integration/` for `WebApplicationFactory` tests.

## Data
- Entities in `Data/Entities/`, one class per table from the TSD data model. `Guid Id`, `DateTime CreatedAt`,
  `DateTime UpdatedAt` on every entity via a base class. snake_case table and column names with
  `EFCore.NamingConventions` (`UseSnakeCaseNamingConvention()`).
- Status and type fields are C# enums stored as strings (`HasConversion<string>()`).
  `UserRole { Lawyer, OfficeManager, Staff, Client }`.
- Money is `decimal` mapped to `numeric(12,2)`.
- Nothing is deleted. Documents have `IsActive`; a global query filter hides inactive documents, and the
  remove handler sets `IsActive = false`, `RemovedById`, `RemovedAt` and writes an audit row.
- Migrations run at startup: `db.Database.Migrate()` in `Program.cs`.

## Access
- `IAccessService.CanAccessCase(ClaimsPrincipal user, Case c)` and `.CanAccessClient(user, Client c)` are the
  only gates. Lawyer and OfficeManager: always. Staff: a `case_staff` row (for a client, on any of its cases).
  Client: their own. Every handler that takes an id loads the entity, checks, and returns `NotFound()` on failure.
- List queries go through `IAccessService.ScopeCases(IQueryable<Case>, user)` and `.ScopeClients(...)` so
  filtering is in SQL.
- Role capabilities live in `IAccessService` too: `CanCreateClients`, `CanCreateCases`, `CanAssignStaff`
  (Lawyer, OfficeManager), `CanManageSettings` (Lawyer). Pages ask these; they never compare role names.
- Folder policies: `AuthorizeFolder("/Staff", "FirmOnly")` (Lawyer, OfficeManager, Staff),
  `AuthorizePage("/Staff/Settings", "LawyerOnly")`, `AuthorizeFolder("/Portal", "ClientOnly")`.
- Two-factor is enforced at login for Lawyer and OfficeManager.

## Notifications
- `INotificationService.NotifyAsync(IEnumerable<Guid> userIds, NotificationKind kind, string titleKey, string linkUrl)`.
  Writes rows; emails Client users via `IEmailService` in the same request with a 5-second timeout; logs and
  continues on failure.
- Firm recipients for client events come only from `IAccessService.FirmRecipientsForCase(caseId)` (the case's
  lawyer + its `case_staff`) and `.FirmRecipientsForClient(clientId)` (lawyers + `case_staff` of all the
  client's cases; all Lawyers if the client has no cases). OfficeManager is never included.

## Bills
- `Bill.Recalculate()` sums lines into `Total`. Called in the service before every save of a Draft bill.
  Sent and Paid bills reject line edits.

## UI
- Look and feel: `docs/UI-Design-Notes.md`. Read the sections for the screens in your milestone before
  building them. Do not invent colours, sizes or layouts that are not there.
- Tailwind theme: colours named exactly as the tokens (`brand-navy`, `brand-gold`, `bg-page`, `text-secondary`,
  `success-bg`, ...), Inter font, radius and shadow from the notes. Defined once in `tailwind.config.js`.
- Components are partials in `Pages/Shared/Components/`: `_Button` (Primary, Accent, Secondary, Ghost, Danger),
  `_Badge`, `_Input`, `_Card`. Status → badge tone mapping is one helper, `Helpers/StatusTone.cs`, used by
  both areas so status words and colours match everywhere.
- Layouts: `_StaffLayout` (240px navy sidebar, Settings only when `CanManageSettings`) and `_PortalLayout`
  (fixed bottom nav Home, Cases, Chat, Bills). Navy header inside a case or bill, light header elsewhere.
- One Accent (gold) button per screen. 44px minimum tap targets.
- Razor Pages, Tailwind via the CLI (`npm run css:watch` in the container). HTMX for: chat message list refresh
  (poll every 5 s), document-request status after upload, bill line add/remove. Alpine.js for toggles only.
- All user-facing strings through `IStringLocalizer<SharedResource>`: `Resources/SharedResource.en.resx`
  and `SharedResource.ms.resx`. Key format `Area.Page.Element`, e.g. `Portal.Case.PendingDocuments`.
  Default culture `en`; new users get `Language = en`.
- Dates `dd/MM/yyyy`, money `RM 1,234.50`, via two helpers in `Helpers/Format.cs`.
- Mobile first. Every portal page checked at 390px width.

## Files
- `IStorageService` wraps `AWSSDK.S3`. Works against MinIO (dev, CI) and Cloudflare R2 (preview, prod);
  only env vars differ: `S3_ENDPOINT`, `S3_PUBLIC_ENDPOINT`, `S3_REGION` (`auto` for R2), `S3_BUCKET`,
  `S3_ACCESS_KEY_ID`, `S3_SECRET_ACCESS_KEY`.
- `AmazonS3Config`: `ServiceURL = S3_ENDPOINT`, `ForcePathStyle = true`,
  `RequestChecksumCalculation = WHEN_REQUIRED`, `ResponseChecksumValidation = WHEN_REQUIRED` (newer SDKs add
  checksums by default, which R2 and MinIO reject on presigned requests).
- Two clients: one on `S3_ENDPOINT` for server calls (`HeadObject`, `CopyObject`, `DeleteObject`), one on
  `S3_PUBLIC_ENDPOINT` only for signing URLs the browser uses. In dev the browser cannot reach `storage:9000`,
  so URLs are signed for `http://localhost:9000` (set `Protocol = HTTP` when the endpoint is http).
- Upload: `StartUploadAsync(UploadTarget target, string fileName, string contentType)` where `UploadTarget` is
  `Case(caseId, requestId?)` or `Chat(clientId)`. Checks access, builds the key (`cases/<id>/<uuid>/<name>` or
  `chats/<clientId>/<uuid>/<name>`, file name sanitised), returns a 10-minute presigned PUT URL and an upload
  ticket protected with `IDataProtector` (purpose `ClientHub.Upload`).
- Completion: `CompleteUploadAsync(string ticket)` unprotects the ticket, re-checks access, `HeadObject` for
  real size and type; rejects (and deletes the object) if not pdf/docx/jpg/png or over 20 MB. Then writes the
  `documents` row (case) or the `messages` row (chat), the audit row and notifications.
- Save chat file to case: `CopyObject` to a new case key, then a `documents` row and an audit row.
- Downloads: handler checks access, then redirects to a 5-minute presigned GET with
  `Content-Disposition: attachment; filename="<original name>"`. Inactive documents return 404.
- Browser side: one small vanilla JS module `wwwroot/js/upload.js` (start → PUT with progress → complete),
  used by document requests, case documents, chat and the settings QR upload.

## Tests
- Unit: `BillServiceTests`, `AccessServiceTests` (all four roles), `NotificationServiceTests` (recipient rules
  included), `StorageServiceTests` (key building, ticket tamper rejected, type and size validation).
- Integration: `AccessIntegrationTests` (404 cases, Staff cannot create clients, OfficeManager sees all clients
  but not Settings), `PortalFlowTests` (client sees only own data).
- Test DB: the `db` container with a `clienthub_test` database, reset per test class. Storage: the `storage`
  (MinIO) container with a `clienthub-test` bucket.

## Commands (inside the container)
- `docker compose exec app dotnet watch --project src/ClientHub.Web`
- `docker compose exec app dotnet test`
- `docker compose exec app dotnet ef migrations add <Name> --project src/ClientHub.Web`
- `docker compose exec app dotnet run --project src/ClientHub.Web -- seed`
