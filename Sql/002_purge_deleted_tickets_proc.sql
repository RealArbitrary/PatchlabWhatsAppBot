-- Purges archived tickets older than 90 days. Hardcoded cutoff, no config table.
-- Just the "how" -- does nothing on its own until called (see 003 for the "when").

CREATE OR ALTER PROCEDURE dbo.PurgeDeletedTickets
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.DeletedTickets
    WHERE DeletedAt < DATEADD(DAY, -90, GETUTCDATE());
END
GO
