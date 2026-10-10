# Client Management Hub – Technical Specification (Version 1)

Date: 10 October 2026 · Author: Fiqri Wilter

Revised 10 October 2026: file storage moved from Vercel Blob to S3-compatible storage (Cloudflare R2, SeaweedFS in development), added the Office Manager role, chat attachments, file removal and daily backups. SeaweedFS replaced MinIO in development and CI on 10 October 2026 because MinIO no longer publishes community container images. The reasons are in `.claude/handoffs/decisions.md`.

This document describes how the system in the Functional Specification is built, stored, secured and deployed. The reader is the developer. Everything here is free to run at the firm's current size.

## Architecture

One ASP.NET Core application runs as a Docker container on Vercel and serves every page to both staff and clients. It talks to three outside services, all on free tiers.

| Part | What it does | Where it runs |
| --- | --- | --- |
| Browser | Renders server-side pages. HTMX swaps parts of the page (chat, upload status) without a full reload. Uploads files straight to file storage with a presigned URL | Staff and client devices, mostly phones for clients |
| App container | ASP.NET Core 10 with Razor Pages. Handles login, pages, access rules, notifications, email sending and bill totals | Vercel Functions, container runtime, scales to zero when idle |
| Database | PostgreSQL. All records: users, clients, cases, documents, messages, bills, notifications | Neon, connected through the Vercel Marketplace |
| File storage | The files themselves, in a private bucket. The database keeps only the object key and metadata | Cloudflare R2 (S3-compatible, free tier). SeaweedFS in the dev container |
| Backups | Daily encrypted database dump | GitHub Actions scheduled job, stored in a separate R2 bucket |
| Email | Outgoing alerts to clients and the set-password link | Brevo or Gmail over SMTP, settings stored in the database |

Request path: browser → Vercel edge → app container → PostgreSQL. File download: browser asks the app for a file; the app checks access, then redirects to a presigned GET URL that expires in 5 minutes. File upload: browser asks the app for a presigned PUT URL for a specific case or chat, uploads to storage directly, then tells the app the file is there.

## Tech stack

| Part | Choice | Why |
| --- | --- | --- |
| Language and framework | C# on ASP.NET Core 10, Razor Pages | Chosen to build .NET skills. One project keeps version 1 small |
| UI | Tailwind CSS, HTMX for partial page updates, Alpine.js for small interactions | Modern look and smooth feel without a separate frontend app |
| Login and security | ASP.NET Identity, email and password, two-factor with an authenticator app | Built in and free. Two-factor is required for the two lawyers and the Office Manager |
| Database access | EF Core with Npgsql, code-first migrations | Standard .NET data access |
| Database | PostgreSQL on Neon, free tier | Added through the Vercel Marketplace, connection string arrives as an environment variable |
| File storage | S3 API through `AWSSDK.S3`: Cloudflare R2 in preview and production, SeaweedFS in the dev container and CI. Presigned PUT for uploads, presigned GET for downloads | Vercel functions reject request bodies over 4.5 MB, so files never pass through the app. Presigned URLs are plain S3, fully supported by the .NET SDK, need no upload callback (so they work on localhost) and keep the bucket private. Vercel Blob was dropped because its client-upload handshake exists only in its JavaScript SDK |
| Email | Plain SMTP through MailKit. Brevo free tier (300 a day) or Gmail with an app password | Free. Host, port, user and password are set on the settings page |
| Languages | ASP.NET localisation with .resx files, English and Bahasa Melayu | Built in, no package |
| Bill PDF | Browser print of the bill page with a print stylesheet | Enough for version 1 |
| Hosting | Vercel, container runtime, Hobby plan for now | Free. Container images are available on all Vercel plans |
| Tests | xUnit, with Testcontainers or the dev-container PostgreSQL for integration tests | Standard .NET test stack |

## Data model

Thirteen tables. Every table has `id` (uuid), `created_at` and `updated_at`. Status and type columns are short text values checked in code, not database enums, so adding a value is a code change only.

