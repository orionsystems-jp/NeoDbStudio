-- SQL Server: DELETE — remove a completed task.

DELETE FROM walkthrough_tasks
WHERE title = 'Set up Docker sandbox' AND is_done = 1;

-- Verify
SELECT * FROM walkthrough_tasks ORDER BY task_id;
