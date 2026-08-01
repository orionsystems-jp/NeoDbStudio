-- Oracle: call the function created above from a SELECT.

SELECT fn_pending_task_count() AS pending_count FROM DUAL;
