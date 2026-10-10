# Build plan: Client Management Hub v1

Inputs: handoffs/requirements.md (R-001..R-158), decisions.md, TSD. Branch `build/v1`, one commit per milestone.
Budget: about 185 min of developer time, inside one 5-hour usage window (tester, reviewer and fixes take the rest).
Open questions Q-01..Q-22 use the defaults in requirements.md: **approved by Fiqri on 2026-10-10**. Where a milestone
depends on one, it is listed under "Assumes".

## Milestones

### M1 Job A: foundation (deployer, not a feature milestone) · 20 min
Goal: an empty but running app in the dev container, with data model, seed, CI and Tailwind tokens.
Requirements: R-028, R-116, R-117, R-118, R-119, R-121, R-122.
Files: `.devcontainer/devcontainer.json`, `docker-compose.yml` (app, db PostgreSQL 16, storage SeaweedFS + bucket-init
`clienthub-files`, `clienthub-test`), `.env.example`, `.gitignore`, `ClientHub.sln`, `src/ClientHub.Web/{Program.cs,ClientHub.Web.csproj}`,
`src/ClientHub.Web/Data/{AppDbContext.cs,Entities/*.cs,Migrations/*,Seed/SeedData.cs}` (all 13 tables, because the seed
needs clients and cases), Identity with roles Lawyer, OfficeManager, Staff, Client,
`src/ClientHub.Web/{tailwind.config.js,package.json,wwwroot/css/input.css}` (tokens and font only),
`Dockerfile.vercel` (two-stage, binds `0.0.0.0:$PORT`, so CI can build it), `.github/workflows/ci.yml`,
`tests/ClientHub.Tests/{ClientHub.Tests.csproj,SmokeTests.cs}`.
Tests: `SmokeTests.Database_MigrationsApplied_SeedUsersExist`.
Done when:
- `docker compose up -d` starts app, db, storage; SeaweedFS answers on `http://localhost:9000/healthz` and bucket `clienthub-files` exists.
- `docker compose exec app dotnet run --project src/ClientHub.Web -- seed` creates 2 Lawyers, 1 OM, 1 Staff, 1 client with a case, firm settings (run twice: no duplicates).
- `docker compose exec app dotnet test` passes (1 test). App starts on :5000 and migrations run at startup.
- `tailwind.config.js` has every token from UI-Design-Notes Tokens with the same names and hex values.
- CI workflow runs restore, build, tests with PostgreSQL and SeaweedFS, and `docker build -f Dockerfile.vercel`.
- `.env.example` lists every variable; `git status` shows no `.env`.

### M2 Shell, sign-in and access rules · 30 min
Goal: login, 2FA, both layouts, shared partials, localisation and the access service every later page uses.
Requirements: R-001..R-013, R-020, R-022..R-027, R-104..R-110 (logins), R-115, R-132..R-136, R-150, R-156..R-158.
Assumes: Q-15, Q-19, Q-20.
Files: `Services/Access/{IAccessService.cs,AccessService.cs}`, `Services/Audit/{IAuditService.cs,AuditService.cs}`,
`Services/Email/{IEmailSender.cs,SmtpEmailSender.cs}`, `Infrastructure/{UserCultureMiddleware.cs,PageModelExtensions.cs}`,
`Pages/Account/{Login,LoginWith2fa,ForgotPassword,SetPassword,Logout}.cshtml(.cs)`, `Pages/Account/Manage/{Index,TwoFactor}`,
`Pages/Shared/{_StaffLayout,_PortalLayout,_AuthLayout}.cshtml`, `Pages/Shared/Components/{_Button,_Badge,_StatusBadge,_Input,
_Card,_EmptyState,_Tabs,_ConfirmDialog,_FileUpload,_Bell}.cshtml`, `Pages/{NotFound,Staff/Index,Portal/Index}.cshtml` (placeholders),
`Resources/SharedResource.{en,ms}.resx`, `Resources/SharedResource.cs`.
Tests: `AccessServiceTests` (Lawyer_AnyCase_True, OfficeManager_AnyCase_True, Staff_Assigned_True, Staff_Unassigned_False,
Client_OwnCase_True, Client_OtherClientCase_False, CanAccessClient_StaffAssignedToOneCase_True, CanAccessClient_StaffNoCase_False,
CasesQuery_StaffNoAssignments_Empty). `AuthTests` via WebApplicationFactory (Client_StaffPage_404, Staff_PortalPage_404,
OfficeManager_Settings_404, InactiveUser_CannotLogin, FiveFailures_LocksOut, LawyerWithout2fa_RedirectedToSetup, Login_WritesAudit).
`ResxTests.EnAndMs_HaveSameKeys`.
Done when:
- Login page shows email, password, Forgot password, EN|BM switch, "account is created by the firm" text, no sign-up link.
- Seeded Client lands on `/Portal` with bottom nav (Home, Cases, Chat, Bills) at 390px; Staff lands on `/Staff` with navy sidebar; Settings link only for Lawyer.
- As Client, `/Staff` returns the 404 page (no "forbidden" text). As Staff, `/Staff/Settings` returns 404.
- Switching to BM on login and in My account changes all labels; `dotnet test` green.

