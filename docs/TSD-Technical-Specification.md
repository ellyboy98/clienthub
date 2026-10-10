# Client Management Hub – Technical Specification (Version 1)

Date: 10 October 2026 · Author: Fiqri Wilter

This document describes how the system in the Functional Specification is built, stored, secured and deployed. The reader is the developer. Everything here is free to run at the firm's current size.

## Architecture

One ASP.NET Core application runs as a Docker container on Vercel and serves every page to both staff and clients. It talks to three outside services, all on free tiers.

| Part | What it does | Where it runs |
| --- | --- | --- |
| Browser | Renders server-side pages. HTMX swaps parts of the page (chat, upload status) without a full reload. Uploads files straight to file storage | Staff and client devices, mostly phones for clients |
| App container | ASP.NET Core 10 with Razor Pages. Handles login, pages, access rules, notifications, email sending and bill totals | Vercel Functions, container runtime, scales to zero when idle |
| Database | PostgreSQL. All records: users, clients, cases, documents, messages, bills, notifications | Neon, connected through the Vercel Marketplace |
| File storage | The files themselves. The database keeps only the link and metadata | Vercel Blob |
| Email | Outgoing alerts to clients and the set-password link | Brevo or Gmail over SMTP, settings stored in the database |

Request path: browser → Vercel edge → app container → PostgreSQL. File download: browser asks the app for a file; the app checks access, then redirects to a short-lived Blob URL. File upload: browser asks the app for an upload token for a specific case, uploads to Blob directly, then tells the app the file is there.

## Tech stack

| Part | Choice | Why |
| --- | --- | --- |
| Language and framework | C# on ASP.NET Core 10, Razor Pages | Chosen to build .NET skills. One project keeps version 1 small |
| UI | Tailwind CSS, HTMX for partial page updates, Alpine.js for small interactions | Modern look and smooth feel without a separate frontend app |
| Login and security | ASP.NET Identity, email and password, two-factor with an authenticator app | Built in and free. Two-factor is turned on for the two lawyers |
| Database access | EF Core with Npgsql, code-first migrations | Standard .NET data access |
| Database | PostgreSQL on Neon, free tier | Added through the Vercel Marketplace, connection string arrives as an environment variable |
| File storage | Vercel Blob, free tier, client-side uploads | Vercel functions reject request bodies over 4.5 MB, so files never pass through the app |
| Email | Plain SMTP through MailKit. Brevo free tier (300 a day) or Gmail with an app password | Free. Host, port, user and password are set on the settings page |
| Languages | ASP.NET localisation with .resx files, English and Bahasa Melayu | Built in, no package |
| Bill PDF | Browser print of the bill page with a print stylesheet | Enough for version 1 |
| Hosting | Vercel, container runtime, Hobby plan for now | Free. Container images are available on all Vercel plans |
| Tests | xUnit, with Testcontainers or the dev-container PostgreSQL for integration tests | Standard .NET test stack |

## Data model

Thirteen tables. Every table has `id` (uuid), `created_at` and `updated_at`. Status and type columns are short text values checked in code, not database enums, so adding a value is a code change only.

| Table | Columns | Relationships |
| --- | --- | --- |
| `users` | ASP.NET Identity columns (email, password hash, two-factor fields), `display_name`, `role` (Lawyer, Staff, Client), `language` (en, ms), `is_active` | A Client user links to one `clients` row |
| `clients` | `client_type` (Person, Company), `name`, `ic_or_reg_no`, `phone`, `email`, `address`, `contact_person_name`, `contact_person_phone`, `user_id` | One login per client |
| `cases` | `client_id`, `title`, `case_type` (Criminal, Civil), `status` (New, DocumentsCollected, Filed, Hearing, Completed, Cancelled), `lawyer_id`, `description`, `opened_at`, `closed_at` | Belongs to one client and one lawyer |
| `case_staff` | `case_id`, `user_id` | Which Staff users may open a case |
| `case_updates` | `case_id`, `author_id`, `body`, `posted_at` | Notes the client sees, newest first |
| `document_requests` | `case_id`, `name`, `note`, `status` (Pending, Received, Rejected), `rejection_reason`, `document_id` | A request may link to the file the client uploaded |
| `documents` | `case_id`, `uploaded_by_id`, `file_name`, `content_type`, `size_bytes`, `blob_url`, `blob_path` | Every file on a case, from staff or client |
| `messages` | `client_id`, `sender_id`, `body`, `attachment_blob_url`, `attachment_name`, `sent_at`, `read_at` | One chat per client; `read_at` drives unread counts |
| `bills` | `case_id`, `bill_no`, `status` (Draft, Sent, Paid), `note_to_client`, `issued_at`, `paid_at`, `total` | `total` is recalculated from lines on every save while Draft |
| `bill_lines` | `bill_id`, `line_type` (Fee, Expense), `description`, `amount`, `sort_order` |  |
| `notifications` | `user_id`, `kind`, `title`, `link_url`, `read_at` | Drives the bell icon |
| `firm_settings` | Single row: `firm_name`, `bank_name`, `bank_account_no`, `bank_account_name`, `qr_blob_url`, `smtp_host`, `smtp_port`, `smtp_user`, `smtp_password` (encrypted), `smtp_from` | Edited on the settings page |
| `audit_log` | `user_id`, `action`, `entity`, `entity_id`, `at`, `details` (json) | Written on status changes, uploads, downloads, logins and bill status changes |

Amounts are `numeric(12,2)` in RM. Bill numbers are generated as `BILL-<year>-<4-digit sequence>` inside a transaction. Soft delete is not used; cases are closed with a status, and files and clients are deactivated, never removed.

## Access control

