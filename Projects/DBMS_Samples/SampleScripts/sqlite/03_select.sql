-- SQLite: SELECT — a few representative read queries.
-- NOTE: NeoDB Studio's result grid only shows the FIRST statement's result set
-- when multiple statements are executed together. Select and run (F5) one
-- query at a time below to see each one's results.

-- All rows
SELECT * FROM walkthrough_tasks ORDER BY task_id;

-- Only pending (not done) tasks
SELECT task_id, title, created_at
FROM walkthrough_tasks
WHERE is_done = 0
ORDER BY created_at;

-- Count by status
SELECT is_done, COUNT(*) AS task_count
FROM walkthrough_tasks
GROUP BY is_done;
