-- Oracle: SELECT — count by status.

SELECT is_done, COUNT(*) AS task_count
FROM walkthrough_tasks
GROUP BY is_done;
