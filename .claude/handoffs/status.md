# Build status

Nothing built yet. Branch: `build/v1`. Specs revised on 2026-10-10 (see decisions.md).
Start with Prompt 1 in KICKOFF.md once the 5-hour usage window has reset.

## Milestones
(planner fills this from plan.md; developer updates after each run)

| # | Milestone | Time box | State |
| --- | --- | --- | --- |
| M1 | Job A: foundation (deployer) | 20 min | built and verified (dev storage is SeaweedFS), not committed |
| M2 | Shell, sign-in and access rules | 30 min | built, review fixes applied, 39/39 tests green, not committed |
| M3 | Clients and cases | 30 min | not started |
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
