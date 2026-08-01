-- MariaDB: DDL — create a small standalone demo table.
-- Uses a fresh table name (walkthrough_tasks) so it doesn't collide with the
-- existing seeded users/products/orders tables in the neodb sandbox.

DROP TABLE IF EXISTS walkthrough_tasks;

CREATE TABLE walkthrough_tasks (
    task_id    INT AUTO_INCREMENT PRIMARY KEY,
    title      VARCHAR(200) NOT NULL,
    is_done    TINYINT(1) NOT NULL DEFAULT 0,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);
