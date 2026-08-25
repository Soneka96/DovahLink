#ifndef RUNNER_UTILS_H_
#define RUNNER_UTILS_H_

#include <string>
#include <vector>

/// Creates a console and redirects runner and Flutter output streams to it.
void CreateAndAttachConsole();

/// Converts a null-terminated UTF-16 string to UTF-8, or returns empty on
/// failure.
std::string Utf8FromUtf16(const wchar_t *utf16_string);

/// Returns UTF-8 command-line arguments, or an empty vector on failure.
std::vector<std::string> GetCommandLineArguments();

#endif // RUNNER_UTILS_H_
