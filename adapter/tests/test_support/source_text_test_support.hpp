#pragma once

#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>

#include <catch2/catch_test_macros.hpp>

namespace dovahlink::adapter::test_support {

///  Reads a source file's own text for structural, text-scan-only test
///  assertions -- never for compiling or linking it. Mirrors
///  `bridge/test_support/source_text_test_support.hpp`'s role for the
///  adapter's own structural tests.
inline std::string ReadSource(const std::filesystem::path &path) {
  std::ifstream file(path);
  REQUIRE(file.is_open());
  std::ostringstream contents;
  contents << file.rdbuf();
  return contents.str();
}

} //  namespace dovahlink::adapter::test_support
