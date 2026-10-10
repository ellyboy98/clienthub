# Requirements: Client Management Hub v1

Sources: FSD, TSD, UI-Design-Notes, decisions.md (all 2026-10-10). Roles: Lawyer, OfficeManager (OM), Staff, Client.
"Firm user" = Lawyer, OM or Staff. "Can open a case" = `CanAccessCase`. "Can open a client" = `CanAccessClient`.

## Requirements

### Users and access
- R-001 A Lawyer can open every client, case, document, request, update, chat and bill at all times.
- R-002 An OM can open every client, case, document, request, update, chat and bill at all times.
- R-003 A Staff user can open a case only when `case_staff` has a row for that user and that case.
- R-004 A Staff user can open a client record and that client's chat only when assigned to at least one of that client's cases.
- R-005 A Client user can open a case only when `case.client_id` equals the client linked to their user.
- R-006 A Client user cannot open any other client's record, chat, cases, documents, updates or bills.
- R-007 A user can open a document, document request, case update or bill only when they can open its case.
- R-008 A user gets 404 (never 403) when opening any case, client, chat, document, file or bill they cannot access, or that does not exist.
- R-009 A Client user gets 404 on every `/Staff/` page.
- R-010 A Lawyer, OM or Staff user cannot open `/Portal/` pages (Client role required).
- R-011 An OM or Staff user gets 404 on `/Staff/Settings`; a Lawyer can open it.
- R-012 A Staff user with no assigned cases sees empty case, client, chat, bill and past-case lists.
- R-013 Every list query and dashboard count is filtered on the server by the same access rule, not by hiding rows or buttons.
- R-014 A Lawyer or OM can create a client record with its Client login; the system emails the client a set-password link.
- R-015 A Staff user cannot create clients or cases, or assign Staff to cases (no button; direct POST returns 404).
- R-016 A Lawyer can create Lawyer, OM and Staff accounts on Settings; the new user gets a set-password link.
- R-017 An OM or Staff user cannot create, edit or deactivate staff accounts.
- R-018 A Lawyer can deactivate a staff account; the user can no longer log in.
- R-019 A Lawyer can deactivate a Client login when the relationship ends; the client's data stays (OM: see Q-01).
- R-020 A user with `is_active = false` cannot log in with any password.
- R-021 A Company client has exactly one shared login.
- R-022 A user can log in with email and password; a user with 2FA enabled must also enter a valid TOTP code.
- R-023 A Lawyer or OM cannot use the staff area until 2FA is enabled on their account (see Q-15).
- R-024 A Staff or Client user can optionally enable 2FA in My account; recovery codes are shown once.
- R-025 A user is locked out after 5 consecutive failed login attempts.
- R-026 A user can reset a forgotten password through an emailed link.
- R-027 A logged-in session uses the Identity cookie: 14 days sliding, Secure, HttpOnly.
- R-028 Passwords are stored hashed, never in plain text.

### Clients
- R-029 A Lawyer or OM can save a client only when type, name, IC/reg no, phone, email and address are all filled.
- R-030 A Lawyer or OM can save a Company client only when person-in-charge name and phone are filled; Person clients have no such fields.
- R-031 A firm user who can open a client can edit the client's details at any time.
- R-032 A Client user cannot edit their own details.
- R-033 A client can have many cases; a case belongs to exactly one client.

### Cases
- R-034 A Lawyer or OM can create a case with title, type (Criminal/Civil), client, lawyer in charge (a Lawyer user), optional description and optional assigned Staff.
- R-035 A new case gets status New and `opened_at` set automatically.
- R-036 A Lawyer or OM can add or remove assigned Staff when creating or editing a case.
- R-037 A firm user who can open a case can set its status to New, Documents collected, Filed, Hearing or Completed, forward or backward.
- R-038 A firm user who can open a case can set it to Cancelled from any stage.
- R-039 A case set to Completed or Cancelled gets `closed_at` and moves to Past cases for staff and client (reopening: Q-09).
- R-040 No user can delete a case; a past case keeps its documents, updates and bills viewable.
- R-041 A firm user who can open a case can post an update (text, dated automatically).
- R-042 A Client user can see every update on their case, newest first, and the case description.
- R-043 A Client user cannot change status, post updates, or edit case fields.
- R-044 Every status change and every posted update writes an audit row with user and time.

