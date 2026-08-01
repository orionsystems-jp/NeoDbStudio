-- Oracle: drop the demo table if a previous run left it behind.
-- Oracle has no "DROP TABLE IF EXISTS" (pre-23c), so the classic idiom is an
-- anonymous PL/SQL block that swallows ORA-00942 ("table or view does not exist").
--
-- IMPORTANT: unlike MySQL/PostgreSQL/SQL Server, Oracle's driver only accepts
-- ONE statement per execution — run each numbered file here separately (F5
-- one at a time), never concatenated.

BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE walkthrough_tasks';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE != -942 THEN
            RAISE;
        END IF;
END;
