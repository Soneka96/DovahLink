# Development setup

This guide covers the tools needed to run the Windows local CI checks in
[`tooling/run-local-ci.ps1`](tooling/run-local-ci.ps1). It does not describe player runtime
requirements.

Run the prerequisite checker first. It checks installed versions and Visual Studio components,
reports every missing requirement in one pass, and prints the install command, verification
command, and official setup link for each one. It never installs software or changes your PATH.

```powershell
.\tooling\check-local-prerequisites.ps1
```

| Requirement | Install or configure | Verify |
| --- | --- | --- |
| Visual Studio 2022 or 2026, Desktop development with C++, MSVC x64/x86 | [Install C++ support](https://learn.microsoft.com/cpp/build/vscpp-step-0-installation); use Visual Studio Installer → Modify → Workloads. | The checker queries `vswhere` for the supported Visual Studio version, workload, and MSVC component. |
| Git for Windows | [Install Git](https://git-scm.com/install/windows). The local CI script uses it to bootstrap the pinned vcpkg checkout. | `git --version` |
| CMake 4.4.2 | [Official CMake 4.4.2 release](https://github.com/Kitware/CMake/releases/tag/v4.4.2). Add `cmake.exe` to PATH or set `DOVAHLINK_CMAKE_PATH` to its full path. | `cmake --version` must report 4.4.2. |
| Ninja 1.13.2 | [Official Ninja 1.13.2 release](https://github.com/ninja-build/ninja/releases/tag/v1.13.2). Add `ninja.exe` to PATH or set `DOVAHLINK_NINJA_PATH` to its full path. | `ninja --version` must report 1.13.2. |
| Python 3.13.x | [Python for Windows](https://www.python.org/downloads/windows/). | `python --version` |
| .NET 9 SDK | [Download the .NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0). The runtime alone cannot build or test the Host. | `dotnet --list-sdks` must include a 9.x SDK. |
| Flutter stable and its bundled Dart SDK | [Install Flutter](https://docs.flutter.dev/install), then add Flutter's `bin` directory to PATH. Do not install Dart separately. | `flutter --version --machine` and `dart --version` |
| clang-format 19.1.5 | [Official LLVM 19.1.5 release](https://github.com/llvm/llvm-project/releases/tag/llvmorg-19.1.5); add the directory containing `clang-format.exe` to PATH. | `clang-format --version` must report 19.1.5. |
| Ruff | [Installation guide](https://docs.astral.sh/ruff/installation/); run `python -m pip install ruff`. | `ruff --version` |
| PSScriptAnalyzer | [Overview and installation](https://learn.microsoft.com/en-us/powershell/utility-modules/psscriptanalyzer/overview); run `pwsh -NoProfile -Command "Install-Module PSScriptAnalyzer -Scope CurrentUser -Force"`. | `pwsh -NoProfile -Command "Get-Command Invoke-Formatter"` |

The checker also verifies that `dotnet format` is available through the .NET SDK. vcpkg is cloned,
checked out at the repository's pinned commit, and bootstrapped by `run-local-ci.ps1`; it does not
need a separate installation.

After the checker reports all prerequisites available, run the complete local CI sequence from the
repository root:

```powershell
.\tooling\run-local-ci.ps1
```

The script restores dependencies and runs the repository, Host, Builder, Flutter/Dart, and native
Adapter checks. The first run can take longer while vcpkg builds its pinned dependencies.