### Documents
- R-045 A firm user who can open a case can add a document request with a name and optional note; it starts Pending.
- R-046 A Client user can upload a file against a Pending or Rejected request on their own case.
- R-047 A client upload against a request sets the request to Received and links it to the new document.
- R-048 A firm user who can open a case can mark a request Rejected only with a reason; the client sees the reason and can re-upload.
- R-049 A Client user cannot upload to a case except against a request (see Q-11).
- R-050 A firm user who can open a case can upload files to it; every active file is visible to that case's client.
- R-051 A firm user who can open a case can remove a file after confirming; it sets `is_active = false`, `removed_by_id`, `removed_at` and writes an audit row.
- R-052 A removed file is hidden from staff and client case pages, its download returns 404, and its storage object stays.
- R-053 A Client user cannot remove files.
- R-054 A user can upload only PDF, DOCX, JPG or PNG of 20 MB or less; checked in the browser and again on completion via `HeadObject`.
- R-055 An upload that fails the server check has its storage object deleted and returns an error; no row is written.
- R-056 Each document stores original file name, uploader, upload time, content type and size.
- R-057 A user can start an upload only when they can open the target; the app returns a 10-minute presigned PUT and a Data Protection ticket.
- R-058 The completion handler rejects a tampered or expired ticket, and re-checks access before writing rows.
- R-059 The server generates storage keys (`cases/<case_id>/<uuid>/<name>`); the browser never chooses a key.
- R-060 A user can download a file only after the access check; the app redirects to a presigned GET that expires in 5 minutes.
- R-061 File bytes never pass through the app for upload or download.
- R-062 Every document upload and every download writes an audit row.

### Chat
- R-063 Each client has exactly one chat with the firm, not split by case.
- R-064 A Lawyer or OM can read and reply in any client's chat.
- R-065 A Staff user can read and reply in a client's chat only when assigned to at least one of that client's cases.
- R-066 A Client user can read and send only in their own chat.
- R-067 A user can send text and/or a file (same type and size rules), uploaded via the same presigned flow to `chats/<client_id>/<uuid>/<name>`.
- R-068 Each message shows sender name and time.
- R-069 The chat refreshes by polling every 5 seconds; real time is not required.
- R-070 A firm user can "Save to case" a chat file only into a case of that client they can open; another case id returns 404.
- R-071 Save to case copies the object server side, writes a `documents` row and an audit row, sends no notification; the message keeps its copy.
- R-072 A Client user cannot use Save to case.
- R-073 A chat file is not added to case documents unless saved to case.
- R-074 A user can download a chat file only when they can open the client; same 5-minute presigned GET and audit row.

### Bills
- R-075 A firm user who can open a case can create a bill for it; it starts Draft.
- R-076 A bill gets a unique number `BILL-<year>-<4-digit seq>` generated inside a transaction (timing: Q-06).
- R-077 A firm user can add, edit and delete lines only while the bill is Draft: type Fee/Expense, description, amount in RM (numeric 12,2).
- R-078 The total is the sum of lines, computed only in `Bill.Recalculate()`, stored, shown with two decimals, never typed.
- R-079 A Client user cannot see a Draft bill in any list, and opening it by id returns 404.
- R-080 A firm user can mark a Draft bill Sent; `issued_at` is set and lines and note are frozen.
- R-081 A firm user cannot edit lines or note on a Sent or Paid bill; only Sent to Paid is allowed.
- R-082 A firm user can mark a Sent bill Paid; `paid_at` is set and the client sees the new status.
- R-083 Every bill status change writes an audit row.
- R-084 A Client user can see Sent and Paid bills on their cases with lines, total, status, note, bank details and DuitNow QR.
- R-085 A Client user can print or save a bill as PDF through the browser print stylesheet.

