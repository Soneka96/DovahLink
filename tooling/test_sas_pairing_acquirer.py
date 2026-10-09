"""Tests for sas_pairing_acquirer.py."""

from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path

from sas_pairing_acquirer import SasPairingAcquirer
from sas_pairing_pin import SasPairingPin

# The pinned commit the fake checkout reports unless a test changes it.
PINNED_COMMIT = "b938016d1de53d3e269fe0840485fbd3dc715fd7"


def _build_pin(commit: str = PINNED_COMMIT) -> SasPairingPin:
    """Builds a representative pin."""
    return SasPairingPin(
        repository="https://github.com/Soneka96/sas-pairing.git",
        commit=commit,
        package_id="SasPairing",
        package_version="0.1.0-dev.1",
        target_framework="net10.0",
        native_abi_version=1,
        platform="windows-x64",
        rust_target="x86_64-pc-windows-msvc",
        rust_toolchain="1.99.0",
    )


class FakeCommandRunner:
    """Records commands and imitates the file effects of git, dotnet, cargo, rustc, and the verifiers."""

    def __init__(
        self,
        heads: tuple[str, ...] = (PINNED_COMMIT,),
        rust_host: str = "x86_64-pc-windows-msvc",
        extra_pack_file: bool = False,
    ) -> None:
        """Configures what each `git rev-parse` reports (the last value repeats), the Rust host, and the pack."""
        self.invocations: list[tuple[list[str], Path]] = []
        self.heads = list(heads)
        self.rust_host = rust_host
        self.extra_pack_file = extra_pack_file
        # A verifier script name and subcommand that should fail, as `(script, subcommand)`.
        self.failing_step: tuple[str, str] | None = None

    def run(self, args: list[str], cwd: Path) -> str:
        """Records the command and produces the files or output the real tool would."""
        self.invocations.append((args, cwd))
        if (
            self.failing_step is not None
            and len(args) > 2
            and args[1].endswith(self.failing_step[0])
            and args[2] == self.failing_step[1]
        ):
            raise subprocess.CalledProcessError(1, args)
        if args[:2] == ["git", "rev-parse"]:
            return (self.heads.pop(0) if len(self.heads) > 1 else self.heads[0]) + "\n"
        if args[:2] == ["dotnet", "pack"]:
            output = Path(args[args.index("-o") + 1])
            output.mkdir(parents=True, exist_ok=True)
            with zipfile.ZipFile(
                output / "SasPairing.0.1.0-dev.1.nupkg", "w"
            ) as package:
                package.writestr("lib/net10.0/SasPairing.dll", b"managed-assembly")
            if self.extra_pack_file:
                (output / "SasPairing.0.1.0-dev.1.snupkg").write_bytes(b"symbols")
        elif args[:1] == ["rustc"]:
            return f"rustc 1.99.0\nhost: {self.rust_host}\nrelease: 1.99.0\n"
        elif args[:1] == ["cargo"]:
            built = cwd / "core" / "target" / "release" / "sas_pairing_core.dll"
            built.parent.mkdir(parents=True, exist_ok=True)
            built.write_bytes(b"native-library")
        elif (
            len(args) > 2
            and args[1].endswith("package_dotnet_nuget.py")
            and args[2] == "stage"
        ):
            output = Path(args[args.index("--output") + 1])
            output.mkdir(parents=True)
            shutil.copy2(
                args[args.index("--nupkg") + 1], output / "SasPairing.0.1.0-dev.1.nupkg"
            )
        elif (
            len(args) > 2
            and args[1].endswith("package_dotnet_native.py")
            and args[2] == "stage"
        ):
            output = Path(args[args.index("--output") + 1])
            output.mkdir(parents=True)
            shutil.copy2(
                args[args.index("--library") + 1], output / "sas_pairing_core.dll"
            )
        return ""

    def commands(self) -> list[list[str]]:
        """Returns the recorded command lines."""
        return [args for args, _ in self.invocations]