| Table | Columns | Relationships |
| --- | --- | --- |
| `users` | ASP.NET Identity columns (email, password hash, two-factor fields), `display_name`, `role` (Lawyer, OfficeManager, Staff, Client), `language` (en, ms; default en), `is_active` | A Client user links to one `clients` row |
| `clients` | `client_type` (Person, Company), `name`, `ic_or_reg_no`, `phone`, `email`, `address`, `contact_person_name`, `contact_person_phone`, `user_id` | One login per client |
| `cases` | `client_id`, `title`, `case_type` (Criminal, Civil), `status` (New, DocumentsCollected, Filed, Hearing, Completed, Cancelled), `lawyer_id`, `description`, `opened_at`, `closed_at` | Belongs to one client and one lawyer |
| `case_staff` | `case_id`, `user_id` | Which Staff users may open a case |
| `case_updates` | `case_id`, `author_id`, `body`, `posted_at` | Notes the client sees, newest first |
| `document_requests` | `case_id`, `name`, `note`, `status` (Pending, Received, Rejected), `rejection_reason`, `document_id` | A request may link to the file the client uploaded |
| `documents` | `case_id`, `uploaded_by_id`, `file_name`, `content_type`, `size_bytes`, `storage_key`, `is_active`, `removed_by_id`, `removed_at` | Every file on a case, from staff or client. Removed files have `is_active = false` and stay in storage |
| `messages` | `client_id`, `sender_id`, `body`, `attachment_key`, `attachment_name`, `attachment_content_type`, `attachment_size_bytes`, `sent_at`, `read_at` | One chat per client; `read_at` drives unread counts |
| `bills` | `case_id`, `bill_no`, `status` (Draft, Sent, Paid), `note_to_client`, `issued_at`, `paid_at`, `total` | `total` is recalculated from lines on every save while Draft |
| `bill_lines` | `bill_id`, `line_type` (Fee, Expense), `description`, `amount`, `sort_order` |  |
| `notifications` | `user_id`, `kind`, `title`, `link_url`, `read_at` | Drives the bell icon |
| `firm_settings` | Single row: `firm_name`, `bank_name`, `bank_account_no`, `bank_account_name`, `qr_storage_key`, `smtp_host`, `smtp_port`, `smtp_user`, `smtp_password` (encrypted), `smtp_from` | Edited on the settings page |
| `audit_log` | `user_id`, `action`, `entity`, `entity_id`, `at`, `details` (json) | Written on status changes, uploads, file removals, saves from chat to a case, downloads, logins and bill status changes |

Amounts are `numeric(12,2)` in RM. Bill numbers are generated as `BILL-<year>-<4-digit sequence>` inside a transaction. Nothing is ever deleted: cases are closed with a status, files are removed from view with `documents.is_active = false`, and client logins are deactivated with `users.is_active = false`. Storage objects are never deleted by the app, except an upload that fails validation before any row points to it.

Storage keys: `cases/<case_id>/<uuid>/<file name>` for case documents, `chats/<client_id>/<uuid>/<file name>` for chat attachments, `settings/qr/<uuid>.<ext>` for the DuitNow QR. The key is generated by the server, never by the browser.

## Access control

Access is checked on the server for every request, never only by hiding buttons. The rule is one function, `CanAccessCase(user, case)`, used by every page and handler that touches a case or anything under it.

| User role | Can open a case when | Everything else |
| --- | --- | --- |
| Lawyer | Always | Can open settings and staff accounts. Can create clients and cases and assign staff |
| OfficeManager | Always | Can create clients and cases and assign staff. No settings, no staff accounts |
| Staff | `case_staff` has a row for this user and case | Cannot create clients or cases or assign staff. No settings, no staff accounts |
| Client | `case.client_id` equals the client linked to this user | Only the client area; staff pages return 404 |

Rules:

- Documents, document requests, updates and bills inherit the case rule. The chat and the client record use `CanAccessClient(user, client)`: a Lawyer or OfficeManager always, a Staff user when assigned to any of that client's cases, a Client user only their own.
- Every list query is filtered by the same rule, so a Staff user's case list is built from `case_staff`, not from all cases with buttons hidden.
- Access failures return 404, not 403, so a client cannot learn that another case id exists.
- File downloads go through the app. The app checks the rule, then redirects to a presigned GET URL that expires in 5 minutes. Removed files (`is_active = false`) return 404.
- Razor Pages for staff live under `/Staff/` and require the Lawyer, OfficeManager or Staff role; `/Staff/Settings` requires Lawyer; client pages live under `/Portal/` and require the Client role. The folder-level policy is the first gate; `CanAccessCase` is the second.

