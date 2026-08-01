# Sample scripts

One folder per supported DBMS provider, each demonstrating the same walkthrough — DDL,
seed data, SELECT/UPDATE/DELETE, and a stored procedure + function (where the engine has
that concept) — against the Docker sandbox started via
[`docker/docker-compose.neodb.yml`](../../../docker/docker-compose.neodb.yml). Every file
here was executed against the real running sandbox via the real API before being
committed, so they're known to actually work, not just believed to.

Open a `.neodb` project from [`../`](../) (the sibling `DBMS_Samples` folder) for the
provider you want, then open each numbered file in order (**File → Open SQL File**) and
run it with F5.

| Folder | Covers | Stored procedure / function support |
|---|---|---|
| `mysql/` | DDL, SEED, SELECT/UPDATE/DELETE, procedure, function | Yes |
| `mariadb/` | Same as MySQL (MySQL-compatible syntax) | Yes |
| `postgresql/` | DDL, SEED, SELECT/UPDATE/DELETE, procedure (PG11+), function | Yes |
| `mssql/` | DDL, SEED, SELECT/UPDATE/DELETE, stored procedure, scalar function | Yes |
| `oracle/` | DDL, SEED, SELECT/UPDATE/DELETE, procedure, function | Yes |
| `sqlite/` | DDL, SEED, SELECT/UPDATE/DELETE | No — SQLite has no procedural SQL |
| `mongodb/` | `find()` queries only against the pre-seeded collections | No — and no insert/update/delete either (see `mongodb/README.md`) |
| `redis/` | Key/hash commands + an `EVAL` Lua script as the closest thing to a "procedure" | No native concept; `EVAL` is the closest equivalent |

Cassandra and ClickHouse are **not** included here: both appear in the Docker sandbox
(for potential future use) but NeoDB Studio's API has no connection implementation for
either provider yet, so there is nothing that would successfully run.

## Cross-dialect gotchas discovered while verifying these

These aren't NeoDB Studio bugs — they're real per-engine differences that surprised the
first pass through this exercise, worth knowing before you write your own queries:

- **Oracle accepts exactly one statement per execution.** MySQL/MariaDB/PostgreSQL/SQL
  Server/SQLite will happily run several `;`-separated statements in one go (though the
  result grid only shows the *first* statement's result set — see below); Oracle's
  driver rejects anything after the first statement outright. The `oracle/` folder is
  numbered one-statement-per-file for this reason.
- **The result grid only shows the first statement's results** when a file has multiple
  SELECTs (MySQL/MariaDB/PostgreSQL/SQL Server/SQLite). Run one query at a time (select
  the text, F5) to see each one.
- **`DELIMITER` (MySQL) and `GO` (SQL Server) are client-tool conventions, not something
  NeoDB Studio (or the underlying driver) understands.** Don't use them. For MySQL,
  a `CREATE PROCEDURE`/`FUNCTION` body's internal semicolons are sent as-is in one
  command. For SQL Server, `CREATE PROCEDURE`/`FUNCTION` must be the *first* statement
  in its batch, so the samples use `CREATE OR ALTER` instead of `DROP ... ; CREATE ...`.
- **Oracle's `INSERT ALL` multitable form doesn't generate distinct identity values per
  branch** — every branch shared the same `GENERATED ALWAYS AS IDENTITY` value and the
  2nd row failed on the primary key. The Oracle seed file uses a plain
  `INSERT ... SELECT ... UNION ALL` instead, which doesn't have this problem.
- **MongoDB's and Redis's query boxes have no comment syntax and no tolerance for extra
  lines** — each file must contain nothing but the bare query/command, with any
  explanation kept in that folder's own `README.md` instead.