### Notifications
- R-086 A Client gets an in-system notification and email when a case status changes.
- R-087 A Client gets an in-system notification and email when a case update is posted.
- R-088 A Client gets an in-system notification and email when a document request is added.
- R-089 A Client gets an in-system notification and email when a document request is rejected.
- R-090 The case's lawyer in charge and its assigned Staff get an in-system notification (no email) when the client uploads a document.
- R-091 A Client gets an in-system notification and email when a firm user sends a chat message.
- R-092 The lawyers in charge and assigned Staff of all the client's cases (deduplicated) get an in-system notification when the client sends a message.
- R-093 All Lawyers get the notification when a client with no cases sends a message.
- R-094 A Client gets an in-system notification and email when a bill is marked Sent, and again when it is marked Paid.
- R-095 A Client gets an email with a set-password link when their account is created.
- R-096 An OM is never notified of client uploads or client messages.
- R-097 A Staff user not assigned to the relevant case(s) is never notified of client events.
- R-098 A firm user receives in-system notifications only, never email.
- R-099 A client email contains only what happened and a login link (no case details, file names or message text), in the client's language.
- R-100 An SMTP failure is logged; the in-system notification is still saved and the user's action succeeds.
- R-101 Firm recipients for client events are computed in one place (`IAccessService`).
- R-102 A user sees a bell with unread count; clicking a notification opens the related case, chat or bill and marks it read.
- R-103 A user can mark all notifications read.

### General
- R-104 All screen text, emails, notifications and validation messages exist in English and Bahasa Melayu `.resx`; no hard-coded strings.
- R-105 A new user's language is English; the login page shows English unless the visitor switches (remembered in culture cookie).
- R-106 A user can change their language in My account; it applies to every page after login and to their emails.
- R-107 User-typed data (titles, updates, messages) is shown as typed in both languages.
- R-108 Dates show as dd/MM/yyyy and amounts as "RM 1,234.50" in both languages.
- R-109 All pages are served over HTTPS.
- R-110 Logins and file downloads are recorded in `audit_log` (PDPA access log).
- R-111 A Lawyer can set firm name, bank name, account number, account name, DuitNow QR image and SMTP host/port/user/password/from on Settings; values live in the database.
- R-112 The SMTP password is stored encrypted.
- R-113 A Lawyer can send a test email from Settings.
- R-114 No user action deletes a record or a stored object (except failed-validation uploads).
- R-115 Every page works on a phone; every portal page is usable at 390px wide.
- R-116 A seed command creates firm settings, 2 Lawyers, 1 OM, 1 Staff and 1 sample client with a case.
- R-117 docker-compose runs `app`, `db` (PostgreSQL 16) and `storage` (SeaweedFS, bucket `clienthub-files` created); presigned URLs signed against `S3_PUBLIC_ENDPOINT`.
- R-118 EF Core migrations run at app startup.
- R-119 CI on every push and PR: restore, build, xUnit against PostgreSQL and SeaweedFS, build `Dockerfile.vercel`.
- R-120 `Dockerfile.vercel` is two-stage and binds `0.0.0.0:$PORT`; `vercel.json` declares the container service with a catch-all rewrite.
- R-121 Logs go to the console only; nothing persistent is written to the container file system.
- R-122 `.env.example` lists every variable; no secret is committed.
- R-123 xUnit tests cover access rules, bill totals and notification triggers.

### Backups
- R-124 `.github/workflows/backup.yml` runs daily at 18:00 UTC and on manual dispatch.
- R-125 The job runs `pg_dump --format=custom` over Neon's direct connection with a matching major version.
- R-126 The dump is encrypted with `gpg --symmetric` AES256 using `BACKUP_PASSPHRASE`.
- R-127 The dump is uploaded to bucket `clienthub-backups` as `db/<yyyy-mm-dd>.dump.gpg` with a write-only token; a lifecycle rule deletes `db/` objects after 30 days.
- R-128 The files bucket is mirrored incrementally to `files/` in the backup bucket.
- R-129 A failed run fails the job so GitHub emails the owner.
- R-130 Secrets come from GitHub Actions: `BACKUP_DATABASE_URL`, `BACKUP_PASSPHRASE`, `R2_ACCOUNT_ID`, `R2_BACKUP_ACCESS_KEY_ID`, `R2_BACKUP_SECRET_ACCESS_KEY`.
- R-131 Restore steps are written in `.claude/handoffs/deploy.md` and tested once before handover.

