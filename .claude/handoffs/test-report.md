## Result
PASS. Full suite 133/133, run twice. Live checks (dev app, curl) all as expected.

## Failures
None.

## Done-criteria not met
None. Live: client send -> pill 1; poll visible=0 returns message, pill stays 1; poll visible=1 returns it, pill clears; chat file upload (Start kind=Chat, PUT 200, Complete with caption) shows caption to staff and client; download 302 to presigned URL, bytes identical, attachment filename f.png; unassigned staff gets 404 on page, poll and download.

## Invariant breaches
None checked beyond the scope of this re-check.
