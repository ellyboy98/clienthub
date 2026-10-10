---
name: gotchas-aspnet-razor
description: ClientHub build gotchas from M2/M3 - resx manifest naming, partial model quoting, PORT, Secure cookies, EF record projection ordering, validatable localisation, test tokens/factories
metadata:
  type: project
---

- Resx next to `Resources/SharedResource.cs` gets a class-based manifest name and IStringLocalizer silently returns keys. Fixed by `EmbeddedResourceUseDependentUponConvention=false` in the web csproj. If labels render as `Area.Page.Key`, check this first.
- `<partial model="new X(L[&quot;Key&quot;])">` does not compile/decode. Build partial models in an `@{ var x = ...; }` block and pass `model="x"`.
- The app container has `PORT=5000` from `.env`, which overrides `--urls`. To run a second instance: `PORT=5055 dotnet run --no-build --no-launch-profile`.
- Auth cookie is Secure always: curl over http drops it (edit the cookie jar), WebApplicationFactory clients need `BaseAddress = https://localhost`.
- `pkill -f ClientHub.Web` inside `bash -c "..."` kills its own shell (pattern matches the command line). Use the pid from `ps` instead.
- Email links must come from `IAppLinks` (APP_BASE_URL), never `Url.Page(..., Request.Scheme)` — review flagged reset poisoning. Tests capture mail via `AppFactory.Emails` (FakeEmailSender).
- Razor HTML-encodes non-ASCII like the middle dot in titles; assert with a regex, not the literal.
- Lawyer/OM are redirected to 2FA setup by `FirmTwoFactorMiddleware` unless `REQUIRE_FIRM_2FA=false`; integration tests for Lawyer/OM pages must expect that or enrol 2FA.
- (M3) EF cannot OrderBy on members of a positional-record projection (`new Row(a, b)`); use member-init (`new Row { A = a }`) when the query is ordered after projecting.
- (M3) `IValidatableObject` messages bypass the data-annotations localizer; resolve `IStringLocalizer<SharedResource>` from `ValidationContext.GetService` (see ClientInput).
- (M3) Portal pages have no antiforgery form; tests posting as a Client take the token from `/Account/Manage`. Staff: any `/Staff` page (logout form).
- (M3) `AppFactory.WithDb` with an async lambda disposes the scope too early; use `WithDbAsync`. Lawyer/OM page tests use `FirmAppFactory` (2FA off).
- (M3 review) Changing a login email via `UserManager.UpdateAsync` does NOT rotate the security stamp; call `UpdateSecurityStampAsync` so old reset/set-password links die. Reviewer treats this as Critical.

**Why:** each cost a debug cycle in M2/M3.
**How to apply:** check before adding resx, partials, manual curl checks or Lawyer-only integration tests in later milestones.
