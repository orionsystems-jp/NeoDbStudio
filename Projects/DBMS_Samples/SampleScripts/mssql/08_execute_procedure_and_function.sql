-- SQL Server: execute the procedure and function created above.

EXEC sp_complete_task @task_id = 3;

SELECT dbo.fn_pending_task_count() AS pending_count;

-- Verify the procedure's effect
SELECT * FROM walkthrough_tasks ORDER BY task_id;
