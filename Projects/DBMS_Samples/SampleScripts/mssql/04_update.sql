-- SQL Server: UPDATE — mark a task as done.

UPDATE walkthrough_tasks
SET is_done = 1
WHERE title = 'Review ER diagram output';

-- Verify
SELECT * FROM walkthrough_tasks WHERE title = 'Review ER diagram output';
