# Client Management Hub – Functional Specification (Version 1)

Date: 10 October 2026 · Author: Fiqri Wilter

Revised 10 October 2026: added the Office Manager role, aligned chat access and file removal with the Technical Specification, narrowed staff notifications to the people on the case, set English as the default language. The reasons are in `.claude/handoffs/decisions.md`.

## Purpose

The Client Management Hub is a website where one small law firm and its clients manage their cases in one place. It replaces the firm's current mix of Excel files and WhatsApp chats, which the firm describes as inefficient and unorganised.

The firm handles criminal and civil litigation. It has 3 to 4 staff (2 of them lawyers) and fewer than 10 active clients. The system is for this firm only. It is not a product for other firms.

Version 1 must let a client do four things: see case updates from the lawyer, see which documents the firm still needs and upload them, message the lawyer, and see their bill of costs. Everything else in this document supports those four needs. The firm wants to start using the system as soon as version 1 is ready.

## Users and access

There are four types of user. Lawyers and the Office Manager see everything. Staff see only the cases they are assigned to. A client sees only their own cases.

In this document, "staff" in lower case means any firm user (Lawyer, Office Manager or Staff). "Staff" with a capital letter means the Staff role.

| User type | Who | What they can see | What they can do |
| --- | --- | --- | --- |
| Lawyer | The firm's 2 lawyers | All clients, all cases, all documents, all chats, all bills | Everything: create and edit clients and cases, assign staff to cases, change case status, upload documents, request documents, chat with clients, create bills, mark bills as paid, manage settings and staff accounts |
| Office Manager | One non-lawyer who runs the office | All clients, all cases, all documents, all chats, all bills | Responsible for creating clients. The same actions as a lawyer on every client and case, including creating clients and cases and assigning staff. Cannot manage settings or staff accounts |
| Staff | The other non-lawyer staff | Only the cases assigned to them, plus those cases' client details, documents, bills and the client's chat | The same actions as a lawyer, but only on their assigned cases. Cannot create clients or cases, assign staff, or manage settings or staff accounts |
| Client | One login per client (a person or a company) | Only their own cases, documents, chat and bills | Read case status and updates, upload requested documents, download shared documents, chat with the firm, view bills |

Rules:

- A lawyer or the Office Manager assigns Staff to a case when creating or editing it. A Staff user with no assigned cases sees an empty list.
- Because the chat is per client, a Staff user assigned to any of a client's cases can read and reply in that client's chat.
- A client logs in with their email address and a password. A lawyer or the Office Manager creates the client's account and the system emails the client a link to set their password.
- A lawyer creates the Office Manager and Staff accounts on the Settings page.
- A company client gets one shared login. The company's person in charge uses it. The firm does not need separate logins for several people in the same company.
- A client never sees another client's data, and never sees documents, notes or chats that are not part of their own cases.
- A lawyer can deactivate a client login when the relationship ends. The data stays; the client can no longer log in.

## Clients

A client is a person or a company the firm works for. A lawyer or the Office Manager creates the client record first, then opens cases under it.

| Field | Required | Notes |
| --- | --- | --- |
| Client type | Yes | Person or Company |
| Name | Yes | Full name, or company name |
| IC number | Yes for a person | Company registration number for a company |
| Phone | Yes |  |
| Email | Yes | Used for login and email alerts |
| Address | Yes |  |
| Person in charge | Company only | Name and phone of the company's contact person |

Rules:

- One client can have many cases.
- One case belongs to exactly one client. Joint clients (for example, husband and wife on one case) are not needed in version 1.
- Lawyers, the Office Manager and Staff assigned to one of the client's cases can edit a client's details at any time. The client cannot edit their own details; they ask the firm.

## Cases

A case is one legal matter for one client. Every case has a status, and the client sees the status and every update the lawyer posts.

Case fields:

| Field | Required | Notes |
| --- | --- | --- |
| Case title | Yes | Short name, for example "Civil suit vs ABC Sdn Bhd" |
| Case type | Yes | Criminal or Civil |
| Client | Yes | The client this case belongs to |
| Status | Yes | One of the stages below, or Cancelled |
| Lawyer in charge | Yes | One of the 2 lawyers |
| Assigned staff | No | Other staff who work on this case and may open it |
| Description | No | Free text, visible to the client |
| Date opened | Yes | Set automatically |

Case stages, in order. The same list applies to criminal and civil cases. Staff can move a case forward or backward.

1. New
2. Documents collected
3. Filed
4. Hearing
5. Completed

A case can also be marked Cancelled from any stage, for example when the client withdraws. Completed and Cancelled cases move to a "Past cases" list for both staff and the client, so the main page shows only active cases. A case is never deleted. Its documents, updates and bills stay available in the past cases list.