## Key technical flows

File upload (case documents, document requests and chat attachments use the same flow):

1. The browser checks the file type (PDF, DOCX, JPG, PNG) and size (20 MB or less), then asks the app to start an upload. It names the target: a case (and the document request, if any), or a client's chat.
2. The app checks access (`CanAccessCase` for a case, `CanAccessClient` for a chat), generates the storage key and returns two things: a presigned PUT URL for that key, valid for 10 minutes, and an upload ticket. The ticket is the key, target, user id and expiry, protected with ASP.NET Data Protection so the browser cannot change it.
3. The browser PUTs the file straight to storage and shows progress.
4. The browser posts the ticket to the app's completion handler. The app checks the ticket, checks access again, and reads the object's real size and content type from storage (`HeadObject`). If either is not allowed, it deletes the object and returns an error.
5. For a case target, the app writes the `documents` row, sets the document request to Received if there is one, writes the audit row and creates notifications. For a chat target, it writes the `messages` row with the attachment fields and creates notifications.

There is no callback from storage to the app, so the same flow works on localhost against SeaweedFS.

Save a chat file to a case:

1. A staff user picks a case of that client which they can open (`CanAccessCase`).
2. The app copies the object server side (`CopyObject`) from the chat key to a new case key, writes a `documents` row and an audit row. The chat message keeps its own copy. No notification is sent.

Notifications and email:

1. Any event in the Notifications table of the Functional Specification calls `Notify(userIds, kind, title, link)`.
   Firm recipients for client events come from one place, `IAccessService`: for an upload, the case's lawyer plus its `case_staff` users; for a client chat message, the lawyers and `case_staff` users of all of that client's cases, falling back to all Lawyers when the client has no cases. The Office Manager is not included.
2. `Notify` writes one `notifications` row per user. For Client users it also sends one email through SMTP, in the user's language, with the title and a login link only.
3. Email sending happens in the same request, with a short timeout. If SMTP fails, the error is logged and the in-system notification still stands. There is no background queue in version 1.

Login and two-factor:

1. ASP.NET Identity handles registration (staff created by a lawyer, clients created by staff), set-password links, lockout after 5 failed attempts, and password reset.
2. Two-factor uses an authenticator app (TOTP). It is required for Lawyer and OfficeManager accounts and optional for others. Recovery codes are shown once.
3. Sessions use the Identity cookie, 14 days, sliding, secure and HTTP-only.

Languages:

1. Every piece of screen text, email text and validation message is a key in `.resx` files for `en` and `ms`.
2. The user's `language` column sets the culture for every request after login; new users get `en`. Before login the culture is English unless the visitor picks BM with the switcher, which is remembered in the culture cookie.
3. Dates show as `dd/MM/yyyy`, amounts as `RM 1,234.50`, in both languages.

Bill totals:

- The total is the sum of lines, computed in one place (`Bill.Recalculate()`), stored on the bill and shown with two decimals. Marking a bill Sent freezes its lines; only the status can change after that.

## Development environment

All work happens inside containers. The laptop needs only Docker Desktop, VS Code and the Dev Containers extension.

- `.devcontainer/devcontainer.json` opens the repository inside the app container, which has the .NET 10 SDK, Node (for Tailwind), the `dotnet-ef` tool and the Vercel CLI.
- `docker-compose.yml` runs three services: `app` (the dev container), `db` (PostgreSQL 16 with a named volume) and `storage` (SeaweedFS `weed server -s3` with a named volume, plus a one-shot `storage-init` step that creates the `clienthub-files` and `clienthub-test` buckets and applies the CORS rules). The app reaches the database at `db:5432` and storage at `storage:9000`. Presigned URLs for the browser are signed against `http://localhost:9000` (`S3_PUBLIC_ENDPOINT`), because the browser cannot resolve `storage`.
- `dotnet watch` runs the site with hot reload on port 5000; Tailwind runs in watch mode beside it.
- `.env.example` lists every variable. `.env` is git-ignored.
- Seed data: one command creates the firm settings, two lawyer accounts, one Office Manager account, one Staff account and one sample client with a case, so the app is usable immediately.
- Repository layout: `src/ClientHub.Web` (the app), `tests/ClientHub.Tests` (xUnit), `.devcontainer/`, `.github/workflows/`, `Dockerfile.vercel`, `vercel.json`, `docker-compose.yml`.

