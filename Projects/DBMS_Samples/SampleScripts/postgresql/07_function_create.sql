-- PostgreSQL: function — count pending (not-done) tasks.

DROP FUNCTION IF EXISTS fn_pending_task_count();

CREATE FUNCTION fn_pending_task_count() RETURNS INT
LANGUAGE plpgsql
AS $$
DECLARE
    cnt INT;
BEGIN
    SELECT COUNT(*) INTO cnt FROM walkthrough_tasks WHERE is_done = FALSE;
    RETURN cnt;
END;
$$;