Case updates:

- Staff can post an update on a case at any time. An update is a short text note with a date, for example "Hearing fixed for 12 November. Please bring your original documents."
- The client sees all updates for their case, newest first.
- When staff change the status or post an update, the client gets an in-system notification and an email (see Notifications).
- Information about the other party, their lawyer or the court is not needed in version 1. Staff can put it in the description if they want.

## Documents

Every document belongs to one case. The client sees every document on their own cases and nothing else. There are two kinds of document: files the firm shares with the client, and files the firm asks the client to send.

Document requests (what the firm still needs from the client):

- Staff add a request to a case, for example "Copy of IC" or "Signed engagement letter". Each request has a name, an optional note and a status: Pending, Received or Rejected.
- There are no ready-made checklists. Staff type each request by hand.
- The client sees the list of pending requests at the top of their case page and uploads a file against each one.
- When the client uploads, the request becomes Received and staff get a notification. Staff can mark it Rejected with a short reason (for example "photo is blurry"), which sends the request back to the client.

Shared documents:

- Staff upload files to a case. Every active file on a case is visible to that case's client.
- Staff can remove a file from a case. A removed file disappears from the case page for staff and the client, but it is never deleted: it stays in storage and the audit log records who removed it and when. The client cannot remove files.

Signing:

- Signing happens outside the system. Staff upload the document and add a request named, for example, "Signed engagement letter". The client downloads it, signs it any way they like, and uploads the signed copy against the request.
- No e-signature feature is needed in version 1.

File rules:

- Allowed types: PDF, Word (docx), JPG, PNG. Maximum size per file: 20 MB.
- Each file keeps its original name, who uploaded it and when.

## Chat

Each client has one chat with the firm. The chat is not split by case. Lawyers and the Office Manager can read and reply in any client's chat. A Staff user can read and reply only in the chat of a client with at least one case assigned to them.

- Messages are text, and can carry files or photos (same file rules as Documents).
- Each message shows the sender's name and the time. The client sees staff names; staff see the client's name.
- When a client sends a message, the lawyers in charge of that client's cases and the Staff assigned to them get a notification (see Notifications). When staff send a message, the client gets an in-system notification and an email.
- A file sent in chat stays in the chat. It is not added to the case documents automatically. If staff want it as a case document, they choose "Save to case" on the message and pick one of that client's cases they can open. The file is copied to the case documents; the chat message keeps its copy.
- The chat does not need to be real time. Refreshing the page or polling every few seconds is enough for a firm of this size.
- Clients may still contact the firm on WhatsApp. The system does not try to stop or capture that.

## Bills

A bill is a list of lines for one case. Staff type the lines, the system adds up the total, and the client sees the bill and how to pay. There is no online payment in version 1.

Bill fields:

| Field | Notes |
| --- | --- |
| Bill number | Set automatically, for example BILL-2026-0001 |
| Case | The case this bill is for |
| Date issued | Set when staff send the bill to the client |
| Lines | One or more, see below |
| Total | Sum of all lines, in RM, calculated by the system |
| Status | Draft, Sent, Paid |
| Note to client | Optional free text |

Each line has:

| Field | Notes |
| --- | --- |
| Type | Fee or Expense. Expenses are costs paid on the client's behalf, for example court fees or filing fees |
| Description | Free text, for example "Court attendance, 3 hours x RM300" |
| Amount | In RM. Staff type the final amount for the line |

Rules:

- Hourly work is written as a line with the hours and rate in the description. The system does not track time.
- A bill starts as Draft. The client cannot see a draft. When staff mark it Sent, the client gets a notification and an email.
- The client's bill page shows the bill lines, the total, the firm's bank account details and a DuitNow QR image that staff upload once in settings.
- The client pays by bank transfer or by scanning the QR. Staff then mark the bill as Paid. The client sees the status change.
- A Sent bill cannot be edited. Staff create a new bill instead.
- No official receipt and no SST are needed.
- The client can download the bill as a PDF.

## Notifications

Staff get in-system notifications only. Clients get an in-system notification and an email, because a client may not open the website on their own.

| Event | Who is told | Email sent |
| --- | --- | --- |
| Case status changed | Client | Yes |
| Case update posted | Client | Yes |
| Document request added | Client | Yes |
| Document request rejected | Client | Yes |
| Client uploaded a document | Lawyer in charge of the case and Staff assigned to it | No |
| New chat message from staff | Client | Yes |
| New chat message from client | Lawyers in charge of any of the client's cases and Staff assigned to any of them | No |
| Bill sent | Client | Yes |
| Bill marked paid | Client | Yes |
| Client account created | Client | Yes, with a set-password link |

