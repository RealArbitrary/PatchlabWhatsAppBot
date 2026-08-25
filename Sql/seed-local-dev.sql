-- =============================================================================
--  STOP AND READ BEFORE RUNNING THIS SCRIPT
-- =============================================================================
--
--  This script WIPES every row in TicketComments, TicketFeedback, Tickets, and
--  Customers (in that FK-safe order) and replaces them with a fixed set of
--  fake/test tickets covering both happy-path and edge-case data shapes —
--  nulls, missing comments, missing feedback, legacy rows, etc. — so gaps like
--  missing null-handling get caught locally instead of against real data.
--
--  ONLY EVER RUN THIS BY HAND, ONE TIME AT A TIME, AFTER PERSONALLY CHECKING
--  WHICH DATABASE YOU ARE CONNECTED TO.
--
--  There is NO automated safeguard in this script that can tell your dev
--  database apart from production. An earlier version of this script tried to
--  guard itself by checking @@SERVERNAME, but in this environment dev and
--  production report the IDENTICAL server name (both currently run on the
--  same physical machine) — so that check could not actually distinguish
--  them, and gave false confidence rather than real protection. It has been
--  removed rather than kept as security theatre. Nothing will stop this
--  script from running against production. The only thing standing between
--  this script and a wiped production database is you, personally, checking
--  the -S and -d you're about to connect with before you run it.
--
--  Before running, always confirm — by hand, every time — which database
--  you're actually pointed at, e.g.:
--
--      sqlcmd -S <server> -d <database> -E -C -Q "SELECT DB_NAME(), @@SERVERNAME"
--
--  and cross-check that against wherever the live bot's config.json actually
--  points, not just against a name or hostname that "sounds like" dev.
--
--  This script must NEVER be wrapped in another script, task, pipeline, CI
--  job, or any other automated invocation — it is deliberately kept out of
--  Program.cs's migrate-on-startup path and must stay that way. It does NOT
--  touch the schema (no CREATE/ALTER TABLE) — schema changes to
--  Tickets/Customers/etc. go through EF Core migrations in this repo, never
--  hand-written SQL (see README.md, "Database & migrations").
--
--  Usage (from the repo root, PowerShell or any shell with sqlcmd on PATH),
--  only after you have personally verified -S/-d above:
--
--      sqlcmd -S localhost -d Patchlab -E -C -i Sql\seed-local-dev.sql
-- =============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

BEGIN TRANSACTION;

-- ---- Wipe existing data (children first) -----------------------------------
DELETE FROM TicketComments;
DELETE FROM TicketFeedback;
DELETE FROM TicketPhotos;
DELETE FROM Tickets;
DELETE FROM Customers;

DBCC CHECKIDENT ('TicketComments', RESEED, 0);
DBCC CHECKIDENT ('TicketFeedback', RESEED, 0);
DBCC CHECKIDENT ('TicketPhotos', RESEED, 0);
DBCC CHECKIDENT ('Tickets', RESEED, 0);

-- ---- Customers ---------------------------------------------------------
-- Real numbers so the WhatsApp flow can be exercised end-to-end locally.
-- Seeding here is pure SQL against the DB — it never calls IWhatsAppSender,
-- so running this script does not send any WhatsApp messages to anyone.
INSERT INTO Customers (CellphoneNumber, FirstName, LastName, Area, CreatedAt, UpdatedAt) VALUES
    (N'27845979202', N'Ané',   N'Botha',   N'Foundation Phase', DATEADD(DAY, -10, SYSUTCDATETIME()), DATEADD(DAY, -7, SYSUTCDATETIME())),
    (N'27618755258', N'Given', N'Nkosi',   N'Admin Block',      DATEADD(DAY, -6,  SYSUTCDATETIME()), DATEADD(DAY, -1, SYSUTCDATETIME())),
    (N'27820000001', N'Thabo', N'Mahlangu', NULL,                DATEADD(DAY, -5,  SYSUTCDATETIME()), DATEADD(DAY, -5, SYSUTCDATETIME())),
    (N'27820000002', N'Ilse',  N'van Wyk', N'Grade 5',          DATEADD(DAY, -30, SYSUTCDATETIME()), DATEADD(DAY, -28, SYSUTCDATETIME())),
    (N'27820000003', N'Pieter', N'Coetzee', N'IT Lab',          DATEADD(DAY, -3,  SYSUTCDATETIME()), DATEADD(DAY, -3, SYSUTCDATETIME())),
    (N'27820000004', N'Naledi', N'Dube',   N'Front Office',     DATEADD(DAY, -12, SYSUTCDATETIME()), DATEADD(DAY, -11, SYSUTCDATETIME())),
    (N'27820000005', N'Werner', N'Els',    N'Sports Field',     DATEADD(DAY, -6,  SYSUTCDATETIME()), DATEADD(DAY, -5, SYSUTCDATETIME())),
    (N'27820000006', N'Zanele', N'Khumalo', N'Library',         DATEADD(DAY, -15, SYSUTCDATETIME()), DATEADD(DAY, -12, SYSUTCDATETIME())),
    (N'27820000007', N'Mpho',  N'Sithole', N'Grade 7',          DATEADD(DAY, -4,  SYSUTCDATETIME()), DATEADD(DAY, -1, SYSUTCDATETIME()));
    -- Note: 27820000008 is deliberately NOT inserted here — see TCKT-0010 below,
    -- which exercises a ticket with no matching Customer row (unknown/blank name).

