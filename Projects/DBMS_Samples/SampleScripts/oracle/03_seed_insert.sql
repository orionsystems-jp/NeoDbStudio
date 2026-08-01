-- Oracle: SEED — insert sample rows into walkthrough_tasks.
-- Uses INSERT ... SELECT ... UNION ALL (a single statement, as required) rather
-- than INSERT ALL: Oracle's multitable INSERT ALL form evaluates a
-- GENERATED ALWAYS AS IDENTITY default only once for all branches sharing the
-- same driving row, so every branch gets the SAME generated task_id and the
-- 2nd+ row fails on the primary key. A plain multi-row INSERT ... SELECT does
-- not have this problem.

INSERT INTO walkthrough_tasks (title, is_done)
SELECT 'Write project README', 1 FROM DUAL UNION ALL
SELECT 'Set up Docker sandbox', 1 FROM DUAL UNION ALL
SELECT 'Review ER diagram output', 0 FROM DUAL UNION ALL
SELECT 'Export schema to Excel', 0 FROM DUAL UNION ALL
SELECT 'Publish to GitHub', 0 FROM DUAL;
