# Test report: M3 re-check after review fixes

## Result
PASS. 75 passed, 0 failed, 0 skipped (full suite).

## Failures
None.

## Done-criteria not met
None. Staff changing a client's email returns 404 and an email change invalidates the old set-password link (ClientTests: Staff_ChangeEmail_404_OtherFieldsSave, EmailChange_OldSetPasswordLinkFails_NewLinkSent).

## Invariant breaches
None found.
