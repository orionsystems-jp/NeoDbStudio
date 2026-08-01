-- PostgreSQL: stored procedure (PostgreSQL 11+, invoked via CALL).

DROP PROCEDURE IF EXISTS sp_complete_task(INT);

CREATE PROCEDURE sp_complete_task(p_task_id INT)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE walkthrough_tasks
    SET is_done = TRUE
    WHERE task_id = p_task_id;
END;
$$;
