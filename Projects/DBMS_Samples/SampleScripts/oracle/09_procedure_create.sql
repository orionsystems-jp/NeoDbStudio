-- Oracle: stored procedure.
-- NOTE: no trailing "/" is needed here — that's a SQL*Plus/SQL Developer
-- client convention. NeoDB Studio sends this PL/SQL block text directly to
-- ODP.NET as one command, which does not require it.

CREATE OR REPLACE PROCEDURE sp_complete_task(p_task_id IN NUMBER)
AS
BEGIN
    UPDATE walkthrough_tasks
    SET is_done = 1
    WHERE task_id = p_task_id;
END;
