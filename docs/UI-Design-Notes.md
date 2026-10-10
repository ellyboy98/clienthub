# Client Management Hub – UI Design Notes (Version 1)

The developer reads this when building Razor Pages and Tailwind styles. It replaces the Figma file (link and notes in `docs/Figma-Design.md`; the Figma Bill editor and Settings frames were never checked visually, so this file wins where they differ).

## Tokens (Tailwind config)

| Token | Hex | Use |
| --- | --- | --- |
| brand-navy | #1B2A41 | sidebar, dark headers, primary buttons |
| brand-navy-dark | #12203A | active nav item, pressed state |
| brand-gold | #C9A227 | the one accent action per screen (Upload, highlights) |
| brand-gold-soft | #F7EFD4 | amount-due card, avatars |
| bg-page | #F4F6F9 | page background |
| bg-surface | #FFFFFF | cards, bars |
| bg-subtle | #EEF1F5 | table header, search box, chat composer field |
| text-primary | #111827 | body text |
| text-secondary | #5B6472 | labels, metadata |
| text-muted | #8A94A3 | placeholders, hints |
| border | #E3E7ED | card borders, dividers |
| border-strong | #C9D0DA | outlined buttons, inputs on focus |
| info / info-bg | #2563EB / #E8EFFD | Filed, Hearing, Sent |
| success / success-bg | #15803D / #E6F4EA | Completed, Received, Paid, Active |
| warning / warning-bg | #B45309 / #FDF1DC | Documents collected, Pending |
| danger / danger-bg | #B91C1C / #FCE8E8 | Cancelled, Rejected, unpaid |
| neutral / neutral-bg | #4B5563 / #EEF1F5 | New, Draft |

Font: Inter (Google Fonts), fallback system-ui. Sizes: 32/40 bold (display), 24/32 and 20/28 and 16/24 semibold (headings), 16/24 and 14/20 regular (body), 14/20 medium (labels, buttons), 12/16 medium (badge, label), 12/16 regular (caption).

Radius: cards 14px, buttons and inputs 10px, badges and avatars full. Card shadow: 0 2px 8px rgba(17,26,41,0.06). Padding: cards 16px (mobile) / 20px (desktop), page 20px (mobile) / 32px (desktop).

## Components

- Button: Primary (navy, white text), Accent (gold, navy text, max one per screen), Secondary (white, border-strong), Ghost (text only, navy), Danger (danger-bg, danger text). 44px tall, 20px side padding.
- Badge: dot + label, pill, 12px medium. Tone mapping above.
- Input: label above (12px medium, text-secondary), 44px field, 10px radius, 1px border; error = danger border + caption.
- Card: surface, border, 14px radius, shadow.
- Bottom nav (mobile client portal): Home, Cases, Chat, Bills. Active item navy, others muted.
- Sidebar (desktop staff area, 240px navy): logo, Dashboard, Clients, Cases, Chats (unread count pill in gold), Bills, Past cases, Settings (Lawyer only), user at bottom with their role (Lawyer, Office Manager, Staff).

## Client portal (phone width first, 390px, works on desktop too)

1. Login: logo, email, password, Sign in (primary, full width), Forgot password (ghost), EN | BM switch, note "Your account is created by the firm".
2. Home: navy header "Hello, <name>"; amber card "We still need from you" listing pending document requests each with a gold Upload button, placed first; case cards (title, badge, type, opened date, last update); two tiles Messages (unread count) and Bills (amount unpaid).
3. Case: navy header with title, status badge, lawyer in charge; tabs Updates | Documents | Bills; pending and rejected requests at top with reason and Re-upload; dated updates list newest first; shared documents with Download.
4. Chat: firm messages white bubbles with sender name, client messages navy on the right, file bubbles with name and size, composer with attach and send (upload progress shown in the bubble), polls every 5 s.
5. Bill: gold "Amount due" card; lines with Fee/Expense badge and amount; total; bank name, account name, number, reference = bill number; DuitNow QR image; "Print / Save as PDF" (secondary) using a print stylesheet.
6. Notifications: list, unread with gold dot, "Mark all read".
7. Past cases: Completed and Cancelled cards.

Dark navy header means inside a case or a bill; light header means a utility screen.

## Staff area (desktop 1440px, usable down to tablet)

1. Dashboard: four stat tiles (active cases, documents pending from clients, unread chats, bills awaiting payment); active cases table (case, client, type, status, lawyer, last update); "Needs your attention" feed (uploads, messages, overdue requests, unpaid bills).
2. Clients: search by name/IC/reg no, chips All/Person/Company/Inactive, table (client, type, IC/reg no, phone, active cases, login state, Open). "+ New client" (primary) for Lawyer and Office Manager only. Staff role sees only assigned clients and no New client button.
3. Case page: header with breadcrumb, title, status badge, actions Post update and Change status; 5-step progress bar (New, Documents collected, Filed, Hearing, Completed; Cancelled from any step); document requests list with Pending/Rejected/Received and View/Reject; updates visible to client; case documents with Upload/Download/Remove (Remove asks for confirmation and says the file is kept in the records); right column: case details (type, opened, lawyer, assigned staff, description; editing assigned staff for Lawyer and Office Manager only), client card with Open chat and Profile, bills with + New bill.
4. Chat (staff): client list on the left with unread pills, thread on the right, same bubbles as the portal. Each file bubble has "Save to case", which opens a small picker of that client's cases the user can open.
5. Bill editor: lines table (type dropdown Fee/Expense, description, amount, delete), + Add line, total calculated by the system (not editable), note to client, status card (Draft / Sent / Paid, "Sending freezes the lines"), preview of bank details and QR from Settings.
6. Settings (Lawyer only): firm details, bank account for bills, DuitNow QR upload, SMTP host/port/user/password/from with Send test email, staff accounts with role (Lawyer, Office Manager, Staff), 2FA state, Add staff, Deactivate. No language setting: English is the default for everyone, and each user changes their own language in My account.

## Behaviour rules the UI must show

- Access failures are a 404 page, not a "forbidden" message.
- Every status word is identical in the portal and the staff area.
- All UI text comes from .resx (en, ms); dates dd/MM/yyyy; money "RM 1,234.50".
- Mobile: 44px minimum tap targets, bottom nav fixed, content scrolls.
