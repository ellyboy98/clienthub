# Test report M6 (Bills and Settings)

## Result
PASS. Full suite 157/157. `npm run css:build` OK. Live checks (curl, REQUIRE_FIRM_2FA=false) all passed.

## Failures
None.

## Done-criteria not met
None. Verified live: BILL-2026-0001 created by Lawyer; Fee + Expense lines gave a 1,025.75 total shown as text (not an input);
after Send no line controls and add-line returns 400; client sees bill with bank details, reference and Print button on
/Portal/Bills, details and case Bills tab; draft invisible to client (details 404, list and tab hide it); Mark paid works
(second Mark paid 400); OM and Staff get 404 on /Staff/Settings, /Staff/Settings/StaffAccounts and POSTs; Staff QR start 404;
QR PNG upload (Start, PUT, Complete) works and shows on Settings and the client bill; PDF as QR rejected 400.

## Invariant breaches
None blocking. Note: Services/Bills/BillService.cs:92 and :104 use `Lines.Sum(...)` for the max-amount cap check only
(not stored, not displayed). Totals are stored only via Bill.Recalculate().
