## Result
PASS - 39/39 tests (M2 re-check after review fixes).

## Failures
None.

## Done-criteria not met
None. Covered by AuthTests: login page, portal/staff landing, Client_StaffPage_404, Staff_Settings_404, culture switch. Also ForgotPassword_ForgedHost_LinkUsesAppBaseUrl and BadAntiforgery_Stays400_Not404 pass.

## Invariant breaches
None. M2 has no case/client handlers yet; no hard-coded strings (validation messages are resx keys); no total computation outside Bill.Recalculate().
