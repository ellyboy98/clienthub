# Client Management Hub

A law-firm client portal for one small firm (2 lawyers, 1 Office Manager, 1–2 staff, under 10 clients).
ASP.NET Core 10 Razor Pages + Tailwind + HTMX, PostgreSQL (Neon), S3-compatible file storage
(Cloudflare R2; SeaweedFS in dev) via presigned URLs, SMTP email, deployed as a Docker container on Vercel.
English (default) and Bahasa Melayu. Roles: Lawyer, OfficeManager, Staff, Client.

## Source of truth

- `docs/FSD-Functional-Specification.md` — WHAT the system does. Every feature, rule, screen.
- `docs/TSD-Technical-Specification.md` — HOW it is built. Stack, data model, access control, flows, deploy, backups.
- `docs/UI-Design-Notes.md` — how every screen LOOKS. Tokens, components, screen layouts. Wins over the
  Figma file (`docs/Figma-Design.md`) where they differ.
- `.claude/handoffs/` — the running state of this build (plan, status, test reports). Read before acting.

If the specs and the code disagree, the specs win. If the specs are silent, ask once, then record the
decision in `.claude/handoffs/decisions.md`. Never invent a feature that is not in the FSD.

## Working rules

- Read `.claude/handoffs/status.md` at the start of every session and after every `/compact`.
- Work on the `build/v1` branch, never on `main`. Work milestone by milestone from `.claude/handoffs/plan.md`.
  One milestone = one commit on `build/v1`. At the end, push `build/v1`; Fiqri opens one pull request into
  `main` and merges it once CI is green.
- Delegate by role (see Agents). The main conversation coordinates; it does not read test logs or
  explore the codebase itself. Keep the main context small.
- All commands run inside the dev container (`docker compose exec app ...`). Nothing is installed on the host.
- Never commit secrets. `.env` is git-ignored. `.env.example` lists every variable.
- Every list query and every page handler goes through `CanAccessCase` (or `CanAccessClient` for the chat
  and client records). Access failures return 404.
- Files never pass through the app: presigned PUT to upload, presigned GET (5 min) to download. Nothing is
  deleted; removed documents get `is_active = false`.
- Bill totals are computed in `Bill.Recalculate()` only.
- UI text goes in `.resx` files for `en` and `ms`. No hard-coded user-facing strings.
- UI follows `docs/UI-Design-Notes.md`: Tailwind colours named as its tokens, its components as partials,
  one gold action per screen, 44px tap targets, every portal page checked at 390px.
- Tests: xUnit in `tests/ClientHub.Tests`. Access rules, bill totals and notification triggers must have tests.
- Commit messages: `feat:`, `fix:`, `test:`, `chore:`, `docs:` prefixes, one line, present tense.

## Agents and how they talk to each other

Agents do not share context. They communicate through files in `.claude/handoffs/` and through the
main conversation. Each agent reads its inputs and writes its outputs there:

| Agent | Reads | Writes |
| --- | --- | --- |
| requirements-analyst | docs/FSD, docs/TSD, docs/UI-Design-Notes | handoffs/requirements.md (numbered requirement list, open questions) |
| planner | handoffs/requirements.md, docs/TSD | handoffs/plan.md (milestones, each with files, tests, done-criteria) |
| developer | handoffs/plan.md (one milestone), handoffs/status.md, the UI-Design-Notes sections for that milestone's screens | code, tests, handoffs/status.md (what was done, what is left) |
| tester | handoffs/plan.md milestone done-criteria, code | handoffs/test-report.md (failures only, with file:line and cause) |
| reviewer | git diff of the milestone | handoffs/review.md (Critical / Should fix / Nice to have) |
| deployer | handoffs/status.md, TSD deploy and backup sections | Dockerfile.vercel, vercel.json, CI and backup workflows, handoffs/deploy.md |

Flow for every milestone:
`developer` → `tester` → (failures? `developer` fixes, `tester` re-runs) → `reviewer` → commit → next milestone.
`requirements-analyst` and `planner` run once at the start. `deployer` runs once at the end, plus the
dev-container setup at the start.

## Budget rules (5-hour usage window)

- Start in plan mode. Approve the plan once, then let it run.
- Explore runs on Haiku (see `.claude/agents/Explore.md`). Tester and deployer run on Sonnet.
  Developer, planner and reviewer inherit the main model.
- Subagents return summaries, never logs. The tester reports failing tests only.
- `/compact` after every milestone commit. Do not `/clear`.
- No re-reading of the FSD and TSD once `handoffs/requirements.md` and `handoffs/plan.md` exist. The
  developer still reads the UI-Design-Notes sections for the screens in its milestone (short, and not
  copied into the handoffs).
- Run `/usage` at every milestone boundary. If remaining usage is below 20%, stop adding features:
  finish the current milestone, run tests, commit, write `handoffs/status.md`, and deploy what exists.
- If something fails twice the same way, stop and ask instead of retrying.
