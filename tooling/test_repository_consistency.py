"""Guard release, workflow, and future-state documentation consistency."""

import json
import re
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent


class RepositoryConsistencyTests(unittest.TestCase):
    """Verify that release-facing configuration and documentation remain aligned."""

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
        checkout = self._yaml_block(workflow, "      - uses: actions/checkout@v4")
        self.assertEqual(
            checkout,
            "      - uses: actions/checkout@v4\n"
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
        self.assertIn("VCPKG_DEFAULT_BINARY_CACHE:", workflow)
        self.assertIn("uses: actions/cache@v4", workflow)
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
        self.assertIn("uses: actions/upload-artifact@v4", workflow)
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
        checkout = self._yaml_block(workflow, "      - uses: actions/checkout@v4")
        self.assertEqual(
            checkout,
            "      - uses: actions/checkout@v4\n"
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
        checkout = self._yaml_block(workflow, "      - uses: actions/checkout@v4")
        self.assertEqual(
            checkout,
            "      - uses: actions/checkout@v4\n"
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
        self.assertIn(
            "git -C \"${{ runner.temp }}\\vcpkg\" checkout 2f1d605400c8727cc00c15797aba796c88ccd523",
            workflow,
        )
        self.assertIn(
            '& "${{ runner.temp }}\\vcpkg\\bootstrap-vcpkg.bat" -disableMetrics',
            workflow,
        )
        self.assertIn("uses: actions/setup-dotnet@v4", workflow)
        self.assertIn("dotnet-version: 9.0.x", workflow)
        self.assertIn("uses: ilammy/msvc-dev-cmd@v1", workflow)
        self.assertIn("uses: actions/cache@v4", workflow)
        self.assertIn("VCPKG_DEFAULT_BINARY_CACHE:", workflow)
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
        self.assertIn("uses: actions/upload-artifact@v4", workflow)
        self.assertIn("integration/TestResults/", workflow)
        self.assertLess(
            workflow.index("Build bridge validation harness"),
            workflow.index("Run integration scenarios"),
        )
        self.assertLess(
            workflow.index("Restore integration dependencies"),
            workflow.index("Run integration scenarios"),
        )

    def test_published_bridge_release_and_roadmap_status_agree(self) -> None:
        """Keep the published bridge version and completed roadmap phase synchronized."""
        manifest = json.loads(self._read("bridge/vcpkg.json"))
        version = "0.1.0"
        root_readme = self._read("README.md")
        bridge_readme = self._read("bridge/README.md")
        phase_one = self._markdown_section("ROADMAP.md", "1. Skyrim Bridge Foundation")
        phase_one_twenty_five = self._markdown_section(
            "ROADMAP.md", "1.25 Local Device Pairing and Reconnection"
        )

        self.assertEqual(manifest["version-string"], version)
        self.assertIn(
            "published on Nexus Mods]"
            f"(https://www.nexusmods.com/skyrimspecialedition/mods/188165) as version `{version}`",
            root_readme,
        )
        self.assertIn(f"published Phase 1\nbridge release, version `{version}`", bridge_readme)
        self.assertIn(f"Bridge version `{version}` supports exactly one runtime", bridge_readme)
        self.assertEqual(re.findall(r"(?m)^\*\*Status:\*\* .+$", phase_one), ["**Status:** Complete"])
        self.assertEqual(
            re.findall(r"(?m)^\*\*Status:\*\* .+$", phase_one_twenty_five),
            ["**Status:** Next"],
        )

    def test_live_state_phase_depends_on_reconnect_and_defines_session_loss(self) -> None:
        """Preserve reconnect ordering and the bounded, session-scoped reliable-event contract."""
        live_state = self._markdown_section(
            "ROADMAP.md", "1.5 Live State Synchronization Foundation"
        )
        bridge_live_state = self._markdown_section(
            "bridge/README.md", "Live event delivery is deferred to Phase 1.5"
        )
        normalized_live_state = self._normalize_whitespace(live_state)
        normalized_bridge_live_state = self._normalize_whitespace(bridge_live_state)

        self.assertIn("depends on the Phase 1 bridge foundation and Phase 1.25", live_state)
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

    def test_dependency_audit_targets_the_next_public_release(self) -> None:
        """Keep maintenance commitments meaningful after the initial public release."""
        dependency_audit = self._markdown_section(
            "ROADMAP.md", "23. CommonLib Dependency Maintenance Audit"
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
