---
name: gotchas-aspnet-razor
description: ClientHub build gotchas from M2-M5 - resx manifest naming, partial model quoting, PORT, Secure cookies, EF record projection ordering, validatable localisation, test tokens/factories
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

- (M4) Tests reach SeaweedFS at `S3_ENDPOINT` (storage:9000 in the container); `AppFactory` sets `S3_PUBLIC_ENDPOINT` = that endpoint and bucket `clienthub-test`, so test code can PUT to presigned URLs. Helpers in `Support/TestStorage.cs` (raw S3 client to simulate a bad content type, since the signed PUT refuses other types).
- (M4) AWSSDK.S3 v4: `GetObjectMetadataResponse.ContentLength` is a plain long; set `UseHttp`/`Protocol.HTTP` for http endpoints. Upload/download pages are body-less Razor pages (`@page` only) returning JsonResult/Redirect; add `OnGet => NotFound()`.
- (M4) Portal pages without an open request render no antiforgery token; take it from `/Account/Manage` in client tests.
- (M4 review) Reviewer treats a reusable presigned PUT as Critical (overwrite after validation). Pattern: PUT to `staging/` key, validate, CopyObject to a server-only key, re-validate the copy, delete staging; catch Npgsql UniqueViolation for concurrent completes. Never log file names (may contain IC numbers).

- (M5) HTMX polling pattern that renders only new rows: a sentinel `<div id="chat-poll" hx-get="...&after=<ticks>" hx-trigger="every 5s, chat:refresh from:body" hx-swap="outerHTML">`; the handler returns new rows + a fresh sentinel. Send handlers set `HX-Trigger: chat:refresh` for an instant refresh. Handler partials: `PartialViewResult` with the absolute view path `/Pages/Shared/Components/_X.cshtml`.
- (M5) Tailwind v3 emits `.hidden` after `.flex`, so `class="hidden flex"` + JS removing `hidden` works. Test hooks on counts need `data-tile-count>N<` (TestData.Tile regex).

- (M5 review) Reviewer treats a timestamp poll cursor as Critical (late commits skipped AND marked read). Pattern: identity `seq` cursor + `pg_advisory_xact_lock(<id as bigint>)` in the insert transaction (identity values are assigned at insert, not commit), and mark read only `seq <=` what the viewer was shown; background-tab polls must not mark read (`hx-vals='js:{visible:...}'`). Migrations generated in the container are root-owned: edit/chown them via `docker compose exec app`. Drop EF's `defaultValue: 0L` on an added identity column.
**Why:** each cost a debug cycle in M2/M3.
**How to apply:** check before adding resx, partials, manual curl checks or Lawyer-only integration tests in later milestones.

- (M6) EF: entities get `Id = Guid.NewGuid()` client side, so a child added only through a tracked parent's collection is treated as existing (UPDATE, 0 rows). Always `_db.X.Add(child)` explicitly. Removing a required child with DeleteBehavior.Restrict: `_db.Remove(child)` explicitly too.
- (M6) FormTagHelper adds no antiforgery token when the form has a literal `action="..."`; add `asp-antiforgery="true"` (HTMX forms with hx-post need it).
- (M6) Filtering after a positional-record projection fails at runtime (not compile time): pass filters into the query method before `select new Row(...)`.
- (M6) Renaming `/Staff/Settings` to a folder means `AuthorizePage` no longer matches `/Staff/Settings/Index`; use `AuthorizeFolder`. Avoid `git rm --cached` (touches the index; coordinator commits) - just `mv`.
