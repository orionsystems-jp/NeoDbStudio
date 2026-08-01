-- SQL Server: function — count pending (not-done) tasks.
-- NOTE: CREATE OR ALTER, same reason as the procedure above.

CREATE OR ALTER FUNCTION dbo.fn_pending_task_count()
RETURNS INT
AS
BEGIN
    DECLARE @cnt INT;
    SELECT @cnt = COUNT(*) FROM walkthrough_tasks WHERE is_done = 0;
    RETURN @cnt;
END;
