# Review: M2 Shell, sign-in and access rules

Scope: uncommitted diff on build/v1 plus untracked files (node_modules, bin, obj, site.css skipped), checked against
plan.md M2 and requirements.md. Access rules (R-001..R-013) in `AccessService` are correct and fully tested; folder
policies, 404 on policy failure, Lawyer-only Settings, firm 2FA gate, lockout, inactive-login block, Data Protection
keys in PostgreSQL, SMTP password protector and en/ms resx parity (105 keys, no missing keys used in code) are all in place.

## Critical (must fix before commit)

1. `src/ClientHub.Web/Pages/Account/ForgotPassword.cshtml.cs:42`: the reset link is built from the request `Host`
   (`Url.Page(..., Request.Scheme)`). The app has no `AllowedHosts` (no appsettings.json) and `APP_BASE_URL`
   (in `.env.example`) is never read, so a forged `Host` header produces a reset email that points to an attacker's
   domain and leaks the token (password-reset poisoning). M3's account-creation email will reuse this. Fix: build
   set-password links from `APP_BASE_URL` in one helper (for example `Infrastructure/AppLinks.cs`) and also set `AllowedHosts`.

## Should fix (next milestone)

1. `src/ClientHub.Web/Services/Access/AppSignInManager.cs:16`: `is_active = false` blocks new logins only; an existing
   14-day cookie keeps working because `ValidateSecurityStampAsync` never checks `IsActive`. Fix: override
   `ValidateSecurityStampAsync(ClaimsPrincipal)` to return null for inactive users, and update the security stamp when
   a user is deactivated (M3/M6).
2. `src/ClientHub.Web/Services/Email/SmtpEmailSender.cs:31`: when `smtp_host` is empty the full email body, including
   set-password and reset tokens, goes to the log in every environment (production too, until Settings is filled in).
   Fix: log the body only in Development; elsewhere log a warning with recipient id and subject only.
3. `src/ClientHub.Web/Pages/Shared/_Head.cshtml:5-6`: htmx and Alpine load from unpkg with no `integrity`
   attribute on a portal that shows IC numbers and case files. Fix: vendor both into `wwwroot/js` (or add SRI hashes and `crossorigin`).
4. `src/ClientHub.Web/Pages/Shared/_Head.cshtml:3`: pages set `ViewData["Title"] = L[...]` (a `LocalizedString`),
   so `ViewData["Title"] is string t` never matches and no page has its own title. Fix: test `ViewData["Title"]?.ToString()`
   or assign `.Value` in pages.
5. `src/ClientHub.Web/Program.cs:467` with `Pages/NotFound.cshtml.cs:12`: every empty 4xx/5xx response is re-executed and
   rewritten to 404, so an expired antiforgery token (400) or 405 shows "page not found". R-008 needs this only for
   access failures and missing rows. Fix: re-execute only for 404 (or keep the original status for non-404 codes).
6. `src/ClientHub.Web/Data/DataServiceExtensions.cs:26-28`: Data Protection keys are stored unencrypted in the same
   database (and backup) as `firm_settings.smtp_password`, so anyone with a DB dump can decrypt the SMTP password and mint
   auth cookies. Fix: protect the key ring with a secret held outside the DB (for example `ProtectKeysWithCertificate` from
   an env var), or have Fiqri accept the risk in decisions.md.
7. `src/ClientHub.Web/Services/Email/SmtpEmailSender.cs:35`: uses `System.Net.Mail` while TSD (Stack table) specifies
   MailKit, and port 465 (implicit TLS) does not work. This changes the TSD, not only the implementation (see decisions.md entry 7).
   Fix: get Fiqri's approval recorded as his decision, or switch to MailKit.
8. `src/ClientHub.Web/Pages/Shared/_StaffLayout.cshtml:31`: R-136 asks for an unread count on Chats; M2 claims R-136 but
   the link has no count. Fix: add the count in M5 and note it as open in status.md.

## Nice to have

1. `src/ClientHub.Web/Pages/Account/Culture.cshtml.cs:20`: a GET request changes `users.language` (a cross-site link can
   change it). Fix: keep the cookie on GET and save `users.language` only from the My account POST.
2. `src/ClientHub.Web/Pages/Account/Manage/TwoFactor.cshtml.cs:75`: Staff/Client can turn 2FA off without entering their
   password or a code again. Fix: require the current authenticator code on Disable.
3. `src/ClientHub.Web/Pages/Account/Login.cshtml.cs:56-62`: unknown emails return without a password hash, so response
   time shows whether an account exists. Fix: run a dummy `PasswordHasher.VerifyHashedPassword` on the unknown-email path.
4. `src/ClientHub.Web/Pages/Account/Login.cshtml.cs:59`: the raw typed email of unknown accounts is kept in `audit_log`
   (personal data of non-users). Fix: store a masked form (for example `ab***@domain`) unless PDPA logging needs the full value.
5. `src/ClientHub.Web/Infrastructure/WebServiceExtensions.cs:84-85`: forwarded headers are trusted from any source, so
   `X-Forwarded-For` can be spoofed if IPs are ever logged. Fix: leave as is for Vercel, but do not use `RemoteIpAddress` in audit rows.
