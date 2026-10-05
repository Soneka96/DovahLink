"""Builds the pinned `sas-pairing` .NET package and Windows native library from source.

Everything is produced under the repository's ignored `out/sas-pairing/` directory from one exact,
pinned commit, using that commit's own packaging verifiers: no binary is committed, downloaded, or
taken from another local checkout, and no floating version or branch is ever resolved.
"""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import stat
import zipfile
from pathlib import Path

from sas_pairing_command_runner import ICommandRunner
from sas_pairing_pin import SasPairingPin

# ---- Output layout (relative to out/sas-pairing) ----

# The detached checkout of the pinned commit.
SOURCE_DIR = "source"
# The raw `dotnet pack` output.
PACK_DIR = "pack"
# The managed bundle staged and verified by sas-pairing's own NuGet tooling.
NUGET_BUNDLE_DIR = "nuget-bundle"
# The local package feed `host/nuget.config` maps the SasPairing package to.
FEED_DIR = "feed"
# The restore folder of the projects that reference SasPairing.
PACKAGES_DIR = "packages"
# The native bundle staged and verified by sas-pairing's own native tooling.
NATIVE_BUNDLE_DIR = "native-bundle"
# The record of what this acquisition produced, read by the Host tests.
RECORD_FILE = "acquisition.json"

# ---- Record ----

# The schema version of the acquisition record.
RECORD_SCHEMA_VERSION = 1
# The native library file name inside the staged native bundle.
NATIVE_LIBRARY_NAME = "sas_pairing_core.dll"


