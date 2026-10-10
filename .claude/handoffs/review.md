# Review: M5 Chat (uncommitted, build/v1, 2026-10-10)

Scope: `git diff` + untracked M5 files, against plan.md M5 and requirements.md (Save to case R-070..R-072 is CUT).

Checked and OK: every chat handler (Staff thread GET, `?handler=New`, `?handler=Send`; Portal GET/New/Send;
`/Uploads/Start` and `/Uploads/Complete` for Chat; `/Files/Download/{id}?handler=Message`) goes through
`ClientsFor` / `CanAccessClientAsync` and returns 404. Tests cover unassigned Staff, other-client and random ids.
Portal/Chat takes the client id only from the signed-in Client (`OwnClientIdAsync`); a planted `clientId` is ignored
(tested). The Chat upload branch keeps M4's protections: ticket bound to user and target, `KeyMatchesTarget` checks
`chats/<clientId>/` plus the staging prefix, access re-checked on completion, staging -> server copy -> re-validate
the copy, idempotent by unique `attachment_key`. Message text, sender names and file names are rendered with Razor
encoding (no `Html.Raw`); error text is HtmlEncoded; Content-Disposition is built by the existing M4 helper.
Staff unread counts (sidebar, dashboard tile, list pills) all go through `ClientsFor`, so only assigned clients count.
New UI strings have en and ms keys. Bucket stays private; no IC numbers or secrets logged.

## Critical (must fix before commit)

1. `src/ClientHub.Web/Services/Chat/ChatService.cs:108-116` (with `:125`, `:136`, `:157-165`, `Pages/Shared/Components/ComponentModels.cs:193-197`) - The poll cursor is `sent_at` stamped in the app with `DateTime.UtcNow` before insert. A message whose commit lands after a later-stamped one has already been shown is never displayed until reload. On Vercel the clock can also differ between container instances, so even the sender's own message can be stamped behind the cursor and not appear for them. And because each poll first marks all of the other side's messages read (`MarkReadAsync`), the missed message is also marked read. The unread pill clears and nothing on screen shows the message. The developer accepted this race without accounting for the read marking. For a law firm, a client instruction that disappears silently and is marked read is not acceptable, and the fix is small. Fix: add `messages.seq bigint GENERATED ALWAYS AS IDENTITY` and use it as the cursor (`seq > after`, order by `seq`); take `pg_advisory_xact_lock(<client id hash>)` in the same transaction before each message insert (text send and `RecordUploadAsync`), so that within one chat `seq` order equals commit order; mark read only messages with `seq <= ` the highest `seq` returned to this viewer. Note: a plain identity or monotonic id **without** the per-client lock does not fix the race, because sequence values are handed out at insert time, not at commit. Add a test: insert a message with a lower seq/stamp after the cursor has moved and assert it is either returned or left unread.

## Should fix (next milestone)

1. `src/ClientHub.Web/Services/Chat/ChatService.cs:113` and `Pages/Shared/Components/_ChatMessages.cshtml:9` - The 5-second poll marks messages read even when the tab is in the background. A thread tab left open on any firm desk keeps every new client message read, so the shared Q-05 unread pill never reaches the lawyer. Fix: send `visible=1` (via `hx-vals` reading `document.visibilityState`) and only mark read when it is set, or pause the poll trigger while the page is hidden.
2. `.claude/handoffs/decisions.md:5` / `ChatService.cs:131-145` - R-067 says "text and/or a file", but a file message cannot carry text and the typed text is sent as a separate message. Fix: have Fiqri confirm the split, or pass the composer text through the upload ticket into `Body`.
3. `src/ClientHub.Web/Services/Chat/ChatService.cs:141,152` - Chat uploads and downloads are logged as `Document.Uploaded` / `Document.Downloaded` with entity `Message`, so any "document" audit filter will mix chat files in with case documents. Fix: either add `Message.FileUploaded` / `Message.FileDownloaded` to the plan's action list, or confirm that every reader filters on the entity.

## Nice to have

1. `src/ClientHub.Web/Pages/Shared/Components/_ChatMessages.cshtml:9` - htmx 2 drops a new trigger by default while a request from the same element is in flight. A `chat:refresh` that arrives during a 5 s poll is therefore lost, and the sender waits up to 5 s for their own message. Fix: add `hx-sync="this:queue last"` to the sentinel.
2. `src/ClientHub.Web/Pages/Staff/Chats/Thread.cshtml:18` - Reuses the `Staff.Case.Profile` key outside the case page. Fix: add `Common.Profile` (en+ms) or `Staff.Chats.Profile`.
3. `src/ClientHub.Web/Services/Chat/ChatService.cs:153` - The `"file"` fallback download name is a hard-coded literal. Fix: since `AttachmentName` is always set for file messages, drop the fallback or use the key's last segment.
4. `src/ClientHub.Web/Pages/Portal/Chat/Index.cshtml.cs:38-52` - Each poll and send runs `OwnClientIdAsync`, then `ClientsFor` again inside the service (two lookups every 5 s for each open tab). Fix: let the service resolve the Client's own chat in a single query.
5. `docs/UI-Design-Notes.md:57` vs `Pages/Shared/Components/_ChatMessages.cshtml:5` - The notes say staff chat uses "same bubbles as the portal". The build mirrors sides (firm on the right for firm users). This is a reasonable reading. Fix: record it in decisions.md.
