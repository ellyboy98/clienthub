The Figma file is built: Client Management Hub – UI Design → https://www.figma.com/design/tuU34jwPLuf2Q4NS9xPnpZ

One thing first: I hit the Figma Starter plan’s MCP call limit on the very last check, so the Bill editor and Settings screens were created (node IDs came back fine) but I could not screenshot them. Everything else was visually checked. Open those two and expect small spacing fixes at most.

What’s in the file

Page 01 Foundations (the design system):

Tokens variable collection, 23 colours. Navy 
#1B2A41 as the brand base, gold 
#C9A227 as the single accent, page 
#F4F6F9, surface white, three text tones, two border tones, and five status pairs (info, success, warning, danger, neutral, each with a soft background). Every fill in every screen is bound to these, so changing a token changes the whole file.
9 text styles, all Inter: Display/32, Heading/24, 20, 16, Body/16, 14, Body/14 Medium, Label/12, Caption/12.
Components: Button (Primary navy, Accent gold, Secondary outlined, Ghost, Danger; label is a text property), Badge (Neutral, Info, Warning, Success, Danger; the description on the set maps every case, document and bill status to a tone), Input, Card. Plus a palette sheet and a type ramp for reference.

Page 02 Client Portal (Mobile), seven screens at 390px, bottom tab bar Home / Cases / Chat / Bills:

Login: logo, email, password, sign in, forgot password, EN | BM switch, note that the firm creates accounts.
Home: navy greeting header; “We still need from you” amber card with gold Upload buttons at the top (this is the firm’s biggest pain point, so it’s first); case cards with status badges; Messages and Bills summary tiles.
Case: navy header with status badge and lawyer; tabs Updates / Documents / Bills; pending requests including a rejected one with the reason and Re-upload; dated updates timeline; shared documents with Download.
Chat: one thread with the firm, lawyer bubbles white, client bubbles navy, a file attachment bubble, composer with attach and send.
Bill: gold “Amount due” card, bill of costs with Fee/Expense badges per line and the total, bank details, DuitNow QR slot, Print / Save as PDF.
Notifications: unread marked with a gold dot, grouped by kind.
Past cases: Completed and Cancelled cards.

Page 03 Staff Area (Desktop), five screens at 1440px with a navy sidebar (Settings appears only for the Lawyer role, Chats shows an unread count):

Dashboard: four stat tiles, active cases table, “Needs your attention” feed.
Clients list (shown as the Staff role, with the note that staff see only assigned clients): search, filter chips, table with login state.
Case page: five-step progress bar, document requests with Pending / Rejected / Received and View / Reject actions, updates visible to client, case documents, and a right column with case details, client card with Open chat, and bills.
Bill editor: editable lines (type dropdown, description, amount, delete), system-calculated total with a note that it can’t be edited by hand, note to client, status card explaining that sending freezes the lines, and a preview of what the client sees.
Settings: firm details, bank account and QR upload, SMTP with “Send test email”, staff accounts with 2FA state and Deactivate.

Design rules the screens follow

Mobile first; nothing on the portal needs a hover.
Dark navy header means “you are inside a case or a bill”; light header is a utility screen.
Gold is used for exactly one thing per screen: the action the user most needs to take.
Status language is identical across portal and staff area so the client and the lawyer see the same words.
Icons are placeholder squares named icon/<name>. Swap them for Lucide icons in Figma when you want; the names match the Lucide set.