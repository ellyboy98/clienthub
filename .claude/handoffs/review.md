# Review: M4 Storage and documents (uncommitted diff on build/v1, 2026-10-10)

Scope: `git diff` plus untracked M4 files (Services/Storage, Services/Documents, Pages/Uploads, Pages/Files, upload.js, migration, tests), checked against plan.md M4, requirements.md R-045..R-062, R-114, R-143, R-144, R-151, R-152.

Checked and fine: file bytes never go through the app (R-061); PUT URL lasts 10 min and GET URL 5 min; content type is signed into the PUT, and a test covers it (`PresignedPut_OtherContentType_Refused`). The ticket uses Data Protection purpose `ClientHub.Upload` with a 15 min time limit and is bound to user, target and key; `KeyMatchesTarget` re-checks the key prefix. Completing the same ticket twice gives back the first row. Access is re-checked on completion and failures return 404. Start, Complete, Download, Request, Reject and Remove all go through `CasesFor`/`CanAccessCaseAsync` and return 404. Keys are `cases/<id>/<uuid>/<sanitised>`, built only on the server, with path parts removed. Remove only sets `is_active = false`, writes an audit row and leaves the object. Deleting failed-validation objects is allowed by R-114. Upload, download and remove each write an audit row in the same save. File names are Razor-encoded or set via `textContent`, and Content-Disposition is `attachment` with the name sanitised. Every new string is in resx with both en and ms. No secrets in code; the bucket key is stored, never a public URL.

## Critical

1. src/ClientHub.Web/Services/Storage/S3StorageService.cs:43 (with UploadService.cs:59 and wwwroot/js/upload.js:22) — Problem: the presigned PUT URL can be reused until it expires (10 min from Start), so after `/Uploads/Complete` has checked the object with HeadObject and written the row, the uploader can PUT again to the same key with any body of any size. That overwrites a document that has already been checked and audited, gets around the 20 MB limit (R-054), and destroys the original file (against R-114 / "nothing is deleted"). The `documents.size_bytes` row is then wrong. Fix: sign `If-None-Match: *` into the presigned PUT and send it from upload.js so the key can be written only once, with a test that a second PUT to the same URL is refused. If SeaweedFS does not honour it, PUT to a staging key and `CopyAsync` to a server-only final key on completion.

## Should fix

1. src/ClientHub.Web/Services/Storage/UploadService.cs:82 — Problem: the refusal warning logs the full storage key, which includes the client's file name (for example "IC 900101-14-5678.jpg"; the request placeholder even suggests "Copy of IC"), so IC numbers can reach the console logs. Fix: log the case id and the key's UUID segment only, never the file-name part.
2. src/ClientHub.Web/Services/Storage/UploadService.cs:74 (and S3StorageService.cs:43) — Problem: object size is not limited by the signature, only checked after HeadObject. An upload that is never completed, or whose completion is refused for access, leaves an object of any size in the bucket forever with no row pointing to it. Fix: record this limitation in decisions.md. If the SDK can sign `Content-Length`, bind the PUT to the declared size (already checked to be 20 MB or less at Start).
3. src/ClientHub.Web/Services/Storage/UploadService.cs:70-89 — Problem: two Complete requests with the same ticket at the same time both pass the "existing" lookup. The second one then hits the unique `storage_key` index and fails with a 500 instead of returning the first document. Fix: catch the unique-violation `DbUpdateException` on save and return the existing document id.
4. src/ClientHub.Web/Pages/Staff/Cases/Details.cshtml:51 — Problem: R-143 "tabs on phone" is listed for M4 but not built (status.md says the sections stack instead), and no decision covers it. Fix: build the phone tabs or get Fiqri's approval and record the stacking layout in decisions.md.

## Nice to have

1. src/ClientHub.Web/Services/Storage/UploadRules.cs:100 — Problem: the type check trusts the signed Content-Type header, so an `.exe` renamed to `.pdf` passes. Fix: on completion, read the first bytes with a ranged GET and check the PDF/ZIP(docx)/JPEG/PNG magic numbers.
2. src/ClientHub.Web/Services/Storage/UploadService.cs:70 — Problem: replaying a completed ticket after the document has been removed returns the id of the inactive document as a success. Fix: return NotFound when the existing row is inactive.
3. src/ClientHub.Web/Pages/Staff/Cases/Details.cshtml.cs:137 — Problem: rejecting a request that is not Received (stale page) shows a bare 400 page. Fix: redirect back to the case page with a localised notice instead.

## decisions.md entries (M4)

- Ticket lifetime 15 min (PUT 10 min): the TSD is silent on ticket lifetime and its expiry is enforced, so this is not drift.
- Only Received requests can be rejected: matches FSD section on requests (lines 98-101), so this is not drift.
- Firm uploads with a request id return 404, and clients can upload only against a request: matches R-047/R-049/Q-11.
- Uploads refused for access leave the object in place: consistent with R-114 (see Should fix 2 on orphans).
- No entry changes spec behaviour. The unrecorded gap is R-143 phone tabs (Should fix 4).
