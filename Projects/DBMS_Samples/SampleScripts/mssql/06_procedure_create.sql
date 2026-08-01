-- SQL Server: stored procedure.
-- NOTE: uses CREATE OR ALTER instead of DROP+CREATE, because SQL Server
-- requires CREATE PROCEDURE to be the first statement in its batch, and
-- NeoDB Studio sends this file's text as a single batch (no `GO` separator
-- support — `GO` is an SSMS/sqlcmd-only client meta-command).

CREATE OR ALTER PROCEDURE sp_complete_task
    @task_id INT
AS
BEGIN
    UPDATE walkthrough_tasks
    SET is_done = 1
    WHERE task_id = @task_id;
END;
