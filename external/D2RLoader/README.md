# D2RLoader upstream source slot

This directory is reserved for an authorized upstream D2RLoader source checkout.

Status checked on 2026-08-08:

- Version under test: `1.0.1-beta`
- The public release archive contains `D2RLoader.exe`, `D2RCore.dll`, and configuration only.
- No D2RLoader/D2RCore source repository was found among TOM_RUS's public GitHub repositories.
- The archive does not contain a source license or build instructions.
- The embedded `D2RL` resource is runtime integration/reference data used by the documented `d2rl` command; it is not a source tree.

Do not place decompiled core code here or represent it as upstream source. Before adopting a source checkout, record all of the following:

1. Author-provided repository URL and immutable commit/tag.
2. License permitting local modification and the intended distribution.
3. Required compiler, SDK, submodules, and reproducible build command.
4. SHA-256 comparison between the official build and a clean local build where reproducibility is supported.

Until those conditions are met, Jeongman Server (정만서버) uses the hash-pinned upstream binaries supplied separately by each operator. Project-owned customization belongs in mod data, JSON patches, and a source-built plugin after the public Plugin SDK is obtained.

References:

- https://diablo2.io/forums/d2rloader-1-0-0-beta-release-t1840130.html
- https://github.com/tomrus88?tab=repositories