Rules:

- The Office Manager is not notified of client uploads or messages unless the event matches no one above. If a client with no cases sends a message, both lawyers are notified.
- An email says what happened and gives a link to log in. It does not include case details, documents or message text, so nothing private sits in the client's inbox.
- Several events within a few minutes can be grouped into one email. This is nice to have, not required.
- In-system notifications show as a bell icon with a count. Clicking one opens the related case, chat or bill and marks it read.

## General requirements

The system is a website that works well on a phone. No native mobile app is needed.

- Language: English and Bahasa Melayu. English is the default: new accounts start in English and the login page shows English until the visitor switches. Each user can change their language in My account. All screen text, emails and notifications exist in both. Data typed by users (case titles, updates, messages) is shown as typed.
- Scale: fewer than 10 clients and under 50 cases in the first year. Simple hosting is enough. Design for a small team, not for high traffic.
- Security: all pages over HTTPS. Passwords stored hashed. A client can only ever open their own records, enforced on the server, not only hidden in the screen. Files are stored outside the public web folder and served only to users allowed to see them.
- Personal data: the system holds IC numbers, addresses and case details, so it must be treated as sensitive under Malaysia's Personal Data Protection Act. Keep access logs for logins and file downloads, and keep daily backups.
- Settings page for lawyers: firm name, bank account details for bills, DuitNow QR image, staff accounts (Lawyer, Office Manager, Staff), and the email setup for alerts (the sender address and the mail server details). All of these are typed into the settings page, not into code or config files.
- Audit: every case status change, update, document upload, document removal and bill status change records who did it and when.
- Backups: the database is backed up every day and the backups are kept for 30 days, so the firm can recover from a mistake or an outage.

## Screens

The staff side and the client portal are separate areas with their own menus. Every screen works on a phone.

Shared:

| Screen | Who | What it shows |
| --- | --- | --- |
| Login | All | Email, password, two-factor code if enabled, forgot password, language switch |
| Set password | All | Opened from the email link when an account is created or reset |
| Notifications | All | Bell icon list; each item opens the related case, chat or bill |
| My account | All | Change password, set up two-factor, choose language |

Staff area:

| Screen | Who | What it shows |
| --- | --- | --- |
| Dashboard | Lawyer, Office Manager, Staff | Active cases the user can open, pending document requests, unread chats, bills awaiting payment |
| Clients list | Lawyer, Office Manager, Staff | All clients (Lawyer, Office Manager) or clients with an assigned case (Staff), search by name or IC. New client button for Lawyer and Office Manager |
| Client page | Lawyer, Office Manager, Staff | Client details, their cases, link to the chat, button to create a case (Lawyer, Office Manager) |
| Case page | Lawyer, Office Manager, Staff | Status with change control, updates, document requests, documents, bills, assigned staff. Tabs on phone |
| Chat | Lawyer, Office Manager, Staff | One thread per client, send text and files, save a file to a case |
| Bill editor | Lawyer, Office Manager, Staff | Add and edit lines while Draft, see total, mark Sent or Paid |
| Past cases | Lawyer, Office Manager, Staff | Completed and Cancelled cases |
| Settings | Lawyer | Firm details, bank account, QR image, SMTP, staff accounts |

Client portal:

| Screen | Who | What it shows |
| --- | --- | --- |
| Home | Client | Their active cases with status, pending document requests at the top, unread messages, unpaid bills |
| Case page | Client | Status, updates, documents the firm still needs (with upload), shared documents, bills for this case |
| Chat | Client | Their one chat with the firm |
| Bill page | Client | Lines, total, status, bank details, QR image, print button |
| Past cases | Client | Their Completed and Cancelled cases |

## Version 2 (not in version 1)

These were in the original request or the firm's answers, but are left out of version 1 so the firm can start using the system sooner.

| Feature | What the firm asked for | Why it waits |
| --- | --- | --- |
| Tasks and deadlines | Staff create tasks per case, can assign a task to the client, reminder 3 days before the deadline, in-system only | Not in the firm's four must-haves |
| Calendar | One page of hearings, meetings and filing deadlines; client sees only dates on their own cases; no Google or Outlook sync | Depends on tasks and deadlines |
| Online payment | FPX and DuitNow QR, paid into the client account | Needs a payment gateway account, fees and bank setup; the firm said viewing the bill is enough |
| WhatsApp alerts | Alerts on WhatsApp instead of email | Needs a WhatsApp Business API account and costs per message |
| Joint clients | More than one client on one case | Firm said one client per case is fine |

## Open questions

None. The firm answered the last five questions on 10 October 2026, and the review questions were settled the same day. The answers are in the sections above. New questions go here as they come up.
