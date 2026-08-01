# MongoDB samples

**Read-only.** NeoDB Studio's MongoDB support only understands
`db.<collection>.find(<filter JSON>).limit(<n>)` — there is no insert/update/delete
capability for MongoDB in this version. The query box parser is a strict single-expression
match, so **do not put `--` comments or blank lines inside the `.sql` files** — each file
must contain nothing but the bare `db.collection.find(...)` expression, or it won't parse.

These samples query the collections already seeded by the Docker sandbox (`users`,
`products`, `orders` — ~1,000 bilingual documents each), rather than a from-scratch
walkthrough collection like the relational samples in the sibling folders.

| File | What it shows |
|---|---|
| `01_find_all_users.sql` | All documents in `users`, limited to 10 |
| `02_find_with_filter.sql` | `products` where `unit_price > 300000` |
| `03_find_active_users.sql` | `users` where `is_active = 1` |
