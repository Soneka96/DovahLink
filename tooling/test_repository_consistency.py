"""Guard release, workflow, and future-state documentation consistency."""

import json
import re
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent


class RepositoryConsistencyTests(unittest.TestCase):
    """Verify that release-facing configuration and documentation remain aligned."""

    def test_target_identity_and_revision_architecture_is_explicit(self) -> None:
        """Keep target state ownership explicit in the current canonical contract."""
        architecture = self._read("ARCHITECTURE.md")
        schema = self._read("protocol/schema/README.md")
        normalized_architecture = self._normalize_whitespace(architecture)

        for identity in (
            "`bridgeInstanceId`",
            "`playContextId`",
            "`clientId`",
            "`sessionId`",
        ):
            self.assertIn(identity, architecture)
        self.assertNotIn("gameInstanceId", architecture)

        for required_phrase in (
            "A bridge restart creates a new identity",
            "A client reconnect creates a new `sessionId` without silently changing its `clientId`",
            "loading another save creates a new `playContextId`",
            "It is valid only for that socket and is invalidated when the connection ends",
            "one authoritative state store for the active play context",
            "captured once and shared with subscribed clients",
            "adding a client must not repeat equivalent Skyrim reads",
            "within one state area and `playContextId`",
            "It advances only when that authoritative state changes",
            "Sending or requesting another snapshot does not advance the revision",
            "reconnecting does not create a new authoritative revision",
            "use `playContextId` and the state-area revision together to reject stale state",
            "invalidates the previous context's state and establishes fresh authoritative state",
            "must not be implemented by silently reinterpreting messages from the previously "
            "published experimental release",
            "Transport location is not identity",
            "receives no undocumented protocol behavior or privileged access",
            "Screens, dashboard modules, orientation, widget layout",
        ):
            self.assertIn(required_phrase, normalized_architecture)

        self.assertIn(
            "belongs to that authoritative bridge instance, play context, and state area",
            schema,
        )
        for target_identity in ("bridgeInstanceId", "playContextId", "clientId"):
            self.assertIn(target_identity, schema)
        # The retired protocol-generation compatibility model must not silently creep back in.
        for retired_term in (
            "protocolVersion",
            "supportedProtocolVersions",
            "selectedProtocolVersion",
            "unsupported_version",
        ):
            self.assertNotIn(retired_term, schema)
            self.assertNotIn(retired_term, architecture)
        self.assertIn(
            "The official Flutter application is one client of the canonical protocol",
            normalized_architecture,
        )
        self.assertIn(
            "Protocol messages remain presentation-independent",
            normalized_architecture,
        )

    def test_bridge_ci_covers_protocol_changes_with_least_privilege(self) -> None:
        """Require protocol triggers, read-only permissions, and non-persistent checkout credentials."""
        workflow = self._read(".github/workflows/bridge-ci.yml")
        expected_paths = {
            '- "bridge/**"',
            '- "protocol/**"',
            '- ".github/workflows/bridge-ci.yml"',
        }

        push_block = self._yaml_block(workflow, "  push:")
        paths_block = self._yaml_block(push_block, "    paths:")
        self.assertEqual(
            {line.strip() for line in paths_block.splitlines()[1:] if line.strip()},
            expected_paths,
        )

        # Pull-request CI must always post a status regardless of changed files: a path filter here
        # would let a PR outside these paths skip this workflow entirely while it is still a
        # required branch-protection check, leaving the PR stuck waiting on a status that never
        # arrives.
        self.assertNotIn("paths:", self._yaml_block(workflow, "  pull_request:"))

        permissions = self._yaml_block(workflow, "permissions:")
        self.assertEqual(permissions, "permissions:\n  contents: read")
        self.assertEqual(len(re.findall(r"(?m)^\s*permissions:", workflow)), 1)

        self.assertEqual(len(re.findall(r"uses: actions/checkout@", workflow)), 1)
        checkout = self._yaml_block(
            workflow,
            "      - uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6",
        )
        self.assertEqual(
            checkout,
            "      - uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6\n"
            "        with:\n"
            "          persist-credentials: false",
        )

    def test_bridge_ci_covers_release_validation_and_reusable_diagnostics(self) -> None:
        """Require release coverage, dependency caching, cancellation, and failure diagnostics."""
        workflow = self._read(".github/workflows/bridge-ci.yml")

        self.assertIn("  workflow_dispatch:", workflow)
        self.assertIn(
            "  group: bridge-ci-${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}",
            workflow,
        )
        self.assertIn("  cancel-in-progress: true", workflow)
        self.assertIn("timeout-minutes: 30", workflow)
        # A job-level env: cannot reference the runner context (unresolved until a runner picks up
        # the job's steps); VCPKG_DEFAULT_BINARY_CACHE is computed in its own step instead.
        self.assertNotIn(
            "    env:\n      VCPKG_DEFAULT_BINARY_CACHE:",
            workflow,
        )
        self.assertIn("      - name: Set VCPKG_DEFAULT_BINARY_CACHE", workflow)
        self.assertIn(
            'run: echo "VCPKG_DEFAULT_BINARY_CACHE=$env:RUNNER_TEMP\\vcpkg-binary-cache" >> $env:GITHUB_ENV',
            workflow,
        )
        self.assertIn("      - name: Prepare vcpkg binary cache", workflow)
        self.assertIn(
            'New-Item -ItemType Directory -Force -Path "$env:RUNNER_TEMP\\vcpkg-binary-cache"',
            workflow,
        )
        self.assertNotIn("choco install", workflow)
        self.assertNotIn("ChocolateyInstall", workflow)
        self.assertIn("      - name: Install pinned CMake", workflow)
        self.assertIn('$cmakeVersion = "4.4.2"', workflow)
        self.assertIn(
            'Invoke-WebRequest -Uri "https://github.com/Kitware/CMake/releases/download/v$cmakeVersion/$cmakeArchive"',
            workflow,
        )
        self.assertIn(
            '$cmakeArchiveSha256 = "e8139d85b3813bc38833142ae1940472e9a587e9b5d2718ac1804c60f4e57a64"',
            workflow,
        )
        self.assertIn(
            'Get-FileHash -Path "$env:RUNNER_TEMP\\$cmakeArchive" -Algorithm SHA256', workflow
        )
        self.assertIn("if ($actualSha256 -ne $cmakeArchiveSha256) {", workflow)
        # A regression that swaps this for Write-Warning/Write-Host would silently downgrade the
        # check to a no-op while leaving the `if` condition above intact -- assert the actual
        # enforcement statement, not just the condition that guards it.
        self.assertIn(
            'throw "CMake archive hash mismatch: expected $cmakeArchiveSha256, got $actualSha256."',
            workflow,
        )
        self.assertLess(
            workflow.index("Get-FileHash -Path"),
            workflow.index("Expand-Archive -Path"),
            "the CMake archive must be hash-verified before extraction",
        )
        self.assertIn("      - name: Verify pinned Ninja is preinstalled", workflow)
        self.assertIn('if ($ninjaVersion -ne "1.13.2") {', workflow)
        self.assertIn("ninja --version", workflow)
        self.assertLess(
            workflow.index("Set VCPKG_DEFAULT_BINARY_CACHE"),
            workflow.index("Prepare vcpkg binary cache"),
        )
        self.assertLess(
            workflow.index("Prepare vcpkg binary cache"),
            workflow.index("Restore vcpkg binary cache"),
        )
        self.assertLess(
            workflow.index("ninja --version"),
            workflow.index("Configure Debug"),
        )
        self.assertIn("VCPKG_DEFAULT_BINARY_CACHE", workflow)
        self.assertIn("uses: actions/cache@caa296126883cff596d87d8935842f9db880ef25 # v5", workflow)
        self.assertIn("path: ${{ runner.temp }}\\vcpkg-binary-cache", workflow)
        self.assertIn("key: ${{ runner.os }}-vcpkg-", workflow)
        self.assertIn("hashFiles('bridge/vcpkg.json', 'bridge/vcpkg-configuration.json')", workflow)

        for step_name in (
            "Configure Debug",
            "Build Debug",
            "Test Debug",
            "Configure Release",
            "Build Release",
            "Test Release",
        ):
            self.assertIn(f"      - name: {step_name}", workflow)

        self.assertIn("run: cmake --preset windows-x64-release", workflow)
        self.assertIn("run: cmake --build --preset windows-x64-release", workflow)
        self.assertIn(
            "run: ctest --test-dir build/windows-x64-release --output-on-failure",
            workflow,
        )
        self.assertIn("if: failure()", workflow)
        self.assertIn("uses: actions/upload-artifact@b7c566a772e6b6bfb58ed0dc250532a479d7789f # v6", workflow)
        self.assertIn(
            "Maintained stable release; no stable Node 24 replacement is available yet.",
            workflow,
        )
        self.assertIn("name: bridge-ci-diagnostics-${{ github.run_id }}", workflow)
        self.assertIn("bridge/build/windows-x64-debug/Testing/", workflow)
        self.assertIn("bridge/build/windows-x64-release/Testing/", workflow)

    def test_app_ci_covers_flutter_quality_and_windows_build(self) -> None:
        """Require Flutter generation, analysis, tests, and desktop build coverage."""
        workflow = self._read(".github/workflows/app-ci.yml")
        expected_paths = {
            '- "app/**"',
            '- "protocol/**"',
            '- "sdk/dart/dovahlink_client/**"',
            '- ".github/workflows/app-ci.yml"',
        }

        push_block = self._yaml_block(workflow, "  push:")
        self.assertIn("    branches: [main]", push_block)
        paths_block = self._yaml_block(push_block, "    paths:")
        self.assertEqual(
            {line.strip() for line in paths_block.splitlines()[1:] if line.strip()},
            expected_paths,
        )

        # Pull-request CI must always post a status regardless of changed files: a path filter here
        # would let a PR outside these paths skip this workflow entirely while it is still a
        # required branch-protection check, leaving the PR stuck waiting on a status that never
        # arrives.
        self.assertNotIn("paths:", self._yaml_block(workflow, "  pull_request:"))

        permissions = self._yaml_block(workflow, "permissions:")
        self.assertEqual(permissions, "permissions:\n  contents: read")
        checkout = self._yaml_block(
            workflow,
            "      - uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6",
        )
        self.assertEqual(
            checkout,
            "      - uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6\n"
            "        with:\n"
            "          persist-credentials: false",
        )
        self.assertIn("    runs-on: windows-2022", workflow)
        self.assertIn("    timeout-minutes: 20", workflow)
        self.assertIn("        shell: pwsh", workflow)
        self.assertIn("  workflow_dispatch:", workflow)
        self.assertIn(
            "  group: app-ci-${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}",
            workflow,
        )
        self.assertIn("  cancel-in-progress: true", workflow)
        self.assertIn("uses: subosito/flutter-action@1a449444c387b1966244ae4d4f8c696479add0b2 # v2", workflow)
        self.assertIn("channel: stable", workflow)
        self.assertIn("cache: true", workflow)
        step_names_and_commands = (
            ("Restore Dart SDK dependencies", "sdk/dart/dovahlink_client", "dart pub get"),
            (
                "Generate Dart SDK sources",
                "sdk/dart/dovahlink_client",
                "dart run build_runner build",
            ),
            ("Restore Flutter dependencies", "app", "flutter pub get"),
            ("Generate Dart sources", "app", "dart run build_runner build"),
            ("Analyze Flutter client", "app", "flutter analyze"),
            ("Test Flutter client", "app", "flutter test"),
            ("Build Windows client", "app", "flutter build windows --debug"),
        )
        step_positions = []
        for step_name, working_directory, command in step_names_and_commands:
            step = self._yaml_block(workflow, f"      - name: {step_name}")
            self.assertIn(f"        working-directory: {working_directory}", step)
            self.assertIn(f"        run: {command}", step)
            self.assertNotIn("continue-on-error:", step)
            step_positions.append(workflow.index(f"      - name: {step_name}"))
        self.assertEqual(step_positions, sorted(step_positions))

    def test_integration_ci_uses_pinned_harness_and_dotnet_scenarios(self) -> None:
        """Require the independent .NET scenarios to run against the pinned bridge harness."""
        workflow = self._read(".github/workflows/integration-ci.yml")
        expected_paths = {
            '- "bridge/**"',
            '- "integration/**"',
            '- "protocol/**"',
            '- ".github/workflows/integration-ci.yml"',
        }

        push_block = self._yaml_block(workflow, "  push:")
        self.assertIn("    branches: [main]", push_block)
        paths_block = self._yaml_block(push_block, "    paths:")
        self.assertEqual(
            {line.strip() for line in paths_block.splitlines()[1:] if line.strip()},
            expected_paths,
        )

        # Pull-request CI must always post a status regardless of changed files: a path filter here
        # would let a PR outside these paths skip this workflow entirely while it is still a
        # required branch-protection check, leaving the PR stuck waiting on a status that never
        # arrives.
        self.assertNotIn("paths:", self._yaml_block(workflow, "  pull_request:"))

        self.assertEqual(
            self._yaml_block(workflow, "permissions:"),
            "permissions:\n  contents: read",
        )
        checkout = self._yaml_block(
            workflow,
            "      - uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6",
        )
        self.assertEqual(
            checkout,
            "      - uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6\n"
            "        with:\n"
            "          persist-credentials: false",
        )
        self.assertIn("    runs-on: windows-2022", workflow)
        self.assertIn("    timeout-minutes: 30", workflow)
        self.assertIn("        shell: pwsh", workflow)
        self.assertIn("  workflow_dispatch:", workflow)
        self.assertIn(
            "  group: integration-ci-${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}",
            workflow,
        )
        self.assertIn("  cancel-in-progress: true", workflow)
        self.assertNotIn("choco install", workflow)
        self.assertNotIn("ChocolateyInstall", workflow)
        self.assertIn('$cmakeVersion = "4.4.2"', workflow)
        # A job-level env: cannot reference the runner context (unresolved until a runner picks up
        # the job's steps); VCPKG_DEFAULT_BINARY_CACHE is computed in its own step instead.
        self.assertNotIn(
            "    env:\n      VCPKG_DEFAULT_BINARY_CACHE:",
            workflow,
        )
        self.assertIn("      - name: Set VCPKG_DEFAULT_BINARY_CACHE", workflow)
        self.assertIn(
            'run: echo "VCPKG_DEFAULT_BINARY_CACHE=$env:RUNNER_TEMP\\vcpkg-binary-cache" >> $env:GITHUB_ENV',
            workflow,
        )
        self.assertIn("      - name: Prepare vcpkg binary cache", workflow)
        self.assertIn(
            'New-Item -ItemType Directory -Force -Path "$env:RUNNER_TEMP\\vcpkg-binary-cache"',
            workflow,
        )
        self.assertIn(
            '$cmakeArchiveSha256 = "e8139d85b3813bc38833142ae1940472e9a587e9b5d2718ac1804c60f4e57a64"',
            workflow,
        )
        self.assertIn(
            'Get-FileHash -Path "$env:RUNNER_TEMP\\$cmakeArchive" -Algorithm SHA256', workflow
        )
        self.assertIn("if ($actualSha256 -ne $cmakeArchiveSha256) {", workflow)
        # A regression that swaps this for Write-Warning/Write-Host would silently downgrade the
        # check to a no-op while leaving the `if` condition above intact -- assert the actual
        # enforcement statement, not just the condition that guards it.
        self.assertIn(
            'throw "CMake archive hash mismatch: expected $cmakeArchiveSha256, got $actualSha256."',
            workflow,
        )
        self.assertLess(
            workflow.index("Get-FileHash -Path"),
            workflow.index("Expand-Archive -Path"),
            "the CMake archive must be hash-verified before extraction",
        )
        self.assertIn("      - name: Install pinned CMake", workflow)
        self.assertIn("      - name: Verify pinned Ninja is preinstalled", workflow)
        self.assertIn('if ($ninjaVersion -ne "1.13.2") {', workflow)
        self.assertIn("ninja --version", workflow)
        self.assertLess(
            workflow.index("Set VCPKG_DEFAULT_BINARY_CACHE"),
            workflow.index("Prepare vcpkg binary cache"),
        )
        self.assertLess(
            workflow.index("Prepare vcpkg binary cache"),
            workflow.index("Restore vcpkg binary cache"),
        )
        self.assertLess(
            workflow.index("ninja --version"),
            workflow.index("Configure bridge debug harness"),
        )
        self.assertLess(
            workflow.index("      - name: Install pinned CMake"),
            workflow.index("Configure bridge debug harness"),
        )
        self.assertIn(
            "git -C \"${{ runner.temp }}\\vcpkg\" checkout $env:VCPKG_BASELINE_COMMIT",
            workflow,
        )
        self.assertIn(
            '& "${{ runner.temp }}\\vcpkg\\bootstrap-vcpkg.bat" -disableMetrics',
            workflow,
        )
        self.assertIn("uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5", workflow)
        self.assertIn("dotnet-version: 9.0.x", workflow)
        self.assertIn("uses: ilammy/msvc-dev-cmd@0b201ec74fa43914dc39ae48a89fd1d8cb592756 # v1", workflow)
        self.assertIn("uses: actions/cache@caa296126883cff596d87d8935842f9db880ef25 # v5", workflow)
        self.assertIn("VCPKG_DEFAULT_BINARY_CACHE", workflow)
        self.assertIn("run: cmake --preset windows-x64-debug", workflow)
        self.assertIn(
            "run: cmake --build --preset windows-x64-debug --target dovahlink_bridge_harness",
            workflow,
        )
        harness_step = self._yaml_block(workflow, "      - name: Configure bridge debug harness")
        self.assertIn("        working-directory: bridge", harness_step)
        self.assertIn("          VCPKG_ROOT: ${{ runner.temp }}\\vcpkg", harness_step)
        self.assertIn("run: dotnet restore integration/DovahLinkValidation.sln", workflow)
        self.assertIn(
            "run: dotnet test integration/DovahLinkValidation.sln --configuration Release --no-restore",
            workflow,
        )
        self.assertIn('--logger "trx;LogFileName=integration.trx"', workflow)
        self.assertIn("--results-directory integration/TestResults", workflow)
        self.assertIn("uses: actions/upload-artifact@b7c566a772e6b6bfb58ed0dc250532a479d7789f # v6", workflow)
        self.assertIn(
            "Maintained stable release; no stable Node 24 replacement is available yet.",
            workflow,
        )
        self.assertIn("integration/TestResults/", workflow)
        self.assertLess(
            workflow.index("Build bridge validation harness"),
            workflow.index("Run integration scenarios"),
        )
        self.assertLess(
            workflow.index("Restore integration dependencies"),
            workflow.index("Run integration scenarios"),
        )

    def test_tooling_ci_covers_repository_consistency_surfaces(self) -> None:
        """Require repository checks to run when their inspected files change."""
        workflow = self._read(".github/workflows/tooling-ci.yml")
        expected_paths = {
            '- "AGENTS.md"',
            '- "README.md"',
            '- "PRODUCT.md"',
            '- "ARCHITECTURE.md"',
            '- "ROADMAP.md"',
            '- "roadmap/**"',
            '- "CONTRIBUTING.md"',
            '- "CHANGELOG.md"',
            '- ".github/workflows/**"',
            '- ".vscode/**"',
            '- "ai/context/**"',
            '- "app/**"',
            '- "bridge/**"',
            '- "integration/**"',
            '- "protocol/**"',
            '- "sdk/**"',
            '- "tooling/**"',
        }

        push_block = self._yaml_block(workflow, "  push:")
        self.assertIn("    branches: [main]", push_block)
        paths_block = self._yaml_block(push_block, "    paths:")
        self.assertEqual(
            {line.strip() for line in paths_block.splitlines()[1:] if line.strip()},
            expected_paths,
        )

        # Pull-request CI must always post a status regardless of changed files: a path filter here
        # would let a PR outside these paths skip this workflow entirely while it is still a
        # required branch-protection check, leaving the PR stuck waiting on a status that never
        # arrives.
        self.assertNotIn("paths:", self._yaml_block(workflow, "  pull_request:"))

        self.assertEqual(
            self._yaml_block(workflow, "permissions:"),
            "permissions:\n  contents: read",
        )
        checkout = self._yaml_block(
            workflow,
            "      - uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6",
        )
        self.assertEqual(
            checkout,
            "      - uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6\n"
            "        with:\n"
            "          persist-credentials: false",
        )
        self.assertIn("runs-on: ubuntu-latest", workflow)
        self.assertIn("timeout-minutes: 10", workflow)
        self.assertIn("        shell: bash", workflow)
        self.assertIn("  workflow_dispatch:", workflow)
        self.assertIn(
            "  group: tooling-ci-${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}",
            workflow,
        )
        self.assertIn("  cancel-in-progress: true", workflow)
        self.assertIn("uses: actions/setup-python@ece7cb06caefa5fff74198d8649806c4678c61a1 # v6", workflow)
        self.assertIn('python-version: "3.13"', workflow)
        self.assertIn('run: python -m unittest discover -s tooling -p "test_*.py"', workflow)
        self.assertNotIn("continue-on-error:", workflow)

    def test_common_conventions_reject_deprecated_workflow_dependencies(self) -> None:
        """Require maintained workflow dependencies and documented runtime exceptions."""
        common = self._read("ai/context/common.md")
        self.assertIn(
            "Do not introduce deprecated or end-of-life dependencies, tools, runtimes, action versions, or APIs.",
            common,
        )
        self.assertIn(
            "Pin every GitHub Actions `uses:` reference to the full immutable commit SHA of the "
            "intended release",
            common,
        )
        self.assertIn(
            "a version bump must replace the SHA and the comment together", common
        )
        self.assertIn(
            "never use a floating branch such as `@main` or a floating version tag such as `@v5`",
            common,
        )
        self.assertIn(
            "no stable replacement for a deprecated runtime",
            common,
        )

        for workflow_path in (
            ".github/workflows/bridge-ci.yml",
            ".github/workflows/integration-ci.yml",
        ):
            workflow = self._read(workflow_path)
            self.assertIn(
                "Maintained stable release; no stable Node 24 replacement is available yet.",
                workflow,
            )

    def test_workflows_use_supported_pinned_action_refs(self) -> None:
        """Require every workflow action reference to be SHA-pinned with its version documented.

        Every `uses:` reference must be pinned to the full 40-character commit SHA of its intended
        release, with that release's human-readable version in an adjacent `# vN` comment -- a repo-
        wide supply-chain hardening decision. This allowlist (require a 40-hex-char SHA) subsumes
        the previous main/master/develop/latest denylist: none of those strings can ever match a
        40-hex-char SHA, and unlike a denylist this also rejects any other floating tag.
        """
        expected_pins = {
            "actions/checkout": ("d23441a48e516b6c34aea4fa41551a30e30af803", "v6"),
            "actions/cache": ("caa296126883cff596d87d8935842f9db880ef25", "v5"),
            "actions/setup-python": ("ece7cb06caefa5fff74198d8649806c4678c61a1", "v6"),
            "actions/setup-dotnet": ("26b0ec14cb23fa6904739307f278c14f94c95bf1", "v5"),
            "actions/upload-artifact": ("b7c566a772e6b6bfb58ed0dc250532a479d7789f", "v6"),
            "ilammy/msvc-dev-cmd": ("0b201ec74fa43914dc39ae48a89fd1d8cb592756", "v1"),
            "subosito/flutter-action": ("1a449444c387b1966244ae4d4f8c696479add0b2", "v2"),
        }

        workflow_directory = REPOSITORY_ROOT / ".github" / "workflows"
        for workflow_path in sorted(workflow_directory.glob("*.yml")):
            workflow = workflow_path.read_text(encoding="utf-8")

            # Matches both the `- uses: X@Y` list-item form and the `uses: X@Y` form nested under
            # an already-open `- name:` step -- the previous regex only matched the second form,
            # so actions/checkout's `- uses:` lines were silently never checked by this test.
            # `\r?$` tolerates this repo's CRLF line endings: without it, a line with no trailing
            # comment fails to match at all (the bare `\r` sits between the SHA and `$`), which
            # would silently drop that reference from `references` instead of failing the assert
            # below -- this repo's workflow files are CRLF-terminated, confirmed by direct read.
            references = re.findall(
                r"(?m)^\s*(?:-\s*)?uses:\s*(\S+)@(\S+)[ \t]*(#.*)?\r?$", workflow
            )
            self.assertTrue(references, workflow_path.name)
            for action, ref, comment in references:
                self.assertRegex(
                    ref, r"^[0-9a-f]{40}$", f"{workflow_path.name}: {action}"
                )
                if action not in expected_pins:
                    continue
                expected_sha, expected_version = expected_pins[action]
                self.assertEqual(ref, expected_sha, f"{workflow_path.name}: {action}")
                # re.findall represents a non-participating optional group as "" (never None), so
                # a missing comment lands here as a plain, readable "" != "# vN" failure below --
                # not a crash.
                self.assertEqual(
                    comment.strip(), f"# {expected_version}", f"{workflow_path.name}: {action}"
                )

    def test_workflow_job_level_env_never_uses_the_runner_context(self) -> None:
        """Guard against a job-level env: value referencing the runner context.

        The runner context is not resolved until a runner has picked up the job's steps, so
        referencing it in a job-level env: (as opposed to a step-level one) makes the whole
        workflow file invalid -- GitHub rejects the run before any job executes. This is exactly
        the defect that silently made bridge-ci.yml and integration-ci.yml fail on every push.
        """
        workflow_directory = REPOSITORY_ROOT / ".github" / "workflows"
        for workflow_path in sorted(workflow_directory.glob("*.yml")):
            lines = workflow_path.read_text(encoding="utf-8").splitlines()
            for index, line in enumerate(lines):
                if not line.startswith("    env:"):
                    continue
                end = index + 1
                while end < len(lines) and (
                    not lines[end].strip() or len(lines[end]) - len(lines[end].lstrip()) > 4
                ):
                    end += 1
                block = "\n".join(lines[index:end])
                self.assertNotIn(
                    "${{ runner.",
                    block,
                    f"{workflow_path.name}: job-level env: cannot use the runner context:\n{block}",
                )

    def test_local_ci_preflight_covers_all_workflow_command_payloads(self) -> None:
        """Require the local preflight to mirror every CI command surface in order."""
        script = self._read("tooling/run-local-ci.ps1")
        required_fragments = (
            ". $integrationScript",
            "Find-VisualStudioToolchain",
            "Import-VisualStudioEnvironment",
            '$vcpkgBaseline = "2f1d605400c8727cc00c15797aba796c88ccd523"',
            '"clone", "https://github.com/microsoft/vcpkg.git", $vcpkgRoot',
            '"-C", $vcpkgRoot, "checkout", $vcpkgBaseline',
            '"-disableMetrics"',
            "$env:VCPKG_ROOT = $vcpkgRoot",
            "$env:VCPKG_DEFAULT_BINARY_CACHE = $cacheRoot",
            "$env:X_VCPKG_REGISTRIES_CACHE = $registriesCacheRoot",
            '$vcpkgExePath = Join-Path $vcpkgRoot "vcpkg.exe"',
            '$currentVcpkgCommit = (& git -C $vcpkgRoot rev-parse HEAD)',
            "$vcpkgAlreadyBootstrapped = $false",
            "& $vcpkgExePath version | Out-Null",
            "$vcpkgAlreadyBootstrapped = ($LASTEXITCODE -eq 0)",
            "if (-not $vcpkgAlreadyBootstrapped) {",
            "$LASTEXITCODE -ne 0",
            'Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "python"',
            'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @(\"pub\", \"get\")',
            '$appBuildCache = Join-Path $appDirectory ".dart_tool',
            'if (Test-Path -LiteralPath $appBuildCache) {',
            'Remove-Item -LiteralPath $appBuildCache -Recurse -Force',
            'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "dart" -ArgumentList @(\"run\", \"build_runner\", \"build\")',
            'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @(\"analyze\")',
            'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @(\"test\")',
            'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @(\"build\", \"windows\", \"--debug\")',
            'Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @(\"--preset\", \"windows-x64-debug\")',
            'Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @(\"--build\", \"--preset\", \"windows-x64-debug\")',
            'Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "ctest" -ArgumentList @(\"--preset\", \"windows-x64-debug\")',
            'Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @(\"--preset\", \"windows-x64-release\")',
            'Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @(\"--build\", \"--preset\", \"windows-x64-release\")',
            '"--test-dir", "build/windows-x64-release", "--output-on-failure"',
            '"restore", "integration/DovahLinkValidation.sln"',
            '"test", "integration/DovahLinkValidation.sln", "--configuration", "Release", "--no-restore"',
        )
        for fragment in required_fragments:
            self.assertIn(fragment, script)

        # dovahlink_bridge_harness has no EXCLUDE_FROM_ALL, so bridge-ci's plain Debug build above
        # already builds it into the same tree this sequential script later reuses for
        # integration-ci -- reconfiguring/rebuilding it there would be a pure duplicate, unlike in
        # integration-ci.yml's own separate, fresh-runner job where it is not a duplicate.
        self.assertNotIn(
            '"--build", "--preset", "windows-x64-debug", "--target", "dovahlink_bridge_harness"',
            script,
        )
        # Guards the assumption the removal above depends on: if this target ever gains
        # EXCLUDE_FROM_ALL, bridge-ci's plain Debug build would stop building it and this script
        # would need its explicit harness build back.
        self.assertIn(
            "add_executable(dovahlink_bridge_harness harness/dovahlink_bridge_harness.cpp)",
            self._read("bridge/CMakeLists.txt"),
        )

        section_positions = [
            script.index("=== tooling-ci ==="),
            script.index("=== app-ci ==="),
            script.index("=== bridge-ci ==="),
            script.index("=== integration-ci ==="),
        ]
        self.assertEqual(section_positions, sorted(section_positions))
        command_positions = [
            script.index('Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "python"'),
            script.index('Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("pub", "get")'),
            script.index('Remove-Item -LiteralPath $appBuildCache -Recurse -Force'),
            script.index('Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "dart" -ArgumentList @("run", "build_runner", "build")'),
            script.index('Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("analyze")'),
            script.index('Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("test")'),
            script.index('Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("build", "windows", "--debug")'),
            script.index('Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @("--preset", "windows-x64-debug")'),
            script.index('Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @("--build", "--preset", "windows-x64-debug")'),
            script.index('Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "ctest" -ArgumentList @("--preset", "windows-x64-debug")'),
            script.index('Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @("--preset", "windows-x64-release")'),
            script.index('Invoke-LocalCommand -WorkingDirectory $bridgeDirectory -FilePath "cmake" -ArgumentList @("--build", "--preset", "windows-x64-release")'),
            script.index('"--test-dir", "build/windows-x64-release", "--output-on-failure"'),
            script.index('"restore", "integration/DovahLinkValidation.sln"'),
            script.index('"test", "integration/DovahLinkValidation.sln", "--configuration", "Release", "--no-restore"'),
        ]
        self.assertEqual(command_positions, sorted(command_positions))
        self.assertNotIn("choco install", script)
        self.assertIn("All local CI command payloads passed.", script)

    def test_published_bridge_release_and_roadmap_status_agree(self) -> None:
        """Keep the published bridge version and completed roadmap phase synchronized."""
        manifest = json.loads(self._read("bridge/vcpkg.json"))
        version = manifest["version-string"]
        root_readme = self._read("README.md")
        bridge_readme = self._read("bridge/README.md")
        phase_zero = self._roadmap_section("0. Documentation baseline")
        phase_zero_five = self._roadmap_section(
            "0.5 Client and Protocol Foundation"
        )
        phase_one = self._roadmap_section("1. Skyrim Bridge Foundation")
        phase_two = self._roadmap_section(
            "2. Bridge Identity and Authoritative State Foundation"
        )
        phase_three = self._roadmap_section("3. Local Device Pairing and Reconnection")
        phase_three_one = self._roadmap_section("3.1 Live Pairing Challenge UX")

        self.assertEqual(manifest["version-string"], version)
        for completed_phase in (phase_zero, phase_zero_five, phase_one, phase_two, phase_three, phase_three_one):
            self.assertEqual(
                re.findall(r"(?m)^\*\*Status:\*\* .+$", completed_phase),
                ["**Status:** Complete"],
            )

    def test_bridge_version_literals_match_the_published_release(self) -> None:
        """Guard every hand-maintained bridge-version literal against drift from vcpkg.json."""
        manifest = json.loads(self._read("bridge/vcpkg.json"))
        version = manifest["version-string"]
        version_components = ", ".join(version.split("."))

        self.assertIn(
            f'kBridgeVersion = "{version}"', self._read("bridge/application/bridge_config.hpp")
        )
        self.assertIn(
            f"REL::Version{{{version_components}, 0}}", self._read("bridge/plugin/dovahlink_bridge_plugin.cpp")
        )

        compatibility = self._read(
            "integration/DovahLinkValidationClient/BridgeVersionCompatibility.cs"
        )
        for limit in ("MinimumSupportedVersion", "MaximumSupportedVersion"):
            self.assertIn(f"{limit} = new({version_components});", compatibility, limit)

        self.assertIn(
            f'CHECK(helloAck->bridgeVersion == "{version}");',
            self._read("bridge/protocol/hello_ack_payload_test.cpp"),
        )
        for current_example in (
            "protocol/schema/README.md",
            "protocol/fixtures/connection/hello-ack.json",
            "protocol/fixtures/connection/hello-ack-active-context.json",
        ):
            self.assertIn(f'"bridgeVersion": "{version}"', self._read(current_example), current_example)

    def test_changelog_matches_the_published_bridge_version(self) -> None:
        """Keep CHANGELOG.md's newest entry synchronized with the published bridge version."""
        manifest = json.loads(self._read("bridge/vcpkg.json"))
        changelog = self._read("CHANGELOG.md")
        entry_versions = re.findall(r"(?m)^## \[(\d+\.\d+\.\d+)\]", changelog)

        self.assertTrue(entry_versions, "CHANGELOG.md has no version entries.")
        self.assertEqual(entry_versions[0], manifest["version-string"])
        for known_version in ("0.1.0", "0.2.0", "0.3.0", "0.3.1", "0.3.2"):
            self.assertIn(known_version, entry_versions)
        self.assertEqual(len(set(entry_versions)), len(entry_versions), "CHANGELOG.md has duplicate versions.")

    def test_flutter_and_integration_docs_use_consistent_terminology(self) -> None:
        """Guard the datasource file-count exception and one shared term for the compatibility
        bootstrap step and its failure case, across the docs that reference them."""
        architecture = self._read("ai/context/flutter/architecture.md")
        self.assertIn(
            "One primary public class or model per file. Datasource files are the documented "
            "exception below:",
            architecture,
        )

        testing = self._read("ai/context/integration/testing.md")
        self.assertIn("- compatibility bootstrap\n", testing)
        self.assertIn(
            "- an incompatible Bridge/client version during the compatibility bootstrap\n",
            testing,
        )

        changelog = self._read("CHANGELOG.md")
        self.assertIn("revision continuity across a reconnect", changelog)

    def test_foundation_first_roadmap_order_and_boundaries_are_explicit(self) -> None:
        """Preserve the approved phase order and deferred-control boundary."""
        roadmap = self._roadmap_corpus()
        expected_headings = [
            "0. Documentation baseline",
            "0.5 Client and Protocol Foundation",
            "1. Skyrim Bridge Foundation",
            "2. Bridge Identity and Authoritative State Foundation",
            "3. Local Device Pairing and Reconnection",
            "3.1 Live Pairing Challenge UX",
            "3.2 Known Device & Trust Administration",
            "3.3 Client Trust-State Integration",
            "4. Live State Synchronization Foundation",
            "5. Dart Client SDK Foundation",
            "6. PC / Second-Screen Baseline",
            "7. Core UI Theme System",
            "8. Live Player State",
            "9. Multi-Client Runtime Foundation",
            "10. Multi-Bridge and Local Discovery Foundation",
            "11. Automatic Connection and Transport Selection",
            "12. Mod Awareness",
            "13. Interactive Map Foundation",
            "14. Map Asset and Worldspace System",
            "15. Quests",
            "16. Navigation / Path Guidance",
            "17. Inventory",
            "18. Equipment",
            "19. Magic, Spells, Shouts, and Powers",
            "20. Favorites and Hotkeys",
            "21. Customizable Dashboard",
            "22. Secure LAN Transport and Network Discovery",
            "23. Mobile / Tablet Client",
            "24. Item Knowledge and Search",
            "25. Legacy of the Dragonborn Integration",
            "26. Installed UI Detection",
            "27. Optional UI Mod Adapters",
            "28. Safe Companion Authorization Foundation",
            "29. Runtime Profiling and Advanced Bridge Hardening",
            "30. CommonLib Dependency Maintenance Audit",
        ]
        actual_headings = re.findall(r"(?m)^## (\d+(?:\.\d+)?\.? .+)$", roadmap)

        self.assertEqual(actual_headings, expected_headings)
        self.assertNotIn("## 1.25 ", roadmap)
        self.assertNotIn("## 1.5 ", roadmap)
        self.assertEqual(roadmap.count("**Status:** Next"), 0)
        self.assertEqual(roadmap.count("**Status:** Complete"), 7)
        self.assertEqual(len(re.findall(r"(?m)^\*\*Status:\*\* Planned$", roadmap)), 26)
        self.assertEqual(roadmap.count("**Status:** Planned after read-only product validation"), 1)
        # Phase 5 was partially pulled forward for Phase 3's pairing needs (sdk/README.md's
        # "Status" section records the same decision); its status line carries that explanation
        # instead of the plain "Planned" every other undone phase uses.
        phase_5_status = (
            "**Status:** Planned. The package scaffold, protocol/transport layer, and "
            "persistence boundary"
        )
        self.assertEqual(roadmap.count(phase_5_status), 1)

        for heading in expected_headings:
            phase = self._roadmap_section(heading)
            if heading.startswith(("0. ", "0.5 ", "1. ", "2. ", "3. ", "3.1 ", "3.2 ")):
                expected_status = "**Status:** Complete"
            elif heading.startswith("5. "):
                expected_status = phase_5_status
            elif heading.startswith("28. "):
                expected_status = "**Status:** Planned after read-only product validation"
            else:
                expected_status = "**Status:** Planned"
            self.assertEqual(
                re.findall(r"(?m)^\*\*Status:\*\* .+$", phase),
                [expected_status],
                heading,
            )

        bridge_readme = self._read("bridge/README.md")
        integration_readme = self._read("integration/README.md")
        for supporting_document in (
            roadmap,
            bridge_readme,
            integration_readme,
        ):
            self.assertNotIn("Phase 1.25", supporting_document)
            self.assertNotIn("Phase 1.5", supporting_document)
        self.assertIn("[Stage 3 roadmap](../roadmap/03-local-device-pairing-and-reconnection.md)", bridge_readme)
        self.assertIn("## Live event delivery is deferred to Phase 4", bridge_readme)
        self.assertIn("the `roadmap/04-live-state-synchronization-foundation.md` and `roadmap/03-local-device-pairing-and-reconnection.md` entries", integration_readme)

        ordering = {heading: roadmap.index(f"## {heading}") for heading in expected_headings}
        self.assertLess(
            ordering["4. Live State Synchronization Foundation"],
            ordering["5. Dart Client SDK Foundation"],
        )
        self.assertLess(
            ordering["5. Dart Client SDK Foundation"], ordering["6. PC / Second-Screen Baseline"]
        )
        self.assertLess(ordering["6. PC / Second-Screen Baseline"], ordering["7. Core UI Theme System"])
        self.assertLess(ordering["7. Core UI Theme System"], ordering["8. Live Player State"])
        self.assertLess(ordering["8. Live Player State"], ordering["9. Multi-Client Runtime Foundation"])
        self.assertLess(ordering["9. Multi-Client Runtime Foundation"], ordering["13. Interactive Map Foundation"])
        self.assertLess(
            ordering["22. Secure LAN Transport and Network Discovery"],
            ordering["23. Mobile / Tablet Client"],
        )

        identity = self._roadmap_section(
            "2. Bridge Identity and Authoritative State Foundation"
        )
        authorization = self._roadmap_section(
            "28. Safe Companion Authorization Foundation"
        )
        deferred = self._roadmap_section("Deferred possibilities")
        self.assertIn("does not add a separate\ngame-process identifier", identity)
        dependency_expectations = {
            "3. Local Device Pairing and Reconnection": "depends on Phase 2",
            "5. Dart Client SDK Foundation": "depends on Phases 2, 3, and 4",
            "6. PC / Second-Screen Baseline": "validates Phases 2 through 5",
            "9. Multi-Client Runtime Foundation": "follows the Phase 8 single-client proof",
            "10. Multi-Bridge and Local Discovery Foundation": "depends on Phases 2 and 9",
            "28. Safe Companion Authorization Foundation": (
                "depends on identity, multi-client isolation, and security"
            ),
        }
        for heading, expected_dependency in dependency_expectations.items():
            self.assertIn(
                expected_dependency,
                self._roadmap_section(heading),
                heading,
            )
        self.assertIn("without exposing a generic command API", authorization)
        self.assertIn("Validate the machinery without adding gameplay mutation", authorization)
        self.assertIn("Each action needs its own product decision", authorization)
        self.assertIn("no Skyrim\nmutation is exposed", authorization)
        self.assertIn("Individual companion actions", deferred)
        for deferred_action in ("equipment", "favorites or hotkeys", "map markers", "fast travel"):
            self.assertIn(deferred_action, deferred)

    def test_pairing_phase_establishes_persistent_per_user_trust(self) -> None:
        """Guard the persistent-trust pairing redesign and its retired restart-bound predecessor."""
        pairing = self._roadmap_section("3. Local Device Pairing and Reconnection")
        normalized_pairing = self._normalize_whitespace(pairing)

        for required_phrase in (
            "Permit only one active pairing challenge globally",
            "final confirmation is idempotent",
            "Persist completed trust so it survives Skyrim, Bridge, and Windows restarts, save "
            "changes, `playContextId` changes, and `bridgeInstanceId` changes",
            "Persistent trust belongs to the current Windows user profile running the client and "
            "the Bridge",
            "Scope `clientId` to the client installation and the Windows user profile running it",
            "a `shortId` is never authentication or authorization material",
            "is enabled only when an explicit `DOVAHLINK_DEV_TOKEN`",
            "token authentication is a separate provider from device pairing",
            "Give WebSocket-level Ping/Pong and a bounded idle timeout sole ownership of "
            "connection liveness",
            "receives a specific revoked/not-trusted outcome",
            "does not implement Phase 10/11 Bridge discovery or endpoint-selection behavior",
            "administration behavior (list trusted clients, revoke one, reset all) in a reusable "
            "Bridge application/domain service rather than inside a Skyrim console-command handler",
            "revoking a trusted client removes its active trust, invalidates any current "
            "authenticated session it owns, closes that connection, and rejects reuse of the "
            "revoked credential",
            "never crashes Skyrim, never silently trusts a client, never invents or merges "
            "uncertain credentials",
            "an approved per-user secure-storage mechanism for the platform; do not invent "
            "cryptography",
        ):
            self.assertIn(required_phrase, normalized_pairing)

        # The restart-bound credential model this phase replaces must not silently creep back in.
        for retired_phrase in (
            "invalidate device credentials when the bridge or Skyrim process",
            "Long-term credential persistence across bridge restarts is intentionally deferred",
        ):
            self.assertNotIn(retired_phrase, normalized_pairing)

        identity_model = self._markdown_section("ARCHITECTURE.md", "Runtime and identity model")
        normalized_identity_model = self._normalize_whitespace(identity_model)
        self.assertIn(
            "Persistent device trust is a separate concept layered on top of these four "
            "lifetimes, not a fifth lifetime that replaces or reinterprets them",
            normalized_identity_model,
        )
        self.assertIn(
            "a trusted client still authenticates into a fresh `sessionId` on every reconnect, "
            "and a bridge restart still creates a new `bridgeInstanceId`",
            normalized_identity_model,
        )
        self.assertIn(
            "belongs to the Windows user profile running the client and the Bridge, and survives "
            "Bridge, Skyrim, and Windows restarts",
            normalized_identity_model,
        )
        self.assertIn(
            "`ai/context/protocol/security.md` and `roadmap/03-local-device-pairing-and-reconnection.md`'s Phase 3 owns the pairing, "
            "storage, and revocation design",
            normalized_identity_model,
        )

    def test_security_doc_establishes_persistent_trust_and_liveness_ownership(self) -> None:
        """Guard the pairing/trust, developer-auth, liveness, and threat-boundary sections."""
        security = self._read("ai/context/protocol/security.md")
        for heading in (
            "## Persistent local trust",
            "## Developer authentication",
            "## Connection liveness",
            "## Local-OS-user threat boundary",
        ):
            self.assertIn(heading, security)

        trust = self._markdown_section(
            "ai/context/protocol/security.md", "Persistent local trust"
        )
        normalized_trust = self._normalize_whitespace(trust)
        for required_phrase in (
            "Normal users authenticate through pairing, not a configured long token",
            "Only one pairing challenge may be active at a time, globally",
            "Final confirmation is idempotent",
            "scoped to the Windows user profile running the client and the Bridge",
            "Do not invent cryptography",
            "the official client must not share one `clientId`/credential between different "
            "Windows user profiles",
            "A `shortId` is never authentication or authorization material",
            "An explicit local reset-all-trust operation exists",
            "Revocation is immediate: revoking a trusted client removes its active trust, "
            "invalidates its current authenticated session, closes that connection, and rejects "
            "reuse of the revoked credential",
            "the Bridge must not claim pairing is available when the in-game confirmation cannot "
            "actually be presented",
            "A client that fails before saving the credential creates no durable trust and may "
            "pair again once the Bridge's pending challenge expires",
            "A client that saves the credential but crashes before confirming retries "
            "confirmation on restart",
            "If the Bridge restarted while the credential was only pending, it reports the "
            "pending credential as no longer known/valid; the client discards its incomplete "
            "local credential and returns to unpaired",
            "it never crashes Skyrim, never silently trusts a client, never invents or merges "
            "uncertain credentials, and always supports a clean reset-and-re-pair path",
            "A revoked client that reconnects with its old credential receives a specific "
            "revoked/not-trusted outcome rather than a generic transport failure",
            "Distinguishing a revoked `clientId` from one that was never paired may use a "
            "minimal revocation tombstone containing no credential; re-pairing an intentionally "
            "revoked `clientId` may remove or replace that tombstone and establish a new "
            "credential",
        ):
            self.assertIn(required_phrase, normalized_trust)

        developer_auth = self._markdown_section(
            "ai/context/protocol/security.md", "Developer authentication"
        )
        normalized_developer_auth = self._normalize_whitespace(developer_auth)
        for required_phrase in (
            "enabled only when an explicit development token (`DOVAHLINK_DEV_TOKEN` or "
            "equivalent approved configuration) is configured, with identical behavior across "
            "debug, beta, and release builds",
            "must not silently enroll the authenticating client into the persistent "
            "trusted-device store",
            "Developer authentication is not a switch that disables security",
            "loopback restriction, input limits, protocol validation, a fresh `sessionId`, and "
            "the single-connected-client limit",
        ):
            self.assertIn(required_phrase, normalized_developer_auth)

        liveness = self._markdown_section(
            "ai/context/protocol/security.md", "Connection liveness"
        )
        normalized_liveness = self._normalize_whitespace(liveness)
        for required_phrase in (
            "WebSocket-level Ping/Pong and a bounded idle timeout own connection liveness",
            "invalidate `sessionId`, cancel or finish outstanding I/O, close the transport, then "
            "release the connection slot",
            "prefer bounded short retry/backoff over same-client connection takeover",
            "rapid restart, timeout, and Bridge restart all recover cleanly under this policy",
            "A dead `sessionId` can never become valid again",
        ):
            self.assertIn(required_phrase, normalized_liveness)

        threat_boundary = self._markdown_section(
            "ai/context/protocol/security.md", "Local-OS-user threat boundary"
        )
        normalized_threat_boundary = self._normalize_whitespace(threat_boundary)
        for required_phrase in (
            "loopback TCP itself is not proof of Windows-user identity",
            "does not automatically make a loopback socket isolated from another simultaneously "
            "logged-in local account",
            "must be solved deliberately, with its own approved design, rather than assumed "
            "from `127.0.0.1`",
        ):
            self.assertIn(required_phrase, normalized_threat_boundary)

        secrets_and_logging = self._markdown_section(
            "ai/context/protocol/security.md", "Secrets and logging"
        )
        for required_phrase in (
            "Store credentials only through the approved trust-store and per-user secure-storage "
            'mechanisms defined under "Persistent local trust" above; do not invent persistence '
            "or cryptography of your own.",
            "Never log credentials, developer tokens, or other security-sensitive credential "
            "verifiers.",
            "Pairing codes are not written to normal persistent logs merely because they are "
            "short-lived.",
        ):
            self.assertIn(required_phrase, secrets_and_logging)
        # The pre-pairing "no approved persistence design" bullet this replaced must not return.
        self.assertNotIn(
            "do not invent persistence during the connection proof", secrets_and_logging
        )

    def test_trust_admin_console_surface_uses_only_canonical_commands(self) -> None:
        """Keep the Papyrus, YAML, and documentation command names synchronized."""
        console_readme = self._read("console-admin/README.md")
        security = self._read("ai/context/protocol/security.md")
        bridge_readme = self._read("bridge/README.md")
        papyrus = self._read("console-admin/DovahLinkAdmin.psc")
        yaml = self._read("console-admin/dovahlink.yaml")

        canonical_commands = (
            "dovahlink list",
            "dovahlink list trust",
            "dovahlink list block",
            "dovahlink help",
            "dovahlink revoke -id <shortId>",
            "dovahlink reset-trust",
            "dovahlink reset",
            "dovahlink confirm-reset -confirm <code>",
            "dovahlink block -id <shortId>",
            "dovahlink unblock -id <shortId>",
            "dovahlink forget -id <shortId>",
        )
        for command in canonical_commands:
            self.assertIn(command, console_readme)
            self.assertIn(f"`{command}`", security)
        self.assertIn("Entering bare `dovahlink` displays ConsoleUtil Extended's compact command index.", console_readme)
        self.assertIn("`dovahlink help` for the full trust-administration descriptions.", console_readme)

        for retired_command in (
            "dovahlink devices",
            "dovahlink blocklist",
            "dovahlink reset trust",
            "dovahlink reset -confirm <code>",
        ):
            self.assertNotIn(retired_command, console_readme)
            self.assertNotIn(retired_command, security)
            self.assertNotIn(retired_command, bridge_readme)

        self.assertIn("String Function List(String akScope) global native", papyrus)
        self.assertIn("String Function Help() global native", papyrus)
        self.assertNotIn("Function Devices", papyrus)
        self.assertNotIn("Function Blocked", papyrus)

        command_blocks = {
            name: body
            for name, body in re.findall(
                r"(?ms)^  - name: ([A-Za-z-]+)$\n(.*?)(?=^  - name: |\Z)", yaml
            )
        }
        self.assertEqual(
            list(command_blocks),
            ["list", "help", "revoke", "block", "unblock", "forget", "reset-trust", "reset", "confirm-reset"],
        )
        expected_functions = {
            "list": "List",
            "help": "Help",
            "revoke": "Revoke",
            "block": "Block",
            "unblock": "Unblock",
            "forget": "Forget",
            "reset-trust": "ResetTrust",
            "reset": "Reset",
            "confirm-reset": "ConfirmReset",
        }
        for command, function in expected_functions.items():
            self.assertIn(f"func: {function}", command_blocks[command])
        for command in ("revoke", "block", "unblock", "forget"):
            self.assertEqual(command_blocks[command].count("      - name: -id"), 1)
            self.assertIn("        required: true", command_blocks[command])
        self.assertNotIn("      - name:", command_blocks["reset"])
        self.assertEqual(command_blocks["confirm-reset"].count("      - name: -confirm"), 1)
        self.assertIn("        required: true", command_blocks["confirm-reset"])
        self.assertIn("name: scope", yaml)
        self.assertIn("required: false", yaml)
        self.assertIn("default: all", yaml)
        self.assertIn("help: Use dovahlink help for full details.", yaml)
        self.assertNotRegex(yaml, r"(?m)^[ \t]+help:\s*[|>]")
        help_values = re.findall(r"(?m)^[ \t]+help:\s?(.*)$", yaml)
        self.assertLessEqual(sum(len(value) for value in help_values), 500)
        self.assertNotIn("alias: id", yaml)
        self.assertNotIn("alias: confirm", yaml)
        self.assertNotIn("reset trust", yaml)
        self.assertNotIn("dovahlink reset -confirm", yaml)
        self.assertNotIn("name: devices", yaml)
        self.assertNotIn("name: blocklist", yaml)

    def test_architecture_establishes_sdk_boundary_and_replaces_client_non_goal(self) -> None:
        """Guard the sdk/ repository boundary and the retired single-client non-goal."""
        architecture = self._read("ARCHITECTURE.md")

        self.assertIn("sdk/", architecture)
        self.assertIn("reusable supported client SDK implementations", architecture)
        self.assertIn(
            "The intended first SDK implementation is `sdk/dart/dovahlink_client/`, added when "
            "the Dart Client SDK Foundation phase begins",
            architecture,
        )
        self.assertIn("Dart Client", architecture)
        # Prove the SDK box actually sits between the protocol box and the client boxes in the
        # diagram itself (box-drawing prefix disambiguates from the phrase's other prose uses).
        self.assertLess(
            architecture.index("│ DovahLink"), architecture.index("│ Dart Client")
        )
        self.assertLess(
            architecture.index("│ Dart Client"), architecture.index("│ Desktop")
        )
        self.assertIn(
            "see `sdk/README.md` for its current planned status", architecture
        )
        self.assertEqual(architecture.count("for its current planned status"), 2)
        self.assertIn("### SDK", architecture)
        self.assertIn(
            "Implements the canonical contract for Dart consumers and is not a second protocol "
            "authority",
            architecture,
        )
        self.assertIn(
            "it maps the wire contract into typed client/domain models without those models "
            "becoming the contract itself",
            architecture,
        )
        self.assertIn(
            "The official Flutter app is the SDK's first production consumer", architecture
        )
        self.assertIn("See `ai/context/sdk/` for SDK-specific conventions", architecture)
        self.assertIn(
            "normal DovahLink communication from the app goes through the SDK rather than "
            "through app-private transport, compatibility, authentication, pairing, reconnect, "
            "or session code",
            architecture,
        )

        non_goals = self._markdown_section("ARCHITECTURE.md", "Architectural non-goals")
        normalized_non_goals = self._normalize_whitespace(non_goals)
        self.assertIn(
            "intentionally introduces a shared client implementation before a second product "
            "client exists, replacing the earlier assumption that such an abstraction should "
            "wait for a second client",
            normalized_non_goals,
        )
        self.assertIn(
            "has grown substantial enough to deserve its own boundary, with the official app as "
            "its first consumer",
            normalized_non_goals,
        )
        # The retired single-client-first non-goal this replaces must not silently creep back in.
        self.assertNotIn(
            "No shared client-implementation abstraction before there is a second client",
            architecture,
        )

    def test_sdk_readme_documents_the_phase_5_pull_forward_and_the_real_package(self) -> None:
        """Guard sdk/README.md's content and the real, partially-implemented Dart package."""
        sdk_readme = self._read("sdk/README.md")

        for required_phrase in (
            "This directory owns the reusable, supported client SDK implementations for the "
            "DovahLink protocol.",
            "consumers do not need to implement transport, Bridge-version compatibility "
            "detection,\nauthentication, pairing recovery, reconnect, session and "
            "authoritative-state identity, revisions,\nsubscriptions, snapshots, recovery, or "
            "reusable client persistence themselves.",
            "Skyrim\n   |\nDovahLink Bridge / mod\n   |\nprotocol/\n   |\nDart Client SDK\n   |\n"
            "Official Flutter app",
            "`protocol/` remains the sole canonical language-neutral Bridge/client contract",
            "the SDK implements\nthat contract for Dart consumers and is not a second protocol "
            "authority",
            "the SDK's first production consumer, not a privileged one — see",
            "Partially implemented, pulled forward from `roadmap/05-dart-client-sdk-foundation.md`'s Phase 5 (\"Dart Client "
            "SDK Foundation\")\nahead of that phase's formal start, because Phase 3 (Local "
            "Device Pairing and Reconnection), documented in `roadmap/03-local-device-pairing-and-reconnection.md`, needed\nthe SDK's persistence boundary to avoid a "
            "larger later migration.",
            "sdk/\n  dart/\n    dovahlink_client/",
            "It currently provides the connect/hello/pairing/disconnect protocol client, proven "
            "against the real\nbridge harness, plus SDK-owned `clientId`, credential, and "
            "`CONFIRMING` pairing-recovery persistence",
            "Phase 5's remaining scope -- Bridge-version\ncompatibility detection, reconnect, "
            "revisions, subscriptions, snapshots, and retiring the app's\nseparate "
            "`features/connection/` Redux protocol code -- is undone, so this pull-forward does "
            "not\nclose Phase 5.",
            "the app-side Redux `features/connection/` code\ndocumented in "
            "[`ai/context/flutter/`](../ai/context/flutter/) remains a separate, not-yet-retired\n"
            "implementation for the identity and live-synchronization foundations already in "
            "progress",
        ):
            self.assertIn(required_phrase, sdk_readme)

        # Phase 5 was pulled forward: the real package now exists, replacing the old
        # "no implementation skeleton yet" invariant this test used to guard.
        real_package = REPOSITORY_ROOT / "sdk" / "dart" / "dovahlink_client"
        self.assertTrue(real_package.is_dir())
        self.assertTrue((real_package / "pubspec.yaml").is_file())
        self.assertTrue((real_package / "lib").is_dir())

    def test_shared_dart_conventions_are_split_from_flutter_only_ones(self) -> None:
        """Guard the ai/context/dart/ extraction and its Flutter-side pointer."""
        dart_style = self._read("ai/context/dart/dart-style.md")
        flutter_dart_style = self._read("ai/context/flutter/dart-style.md")

        for required_phrase in (
            "Shared Dart-language conventions that apply to every Dart package in this "
            "repository",
            "Do not use `dynamic` or `any`-style escape hatches to avoid modelling a type.",
            "Use the null assertion operator (`!`) only when an immediately visible check or "
            "constructor",
            "Use Dart doc links such as `[SymbolName]` when referring to another documented "
            "symbol",
            "Missing implementation uses `// TODO: ...` immediately above the declaration.",
            "Use UpperCamelCase for classes, enums, typedefs, extensions, and type parameters.",
            "Use lowercase_with_underscores for packages, directories, source files, and import "
            "prefixes.",
            "Use `dart format`, trailing commas, braces for flow control, and single quotes.",
            "Never prefix methods with `get`; use a getter or a descriptive verb.",
        ):
            self.assertIn(required_phrase, dart_style)

        self.assertIn(
            "Shared Dart-language conventions (type safety, naming case, formatting, async, "
            "dartdoc mechanics)\nlive in [`ai/context/dart/dart-style.md`](../dart/dart-style.md)",
            flutter_dart_style,
        )
        self.assertIn(
            "follow `ai/context/dart/dart-style.md`'s baseline naming rules", flutter_dart_style
        )
        # The moved sections and their content must not be duplicated in the Flutter-only file.
        for retired_phrase in (
            "## Type safety",
            "## Baseline Dart rules",
            "Do not use `dynamic` or `any`-style escape hatches",
            "Use UpperCamelCase for classes, enums, typedefs, extensions, and type parameters.",
            "Use lowercase_with_underscores for packages, directories, source files",
            "Use `dart format`, trailing commas, braces for flow control, and single quotes.",
            "[CharacterStateEntity]",
            "Missing implementation uses `// TODO:",
            "Dart defaults: `snake_case.dart` filenames, `UpperCamelCase` types",
        ):
            self.assertNotIn(retired_phrase, flutter_dart_style)
        # Flutter-architecture-specific documentation rules stay behind.
        self.assertIn(
            "Describe dependencies in the architectural direction: Model to Entity, UseCase to "
            "repository",
            flutter_dart_style,
        )

    def test_sdk_conventions_cover_architecture_api_persistence_and_testing(self) -> None:
        """Guard the four ai/context/sdk/ convention files and their non-duplication of security/compatibility."""
        sdk_architecture = self._read("ai/context/sdk/architecture.md")
        sdk_api_design = self._read("ai/context/sdk/api-design.md")
        sdk_persistence = self._read("ai/context/sdk/persistence.md")
        sdk_testing = self._read("ai/context/sdk/testing.md")

        for required_phrase in (
            "`protocol/` remains the sole canonical language-neutral Bridge/client contract.",
            "The Bridge remains authoritative for live Skyrim game state, authoritative "
            "revisions, the current\n`playContextId`, server-side trusted-client records, "
            "revocation, trust administration, Bridge\ncapabilities, and server-side security "
            "decisions.",
            "The SDK has one underlying client engine/state machine.",
            "The reusable client core must not depend on Flutter widgets, Redux, `GetIt`, "
            "navigation",
            "it must never become\nauthoritative over Skyrim or server-side trust.",
            "It must not construct a new parallel raw WebSocket implementation,\nBridge "
            "compatibility implementation, protocol decoder, authentication implementation, "
            "pairing\nimplementation, reconnect state machine, revision tracker, or subscription "
            "engine.",
        ):
            self.assertIn(required_phrase, sdk_architecture)

        for required_phrase in (
            "The long-term simple\nexperience trends toward: find/select a Bridge, pair if "
            "necessary, listen to typed state.",
            '"Advanced" must not mean\n"bypass invariants"',
            "Do not duplicate `ai/context/protocol/security.md` here; obey it.",
            "The SDK owns typed meaning; the app owns user-facing wording and presentation.",
            "never persists secrets insecurely, never turns a security-sensitive failure into\n"
            "plausible success or default state",
        ):
            self.assertIn(required_phrase, sdk_api_design)

        for required_phrase in (
            "If persisted data is required for correct reusable DovahLink client behavior, the "
            "SDK owns it.",
            "The app must not persist a competing authoritative copy of SDK-owned protocol or "
            "client state",
            "The SDK is not merely a WebSocket wrapper",
            "The SDK must not assume a cached resource is valid merely because a file exists",
        ):
            self.assertIn(required_phrase, sdk_persistence)

        for required_phrase in (
            "Do not maintain the same Dart client correctness test suite independently inside "
            "both `app/` and\n`sdk/`.",
            "It must not consume, wrap, generate from, or\notherwise reuse the Dart SDK",
        ):
            self.assertIn(required_phrase, sdk_testing)

        # These conventions must point to the compatibility/security authorities, not restate
        # their content — this is the same rule common.md applies to every shared contract.
        retired_duplication_phrase = (
            "Compatibility is identified by the DovahLink Bridge/mod release version against the "
            "supported Bridge-version range a client or SDK explicitly declares"
        )
        for sdk_doc in (sdk_architecture, sdk_api_design, sdk_persistence, sdk_testing):
            self.assertNotIn(retired_duplication_phrase, sdk_doc)
            self.assertNotIn("never use floating branches such as `@main`", sdk_doc)

    def test_agents_and_common_point_at_the_new_dart_and_sdk_convention_areas(self) -> None:
        """Guard the AGENTS.md/common.md pointers added for ai/context/dart/ and ai/context/sdk/."""
        agents = self._read("AGENTS.md")
        common = self._read("ai/context/common.md")

        self.assertIn(
            "- `ai/context/dart/` — shared Dart-language conventions; read for any Dart work, "
            "Flutter client or SDK",
            agents,
        )
        self.assertIn(
            "- `ai/context/sdk/` — Dart Client SDK conventions; read for SDK work", agents
        )
        self.assertIn(
            "SDK conventions in `ai/context/sdk/` are locally originated for DovahLink, not "
            "copied from Price check; do not treat Price check as their external source.",
            agents,
        )

        self.assertIn(
            "`sdk/` is reserved for reusable, supported client SDK implementations; see "
            "`sdk/README.md`.",
            common,
        )
        self.assertIn(
            "Shared Dart-language conventions are the complete set in "
            "`ai/context/dart/dart-style.md`; Flutter\n  conventions are the complete set in",
            common,
        )
        self.assertIn(
            "`ai/context/flutter/architecture.md`, `dart-style.md`,\n  `testing.md`, and "
            "`error-handling.md`; SDK conventions are the complete set in",
            common,
        )
        self.assertIn(
            "SDK conventions are the complete set in\n  `ai/context/sdk/architecture.md`, "
            "`api-design.md`, `persistence.md`, and `testing.md`.",
            common,
        )
        self.assertIn("do not duplicate a rule across more than one of them.", common)

    def test_app_protocol_and_integration_docs_reconcile_the_sdk_boundary(self) -> None:
        """Guard the SDK-transition notes added to app/, protocol/, and integration/ READMEs."""
        app_readme = self._read("app/README.md")
        protocol_readme = self._read("protocol/README.md")
        integration_readme = self._read("integration/README.md")

        self.assertIn("## SDK migration", app_readme)
        self.assertIn(
            "Before `roadmap/05-dart-client-sdk-foundation.md`'s Phase 5 (\"Dart Client SDK Foundation\"), this directory owns "
            "its protocol and\nclient adapters directly",
            app_readme,
        )
        self.assertIn(
            "After that phase, this app consumes\n"
            "[`sdk/dart/dovahlink_client/`](../sdk/README.md)'s public API for normal DovahLink "
            "communication",
            app_readme,
        )
        self.assertIn(
            "instead: transport, Bridge-version compatibility, authentication, pairing, "
            "reconnect, and revision\nlogic move to the SDK boundary.",
            app_readme,
        )
        self.assertIn(
            "Flutter conventions point to\n[`ai/context/sdk/`](../ai/context/sdk/) for that "
            "SDK-owned behavior rather than duplicating it here.",
            app_readme,
        )

        self.assertIn(
            "The repository-root [`app/`](../app/) and planned `bridge/` areas, and the planned\n"
            "  [`sdk/`](../sdk/) area, contain adapters, not competing protocol definitions.",
            protocol_readme,
        )

        self.assertIn(
            "It also does not consume, wrap, or generate from the future Dart Client SDK\n"
            "(`sdk/`, see `roadmap/05-dart-client-sdk-foundation.md`'s Phase 5) once one exists; its value depends on staying "
            "an independent\nimplementation of the canonical contract.",
            integration_readme,
        )

    def test_live_state_phase_depends_on_reconnect_and_defines_session_loss(self) -> None:
        """Preserve reconnect ordering and the bounded, session-scoped reliable-event contract."""
        live_state = self._roadmap_section("4. Live State Synchronization Foundation")
        bridge_live_state = self._markdown_section(
            "bridge/README.md", "Live event delivery is deferred to Phase 4"
        )
        normalized_live_state = self._normalize_whitespace(live_state)
        normalized_bridge_live_state = self._normalize_whitespace(bridge_live_state)

        self.assertIn("depends on Phases 2 and 3", live_state)
        self.assertNotRegex(live_state, r"(?i)\bdeltas?\b")
        self.assertNotRegex(bridge_live_state, r"(?i)\bdeltas?\b")
        self.assertIn("complete post-change state rather than a patch", live_state)
        for required_phrase in (
            "Reliable-event delivery is scoped to one authenticated session",
            "explicitly disconnected",
            "does not replay the previous session's queued events",
        ):
            self.assertIn(required_phrase, live_state)
        for required_phrase in (
            "if it fills, delivery is prioritized over `stateUpdates` and, if a client still cannot keep up, that client is marked unhealthy and disconnected",
            "Reliable events are scoped to the authenticated session",
            "disconnecting discards any events still queued for that session",
            "reconnecting starts from fresh snapshots rather than replaying the discarded queue",
        ):
            self.assertIn(required_phrase, normalized_bridge_live_state)
        self.assertIn(
            "a client that cannot consume them in time is explicitly disconnected",
            normalized_live_state,
        )

    def test_state_identity_and_snapshot_exceptions_are_explicit(self) -> None:
        """Keep bridge lifetime identity and snapshot delivery exceptions in the roadmap."""
        identity = self._roadmap_section(
            "2. Bridge Identity and Authoritative State Foundation"
        )
        live_state = self._roadmap_section("4. Live State Synchronization Foundation")
        normalized_identity = self._normalize_whitespace(identity)
        normalized_live_state = self._normalize_whitespace(live_state)

        self.assertIn(
            "authoritative state identity as one `bridgeInstanceId`, `playContextId`, and state area",
            normalized_identity,
        )
        self.assertIn(
            "a bridge restart creates a new state identity even when the same play context remains loaded",
            normalized_identity,
        )
        self.assertIn(
            "`sessionId` scoped to authenticated socket delivery only",
            normalized_identity,
        )
        self.assertIn(
            "reconnecting creates a new session without resetting the current authoritative revision",
            normalized_identity,
        )
        self.assertIn(
            "unchanged snapshot requests reuse it",
            normalized_identity,
        )
        self.assertIn(
            "Invalidate prior state when a new play context replaces the previous loaded game",
            normalized_identity,
        )
        self.assertIn(
            "Do not silently reinterpret messages from the previously published experimental "
            "release as already carrying this ownership",
            normalized_identity,
        )
        roadmap = self._roadmap_corpus()
        for retired_term in (
            "protocol-v2 flow",
            "versioned pairing flow",
            "versioned contract tests",
            "versioned pairing contract",
        ):
            self.assertNotIn(retired_term, roadmap)
        self.assertIn(
            "executable bridge-restart acceptance test proving cached state from the previous bridge lifetime is rejected",
            normalized_identity,
        )
        self.assertIn(
            "publish unsolicited replaceable state only on authoritative change",
            normalized_live_state,
        )
        self.assertIn(
            "Always deliver initial, recovery, and explicitly requested snapshots, even when the state is unchanged",
            normalized_live_state,
        )
        self.assertIn(
            "these snapshots reuse the current authoritative revision",
            normalized_live_state,
        )
        self.assertIn(
            "unchanged unsolicited replaceable state produces no traffic",
            normalized_live_state,
        )

    def test_dependency_audit_targets_the_next_public_release(self) -> None:
        """Keep maintenance commitments meaningful after the initial public release."""
        dependency_audit = self._roadmap_section(
            "30. CommonLib Dependency Maintenance Audit"
        )

        self.assertNotIn("before public release", dependency_audit)
        self.assertEqual(dependency_audit.count("before the next public release"), 3)

    def test_token_examples_use_secure_generation_and_powershell_assignment(self) -> None:
        """Reject insecure token generation and shell-incompatible environment examples."""
        bridge_readme = self._read("bridge/README.md")
        integration_readme = self._read("integration/README.md")
        token_example_match = re.search(
            r"(?ms)```powershell\n(?P<example>.*?RandomNumberGenerator.*?)\n```",
            bridge_readme,
        )
        self.assertIsNotNone(token_example_match)
        token_example = token_example_match.group("example")

        self.assertNotIn("Get-Random", bridge_readme)
        self.assertIn("$tokenBytes = [byte[]]::new(32)", token_example)
        self.assertIn("RandomNumberGenerator", token_example)
        self.assertIn("$rng.GetBytes($tokenBytes)", token_example)
        self.assertIn(
            "$env:DOVAHLINK_BRIDGE_TOKEN = "
            '[BitConverter]::ToString($tokenBytes).Replace("-", "").ToLowerInvariant()',
            token_example,
        )
        self.assertIn("[Array]::Clear($tokenBytes", token_example)
        self.assertLess(token_example.index("[byte[]]::new(32)"), token_example.index("GetBytes"))
        self.assertLess(
            token_example.index("$rng.GetBytes($tokenBytes)"),
            token_example.index("$env:DOVAHLINK_BRIDGE_TOKEN"),
        )
        self.assertLess(
            token_example.index("$env:DOVAHLINK_BRIDGE_TOKEN"),
            token_example.index("[Array]::Clear($tokenBytes"),
        )
        self.assertIn('$env:DOVAHLINK_BRIDGE_TOKEN = "<the same hex token', integration_readme)
        self.assertNotRegex(
            integration_readme,
            r"(?m)^DOVAHLINK_BRIDGE_TOKEN=.*\bdotnet run$",
        )

    def test_handshake_handler_revalidates_trust_after_session_admission(self) -> None:
        """Keep HandleHello's revoke/block-race closure wired after admission, not only before it."""
        handshake_handler = self._read("bridge/application/handshake_handler.cpp")

        self.assertIn("TryCreateSession(", handshake_handler)
        self.assertIn("TrustLossAfterAdmission(", handshake_handler)
        # Not "return HandshakeResult{": Fail() (used by every rejection, including this file's
        # first, much earlier one) builds one of those too. ".closeConnection = false," is unique
        # to the one real success return.
        self.assertIn(".closeConnection = false,", handshake_handler)

        try_create_session_index = handshake_handler.index("TryCreateSession(")
        revalidation_index = handshake_handler.index("TrustLossAfterAdmission(")
        success_index = handshake_handler.index(".closeConnection = false,")

        # A revoke or block landing between the earlier trust checks and session admission is
        # otherwise invisible to TrustAdminService's one-shot DisconnectIfClientActive call, which
        # already ran (and found nothing) by the time TryCreateSession makes the session visible --
        # TrustLossAfterAdmission only closes that gap if it runs after admission and before the
        # handshake is ever declared successful.
        self.assertLess(try_create_session_index, revalidation_index)
        self.assertLess(revalidation_index, success_index)

    def test_bridge_ci_caches_vcpkg_tooling_and_registries_to_avoid_gitlab_403(self) -> None:
        """Require cached vcpkg tooling and a cached colorglass registry checkout.

        Re-fetching the pinned, immutable colorglass registry from GitLab on every run is what
        caused repeated HTTP 403s from GitLab's abuse detection on shared CI IPs; caching its
        checkout (and the vcpkg tooling checkout, also pinned and immutable) removes the repeated
        fetch instead of only retrying it.
        """
        workflow = self._read(".github/workflows/bridge-ci.yml")

        # The pinned commit is defined exactly once, at job level, and referenced everywhere else
        # -- a duplicated literal here (checkout command vs. cache key) is exactly the drift that
        # would leave a bumped pin silently served from a stale cache.
        self.assertIn(
            "      VCPKG_BASELINE_COMMIT: 2f1d605400c8727cc00c15797aba796c88ccd523", workflow
        )
        self.assertEqual(
            workflow.count("2f1d605400c8727cc00c15797aba796c88ccd523"),
            1,
            "the pinned vcpkg commit must appear exactly once (the job-level env value); every "
            "other reference must go through $env:VCPKG_BASELINE_COMMIT or env.VCPKG_BASELINE_COMMIT",
        )

        tooling_cache = self._yaml_block(workflow, "      - name: Restore cached vcpkg tooling")
        self.assertIn("        id: vcpkg-tooling-cache", tooling_cache)
        self.assertIn("        uses: actions/cache@caa296126883cff596d87d8935842f9db880ef25 # v5", tooling_cache)
        self.assertIn("          path: ${{ runner.temp }}\\vcpkg", tooling_cache)
        self.assertIn(
            "          key: ${{ runner.os }}-vcpkg-tooling-${{ env.VCPKG_BASELINE_COMMIT }}",
            tooling_cache,
        )

        checkout_step = self._yaml_block(
            workflow, "      - name: Check out vcpkg at the pinned builtin baseline"
        )
        self.assertIn(
            "        if: steps.vcpkg-tooling-cache.outputs.cache-hit != 'true'", checkout_step
        )
        self.assertIn("checkout $env:VCPKG_BASELINE_COMMIT", checkout_step)

        binary_cache = self._yaml_block(workflow, "      - name: Restore vcpkg binary cache")
        self.assertIn(
            "          key: ${{ runner.os }}-vcpkg-${{ env.VCPKG_BASELINE_COMMIT }}-"
            "${{ hashFiles('bridge/vcpkg.json', 'bridge/vcpkg-configuration.json') }}",
            binary_cache,
        )

        registries_env = self._yaml_block(workflow, "      - name: Set X_VCPKG_REGISTRIES_CACHE")
        self.assertIn(
            'run: echo "X_VCPKG_REGISTRIES_CACHE=$env:RUNNER_TEMP\\vcpkg-registries-cache" '
            ">> $env:GITHUB_ENV",
            registries_env,
        )
        self.assertIn("      - name: Prepare vcpkg registries cache", workflow)
        self.assertIn(
            'New-Item -ItemType Directory -Force -Path "$env:RUNNER_TEMP\\vcpkg-registries-cache"',
            workflow,
        )

        registries_cache = self._yaml_block(workflow, "      - name: Restore vcpkg registries cache")
        self.assertIn("        uses: actions/cache@caa296126883cff596d87d8935842f9db880ef25 # v5", registries_cache)
        self.assertIn("          path: ${{ runner.temp }}\\vcpkg-registries-cache", registries_cache)
        self.assertIn(
            "          key: ${{ runner.os }}-vcpkg-registries-"
            "${{ hashFiles('bridge/vcpkg-configuration.json') }}",
            registries_cache,
        )

        self.assertLess(
            workflow.index("Restore cached vcpkg tooling"),
            workflow.index("Configure Debug"),
        )
        self.assertLess(
            workflow.index("Restore vcpkg registries cache"),
            workflow.index("Configure Debug"),
        )
        self.assertLess(
            workflow.index("      - name: Install pinned CMake"),
            workflow.index("Configure Debug"),
        )

    def test_integration_ci_caches_vcpkg_tooling_and_registries_to_avoid_gitlab_403(self) -> None:
        """Require the same vcpkg tooling/registry caching as bridge-ci.yml.

        This job runs on its own fresh runner with no bridge-ci filesystem state to reuse, so it
        needs this caching independently rather than relying on bridge-ci.yml's job having run
        first.
        """
        workflow = self._read(".github/workflows/integration-ci.yml")

        self.assertIn(
            "      VCPKG_BASELINE_COMMIT: 2f1d605400c8727cc00c15797aba796c88ccd523", workflow
        )
        self.assertEqual(
            workflow.count("2f1d605400c8727cc00c15797aba796c88ccd523"),
            1,
            "the pinned vcpkg commit must appear exactly once (the job-level env value); every "
            "other reference must go through $env:VCPKG_BASELINE_COMMIT or env.VCPKG_BASELINE_COMMIT",
        )

        tooling_cache = self._yaml_block(workflow, "      - name: Restore cached vcpkg tooling")
        self.assertIn("        id: vcpkg-tooling-cache", tooling_cache)
        self.assertIn("        uses: actions/cache@caa296126883cff596d87d8935842f9db880ef25 # v5", tooling_cache)
        self.assertIn("          path: ${{ runner.temp }}\\vcpkg", tooling_cache)
        self.assertIn(
            "          key: ${{ runner.os }}-vcpkg-tooling-${{ env.VCPKG_BASELINE_COMMIT }}",
            tooling_cache,
        )

        checkout_step = self._yaml_block(
            workflow, "      - name: Check out vcpkg at the pinned builtin baseline"
        )
        self.assertIn(
            "        if: steps.vcpkg-tooling-cache.outputs.cache-hit != 'true'", checkout_step
        )
        self.assertIn("checkout $env:VCPKG_BASELINE_COMMIT", checkout_step)

        registries_env = self._yaml_block(workflow, "      - name: Set X_VCPKG_REGISTRIES_CACHE")
        self.assertIn(
            'run: echo "X_VCPKG_REGISTRIES_CACHE=$env:RUNNER_TEMP\\vcpkg-registries-cache" '
            ">> $env:GITHUB_ENV",
            registries_env,
        )
        self.assertIn("      - name: Prepare vcpkg registries cache", workflow)
        self.assertIn(
            'New-Item -ItemType Directory -Force -Path "$env:RUNNER_TEMP\\vcpkg-registries-cache"',
            workflow,
        )

        registries_cache = self._yaml_block(workflow, "      - name: Restore vcpkg registries cache")
        self.assertIn("        uses: actions/cache@caa296126883cff596d87d8935842f9db880ef25 # v5", registries_cache)
        self.assertIn("          path: ${{ runner.temp }}\\vcpkg-registries-cache", registries_cache)
        self.assertIn(
            "          key: ${{ runner.os }}-vcpkg-registries-"
            "${{ hashFiles('bridge/vcpkg-configuration.json') }}",
            registries_cache,
        )

        self.assertLess(
            workflow.index("Restore cached vcpkg tooling"),
            workflow.index("Configure bridge debug harness"),
        )
        self.assertLess(
            workflow.index("Restore vcpkg registries cache"),
            workflow.index("Configure bridge debug harness"),
        )

    @classmethod
    def _roadmap_corpus(cls) -> str:
        """Read the ordered roadmap stage documents as one validation corpus."""
        stage_paths = sorted((REPOSITORY_ROOT / "roadmap").glob("*.md"))
        return "\n".join(path.read_text(encoding="utf-8") for path in stage_paths)

    @classmethod
    def _roadmap_section(cls, heading: str) -> str:
        """Return a phase section from its canonical roadmap stage document."""
        if heading == "Deferred possibilities":
            return cls._markdown_section("ROADMAP.md", heading)
        for path in sorted((REPOSITORY_ROOT / "roadmap").glob("*.md")):
            document = path.read_text(encoding="utf-8")
            match = re.search(
                rf"(?ms)^## {re.escape(heading)}\n(?P<body>.*?)(?=^## |\Z)",
                document,
            )
            if match is not None:
                return match.group("body")
        raise AssertionError(f"Missing roadmap section: {heading}")

    @staticmethod
    def _read(relative_path: str) -> str:
        """Read one UTF-8 repository file."""
        return (REPOSITORY_ROOT / relative_path).read_text(encoding="utf-8")

    @classmethod
    def _markdown_section(cls, relative_path: str, heading: str) -> str:
        """Return the body of one level-two Markdown section."""
        document = cls._read(relative_path)
        match = re.search(
            rf"(?ms)^## {re.escape(heading)}\n(?P<body>.*?)(?=^## |\Z)",
            document,
        )
        if match is None:
            raise AssertionError(f"Missing Markdown section: {heading}")
        return match.group("body")

    @staticmethod
    def _normalize_whitespace(value: str) -> str:
        """Collapse wrapped prose into a single comparable line."""
        return " ".join(value.split())

    @staticmethod
    def _yaml_block(document: str, header: str) -> str:
        """Return one indentation-delimited YAML mapping or sequence item."""
        lines = document.splitlines()
        try:
            start = lines.index(header)
        except ValueError as error:
            raise AssertionError(f"Missing YAML block: {header.strip()}") from error
        indentation = len(header) - len(header.lstrip())
        end = start + 1
        while end < len(lines):
            line = lines[end]
            if line and len(line) - len(line.lstrip()) <= indentation:
                break
            end += 1
        return "\n".join(lines[start:end]).rstrip()


if __name__ == "__main__":
    unittest.main()
