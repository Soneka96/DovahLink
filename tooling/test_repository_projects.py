"""Tests for repository_projects.py and the repository workflow's project discovery."""

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path

from repository_projects import REPOSITORY_ROOT, owned_projects

# The repository workflow that restores every DovahLink-owned project before its checks.
REPOSITORY_WORKFLOW = REPOSITORY_ROOT / ".github" / "workflows" / "tooling-ci.yml"


def _git(repository_root: Path, *arguments: str) -> None:
    """Runs one Git command in `repository_root`, failing the test on a non-zero exit."""
    subprocess.run(
        ["git", *arguments],
        cwd=repository_root,
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )


def _touch(repository_root: Path, relative_path: str) -> None:
    """Creates an empty file at `relative_path`, including its parent directories."""
    path = repository_root / relative_path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.touch()


class OwnedProjectsTests(unittest.TestCase):
    """Tests for owned_projects."""

    def test_projects_under_generated_output_are_never_discovered(self) -> None:
        """Verifies acquired sources and build output under `out/**` are ignored, tracked or not."""
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            _git(root, "init", "-q")
            (root / ".gitignore").write_text(
                "**/bin/\n**/obj/\n/out/\ntooling/out/\n", encoding="utf-8"
            )
            _touch(root, "host/DovahLink.Host/DovahLink.Host.csproj")
            _touch(
                root,
                "tooling/DovahLinkBuilder/DovahLinkBuilder/DovahLinkBuilder.csproj",
            )
            # The pinned sas-pairing checkout, exactly where the acquirer places it.
            _touch(
                root,
                "out/sas-pairing/source/dotnet/tests/SasPairing.PackageSmoke/SasPairing.PackageSmoke.csproj",
            )
            _touch(
                root, "out/sas-pairing/source/dotnet/src/SasPairing/SasPairing.csproj"
            )
            _touch(root, "tooling/out/Staged/Staged.csproj")
            _touch(root, "host/DovahLink.Host/obj/Generated.csproj")
            _git(root, "add", "--all")
            # Even a project wrongly force-added under the generated root is not DovahLink-owned.
            _touch(root, "out/forced/Forced.csproj")
            _git(root, "add", "--force", "out/forced/Forced.csproj")

            projects = owned_projects(root)

        self.assertEqual(
            projects,
            [
                "host/DovahLink.Host/DovahLink.Host.csproj",
                "tooling/DovahLinkBuilder/DovahLinkBuilder/DovahLinkBuilder.csproj",
            ],
        )

    def test_repository_discovers_only_owned_host_and_tooling_projects(self) -> None:
        """Verifies the real checkout lists its own Host projects and nothing generated."""
        projects = owned_projects(REPOSITORY_ROOT)

        self.assertIn("host/DovahLink.Host/DovahLink.Host.csproj", projects)
        self.assertIn("host/DovahLink.Host.Tests/DovahLink.Host.Tests.csproj", projects)
        for project in projects:
            self.assertFalse(project.startswith("out/"), project)
            self.assertNotIn("SasPairing", project)


class RepositoryWorkflowDiscoveryTests(unittest.TestCase):
    """Tests that the repository workflow restores owned projects only."""

    def test_workflow_restores_projects_from_owned_discovery(self) -> None:
        """Verifies the restore loop uses owned_projects instead of a filesystem-wide walk."""
        workflow = REPOSITORY_WORKFLOW.read_text(encoding="utf-8")

        self.assertIn(
            "done < <(python tooling/repository_projects.py -z)",
            workflow,
        )
        self.assertNotIn("find . -name '*.csproj'", workflow)
        self.assertLess(
            workflow.index("python tooling/sas_pairing_dependency.py acquire"),
            workflow.index("python tooling/repository_projects.py -z"),
        )


if __name__ == "__main__":
    unittest.main()
