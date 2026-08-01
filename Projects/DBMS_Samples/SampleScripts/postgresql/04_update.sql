-- PostgreSQL: UPDATE — mark a task as done.

UPDATE walkthrough_tasks
SET is_done = TRUE
WHERE title = 'Review ER diagram output';

-- Verify
SELECT * FROM walkthrough_tasks WHERE title = 'Review ER diagram output';
