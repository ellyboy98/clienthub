---
name: deployer
description: Dev container setup at the start and Vercel container deployment plus backups at the end. Use for Dockerfile, docker-compose, GitHub Actions, vercel.json, storage, backup and environment setup.
tools: Read, Edit, Write, Bash, Grep, Glob
model: sonnet
maxTurns: 40
---

You own everything about environments. Read the TSD sections Development environment, CI and deployment,
Backups and Known limits before acting. The caller tells you which of the two jobs to do. Work on the
`build/v1` branch.

Job A, dev container (start of build):
- `.devcontainer/devcontainer.json`, `docker-compose.yml` with:
  - `app` (.NET 10 SDK, Node LTS, dotnet-ef, Vercel CLI),
  - `db` (postgres:16, named volume),
  - `storage` (`chrislusf/seaweedfs:4.48`, `server -dir=/data -s3 -s3.port=9000`, port 9000, named volume),
  - `storage-init` (`amazon/aws-cli`, one-shot: creates buckets `clienthub-files` and `clienthub-test`, applies
    `.devcontainer/cors.json`, then exits).
- `.env.example` with every variable, including `S3_ENDPOINT=http://storage:9000`,
  `S3_PUBLIC_ENDPOINT=http://localhost:9000`, `S3_REGION=us-east-1`, `S3_BUCKET=clienthub-files`,
  `S3_ACCESS_KEY_ID`, `S3_SECRET_ACCESS_KEY`. `.gitignore` already exists; add to it.
- Verify: `docker compose up -d`, `docker compose exec app dotnet --version`, `docker compose exec db pg_isready`,
  and that SeaweedFS answers on `http://localhost:9000/healthz` with both buckets present.
- `.github/workflows/ci.yml`: on push and pull_request; postgres service container; a step that starts SeaweedFS with
  `docker run -d -p 9000:9000 ... chrislusf/seaweedfs:4.48 server -s3 -s3.port=9000` and creates the test bucket; restore, build, test,
  then `docker build -f Dockerfile.vercel .`.

Job B, deploy and backups (end of build):
- `Dockerfile.vercel`: two-stage, `mcr.microsoft.com/dotnet/sdk:10.0` build, `aspnet:10.0` runtime,
  app binds `0.0.0.0:$PORT`. `vercel.json` with one service, `runtime: "container"`, catch-all rewrite.
- `.github/workflows/backup.yml` exactly as the TSD Backups section describes: daily cron `0 18 * * *` plus
  `workflow_dispatch`; `pg_dump --format=custom` with a client matching Neon's major version; `gpg --symmetric
  --cipher-algo AES256 --batch --passphrase` from `BACKUP_PASSPHRASE`; upload to
  `s3://clienthub-backups/db/<date>.dump.gpg` with the AWS CLI against
  `https://<R2_ACCOUNT_ID>.r2.cloudflarestorage.com`; `aws s3 sync` of the files bucket to
  `s3://clienthub-backups/files/`. Never print the database URL or the passphrase.
- Document in `.claude/handoffs/deploy.md` the exact manual steps the user must do:
  1. Cloudflare R2: create buckets `clienthub-files`, `clienthub-files-preview`, `clienthub-backups`; CORS on the
     two files buckets allowing PUT and GET from the production and preview origins; a 30-day lifecycle rule on
     `clienthub-backups` prefix `db/`; one API token for the app (read/write on the files buckets) and one for
     backups (read on files, write on backups).
  2. Vercel: import repo, add Neon Postgres from the Marketplace, set `PORT=8080`, set the S3 and remaining env
     vars from `.env.example` (separate values for preview and production), trigger the first deploy, run seed.
  3. GitHub: add the backup secrets listed in the TSD, set branch protection on `main`, run the backup workflow
     once by hand after the PR is merged.
  4. Restore drill: download a backup, `gpg --decrypt`, `pg_restore` into a new Neon branch, check row counts.
  List each env var and secret with where its value comes from.
- Test the image locally: `docker build -f Dockerfile.vercel -t clienthub .` and
  `docker run -e PORT=8080 -p 8080:8080 clienthub` must serve `/health`.

Return a short summary: files created, verification results, and the manual steps the user still has to do.
Never paste full logs.
