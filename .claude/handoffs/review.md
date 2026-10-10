# Review: M3 Clients and cases (uncommitted, build/v1)

Scope: `git diff` plus untracked M3 files. What passed: every Staff and Portal page handler (GET and POST) loads through
`CasesFor`/`ClientsFor` and returns 404 on a miss. Create client, create case, assign staff and deactivate/reactivate are
gated by `IsLawyerOrOfficeManager` on POST as well as GET. Lawyer/staff/client ids from form fields are checked as an
active Lawyer, active Staff or an accessible client. Staff cannot change lawyer/staff through Edit (Q-10). `closed_at`
logic and audit rows have tests. en/ms resx keys match and every key used exists. No hard-coded UI text, no IC numbers
logged, one Primary action per screen, gold only where UI-Design-Notes asks for it.

## Critical (must fix before commit)

1. `src/ClientHub.Web/Services/Clients/ClientService.cs:143-149` (`UpdateAsync`): the login email/user name changes
   through `UserManager.UpdateAsync`, which does not rotate the security stamp. A set-password or reset link already
   sent to the old (for example mistyped) address stays valid for its full day, and the corrected address never gets a
   link (R-014). Fix: change the email with `SetEmailAsync`/`SetUserNameAsync` (or call `UpdateSecurityStampAsync`
   afterwards), and if the user has no password yet, send a new account-created link to the new address. Add a test
   that the old token fails after an email change.

## Should fix (next milestone)

1. `src/ClientHub.Web/Services/Cases/CaseService.cs:92-94` and `:100-103`: the status change (and update post) and its
   audit row are saved by two separate `SaveChangesAsync` calls, so a failure in between leaves a change with no audit
   row (R-044). Fix: add the audit entry to the context before one `SaveChangesAsync`, or wrap both in a transaction.
2. `src/ClientHub.Web/Services/Clients/ClientService.cs:185-186` with `Pages/Staff/Clients/Create.cshtml.cs:32`: if the
   account-created email fails, the error is only logged and the OM still sees "Client created". Fix: return an
   email-failed flag and show a warning notice telling them to use Forgot password for the client.
3. `src/ClientHub.Web/Pages/Staff/Clients/Edit.cshtml.cs:44-52` (Q-14 as approved): any Staff user who can open a
   client can change that client's login email, then use Forgot password to take over the client's account. Fix: ask
   Fiqri whether login-email changes should be Lawyer/OM only (spec change), or at least write an audit row for them.
4. `.claude/handoffs/decisions.md` (M3 status entry): "Completed and Cancelled keep the first `closed_at`" is a
   reading of R-039 that the spec does not state. Fix: confirm with Fiqri, or reset `closed_at` on every move into a
   closed status (`Domain.cs:44`).

## Nice to have

1. `src/ClientHub.Web/Pages/Staff/Cases/Details.cshtml.cs:96`: an invalid staff choice returns a bare 400. Fix: show
   the page again with `Validation.InvalidChoice`, as Edit does.
2. `src/ClientHub.Web/Pages/Staff/Cases/Edit.cshtml.cs:68`: `Request.Form.ContainsKey("Form.StaffIds")` misses indexed
   keys (`Form.StaffIds[0]`). This is harmless because lines 72-73 ignore those fields for Staff. Fix: check
   `Form.StaffIds.Count > 0`, so the 404 rule cannot be bypassed this way.
3. `src/ClientHub.Web/Services/Clients/ClientService.cs:179-183`: `CurrentUICulture` is not restored in a `finally`.
   Fix: wrap the swap in try/finally.
4. `src/ClientHub.Web/Services/Cases/CaseService.cs:95,104`: the `// NOTIFY:` markers say `R-1xx`. Fix: write the exact
   requirement ids so M7 can grep for them.
5. `src/ClientHub.Web/Pages/Staff/Clients/Index.cshtml.cs:47`: a client with no login (`UserId` null) is shown as
   Active. Fix: show a neutral "No login" state, or treat it as Inactive.
6. `src/ClientHub.Web/Services/Cases/CaseService.cs:39`: the client check only tests that the client exists, not that
   the user can access it. This is correct today because only Lawyer/OM call it. Fix: take the access-scoped query, so
   the check stays correct if the role rules change.
