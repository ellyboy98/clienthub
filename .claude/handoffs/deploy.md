# Deploy guide (Fiqri, by hand)

Nothing here has been done for you. Do the steps in order. Production origin below is written `https://PROD_HOST`; replace it with your real domain (or the `*.vercel.app` production URL).

## 1. Cloudflare R2

1. Cloudflare dashboard > R2 > add a payment method if asked (free tier is enough).
2. Create three buckets, all private (leave "Public access" off, do not attach a custom domain):
   - `clienthub-files` (production documents)
   - `clienthub-files-preview` (preview deployments)
   - `clienthub-backups` (backups only)
3. CORS, on both `clienthub-files` and `clienthub-files-preview` (Bucket > Settings > CORS policy):
   ```json
   [
     {
       "AllowedOrigins": ["https://PROD_HOST"],
       "AllowedMethods": ["PUT", "GET"],
       "AllowedHeaders": ["content-type"],
       "ExposeHeaders": ["ETag"],
       "MaxAgeSeconds": 3600
     }
   ]
   ```
   For `clienthub-files-preview` use the preview origin(s) instead (for example `https://clienthub-git-build-v1-YOURTEAM.vercel.app`); add one entry per preview host you test on.
4. Lifecycle rules:
   - `clienthub-files`: prefix `staging/`, delete objects after 1 day (covers abandoned or refused uploads). Add the same rule on `clienthub-files-preview`.
   - `clienthub-backups`: prefix `db/`, delete objects after 30 days. Do NOT put a rule on `files/`.
5. API tokens (R2 > Manage API tokens > Create API token). Create four. For each: pick the permission, choose "Apply to specific buckets only", select the buckets, leave TTL as you prefer, Create, and copy the Access Key ID and Secret at once (the secret is shown once). Never use an "Admin" token.
   - Production app token: permission "Object Read & Write", bucket `clienthub-files` only. Gives Production `S3_ACCESS_KEY_ID` / `S3_SECRET_ACCESS_KEY`.
   - Preview app token: "Object Read & Write", bucket `clienthub-files-preview` only. Gives Preview `S3_ACCESS_KEY_ID` / `S3_SECRET_ACCESS_KEY`.
   - Files read token (backups): "Object Read only", bucket `clienthub-files` only. Gives GitHub secrets `R2_FILES_READ_ACCESS_KEY_ID` / `R2_FILES_READ_SECRET_ACCESS_KEY`. It cannot change or delete documents.
   - Backup write token: "Object Read & Write", bucket `clienthub-backups` only. Gives GitHub secrets `R2_BACKUP_ACCESS_KEY_ID` / `R2_BACKUP_SECRET_ACCESS_KEY`. (Read is needed so the job can verify its own upload.)
   Why: a leaked GitHub secret must not be able to change or delete client documents.
6. Note your Account ID (R2 overview page, right side). The S3 endpoint is `https://<ACCOUNT_ID>.r2.cloudflarestorage.com`.

## 2. Neon

Created from Vercel (step 3) via the Marketplace integration, which also sets `DATABASE_URL`. Afterwards, in the Neon console:
- Note the PostgreSQL major version (Project > Settings). The backup workflow installs `postgresql-client-17` (`PGMAJOR` in `.github/workflows/backup.yml`). If your project is not 17, edit `PGMAJOR` before the first backup run.
- Copy the **direct (non-pooled)** connection string (turn off "Connection pooling" in the Connect dialog). That goes in the GitHub secret `BACKUP_DATABASE_URL`.
- Previews must NEVER get production data. Do not enable "branch per preview deployment" and do not branch from the production branch. Give Preview its own `DATABASE_URL`: create a separate Neon project (for example `clienthub-preview`), or a new root branch created empty (not branched from production). It is filled only by the seed command. Set that URL as the Preview-only `DATABASE_URL` in Vercel.

## 3. Vercel

1. Add New > Project > import the GitHub repo. Framework preset: Other. `vercel.json` already points at `Dockerfile.vercel`.
2. Storage tab > Neon Postgres (Marketplace) > connect the production Neon project to the **Production** environment only. Then add `DATABASE_URL` for **Preview** by hand with the connection string of the separate preview project/branch from section 2 (the integration must not supply Preview).
3. Settings > Environment Variables. Set each below; Production and Preview get separate values where marked.

