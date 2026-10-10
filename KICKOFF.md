# Kickoff: building Client Management Hub in one 5-hour Claude Code window

## Before you start (10 minutes, no Claude Code usage)

1. Done: the kit is in the repo root and committed on `main`. Push `main` (`git push -u origin main`), then
   switch to the build branch and push it too (`git switch build/v1 && git push -u origin build/v1`).
   All build work happens on `build/v1`.
2. Install Docker Desktop and VS Code with the Dev Containers extension. Nothing else.
3. Open the repo folder, run `claude`. Trust the folder when asked (needed for project agents and settings).
4. Run `/usage` once and note the reset time of your 5-hour window. Start when the window is fresh.
5. Run `/agents` and confirm all seven agents are listed (Explore, requirements-analyst, planner, developer,
   tester, reviewer, deployer).

## The plan (time boxes, not promises)

| Phase | Time | Who | Output |
| --- | --- | --- | --- |
| 1. Requirements | 0:00–0:15 | requirements-analyst | handoffs/requirements.md |
| 2. Plan | 0:15–0:30 | planner, then you approve | handoffs/plan.md |
| 3. Environment | 0:30–0:50 | deployer (Job A) | dev container, CI, skeleton builds |
| 4. Build | 0:50–3:40 | developer → tester → reviewer per milestone | code + tests, one commit per milestone |
| 5. Hardening | 3:40–4:15 | tester full pass, developer fixes | green suite, access tests |
| 6. Deploy | 4:15–4:45 | deployer (Job B) | Dockerfile.vercel, vercel.json, backup.yml, deploy.md |
| 7. Handoff | 4:45–5:00 | main conversation | status.md final, README, push `build/v1` |

Check `/usage` at the end of every milestone. Below 20% remaining: skip to phase 5.

## Prompt 1 (paste this first, in plan mode: press Shift+Tab until it says plan)

```
Read CLAUDE.md. We are building this project in one session, on the build/v1 branch.

Step 1: Use the requirements-analyst agent to produce .claude/handoffs/requirements.md from docs/.
Step 2: Use the planner agent to produce .claude/handoffs/plan.md from it.
Then show me the milestone list with time boxes and the cut list, and stop for my approval.
Do not write any code yet.
```

Review the plan. Cut anything you don't need. Then exit plan mode and send Prompt 2.

## Prompt 2 (the build loop)

```
Plan approved. Run the build loop:

1. Use the deployer agent for Job A (dev container, CI, solution skeleton). Verify the container builds.
2. For each milestone in order:
   a. Use the developer agent for that milestone only.
   b. Use the tester agent. If it reports failures, send them back to the developer (resume the same
      developer agent) until the tester reports PASS. Maximum two fix rounds per milestone; if still failing,
      stop and ask me.
   c. Use the reviewer agent. Fix Critical items with the developer, then commit to build/v1 with a feat: message.
   d. /compact, then tell me in three lines: milestone done, tests passing, /usage remaining.
3. After the last feature milestone, use the tester agent for a full pass, then the deployer agent for Job B.
4. Update .claude/handoffs/status.md and write a short README.md (how to run locally, how to deploy,
   how backups work). Commit, then push build/v1. I will open the pull request into main myself.

Rules: never paste logs into this conversation, keep your own messages to me short, and ask me only when
something has failed twice or a spec question blocks a milestone.
```

## If the window runs out mid-build

Everything needed to resume is on disk: `handoffs/status.md`, `plan.md`, `test-report.md`, and committed code.
In the next window, start a new session and paste:

```
Read CLAUDE.md and .claude/handoffs/status.md. Make sure you are on build/v1. Continue the build loop from the next unfinished milestone.
```

## Why this setup saves usage

- Agents keep logs, file contents and exploration out of the main conversation. The main thread only
  routes and commits.
- Explore runs on Haiku; tester and deployer run on Sonnet. Only developer, planner and reviewer use the
  main model.
- Specs are read once by the analyst. Every later agent reads the short handoff files instead.
- The conventions skill is preloaded into the developer, so it does not rediscover the project shape every run.
- `/compact` after each commit keeps the context from ballooning across 6–8 milestones.
- Subagent depth is capped at 1 and concurrency at 3 in settings.json, so no runaway nesting.

## Things to do by hand after the build (outside Claude Code)

- Open the pull request `build/v1` → `main`, wait for CI to pass, merge. Then turn on branch protection for `main`.
- Cloudflare R2: create the files, preview and backup buckets, CORS, lifecycle rule and two API tokens
  (exact steps in `handoffs/deploy.md`).
- Vercel: import the repo, add Neon Postgres from the Marketplace, set env vars from `.env.example` and
  `handoffs/deploy.md`, set `PORT=8080`, deploy.
- GitHub: add the backup secrets, run the backup workflow once by hand, do the restore drill once.
- Brevo or Gmail: create the SMTP credentials, then enter them on the app's Settings page.
- Create the two lawyer accounts and the Office Manager account, then let the Office Manager create the first client.
