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
            "`stateAuthorityId`",
            "`playContextId`",
            "`clientId`",
            "`sessionId`",
        ):
            self.assertIn(identity, architecture)
        self.assertNotIn("gameInstanceId", architecture)

        for required_phrase in (
            "A native-plugin restart creates a new identity",
            "A client reconnect creates a new `sessionId` without silently changing its `clientId`",
            "loading another save creates a new `playContextId`",
            "It is valid only for that socket and is invalidated when the connection ends",
            "one authoritative state store for the active play context",
            "captured once and shared with subscribed clients",
            "adding a client must not repeat equivalent Skyrim reads",
            "within one state area, `playContextId`, and `stateAuthorityId`",
            "A revision advances only when that authoritative state changes",
            "Sending or requesting another snapshot does not advance the revision",
            "reconnecting does not create a new authoritative revision",
            "use that authoritative-lineage identity, `playContextId`, and the state-area "
            "revision together to reject stale state",
            "invalidates the previous context's state and establishes fresh authoritative state",
            "must not be implemented by silently reinterpreting messages from the previously "
            "published experimental release",
            "Transport location is not identity",
            "receives no undocumented protocol behavior or privileged access",
            "Screens, dashboard modules, orientation, widget layout",
        ):
            self.assertIn(required_phrase, normalized_architecture)

        self.assertIn(
            "belongs to that authority continuity epoch, play context, and state area",
            schema,
        )
        for target_identity in ("stateAuthorityId", "playContextId", "clientId"):
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

    def test_commonlibsse_port_is_repository_owned_and_pinned(self) -> None:
        """Keep the shared CommonLib port local and aligned with the frozen source recipe."""
        expected_overlay = {"overlay-ports": ["../tooling/vcpkg-ports"]}
        for configuration_path in ("adapter/vcpkg-configuration.json",):
            self.assertEqual(
                json.loads(self._read(configuration_path)),
                expected_overlay,
                configuration_path,
            )

        for manifest_path in ("adapter/vcpkg.json",):
            manifest = json.loads(self._read(manifest_path))
            self.assertIn("commonlibsse-ng-flatrim", manifest["dependencies"])

        port = json.loads(
            self._read("tooling/vcpkg-ports/commonlibsse-ng-flatrim/vcpkg.json")
        )
        self.assertEqual(port["name"], "commonlibsse-ng-flatrim")
        self.assertEqual(port["version-semver"], "9.0.0")
        self.assertEqual(port["port-version"], 0)
        dependencies = {
            dependency["name"] if isinstance(dependency, dict) else dependency
            for dependency in port["dependencies"]
        }
        self.assertEqual(
            dependencies,
            {
                "vcpkg-cmake-config",
                "directxmath",
                "directxtk",
                "fmt",
                "nlohmann-json",
                "rapidcsv",
                "simpleini",
                "spdlog",
                "toml11",
                "xbyak",
            },
        )

        portfile = self._read(
            "tooling/vcpkg-ports/commonlibsse-ng-flatrim/portfile.cmake"
        )
        for required_fragment in (
            "REPO alandtse/CommonLibSSE-NG",
            "REF 5decf47b01dde5501b03afaa91cd4d182e793cca",
            "SHA512 58a1647f5e7a23d3f5e75a02b2d1a4ef9d8a1b4e799c034c2086c20d736ca920cc8927100157130ee766a32cd20d4e2de60c1019e6d3e2bc37bfb9c6fcd8c25b",
            "HEAD_REF ng",
            "-DENABLE_SKYRIM_VR=off",
            "-DBUILD_TESTS=off",
            "-DSKSE_SUPPORT_XBYAK=off",
            "find_dependency(directxtk CONFIG)",
        ):
            self.assertIn(required_fragment, portfile)

        for stale_fragment in (
            "REPO CharmedBaryon/CommonLibSSE",
            "-DSKSE_SUPPORT_XBYAK=on",
            "fix-register-latent-function-return-type.patch",
        ):
            self.assertNotIn(stale_fragment, portfile)

        self.assertFalse(
            (
                REPOSITORY_ROOT
                / "tooling/vcpkg-ports/commonlibsse-ng-flatrim"
                / "fix-register-latent-function-return-type.patch"
            ).is_file(),
            "the retired latent-function patch must not remain beside the migrated portfile",
        )

    def test_clang_format_uses_the_established_source_style(self) -> None:
        """Keep C++ formatting explicit instead of relying on clang-format defaults."""
        style = self._read(".clang-format")

        for setting in (
            "BasedOnStyle: LLVM",
            "IndentWidth: 4",
            "ContinuationIndentWidth: 4",
            "ColumnLimit: 0",
            "PointerAlignment: Left",
            "ReferenceAlignment: Left",
            "DerivePointerAlignment: false",
            "SpacesInLineCommentPrefix:",
            "  Minimum: 2",
            "  Maximum: 2",
        ):
            self.assertIn(setting, style)

    def test_app_ci_covers_flutter_quality_and_windows_build(self) -> None:
        """Require SDK and Flutter generation, analysis, tests, and desktop build coverage."""
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
        self.assertIn("    timeout-minutes: 30", workflow)
        self.assertIn("        shell: pwsh", workflow)
        self.assertIn("  workflow_dispatch:", workflow)
        self.assertIn(
            "  group: app-ci-${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}",
            workflow,
        )
        self.assertIn("  cancel-in-progress: true", workflow)
        self.assertIn(
            "uses: subosito/flutter-action@1a449444c387b1966244ae4d4f8c696479add0b2 # v2",
            workflow,
        )
        self.assertIn("channel: stable", workflow)
        self.assertIn("cache: true", workflow)
        step_names_and_commands = (
            (
                "Restore Dart SDK dependencies",
                "sdk/dart/dovahlink_client",
                "dart pub get",
            ),
            (
                "Generate Dart SDK sources",
                "sdk/dart/dovahlink_client",
                "dart run build_runner build",
            ),
            ("Analyze Dart SDK", "sdk/dart/dovahlink_client", "dart analyze"),
            ("Test Dart SDK", "sdk/dart/dovahlink_client", "dart test"),
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

    def test_host_ci_covers_build_tests_and_xml_documentation(self) -> None:
        """Require the host executable, tests, and XML documentation checks on Windows CI."""
        workflow = self._read(".github/workflows/host-ci.yml")
        expected_paths = {
            '- "host/**"',
            '- "ARCHITECTURE.md"',
            '- "ai/context/host/**"',
            '- "ai/context/common.md"',
            '- "ai/context/dotnet/**"',
            '- ".github/workflows/host-ci.yml"',
            '- "adapter-host-ipc/**"',
        }

        push_block = self._yaml_block(workflow, "  push:")
        self.assertIn("    branches: [main]", push_block)
        paths_block = self._yaml_block(push_block, "    paths:")
        self.assertEqual(
            {line.strip() for line in paths_block.splitlines()[1:] if line.strip()},
            expected_paths,
        )
        self.assertNotIn("paths:", self._yaml_block(workflow, "  pull_request:"))
        self.assertEqual(
            self._yaml_block(workflow, "permissions:"),
            "permissions:\n  contents: read",
        )
        self.assertIn(
            "uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6",
            workflow,
        )
        self.assertIn(
            "uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5",
            workflow,
        )
        self.assertIn("    runs-on: windows-2022", workflow)
        self.assertIn("    timeout-minutes: 10", workflow)
        self.assertIn("        shell: pwsh", workflow)
        self.assertIn("  workflow_dispatch:", workflow)
        self.assertIn(
            "  group: host-ci-${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}",
            workflow,
        )
        for fragment in (
            "dotnet restore host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj",
            "dotnet build host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --configuration Release --no-restore --no-incremental",
            "host/DovahLink.Host/bin/Release/net9.0-windows/DovahLink.Host.exe",
            "dotnet test host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj --configuration Release --no-restore --no-build",
            "-p:GenerateDocumentationFile=true",
            "-p:TreatWarningsAsErrors=true",
        ):
            self.assertIn(fragment, workflow)
        self.assertNotIn("continue-on-error:", workflow)

    def test_builder_ci_covers_build_and_tests(self) -> None:
        """Require the DovahLinkBuilder solution to restore, build, test, and publish on Windows CI."""
        workflow = self._read(".github/workflows/tooling-builder-ci.yml")
        expected_paths = {
            '- "tooling/DovahLinkBuilder/**"',
            '- "ai/context/tooling/**"',
            '- "ai/context/dotnet/**"',
            '- "ai/context/common.md"',
            '- ".github/workflows/tooling-builder-ci.yml"',
        }

        push_block = self._yaml_block(workflow, "  push:")
        self.assertIn("    branches: [main]", push_block)
        paths_block = self._yaml_block(push_block, "    paths:")
        self.assertEqual(
            {line.strip() for line in paths_block.splitlines()[1:] if line.strip()},
            expected_paths,
        )
        # Pull-request CI must always post a status regardless of changed files: a path filter here
        # would let a PR outside these paths skip this required branch-protection check, leaving
        # the PR stuck waiting on a status that never arrives.
        self.assertNotIn("paths:", self._yaml_block(workflow, "  pull_request:"))
        self.assertEqual(
            self._yaml_block(workflow, "permissions:"),
            "permissions:\n  contents: read",
        )
        self.assertIn(
            "uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6",
            workflow,
        )
        self.assertIn(
            "uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5",
            workflow,
        )
        self.assertIn("    runs-on: windows-2022", workflow)
        self.assertIn("    timeout-minutes: 10", workflow)
        self.assertIn("        shell: pwsh", workflow)
        self.assertIn("  workflow_dispatch:", workflow)
        self.assertIn(
            "  group: builder-ci-${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}",
            workflow,
        )
        for fragment in (
            "dotnet restore tooling/DovahLinkBuilder/DovahLinkBuilder.slnx -p:Configuration=Release",
            "dotnet build tooling/DovahLinkBuilder/DovahLinkBuilder.slnx --configuration Release --no-restore",
            "dotnet test tooling/DovahLinkBuilder/DovahLinkBuilder.slnx --configuration Release --no-restore --no-build",
            "dotnet publish tooling/DovahLinkBuilder/DovahLinkBuilder/DovahLinkBuilder.csproj -p:PublishProfile=FolderProfile --no-restore",
            "tooling/out/DovahLinkBuilder/DovahLinkBuilder.exe",
            "Expected published DovahLinkBuilder executable was not built",
        ):
            self.assertIn(fragment, workflow)
        self.assertNotIn("continue-on-error:", workflow)

        # These publish properties must live in the project file itself, not only in a manual
        # command: that is what keeps CI, Visual Studio's Publish button, the .pubxml, and the
        # README's documented command all producing the same artifact.
        csproj = self._read(
            "tooling/DovahLinkBuilder/DovahLinkBuilder/DovahLinkBuilder.csproj"
        )
        for fragment in (
            "<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>",
            "<DebugType>None</DebugType>",
        ):
            self.assertIn(fragment, csproj)

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
            '- "host/CHANGELOG.md"',
            '- "integration/**"',
            '- "protocol/**"',
            '- "sdk/**"',
            '- "tooling/**"',
            '- "VERSION"',
            '- "adapter-host-ipc/**"',
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
            "          persist-credentials: false\n"
            "          fetch-depth: 0",
        )
        self.assertIn("runs-on: ubuntu-latest", workflow)
        self.assertIn("timeout-minutes: 10", workflow)
        self.assertIn("        shell: bash", workflow)
        self.assertEqual(
            self._yaml_block(workflow, "    env:"),
            "    env:\n      EnableWindowsTargeting: true",
        )
        self.assertIn("  workflow_dispatch:", workflow)
        self.assertIn(
            "  group: tooling-ci-${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}",
            workflow,
        )
        self.assertIn("  cancel-in-progress: true", workflow)
        self.assertIn(
            "uses: actions/setup-python@ece7cb06caefa5fff74198d8649806c4678c61a1 # v6",
            workflow,
        )
        self.assertIn('python-version: "3.13"', workflow)
        self.assertIn(
            "uses: subosito/flutter-action@1a449444c387b1966244ae4d4f8c696479add0b2 # v2",
            workflow,
        )
        self.assertIn(
            "uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5",
            workflow,
        )
        sdk_restore = self._yaml_block(
            workflow,
            "      - name: Restore Dart SDK dependencies",
        )
        self.assertIn(
            "        working-directory: sdk/dart/dovahlink_client", sdk_restore
        )
        self.assertIn("        run: dart pub get", sdk_restore)
        app_restore = self._yaml_block(
            workflow,
            "      - name: Restore Flutter dependencies",
        )
        self.assertIn("        working-directory: app", app_restore)
        self.assertIn("        run: flutter pub get", app_restore)
        self.assertLess(
            workflow.index("Restore Dart SDK dependencies"),
            workflow.index("Check formatting of changed source files"),
        )
        self.assertLess(
            workflow.index("Restore Flutter dependencies"),
            workflow.index("Check formatting of changed source files"),
        )
        formatter_install = self._yaml_block(
            workflow,
            "      - name: Install formatter tools",
        )
        self.assertIn('llvm_version="19.1.5"', formatter_install)
        self.assertIn(
            'llvm_sha256="13e9975b026d431c945927960e5f8c0a47a155a2f600f57e85f4d1482620c65f"',
            formatter_install,
        )
        self.assertIn(
            'curl --fail --location --retry 3 --output "$RUNNER_TEMP/$llvm_archive" "$llvm_url"',
            formatter_install,
        )
        self.assertIn(
            'llvm_archive="LLVM-$llvm_version-Linux-X64.tar.xz"', formatter_install
        )
        self.assertIn(
            'llvm_url="https://github.com/llvm/llvm-project/releases/download/llvmorg-$llvm_version/$llvm_archive"',
            formatter_install,
        )
        self.assertIn(
            'echo "$llvm_sha256  $RUNNER_TEMP/$llvm_archive" | sha256sum --check --strict',
            formatter_install,
        )
        self.assertIn(
            'tar -xJf "$RUNNER_TEMP/$llvm_archive" -C "$RUNNER_TEMP"', formatter_install
        )
        self.assertIn(
            'echo "$RUNNER_TEMP/LLVM-$llvm_version-Linux-X64/bin" >> "$GITHUB_PATH"',
            formatter_install,
        )
        self.assertIn(
            'export PATH="$RUNNER_TEMP/LLVM-$llvm_version-Linux-X64/bin:$PATH"',
            formatter_install,
        )
        self.assertIn("clang-format --version", formatter_install)
        self.assertLess(
            formatter_install.index("export PATH="),
            formatter_install.index("clang-format --version"),
        )
        self.assertNotIn("apt-get install --yes clang-format", formatter_install)
        self.assertIn("python -m pip install ruff", workflow)
        self.assertIn("Install-Module PSScriptAnalyzer", workflow)
        self.assertIn('dotnet restore "$project"', workflow)
        self.assertIn(
            'run: python -m unittest discover -s tooling -p "test_*.py"', workflow
        )
        self.assertIn("python tooling/format_staged.py --check --paths", workflow)
        self.assertIn("git diff --name-only -z --diff-filter=ACMR", workflow)
        self.assertIn("set -euo pipefail", workflow)
        self.assertIn('changed_file="$(mktemp)"', workflow)
        self.assertIn("if ! git diff --name-only", workflow)
        self.assertIn(
            "Unable to determine changed files for formatter verification.", workflow
        )
        self.assertIn("core.hooksPath .githooks", self._read("CONTRIBUTING.md"))
        hook = self._read(".githooks/pre-commit")
        self.assertIn("exec python tooling/format_staged.py", hook)
        formatter = self._read("tooling/format_staged.py")
        self.assertIn("partial_staged_paths", formatter)
        self.assertIn("Required formatter(s) unavailable", formatter)
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

        for workflow_path in (".github/workflows/adapter-ci.yml",):
            workflow = self._read(workflow_path)
            self.assertIn(
                "Maintained stable release; no stable Node 24 replacement is available yet.",
                workflow,
            )

    def test_documentation_and_ordering_conventions_are_semantic_not_append_only(
        self,
    ) -> None:
        """Guard the semantic member/collection ordering rule and the documentation
        economy rules that replaced the old universal append-only convention.

        Checks structural invariants (heading present/absent, a short identifying
        phrase per rule, the frozen numeric threshold) rather than pinning full prose
        sentences or line-wrap positions, so a future reword of the surrounding
        explanation does not need a matching test change.
        """
        common = self._read("ai/context/common.md")

        self.assertIn("## Member and collection ordering", common)
        self.assertNotIn("## Addition convention", common)
        self.assertIn("Changelog entries: reverse-chronological", common)
        self.assertIn("C++ data members are the one exception", common)
        self.assertIn("20-40 lines", common)
        self.assertIn("Route information to its owning home", common)
        self.assertIn(
            "Document every parameter, every non-void return value, and every exception",
            common,
        )

    def test_common_repository_boundaries_use_current_adapter_and_host_naming(
        self,
    ) -> None:
        """Guard common.md's repository-boundary and area-pointer prose against
        describing the deleted native plugin's directory as current architecture."""
        common = self._read("ai/context/common.md")

        self.assertIn(
            "`adapter/` is reserved for the native SKSE Adapter plugin", common
        )
        self.assertIn("`host/` is reserved for the C# Host process", common)
        self.assertIn("native Adapter, C# Host, protocol", common)
        self.assertIn("C++ (native Adapter): `ai/context/skse/cpp-style.md`", common)
        self.assertIn(
            "update\n  Adapter, Host, SDK, app, tests, and docs together", common
        )

    def test_root_instructions_use_current_host_adapter_layering(self) -> None:
        """Guard AGENTS.md's active instructions and ROADMAP.md's layering example against
        reverting to the retired three-tier boundary framing this repository replaced.
        """
        agents = self._read("AGENTS.md")
        roadmap = self._read("ROADMAP.md")

        self.assertIn(
            "tests that verify the client and Host meet at the contract", agents
        )
        self.assertIn(
            "Do not add Flutter, the SKSE adapter, networking, or protocol implementation",
            agents,
        )
        self.assertIn(
            "Treat SKSE/game integration, Host/Adapter composition, WebSocket and session "
            "lifecycle",
            agents,
        )
        # The legitimate historical reference must survive this pass untouched.
        self.assertIn(
            "the retired native SKSE plugin's design as reference only", agents
        )

        self.assertNotIn("Core / Skyrim / Bridge", roadmap)
        self.assertIn(
            "Adapter / Host\n          ↓\n  SDK / Client Integration", roadmap
        )

    def test_csharp_style_defines_semantic_member_ordering(self) -> None:
        """Guard the semantic C# member-ordering rule that replaced append-only
        placement, and the required-but-concise parameter/return/exception rule."""
        dotnet_style = self._read("ai/context/dotnet/csharp-style.md")

        self.assertIn("## Member ordering", dotnet_style)
        for ordered_item in (
            "1. constants and static state;",
            "2. injected dependencies (fields populated by the constructor);",
            "3. mutable instance state;",
            "4. constructors;",
            "5. properties and events;",
            "6. interface implementation and override methods, kept together as one group;",
            "7. other public and internal methods;",
            "8. private helper methods;",
            "9. nested types.",
        ):
            self.assertIn(ordered_item, dotnet_style)
        self.assertIn("Do not interleave a private helper", dotnet_style)
        self.assertIn("`Enums.cs`/`Constants.cs` files", dotnet_style)
        self.assertIn("with `<param>`/`<typeparam>`", dotnet_style)
        self.assertIn("with `<returns>`, normally in one or two lines", dotnet_style)
        self.assertIn("Add `<remarks>` only when", dotnet_style)
        self.assertNotIn("only when they add useful contract", dotnet_style)

    def test_cpp_style_normative_rules_do_not_depend_on_deleted_native_plugin_paths(
        self,
    ) -> None:
        """Guard cpp-style.md's normative rules against depending on the deleted native
        plugin's paths (removed in 3A.2) or the retired skse/architecture.md as current authority, against
        prescribing a directory adapter/ does not have, and confirm the current
        adapter/ examples and rules that replaced them are present.

        Checks stale/forbidden literals, required current paths and type names, and
        short identifying phrases -- not full prose sentences or line-wrap positions.
        """
        cpp_style = self._read("ai/context/skse/cpp-style.md")

        for stale_literal in (
            "IPairingNotificationSink",
            "CommonLibPairingNotificationSink",
            "TokenStore::Reservation",
            "SessionManager::Lease",
            "ConnectionSlot::Lease",
            "WebSocketSession",
            "transport/websocket_session.hpp",
            "adapter/shared/enums.hpp",
            "adapter/shared",
            "ai/context/skse/architecture.md",
            "only when they add contract information beyond the signature",
        ):
            self.assertNotIn(stale_literal, cpp_style)

        for required_module in (
            "capture/",
            "dispatch/",
            "identity/",
            "ipc/",
            "papyrus/",
            "plugin/",
            "process/",
            "runtime/",
        ):
            self.assertIn(required_module, cpp_style)
        self.assertIn("commonlib_", cpp_style)
        self.assertIn("IAdapterPairingNotificationSink", cpp_style)
        self.assertIn("CommonLibAdapterPairingNotificationSink", cpp_style)
        self.assertIn("IAdapterTaskMarshaller", cpp_style)
        self.assertIn("CommonLibAdapterTaskMarshaller", cpp_style)
        self.assertIn("runtime/adapter_task_marshaller.hpp", cpp_style)
        self.assertIn(
            "Every enum in `adapter/` is a single project-wide exception", cpp_style
        )
        self.assertIn("Every `adapter/` enum belongs in one project-wide", cpp_style)
        self.assertIn("adapter/enums.hpp", cpp_style)
        self.assertIn("adapter/constants.hpp", cpp_style)
        self.assertIn(
            "one physical file\n  does not require flattening domain namespaces",
            cpp_style,
        )
        # Concept 01.1 completed the physical move: adapter/enums.hpp and
        # adapter/constants.hpp are current fact, not a pending target. Guard against
        # either the old per-module paths or "pending" framing silently coming back.
        for stale_pending_literal in (
            "ipc/ipc_enums.hpp",
            "pending a physical normalization",
            "pending the same physical",
        ):
            self.assertNotIn(stale_pending_literal, cpp_style)
        self.assertIn("Never reorder existing data members", cpp_style)
        self.assertIn("## Member ordering", cpp_style)
        self.assertIn("Data members are the one deliberate exception", cpp_style)
        self.assertNotIn("not consolidated adapter-wide like enums are", cpp_style)
        self.assertIn("Document each parameter with `@param`", cpp_style)
        self.assertIn("with `@return`, normally in one or two lines", cpp_style)

    def test_adapter_enums_and_constants_are_physically_consolidated(self) -> None:
        """Guard Concept 01.1's physical move: the project-wide enums.hpp/constants.hpp
        exist and the per-module headers they replaced are gone, so cpp-style.md's
        documented target can't silently drift back to the pre-01.1 layout.
        """
        for consolidated_header in ("adapter/enums.hpp", "adapter/constants.hpp"):
            self.assertTrue(
                (REPOSITORY_ROOT / consolidated_header).is_file(),
                consolidated_header,
            )

        for retired_header in (
            "adapter/ipc/ipc_enums.hpp",
            "adapter/ipc/ipc_constants.hpp",
            "adapter/capture/adapter_capture_constants.hpp",
            "adapter/process/adapter_host_constants.hpp",
            "adapter/runtime/adapter_runtime_constants.hpp",
        ):
            self.assertFalse(
                (REPOSITORY_ROOT / retired_header).exists(),
                f"{retired_header} was consolidated by Concept 01.1 and must not be "
                "reintroduced",
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
            "actions/upload-artifact": (
                "b7c566a772e6b6bfb58ed0dc250532a479d7789f",
                "v6",
            ),
            "ilammy/msvc-dev-cmd": ("0b201ec74fa43914dc39ae48a89fd1d8cb592756", "v1"),
            "subosito/flutter-action": (
                "1a449444c387b1966244ae4d4f8c696479add0b2",
                "v2",
            ),
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
                    comment.strip(),
                    f"# {expected_version}",
                    f"{workflow_path.name}: {action}",
                )

    def test_workflow_job_level_env_never_uses_the_runner_context(self) -> None:
        """Guard against a job-level env: value referencing the runner context.

        The runner context is not resolved until a runner has picked up the job's steps, so
        referencing it in a job-level env: (as opposed to a step-level one) makes the whole
        workflow file invalid -- GitHub rejects the run before any job executes. This is exactly
        the defect that silently made a now-deleted native-plugin CI workflow and
        integration-ci.yml fail on every push.
        """
        workflow_directory = REPOSITORY_ROOT / ".github" / "workflows"
        for workflow_path in sorted(workflow_directory.glob("*.yml")):
            lines = workflow_path.read_text(encoding="utf-8").splitlines()
            for index, line in enumerate(lines):
                if not line.startswith("    env:"):
                    continue
                end = index + 1
                while end < len(lines) and (
                    not lines[end].strip()
                    or len(lines[end]) - len(lines[end].lstrip()) > 4
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
        prerequisite_checker = self._read("tooling/check-local-prerequisites.ps1")
        required_fragments = (
            ". $toolchainScript",
            "Import-VisualStudioEnvironment",
            '$vcpkgBaseline = "2f1d605400c8727cc00c15797aba796c88ccd523"',
            '"clone", "https://github.com/microsoft/vcpkg.git", $vcpkgRoot',
            '"-C", $vcpkgRoot, "checkout", $vcpkgBaseline',
            '"-disableMetrics"',
            "$env:VCPKG_ROOT = $vcpkgRoot",
            "$env:VCPKG_DEFAULT_BINARY_CACHE = $cacheRoot",
            '$vcpkgExePath = Join-Path $vcpkgRoot "vcpkg.exe"',
            "$currentVcpkgCommit = (& git -C $vcpkgRoot rev-parse HEAD)",
            "$vcpkgAlreadyBootstrapped = $false",
            "& $vcpkgExePath version | Out-Null",
            "$vcpkgAlreadyBootstrapped = ($LASTEXITCODE -eq 0)",
            "if (-not $vcpkgAlreadyBootstrapped) {",
            "$LASTEXITCODE -ne 0",
            'Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "python"',
            '"tooling/format_staged.py", "--check", "--base-ref", "main"',
            'Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "dotnet" -ArgumentList @(',
            '"restore", "host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj"',
            '$hostExecutablePath = Join-Path $repoRoot "host\\DovahLink.Host\\bin\\Release\\net9.0-windows\\DovahLink.Host.exe"',
            "Test-Path -LiteralPath $hostExecutablePath -PathType Leaf",
            'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("pub", "get")',
            'Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("analyze")',
            'Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("test")',
            '$appBuildCache = Join-Path $appDirectory ".dart_tool',
            "if (Test-Path -LiteralPath $appBuildCache) {",
            "Remove-Item -LiteralPath $appBuildCache -Recurse -Force",
            'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "dart" -ArgumentList @("run", "build_runner", "build")',
            'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("analyze")',
            'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("test")',
            'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("build", "windows", "--debug")',
            '"build", "host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj", "--configuration", "Release",',
            '"--no-restore", "--no-incremental"\n)',
            '"test", "host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj", "--configuration", "Release",',
            '"--no-restore", "--no-build"',
            '"-p:GenerateDocumentationFile=true", "-p:TreatWarningsAsErrors=true"',
            '"restore", "tooling/DovahLinkBuilder/DovahLinkBuilder.slnx", "-p:Configuration=Release"',
            '"build", "tooling/DovahLinkBuilder/DovahLinkBuilder.slnx", "--configuration", "Release", "--no-restore"',
            '"test", "tooling/DovahLinkBuilder/DovahLinkBuilder.slnx", "--configuration", "Release",',
            '"publish", "tooling/DovahLinkBuilder/DovahLinkBuilder/DovahLinkBuilder.csproj",',
            '"-p:PublishProfile=FolderProfile", "--no-restore"',
            '$builderExecutablePath = Join-Path $repoRoot "tooling\\out\\DovahLinkBuilder\\DovahLinkBuilder.exe"',
            "Test-Path -LiteralPath $builderExecutablePath -PathType Leaf",
        )
        for fragment in required_fragments:
            self.assertIn(fragment, script)
        for fragment in (
            "Find-VisualStudioToolchain -LocatorPath $vswherePath",
            '"Visual Studio 2022 or 2026 with Desktop development with C++ and MSVC x64/x86"',
        ):
            self.assertIn(fragment, prerequisite_checker)
        self.assertNotIn("X_VCPKG_REGISTRIES_CACHE", script)
        self.assertNotIn("vcpkg-registries-cache", script)

        section_positions = [
            script.index("=== tooling-ci ==="),
            script.index("=== app-ci ==="),
        ]
        self.assertEqual(section_positions, sorted(section_positions))
        command_positions = [
            script.index(
                'Invoke-LocalCommand -WorkingDirectory $repoRoot -FilePath "python"'
            ),
            script.index('"tooling/format_staged.py", "--check", "--base-ref", "main"'),
            script.index('Write-Host "=== host-ci ==="'),
            script.index(
                '"restore", "host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj"'
            ),
            script.index(
                '"build", "host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj", "--configuration", "Release",'
            ),
            script.index("Test-Path -LiteralPath $hostExecutablePath -PathType Leaf"),
            script.index(
                '"test", "host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj", "--configuration", "Release",'
            ),
            script.index(
                '"-p:GenerateDocumentationFile=true", "-p:TreatWarningsAsErrors=true"'
            ),
            script.index('Write-Host "=== builder-ci ==="'),
            script.index(
                '"restore", "tooling/DovahLinkBuilder/DovahLinkBuilder.slnx", "-p:Configuration=Release"'
            ),
            script.index(
                '"build", "tooling/DovahLinkBuilder/DovahLinkBuilder.slnx", "--configuration", "Release", "--no-restore"'
            ),
            script.index(
                '"test", "tooling/DovahLinkBuilder/DovahLinkBuilder.slnx", "--configuration", "Release",'
            ),
            script.index(
                '"publish", "tooling/DovahLinkBuilder/DovahLinkBuilder/DovahLinkBuilder.csproj",'
            ),
            script.index('"-p:PublishProfile=FolderProfile", "--no-restore"'),
            script.index(
                "Test-Path -LiteralPath $builderExecutablePath -PathType Leaf"
            ),
            script.index(
                'Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("pub", "get")'
            ),
            script.index(
                'Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("run", "build_runner", "build")'
            ),
            script.index(
                'Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("analyze")'
            ),
            script.index(
                'Invoke-LocalCommand -WorkingDirectory $sdkDirectory -FilePath "dart" -ArgumentList @("test")'
            ),
            script.index(
                'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("pub", "get")'
            ),
            script.index("Remove-Item -LiteralPath $appBuildCache -Recurse -Force"),
            script.index(
                'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "dart" -ArgumentList @("run", "build_runner", "build")'
            ),
            script.index(
                'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("analyze")'
            ),
            script.index(
                'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("test")'
            ),
            script.index(
                'Invoke-LocalCommand -WorkingDirectory $appDirectory -FilePath "flutter" -ArgumentList @("build", "windows", "--debug")'
            ),
        ]
        self.assertEqual(command_positions, sorted(command_positions))
        self.assertNotIn("choco install", script)
        self.assertIn("All local CI command payloads passed.", script)

    def test_local_ci_resolves_pinned_executables_without_visual_studio_cmake_tools(
        self,
    ) -> None:
        """Require local CI to locate pinned tools dynamically and invoke the selected paths."""
        script = self._read("tooling/run-local-ci.ps1")
        prerequisite_checker = self._read("tooling/check-local-prerequisites.ps1")
        toolchain = self._read("tooling/local-ci-toolchain.ps1")

        for fragment in (
            "$env:DOVAHLINK_VSWHERE_PATH",
            'Get-ExecutablePathsFromPath -Name "vswhere.exe"',
            "Resolve-ExistingExecutablePath",
            "$env:DOVAHLINK_CMAKE_PATH",
            "$env:DOVAHLINK_NINJA_PATH",
            'Get-ExecutablePathsFromPath -Name "cmake.exe"',
            'Get-ExecutablePathsFromPath -Name "ninja.exe"',
            '-ExpectedVersion "cmake version 4.4.2"',
            '-ExpectedVersion "1.13.2"',
        ):
            self.assertIn(fragment, prerequisite_checker)

        for fragment in (
            "-FilePath $cmakePath",
            '"-DCMAKE_MAKE_PROGRAM=$ninjaPath"',
        ):
            self.assertIn(fragment, script)

        self.assertNotIn('-FilePath "cmake"', script)
        self.assertNotIn("Microsoft.VisualStudio.Component.VC.CMake.Project", toolchain)
        self.assertNotIn("CMakeDirectory", toolchain)
        self.assertNotIn("NinjaDirectory", toolchain)

    def test_local_ci_checks_all_prerequisites_before_bootstrapping(self) -> None:
        """Require an actionable prerequisite report before local CI starts changing temp state."""
        script = self._read("tooling/run-local-ci.ps1")
        checker = self._read("tooling/check-local-prerequisites.ps1")
        readme = self._read("README.md")
        guide = self._read("DEVELOPMENT.md")
        local_ci_guide = self._read("ai/context/tooling/local-ci.md")

        check_position = script.index(
            "$prerequisiteReport = Invoke-LocalCiPrerequisiteCheck"
        )
        failure_guard_position = script.index("if (-not $prerequisiteReport.IsReady)")
        failure_throw_position = script.index(
            'throw "Local CI prerequisites are missing. Follow DEVELOPMENT.md and rerun the prerequisite check."'
        )
        import_position = script.index(
            "Import-VisualStudioEnvironment -Toolchain $toolchain"
        )
        cache_position = script.index("$cacheRoot = Join-Path")
        clone_position = script.index(
            '"clone", "https://github.com/microsoft/vcpkg.git", $vcpkgRoot'
        )
        self.assertEqual(
            [
                check_position,
                failure_guard_position,
                failure_throw_position,
                import_position,
                cache_position,
                clone_position,
            ],
            sorted(
                [
                    check_position,
                    failure_guard_position,
                    failure_throw_position,
                    import_position,
                    cache_position,
                    clone_position,
                ]
            ),
        )
        self.assertIn("Get-LocalCiPrerequisiteDefinitions", checker)
        self.assertIn("InstallCommand", checker)
        self.assertIn("VerifyCommand", checker)
        self.assertIn("InstallUrl", checker)
        self.assertIn('VerifyCommand  = "python -m ruff --version"', checker)
        self.assertIn("python -m ruff --version", guide)
        self.assertIn('VerifyCommand  = "clang-format.exe --version', checker)
        self.assertIn("clang-format.exe --version", guide)
        self.assertIn(
            "Visual Studio-bundled version does not satisfy the CI pin", guide
        )
        self.assertIn("DEVELOPMENT.md", readme)
        self.assertIn("check-local-prerequisites.ps1", guide)
        self.assertIn("DEVELOPMENT.md", local_ci_guide)
        for install_url in re.findall(r'InstallUrl\s*=\s*"([^"]+)"', checker):
            self.assertIn(install_url, guide, f"DEVELOPMENT.md omits {install_url}.")

    def test_published_release_and_roadmap_status_agree(self) -> None:
        """Keep the published version and completed roadmap phase synchronized."""
        version = self._read("VERSION").strip()
        phase_zero = self._roadmap_section("0. Documentation baseline")
        phase_zero_five = self._roadmap_section("0.5 Client and Protocol Foundation")
        phase_one = self._roadmap_section("1. Skyrim Bridge Foundation")
        phase_two = self._roadmap_section(
            "2. Bridge Identity and Authoritative State Foundation"
        )
        phase_three = self._roadmap_section("3. Local Device Pairing and Reconnection")
        phase_three_one = self._roadmap_section("3.1 Live Pairing Challenge UX")

        self.assertRegex(version, r"^\d+\.\d+\.\d+$")
        for completed_phase in (
            phase_zero,
            phase_zero_five,
            phase_one,
            phase_two,
            phase_three,
            phase_three_one,
        ):
            self.assertEqual(
                re.findall(r"(?m)^\*\*Status:\*\* .+$", completed_phase),
                ["**Status:** Complete"],
            )

    def test_version_literals_match_the_published_release(self) -> None:
        """Guard every hand-maintained version literal against drift from VERSION."""
        version = self._read("VERSION").strip()

        self.assertEqual(
            json.loads(self._read("adapter/vcpkg.json"))["version-string"], version
        )

        self.assertIn(
            f'public const string PublicProtocolHostVersion = "{version}";',
            self._read("host/DovahLink.Host/Constants.cs"),
        )

        for current_example in (
            "protocol/schema/README.md",
            "protocol/fixtures/connection/hello-ack.json",
            "protocol/fixtures/connection/hello-ack-active-context.json",
            "protocol/fixtures/connection/hello-ack-paired.json",
        ):
            self.assertIn(
                f'"hostVersion": "{version}"',
                self._read(current_example),
                current_example,
            )

    def test_component_changelogs_match_the_published_version(self) -> None:
        """Keep Host/Adapter release history aligned with the packaged version."""
        version = self._read("VERSION").strip()
        changelogs = {
            "app/CHANGELOG.md": self._read("app/CHANGELOG.md"),
            "sdk/CHANGELOG.md": self._read("sdk/CHANGELOG.md"),
            "host/CHANGELOG.md": self._read("host/CHANGELOG.md"),
        }

        for path, changelog in changelogs.items():
            with self.subTest(changelog=path):
                section_headings = re.findall(r"(?m)^## (.+)$", changelog)
                self.assertTrue(section_headings, f"{path} has no ## sections.")
                self.assertEqual(
                    section_headings[0],
                    "[Unreleased]",
                    f"[Unreleased] must be the first section in {path}.",
                )
                entry_versions = re.findall(r"(?m)^## \[(\d+\.\d+\.\d+)\]", changelog)
                self.assertEqual(
                    len(set(entry_versions)),
                    len(entry_versions),
                    f"{path} has duplicate versions.",
                )
                self.assertEqual(
                    entry_versions,
                    sorted(
                        entry_versions,
                        key=lambda entry: tuple(map(int, entry.split("."))),
                        reverse=True,
                    ),
                    f"{path} release sections must be newest first.",
                )

        host_versions = re.findall(
            r"(?m)^## \[(\d+\.\d+\.\d+)\]", changelogs["host/CHANGELOG.md"]
        )
        self.assertTrue(host_versions, "host/CHANGELOG.md has no version entries.")
        self.assertEqual(host_versions[0], version)
        published_versions = set(host_versions)
        published_versions.update(
            re.findall(r"(?m)^## \[(\d+\.\d+\.\d+)\]", self._read("CHANGELOG.md"))
        )
        for path in ("app/CHANGELOG.md", "sdk/CHANGELOG.md"):
            component_versions = re.findall(
                r"(?m)^## \[(\d+\.\d+\.\d+)\]", changelogs[path]
            )
            self.assertTrue(
                set(component_versions).issubset(published_versions),
                f"{path} contains a version with no repository release.",
            )

    def test_component_changelog_ownership_and_historical_archive(self) -> None:
        """Keep component entries local and preserve the combined history verbatim."""
        app_changelog = self._read("app/CHANGELOG.md")
        sdk_changelog = self._read("sdk/CHANGELOG.md")
        host_changelog = self._read("host/CHANGELOG.md")
        archive = self._read("CHANGELOG.md")
        sdk_unreleased = sdk_changelog.split("## [Unreleased]", 1)[1].split("\n## ", 1)[
            0
        ]

        self.assertIn(
            "The app no longer keeps observing a stale connection status", app_changelog
        )
        self.assertIn(
            "rejects Host versions outside its declared `0.4.x` compatibility range",
            sdk_unreleased,
        )
        self.assertIn(
            "rejects Host versions outside its declared `0.4.x` compatibility range",
            sdk_changelog,
        )
        self.assertIn(
            "stamps outgoing envelopes with the resolved `clientId`", sdk_changelog
        )
        for host_outcome in (
            "Standalone C# Host process and thin native Adapter",
            "Host-owned public client boundary",
            "Private, bounded IPC channel between the Adapter and Host",
            "Host-owned state subscriptions with baseline snapshots",
            "validated real Skyrim capture for health, magicka, stamina, XP, and level",
            "Reserved control and data outbound lanes",
            "The native Bridge (`bridge/`) and its CI/tooling wiring",
        ):
            self.assertIn(host_outcome, host_changelog)

        self.assertNotIn("## [Unreleased]", archive)
        self.assertIn(
            "frozen combined project history is preserved through release `0.4.0`",
            archive,
        )
        archive_versions = re.findall(r"(?m)^## \[(\d+\.\d+\.\d+)\]", archive)
        self.assertEqual(
            archive_versions,
            ["0.4.0", "0.3.3", "0.3.2", "0.3.1", "0.3.0", "0.2.0", "0.1.0"],
        )
        for historical_outcome in (
            "## [0.4.0] - 2026-09-23",
            "## [0.3.3] - 2026-08-24",
            "## [0.3.2] - 2026-08-20",
            "## [0.3.1] - 2026-08-19",
            "## [0.3.0] - 2026-08-18",
            "## [0.2.0] - 2026-08-15",
            "## [0.1.0] - 2026-08-12",
            "cached character state from a previous bridge lifetime",
            "survives Skyrim/Bridge/Windows restarts",
            "The native Bridge (`bridge/`) and its CI/tooling wiring",
        ):
            self.assertIn(historical_outcome, archive)

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
            "- an incompatible Host/client version during the compatibility bootstrap\n",
            testing,
        )

        archive = self._read("CHANGELOG.md")
        self.assertIn("revision continuity across a reconnect", archive)

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
            "3A. Host/Adapter Production Migration",
            "4. Live State Synchronization Foundation",
            "5. Dart Client SDK Foundation",
            "5A. Android and Secure Wi-Fi Development Path",
            "6. PC / Second-Screen Baseline",
            "7. Core UI Theme System",
            "8. Live Player State",
            "9. Multi-Client Runtime Foundation",
            "10. Multi-Instance and Local Discovery Foundation",
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
            "29. Runtime Profiling and Advanced Hardening",
            "30. CommonLib Dependency Maintenance Audit",
        ]
        actual_headings = re.findall(r"(?m)^## (\d+(?:\.\d+)?[A-Z]?\.? .+)$", roadmap)

        self.assertEqual(actual_headings, expected_headings)
        self.assertNotIn("## 1.25 ", roadmap)
        self.assertNotIn("## 1.5 ", roadmap)
        self.assertEqual(roadmap.count("**Status:** Next"), 0)
        self.assertEqual(roadmap.count("**Status:** Complete"), 18)
        self.assertEqual(len(re.findall(r"(?m)^\*\*Status:\*\* Planned$", roadmap)), 25)
        self.assertEqual(len(re.findall(r"(?m)^\*\*Status:\*\* Active\.", roadmap)), 1)
        self.assertEqual(
            roadmap.count("**Status:** Planned after read-only product validation"), 1
        )
        root_roadmap = self._read("ROADMAP.md")
        current_position = root_roadmap.split("## Current position", 1)[1].split(
            "## Ordered stages", 1
        )[0]
        self.assertIn(
            "**Current stage:** Stage 5 — Dart Client SDK Foundation is active; Phase 5.2 is the next planned",
            current_position,
        )
        self.assertIn(
            "**Current phase:** Phase 5.2 — SDK State Synchronization API (**Planned**). Phase 5.1 completed",
            current_position,
        )
        ordered_stages = root_roadmap.split("## Ordered stages", 1)[1].split(
            "## Major dependencies", 1
        )[0]
        self.assertIn(
            "| 5 | Active. Phase 5.1 is complete; Phase 5.2 is the next planned target.",
            ordered_stages,
        )
        self.assertIn(
            "Stage 4 — Live State Synchronization Foundation is complete",
            self._normalize_whitespace(current_position),
        )
        self.assertIn("recommends `0.4.0`", current_position)
        self.assertNotIn("Stage 4 remains Active", current_position)
        # Stage 5 is active because Phase 5.1 is complete and Phase 5.2 is next; the status line
        # also records work pulled forward for Phase 3's pairing needs.
        phase_5_status = (
            "**Status:** Active. The package scaffold, protocol/transport layer, and "
            "persistence boundary"
        )
        self.assertEqual(roadmap.count(phase_5_status), 1)
        phase_5_summary = self._normalize_whitespace(
            self._read("roadmap/05-dart-client-sdk-foundation.md").split(
                "### Outcome", 1
            )[0]
        )
        self.assertIn(
            "Phase 5.1 — SDK Typed Protocol and Host Compatibility Boundary is complete.",
            phase_5_summary,
        )
        self.assertIn(
            "State revisions, subscriptions, snapshots, recovery, and completing the app's "
            "SDK integration remain for the rest of Stage 5.",
            phase_5_summary,
        )

        # 3A is now complete; its status line records what completing it means instead of the
        # bare "Complete" every other closed stage uses.
        phase_3a_status = (
            "**Status:** Complete. Host + Adapter are the current production implementation; the "
            "native Bridge (`bridge/`) has been deleted."
        )
        self.assertEqual(roadmap.count(phase_3a_status), 1)

        # Stage 4 is complete, including Phase 4.5's full-range version-impact audit.
        phase_4_status = "**Status:** Complete"

        for heading in expected_headings:
            phase = self._roadmap_section(heading)
            if heading.startswith(
                ("0. ", "0.5 ", "1. ", "2. ", "3. ", "3.1 ", "3.2 ", "3.3 ")
            ):
                expected_statuses = ["**Status:** Complete"]
            elif heading == "4. Live State Synchronization Foundation":
                # Stage 4's span also carries Phase 4.1, Phase 4.5, Host-owned state/publication,
                # and real-capture subsection status lines.
                expected_statuses = [
                    phase_4_status,
                    "**Status:** Complete",
                    "**Status:** Complete",
                    "**Status:** Complete",
                    "**Status:** Complete",
                ]
            elif heading == "3A. Host/Adapter Production Migration":
                # 3A.1, 3A.2, and 3A.3 each carry their own "**Status:** Complete" line now that
                # the whole stage is done.
                expected_statuses = [
                    phase_3a_status,
                    "**Status:** Complete",
                    "**Status:** Complete",
                    "**Status:** Complete",
                ]
            elif heading.startswith("5. "):
                expected_statuses = [phase_5_status, "**Status:** Complete"]
            elif heading.startswith("28. "):
                expected_statuses = [
                    "**Status:** Planned after read-only product validation"
                ]
            else:
                expected_statuses = ["**Status:** Planned"]
            self.assertEqual(
                re.findall(r"(?m)^\*\*Status:\*\* .+$", phase),
                expected_statuses,
                heading,
            )

        integration_readme = self._read("integration/README.md")
        for supporting_document in (
            roadmap,
            integration_readme,
        ):
            self.assertNotIn("Phase 1.25", supporting_document)
            self.assertNotIn("Phase 1.5", supporting_document)

        ordering = {
            heading: roadmap.index(f"## {heading}") for heading in expected_headings
        }
        self.assertLess(
            ordering["4. Live State Synchronization Foundation"],
            ordering["5. Dart Client SDK Foundation"],
        )
        self.assertLess(
            ordering["5. Dart Client SDK Foundation"],
            ordering["5A. Android and Secure Wi-Fi Development Path"],
        )
        self.assertLess(
            ordering["5A. Android and Secure Wi-Fi Development Path"],
            ordering["6. PC / Second-Screen Baseline"],
        )
        self.assertLess(
            ordering["6. PC / Second-Screen Baseline"],
            ordering["7. Core UI Theme System"],
        )
        self.assertLess(
            ordering["7. Core UI Theme System"], ordering["8. Live Player State"]
        )
        self.assertLess(
            ordering["8. Live Player State"],
            ordering["9. Multi-Client Runtime Foundation"],
        )
        self.assertLess(
            ordering["9. Multi-Client Runtime Foundation"],
            ordering["13. Interactive Map Foundation"],
        )
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
            "10. Multi-Instance and Local Discovery Foundation": "depends on Phases 2 and 9",
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
        self.assertIn(
            "Validate the machinery without adding gameplay mutation", authorization
        )
        self.assertIn("Each action needs its own product decision", authorization)
        self.assertIn("no Skyrim\nmutation is exposed", authorization)
        self.assertIn("Individual companion actions", deferred)
        for deferred_action in (
            "equipment",
            "favorites or hotkeys",
            "map markers",
            "fast travel",
        ):
            self.assertIn(deferred_action, deferred)

    def test_pairing_phase_establishes_persistent_per_user_trust(self) -> None:
        """Guard the persistent-trust pairing redesign and its retired restart-bound predecessor."""
        pairing = self._roadmap_section("3. Local Device Pairing and Reconnection")
        normalized_pairing = self._normalize_whitespace(pairing)

        for required_phrase in (
            "Permit only one active pairing challenge globally",
            "final confirmation is idempotent",
            "Persist completed trust so it survives Skyrim, Bridge, and Windows restarts, save "
            "changes, `playContextId` changes, and `stateAuthorityId` changes",
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

        identity_model = self._markdown_section(
            "ARCHITECTURE.md", "Runtime and identity model"
        )
        normalized_identity_model = self._normalize_whitespace(identity_model)
        self.assertIn(
            "Persistent device trust is a separate concept layered on top of these four "
            "*private* lifetimes, not a fifth private one that replaces or reinterprets them",
            normalized_identity_model,
        )
        self.assertIn(
            "a trusted client still authenticates into a fresh `sessionId` on every reconnect, "
            "and a native-plugin restart still created a new restart-scoped identifier",
            normalized_identity_model,
        )
        self.assertIn(
            "belonged to the Windows user profile running the client and the retired "
            "native plugin, and survived the plugin, Skyrim, and Windows restarts",
            normalized_identity_model,
        )
        self.assertIn(
            "`ai/context/protocol/security.md` and `roadmap/03-local-device-pairing-and-reconnection.md`'s Phase 3 owns the pairing, "
            "storage, and revocation design",
            normalized_identity_model,
        )

    def test_security_doc_establishes_persistent_trust_and_liveness_ownership(
        self,
    ) -> None:
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
            "scoped to the Windows user profile running the client and the Host",
            "Do not invent cryptography",
            "the official client must not share one `clientId`/credential between different "
            "Windows user profiles",
            "A `shortId` is never authentication or authorization material",
            "An explicit local reset-all-trust operation exists",
            "Revocation is immediate: revoking a trusted client removes its active trust, "
            "invalidates its current authenticated session, closes that connection, and rejects "
            "reuse of the revoked credential",
            "the Host must not claim pairing is available when the in-game confirmation cannot "
            "actually be presented",
            "A client that fails before saving the credential creates no durable trust and may "
            "pair again once the Host's pending challenge expires",
            "A client that saves the credential but crashes before confirming retries "
            "confirmation on restart",
            "If the Host restarted while the credential was only pending, it reports the "
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
            "rapid restart, timeout, and Host restart all recover cleanly under this policy",
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
        papyrus = self._read("console-admin/DovahLinkAdmin.psc")
        yaml = self._read("console-admin/dovahlink.yaml")

        canonical_commands = (
            "dovahlink list",
            "dovahlink list trusted",
            "dovahlink list blocked",
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
        self.assertIn(
            "Entering bare `dovahlink` displays ConsoleUtil Extended's compact command index.",
            console_readme,
        )
        self.assertIn(
            "`dovahlink help` for the full trust-administration descriptions.",
            console_readme,
        )

        for retired_command in (
            "dovahlink devices",
            "dovahlink blocklist",
            "dovahlink reset trust",
            "dovahlink reset -confirm <code>",
        ):
            self.assertNotIn(retired_command, console_readme)
            self.assertNotIn(retired_command, security)

        self.assertIn("String Function List(String akScope) global native", papyrus)
        self.assertIn("String Function Help() global native", papyrus)
        self.assertNotIn("Function Devices", papyrus)
        self.assertNotIn("Function Blocked", papyrus)

        self.assertIn(
            "the native functions DovahLink's Adapter registers via SKSE's Papyrus interface",
            papyrus,
        )
        self.assertIn(
            "adapter/papyrus/commonlib_adapter_trust_admin_papyrus_adapter.cpp", papyrus
        )
        self.assertIn("implemented natively by the Adapter plugin", papyrus)
        self.assertIn(
            "ConsoleUtil Extended command definition for DovahLink's trust-administration console",
            yaml,
        )
        self.assertIn("installed alongside DovahLink.\n# Place this file", yaml)

        command_entries = re.findall(
            r"(?ms)^  - name: ([A-Za-z-]+)$\n(.*?)(?=^  - name: |\Z)", yaml
        )
        raw_command_names = re.findall(r"(?m)^  - name: (.+)$", yaml)
        self.assertEqual(
            [name for name, _ in command_entries],
            [
                "list",
                "help",
                "revoke",
                "block",
                "unblock",
                "forget",
                "reset-trust",
                "reset",
                "confirm-reset",
            ],
        )
        self.assertEqual([name for name, _ in command_entries], raw_command_names)
        command_blocks = dict(command_entries)
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
            self.assertEqual(
                len(re.findall(r"(?m)^    func: .+$", command_blocks[command])), 1
            )
            self.assertIn(f"func: {function}", command_blocks[command])
        for command in ("revoke", "block", "unblock", "forget"):
            self.assertEqual(command_blocks[command].count("      - name: -id"), 1)
            self.assertIn("        required: true", command_blocks[command])
        self.assertNotIn("      - name:", command_blocks["reset"])
        self.assertEqual(
            command_blocks["confirm-reset"].count("      - name: -confirm"), 1
        )
        self.assertIn("        required: true", command_blocks["confirm-reset"])
        self.assertIn("name: scope", yaml)
        self.assertIn("required: false", yaml)
        self.assertIn("default: known", yaml)
        self.assertIn('help: "known, trusted, or blocked."', yaml)
        self.assertNotIn("default: all", yaml)
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

    def test_architecture_establishes_sdk_boundary_and_replaces_client_non_goal(
        self,
    ) -> None:
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
            "The official Flutter app is the SDK's first production consumer",
            architecture,
        )
        self.assertIn(
            "See `ai/context/sdk/` for SDK-specific conventions", architecture
        )
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

    def test_sdk_readme_documents_the_phase_5_pull_forward_and_the_real_package(
        self,
    ) -> None:
        """Guard sdk/README.md's content and the real, partially-implemented Dart package."""
        sdk_readme = self._read("sdk/README.md")

        for required_phrase in (
            "This directory owns the reusable, supported client SDK implementations for the "
            "DovahLink protocol.",
            "consumers do not need to implement transport, Host-version compatibility "
            "detection,\nauthentication, pairing recovery, reconnect, session and "
            "authoritative-state identity, revisions,\nsubscriptions, snapshots, recovery, or "
            "reusable client persistence themselves.",
            "Skyrim\n   |\nDovahLink Host / Adapter\n   |\nprotocol/\n   |\nDart Client SDK\n   |\n"
            "Official Flutter app",
            "`protocol/` remains the sole canonical language-neutral Host/client contract",
            "the SDK implements\nthat contract for Dart consumers and is not a second protocol "
            "authority",
            "the SDK's first production consumer, not a privileged one — see",
            "Partially implemented, pulled forward from `roadmap/05-dart-client-sdk-foundation.md`'s Phase 5 (\"Dart Client "
            "SDK Foundation\")\nahead of that phase's formal start, because Phase 3 (Local "
            "Device Pairing and Reconnection), documented in `roadmap/03-local-device-pairing-and-reconnection.md`, needed\nthe SDK's persistence boundary to avoid a "
            "larger later migration.",
            "sdk/\n  dart/\n    dovahlink_client/",
            "It currently provides the connect/hello/pairing/disconnect protocol client and bounded automatic\nreconnection after ordinary transport loss",
            "The official\nFlutter app depends on it (`dovahlink_client_sdk` in `app/pubspec.yaml`) and already uses its public\nclient for pairing and authentication through `PairingRemoteDataSource`.",
            "The SDK supports Host releases in the `0.4.x` range and rejects older or newer Host "
            "versions during\n`hello`, before admitting a session.",
            "It still has no public state synchronization API: Stage 5 owns\nthe SDK's typed state models, revisions, subscriptions, snapshot/recovery lifecycle",
            "The app's `features/connection/` code currently handles Host\nselection and navigation",
        ):
            self.assertIn(required_phrase, sdk_readme)

        # Phase 5 was pulled forward: the real package now exists, replacing the old
        # "no implementation skeleton yet" invariant this test used to guard.
        real_package = REPOSITORY_ROOT / "sdk" / "dart" / "dovahlink_client"
        self.assertTrue(real_package.is_dir())
        self.assertTrue((real_package / "pubspec.yaml").is_file())
        self.assertTrue((real_package / "lib").is_dir())

    def test_sdk_public_api_hides_transport_types(self) -> None:
        """Guard the SDK's curated public surface from exposing transport wiring."""
        public_api = self._read("sdk/dart/dovahlink_client/lib/dovahlink_client.dart")
        client_source = self._read(
            "sdk/dart/dovahlink_client/lib/src/dovahlink_client.dart"
        )
        public_constructor = client_source.split("DovahLinkClient({", 1)[1].split(
            "factory DovahLinkClient.windows()", 1
        )[0]

        self.assertNotIn("IDovahLinkTransport", public_api)
        self.assertNotIn("transport/websocket_transport.dart", public_api)
        self.assertNotIn("buildDovahLinkClientForTesting", public_api)
        self.assertNotIn("IDovahLinkTransport", public_constructor)

        sdk_root = REPOSITORY_ROOT / "sdk" / "dart" / "dovahlink_client"
        transport_import = (
            "package:dovahlink_client_sdk/src/transport/websocket_transport.dart"
        )
        for package_area in (sdk_root / "lib", sdk_root / "test"):
            for source_path in package_area.rglob("*.dart"):
                source = source_path.read_text(encoding="utf-8")
                if (
                    "IDovahLinkTransport" in source
                    and source_path.name != "websocket_transport.dart"
                ):
                    self.assertIn(transport_import, source, str(source_path))

    def test_shared_dart_documentation_conventions_are_linked_by_each_dart_area(
        self,
    ) -> None:
        """Guard the shared Dartdoc rule and the SDK/Flutter pointers to it."""
        dart_style = self._read("ai/context/dart/dart-style.md")
        flutter_dart_style = self._read("ai/context/flutter/dart-style.md")
        sdk_api_design = self._read("ai/context/sdk/api-design.md")

        for required_phrase in (
            "Shared Dart-language conventions that apply to every Dart package in this "
            "repository",
            "Do not use `dynamic` or `any`-style escape hatches to avoid modelling a type.",
            "Use the null assertion operator (`!`) only when an immediately visible check or "
            "constructor",
            "Link Dart declarations with unadorned Dartdoc references such as `[Type]` and "
            "`[Type.member]`.",
            "Do not wrap symbol names in backticks or quotes, or add Markdown "
            "emphasis around links.",
            "Import the declaring library even when a Dartdoc link is its only reference",
            "Missing implementation uses `// TODO: ...` immediately above the declaration.",
            "Use UpperCamelCase for classes, enums, typedefs, extensions, and type parameters.",
            "Use lowercase_with_underscores for packages, directories, source files, and import "
            "prefixes.",
            "Use `dart format`, trailing commas, braces for flow control, and single quotes.",
            "Never prefix methods with `get`; use a getter or a descriptive verb.",
        ):
            self.assertIn(required_phrase, dart_style)

        normalized_dart_style = self._normalize_whitespace(dart_style)
        self.assertIn(
            "For unchanged overrides, use a concise link to the inherited member instead of "
            "repeating its contract",
            normalized_dart_style,
        )
        self.assertIn(
            "do not pad API comments with an `Implements ... per architecture "
            "document` statement",
            normalized_dart_style,
        )

        self.assertIn(
            "Shared Dart-language conventions (type safety, naming case, formatting, async, "
            "dartdoc mechanics)\nlive in [`ai/context/dart/dart-style.md`](../dart/dart-style.md)",
            flutter_dart_style,
        )
        self.assertIn(
            "follow `ai/context/dart/dart-style.md`'s baseline naming rules",
            flutter_dart_style,
        )
        self.assertIn(
            "Dartdoc symbol-link and\nbrevity rules in [`ai/context/dart/dart-style.md`]",
            flutter_dart_style,
        )
        self.assertIn(
            "for\nDartdoc symbol links and concise inherited-contract references.",
            sdk_api_design,
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

    def test_convention_updates_have_one_owner_and_no_planning_document_references(
        self,
    ) -> None:
        """Keep the approved lifecycle, file, and source-comment convention updates aligned."""
        architecture = self._read("ARCHITECTURE.md")
        host_architecture = self._read("ai/context/host/architecture.md")
        dotnet_style = self._read("ai/context/dotnet/csharp-style.md")
        common = self._read("ai/context/common.md")
        dart_style = self._read("ai/context/dart/dart-style.md")
        flutter_architecture = self._read("ai/context/flutter/architecture.md")

        self.assertIn(
            "plugin unload/reload is not a supported lifecycle boundary", architecture
        )
        self.assertIn(
            "**Adapter restart** means a Skyrim process restart; live SKSE plugin unload/reload is not a\n"
            "  supported lifecycle boundary",
            host_architecture,
        )
        self.assertNotIn(
            "an SKSE plugin reload or a Skyrim process restart", host_architecture
        )
        self.assertIn("host process and its tests", dotnet_style)
        self.assertIn("One Flutter-specific grouping exception is a feature's", common)
        self.assertNotIn("The Flutter-specific exception is", common)
        self.assertIn("`<feature>.actions.dart` file", common)
        self.assertIn("exception in `ai/context/common.md`", flutter_architecture)
        self.assertNotIn("sole Flutter-specific", flutter_architecture)
        self.assertIn("one private, widget-local `_<WidgetName>ViewModel`", dart_style)

        for source_path, widget_name in (
            (
                "app/lib/features/pairing/presentation/widgets/pairing_renotify_button.widget.dart",
                "PairingRenotifyButton",
            ),
            (
                "app/lib/features/pairing/presentation/widgets/pairing_cancel_button.widget.dart",
                "PairingCancelButton",
            ),
        ):
            source = self._read(source_path)
            self.assertIn(f"class _{widget_name}ViewModel", source)
            self.assertIn(
                f"/// Widget-local presentation values consumed by [{widget_name}]",
                source,
            )

        for source_path in ("adapter/tests/plugin/dovahlink_adapter_plugin_test.cpp",):
            source = self._read(source_path)
            self.assertNotIn("roadmap/", source)
            self.assertNotIn("plans/", source)
            self.assertNotIn("ROADMAP.md", source)

    def test_sdk_conventions_cover_architecture_api_persistence_and_testing(
        self,
    ) -> None:
        """Guard the four ai/context/sdk/ convention files and their non-duplication of security/compatibility."""
        sdk_architecture = self._read("ai/context/sdk/architecture.md")
        sdk_api_design = self._read("ai/context/sdk/api-design.md")
        sdk_persistence = self._read("ai/context/sdk/persistence.md")
        sdk_testing = self._read("ai/context/sdk/testing.md")

        for required_phrase in (
            "`protocol/` remains the sole canonical language-neutral Host/client contract.",
            "The Host remains authoritative for live Skyrim game state, authoritative "
            "revisions, the current\n`playContextId`, server-side trusted-client records, "
            "revocation, trust administration, Host\ncapabilities, and server-side security "
            "decisions.",
            "The SDK has one underlying client engine/state machine.",
            "The reusable client core must not depend on Flutter widgets, Redux, `GetIt`, "
            "navigation",
            "it must never become\nauthoritative over Skyrim or server-side trust.",
            "It must not construct a new parallel raw WebSocket implementation,\nHost "
            "compatibility implementation, protocol decoder, authentication implementation, "
            "pairing\nimplementation, reconnect state machine, revision tracker, or subscription "
            "engine.",
        ):
            self.assertIn(required_phrase, sdk_architecture)

        for required_phrase in (
            "The long-term simple\nexperience trends toward: find/select a DovahLink instance, "
            "pair if necessary, listen to typed state.",
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
        ):
            self.assertIn(required_phrase, sdk_testing)

        # These conventions must point to the compatibility/security authorities, not restate
        # their content — this is the same rule common.md applies to every shared contract.
        for sdk_doc in (sdk_architecture, sdk_api_design, sdk_persistence, sdk_testing):
            self.assertNotIn("never use floating branches such as `@main`", sdk_doc)

        # api-design.md's compatibility vocabulary describes the Host, the current server-side actor.
        for compatibility_vocabulary_phrase in (
            "Host compatibility mechanics",
            "connected\nHost version, SDK/Host compatibility result",
            "incompatible Host\nversion",
            '"Host is older than supported" from "Host is\nnewer than supported"',
        ):
            self.assertIn(compatibility_vocabulary_phrase, sdk_api_design)

    def test_sdk_and_integration_docs_describe_host_as_the_current_actor(self) -> None:
        """Guard the remaining SDK/integration convention docs against describing a retired
        component, rather than the Host or the Adapter, as the current server-side actor."""
        dart_style = self._read("ai/context/dart/dart-style.md")
        sdk_persistence = self._read("ai/context/sdk/persistence.md")
        sdk_testing = self._read("ai/context/sdk/testing.md")
        integration_testing = self._read("ai/context/integration/testing.md")

        self.assertIn(
            "A compatible Host/SDK pair must never rely on raw wire\n  strings for branching.",
            dart_style,
        )

        self.assertIn("reusable known-Host information", sdk_persistence)
        self.assertIn("preferred Host selection", sdk_persistence)
        self.assertIn("must be re-established from the Host after", sdk_persistence)

        self.assertIn("not the Host harness -- is required only when", sdk_testing)
        self.assertIn("do not depend on the Host\nharness", sdk_testing)

        self.assertIn(
            "Integration tests prove that the Host and Flutter client agree on the canonical "
            "protocol.",
            integration_testing,
        )
        self.assertNotIn("the Adapter and Flutter client agree", integration_testing)
        self.assertIn("shared\n  Host/SDK fixtures.", integration_testing)
        self.assertIn(
            "client- or host-only fixtures must not redefine them.",
            integration_testing,
        )
        self.assertIn(
            "A single-session Host process (today's capacity-one session-registry boundary)",
            integration_testing,
        )
        self.assertNotIn("kMaxConnectedClients", integration_testing)

    def test_agents_and_common_point_at_the_new_dart_and_sdk_convention_areas(
        self,
    ) -> None:
        """Guard the AGENTS.md/common.md pointers added for ai/context/dart/ and ai/context/sdk/."""
        agents = self._read("AGENTS.md")
        common = self._read("ai/context/common.md")

        self.assertIn(
            "- `ai/context/dart/` — shared Dart-language conventions; read for any Dart work, "
            "Flutter client or SDK",
            agents,
        )
        self.assertIn(
            "- `ai/context/sdk/` — Dart Client SDK conventions; read for SDK work",
            agents,
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

    def test_app_and_protocol_docs_reconcile_the_sdk_boundary(self) -> None:
        """Guard the SDK-transition notes added to app/ and protocol/ READMEs."""
        app_readme = self._read("app/README.md")
        protocol_readme = self._read("protocol/README.md")

        self.assertIn("## SDK integration", app_readme)
        self.assertIn(
            "The pairing feature already uses [`sdk/dart/dovahlink_client/`](../sdk/README.md)'s public API",
            app_readme,
        )
        self.assertIn(
            "`features/connection/` area currently owns Host selection and navigation",
            app_readme,
        )
        self.assertIn(
            "Phase 5.1 delivered the SDK's Host-version compatibility checks",
            app_readme,
        )
        self.assertIn(
            "Flutter\nconventions point to [`ai/context/sdk/`](../ai/context/sdk/) for "
            "SDK-owned protocol behavior rather\nthan duplicating it in the app.",
            app_readme,
        )

        self.assertIn(
            "The repository-root [`app/`](../app/), [`sdk/`](../sdk/), [`host/`](../host/), and "
            "[`adapter/`](../adapter/)\n  areas contain adapters, not competing protocol "
            "definitions.",
            protocol_readme,
        )

    def test_protocol_docs_describe_host_not_the_retired_native_plugin(self) -> None:
        """Guard protocol/README.md and ai/context/protocol/conventions.md against describing
        the retired native SKSE plugin, rather than the Host, as the protocol's server-side endpoint."""
        protocol_readme = self._read("protocol/README.md")
        conventions = self._read("ai/context/protocol/conventions.md")

        self.assertIn(
            "This directory owns the contract between the DovahLink Host and DovahLink clients.",
            protocol_readme,
        )
        self.assertIn(
            "The protocol is the canonical contract joining the DovahLink Host and Flutter "
            "client.",
            conventions,
        )
        self.assertIn("a client asks the Host to do something", conventions)
        self.assertIn(
            "Do not use plausible defaults when the Host does not know a value",
            conventions,
        )
        security = self._read("ai/context/protocol/security.md")
        self.assertIn(
            "the approved, narrow exception to `ai/context/skse/architecture.md`'s Papyrus rule\n"
            "    (originally written for the retired native plugin, now the Adapter's own boundary)",
            security,
        )

        # Stage 5 is active/pulled-forward, not a frozen historical record, so its ownership-boundary
        # list must name the areas that actually exist today.
        sdk_foundation = self._read("roadmap/05-dart-client-sdk-foundation.md")
        self.assertIn(
            "alongside\n  `app/`, `host/`, `adapter/`, `protocol/`, and `integration/`",
            sdk_foundation,
        )
        self.assertNotIn("`app/`, `bridge/`, `protocol/`", sdk_foundation)

    def test_live_state_phase_depends_on_reconnect_and_defines_session_loss(
        self,
    ) -> None:
        """Preserve reconnect ordering and the bounded, session-scoped reliable-event contract."""
        live_state = self._roadmap_section("4. Live State Synchronization Foundation")
        normalized_live_state = self._normalize_whitespace(live_state)

        self.assertIn("depends on Phases 2 and 3", live_state)
        self.assertNotRegex(live_state, r"(?i)\bdeltas?\b")
        self.assertIn("complete post-change state rather than a patch", live_state)
        for required_phrase in (
            "Reliable-event delivery is scoped to one authenticated session",
            "explicitly disconnected",
            "does not replay the previous session's queued events",
        ):
            self.assertIn(required_phrase, live_state)
        self.assertIn(
            "a client that cannot consume them in time is explicitly disconnected",
            normalized_live_state,
        )

    def test_state_identity_and_snapshot_exceptions_are_explicit(self) -> None:
        """Keep native-plugin-lifetime identity and snapshot delivery exceptions in the roadmap."""
        identity = self._roadmap_section(
            "2. Bridge Identity and Authoritative State Foundation"
        )
        live_state = self._roadmap_section("4. Live State Synchronization Foundation")
        normalized_identity = self._normalize_whitespace(identity)
        normalized_live_state = self._normalize_whitespace(live_state)

        self.assertIn(
            "authoritative state identity as one `stateAuthorityId`, `playContextId`, and state area",
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

    def test_no_deleted_migration_doc_references(self) -> None:
        """Guard against reintroducing deleted migration docs."""
        deleted_paths = (
            "host/PLAN.md",
            "ai/context/host/migration-audit.md",
            "plans/stage-3-thin-native-adapter-private-ipc",
            "plans/stage-3a.1-host-adapter-production-cutover",
            "plans/stage-4-host-client-boundary-and-pairing",
        )
        for relative_path in deleted_paths:
            self.assertFalse(
                (REPOSITORY_ROOT / relative_path).exists(),
                f"{relative_path} was deleted by 3A.3 and must not be reintroduced",
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