### Screens
- R-132 Any visitor sees Login with email, password, 2FA code when enabled, Forgot password, EN|BM switch and "Your account is created by the firm"; no self-registration.
- R-133 Any user can open Set password from an account-creation or reset email link.
- R-134 Any user sees Notifications as a list with unread marker and "Mark all read".
- R-135 Any user sees My account with change password, 2FA setup and language choice.
- R-136 A firm user sees the staff sidebar: Dashboard, Clients, Cases, Chats (unread count), Bills, Past cases, Settings (Lawyer only), own name and role (see Q-04).
- R-137 A firm user sees Dashboard tiles (active cases, documents pending from clients, unread chats, bills awaiting payment) counted over cases they can open.
- R-138 A firm user sees on Dashboard an active-cases table (case, client, type, status, lawyer, last update) and a "Needs your attention" feed (see Q-03).
- R-139 A firm user sees Clients list with search by name/IC/reg no, chips All/Person/Company/Inactive, columns client, type, IC/reg no, phone, active cases, login state, Open.
- R-140 A Lawyer or OM sees "+ New client" on Clients list; a Staff user does not.
- R-141 A firm user sees Client page with details, cases, chat link; "Create case" only for Lawyer and OM.
- R-142 A firm user sees Case page header with breadcrumb, title, status badge, Post update and Change status, and a 5-step progress bar plus Cancelled.
- R-143 A firm user sees on Case page the requests (Pending/Rejected/Received, View/Reject), updates, documents (Upload/Download/Remove), case details, client card (Open chat, Profile), bills with "+ New bill"; tabs on phone.
- R-144 A firm user sees a Remove confirmation that says the file is kept in the records.
- R-145 A Lawyer or OM can edit assigned staff on the Case page; a Staff user sees them read-only.
- R-146 A firm user sees staff Chat with accessible clients and unread pills on the left, thread on the right, "Save to case" on each file bubble.
- R-147 A firm user sees Bill editor with lines table (type, description, amount, delete), + Add line, read-only total, note, status card ("Sending freezes the lines"), bank/QR preview.
- R-148 A firm user sees Past cases listing Completed and Cancelled cases they can open.
- R-149 A Lawyer sees Settings with firm details, bank, QR upload, SMTP with Send test email, staff accounts (role, 2FA state, Add staff, Deactivate) and no language setting.
- R-150 A Client sees a fixed mobile bottom nav: Home, Cases, Chat, Bills.
- R-151 A Client sees Home with "Hello, <name>", a "We still need from you" card first (Upload per pending request), active case cards (title, status, type, opened date, last update), Messages tile (unread) and Bills tile (amount unpaid).
- R-152 A Client sees Case page with title, status, lawyer in charge, tabs Updates/Documents/Bills, pending and rejected requests at top with reason and Re-upload, updates newest first, shared documents with Download.
- R-153 A Client sees Chat with firm messages (sender name) and own messages on the right, file bubbles with name and size, attach and send with progress shown.
- R-154 A Client sees Bill page with "Amount due", lines with Fee/Expense, total, status, bank name, account name and number, reference = bill number, DuitNow QR, "Print / Save as PDF".
- R-155 A Client sees Past cases listing their Completed and Cancelled cases.
- R-156 Any user sees a 404 page (no "forbidden" message) on an access failure.
- R-157 Every status word is identical in the portal and the staff area, in both languages.
- R-158 Every tap target is at least 44px on mobile; content scrolls under fixed navigation.

