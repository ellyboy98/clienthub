# Review: M1 Job A foundation (uncommitted, build/v1)

Scope: `git diff` plus untracked files from M1. Dev/CI storage is SeaweedFS instead of MinIO (decisions.md, approved).
Checked: no secrets committed, `.env` is git-ignored (`git check-ignore` confirms) and nothing else ships it, `.dockerignore` excludes `.env`.
The data model matches the 13 TSD tables (snake_case, `numeric(12,2)`, `jsonb` details, Restrict FKs, nothing cascades). Seed is idempotent.
Anonymous S3 GET and PUT on the dev bucket return 403 (bucket is private). The app binds `0.0.0.0:$PORT`. Migrations run at startup.
Access control, localisation and UI checks do not apply yet: M1 has no feature pages.

## Critical

None.

## Should fix (next milestone)

- src/ClientHub.Web/Data/DataServiceExtensions.cs:14: Data Protection keys are not persisted. Identity tokens, auth cookies (M2), upload tickets (M4) and possibly the SMTP password would use keys stored in the container's own file system. These keys are lost on every Vercel instance and redeploy, which also breaks R-121. Fix: in M2, persist keys with `PersistKeysToDbContext` (add a `data_protection_keys` table and record it in decisions.md as a 14th table) or derive them from an env secret.
- src/ClientHub.Web/Data/Entities/Domain.cs:125: The comment says the SMTP password uses "Data Protection / SMTP_ENCRYPTION_KEY", so the method is unresolved. Fix: choose one before M6 (the TSD implies `SMTP_ENCRYPTION_KEY`, for example AES-GCM) and record it in decisions.md.
- src/ClientHub.Web/Data/Entities/Base.cs:15: `AppUser.Role` (the TSD column) and Identity `user_roles` are two sources of truth for the role. Fix: in M2, write both only through one user-creation/role-change helper, and have `IAccessService` read one of them.
- Dockerfile.vercel:22: The runtime stage runs as root. Fix: add `USER $APP_UID` before `ENTRYPOINT` (the .NET 8+ aspnet images include the `app` user).
- Dockerfile.vercel:10: `npm install` ignores the committed `package-lock.json`, so image builds are not reproducible. Fix: use `npm ci --no-audit --no-fund`.
- .claude/handoffs/requirements.md:139, :141 and plan.md:13, :22, :26, :75, :83, :154: These still say MinIO (and "MinIO console"), which drifts from the SeaweedFS decision and will mislead the M4 developer and tester. Fix: replace MinIO with SeaweedFS (S3 API on :9000, no console) in the handoffs.

## Nice to have

- .github/workflows/ci.yml:42: The SeaweedFS wait loop never fails on its own, so a dead container only shows up later as a confusing create-bucket error. Fix: after the loop, add `curl -sf http://localhost:9000/healthz || { docker logs storage; exit 1; }`.
- .github/workflows/ci.yml:3-4: `on: push` and `pull_request` both run with no filter, so each PR commit runs CI twice. Fix: limit `push` to `branches: [main, build/v1]`.
- Dockerfile.vercel:7: Copying the test csproj into the build stage is unused, because only the web project is restored. Fix: remove the line.
- .gitignore:1: Only `.env` is ignored, so variants like `.env.local` or `.env.production` would be committed. Fix: add `.env.*` and `!.env.example`.
- .devcontainer/devcontainer.json:7: It forwards port 9001, a leftover MinIO console port that SeaweedFS does not serve. Fix: remove 9001.
- src/ClientHub.Web/Data/AppDbContext.cs:110: `audit_log.user_id` has no FK to `users`. Fix: add `HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(Restrict)` in the next migration.
- src/ClientHub.Web/Pages/Index.cshtml:3-4: The placeholder page has hard-coded English text. Fix: replace it with the localised shell in M2, as planned.
- src/ClientHub.Web/Program.cs (no appsettings.json): Default logging is Information for every category, so EF logs every SQL command to Vercel logs. Fix: add appsettings.json with `Microsoft.EntityFrameworkCore.Database.Command: Warning`, and never enable sensitive data logging.
- .claude/handoffs/status.md:20: It says Q-01..Q-22 are "pending approval", but decisions.md records them as approved. Fix: update the line.
