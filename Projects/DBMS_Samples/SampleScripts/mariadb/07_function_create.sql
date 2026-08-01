-- MariaDB: function — count pending (not-done) tasks.

DROP FUNCTION IF EXISTS fn_pending_task_count;

CREATE FUNCTION fn_pending_task_count() RETURNS INT
DETERMINISTIC READS SQL DATA
BEGIN
    DECLARE cnt INT;
    SELECT COUNT(*) INTO cnt FROM walkthrough_tasks WHERE is_done = 0;
    RETURN cnt;
END;
