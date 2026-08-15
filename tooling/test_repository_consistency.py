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

        for trigger in ("push", "pull_request"):
            trigger_block = self._yaml_block(workflow, f"  {trigger}:")
            paths_block = self._yaml_block(trigger_block, "    paths:")
            self.assertEqual(
                {line.strip() for line in paths_block.splitlines()[1:] if line.strip()},
                expected_paths,
            )

        permissions = self._yaml_block(workflow, "permissions:")
        self.assertEqual(permissions, "permissions:\n  contents: read")
        self.assertEqual(len(re.findall(r"(?m)^\s*permissions:", workflow)), 1)

        self.assertEqual(len(re.findall(r"uses: actions/checkout@", workflow)), 1)
        checkout = self._yaml_block(workflow, "      - uses: actions/checkout@v6")
        self.assertEqual(
            checkout,
            "      - uses: actions/checkout@v6\n"
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
        self.assertIn(
            '"$env:ChocolateyInstall\\bin" | Out-File -FilePath $env:GITHUB_PATH',
            workflow,
        )
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
        self.assertIn("uses: actions/cache@v5", workflow)
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
        self.assertIn("uses: actions/upload-artifact@v6", workflow)
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
            '- ".github/workflows/app-ci.yml"',
        }

        for trigger in ("push", "pull_request"):
            trigger_block = self._yaml_block(workflow, f"  {trigger}:")
            if trigger == "push":
                self.assertIn("    branches: [main]", trigger_block)
            paths_block = self._yaml_block(trigger_block, "    paths:")
            self.assertEqual(
                {line.strip() for line in paths_block.splitlines()[1:] if line.strip()},
                expected_paths,
            )

        permissions = self._yaml_block(workflow, "permissions:")
        self.assertEqual(permissions, "permissions:\n  contents: read")
        checkout = self._yaml_block(workflow, "      - uses: actions/checkout@v6")
        self.assertEqual(
            checkout,
            "      - uses: actions/checkout@v6\n"
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
        self.assertIn("uses: subosito/flutter-action@v2", workflow)
        self.assertIn("channel: stable", workflow)
        self.assertIn("cache: true", workflow)
        step_names_and_commands = (
            ("Restore Flutter dependencies", "flutter pub get"),
            ("Generate Dart sources", "dart run build_runner build"),
            ("Analyze Flutter client", "flutter analyze"),
            ("Test Flutter client", "flutter test"),
            ("Build Windows client", "flutter build windows --debug"),
        )
        step_positions = []
        for step_name, command in step_names_and_commands:
            step = self._yaml_block(workflow, f"      - name: {step_name}")
            self.assertIn("        working-directory: app", step)
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

        for trigger in ("push", "pull_request"):
            trigger_block = self._yaml_block(workflow, f"  {trigger}:")
            if trigger == "push":
                self.assertIn("    branches: [main]", trigger_block)
            paths_block = self._yaml_block(trigger_block, "    paths:")
            self.assertEqual(
                {line.strip() for line in paths_block.splitlines()[1:] if line.strip()},
                expected_paths,
            )

        self.assertEqual(
            self._yaml_block(workflow, "permissions:"),
            "permissions:\n  contents: read",
        )
        checkout = self._yaml_block(workflow, "      - uses: actions/checkout@v6")
        self.assertEqual(
            checkout,
            "      - uses: actions/checkout@v6\n"
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
        self.assertIn("cmake --version=4.4.2", workflow)
        self.assertIn("ninja --version=1.13.2", workflow)
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
            '"$env:ChocolateyInstall\\bin" | Out-File -FilePath $env:GITHUB_PATH',
            workflow,
        )
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
        self.assertIn(
            "git -C \"${{ runner.temp }}\\vcpkg\" checkout 2f1d605400c8727cc00c15797aba796c88ccd523",
            workflow,
        )
        self.assertIn(
            '& "${{ runner.temp }}\\vcpkg\\bootstrap-vcpkg.bat" -disableMetrics',
            workflow,
        )
        self.assertIn("uses: actions/setup-dotnet@v5", workflow)
        self.assertIn("dotnet-version: 9.0.x", workflow)
        self.assertIn("uses: ilammy/msvc-dev-cmd@v1", workflow)
        self.assertIn("uses: actions/cache@v5", workflow)
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
        self.assertIn("uses: actions/upload-artifact@v6", workflow)
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
            '- "CONTRIBUTING.md"',
            '- ".github/workflows/**"',
            '- ".vscode/**"',
            '- "app/**"',
            '- "bridge/**"',
            '- "integration/**"',
            '- "protocol/**"',
            '- "tooling/**"',
        }

        for trigger in ("push", "pull_request"):
            trigger_block = self._yaml_block(workflow, f"  {trigger}:")
            if trigger == "push":
                self.assertIn("    branches: [main]", trigger_block)
            paths_block = self._yaml_block(trigger_block, "    paths:")
            self.assertEqual(
                {line.strip() for line in paths_block.splitlines()[1:] if line.strip()},
                expected_paths,
            )

        self.assertEqual(
            self._yaml_block(workflow, "permissions:"),
            "permissions:\n  contents: read",
        )
        checkout = self._yaml_block(workflow, "      - uses: actions/checkout@v6")
        self.assertEqual(
            checkout,
            "      - uses: actions/checkout@v6\n"
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
        self.assertIn("uses: actions/setup-python@v6", workflow)
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
            "Prefer maintained stable releases and pinned action versions",
            common,
        )
        self.assertIn("never use floating branches such as `@main`", common)
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
        """Reject stale official action majors and floating workflow references."""
        expected_versions = {
            "actions/checkout": "v6",
            "actions/cache": "v5",
            "actions/setup-python": "v6",
            "actions/setup-dotnet": "v5",
            "actions/upload-artifact": "v6",
        }

        workflow_directory = REPOSITORY_ROOT / ".github" / "workflows"
        for workflow_path in sorted(workflow_directory.glob("*.yml")):
            workflow = workflow_path.read_text(encoding="utf-8")
            for action, version in expected_versions.items():
                references = re.findall(
                    rf"(?m)^\s*uses:\s*({re.escape(action)}@[^\s#]+)",
                    workflow,
                )
                for reference in references:
                    self.assertEqual(reference, f"{action}@{version}", workflow_path.name)

            self.assertNotRegex(
                workflow,
                r"(?m)^\s*uses:\s*[^\s#]+@(main|master|develop|latest)\s*(?:#|$)",
                workflow_path.name,
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
            "$LASTEXITCODE -ne 0",
            'Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "python"',
            'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @(\"pub\", \"get\")',
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
            '"--build", "--preset", "windows-x64-debug", "--target", "dovahlink_bridge_harness"',
            '"restore", "integration/DovahLinkValidation.sln"',
            '"test", "integration/DovahLinkValidation.sln", "--configuration", "Release", "--no-restore"',
        )
        for fragment in required_fragments:
            self.assertIn(fragment, script)

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
            script.index('"--build", "--preset", "windows-x64-debug", "--target", "dovahlink_bridge_harness"'),
            script.index('"restore", "integration/DovahLinkValidation.sln"'),
            script.index('"test", "integration/DovahLinkValidation.sln", "--configuration", "Release", "--no-restore"'),
        ]
        self.assertEqual(command_positions, sorted(command_positions))
        self.assertNotIn("choco install", script)
        self.assertIn("All local CI command payloads passed.", script)

    def test_published_bridge_release_and_roadmap_status_agree(self) -> None:
        """Keep the published bridge version and completed roadmap phase synchronized."""
        manifest = json.loads(self._read("bridge/vcpkg.json"))
        version = "0.2.0"
        root_readme = self._read("README.md")
        bridge_readme = self._read("bridge/README.md")
        phase_zero = self._markdown_section("ROADMAP.md", "0. Documentation baseline")
        phase_zero_five = self._markdown_section(
            "ROADMAP.md", "0.5 Client and Protocol Foundation"
        )
        phase_one = self._markdown_section("ROADMAP.md", "1. Skyrim Bridge Foundation")
        phase_two = self._markdown_section(
            "ROADMAP.md", "2. Bridge Identity and Authoritative State Foundation"
        )

        self.assertEqual(manifest["version-string"], version)
        self.assertIn(
            "published on Nexus Mods]"
            f"(https://www.nexusmods.com/skyrimspecialedition/mods/188165) as version `{version}`",
            root_readme,
        )
        self.assertIn(f"published Phase 2\nbridge release, version `{version}`", bridge_readme)
        self.assertIn(f"Bridge version `{version}` supports exactly one runtime", bridge_readme)
        for completed_phase in (phase_zero, phase_zero_five, phase_one, phase_two):
            self.assertEqual(
                re.findall(r"(?m)^\*\*Status:\*\* .+$", completed_phase),
                ["**Status:** Complete"],
            )

    def test_bridge_version_literals_match_the_published_release(self) -> None:
        """Guard every hand-maintained bridge-version literal against drift from vcpkg.json."""
        manifest = json.loads(self._read("bridge/vcpkg.json"))
        version = manifest["version-string"]

        for source_path in (
            "bridge/harness/dovahlink_bridge_harness.cpp",
            "bridge/plugin/dovahlink_bridge_plugin.cpp",
            "bridge/application/connection_session_test.cpp",
            "bridge/application/bridge_worker_pool_test.cpp",
        ):
            self.assertIn(f'kBridgeVersion = "{version}"', self._read(source_path), source_path)

        self.assertIn(
            f'CHECK(helloAck->bridgeVersion == "{version}");',
            self._read("bridge/protocol/messages_test.cpp"),
        )

    def test_changelog_matches_the_published_bridge_version(self) -> None:
        """Keep CHANGELOG.md's newest entry synchronized with the published bridge version."""
        manifest = json.loads(self._read("bridge/vcpkg.json"))
        changelog = self._read("CHANGELOG.md")
        entry_versions = re.findall(r"(?m)^## \[(\d+\.\d+\.\d+)\]", changelog)

        self.assertTrue(entry_versions, "CHANGELOG.md has no version entries.")
        self.assertEqual(entry_versions[0], manifest["version-string"])
        for known_version in ("0.1.0", "0.2.0"):
            self.assertIn(known_version, entry_versions)
        parsed_versions = [tuple(int(part) for part in v.split(".")) for v in entry_versions]
        self.assertEqual(
            parsed_versions,
            sorted(set(parsed_versions), reverse=True),
            "CHANGELOG.md entries must be strictly descending with no duplicate versions.",
        )

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
        roadmap = self._read("ROADMAP.md")
        expected_headings = [
            "0. Documentation baseline",
            "0.5 Client and Protocol Foundation",
            "1. Skyrim Bridge Foundation",
            "2. Bridge Identity and Authoritative State Foundation",
            "3. Local Device Pairing and Reconnection",
            "4. Live State Synchronization Foundation",
            "5. PC / Second-Screen Baseline",
            "6. Core UI Theme System",
            "7. Live Player State",
            "8. Multi-Client Runtime Foundation",
            "9. Multi-Bridge and Local Discovery Foundation",
            "10. Automatic Connection and Transport Selection",
            "11. Mod Awareness",
            "12. Interactive Map Foundation",
            "13. Map Asset and Worldspace System",
            "14. Quests",
            "15. Navigation / Path Guidance",
            "16. Inventory",
            "17. Equipment",
            "18. Magic, Spells, Shouts, and Powers",
            "19. Favorites and Hotkeys",
            "20. Customizable Dashboard",
            "21. Secure LAN Transport and Network Discovery",
            "22. Mobile / Tablet Client",
            "23. Item Knowledge and Search",
            "24. Legacy of the Dragonborn Integration",
            "25. Installed UI Detection",
            "26. Optional UI Mod Adapters",
            "27. Safe Companion Authorization Foundation",
            "28. Runtime Profiling and Advanced Bridge Hardening",
            "29. CommonLib Dependency Maintenance Audit",
        ]
        actual_headings = re.findall(r"(?m)^## (\d+(?:\.\d+)?\.? .+)$", roadmap)

        self.assertEqual(actual_headings, expected_headings)
        self.assertNotIn("## 1.25 ", roadmap)
        self.assertNotIn("## 1.5 ", roadmap)
        self.assertEqual(roadmap.count("**Status:** Next"), 0)
        self.assertEqual(roadmap.count("**Status:** Complete"), 4)
        self.assertEqual(len(re.findall(r"(?m)^\*\*Status:\*\* Planned$", roadmap)), 26)
        self.assertEqual(roadmap.count("**Status:** Planned after read-only product validation"), 1)

        for heading in expected_headings:
            phase = self._markdown_section("ROADMAP.md", heading)
            if heading.startswith(("0. ", "0.5 ", "1. ", "2. ")):
                expected_status = "**Status:** Complete"
            elif heading.startswith("27. "):
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
        self.assertIn("See `ROADMAP.md`'s Phase 3", bridge_readme)
        self.assertIn("## Live event delivery is deferred to Phase 4", bridge_readme)
        self.assertIn("`ROADMAP.md`'s Phase 4\nand Phase 3 entries", integration_readme)

        ordering = {heading: roadmap.index(f"## {heading}") for heading in expected_headings}
        self.assertLess(ordering["5. PC / Second-Screen Baseline"], ordering["6. Core UI Theme System"])
        self.assertLess(ordering["6. Core UI Theme System"], ordering["7. Live Player State"])
        self.assertLess(ordering["7. Live Player State"], ordering["8. Multi-Client Runtime Foundation"])
        self.assertLess(ordering["8. Multi-Client Runtime Foundation"], ordering["12. Interactive Map Foundation"])
        self.assertLess(
            ordering["21. Secure LAN Transport and Network Discovery"],
            ordering["22. Mobile / Tablet Client"],
        )

        identity = self._markdown_section(
            "ROADMAP.md", "2. Bridge Identity and Authoritative State Foundation"
        )
        authorization = self._markdown_section(
            "ROADMAP.md", "27. Safe Companion Authorization Foundation"
        )
        deferred = self._markdown_section("ROADMAP.md", "Deferred possibilities")
        self.assertIn("does not add a separate\ngame-process identifier", identity)
        dependency_expectations = {
            "3. Local Device Pairing and Reconnection": "depends on Phase 2",
            "5. PC / Second-Screen Baseline": "validates Phases 2 through 4",
            "8. Multi-Client Runtime Foundation": "follows the Phase 7 single-client proof",
            "9. Multi-Bridge and Local Discovery Foundation": "depends on Phases 2 and 8",
            "27. Safe Companion Authorization Foundation": (
                "depends on identity, multi-client isolation, and security"
            ),
        }
        for heading, expected_dependency in dependency_expectations.items():
            self.assertIn(
                expected_dependency,
                self._markdown_section("ROADMAP.md", heading),
                heading,
            )
        self.assertIn("without exposing a generic command API", authorization)
        self.assertIn("Validate the machinery without adding gameplay mutation", authorization)
        self.assertIn("Each action needs its own product decision", authorization)
        self.assertIn("no Skyrim\nmutation is exposed", authorization)
        self.assertIn("Individual companion actions", deferred)
        for deferred_action in ("equipment", "favorites or hotkeys", "map markers", "fast travel"):
            self.assertIn(deferred_action, deferred)

    def test_live_state_phase_depends_on_reconnect_and_defines_session_loss(self) -> None:
        """Preserve reconnect ordering and the bounded, session-scoped reliable-event contract."""
        live_state = self._markdown_section("ROADMAP.md", "4. Live State Synchronization Foundation")
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
        identity = self._markdown_section(
            "ROADMAP.md", "2. Bridge Identity and Authoritative State Foundation"
        )
        live_state = self._markdown_section("ROADMAP.md", "4. Live State Synchronization Foundation")
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
        roadmap = self._read("ROADMAP.md")
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
        dependency_audit = self._markdown_section(
            "ROADMAP.md", "29. CommonLib Dependency Maintenance Audit"
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
