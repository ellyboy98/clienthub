# Build status

Branch `build/v1`. All milestones built, reviewed and tested locally: 180/180 tests green. All milestones committed on `build/v1`. Nothing deployed.

## Milestones

| # | Milestone | Commit | State |
| --- | --- | --- | --- |
| M1 | Job A: foundation (deployer) | b2baa13 | done; dev storage is SeaweedFS |
| M2 | Shell, sign-in and access rules | 278895a | done |
| M3 | Clients and cases | 459c029 | done |
| M4 | Storage and documents | 065166e | done |
| M5 | Chat (with file messages) | 18ff3f0 | done |
| M6 | Bills and Settings | 3e1da36 | done |
| M7 | Notifications and email | 2e2c5e2 | done |
| M8 | Deploy and backups (deployer) | 312a0f2 | built; image and workflow verified locally only |

Open questions Q-01..Q-22: defaults approved (decisions.md).

## Cut items (plan.md cut list)

- Cut for v1 (Fiqri): 1 Save to case from chat (R-070..R-072), 2 dashboard "Needs your attention" feed (R-138 second half), 3 Settings "Send test email" (R-113).
- Item 4 (Staff/Cases, Staff/Bills, portal Bills lists) was built. Chat file attachments (item 5) were built in M5. No cut was recorded for items 6-8.

## Known open items

