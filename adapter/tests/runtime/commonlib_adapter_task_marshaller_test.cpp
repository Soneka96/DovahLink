#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <filesystem>
#include <string>

using dovahlink::adapter::test_support::ReadSource;

TEST_CASE("no adapter/runtime header includes a Skyrim or SKSE runtime "
          "header",
          "[runtime][structural]") {
  //  Structural pin, not a functional assertion: IAdapterTaskMarshaller and
  //  CommonLibAdapterTaskMarshaller's own declaration must stay CommonLib-
  //  free so consumers can depend on the port without SKSE, per
  //  ai/context/adapter/architecture.md's "Technology boundary". Only the
  //  implementation's .cpp is allowed to reach CommonLib.
  std::filesystem::path runtimeDir{DOVAHLINK_ADAPTER_RUNTIME_DIR};
  REQUIRE(std::filesystem::exists(runtimeDir));

  int headerCount = 0;
  for (const auto &entry : std::filesystem::directory_iterator(runtimeDir)) {
    if (entry.path().extension() != ".hpp") {
      continue;
    }
    ++headerCount;

    std::string text = ReadSource(entry.path());
    INFO("checking " << entry.path().filename().string());
    CHECK(text.find("RE/") == std::string::npos);
    CHECK(text.find("SKSE/") == std::string::npos);
  }

  CHECK(headerCount > 0);
}

TEST_CASE("CommonLibAdapterTaskMarshaller includes SKSE/SKSE.h first and "
          "forwards to SKSE::GetTaskInterface",
          "[runtime][commonlib_adapter_task_marshaller][structural]") {
  //  The test target intentionally does not link CommonLibSSE-NG. This
  //  structural check protects the marshaller's contract against SKSE's own
  //  task interface without pretending to test SKSE internals as DovahLink
  //  behavior, mirroring bridge/game_state/commonlib_task_marshaller_test.cpp.
  std::string source =
      ReadSource(DOVAHLINK_ADAPTER_TASK_MARSHALLER_SOURCE_FILE);

  CHECK(source.starts_with("#include \"SKSE/SKSE.h\""));
  CHECK(source.find("SKSE::GetTaskInterface()->AddTask(std::move(task));") !=
        std::string::npos);
}
