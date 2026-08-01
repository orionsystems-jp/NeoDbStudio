-- Oracle: execute the procedure created above (via an anonymous PL/SQL block —
-- Oracle procedures are not called with a bare CALL/EXEC in plain SQL text).

BEGIN
    sp_complete_task(3);
END;