- Not browser-checked: the 390px layouts of every portal page (each milestone section lists its "Check at 390px" pages). No manual browser pass was done.
- Unresolved review items, left on purpose (details in the milestone sections below):
  - M2: Data Protection key ring is not encrypted at rest (Should fix 6, needs Fiqri's call); Nice to have items.
  - M3: status/update and audit row not saved in one transaction (Should fix 1); Nice to have 1-6.
  - M4: Content-Length binding on presigned PUT (Should fix 2; staging plus the 1-day lifecycle rule covers orphans); magic-byte check (NtH 1); stale reject shows 400 (NtH 3).
  - M7: SMTP failure log includes the full MailKit exception (NtH 1); no background email queue (NtH 2); missing tests for non-local link and unassigned Staff on client message (NtH 3); plan.md line 145 is stale (NtH 4).
- Tester watch: M3 `CaseTests.Create_StatusNew_OpenedAtSet` failed once under load (1-second window); passed in every other run.
- Not verified without cloud accounts: backup workflow against Neon and R2, Vercel container runtime, restore drill, `PGMAJOR` (17) matching Neon.

## Steps for Fiqri by hand

Full detail in `.claude/handoffs/deploy.md`.
1. Push `build/v1`, open one PR into `main`, merge when CI is green.
2. Cloudflare R2: three private buckets, CORS on the two files buckets, lifecycle rules (`staging/` 1 day on files buckets, `db/` 30 days on backups), app and backup API tokens.
3. Neon via Vercel Marketplace; confirm the PostgreSQL major version matches `PGMAJOR` in `backup.yml`.
4. Vercel: import repo, set env vars (separate Production and Preview values; `PORT=8080`; `APP_BASE_URL` mandatory in Production; preview needs its own `APP_BASE_URL` or `AllowedHosts`), first deploy, run seed once.
5. Sign in, enter SMTP on Settings, set up 2FA, change seeded passwords.
6. GitHub: five backup secrets, branch protection on `main`, run the Backup workflow once by hand, then do one restore test.
7. Decisions only you can make: Data Protection key encryption at rest; Vercel Pro once in daily use.

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


## M4 Storage and documents (developer, 2026-10-10)

Built:
- Storage: `Services/Storage/{StorageOptions,IStorageService,S3StorageService,UploadRules,UploadTicket,UploadTicketProtector,UploadService}.cs`.
  `S3StorageService`: two AWSSDK.S3 (4.0.104.2) clients (server on S3_ENDPOINT, signer on S3_PUBLIC_ENDPOINT), path style, checksums WHEN_REQUIRED,
  content type signed into the PUT. `UploadRules` (type from extension, 20 MB, file-name sanitising) + `StorageKeys` (only key builder).
  `UploadTicketProtector`: Data Protection purpose `ClientHub.Upload`, time limited (15 min; PUT URL 10 min). `UploadService` (`IUploadService`):
  start/complete for every target; only `Case` accepted now (Chat = M5, SettingsQr = M6 add their branches in `CanUseTargetAsync`/`StartAsync`/`CompleteAsync`).
- Documents: `Services/Documents/DocumentService.cs` (requests list/add/reject, documents list, remove, download URL + audit, `RecordUploadAsync`).
  `// NOTIFY:` markers for R-088, R-089, R-090.
- Data: global query filter `documents.is_active`; unique index on `documents.storage_key` (migration `*_M4_DocumentStorageKeyIndex`).
- Pages: `Uploads/{Start,Complete}` (JSON, antiforgery, 404 on access failure, 400 + localised `error` otherwise), `Uploads/UploadErrors.cs`,
  `Files/Download/{id}` (302 to presigned GET, `Cache-Control: no-store`). Folders `/Uploads` and `/Files` require sign-in.
  Staff case page: Document requests card (add, Received → View/Reject with reason), Documents card (Upload, Download, Remove with confirm dialog).
  Portal case page: open requests card at top (reason, Upload/Re-upload), Documents tab with Download. Portal Home: gold Upload per pending request now uploads in place.
- UI: `_FileUpload` partial rewritten (`FileUploadModel.ForCase(...)`), `wwwroot/js/upload.js` (type/size check, start, XHR PUT with progress bar, complete, reload),
  loaded from `_Head`. `Format.FileSize`. `IAccessService.IsFirm`. 33 new resx keys (en + ms).
- Tests: `Storage/StorageServiceTests.cs` (5, against SeaweedFS), `Storage/UploadTicketTests.cs` (17 incl. theories), `Documents/DocumentFlowTests.cs` (17);
  `Support/TestStorage.cs`; `AppFactory` points S3 at the `clienthub-test` bucket with the public endpoint = internal endpoint. Full suite 114/114.

Skipped / left:
- Staff case page "tabs on phone" (R-143): sections stack on narrow screens instead. Bills section / "+ New bill": M6.
- After an upload the page reloads (no HTMX swap). Chat and Settings QR targets: M5/M6. Save to case: cut.

Tester notes:
- Run `npm run css:build` (new classes). Browser checks need the dev app on http://localhost:5000 (CORS allows only that origin).
- PUT goes to `http://localhost:9000/clienthub-files/cases/<caseId>/<uuid>/<name>`; check in the network tab. A 25 MB file or `.exe` is refused in the browser before any request.
- Remove: confirm dialog says the file is kept; after removal the old `/Files/Download/{id}` returns 404 and the object is still in the bucket.
- Check at 390px: `/Portal` (upload widget under each needed item), `/Portal/Cases/Details/{id}` and `?tab=documents`.

Review/test fixes (M4, 2026-10-10):
- Critical 1: staging keys. The browser PUTs to `staging/<uuid>/<uuid>` (no file name); Complete heads and validates it, `CopyObject`s to the final
  `cases/<id>/<uuid>/<name>` key (known only to the server, inside the encrypted ticket), heads and validates the copy again, writes the row, then
  deletes the staging object. Re-using the PUT URL only touches staging. `UploadTicket` gained `StagingKey`; `StorageKeys.ForStaging`.
- Test failure: the portal case page lists Received requests read only ("Documents you have sent", status pill, no upload); Home still lists only Pending/Rejected.
- Should fix 1: refusal log lines carry case id, user id, size and problem only (no key, no file name).
- Should fix 3: a concurrent completion that hits the `storage_key` unique index returns the first document; a completion that finds staging already
  cleaned up also returns the existing document. Nice-to-have 2: completing a ticket whose document was removed is 404.
- New tests: PutUrl_ReusedAfterComplete_DoesNotChangeDocument, Complete_Concurrent_ReturnsOneDocument, Complete_AfterRemoval_404,
  StagingKey_HasNoFileNameAndIsUnguessable; Complete_WithRequest_SetsReceivedAndLinks now checks Received on the portal case page and the staff page. Suite 118/118.
- Deployer (M8): add an R2 lifecycle rule on the files bucket expiring `staging/` after 1 day (covers abandoned and refused uploads, review Should fix 2).
  Optional for dev: the same rule on SeaweedFS is not needed.
- Left: Should fix 2 Content-Length binding (not done; staging + lifecycle covers orphans, see decisions.md), Nice-to-have 1 (magic bytes) and 3 (stale reject shows 400).
- Race fix (tester, intermittent Complete_Concurrent_ReturnsOneDocument): `IStorageService.CopyAsync` returns false when the source is gone; a missing
  staging object on head or copy re-reads the document by final key and returns it, else 400. New test Copy_MissingSource_ReturnsFalse. Flow + storage tests 10/10 runs green.

- Seen once in a full-suite run after the race fix: M3 `CaseTests.Create_StatusNew_OpenedAtSet` failed (passed alone and in the next 2 full runs). Likely its 1-second time window under load; not M4 code, left for the tester to watch.


## M5 Chat (developer, 2026-10-10)

Built:
- Service: `Services/Chat/ChatService.cs` (`IChatService`: own client id, list with unread counts, unread total, open + mark read (Q-05),
  new-since cursor, send (trim, 1..4000 chars), chat-file `RecordUploadAsync`, chat-file download URL + audit). Every id goes through
  `ClientsFor`/`CanAccessClientAsync`; no edit/delete (Q-21). `// NOTIFY: ChatMessage R-091/R-092` markers in send and file record.
- Uploads: `UploadService` Chat branch (key `chats/<clientId>/<uuid>/<name>`, same staging -> copy -> re-validate, ticket bound to user +
  target, idempotent completion by `attachment_key`, access via CanAccessClient). `Uploads/Start` accepts `kind=Chat&clientId=`.
  Migration `*_M5_MessageAttachmentKeyIndex` (unique `messages.attachment_key`).
- Downloads: `Files/Download/{messageId}?handler=Message` (CanAccessClient, audit `Document.Downloaded` with entity `Message`, 5-min presigned GET).
- Pages: `Staff/Chats/Index` (list), `Staff/Chats/Thread` at `/Staff/Chats/{clientId}` (list left on desktop, thread right; `?handler=New`
  poll, `?handler=Send`), `Staff/Chats/_ChatList`, `Staff/Chats/ChatPages.cs` (shared send/poll results), `Portal/Chat/Index`
  (`/Portal/Chat`, client id from the signed-in Client only).
- Components: `_MessageBubble`, `_ChatMessages` (bubbles + polling sentinel `#chat-poll`, `hx-trigger="every 5s, chat:refresh from:body"`,
  outerHTML swap so only new messages are rendered), `_ChatComposer` (HTMX send; attach via `upload.js` with `data-upload-event`), models in
  `ComponentModels.cs`. `wwwroot/js/chat.js` (scroll to newest, Enter sends). `upload.js`: optional `data-upload-event` instead of reload.
- Sidebar Chats unread pill (gold, R-136) in `_StaffLayout`; dashboard tile now uses `IChatService.UnreadAsync` (same count).
  Portal Home Messages tile count got `data-tile-count` (test hook only).
- Resx: 13 keys en + ms (`Staff.Chats.*`, `Portal.Chat.*`, `Chat.*`).
- Tests: `tests/ClientHub.Tests/Chat/ChatTests.cs` (10): Staff_UnassignedClient_404, Staff_AssignedToAnyCase_ReadsAndSends,
  LawyerAndOfficeManager_SeeEveryChat, Client_OtherClientChat_404, PollingEndpoint_ReturnsOnlyNewMessages_EscapesText,
  Send_Htmx_TriggersRefresh_EmptyAndTooLongRefused, OpenChat_MarksOtherSideRead, AttachmentKey_UnderChatsClientId,
  ChatFile_Download_WritesAudit, ChatUpload_TicketForOtherClient_Refused. Full suite 129/129.

Skipped:
- Save to case (R-070..R-072): CUT. Plan tests SaveToCase_* and Client_SaveToCase_404 not written; no SaveToCase page.
- Upload progress is a navy "bubble" above the composer, not inside the final message bubble (the message row exists only after completion).

Tester notes:
- Run `npm run css:build`. Two browsers: client@ on `/Portal/Chat`, staff/lawyer on `/Staff/Chats/{clientId}`; a message appears on the
  other side within 5 s; sender's own send appears at once (HX-Trigger). Pill in the sidebar and the list clears when the thread opens.
- Check at 390px: `/Portal/Chat` (card fills the screen above the bottom nav), `/Staff/Chats/{id}` (list hidden, back arrow).
- Attach a PNG/PDF in chat: PUT goes to `staging/...`; message shows name + size, download link `/Files/Download/{id}?handler=Message`.

Review fixes (M5 review, 2026-10-10):
- Critical 1: `messages.seq bigint GENERATED ALWAYS AS IDENTITY` (migration `*_M5_MessageSeq`, index `(client_id, seq)`) is the poll cursor
  (`seq > after`, ordered by seq). `SendAsync` and `RecordUploadAsync` insert inside a transaction that first takes
  `pg_advisory_xact_lock(<client id as bigint>)`, so per chat seq order = commit order. Mark-read covers only `seq <=` the highest seq shown to
  that viewer (returned or the cursor).
- Should fix 1: the poll sends `visible` (hx-vals from `document.visibilityState`); background-tab polls fetch but do not mark read.
  `chat.js` triggers an immediate poll when the tab becomes visible again.
- Should fix 2: a file message carries the composer text as its caption (`upload.js` sends `caption` with Complete via `data-upload-caption`;
  `IUploadService.CompleteAsync(user, ticket, caption, ct)`; over 4000 chars = 400 `Chat.Error.TooLong`).
- Should fix 3: audit actions `Message.FileUploaded` / `Message.FileDownloaded`.
- Nice to have: `hx-sync="this:queue last"` on the poll sentinel; `Staff.Chats.Profile` key (en + ms); no "file" fallback name
  (downloads require `attachment_name`). Not done: Nice 4 (portal double client lookup per poll), Nice 5 (bubble sides) recorded in decisions.md.
- New tests: Poll_LateCommittedEarlierStamp_StillShown_HiddenPollLeavesUnread, Poll_UncommittedInsert_NotSkippedNorMarkedRead,
  ConcurrentSends_AllShownInSeqOrder, ChatFile_CarriesCaption_TooLongRefused. ChatTests 14/14 in 5 consecutive runs; full suite 133/133.
- Tester: with the thread open in a background tab, a new client message must stay unread (pill stays) until the tab is shown.


## M6 Bills and Settings (developer, 2026-10-10)

Built:
- Domain: `Data/Entities/{Bill.cs,BillLine.cs}` (moved out of Domain.cs; `Recalculate`, `AddLine/UpdateLine/RemoveLine/SetNote` Draft only,
  `MarkSent` (>=1 line, issued_at), `MarkPaid` (Sent only), `BillRuleException`). No migration (model unchanged).
- Services: `Services/Bills/BillNumberGenerator.cs` (per-year `pg_advisory_xact_lock` + unique index), `Services/Bills/BillService.cs`
  (`FindAsync`/`ListFor(user, caseId?)` = access gates via CasesFor, Clients never see Draft; every change locks the bill row
  `FOR UPDATE` in a transaction; status audit `Bill.StatusChanged` in the same transaction; `// NOTIFY: BillSent / BillPaid R-094`),
  `Services/Settings/FirmSettingsService.cs` (settings, SMTP password protected with purpose `ClientHub.SmtpPassword`, bank view +
  5-min presigned QR URL, firm accounts create/deactivate/reactivate), `Services/Access/AccountEmails.cs` (set-password email; staff kind).
- Uploads: `UploadService` SettingsQr branch (Lawyer only, JPG/PNG only, key `settings/qr/<uuid>/<name>`, staging -> copy as before);
  `Uploads/Start` accepts `kind=SettingsQr`; `UploadRules.ImageContentTypes/ImageExtensions`; `_FileUpload` gains `ImagesOnly`/`Hint`.
- Pages: `Staff/Bills/{Index,Edit,_BillLines}` (HTMX add/edit/remove line swaps `#bill-lines`), `Portal/Bills/{Index,Details,_BillCards}`,
  `Staff/Settings/{Index,StaffAccounts}` (old `Staff/Settings.cshtml` moved to `Settings/Index`; policy now `AuthorizeFolder("/Staff/Settings", LawyerOnly)`).
  Staff case page: Bills card with "+ New bill" (`?handler=NewBill`). Portal case page: Bills tab. `Components/_BankDetails` + `BankDetailsModel`.
  `wwwroot/css/print.css` (linked media=print in `_Head`). 97 resx keys en + ms. `WebServiceExtensions` registers the new services.
- Tests: `Bills/BillTests.cs` (14), `Bills/BillAccessTests.cs` (5), `Settings/SettingsTests.cs` (6). Full suite 157/157.

Skipped: Settings "Send test email" (R-113, cut). Staff deactivation has no confirm dialog. ClientService keeps its own account email
method (AccountEmails not back-ported, to avoid touching M3 code).

Tester notes:
- `npm run css:build` first. As Staff on an assigned case: "+ New bill" -> editor; add Fee/Expense lines (total updates, read only);
  Mark sent -> line controls gone; Mark paid. Client: `/Portal/Bills`, `/Portal/Bills/Details/{id}`, print preview hides nav/header icons/button.
- Draft bill URL as the client -> 404. Settings as lawyer1: save firm/bank/SMTP (password field never filled back), upload a PNG QR,
  Add staff (set-password email in console log), Deactivate/Reactivate. OM/Staff get 404 on `/Staff/Settings*`.
- Check at 390px: `/Portal/Bills`, `/Portal/Bills/Details/{id}`, `/Portal/Cases/Details/{id}?tab=bills`.

## M7 Notifications and email (developer, 2026-10-10)

Built:
- `Services/Notifications/{NotificationKinds,INotificationService,NotificationService}.cs`: the single Notify service. Writes one row per
  distinct recipient (title from `Notify.<Kind>.Title` in the recipient's language, {0} = case title / client name / bill no), saves, then
  emails active Client recipients only for emailed kinds (`Email.<Kind>.{Subject,Body}`, recipient's language, body = greeting + "what
  happened" + `APP_BASE_URL/Account/Login?ReturnUrl=<target>` via `IAppLinks`). Email failures are caught and logged (R-100).
  Also `NotifyCaseClientAsync`, `UnreadCountAsync`, `ListAsync`, `OpenAsync`, `MarkAllReadAsync` (all scoped to a user id).
- `IAccessService`/`AccessService`: `FirmRecipientsForUploadAsync(caseId)` (lawyer in charge + case_staff) and
  `FirmRecipientsForClientMessageAsync(clientId)` (lawyers + case_staff of all the client's cases, deduplicated; all active Lawyers when
  no cases). OM never included.
- All 8 `// NOTIFY:` markers replaced (none left): `CaseService` (status R-086, update R-087), `DocumentService` (request R-088,
  reject R-089, client upload R-090; firm uploads notify nobody), `ChatService` (text + file message: client sender -> firm recipients,
  firm sender -> client login, R-091..R-093), `BillService` (Sent/Paid R-094). Each call runs after the action's commit.
- Pages: `Pages/Notifications/{_ViewStart,Index,Open}` (folder requires sign-in; layout by role). List with gold unread dot,
  "Mark all read" (POST `?handler=MarkAll`), each item is a POST form to `/Notifications/Open/{id}` (marks read, LocalRedirect to the
  target; others' ids 404; GET 404). `_Bell` uses `INotificationService.UnreadCountAsync` and has a `data-bell-count` hook.
- DI: `INotificationService` registered; `/Notifications` authorised. Resx: 26 new keys en + ms (`Notifications.*`, `Notify.*.Title`,
  `Email.<Kind>.*` for 7 client kinds).
- Tests: `tests/ClientHub.Tests/Notifications/NotificationTriggerTests.cs` (19): every trigger with who does / does not get it, OM and
  unassigned Staff excluded, dedup across cases, no-cases fallback (inactive lawyer excluded), firm never emailed, SMTP failure keeps
  the action and the row, inactive client not emailed, email in `ms` without case details, Open marks read, others' notification 404,
  Mark all read only touches own rows, anonymous redirect. Full suite 176/176. `FakeEmailSender` already existed (M2).

Skipped:
- Settings "Send test email" (R-113): CUT for v1 (cut list items 1-3), not built. `SaveToCase_SendsNoNotification`: Save to case is cut.
- No digests (Q-22).

Tester notes:
- Run `npm run css:build` (new classes). Dev console shows the email (no SMTP host, Development) after e.g. a status change as Staff.
- Wrong SMTP host: set it on Settings, post an update -> page succeeds; the error is logged (MailKit connect fails within 5 s, so the
  request takes up to ~5 s per client email).
- Check at 390px: `/Notifications` as a Client (portal layout, light header); bell count in the portal header and staff sidebar.

Review fixes (M7 review, 2026-10-10):
- Should fix 1: every trigger runs its post-commit work through `INotificationService.AfterCommitAsync(kind, work)`. It uses
  CancellationToken.None, catches and logs any failure (DB, recipients lookup, cancellation; it logs the exception type only), and discards unsaved notification rows. A
  failing Notify step never fails the saved action, and upload staging cleanup still runs.
- Should fix 2 (Fiqri): firm recipients are active users only. When none remain (no cases, or the lawyers in charge are deactivated and
  no active Staff is assigned), uploads and client messages go to all active Lawyers (`AccessService.ActiveOrAllLawyersAsync`).
- Should fix 3: `/Notifications` shows 50 per page (`?p=N`) with "Load older" / "Newer" buttons. "Mark all read" shows whenever any
  notification is unread, on any page.
- New tests: `ClientEvents_NoActiveRecipient_FallBackToAllActiveLawyers`, `List_Pages_OlderNotificationsReachable` (in
  NotificationTriggerTests). `Notifications/NotifyFailureTests.cs` uses a factory whose NotifyAsync throws:
  `NotifyThrows_ChatSend_SucceedsAndSavesOneMessage`, `NotifyThrows_StatusChange_Succeeds`. 2 new resx keys en + ms. Suite 180/180.
- Left (Nice to have): 1 (SMTP failure log still includes the full MailKit exception), 2 (background email queue), 3 (non-local link test, unassigned Staff on client message), 4 (plan.md line 145 is
  stale; it still describes the 3-argument, page-handler Notify).


## M8 Deploy and backups (deployer, 2026-10-10)

Files: `Dockerfile.vercel` (npm ci, USER app), `vercel.json`, `.github/workflows/backup.yml`, `.env.example` (added DATABASE_URL, AllowedHosts, SECURITY_STAMP_INTERVAL_SECONDS; removed unused S3_FORCE_PATH_STYLE), `.claude/handoffs/deploy.md`.
Verified: image builds, serves /health 200 as uid 1654 (app) on PORT=8080 against compose db; backup.yml parses as YAML (no actionlint available); 180/180 tests.
Not verified: backup workflow against real Neon/R2, Vercel container runtime, restore drill (need cloud accounts).