| Variable | Production | Preview | Value comes from |
| --- | --- | --- | --- |
| `PORT` | `8080` | `8080` | fixed |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Production` | fixed (Vercel does not set it; without it the app treats itself as Production anyway, but set it explicitly) |
| `DATABASE_URL` | production Neon project | separate preview Neon project (empty, seeded only) | Production: Neon integration; Preview: you add it by hand |
| `APP_BASE_URL` | `https://PROD_HOST` | the preview URL, see note | your domain. **Mandatory in Production: the app will not start without it.** |
| `AllowedHosts` | leave unset | optional, see note | the host name only |
| `S3_ENDPOINT` | `https://<ACCOUNT_ID>.r2.cloudflarestorage.com` | same | R2 step 6 |
| `S3_PUBLIC_ENDPOINT` | same as `S3_ENDPOINT` | same | R2 step 6 (browsers sign against the real endpoint) |
| `S3_REGION` | `auto` | `auto` | fixed for R2 |
| `S3_BUCKET` | `clienthub-files` | `clienthub-files-preview` | R2 step 2 |
| `S3_ACCESS_KEY_ID` | app token | preview app token | R2 step 5 |
| `S3_SECRET_ACCESS_KEY` | app token secret | preview app token secret | R2 step 5 |
| `REQUIRE_FIRM_2FA` | `true` | `true` | fixed |
| `SEED_PASSWORD` | not needed on Vercel | not needed | only used when you run the seed (below) |
| `SECURITY_STAMP_INTERVAL_SECONDS` | optional | optional | default 60 |

   Do not set `POSTGRES_*`, `ConnectionStrings__Default` (dev only; if both are set `ConnectionStrings__Default` wins over `DATABASE_URL`).

   Preview note: the app is Production-mode on preview too, so it checks the Host header against the `APP_BASE_URL` host. Preview URLs change per branch, so either (a) set a preview-scoped `APP_BASE_URL` to a stable preview URL (for example the branch alias URL for `build/v1`), or (b) set preview-scoped `AllowedHosts=*` and `APP_BASE_URL` to any valid URL (links in preview emails will point there). Option (b) turns off host filtering, which is acceptable only because the preview database and bucket are separate and hold only seeded test data (sections 1 and 2); never point Preview at production data or `clienthub-files`.

   SMTP is not an env var. Sign in as a Lawyer and enter it on the Settings page: host, port 587 (STARTTLS) or 465 (implicit TLS), user, password. The password is encrypted with ASP.NET data-protection keys stored in the database.
4. Trigger the first deploy (push or "Redeploy"). Migrations run at startup. Open `https://PROD_HOST/health`; expect `{"status":"ok"}`. Cold start after 5 idle minutes takes a few seconds. Also check the `vercel.json` catch-all rewrite: open a deep link such as `https://PROD_HOST/Account/Login` directly; it must show the login page, not the home page. If it shows home, fix the rewrite before going on.
5. Seed (once, from your laptop dev container, against the production database; the app image has no shell hook for this). Use the Neon **direct** connection string:
   ```
   docker compose exec -e ConnectionStrings__Default="<NEON_URL>" -e SEED_PASSWORD="<strong one-time password>" \
     -e ASPNETCORE_ENVIRONMENT=Development app dotnet run --project src/ClientHub.Web -- seed
   ```
   (Development here only relaxes the APP_BASE_URL requirement for this one command.) Seed creates the firm settings, two Lawyers, one Office Manager, one Staff and a sample client with a case. Sign in, set up 2FA, change every password, then deactivate the sample accounts you do not need. Do not run it twice.

## 4. GitHub

1. Settings > Secrets and variables > Actions > New repository secret (seven in all):

| Secret | Value comes from |
| --- | --- |
| `BACKUP_DATABASE_URL` | Neon direct (non-pooled) connection string, step 2 |
| `BACKUP_PASSPHRASE` | a long random passphrase you generate; store a copy in your password manager, the backups are unreadable without it |
| `R2_ACCOUNT_ID` | R2 step 6 |
| `R2_BACKUP_ACCESS_KEY_ID` | backup write token, R2 step 5 |
| `R2_BACKUP_SECRET_ACCESS_KEY` | backup write token, R2 step 5 |
| `R2_FILES_READ_ACCESS_KEY_ID` | files read token, R2 step 5 |
| `R2_FILES_READ_SECRET_ACCESS_KEY` | files read token, R2 step 5 |

2. Settings > Branches > add rule for `main`: require a pull request, require status check "build-test" (CI) to pass, restrict who can push/merge to you.
3. Open the PR `build/v1` into `main`, wait for green CI, merge. (Scheduled workflows run only from `main`.)
4. Actions > Backup > Run workflow, once by hand. Check `s3://clienthub-backups/db/<date>.dump.gpg` and `files/` exist in R2. GitHub emails you if a scheduled run later fails.

## 5. Restore test (do once, then keep the steps)

The workflow already verifies each backup (decrypts it and runs `pg_restore --list`), but a real restore must be tried once.

1. Download `db/<date>.dump.gpg` from the `clienthub-backups` bucket (dashboard, or `aws s3 cp ... --endpoint-url https://<ACCOUNT_ID>.r2.cloudflarestorage.com`).
2. Decrypt with any gpg 2.2 or newer (the file is plain symmetric AES256, no AEAD): `gpg --decrypt --output restore.dump <date>.dump.gpg` (enter `BACKUP_PASSPHRASE`).
3. Create a new EMPTY Neon target: a new project (for example `restore-test`) or a new root branch created empty. Do not branch from production and do not restore into production or the preview database. Copy its direct connection string.
4. `pg_restore --no-owner --dbname="<empty-target-url>" restore.dump` (use a `pg_restore` of the same major version as Neon; add `--clean --if-exists` only when re-running into the same target).
5. Check: `psql "<empty-target-url>" -c "select count(*) from clients;"` and the same for `cases`, `documents`, `messages`, `bills`; compare with production (`\dt` lists the snake_case table names).
6. Delete the target project/branch and `restore.dump` afterwards (it is unencrypted personal data). For a real restore, point `DATABASE_URL` at the restored database and redeploy.

Notes
- Hobby plan is non-commercial; move to Pro once the firm uses it daily.
- Files bucket is mirrored with `aws s3 sync` without `--delete`, so deletions in the app bucket never propagate to backups.
