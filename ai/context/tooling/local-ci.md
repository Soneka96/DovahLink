# Local CI bootstrap

`tooling/run-local-ci.ps1` mirrors the repository's hosted CI command payloads locally on Windows
before a push, using a pinned, disposable toolchain -- a vcpkg checkout cloned at a pinned baseline
commit, plus pinned CMake and Ninja versions -- kept under `%LOCALAPPDATA%\Temp\DovahLink\`.

## Disposable state vs. genuine failures

That pinned toolchain state is entirely disposable: the script (re)creates it deterministically from
the pinned baseline and never depends on it surviving between runs. Because it lives outside the
repository in a temp directory, it can become corrupt or incomplete for reasons unrelated to the
repository's own source or build correctness -- an interrupted build, a killed process, a full disk
-- and the underlying tool (vcpkg, CMake) does not detect or repair that corruption on its own.

- The local CI bootstrap must distinguish disposable bootstrap/cache state from a genuine
  source/build failure. When the pinned disposable state is structurally corrupt or incomplete, the
  script repairs or recreates that state deterministically rather than requiring a developer to
  manually diagnose it. For example, a per-port version checkout under vcpkg's
  `buildtrees/versioning_/versions/<port>/<sha>/` cache that is missing both its `vcpkg.json`
  manifest and a legacy `CONTROL` file is corrupt and safe to delete; vcpkg regenerates it on demand
  from its own pinned git history the next time that version is needed.
- Detection must be narrow: remove a cache only once a concrete corruption signature is found inside
  it, never on a blanket schedule and never merely because a later build step failed. Do not blindly
  delete arbitrary user caches or global installations -- only repository-owned, disposable temp
  state may be automatically recreated. Leave any cache that is valid and independent of the
  detected corruption alone (for example vcpkg's binary package cache, which caches built package
  output rather than the per-version source checkout the corruption above affects).
- Do not mask a genuine vcpkg/package build error behind a retry loop. Recovery is narrowly
  triggered by the specific corruption signature that was actually detected, not a general
  "retry until it passes" loop; a real dependency build failure must still fail visibly.
- A required build tool (for example Ninja) must be resolved explicitly by the bootstrap --
  verifying the pinned executable exists and passing its resolved path directly to the build tool
  invocation -- rather than relying on unrelated machine PATH search order. A build directory's
  CMake cache can retain a stale or absent `CMAKE_MAKE_PROGRAM` entry from an earlier environment or
  an aborted configure; passing it explicitly (`-DCMAKE_MAKE_PROGRAM=<resolved path>`) on every
  configure invocation overrides that stale cache value deterministically instead of depending on
  CMake's auto-detection succeeding again.
