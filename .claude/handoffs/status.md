# Build status

Nothing built yet. Branch: `build/v1`. Specs revised on 2026-10-10 (see decisions.md).
Start with Prompt 1 in KICKOFF.md once the 5-hour usage window has reset.

## Milestones
(planner fills this from plan.md; developer updates after each run)

| # | Milestone | Time box | State |
| --- | --- | --- | --- |
| M1 | Job A: foundation (deployer) | 20 min | built and verified (dev storage is SeaweedFS), not committed |
| M2 | Shell, sign-in and access rules | 30 min | built, review fixes applied, 39/39 tests green, not committed |
| M3 | Clients and cases | 30 min | built, review fixes applied, 75/75 tests green, not committed |
| M4 | Storage and documents | 30 min | not started |
| M5 | Chat | 20 min | not started |
| M6 | Bills and Settings | 25 min | not started |
| M7 | Notifications and email | 20 min | not started |
| M8 | Deploy and backups (deployer) | 10 min | not started |

Open questions Q-01..Q-22: defaults approved (decisions.md).

## Decisions
See decisions.md.

## M2 Shell, sign-in and access rules (developer, 2026-10-10)

Built:
- Services: `Services/Access/{IAccessService,AccessService,AppClaimsPrincipalFactory,AppSignInManager}.cs`,
  `Services/Audit/{IAuditService,AuditService}.cs`, `Services/Email/{IEmailSender,SmtpEmailSender}.cs`.
- Infrastructure: `WebServiceExtensions.cs` (auth, cookies, policies, localisation, forwarded headers, DI),
  `UserCultureProvider.cs` (plan said UserCultureMiddleware; it is a RequestCultureProvider), `Cultures.cs`, `Format.cs`,
  `StatusTone.cs`, `PageModelExtensions.cs`, `FirmTwoFactorMiddleware.cs`, `IdentityErrors.cs`. `Program.cs` rewired.
- Data: `AppDbContext` is now `IdentityUserContext<AppUser, Guid>, IDataProtectionKeyContext`; `AppRole` removed; seed no longer
  writes Identity roles; Data Protection keys persisted to `data_protection_keys` with application name `ClientHub`
  (`DataServiceExtensions.cs`). Migration `Data/Migrations/*_M2_DataProtectionKeysSingleRole.cs` (drops roles, user_roles, role_claims).
- Role storage (review item): single source = `users.role`; Identity role tables dropped (decisions.md).
- Pages: `Index` (redirect by role), `NotFound`, `Account/{Login,LoginWith2fa,ForgotPassword,SetPassword,Logout,Culture}`,
  `Account/Manage/{Index,TwoFactor}` (QRCoder SVG), placeholders `Staff/Index`, `Staff/Settings` (Lawyer only), `Portal/Index`.
- Layouts: `Shared/{_Head,_Logo,_LanguageSwitch,_AuthLayout,_StaffLayout,_PortalLayout}.cshtml`, `_ViewImports`, per-folder `_ViewStart`.
- Components: `Shared/Components/{_Button,_Badge,_StatusBadge,_Input,_Card,_EmptyState,_Tabs,_ConfirmDialog,_FileUpload,_Bell}.cshtml`
  + `ComponentModels.cs`; component CSS in `wwwroot/css/input.css`; tailwind content globs include `Pages/**/*.cs`, `Infrastructure/**/*.cs`.
- Resx: `Resources/SharedResource.{en,ms}.resx` (105 keys incl. all `Status.*`, `Role.*`), `Resources/SharedResource.cs`
  (root namespace). csproj sets `EmbeddedResourceUseDependentUponConvention=false` (otherwise the localizer finds nothing).
- Packages: Microsoft.AspNetCore.DataProtection.EntityFrameworkCore 10.0.12, QRCoder 1.8.0; tests: Microsoft.AspNetCore.Mvc.Testing 10.0.12.
- `.env.example`: removed `SMTP_ENCRYPTION_KEY`, documented `REQUIRE_FIRM_2FA`.
- Tests: `tests/ClientHub.Tests/Access/AccessServiceTests.cs` (15), `Account/AuthTests.cs` (17), `Localization/ResxTests.cs` (2),
  `Support/{TestDatabase,AppFactory}.cs`; `SmokeTests` no longer counts roles. Full suite 35/35.

Skipped / left for later milestones:
- `_Bell` shows the unread count only; `/Notifications` list and mark-read are M7 (link 404s until then). Sidebar links to
  Clients/Cases/Chats/Bills/Past cases 404 until M3-M6. Chats unread pill: M5.
- `_FileUpload` is markup only; `wwwroot/js/upload.js` is M4. `FakeEmailSender` arrives with the first email test (M7).
- Account-creation email (R-133 sender side) is M3/M6; `SetPassword` already accepts its link (password-reset token).
- Firm recipient methods on `IAccessService` are M7 per plan.

Review fixes (M2 review, 2026-10-10):
- Reset-link poisoning: `Infrastructure/AppLinks.cs` (`IAppLinks`) builds links from `APP_BASE_URL`; Production fails at startup
  without it; `AllowedHosts` defaults to the APP_BASE_URL host in Production. M3 account-creation email must use `IAppLinks.SetPassword`.
- SMTP switched to MailKit (465 implicit TLS, 587 STARTTLS). Console fallback logs bodies only in Development.
- Deactivation: `Services/Access/UserAccountExtensions.cs` `DeactivateAsync` (flag + stamp rotation); `AppSignInManager.ValidateSecurityStampAsync`
  rejects inactive users; validation interval 60 s (`SECURITY_STAMP_INTERVAL_SECONDS`).
