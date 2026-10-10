---
name: gotchas-aspnet-razor
description: ClientHub build gotchas found in M2 - resx manifest naming, Razor partial model quoting, container PORT, Secure cookies with curl, pkill self-kill
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

**Why:** each cost a debug cycle in M2.
**How to apply:** check before adding resx, partials, manual curl checks or Lawyer-only integration tests in later milestones.
