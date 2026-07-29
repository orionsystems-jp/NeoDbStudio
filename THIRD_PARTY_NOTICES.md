# Third-Party Notices

NeoDB Studio itself is licensed under the MIT License (see [LICENSE](LICENSE)).
It depends on the following third-party NuGet packages. License information below
was retrieved directly from each package's official NuGet metadata / bundled license
file on 2026-07-29 and should be re-verified against the actual package versions in
use before any redistribution.

## Permissively licensed dependencies (MIT)

| Package | Version | License |
|---|---|---|
| AutomaticGraphLayout.WpfGraphControl | 1.1.12 | MIT |
| AvalonEdit | 6.3.0 | MIT |
| CommunityToolkit.Mvvm | 8.2.2 | MIT |
| Microsoft.Data.SqlClient | 5.2.0 | MIT |
| Microsoft.Data.Sqlite | 8.0.3 | MIT |
| Microsoft.Extensions.DependencyInjection | 8.0.0 | MIT |
| ModernWpfUI | 0.9.6 | MIT |
| MySqlConnector | 2.3.5 | MIT |
| SSH.NET | 2024.2.0 | MIT |
| StackExchange.Redis | 2.7.33 | MIT |
| System.Data.Odbc | 8.0.0 | MIT |
| System.Security.Cryptography.ProtectedData | 8.0.0 | MIT |

## Apache License 2.0 / BSD-3-Clause

| Package | Version | License |
|---|---|---|
| Dapper | 2.1.35 | Apache-2.0 |
| Google.Protobuf | 3.25.1 | BSD-3-Clause |
| Grpc.AspNetCore | 2.60.0 | Apache-2.0 |
| Grpc.Net.Client | 2.60.0 | Apache-2.0 |
| Grpc.Tools | 2.60.0 | Apache-2.0 |
| MongoDB.Driver | 2.24.0 | Apache-2.0 |

## Other permissive license

| Package | Version | License |
|---|---|---|
| Npgsql | 8.0.3 | PostgreSQL License (permissive, BSD/MIT-style) |

## ⚠️ Non-MIT/Apache dependencies requiring special attention

These two packages carry license terms that differ materially from the rest of the
dependency tree and from this project's own MIT license. They are noted separately
because a casual reader assuming "everything here is MIT/Apache" would be wrong.

### Dirkster.AvalonDock 4.72.1 — **Ms-PL (Microsoft Public License)**
Confirmed from the `LICENSE` file bundled in the NuGet package. Ms-PL is an OSI-approved
license but has different terms from MIT/Apache-2.0 (notably around patent grants and
distribution of derivative works in source vs. compiled form). Review the full Ms-PL
text before redistributing modified versions of AvalonDock itself.

### Oracle.ManagedDataAccess.Core 23.5.1 — **Oracle Free Distribution, Hosting, and Use Terms and Conditions**
This is **not an OSI-approved open source license**. Confirmed from the `LICENSE.txt`
file bundled in the NuGet package. Key restrictions to be aware of:
- Reverse engineering, disassembly, or decompilation is prohibited.
- Redistribution must not charge additional fees specifically for the Program itself
  (bundling it as part of a larger for-fee product/service is permitted).
- The license is Oracle's own terms, not MIT/Apache/BSD/GPL — it does not grant the
  same modification rights an MIT dependency would.

**Recommendation:** if full OSS license purity (e.g., for distros that reject
non-OSI-approved licenses) is a goal, consider isolating Oracle support behind an
optional/plugin boundary, or clearly documenting this exception in the top-level
README so downstream users are not surprised.

## Unconfirmed — recommend manual verification before public release

### AutomaticGraphLayout 1.1.12 / AutomaticGraphLayout.Drawing 1.1.12
The NuGet packages for these two contain **no license metadata at all** (empty/placeholder
`<description>`, no `<license>` or `<licenseUrl>` element, no project URL). They appear to
be a third-party repackaging of Microsoft's open-source MSAGL project
(https://github.com/microsoft/automatic-graph-layout, MIT licensed), based on matching
namespaces/assembly names with `AutomaticGraphLayout.WpfGraphControl` (which *does*
declare MIT in its own nuspec). This is a reasonable inference, **not a confirmed fact**
about these two specific packages — verify against the upstream repository or the
package author before stating a license for them with confidence.

## OrionSystems.UndoRedoKit (this author's own sibling project)
Not a third-party dependency in the usual sense — it is another project by the same
author (`F:\OSS\UndoRedoKit`), referenced via project reference rather than a published
NuGet package. It currently has no LICENSE file of its own; add one (MIT, matching this
project, is the natural choice) before treating it as independently redistributable.
