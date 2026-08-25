-- Run manually against msdb on the instance hosting the Patchlab database.
-- SQL Server Agent must be running. This is the "when": a nightly job whose
-- single step calls dbo.PurgeDeletedTickets (the "how", see 002).
--
-- Requires: SQL Server Agent (not available on Express edition without manual
-- setup, and not at all on Azure SQL Database -- use Elastic Jobs there instead).

USE msdb;
GO

IF NOT EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'Purge Deleted Tickets')
BEGIN
    DECLARE @jobId BINARY(16);

    EXEC msdb.dbo.sp_add_job
        @job_name = N'Purge Deleted Tickets',
        @enabled = 1,
        @description = N'Nightly purge of DeletedTickets rows older than 90 days. Calls dbo.PurgeDeletedTickets in the Patchlab database.',
        @job_id = @jobId OUTPUT;

    EXEC msdb.dbo.sp_add_jobstep
        @job_id = @jobId,
        @step_name = N'Execute PurgeDeletedTickets',
        @subsystem = N'TSQL',
        @database_name = N'Patchlab',
        @command = N'EXEC dbo.PurgeDeletedTickets;',
        @on_success_action = 1, -- quit reporting success
        @on_fail_action = 2;    -- quit reporting failure

    EXEC msdb.dbo.sp_add_jobschedule
        @job_id = @jobId,
        @name = N'Nightly at 2 AM',
        @freq_type = 4,        -- daily
        @freq_interval = 1,    -- every day
        @active_start_time = 020000; -- 02:00:00

    EXEC msdb.dbo.sp_add_jobserver
        @job_id = @jobId,
        @server_name = N'(local)';
END
GO