### M3 Clients and cases · 30 min
Goal: firm users manage clients, cases, statuses and updates; clients see their cases and updates.
Requirements: R-012, R-014, R-015, R-019, R-021, R-029..R-044, R-137, R-138, R-139..R-142, R-145, R-148, R-151 (case cards), R-152 (updates), R-155.
Assumes: Q-01, Q-02, Q-03, Q-04 (cases list), Q-09, Q-10, Q-14, Q-16, Q-17, Q-18 (lawyer picker), Q-21.
Files: `Services/Cases/CaseService.cs`, `Data/Entities/Case.cs` (`SetStatus`), `Pages/Staff/Clients/{Index,Create,Details,Edit}`,
`Pages/Staff/Cases/{Index,Create,Details,Edit,Past}`, `Pages/Staff/Index` (dashboard tiles, active cases table, attention feed),
`Pages/Portal/{Index,Cases/Index,Cases/Details,Cases/Past}`, `Pages/Shared/Components/{_CaseProgress,_CaseCard}.cshtml`, resx keys.
Tests: `CaseTests` (Create_StatusNew_OpenedAtSet, SetCompleted_SetsClosedAt, SetCancelled_FromAnyStage, Reopen_ClearsClosedAt,
StatusChange_WritesAudit, PostUpdate_WritesAudit). `ClientTests` (Company_WithoutContactPerson_Invalid, Person_AllFieldsRequired,
Create_SendsSetPasswordEmail, EditEmail_UpdatesLoginEmail, Deactivate_ClientCannotLogin). `CasePageAccessTests` (Staff_PostCreateClient_404,
Staff_PostCreateCase_404, Staff_EditAssignedStaff_404, Staff_UnassignedCase_404, Client_OtherClientCase_404, Dashboard_StaffCountsOnlyAssigned).
Done when:
- As OM: create a Company client (form refuses without person in charge), create a case with Staff assigned; case shows New.
- As Staff: no "+ New client" button; only assigned cases appear in Cases, Dashboard counts and Past cases.
- Status moves forward and back; Completed moves the case to Past cases in staff and portal; reopening returns it.
- As Client: Home shows "Hello, <name>" and case cards; case page shows updates newest first; no edit controls.

