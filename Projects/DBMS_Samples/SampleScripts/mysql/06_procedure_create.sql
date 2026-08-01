-- MySQL: stored procedure + function.
-- NOTE: run this WITHOUT a `DELIMITER` line. `DELIMITER` is a mysql-CLI-only
-- meta-command; NeoDB Studio sends this text verbatim to the server via
-- MySqlConnector, which accepts a single CREATE PROCEDURE/FUNCTION statement
-- (including its semicolons) as one command, so no delimiter switch is needed
-- (or possible) here.

DROP PROCEDURE IF EXISTS sp_complete_task;

CREATE PROCEDURE sp_complete_task(IN p_task_id INT)
BEGIN
    UPDATE walkthrough_tasks
    SET is_done = 1
    WHERE task_id = p_task_id;
END;
