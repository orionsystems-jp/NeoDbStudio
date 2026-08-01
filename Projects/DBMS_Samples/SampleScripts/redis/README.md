# Redis samples

Unlike the SQL dialects, NeoDB Studio's Redis command box has **no comment syntax at
all** — the whole input (including newlines) is tokenized as a single command line, so
**don't add `--` comments or extra lines inside the `.sql` files**; each one must contain
nothing but the bare command, or the leading text gets sent to Redis as (part of) the
command name and fails.

| File | Demonstrates |
|---|---|
| `01_set.sql` / `02_get.sql` | Simple string key SET/GET |
| `03_hset.sql` / `04_hgetall.sql` | A hash — the closest Redis analog to a table "row" |
| `05_keys.sql` | Listing keys by pattern |
| `06_expire.sql` | TTL / key expiration |
| `07_del.sql` | Deleting a key |
| `08_eval_lua_script.sql` | `EVAL` — a Lua script run server-side, the closest Redis has to a "stored procedure" (Redis has no native stored procedure/function concept) |

Connection requires `allowAdmin=true` in the connection string (already set in
`redis_project.neodb`) because `KEYS` is an admin-flagged command in StackExchange.Redis.
