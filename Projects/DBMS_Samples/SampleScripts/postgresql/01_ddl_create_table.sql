-- PostgreSQL: DDL — create a small standalone demo table.

DROP TABLE IF EXISTS walkthrough_tasks;

CREATE TABLE walkthrough_tasks (
    task_id    SERIAL PRIMARY KEY,
    title      VARCHAR(200) NOT NULL,
    is_done    BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
