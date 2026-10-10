# Client Management Hub

A client portal for one small law firm (two lawyers, an Office Manager, one or two staff and fewer than ten clients). Staff manage clients, cases, documents, bills and a per-client chat; clients sign in to a mobile-friendly portal to follow their cases, upload requested documents, read bills and message the firm. English and Bahasa Melayu. ASP.NET Core 10 Razor Pages, Tailwind, HTMX, PostgreSQL (Neon in production), S3-compatible storage via presigned URLs, SMTP email. Specs are in `docs/` (FSD, TSD, UI notes).

## Run locally

Everything runs in containers; the host needs only Docker.

```
cp .env.example .env
docker compose up -d                       # app, db (postgres 16), storage (SeaweedFS), storage-init (buckets + CORS)
docker compose exec app npm ci --prefix src/ClientHub.Web
docker compose exec app npm run css:build --prefix src/ClientHub.Web
docker compose exec app dotnet run --project src/ClientHub.Web -- seed     # migrates, then seeds; uses SEED_PASSWORD from .env
docker compose exec app dotnet watch --project src/ClientHub.Web           # http://localhost:5000
```

Seeded logins (all use `SEED_PASSWORD`, default `ChangeMe-Dev-123!`): `lawyer1@clienthub.test`, `lawyer2@clienthub.test`, `om@clienthub.test`, `staff@clienthub.test`, `client@clienthub.test`.

Lawyers and the Office Manager must set up 2FA before using the staff area. For local manual checks set `REQUIRE_FIRM_2FA=false` in `.env`.

Dev file storage is SeaweedFS (S3 API on `http://localhost:9000`); production uses Cloudflare R2. Only the `S3_*` variables differ.

Tests (xUnit, need the compose `db` and `storage` services):

```
docker compose exec app dotnet test
```

## Deploy

Pushing to `main` deploys to production on Vercel as a container built from `Dockerfile.vercel` (`vercel.json` declares the service). Pull requests and branches get preview deployments. CI (`.github/workflows/ci.yml`) restores, builds, tests against Postgres and SeaweedFS service containers and builds the image. Database migrations run at startup. Production needs `APP_BASE_URL` or the app will not start. The one-time setup (R2 buckets, CORS, lifecycle rules, Neon, Vercel env vars, GitHub secrets, first seed) is a step-by-step guide in [`.claude/handoffs/deploy.md`](.claude/handoffs/deploy.md).

## Backups

`.github/workflows/backup.yml` runs daily at 02:00 Malaysia time (18:00 UTC) and on demand. It takes a `pg_dump --format=custom` of the production database, encrypts it with `gpg` (AES256, `BACKUP_PASSPHRASE`), uploads it to the R2 bucket `clienthub-backups` as `db/<date>.dump.gpg` (a lifecycle rule deletes these after 30 days), and mirrors the files bucket to `files/` in the same bucket (no deletes). Neon's point-in-time restore covers "undo the last few hours". To restore: download the dump, `gpg --decrypt`, `pg_restore` into a new Neon branch, check row counts, then switch `DATABASE_URL`. Exact steps are in [`.claude/handoffs/deploy.md`](.claude/handoffs/deploy.md), section 5.
