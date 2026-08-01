-- PostgreSQL: execute the procedure and function created above.

CALL sp_complete_task(3);

SELECT fn_pending_task_count() AS pending_count;

-- Verify the procedure's effect
SELECT * FROM walkthrough_tasks ORDER BY task_id;
