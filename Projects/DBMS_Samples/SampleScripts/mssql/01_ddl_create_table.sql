-- SQL Server: DDL — create a small standalone demo table.

DROP TABLE IF EXISTS walkthrough_tasks;

CREATE TABLE walkthrough_tasks (
    task_id    INT IDENTITY(1,1) PRIMARY KEY,
    title      NVARCHAR(200) NOT NULL,
    is_done    BIT NOT NULL DEFAULT 0,
    created_at DATETIME2 NOT NULL DEFAULT GETDATE()
);