### M4 Storage and documents · 30 min
Goal: presigned upload/download through `IStorageService`, document requests, removal; reused by chat and settings.
Requirements: R-045..R-062, R-114, R-143, R-144, R-151 ("We still need from you"), R-152 (requests, documents).
Assumes: Q-11, Q-12, Q-13.
Files: `Services/Storage/{IStorageService.cs,S3StorageService.cs,StorageOptions.cs,UploadTicket.cs,UploadTicketProtector.cs,UploadRules.cs}`,
`Pages/Uploads/{Start,Complete}.cshtml.cs` (one endpoint pair for all targets), `Pages/Files/Download.cshtml.cs`,
`Services/Documents/DocumentService.cs`, `wwwroot/js/upload.js` (type/size check, PUT with progress, complete),
case page sections in `Pages/Staff/Cases/Details` and `Pages/Portal/Cases/Details`, `Pages/Portal/Index` pending card.
Tests: `StorageServiceTests` against SeaweedFS (PresignedPut_ThenHead_ReturnsSizeAndType, PresignedGet_ExpiresIn5Min, Copy_CreatesNewObject).
`UploadTicketTests` (Tampered_Rejected, Expired_Rejected). `DocumentFlowTests` (Start_NoAccess_404, Key_GeneratedByServer,
Complete_WithRequest_SetsReceivedAndLinks, Complete_Over20MB_DeletesObjectNoRow, Complete_BadType_DeletesObjectNoRow,
Complete_AccessRevoked_404, Client_UploadWithoutRequest_Rejected, Reject_WithoutReason_Invalid, Remove_SetsInactiveAuditObjectStays,
Client_Remove_404, Download_Removed_404, Download_RedirectsAndWritesAudit).
Done when:
- As Staff: add a request; as Client: upload a PDF against it from Home; request shows Received in both areas.
- Reject with reason; client sees reason and Re-upload; a 25 MB file or `.exe` is refused in the browser.
- Remove asks for confirmation saying the file is kept; file disappears; its old download URL returns 404; object still in storage.
- Browser network tab shows the PUT going to `localhost:9000`, never through the app.

### M5 Chat · 20 min
Goal: one chat per client with text and file messages, 5-second polling, Save to case.
Requirements: R-063..R-074, R-146, R-153. Assumes: Q-05, Q-21.
Files: `Services/Chat/ChatService.cs`, `Pages/Staff/Chats/{Index,Thread}`, `Pages/Portal/Chat/Index`,
`Pages/Shared/Components/{_MessageBubble,_ChatComposer}.cshtml`, chat target in `Pages/Uploads/*`, `Pages/Staff/Chats/SaveToCase`.
Tests: `ChatTests` (Staff_UnassignedClient_404, Client_OtherClientChat_404, AttachmentKey_UnderChatsClientId, OpenChat_MarksOtherSideRead,
SaveToCase_OtherClientCase_404, SaveToCase_CopiesObjectWritesDocumentAndAudit, Client_SaveToCase_404, ChatFile_Download_WritesAudit).
Done when:
- Client sends text and a PNG; within 5 s it appears in the staff thread with sender name and time, unread pill clears on open.
- Staff not assigned to that client sees no entry and gets 404 on the thread URL.
- "Save to case" puts the file in that case's documents; the message still shows its file.

### M6 Bills and Settings · 25 min
Goal: draft/send/pay bills with frozen lines, client bill page with print; Lawyer settings and staff accounts.
Requirements: R-016..R-018, R-075..R-085, R-111, R-112, R-147, R-149, R-151 (bills tile), R-154. Assumes: Q-02, Q-04 (bills list), Q-06, Q-07, Q-08.
Files: `Data/Entities/{Bill.cs,BillLine.cs}` (`Recalculate`, `MarkSent`, `MarkPaid`), `Services/Bills/{BillService.cs,BillNumberGenerator.cs}`,
`Pages/Staff/Bills/{Index,Edit}`, `Pages/Portal/Bills/{Index,Details}`, `wwwroot/css/print.css`,
`Pages/Staff/Settings/{Index,StaffAccounts}` (firm, bank, QR via `IStorageService`, SMTP fields), `Services/Settings/FirmSettingsService.cs`.
Tests: `BillTests` (Recalculate_SumsLines, Recalculate_NoLines_Zero, Total_TwoDecimals, AddLine_WhenSent_Throws, MarkSent_SetsIssuedAt,
MarkSent_ZeroLines_Rejected, MarkPaid_FromSent_SetsPaidAt, Paid_ToSent_Rejected, BillNo_SequentialPerYearUnique, StatusChange_WritesAudit).
`BillAccessTests` (Client_DraftBill_404, Client_ListHidesDrafts, Staff_UnassignedCaseBill_404). `SettingsTests` (SmtpPassword_StoredEncrypted,
OfficeManager_PostStaffAccount_404, DeactivatedStaff_CannotLogin, NewStaff_GetsSetPasswordEmail).
Done when:
- As Staff: new bill gets `BILL-2026-0001`; add Fee and Expense lines; total updates and cannot be typed.
- After Mark sent, line controls are gone; client sees the bill with bank details, QR and "Print / Save as PDF" (print preview hides nav).
- Draft bill URL opened as Client returns 404. As Lawyer, Settings saves firm and bank details, uploads QR, adds and deactivates a staff account.

