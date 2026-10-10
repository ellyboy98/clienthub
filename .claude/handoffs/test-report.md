# Test report: M1

## Result
PASS. dotnet test: 1 passed, 0 failed.

## Failures
None.

## Done-criteria not met
None. Verified: compose up (app, db, SeaweedFS healthy, bucket clienthub-files created, /healthz 200); seed run twice gives 2 Lawyer, 1 OfficeManager, 1 Staff, 1 Client, 1 case, 1 firm_settings (no duplicates); app answers 200 on :5000 with migrations applied; tailwind tokens match UI-Design-Notes; ci.yml has restore/build/test with PostgreSQL and SeaweedFS plus docker build; Dockerfile.vercel builds; .env.example complete, .env git-ignored.

## Invariant breaches
None applicable yet (no case pages, UI strings or Bill.Recalculate in M1).