-- ---- Tickets -------------------------------------------------------------
-- TicketNumber is a computed column (TCKT-000N from Id) — never insert it directly.
-- Identity was reseeded to 0 above, so insert order below fixes TCKT-0001..0010.
-- TicketType is an EF enum backed by a plain int column: 0 = IT, 1 = Herstelwerk.
INSERT INTO Tickets (CellphoneNumber, Issue, Area, CreatedAt, Status, ResolvedAt, TicketType) VALUES
    -- TCKT-0001 — happy path: fully populated, closed, resolved, has comments + satisfied feedback
    (N'27845979202', N'Projector bulb needs replacing in the Grade 2 classroom', N'Foundation Phase', DATEADD(DAY, -10, SYSUTCDATETIME()), N'Closed', DATEADD(DAY, -7, SYSUTCDATETIME()), 0 /* IT */),
    -- TCKT-0002 — happy path: fully populated, still open, no ResolvedAt yet.
    -- Ticket.Area deliberately differs from this customer's Customers.Area (Admin Block) to show the two are independent.
    (N'27618755258', N'New printer needs network setup in the reception area', N'Reception', DATEADD(DAY, -2, SYSUTCDATETIME()), N'Open', NULL, 0 /* IT */),
    -- TCKT-0003 — edge case: Area = NULL (unassigned/unknown area)
    (N'27820000001', N'Laptop won''t power on, no lights at all', NULL, DATEADD(DAY, -5, SYSUTCDATETIME()), N'Open', NULL, 0 /* IT */),
    -- TCKT-0004 — edge case: Closed but ResolvedAt = NULL (legacy pre-ResolvedAt-column ticket)
    (N'27820000002', N'Smartboard calibration drifted, touch input misaligned', N'Grade 5', DATEADD(DAY, -30, SYSUTCDATETIME()), N'Closed', NULL, 0 /* IT */),
    -- TCKT-0005 — edge case: no comments at all, no feedback at all, still open
    (N'27820000003', N'Three lab PCs can''t connect to wifi', N'IT Lab', DATEADD(DAY, -3, SYSUTCDATETIME()), N'Open', NULL, 0 /* IT */),
    -- TCKT-0006 — edge case: resolved but no feedback at all (customer never responded to the satisfaction prompt)
    (N'27820000004', N'Landline phone has no dial tone', N'Front Office', DATEADD(DAY, -12, SYSUTCDATETIME()), N'Closed', DATEADD(DAY, -11, SYSUTCDATETIME()), 1 /* Herstelwerk */),
    -- TCKT-0007 — edge case: has feedback, Reason = NULL (satisfied, no comment given)
    (N'27820000005', N'Scoreboard remote batteries need replacing', N'Sports Field', DATEADD(DAY, -6, SYSUTCDATETIME()), N'Closed', DATEADD(DAY, -5, SYSUTCDATETIME()), 1 /* Herstelwerk */),
    -- TCKT-0008 — edge case: feedback Status = Unhappy with a populated Reason
    (N'27820000006', N'Library catalogue system keeps logging users out', N'Library', DATEADD(DAY, -15, SYSUTCDATETIME()), N'Closed', DATEADD(DAY, -12, SYSUTCDATETIME()), 0 /* IT */),
    -- TCKT-0009 — edge case: multiple comments, to exercise comment ordering/display
    (N'27820000007', N'Classroom AC unit leaking water onto the floor', N'Grade 7', DATEADD(DAY, -4, SYSUTCDATETIME()), N'Open', NULL, 1 /* Herstelwerk */),
    -- TCKT-0010 — edge case: CellphoneNumber has no matching Customers row at all (name/customer fields come back null — GUI shows "—")
    (N'27820000008', N'Front gate intercom not buzzing through to the office', N'Reception', DATEADD(DAY, -1, SYSUTCDATETIME()), N'Open', NULL, 1 /* Herstelwerk */);