class AcquireTests(unittest.TestCase):
    """Tests for SasPairingAcquirer.acquire."""

    def setUp(self) -> None:
        """Creates an isolated repository root for each test."""
        self._temp = tempfile.TemporaryDirectory()
        self.root = Path(self._temp.name)

    def tearDown(self) -> None:
        """Removes the isolated repository root."""
        self._temp.cleanup()

    def _acquire(
        self,
        runner: FakeCommandRunner,
        native: bool = True,
        pin: SasPairingPin | None = None,
    ) -> Path:
        """Runs one acquisition with the fake runner."""
        return SasPairingAcquirer(
            self.root, pin or _build_pin(), runner, "python-under-test"
        ).acquire(native=native)

    def test_fetches_exactly_the_pinned_commit_into_ignored_output(self) -> None:
        """Verifies a fresh checkout fetches only the pinned commit, detached, under out/sas-pairing/source."""
        runner = FakeCommandRunner()

        self._acquire(runner)

        source = self.root / "out" / "sas-pairing" / "source"
        self.assertIn(
            (
                [
                    "git",
                    "fetch",
                    "--quiet",
                    "--depth",
                    "1",
                    "https://github.com/Soneka96/sas-pairing.git",
                    PINNED_COMMIT,
                ],
                source,
            ),
            runner.invocations,
        )
        self.assertIn(
            [
                "git",
                "-c",
                "advice.detachedHead=false",
                "checkout",
                "--quiet",
                "--detach",
                "FETCH_HEAD",
            ],
            runner.commands(),
        )
        for args in runner.commands():
            self.assertNotIn("latest", args)
            self.assertNotIn("main", args)

    def test_fetched_head_other_than_pin_fails_closed(self) -> None:
        """Verifies a checkout that is not the pinned commit stops before anything is built."""
        runner = FakeCommandRunner(heads=("0" * 40,))

        with self.assertRaises(ValueError):
            self._acquire(runner)

        self.assertFalse(any(args[:1] == ["dotnet"] for args in runner.commands()))

    def test_package_is_built_with_the_pinned_commit_and_verified_by_sas_pairing_tooling(
        self,
    ) -> None:
        """Verifies the build and pack carry the pinned commit and the bundle is staged and verified against the built assembly."""
        runner = FakeCommandRunner()

        self._acquire(runner)

        commands = runner.commands()
        commit_property = f"-p:RepositoryCommit={PINNED_COMMIT}"
        self.assertTrue(
            any(
                args[:2] == ["dotnet", "build"] and commit_property in args
                for args in commands
            )
        )
        self.assertTrue(
            any(
                args[:2] == ["dotnet", "pack"]
                and commit_property in args
                and "--no-build" in args
                for args in commands
            )
        )
        verifier_runs = [
            args
            for args in commands
            if len(args) > 2 and args[1].endswith("package_dotnet_nuget.py")
        ]
        self.assertEqual([args[2] for args in verifier_runs], ["stage", "verify"])
        for args in verifier_runs:
            self.assertEqual(args[0], "python-under-test")
            self.assertEqual(args[args.index("--git-sha") + 1], PINNED_COMMIT)
            self.assertTrue(
                args[args.index("--assembly") + 1].endswith(
                    "bin\\Release\\net10.0\\SasPairing.dll"
                )
                or args[args.index("--assembly") + 1].endswith(
                    "bin/Release/net10.0/SasPairing.dll"
                )
            )

    def test_unexpected_pack_output_fails_closed(self) -> None:
        """Verifies any pack output other than exactly the pinned package file is rejected."""
        with self.assertRaises(ValueError):
            self._acquire(FakeCommandRunner(extra_pack_file=True))

    def test_feed_holds_only_the_verified_package_and_stale_restore_copy_is_removed(
        self,
    ) -> None:
        """Verifies the local feed is replaced by the verified package and the old extracted package is dropped."""
        output = self.root / "out" / "sas-pairing"
        (output / "feed").mkdir(parents=True)
        (output / "feed" / "SasPairing.0.0.1.nupkg").write_bytes(b"old")
        stale = output / "packages" / "saspairing" / "0.1.0-dev.1"
        stale.mkdir(parents=True)
        (stale / "marker").write_bytes(b"stale")

        self._acquire(FakeCommandRunner())

        self.assertEqual(
            sorted(path.name for path in (output / "feed").iterdir()),
            ["SasPairing.0.1.0-dev.1.nupkg"],
        )
        self.assertFalse(stale.exists())

    def test_native_build_uses_pinned_toolchain_locked_and_is_audited(self) -> None:
        """Verifies the native library is built with the pinned Rust release, --locked, then staged, verified, and export-audited."""
        runner = FakeCommandRunner()

        self._acquire(runner)

        commands = runner.commands()
        self.assertIn(["rustc", "+1.99.0", "-vV"], commands)
        cargo = next(args for args in commands if args[:1] == ["cargo"])
        self.assertEqual(cargo[1], "+1.99.0")
        self.assertIn("--locked", cargo)
        self.assertIn("native-abi", cargo)
        native_steps = [
            args[2]
            for args in commands
            if len(args) > 2 and args[1].endswith("package_dotnet_native.py")
        ]
        self.assertEqual(native_steps, ["stage", "verify"])
        audit = next(
            args
            for args in commands
            if len(args) > 1 and args[1].endswith("check_abi_exports.py")
        )
        self.assertEqual(audit[audit.index("--expect") + 1], "manifest")

    def test_wrong_rust_host_target_fails_closed(self) -> None:
        """Verifies a toolchain that does not build the pinned Windows x64 target stops before cargo runs."""
        runner = FakeCommandRunner(rust_host="aarch64-pc-windows-msvc")

        with self.assertRaises(ValueError):
            self._acquire(runner)

        self.assertFalse(any(args[:1] == ["cargo"] for args in runner.commands()))

    def test_record_pins_commit_and_artifact_hashes(self) -> None:
        """Verifies the record names the pinned commit and the exact package, assembly, and native hashes."""
        record_path = self._acquire(FakeCommandRunner())

        record = json.loads(record_path.read_text(encoding="utf-8"))
        output = self.root / "out" / "sas-pairing"
        self.assertEqual(record["commit"], PINNED_COMMIT)
        self.assertEqual(record["package_version"], "0.1.0-dev.1")
        self.assertEqual(
            record["assembly_sha256"], hashlib.sha256(b"managed-assembly").hexdigest()
        )
        self.assertEqual(
            record["nupkg_sha256"],
            hashlib.sha256(
                (output / "nuget-bundle" / "SasPairing.0.1.0-dev.1.nupkg").read_bytes()
            ).hexdigest(),
        )
        self.assertEqual(record["native_library"], "native-bundle/sas_pairing_core.dll")
        self.assertEqual(
            record["native_library_sha256"],
            hashlib.sha256(b"native-library").hexdigest(),
        )

    def test_managed_only_acquisition_records_no_native_library(self) -> None:
        """Verifies an acquisition without --native builds no native library and leaves none behind."""
        stale_native = self.root / "out" / "sas-pairing" / "native-bundle"
        stale_native.mkdir(parents=True)
        runner = FakeCommandRunner()

        record = json.loads(
            self._acquire(runner, native=False).read_text(encoding="utf-8")
        )

        self.assertIsNone(record["native_library"])
        self.assertIsNone(record["native_library_sha256"])
        self.assertFalse(stale_native.exists())
        self.assertFalse(
            any(args[:1] in (["cargo"], ["rustc"]) for args in runner.commands())
        )

    def test_clean_checkout_of_pinned_commit_is_reused(self) -> None:
        """Verifies a clean existing checkout of the pinned commit is not fetched again."""
        source = self.root / "out" / "sas-pairing" / "source"
        (source / ".git").mkdir(parents=True)
        runner = FakeCommandRunner()

        self._acquire(runner)

        self.assertFalse(
            any(args[:2] == ["git", "fetch"] for args in runner.commands())
        )

    def test_checkout_of_other_commit_is_replaced(self) -> None:
        """Verifies an existing checkout of another commit is deleted and fetched fresh."""
        source = self.root / "out" / "sas-pairing" / "source"
        (source / ".git").mkdir(parents=True)
        (source / "leftover").write_text("old", encoding="utf-8")
        runner = FakeCommandRunner(heads=("1" * 40, PINNED_COMMIT))

        self._acquire(runner)

        self.assertFalse((source / "leftover").exists())
        self.assertTrue(any(args[:2] == ["git", "fetch"] for args in runner.commands()))

    def test_failing_verifier_stops_without_a_record(self) -> None:
        """Verifies a failed package or native verification stops acquisition and writes no acquisition record."""
        for step in (
            ("package_dotnet_nuget.py", "verify"),
            ("package_dotnet_native.py", "verify"),
        ):
            with self.subTest(step=step):
                record = self.root / "out" / "sas-pairing" / "acquisition.json"
                if record.exists():
                    record.unlink()
                runner = FakeCommandRunner()
                runner.failing_step = step

                with self.assertRaises(subprocess.CalledProcessError):
                    self._acquire(runner)

                self.assertFalse(record.exists())

    def test_never_deletes_outside_its_output_directory(self) -> None:
        """Verifies the acquirer refuses to delete a path outside out/sas-pairing."""
        acquirer = SasPairingAcquirer(
            self.root, _build_pin(), FakeCommandRunner(), "python-under-test"
        )
        outside = self.root / "keep-me"
        outside.mkdir()

        with self.assertRaises(ValueError):
            acquirer._remove(outside)

        self.assertTrue(outside.exists())


if __name__ == "__main__":
    unittest.main()
