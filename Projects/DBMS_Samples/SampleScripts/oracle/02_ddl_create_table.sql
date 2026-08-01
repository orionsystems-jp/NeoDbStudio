-- Oracle: DDL — create a small standalone demo table.

CREATE TABLE walkthrough_tasks (
    task_id    NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    title      VARCHAR2(200) NOT NULL,
    is_done    NUMBER(1) DEFAULT 0 NOT NULL,
    created_at DATE DEFAULT SYSDATE NOT NULL
);