class SasPairingAcquirer:
    """Fetches one pinned `sas-pairing` commit and builds, verifies, and stages its artifacts."""

    def __init__(
        self,
        repository_root: Path,
        pin: SasPairingPin,
        runner: ICommandRunner,
        python_executable: str,
    ) -> None:
        """Creates an acquirer for one repository and pin.

        Args:
            repository_root: The DovahLink repository root.
            pin: The validated dependency pin.
            runner: Runs git, dotnet, cargo, rustc, and the sas-pairing verifiers.
            python_executable: The Python interpreter that runs the sas-pairing verifiers.
        """
        self._pin = pin
        self._runner = runner
        self._python = python_executable
        self._output = repository_root / "out" / "sas-pairing"

    @property
    def output_directory(self) -> Path:
        """The ignored directory every acquisition artifact is written under."""
        return self._output

    def acquire(self, native: bool) -> Path:
        """Fetches the pinned commit and builds its package and, optionally, its native library.

        Args:
            native: Whether to also build and verify the Windows x64 native library.

        Returns:
            The written acquisition record.

        Raises:
            subprocess.CalledProcessError: A fetch, build, or verifier command failed.
            ValueError: The checkout, package, or native build does not match the pin.
        """
        source = self._ensure_source()
        nupkg = self._build_package(source)
        self._publish_to_feed(nupkg)
        native_library = self._build_native(source) if native else None
        if not native:
            self._remove(self._output / NATIVE_BUNDLE_DIR)
        return self._write_record(nupkg, native_library)

    def _ensure_source(self) -> Path:
        """Reuses a clean checkout of exactly the pinned commit, or replaces it with a fresh one."""
        source = self._output / SOURCE_DIR
        if (
            (source / ".git").is_dir()
            and self._head(source) == self._pin.commit
            and not self._runner.run(
                ["git", "status", "--porcelain", "--untracked-files=no"], source
            ).strip()
        ):
            return source

        self._remove(source)
        source.mkdir(parents=True)
        self._runner.run(["git", "init", "--quiet"], source)
        self._runner.run(
            [
                "git",
                "fetch",
                "--quiet",
                "--depth",
                "1",
                self._pin.repository,
                self._pin.commit,
            ],
            source,
        )
        self._runner.run(
            [
                "git",
                "-c",
                "advice.detachedHead=false",
                "checkout",
                "--quiet",
                "--detach",
                "FETCH_HEAD",
            ],
            source,
        )
        if self._head(source) != self._pin.commit:
            raise ValueError(
                "The fetched sas-pairing checkout is not the pinned commit."
            )
        return source

    def _head(self, source: Path) -> str:
        """Returns the checkout's current commit."""
        return self._runner.run(["git", "rev-parse", "HEAD"], source).strip()

    def _build_package(self, source: Path) -> Path:
        """Builds and packs the managed package, then stages and verifies it with sas-pairing's tool."""
        dotnet_dir = source / "dotnet"
        project = "src/SasPairing/SasPairing.csproj"
        commit_property = f"-p:RepositoryCommit={self._pin.commit}"
        pack_dir = self._output / PACK_DIR
        bundle_dir = self._output / NUGET_BUNDLE_DIR
        self._remove(pack_dir)
        self._remove(bundle_dir)
        self._runner.run(
            ["dotnet", "build", project, "-c", "Release", commit_property], dotnet_dir
        )
        self._runner.run(
            [
                "dotnet",
                "pack",
                project,
                "-c",
                "Release",
                "--no-build",
                "--no-restore",
                commit_property,
                "-o",
                str(pack_dir),
            ],
            dotnet_dir,
        )
        packed = (
            sorted(path.name for path in pack_dir.iterdir())
            if pack_dir.is_dir()
            else []
        )
        if packed != [self._pin.nupkg_file_name]:
            raise ValueError(
                f"Expected exactly {self._pin.nupkg_file_name}, but the pack produced {packed}."
            )

        nupkg = pack_dir / self._pin.nupkg_file_name
        built_assembly = (
            dotnet_dir
            / "src"
            / "SasPairing"
            / "bin"
            / "Release"
            / self._pin.target_framework
            / "SasPairing.dll"
        )
        verifier = str(source / "tooling" / "package_dotnet_nuget.py")
        identity = ["--git-sha", self._pin.commit, "--assembly", str(built_assembly)]
        self._runner.run(
            [
                self._python,
                verifier,
                "stage",
                "--nupkg",
                str(nupkg),
                "--output",
                str(bundle_dir),
                *identity,
            ],
            source,
        )
        self._runner.run(
            [self._python, verifier, "verify", "--bundle", str(bundle_dir), *identity],
            source,
        )
        return bundle_dir / self._pin.nupkg_file_name

    def _publish_to_feed(self, nupkg: Path) -> None:
        """Makes the verified package the only one in the local feed and drops its stale restore copy."""
        feed = self._output / FEED_DIR
        self._remove(feed)
        feed.mkdir(parents=True)
        shutil.copy2(nupkg, feed / nupkg.name)
        self._remove(
            self._output
            / PACKAGES_DIR
            / self._pin.package_id.lower()
            / self._pin.package_version
        )

    def _build_native(self, source: Path) -> Path:
        """Builds the native library with the pinned Rust release, then stages, verifies, and audits it."""
        toolchain = f"+{self._pin.rust_toolchain}"
        host = self._runner.run(["rustc", toolchain, "-vV"], source)
        targets = [
            line.removeprefix("host: ").strip()
            for line in host.splitlines()
            if line.startswith("host: ")
        ]
        if targets != [self._pin.rust_target]:
            raise ValueError(
                f"The pinned Rust toolchain targets {targets}, not {self._pin.rust_target}."
            )

        self._runner.run(
            [
                "cargo",
                toolchain,
                "build",
                "--manifest-path",
                "core/Cargo.toml",
                "--release",
                "--features",
                "native-abi",
                "--locked",
            ],
            source,
        )
        built = source / "core" / "target" / "release" / NATIVE_LIBRARY_NAME
        bundle_dir = self._output / NATIVE_BUNDLE_DIR
        self._remove(bundle_dir)
        native_verifier = str(source / "tooling" / "package_dotnet_native.py")
        self._runner.run(
            [
                self._python,
                native_verifier,
                "stage",
                "--library",
                str(built),
                "--output",
                str(bundle_dir),
                "--git-sha",
                self._pin.commit,
                "--rust-target",
                self._pin.rust_target,
            ],
            source,
        )
        self._runner.run(
            [
                self._python,
                native_verifier,
                "verify",
                "--bundle",
                str(bundle_dir),
                "--git-sha",
                self._pin.commit,
                "--source-library",
                str(built),
            ],
            source,
        )
        staged = bundle_dir / NATIVE_LIBRARY_NAME
        self._runner.run(
            [
                self._python,
                str(source / "tooling" / "check_abi_exports.py"),
                "--library",
                str(staged),
                "--expect",
                "manifest",
            ],
            source,
        )
        return staged

    def _write_record(self, nupkg: Path, native_library: Path | None) -> Path:
        """Writes the acquisition record the Host tests use to find and check the artifacts."""
        with zipfile.ZipFile(nupkg) as package:
            assembly = package.read(f"lib/{self._pin.target_framework}/SasPairing.dll")
        record = {
            "schema_version": RECORD_SCHEMA_VERSION,
            "repository": self._pin.repository,
            "commit": self._pin.commit,
            "package_id": self._pin.package_id,
            "package_version": self._pin.package_version,
            "nupkg_sha256": _sha256(nupkg.read_bytes()),
            "assembly_sha256": _sha256(assembly),
            "native_abi_version": self._pin.native_abi_version,
            "platform": self._pin.platform,
            "native_library": None
            if native_library is None
            else native_library.relative_to(self._output).as_posix(),
            "native_library_sha256": None
            if native_library is None
            else _sha256(native_library.read_bytes()),
        }
        path = self._output / RECORD_FILE
        path.write_text(
            json.dumps(record, indent=2, sort_keys=True) + "\n", encoding="utf-8"
        )
        return path

    def _remove(self, path: Path) -> None:
        """Deletes a file or directory, but only inside this acquirer's own output directory.

        Raises:
            ValueError: `path` is outside `out/sas-pairing`.
        """
        if not path.resolve().is_relative_to(self._output.resolve()):
            raise ValueError(f"Refusing to delete {path} outside {self._output}.")
        if path.is_dir():
            shutil.rmtree(path, onexc=_clear_read_only_and_retry)
        elif path.exists():
            path.unlink()


def _clear_read_only_and_retry(function, path: str, _exception: BaseException) -> None:
    """Clears the read-only attribute Git gives its object files on Windows, then retries the deletion."""
    os.chmod(path, stat.S_IWRITE)
    function(path)


def _sha256(data: bytes) -> str:
    """Returns the lowercase hex SHA-256 of `data`."""
    return hashlib.sha256(data).hexdigest()
