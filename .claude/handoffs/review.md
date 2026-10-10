# Review: M6 Bills and Settings (uncommitted, build/v1)

Scope: `git diff` plus untracked files for M6, checked against plan.md M6 and requirements.md (R-113 cut).
Checked and clean: CanAccessCase/404 on every bill handler and list (IBillService.FindAsync/ListFor via CasesFor; portal Draft = 404;
line ids resolved only inside the access-checked bill); frozen lines after Send (all mutations go through MutateAsync with
`SELECT ... FOR UPDATE` and Draft checks; no other write path to bills/bill_lines); bill number (per-year advisory xact lock +
unique index); Settings Lawyer-only (folder policy + CanManageSettings on every handler, SettingsQr upload start and completion);
SMTP password protected with Data Protection and never rendered back (`_Input` drops password values); staff set-password link via
IAppLinks; QR upload keeps staging -> copy -> re-check, JPG/PNG only (no SVG); no Html.Raw on bill or print pages (descriptions,
note, firm and bank fields are Razor-encoded); en/ms resx parity (all used keys present in both).

## Critical (must fix before commit)

None.

## Should fix (next milestone)

1. src/ClientHub.Web/Services/Bills/BillService.cs:92 and :104 (also Data/Entities/Bill.cs:57 and :74), problem: amounts are summed outside `Bill.Recalculate()` for the "too large" pre-check, against R-078 and the plan rule. Fix: drop the service pre-checks, let `Bill.AddLine/UpdateLine` raise BillRuleException, map it to `BillError.TooLarge`, and compute the candidate total through one Bill method that Recalculate also uses.
2. src/ClientHub.Web/Services/Bills/BillService.cs:115, problem: removing a Draft line hard-deletes the `bill_lines` row. R-077 says "delete lines" but R-114 says no user action deletes a record, and decisions.md now settles this without a ruling. Fix: get Fiqri to choose between keeping the hard delete (and recording that R-077 overrides R-114 for draft lines) and adding `is_active` to bill_lines.
3. src/ClientHub.Web/Services/Bills/BillService.cs:41-46, problem: the mutation methods take a bill id but no user, so access depends on every caller running FindAsync first. Fix: pass the ClaimsPrincipal and load the bill through `Visible(user)` inside MutateAsync.
4. src/ClientHub.Web/Pages/Staff/Settings/Index.cshtml:116 and src/ClientHub.Web/Pages/Staff/Bills/_BillLines.cshtml:40, problem: Deactivate and Remove line act on one tap with no confirmation (status.md lists this as skipped). Fix: wrap both in the shared `_ConfirmDialog` partial.

## Nice to have

1. src/ClientHub.Web/Services/Storage/UploadService.cs:151, problem: the QR type check only reads the declared Content-Type, not the file bytes. The low risk is that the image is served as image/jpeg or image/png. Fix: check the JPG/PNG magic bytes with a ranged GET before recording the QR.
2. src/ClientHub.Web/Services/Settings/FirmSettingsService.cs:92-99, problem: GetAsync inserts a firm_settings row on a GET, and two first-time requests at once can create two rows. Fix: seed the single row in migrations or seeding, and make GetAsync read only.
3. src/ClientHub.Web/Pages/Staff/Cases/Details.cshtml.cs:148, problem: a double-click on "+ New bill" creates two Drafts, which can never be deleted (Q-07). Fix: disable the button on submit (`hx-disabled-elt` or a small JS guard).
4. src/ClientHub.Web/Data/Entities/Bill.cs:23-29, problem: `Total`, `Status`, `IssuedAt`, `PaidAt` and `Lines` have public setters, so any code could bypass Recalculate, MarkSent and MarkPaid. Fix: make the setters private or internal (EF can map backing fields).
5. src/ClientHub.Web/Services/Settings/FirmSettingsService.cs:23, problem: a non-numeric SMTP port gives the default English model-binding message. Fix: set localized `ModelBindingMessageProvider` accessors from resx.
