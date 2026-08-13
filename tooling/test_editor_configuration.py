"""Validate the repository's shared VS Code and CMake editor configuration."""

import json
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent


class EditorConfigurationTests(unittest.TestCase):
    """Verify that editor configuration stays aligned with project tooling."""

    def test_workspace_settings_use_portable_cmake_and_exclude_generated_trees(self) -> None:
        """Require portable CMake settings and exclusions for generated directories."""
        settings = self._load_json(".vscode/settings.json")

        self.assertEqual(settings["cmake.sourceDirectory"], "${workspaceFolder}/bridge")
        self.assertEqual(settings["cmake.useCMakePresets"], "always")
        self.assertTrue(settings["cmake.exportCompileCommandsFile"])

        excluded_paths = {
            "**/bridge/build/**",
            "**/bridge/vcpkg_installed/**",
            "**/app/build/**",
            "**/app/.dart_tool/**",
            "**/app/**/ephemeral/**",
        }
        for setting_name in ("files.exclude", "search.exclude", "files.watcherExclude"):
            self.assertEqual(
                set(settings[setting_name]),
                excluded_paths,
                msg=f"unexpected exclusions in {setting_name}",
            )
            self.assertTrue(all(value is True for value in settings[setting_name].values()))

        self.assertEqual(
            set(settings["dart.analysisExcludedFolders"]),
            {
                "${workspaceFolder}/app/build",
                "${workspaceFolder}/app/.dart_tool",
                "${workspaceFolder}/app/linux/flutter/ephemeral",
                "${workspaceFolder}/app/windows/flutter/ephemeral",
                "${workspaceFolder}/app/macos/Flutter/ephemeral",
            },
        )

    def test_cpp_configuration_uses_cmake_without_recursive_workspace_include(self) -> None:
        """Require CMake-provided C++ configuration and bounded symbol browsing."""
        properties = self._load_json(".vscode/c_cpp_properties.json")
        self.assertEqual(len(properties["configurations"]), 1)
        for configuration in properties["configurations"]:
            self.assertEqual(configuration["configurationProvider"], "ms-vscode.cmake-tools")
            self.assertNotIn("includePath", configuration)
            self.assertTrue(configuration["browse"]["limitSymbolsToIncludedHeaders"])
            self.assertEqual(
                configuration["compileCommands"],
                [
                    "${workspaceFolder}/bridge/build/windows-x64-debug/compile_commands.json",
                    "${workspaceFolder}/bridge/build/windows-x64-release/compile_commands.json",
                ],
            )

    def test_cmake_exports_compile_commands_for_all_presets(self) -> None:
        """Require direct CMake and shared presets to produce compile databases."""
        cmake_lists = (REPOSITORY_ROOT / "bridge" / "CMakeLists.txt").read_text(encoding="utf-8")
        self.assertIn("set(CMAKE_EXPORT_COMPILE_COMMANDS ON)", cmake_lists)

        presets = self._load_json("bridge/CMakePresets.json")
        base_preset = next(
            preset for preset in presets["configurePresets"] if preset["name"] == "windows-x64"
        )
        self.assertTrue(base_preset["cacheVariables"]["CMAKE_EXPORT_COMPILE_COMMANDS"])
        for preset_name in ("windows-x64-debug", "windows-x64-release"):
            preset = next(
                preset for preset in presets["configurePresets"] if preset["name"] == preset_name
            )
            self.assertEqual(preset["inherits"], "windows-x64")

    def test_workspace_recommends_language_servers_for_repository_languages(self) -> None:
        """Recommend the installed or applicable language tooling for repository sources."""
        extensions = self._load_json(".vscode/extensions.json")
        recommendations = set(extensions["recommendations"])

        self.assertTrue(
            {
                "ms-vscode.cpptools",
                "ms-vscode.cmake-tools",
                "ms-dotnettools.csdevkit",
                "dart-code.dart-code",
                "dart-code.flutter",
                "ms-python.python",
                "ms-python.vscode-pylance",
                "ms-vscode.powershell",
                "redhat.vscode-yaml",
            }.issubset(recommendations)
        )

    @staticmethod
    def _load_json(relative_path: str) -> dict:
        """Load a repository JSON configuration file."""
        path = REPOSITORY_ROOT / relative_path
        with path.open(encoding="utf-8") as file:
            return json.load(file)


if __name__ == "__main__":
    unittest.main()