## CI and deployment

A push to `main` deploys to production. Nothing reaches `main` without a green pipeline and Fiqri's merge.

- Repository: Fiqri's personal GitHub, private. Branch protection on `main`: pull request required, CI must pass, only Fiqri can merge. Others may clone and push branches.
- Version 1 is built on the `build/v1` branch, one commit per milestone, and reaches `main` through one pull request.
- GitHub Actions on every push and pull request: restore, build, run xUnit tests against a PostgreSQL service container and a SeaweedFS container (started with `docker run` in a step, because it needs a start command), then build `Dockerfile.vercel` to prove the image compiles.
- Vercel is connected to the repository. Every branch gets a preview URL; `main` is production. `vercel.json` declares one service with `runtime: "container"` and `Dockerfile.vercel`, and a catch-all rewrite to it.
- `Dockerfile.vercel` is two-stage: SDK image restores and publishes, ASP.NET runtime image carries only the published output. The app binds to `0.0.0.0` on the `PORT` environment variable; `PORT` is set to 8080 in Vercel project settings to avoid the privileged-port problem.
- Secrets (database connection string, S3 endpoint, bucket and access keys, SMTP password encryption key) are Vercel environment variables, separate for preview and production. Preview uses its own R2 bucket.
- EF Core migrations run at startup. A preview deployment uses a Neon branch of the database, so it never touches production data.
- Logging goes to the console only, which Vercel captures as runtime logs.

## Backups

Two layers, so one failure does not lose data:

1. Neon's own point-in-time restore, within whatever window the free tier allows. Used for "undo the last few hours".
2. `.github/workflows/backup.yml`, a scheduled GitHub Actions job that runs daily at 02:00 Malaysia time (18:00 UTC) and can be run by hand:
   - `pg_dump --format=custom` of the production database over Neon's direct (non-pooled) connection, with a `pg_dump` matching Neon's PostgreSQL major version.
   - Encrypts the dump with `gpg --symmetric` (AES256) using the `BACKUP_PASSPHRASE` secret, because the dump holds IC numbers and case details.
   - Uploads it to a separate R2 bucket, `clienthub-backups`, under `db/<yyyy-mm-dd>.dump.gpg`, with a token that can only write to that bucket. A lifecycle rule on the `db/` prefix deletes objects after 30 days.
   - Mirrors the files bucket into the same backup bucket under `files/` (`aws s3 sync`, incremental), so a deleted bucket or a bad credential does not lose documents.
   - Fails loudly: GitHub emails the repository owner when a scheduled run fails.
- Secrets for the job live in GitHub Actions secrets, not in Vercel: `BACKUP_DATABASE_URL`, `BACKUP_PASSPHRASE`, `R2_ACCOUNT_ID`, `R2_BACKUP_ACCESS_KEY_ID`, `R2_BACKUP_SECRET_ACCESS_KEY`.
- Restore steps (download, `gpg --decrypt`, `pg_restore` into a new Neon branch, check it, then switch) are written in `.claude/handoffs/deploy.md` and tested once before handover.
- GitHub runs scheduled workflows only from the default branch, so the job starts after the `build/v1` pull request is merged into `main`.

## Known limits

- Vercel's Hobby plan is for personal, non-commercial use. The firm uses it anyway for now and moves to the Pro plan (USD 20 a month, paid by the firm) once the system is in daily use.
- The container sleeps after 5 minutes without traffic. The first page after a quiet period takes a few seconds to load.
- No always-on process, so no timers or background jobs in the app. Backups run in GitHub Actions. Version 2 reminders run through Vercel Cron calling an endpoint.
- No persistent disk in the container. Nothing is written to the file system except temporary files.
- Request and response bodies are capped at 4.5 MB, which is why uploads go straight to storage.
- A presigned PUT URL cannot enforce the 20 MB limit on its own, so the limit is checked twice: in the browser before upload, and with `HeadObject` in the completion handler.
- Cloudflare R2's free tier is 10 GB of storage, far above what this firm needs. R2 needs a payment method on the Cloudflare account even on the free tier. If that is a problem, Backblaze B2 also speaks the S3 API, and switching only changes the S3 environment variables.
- The free Neon tier pauses the database after inactivity, adding to the first-request delay.
