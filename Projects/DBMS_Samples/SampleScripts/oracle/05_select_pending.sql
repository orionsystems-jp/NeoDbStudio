-- Oracle: SELECT — only pending (not done) tasks.

SELECT task_id, title, created_at
FROM walkthrough_tasks
WHERE is_done = 0
ORDER BY created_at;
