# Review: M7 Notifications and email (uncommitted, build/v1)

Scope: `git diff` plus the untracked Pages/Notifications, Services/Notifications and tests/ClientHub.Tests/Notifications. Checked against plan.md M7, requirements R-086..R-103 and R-134 (R-113 is cut), and decisions.md.

Checked with no findings:
- Recipients match each trigger. The client gets only links it can open. The case lawyer and assigned Staff get client uploads. The lawyers and Staff of the client's cases (or all active Lawyers when it has none) get client messages. The OM is never included.
- Open and Mark all are scoped to the signed-in user. The LinkUrl is checked to be local when it is written and checked with IsLocalUrl when it is opened.
- The email's ReturnUrl is escaped, and Login and LoginWith2fa redirect through IsLocalUrl.
- Emails are plain text with the subject taken only from resx, contain no case details, and use the recipient's language.
- An SMTP failure is caught and logged.
- Notifications are sent after commit.
- en and ms each have 26 new keys.
- The UI uses the shared partials, has no gold action, and has 56px rows.

## Critical
None.

## Should fix
1. src/ClientHub.Web/Services/Chat/ChatService.cs:141,162; Services/Cases/CaseService.cs:97,106; Services/Documents/DocumentService.cs:67,78,120-126; Services/Bills/BillService.cs:162-166. Problem: a failure inside the post-commit notify step (DB error, FirstAsync, or the request being cancelled via `ct`) still throws. The user's action is already committed, but they get a 500 and may retry, which duplicates a chat message. For uploads the throw also skips the staging cleanup in UploadService.cs:107-121. Fix: wrap post-commit Notify calls in a try/catch that logs and swallows the error, and pass `CancellationToken.None`, so the committed action always succeeds.
2. src/ClientHub.Web/Services/Access/AccessService.cs:73-76. Problem: when a client has cases but every lawyer in charge is deactivated and no Staff is assigned, a client message reaches no active user. Q-18 only covers the no-cases fallback. Fix: ask Fiqri whether to fall back to all active Lawyers in that case, then implement and record the decision.
3. src/ClientHub.Web/Services/Notifications/NotificationService.cs:67 and Pages/Notifications/Index.cshtml.cs:20. Problem: the list shows only the latest 100, but the bell counts every unread row, so older unread items can never be opened. Fix: add paging, or make the 100 cap a decision Fiqri confirms.

## Nice to have
1. src/ClientHub.Web/Services/Notifications/NotificationService.cs:52. Problem: `LogError(ex, ...)` logs the full MailKit exception, and the SMTP server's reply can echo the recipient address. Fix: log the exception type and SMTP status code without the message.
2. src/ClientHub.Web/Services/Notifications/NotificationService.cs:49-53. Problem: email is sent inside the request (5 s timeout per recipient), so a slow SMTP server delays the user's action. Fix: acceptable for one recipient; move sending to a background queue only if latency is seen.
3. tests/ClientHub.Tests/Notifications/NotificationTriggerTests.cs. Problem: no test shows that NotifyAsync rejects a non-local or `//host` link, or that an unassigned Staff user gets no client-message notification (R-097). Fix: add both tests.
4. .claude/handoffs/plan.md:145. Problem: the plan says Notify is called from page handlers with a three-argument signature, but it is now called from services with a context argument. The decision is recorded, but the plan text is stale. Fix: update the plan line.