- Status-code pages: only 404 (and 403 rewritten to 404) re-execute `/NotFound`; 400/405/500 keep their status (`Program.cs`).
- Page titles render (`_Head` uses `ViewData["Title"]?.ToString()`). htmx 2.0.4 and Alpine 3.14.8 have SRI `integrity` + `crossorigin`.
- New tests in `AuthTests`: ForgotPassword_ForgedHost_LinkUsesAppBaseUrl, DeactivatedUser_LosesSession, BadAntiforgery_Stays400_Not404,
  PageTitle_IsLocalised. `tests/ClientHub.Tests/Support/FakeEmailSender.cs` added. Suite 39/39.
- Still open from the review (left on purpose): Data Protection key ring not encrypted at rest (Should fix 6, needs Fiqri's call),
  Chats unread count (M5), and the Nice-to-have items.

Tester notes:
- Seeded Lawyer and OM are sent to `/Account/Manage/TwoFactor` until 2FA is on (REQUIRE_FIRM_2FA=true). For manual checks either
  enrol an authenticator app or set `REQUIRE_FIRM_2FA=false` in `.env`.
- Auth cookie is Secure: use a browser on http://localhost:5000 (Chrome/Firefox accept it); curl needs the cookie-jar Secure flag cleared.
- Rebuild CSS after pulling (`npm run css:build` in `src/ClientHub.Web`); `site.css` is git-ignored.
- Check at 390px: login, `/Portal` (bottom nav), `/Account/Manage`; staff sidebar collapses to a navy top bar with menu toggle below 1024px.
- Something was bound to port 5000 inside the app container at the start of M2 (an earlier `dotnet run`/watch); it is no longer running.

## M3 Clients and cases (developer, 2026-10-10)

Built:
- Domain: `CaseStatus.{Steps,All,Closed,IsValid,IsClosed}` (`Data/Entities/Base.cs`), `Case.SetStatus(status, now)` + `IsClosed` (`Data/Entities/Domain.cs`). No migration (no schema change).
- Services: `Services/Cases/CaseService.cs` (`ICaseService`: create, details, staff, status + audit, post update + audit, lawyer/staff pickers; `CaseRow`, `CaseQueries.Active/Past/ToRows`),
  `Services/Clients/ClientService.cs` (`ClientInput` validation incl. Company person-in-charge; `IClientService`: create with Client login + set-password email via `IAppLinks.SetPassword`,
  update incl. login email, deactivate via `DeactivateAsync` / reactivate). Registered in `WebServiceExtensions.cs`. `// NOTIFY:` markers left for M7 in status change and post update.
- Staff pages: `Staff/Index` (4 tiles + active cases table; no attention feed, cut), `Staff/Clients/{Index,Create,Details,Edit,_ClientForm}`, `Staff/Cases/{Index,Create,Details,Edit,_CaseTable,_CaseFields,CaseForm.cs}`,
  `Staff/PastCases`.
- Portal pages: `Portal/Index` (greeting, "We still need from you", case cards, Messages/Bills tiles), `Portal/_PortalHeader`, `Portal/Cases/{Index,Details,Past}`.
- Components: `Shared/Components/{_CaseCard,_CaseProgress}.cshtml` + models in `ComponentModels.cs`; `StatusTone.Inactive`.
- Resx: 114 new keys, en + ms.
- Tests: `Cases/CaseTests.cs` (12), `Clients/ClientTests.cs` (9), `Cases/CasePageAccessTests.cs` (12); `Support/TestData.cs`; `Support/AppFactory.cs` gains `FirmAppFactory`,
  `WithDbAsync`, `WithServiceAsync`. Full suite 72/72.

Skipped / left for later milestones:
- Case page: document requests, documents (M4) and bills / "+ New bill" (M6). Portal case Documents and Bills tabs show "coming soon".
- Portal Home "Upload" buttons link to the case page; upload is M4. Chat links go to `/Staff/Chats/{clientId}` (M5). `/Staff/Bills`, `/Portal/Bills`, `/Portal/Chat` still 404.
- Dashboard "Needs your attention" feed: cut (decisions.md).

Tester notes:
- Seed has one client/case; create more as OM (`om@`, needs 2FA or `REQUIRE_FIRM_2FA=false`). Run `npm run css:build` first (new classes).
- Check at 390px: `/Portal`, `/Portal/Cases`, `/Portal/Cases/Details/{id}`, `/Portal/Cases/Past`. Staff tables scroll horizontally on narrow screens.
- Client form: the person-in-charge fields appear only when Company is chosen (Alpine); the server also refuses Company without them.
- Deactivate on the Client page asks for confirmation; the client is signed out at the next request (stamp interval) and appears under the Inactive chip.

Review fixes (M3 review, 2026-10-10):
- Critical 1: email change rotates the security stamp (`ClientService.UpdateAsync`); a login without a password gets a fresh set-password link at the new address.
- Fiqri: only Lawyer/OM change a client's email. Staff see the email read only (`_Input` gained `ReadOnly`); a Staff POST with a changed email returns 404 (`Clients/Edit`), and the service refuses it too (`canChangeEmail`).
- Should fix 2: a failed account-created email still saves the client and shows a warning on the Client page (`Staff.Clients.EmailNotSent`, en + ms). `FakeEmailSender.Fail` simulates SMTP failure.
- Should fix 4: Completed↔Cancelled keeping the first `closed_at` is confirmed by Fiqri (decisions.md).
- New tests in `ClientTests`: EmailChange_OldSetPasswordLinkFails_NewLinkSent, Staff_ChangeEmail_404_OtherFieldsSave, Create_EmailFails_ClientCreated_WarningShown; EditEmail_UpdatesLoginEmail now runs as OM.
- Still open from the review (left on purpose): Should fix 1 (status/update and audit row in one save), Nice-to-have 1-6.