### M7 Notifications and email · 20 min
Goal: every trigger calls `Notify`; bell, list, mark read; client emails; SMTP test.
Requirements: R-086..R-103, R-113, R-123, R-134 (complete), R-095 (email text). Assumes: Q-18 (no email to inactive), Q-22.
Files: `Services/Notifications/{INotificationService.cs,NotificationService.cs,NotificationKinds.cs}`, recipient methods in `AccessService`,
calls at every `// NOTIFY:` marker left by M3..M6, `Pages/Notifications/{Index,Open}`, `_Bell` partial, `Pages/Staff/Settings` test-email handler, email resx keys.
Tests: `NotificationTriggerTests` (StatusChange_NotifiesAndEmailsClient, UpdatePosted_…, RequestAdded_…, RequestRejected_…, FirmChat_…,
BillSent_…, BillPaid_…, ClientUpload_NotifiesLawyerAndAssignedStaffOnly, ClientUpload_NotOfficeManager, ClientMessage_DedupsAcrossCases,
ClientMessage_NoCases_AllActiveLawyers, FirmUsers_NeverEmailed, SmtpFails_NotificationSavedActionSucceeds, InactiveClient_NoEmail,
ClientEmail_HasNoCaseDetails_InUserLanguage, Open_MarksRead, MarkAllRead). `SaveToCase_SendsNoNotification`.
Done when:
- Change a case status as Staff: Client bell shows 1; clicking opens the case and the count drops; email appears in the dev console log.
- Client uploads: lawyer in charge and assigned Staff get a notification; OM and unassigned Staff do not.
- With a wrong SMTP host, posting an update still succeeds. "Send test email" on Settings reports success or failure.

### M8 Deploy and backups (deployer) · 10 min
Goal: production container config and the daily backup job; restore steps written.
Requirements: R-109, R-120, R-124..R-131.
Files: `Dockerfile.vercel` (final), `vercel.json` (container service, catch-all rewrite), `.github/workflows/backup.yml`, `.claude/handoffs/deploy.md`.
Tests: none in xUnit; CI must stay green.
Done when:
- `docker build -f Dockerfile.vercel .` then `docker run -e PORT=8080` answers on 8080 with the login page.
- `backup.yml` has cron `0 18 * * *` and `workflow_dispatch`, `pg_dump --format=custom`, `gpg --symmetric --cipher-algo AES256`, upload to `clienthub-backups/db/<date>.dump.gpg`, `aws s3 sync` to `files/`, and the five secrets by name.
- `deploy.md` lists Vercel env vars, R2 lifecycle rule, restore steps. `build/v1` pushed.

## Shared decisions

- Layout: `src/ClientHub.Web/{Data,Services/<Area>,Infrastructure,Pages/{Account,Staff,Portal,Notifications,Uploads,Files,Shared},Resources,wwwroot}`; tests in `tests/ClientHub.Tests/<Area>/<Name>Tests.cs`.
- Naming: entities singular PascalCase; tables and columns snake_case via `UseSnakeCaseNamingConvention()`. Status and type values are string constants in static classes (`CaseStatus.Hearing`), never enums in the DB.
- Access: `IAccessService` (Services/Access) is the only place with role rules: `CanAccessCaseAsync`, `CanAccessClientAsync`,
  `CasesFor(user)`, `ClientsFor(user)`, `FirmRecipientsForUploadAsync(caseId)`, `FirmRecipientsForClientMessageAsync(clientId)`.
  Pages call it first and `return NotFound()` on false or missing rows. Folder policies: `/Staff` = Lawyer|OfficeManager|Staff, `/Staff/Settings` = Lawyer, `/Portal` = Client; failures re-route to the 404 page.