## Data model
All tables: `id` uuid, `created_at`, `updated_at`. Status/type = short text checked in code. Amounts `numeric(12,2)`.
- [ ] `users`: Identity columns, display_name, role (Lawyer/OfficeManager/Staff/Client), language (en/ms, default en), is_active
- [ ] `clients`: client_type, name, ic_or_reg_no, phone, email, address, contact_person_name, contact_person_phone, user_id
- [ ] `cases`: client_id, title, case_type, status (New/DocumentsCollected/Filed/Hearing/Completed/Cancelled), lawyer_id, description, opened_at, closed_at
- [ ] `case_staff`: case_id, user_id
- [ ] `case_updates`: case_id, author_id, body, posted_at
- [ ] `document_requests`: case_id, name, note, status (Pending/Received/Rejected), rejection_reason, document_id
- [ ] `documents`: case_id, uploaded_by_id, file_name, content_type, size_bytes, storage_key, is_active, removed_by_id, removed_at
- [ ] `messages`: client_id, sender_id, body, attachment_key, attachment_name, attachment_content_type, attachment_size_bytes, sent_at, read_at
- [ ] `bills`: case_id, bill_no, status (Draft/Sent/Paid), note_to_client, issued_at, paid_at, total
- [ ] `bill_lines`: bill_id, line_type (Fee/Expense), description, amount, sort_order
- [ ] `notifications`: user_id, kind, title, link_url, read_at
- [ ] `firm_settings` (single row): firm_name, bank_name, bank_account_no, bank_account_name, qr_storage_key, smtp_host, smtp_port, smtp_user, smtp_password (encrypted), smtp_from
- [ ] `audit_log`: user_id, action, entity, entity_id, at, details (json)

## Out of scope
- Tasks and deadlines (incl. client tasks, 3-day reminders, Vercel Cron)
- Calendar of hearings, meetings, filing deadlines
- Online payment (FPX, DuitNow payment gateway)
- WhatsApp alerts
- Joint clients (more than one client per case)
- Also not v1: e-signature, ready-made request checklists, time tracking, receipts/SST, other-party/court fields, real-time chat, native app, background job queue

## Open questions
- Q-01 Can the OM deactivate a client login? FSD says "a lawyer", but the OM has lawyer-equivalent rights on clients. Default: Lawyer and OM can; Staff cannot.
- Q-02 Can deactivated client logins and staff accounts be reactivated? Not stated. Default: yes, the same roles that deactivate can reactivate.
- Q-03 Dashboard "overdue requests" (UI) has no due date in the data model. Default: show Pending and Rejected requests; no overdue concept.
- Q-04 The staff sidebar "Cases" and "Bills" and the portal bottom nav "Cases" and "Bills" have no FSD screen. Default: simple access-filtered lists (active cases; bills with status; portal shows Sent/Paid only).
- Q-05 `messages.read_at` is one column, shared by all firm users. Default: a client message counts as read once any firm user opens that chat; a firm message counts as read once the client opens it.
- Q-06 When is the bill number assigned, and which year? Default: on Draft creation, using the creation year; gaps from unsent drafts are accepted.
- Q-07 Can a Draft bill be deleted? Default: no (nothing is deleted); it stays as Draft.
- Q-08 Can a bill with zero lines be sent? Default: no, at least one line is required.
- Q-09 Can a Completed or Cancelled case be moved back to an active stage? Default: yes (backward moves allowed); `closed_at` is cleared and it returns to active lists.
- Q-10 Which case fields can Staff edit? Default: Staff can edit title, type and description; only Lawyer/OM can change lawyer in charge and assigned staff.
- Q-11 Can a client upload a file that is not against a request? Default: no.
- Q-12 After a rejection and re-upload, what happens to the first file? Default: it stays active; the request links to the new document and becomes Received.
- Q-13 Removing a document linked to a Received request: what happens to the request? Default: the request is unchanged; staff reject it if they need a new file.
- Q-14 Does editing a client's email also change their login email, and can Staff do it? Default: yes, it updates `users.email` too; any firm user who can open the client can edit it.
- Q-15 How is required 2FA for Lawyer/OM enforced? Default: after password login they are redirected to 2FA setup until it is enabled.
- Q-16 TSD says "clients created by staff"; FSD says Lawyer or OM. Default: FSD wins (Lawyer or OM only).
- Q-17 The FSD audits case update posts; the TSD audit list omits them. Default: audit them.
- Q-18 Deactivated users: notifications and pickers. Default: no email to inactive clients; deactivated Lawyers are left out of the lawyer picker and the all-Lawyers fallback; existing case assignments are kept.
- Q-19 Should failed logins be audited? Default: log both successful and failed logins.
- Q-20 Set-password link lifetime and resend. Default: Identity default (1 day); Forgot password works as the resend.
- Q-21 Can case updates or chat messages be edited or deleted? Default: no.
- Q-22 Grouping several events into one email is "nice to have". Default: don't build it in v1.
