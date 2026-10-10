# Start here

Everything for the Client Management Hub project is in this folder.

| Folder / file | What it is |
| --- | --- |
| docs/FSD-Functional-Specification.md | What the system does (the firm's requirements, screens, rules) |
| docs/TSD-Technical-Specification.md | How it is built (stack, data model, access control, flows, deploy) |
| docs/UI-Design-Notes.md | Colours, type, components and every screen, for the Tailwind build |
| docs/Figma-Design.md | Link to the Figma file and what it contains (UI-Design-Notes wins where they differ) |
| CLAUDE.md | Instructions Claude Code reads automatically in this repo |
| .claude/agents/ | Seven subagents: analyst, planner, developer, tester, reviewer, deployer, Explore |
| .claude/skills/clienthub-conventions/ | Coding conventions preloaded into the developer agent |
| .claude/settings.json | Pre-approved commands, denies, subagent caps |
| .claude/handoffs/ | Where the agents pass work to each other |
| KICKOFF.md | The 5-hour plan and the exact prompts to paste |

## Steps, in order

1. Install Docker Desktop, VS Code and the "Dev Containers" extension. Install Claude Code (`npm install -g @anthropic-ai/claude-code`) and sign in.
2. Done: the repo `clienthub` holds this kit, committed on `main`. Push `main`, then `git switch build/v1` and push that too. All build work happens on `build/v1`; it reaches `main` through one pull request.
3. Open the folder in a terminal and run `claude`. Accept the trust prompt.
4. Run `/usage` and start when the 5-hour window is fresh. Run `/agents` and confirm the seven agents are listed.
5. Press Shift+Tab until the mode says "plan", then paste Prompt 1 from KICKOFF.md. Read the plan it returns. Cut what you don't need.
6. Exit plan mode and paste Prompt 2. Let it run. Answer only when it asks.
7. After the build, open the pull request `build/v1` → `main` and merge it once CI passes. Then follow `.claude/handoffs/deploy.md` to set up Cloudflare R2, Vercel, Neon, the backup job and SMTP by hand, then deploy.
8. Create the two lawyer accounts and the Office Manager account, then hand the link to your sister.

If the window runs out, start a new session and paste the resume prompt from KICKOFF.md.