Access is checked on the server for every request, never only by hiding buttons. The rule is one function, `CanAccessCase(user, case)`, used by every page and handler that touches a case or anything under it.

| User role | Can open a case when | Everything else |
| --- | --- | --- |
| Lawyer | Always | Can open settings and staff accounts |
| Staff | `case_staff` has a row for this user and case | No settings, no staff accounts |
| Client | `case.client_id` equals the client linked to this user | Only the client area; staff pages return 404 |

Rules:

- Documents, document requests, updates and bills inherit the case rule. The chat uses the client: a Lawyer always, a Staff user when assigned to any of that client's cases, a Client user only their own.
- Every list query is filtered by the same rule, so a Staff user's case list is built from `case_staff`, not from all cases with buttons hidden.
- Access failures return 404, not 403, so a client cannot learn that another case id exists.
- File downloads go through the app. The app checks the rule, then redirects to a Blob URL that expires in a few minutes.
- Razor Pages for staff live under `/Staff/` and require the Lawyer or Staff role; client pages live under `/Portal/` and require the Client role. The folder-level policy is the first gate; `CanAccessCase` is the second.

## Key technical flows

File upload:

1. The browser asks the app for an upload token, naming the case (and the document request, if any).
2. The app checks access and asks Vercel Blob for a client-upload token limited to that path, the allowed types (PDF, DOCX, JPG, PNG) and 20 MB.
3. The browser uploads the file straight to Blob and shows progress.
4. Blob calls the app's completion endpoint (or the browser posts the result); the app writes the `documents` row, updates the document request to Received, and creates notifications.

Notifications and email:

1. Any event in the Notifications table of the Functional Specification calls `Notify(userIds, kind, title, link)`.
2. `Notify` writes one `notifications` row per user. For Client users it also sends one email through SMTP, in the user's language, with the title and a login link only.
3. Email sending happens in the same request, with a short timeout. If SMTP fails, the error is logged and the in-system notification still stands. There is no background queue in version 1.

Login and two-factor:

1. ASP.NET Identity handles registration (staff created by a lawyer, clients created by staff), set-password links, lockout after 5 failed attempts, and password reset.
2. Two-factor uses an authenticator app (TOTP). It is required for Lawyer accounts and optional for others. Recovery codes are shown once.
3. Sessions use the Identity cookie, 14 days, sliding, secure and HTTP-only.

Languages:

1. Every piece of screen text, email text and validation message is a key in `.resx` files for `en` and `ms`.
2. The user's `language` column sets the culture for every request after login; before login, the browser's language is used with a switcher on the page.
3. Dates show as `dd/MM/yyyy`, amounts as `RM 1,234.50`, in both languages.

Bill totals:

- The total is the sum of lines, computed in one place (`Bill.Recalculate()`), stored on the bill and shown with two decimals. Marking a bill Sent freezes its lines; only the status can change after that.

## Development environment

All work happens inside containers. The laptop needs only Docker Desktop, VS Code and the Dev Containers extension.

- `.devcontainer/devcontainer.json` opens the repository inside the app container, which has the .NET 10 SDK, Node (for Tailwind), the `dotnet-ef` tool and the Vercel CLI.
- `docker-compose.yml` runs two services: `app` (the dev container) and `db` (PostgreSQL 16 with a named volume). The app reaches the database at `db:5432`.
- `dotnet watch` runs the site with hot reload on port 5000; Tailwind runs in watch mode beside it.
- `.env.example` lists every variable. `.env` is git-ignored.
- Seed data: one command creates the firm settings, two lawyer accounts, one staff account and one sample client with a case, so the app is usable immediately.
- Repository layout: `src/ClientHub.Web` (the app), `tests/ClientHub.Tests` (xUnit), `.devcontainer/`, `.github/workflows/`, `Dockerfile.vercel`, `vercel.json`, `docker-compose.yml`.

## CI and deployment

A push to `main` deploys to production. Nothing reaches `main` without a green pipeline and Fiqri's merge.

- Repository: Fiqri's personal GitHub, private. Branch protection on `main`: pull request required, CI must pass, only Fiqri can merge. Others may clone and push branches.
- GitHub Actions on every push and pull request: restore, build, run xUnit tests against a PostgreSQL service container, then build `Dockerfile.vercel` to prove the image compiles.
- Vercel is connected to the repository. Every branch gets a preview URL; `main` is production. `vercel.json` declares one service with `runtime: "container"` and `Dockerfile.vercel`, and a catch-all rewrite to it.
- `Dockerfile.vercel` is two-stage: SDK image restores and publishes, ASP.NET runtime image carries only the published output. The app binds to `0.0.0.0` on the `PORT` environment variable; `PORT` is set to 8080 in Vercel project settings to avoid the privileged-port problem.
- Secrets (database connection string, Blob token, SMTP password encryption key) are Vercel environment variables, separate for preview and production.
- EF Core migrations run at startup. A preview deployment uses a Neon branch of the database, so it never touches production data.
- Logging goes to the console only, which Vercel captures as runtime logs.

## Known limits

- Vercel's Hobby plan is for personal, non-commercial use. The firm uses it anyway for now and moves to the Pro plan (USD 20 a month, paid by the firm) once the system is in daily use.
- The container sleeps after 5 minutes without traffic. The first page after a quiet period takes a few seconds to load.
- No always-on process, so no timers or background jobs in the app. Version 2 reminders run through Vercel Cron calling an endpoint.
- No persistent disk in the container. Nothing is written to the file system except temporary files.
- Request and response bodies are capped at 4.5 MB, which is why uploads go straight to Blob.
- The free Neon tier pauses the database after inactivity, adding to the first-request delay.
