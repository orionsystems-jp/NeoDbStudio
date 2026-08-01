-- SQLite: DDL — create a small standalone demo table.

DROP TABLE IF EXISTS walkthrough_tasks;

CREATE TABLE walkthrough_tasks (
    task_id    INTEGER PRIMARY KEY AUTOINCREMENT,
    title      TEXT NOT NULL,
    is_done    INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
