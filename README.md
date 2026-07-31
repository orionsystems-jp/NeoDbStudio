# NeoDB Studio

A Windows desktop database client (WPF client + ASP.NET Core gRPC backend) supporting
eight relational and non-relational database engines through one interface.

## Supported databases

| Provider | Notes |
|---|---|
| MySQL / MariaDB | Full support |
| PostgreSQL | Full support |
| SQL Server (MSSQL) | Full support (execution plan viewer not available — no simple text-based `EXPLAIN` equivalent) |
| Oracle | Full support, via [Oracle.ManagedDataAccess.Core](THIRD_PARTY_NOTICES.md) (Oracle's own license, not MIT/Apache — see notice) |
| SQLite | Full support |
| MongoDB | Read-only (`db.collection.find({...}).limit(n)`) |
| Redis | Arbitrary commands via a generic passthrough |

## Features

- **Schema reverse-engineering** with an MSAGL-rendered ER diagram, split per schema/
  database with a table checklist filter — designed so a real multi-hundred-table
  database renders as one readable diagram per schema instead of a single unreadable
  canvas.
- **Table Designer** with ALTER TABLE script generation across all five relational
  dialects.
- **Schema comparison** between two connections of the same provider, with a generated
  sync script (destructive statements are always emitted commented-out, never executed
  automatically).
- **Excel schema export** (via ClosedXML): a connection-info sheet, per-object-type list
  sheets (tables / views / procedures), a foreign-key relationship sheet, and one detail
  sheet per table.
- **SQL editor** (AvalonEdit) with keyword + live-schema autocomplete, a persistent
  encrypted query history, and a named SQL snippet library.
- **Inline result-grid editing** that generates and executes the corresponding `UPDATE`
  for single-table `SELECT` results.
- **Server-side pagination** for large result sets, with automatic fallback to
  client-side paging when a query can't be safely rewritten.
- **BLOB/CLOB viewer** (hex dump, image preview, save to file).
- **Execution plan viewer** for MySQL/MariaDB/PostgreSQL/SQLite (single `EXPLAIN`) and
  Oracle (`EXPLAIN PLAN FOR` + `DBMS_XPLAN.DISPLAY()`).
- **SSH tunnel support** for connecting to databases behind a bastion host.
- **Encrypted project files and connection history** — connection strings, project
  files (`.neodb`), query history, and SQL snippets are all encrypted at rest with
  Windows DPAPI (current user only).
- **Undo/Redo and Copy/Cut/Paste** on the ER diagram model, via the companion
  UndoRedoKit library (same author, published separately) — most competing tools don't
  support this at all for diagram editing.

## Architecture

Three projects:

- **`NeoDbStudio.Client`** — the WPF desktop UI. On first connection, it launches
  `NeoDbStudio.Api` on demand as a local child process on a dynamically chosen free port
  and talks to it over gRPC; the child process is torn down when the client exits.
- **`NeoDbStudio.Api`** — an ASP.NET Core gRPC service that does the actual database
  I/O (via Dapper, MongoDB.Driver, and StackExchange.Redis) and schema introspection.
- **`NeoDbStudio.Shared`** — the protobuf contract shared by both.

## Docker sandbox

`docker/docker-compose.neodb.yml` brings up all eight supported engines locally
(MySQL, MariaDB, PostgreSQL, SQL Server, Oracle, MongoDB, Redis, plus SQLite as a local
file) pre-seeded with ~1,000 bilingual (English/Japanese) sample rows across
`users` / `products` / `orders` tables, so you can try every feature against real data
without touching a production database:

```bash
cd docker
docker compose -f docker-compose.neodb.yml up -d
```

Ready-made connection project files for each engine are in
[`Projects/DBMS_Samples/`](Projects/DBMS_Samples/).

## Building

Requires the .NET 8 SDK and Windows (the client is WPF; the API server itself is
cross-platform but is only exercised on Windows in this repo).

```bash
dotnet build NeoDbStudio.sln -c Debug
```

## License

MIT — see [LICENSE](LICENSE). Third-party dependency licenses (including two
exceptions that are **not** MIT/Apache) are listed in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
