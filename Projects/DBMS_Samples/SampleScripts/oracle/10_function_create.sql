-- Oracle: function — count pending (not-done) tasks.

CREATE OR REPLACE FUNCTION fn_pending_task_count RETURN NUMBER
AS
    cnt NUMBER;
BEGIN
    SELECT COUNT(*) INTO cnt FROM walkthrough_tasks WHERE is_done = 0;
    RETURN cnt;
END;