-- ---- TicketComments --------------------------------------------------------
INSERT INTO TicketComments (TicketId, Comment, CreatedAt)
SELECT t.Id, c.Comment, c.CreatedAt
FROM (VALUES
    (N'TCKT-0001', N'Ordered replacement bulb, ETA Friday.',                          DATEADD(DAY, -9, SYSUTCDATETIME())),
    (N'TCKT-0001', N'Bulb replaced and tested — working.',                            DATEADD(DAY, -7, SYSUTCDATETIME())),
    (N'TCKT-0002', N'Ordered the printer, arriving Monday — will install once it''s here.', DATEADD(DAY, -1, SYSUTCDATETIME())),
    (N'TCKT-0004', N'Recalibrated the board, should be accurate now.',                DATEADD(DAY, -28, SYSUTCDATETIME())),
    (N'TCKT-0006', N'Line fixed by the provider, confirmed dial tone restored.',       DATEADD(DAY, -11, SYSUTCDATETIME())),
    (N'TCKT-0007', N'Replaced the batteries, scoreboard working again.',              DATEADD(DAY, -5, SYSUTCDATETIME())),
    (N'TCKT-0008', N'Applied a session-timeout patch from the vendor.',               DATEADD(DAY, -14, SYSUTCDATETIME())),
    (N'TCKT-0008', N'Vendor patch didn''t hold, escalated to their support team.',    DATEADD(DAY, -12, SYSUTCDATETIME())),
    (N'TCKT-0009', N'Plumber notified, waiting on availability.',                     DATEADD(DAY, -4, SYSUTCDATETIME())),
    (N'TCKT-0009', N'Plumber confirmed for Thursday morning.',                        DATEADD(DAY, -3, SYSUTCDATETIME())),
    (N'TCKT-0009', N'Temporary bucket placed under the unit to catch drips meanwhile.', DATEADD(DAY, -1, SYSUTCDATETIME()))
) AS c(TicketNumber, Comment, CreatedAt)
JOIN Tickets t ON t.TicketNumber = c.TicketNumber;

-- ---- TicketFeedback ---------------------------------------------------------
INSERT INTO TicketFeedback (TicketId, Status, Reason, CreatedAt)
SELECT t.Id, f.Status, f.Reason, f.CreatedAt
FROM (VALUES
    (N'TCKT-0001', N'Satisfied', CAST(NULL AS NVARCHAR(MAX)), DATEADD(DAY, -6, SYSUTCDATETIME())),
    (N'TCKT-0004', N'Satisfied', CAST(NULL AS NVARCHAR(MAX)), DATEADD(DAY, -27, SYSUTCDATETIME())),
    (N'TCKT-0007', N'Satisfied', CAST(NULL AS NVARCHAR(MAX)), DATEADD(DAY, -4, SYSUTCDATETIME())),
    (N'TCKT-0008', N'Unhappy',   N'Still logs out randomly, hasn''t actually been fixed.', DATEADD(DAY, -11, SYSUTCDATETIME()))
) AS f(TicketNumber, Status, Reason, CreatedAt)
JOIN Tickets t ON t.TicketNumber = f.TicketNumber;

-- ---- TicketPhotos -----------------------------------------------------------
-- These rows point at REAL files — run Sql\seed-ticket-photos-files.ps1
-- separately (by hand, same as this script) to actually create them under
-- TicketPhotos/<today>/<guid>.jpeg. Both scripts compute "today" (UTC)
-- independently, so run them the same calendar day for the paths to line up.
-- Covers three photo-count shapes for the GUI to exercise locally:
--   TCKT-0001 -> 1 photo   (single-photo layout)
--   TCKT-0008 -> 4 photos  (thumbnail strip scroll/wrap)
--   everything else (e.g. TCKT-0002) -> 0 photos (the "No photos yet." empty state)
-- GUIDs here must match Sql\seed-ticket-photos-files.ps1 exactly.
DECLARE @photoDateFolder NVARCHAR(10) = FORMAT(SYSUTCDATETIME(), 'yyyy/MM/dd');

INSERT INTO TicketPhotos (TicketId, FilePath, CreatedAt)
SELECT t.Id, @photoDateFolder + N'/' + p.FileName, SYSUTCDATETIME()
FROM (VALUES
    (N'TCKT-0001', N'44444444-4444-4444-4444-444444444444.jpeg'),
    (N'TCKT-0008', N'55555555-5555-5555-5555-555555555555.jpeg'),
    (N'TCKT-0008', N'66666666-6666-6666-6666-666666666666.jpeg'),
    (N'TCKT-0008', N'77777777-7777-7777-7777-777777777777.jpeg'),
    (N'TCKT-0008', N'88888888-8888-8888-8888-888888888888.jpeg')
) AS p(TicketNumber, FileName)
JOIN Tickets t ON t.TicketNumber = p.TicketNumber;

COMMIT TRANSACTION;

PRINT 'Seed complete.';
SELECT (SELECT COUNT(*) FROM Customers) AS Customers,
       (SELECT COUNT(*) FROM Tickets) AS Tickets,
       (SELECT COUNT(*) FROM TicketComments) AS TicketComments,
       (SELECT COUNT(*) FROM TicketFeedback) AS TicketFeedback,
       (SELECT COUNT(*) FROM TicketPhotos) AS TicketPhotos;
GO
