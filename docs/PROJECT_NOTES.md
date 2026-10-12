# Implementation notes

MiniCRM models clients, opportunities, follow-ups, demo users and role permissions. The project is intended to demonstrate a small connected business workflow with a server-rendered interface.

- Endpoints parse forms and enforce module access; services validate and apply workflow changes.
- The local store gives readers detached snapshots. A single lock serializes mutations, and a flushed temporary file is renamed only after a successful write. Failed validation or persistence leaves the in-memory state unchanged.
- Existing JSON is validated on startup. Corrupt or unsupported data needs an explicit repair or backup restore, rather than automatic replacement.
- Notes participate in global ID allocation. A follow-up is linked to a real deal and its client.
- Blocking and role permissions are checked for pages, mutations and exports. Every POST needs an antiforgery token. The demo selector remains intentionally available to switch identities, including blocked identities.
- Overdue status is derived from due dates and the current UTC date. Reports use current deal stages and creation-date filters, with an actual preceding period of equal length.
- PDF export embeds a licensed Unicode font and wraps and paginates all rows. Spreadsheet export uses typed strings in Excel 2003 XML, preserving text without interpreting it as formulas.

Useful future work would be real authentication, database-backed storage and historical sales events. These require separate product decisions; they are not necessary to run this local demo.