- Role checks for create-client/case/assign-staff use `IAccessService.IsLawyerOrOfficeManager(user)`; never check roles inline in Razor only.
- Notify: `INotificationService.NotifyAsync(IEnumerable<Guid> userIds, string kind, string linkUrl)`; title and email text come from resx keys `Notify.<Kind>.Title` / `Email.<Kind>.{Subject,Body}` in each recipient's language. Called in the page handler after `SaveChangesAsync`, never from entities. Until M7, handlers leave `// NOTIFY: <Kind> R-0xx`.
- Email: `IEmailSender`; when `firm_settings.smtp_host` is empty, the email is written to the console log (dev). Tests use `FakeEmailSender`.
- Audit: `IAuditService.LogAsync(action, entity, entityId, details)`; action names `Case.StatusChanged`, `Document.Uploaded`, `Document.Downloaded`, `Document.Removed`, `Document.SavedFromChat`, `Bill.StatusChanged`, `User.Login`, `User.LoginFailed`, `CaseUpdate.Posted`.
- Bill totals only in `Bill.Recalculate()`; status changes only through `Bill.MarkSent()` / `MarkPaid()`, which throw on invalid moves.
- Storage: `IStorageService` (`PresignPut`, `PresignGet`, `HeadAsync`, `CopyAsync`, `DeleteAsync`); keys built only by `StorageKeys.ForCase/ForChat/ForQr`. Upload targets: `Case(caseId, requestId?)`, `Chat(clientId)`, `SettingsQr`.
- Resx: one `SharedResource` per language. Keys `<Area>.<Screen>.<Element>` (`Staff.Case.PostUpdate`, `Portal.Home.Greeting`), shared `Common.*`, statuses `Status.<Value>`, validation `Validation.<Rule>`. Add en and ms in the same edit.
- UI: shared partials in `Pages/Shared/Components`; pages never restyle a button by hand. One `_Button` Accent per screen.
- Dates `dd/MM/yyyy` via `Format.Date()`, money via `Format.Rm()` (Infrastructure/Format.cs); times shown in Asia/Kuala_Lumpur.
- Seed logins: `lawyer1@`, `lawyer2@`, `om@`, `staff@`, `client@clienthub.test`, password from `SEED_PASSWORD`. Dev `.env` may set `REQUIRE_FIRM_2FA=false` for manual checks only; default and production are true (approved).
- Tests: integration tests use the compose PostgreSQL and SeaweedFS (CI services); each test class creates its own data in a fresh schema.
- Commits: `feat: M<n> <name>` on `build/v1`; tester and reviewer pass first.

## Cut list (drop in this order)

**Items 1-3 are CUT for v1 (Fiqri, 2026-10-10). Developers: do not build R-070..R-072, the R-138 "Needs your attention" feed, or R-113, even where a milestone above lists them.**

1. Save to case from chat (R-070..R-072): staff download and re-upload instead.
2. Dashboard "Needs your attention" feed (R-138 second half); keep tiles and table.
3. Settings "Send test email" (R-113).
4. Staff `/Staff/Cases` and `/Staff/Bills` list pages and portal Bills list (Q-04); reach cases and bills from client and case pages.
5. Chat file attachments (R-067, R-074, R-153 file bubbles); text-only chat.
6. Optional 2FA for Staff and Clients (R-024); keep required 2FA for Lawyer and OM.
7. Settings staff-account management UI (R-016..R-018); create staff through the seed command.
8. Bill print stylesheet polish (R-085); browser default print.
Never cut: access rules and their tests, 404 behaviour, `Bill.Recalculate`, notification triggers, en/ms resx parity, backups.
