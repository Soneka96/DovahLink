"""Tests for build_output_ownership.py."""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path
from unittest import mock

from build_output_ownership import MARKER_FILE_NAME, BuildOutputOwnershipGuard


class EnsureOwnedTests(unittest.TestCase):
    """Tests for BuildOutputOwnershipGuard.ensure_owned against real filesystem state."""

    def test_trusts_the_default_output_root_without_a_marker(self) -> None:
        """Trusts the repository's own default output root without a marker, even with unrelated content already there."""
        with tempfile.TemporaryDirectory() as temp_dir:
            repository_root = Path(temp_dir)
            output_root = repository_root / "tooling" / "out"
            output_root.mkdir(parents=True)
            (output_root / "leftover-from-before-this-feature.txt").write_text(
                "pre-existing"
            )
            guard = BuildOutputOwnershipGuard()

            guard.ensure_owned(output_root, repository_root)

            self.assertFalse((output_root / MARKER_FILE_NAME).is_file())

    def test_trusts_a_profile_subfolder_of_the_default_output_root(self) -> None:
        """Trusts a profile-specific subfolder of the default output root (for example tooling/out/debug) the same as the root itself."""
        with tempfile.TemporaryDirectory() as temp_dir:
            repository_root = Path(temp_dir)
            output_root = repository_root / "tooling" / "out" / "debug"
            guard = BuildOutputOwnershipGuard()

            guard.ensure_owned(output_root, repository_root)

            self.assertFalse((output_root / MARKER_FILE_NAME).is_file())

    def test_adopts_a_nonexistent_custom_output_root(self) -> None:
        """Adopts a nonexistent custom output root: creates it and marks it Builder-owned."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out"
            guard = BuildOutputOwnershipGuard()

            guard.ensure_owned(output_root, Path(temp_dir) / "repo")

            self.assertTrue(output_root.is_dir())
            marker_path = output_root / MARKER_FILE_NAME
            self.assertTrue(marker_path.is_file())
            self.assertTrue(marker_path.read_text(encoding="utf-8"))

    def test_adopts_an_empty_custom_output_root(self) -> None:
        """Adopts an existing but empty custom output root, marking it Builder-owned."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out"
            output_root.mkdir()
            guard = BuildOutputOwnershipGuard()

            guard.ensure_owned(output_root, Path(temp_dir) / "repo")

            marker_path = output_root / MARKER_FILE_NAME
            self.assertTrue(marker_path.is_file())
            self.assertTrue(marker_path.read_text(encoding="utf-8"))

    def test_accepts_an_already_marked_custom_output_root(self) -> None:
        """Treats a custom output root that already carries the ownership marker as already owned, without rewriting it."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out"
            output_root.mkdir()
            marker_path = output_root / MARKER_FILE_NAME
            marker_path.write_text("already owned", encoding="utf-8")
            (output_root / "package").write_text("a previous build's real output")
            guard = BuildOutputOwnershipGuard()

            guard.ensure_owned(output_root, Path(temp_dir) / "repo")

            self.assertEqual("already owned", marker_path.read_text(encoding="utf-8"))
            self.assertTrue((output_root / "package").is_file())

    def test_refuses_a_non_empty_unmarked_custom_output_root(self) -> None:
        """Refuses an existing, non-empty custom output root with no ownership marker, leaving its content untouched."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out"
            output_root.mkdir()
            unrelated_file = output_root / "package"
            unrelated_content = b"unrelated user data, not DovahLink's"
            unrelated_file.write_bytes(unrelated_content)
            guard = BuildOutputOwnershipGuard()

            with self.assertRaises(RuntimeError) as context:
                guard.ensure_owned(output_root, Path(temp_dir) / "repo")

            self.assertIn(str(output_root), str(context.exception))
            self.assertFalse((output_root / MARKER_FILE_NAME).is_file())
            self.assertEqual(unrelated_content, unrelated_file.read_bytes())

    def test_refuses_a_custom_output_root_containing_only_a_subfolder(self) -> None:
        """Refuses a non-empty unmarked custom output root even when its only content is a nested subfolder rather than a file."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out"
            unrelated_subfolder = output_root / "package"
            unrelated_subfolder.mkdir(parents=True)
            (unrelated_subfolder / "real-file.txt").write_text("unrelated user data")
            guard = BuildOutputOwnershipGuard()

            with self.assertRaises(RuntimeError):
                guard.ensure_owned(output_root, Path(temp_dir) / "repo")

            self.assertTrue((unrelated_subfolder / "real-file.txt").is_file())

    def test_is_idempotent_across_repeated_calls_against_an_adopted_root(self) -> None:
        """A second ensure_owned call against the same adopted custom root succeeds again without error, matching a restart reusing the same configured output folder."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out"
            repository_root = Path(temp_dir) / "repo"
            guard = BuildOutputOwnershipGuard()
            guard.ensure_owned(output_root, repository_root)
            (output_root / "package").write_text("this run's real output")

            guard.ensure_owned(output_root, repository_root)

            self.assertTrue((output_root / "package").is_file())

    def test_normalizes_an_os_error_while_creating_a_custom_output_root(self) -> None:
        """Normalizes an OSError raised while adopting a new custom output root into RuntimeError.

        Triggered with a mocked `Path.mkdir` failure rather than a platform-specific invalid path
        (for example a colon outside the drive designator, which only Windows rejects): Repository
        CI runs this suite on Linux, where such a path is perfectly legal, so a real invalid-path
        trigger would not exercise this branch there at all.
        """
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out"
            guard = BuildOutputOwnershipGuard()

            with mock.patch.object(
                Path, "mkdir", side_effect=OSError("simulated mkdir failure")
            ):
                with self.assertRaises(RuntimeError) as context:
                    guard.ensure_owned(output_root, Path(temp_dir) / "repo")

            self.assertIsInstance(context.exception.__cause__, OSError)

    def test_normalizes_a_value_error_while_creating_a_malformed_custom_output_root(
        self,
    ) -> None:
        """Normalizes a ValueError raised while adopting a malformed custom output root -- here an embedded null character, which mkdir rejects before touching the OS -- into RuntimeError."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out\0bad"
            guard = BuildOutputOwnershipGuard()

            with self.assertRaises(RuntimeError) as context:
                guard.ensure_owned(output_root, Path(temp_dir) / "repo")

            self.assertIsInstance(context.exception.__cause__, ValueError)


class MarkOwnedTests(unittest.TestCase):
    """Tests for BuildOutputOwnershipGuard.mark_owned against real filesystem state."""

    def test_marks_a_nonexistent_root(self) -> None:
        """Creates a nonexistent root and marks it Builder-owned."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out"
            guard = BuildOutputOwnershipGuard()

            guard.mark_owned(output_root)

            self.assertTrue(output_root.is_dir())
            marker_path = output_root / MARKER_FILE_NAME
            self.assertTrue(marker_path.is_file())
            self.assertTrue(marker_path.read_text(encoding="utf-8"))

    def test_marks_a_root_that_already_holds_real_unmarked_content(self) -> None:
        """Marks a root even though it already holds real, unmarked content -- the exact state a caller's own prior destructive rebuild leaves the root in."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out"
            output_root.mkdir()
            (output_root / "real-output.txt").write_text("this run's real output")
            guard = BuildOutputOwnershipGuard()

            guard.mark_owned(output_root)

            self.assertTrue((output_root / MARKER_FILE_NAME).is_file())
            self.assertTrue((output_root / "real-output.txt").is_file())

    def test_is_idempotent_across_repeated_calls(self) -> None:
        """A second mark_owned call against the same root succeeds again without error."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out"
            guard = BuildOutputOwnershipGuard()
            guard.mark_owned(output_root)

            guard.mark_owned(output_root)

            self.assertTrue((output_root / MARKER_FILE_NAME).is_file())

    def test_normalizes_a_value_error_while_marking_a_malformed_root(self) -> None:
        """Normalizes a ValueError raised while marking a malformed root -- here an embedded null character, which mkdir rejects before touching the OS -- into RuntimeError."""
        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "custom-out\0bad"
            guard = BuildOutputOwnershipGuard()

            with self.assertRaises(RuntimeError) as context:
                guard.mark_owned(output_root)

            self.assertIsInstance(context.exception.__cause__, ValueError)


if __name__ == "__main__":
    unittest.main()
