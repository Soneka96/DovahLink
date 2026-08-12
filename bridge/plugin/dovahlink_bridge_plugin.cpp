// The SKSE plugin entry point: the one place that constructs every
// plugin-lifetime component and wires them together for real. Every piece
// used here was built and tested in isolation in an earlier step; this file
// is deliberately thin glue, not new logic, per ai/context/skse/
// architecture.md's "no generic event framework or speculative service
// layer" and "keep the first implementation read-only and minimize hooks."
//
// Unlike every other file in this project, none of this can be exercised by
// dovahlink_bridge_tests: it requires a real SKSE load sequence inside a
// running Skyrim process (ai/context/skse/testing.md: "Reserve manual
// in-game checks for runtime hooks, event timing, loading behavior... that
// cannot be represented in unit tests"). It has been verified to compile
// against the real, pinned CommonLibSSE-NG headers; correctness beyond that
// depends on TASK.md's required manual verification record.

#include "SKSE/SKSE.h"

#include "application/bridge_transport.hpp"
#include "application/bridge_worker_pool.hpp"
#include "application/character_state_store.hpp"
#include "application/coordinator.hpp"
#include "application/session.hpp"
#include "game_state/commonlib_level_accessor.hpp"
#include "game_state/commonlib_level_increase_sink.hpp"
#include "game_state/level_increase_handler.hpp"
#include "game_state/runtime_guard.hpp"
#include "security/throttle.hpp"
#include "security/token_provider.hpp"
#include "security/token_store.hpp"
#include "transport/connection_slot.hpp"
#include "transport/listener.hpp"

#include <spdlog/sinks/basic_file_sink.h>

#include <boost/asio/io_context.hpp>

#include <memory>
#include <utility>

namespace {

// bridge/README.md's documented default loopback port. Phase 1 has no
// configuration-loading mechanism yet (TASK.md does not require one for
// the first proof, only that a default be chosen and documented), so this
// is the only value used.
constexpr std::uint16_t kBridgePort = 58231;

constexpr const char* kTokenEnvVar = "DOVAHLINK_BRIDGE_TOKEN";

// Minimal file logging, the standard CommonLibSSE-NG plugin setup: one log
// file under the SKSE log directory. Kept to the bare minimum this project
// actually needs (ai/context/skse/cpp-style.md: "do not add a dependency
// solely to avoid a small, well-understood adapter" -- spdlog is already a
// transitive CommonLibSSE-NG dependency, not a new one).
void SetupLogging() {
    auto path = SKSE::log::log_directory();
    if (!path.has_value()) {
        return;
    }
    *path /= "DovahLinkBridge.log";
    auto sink = std::make_shared<spdlog::sinks::basic_file_sink_mt>(path->string(), true);
    auto logger = std::make_shared<spdlog::logger>("global", std::move(sink));
    logger->set_level(spdlog::level::info);
    logger->flush_on(spdlog::level::info);
    spdlog::set_default_logger(std::move(logger));
}

// The one CommonLib-touching CallbackRegistry implementation
// (ai/context/skse/architecture.md: game-state adapters -- and their
// direct callers -- are the only bridge components allowed to depend on
// CommonLib/RE:: types). Trivial enough not to need its own header: this
// is the only place it is ever constructed. Wraps
// CommonLibLevelIncreaseSink::Register/Unregister, called by
// Coordinator::Start()/Shutdown() as the first and second steps of their
// documented sequences (application/coordinator.hpp).
class BridgeCallbackRegistry : public dovahlink::application::CallbackRegistry {
public:
    explicit BridgeCallbackRegistry(dovahlink::game_state::CommonLibLevelIncreaseSink& sink) : sink_(sink) {}

    void RegisterAll() override { sink_.Register(); }
    void UnregisterAll() override { sink_.Unregister(); }

private:
    dovahlink::game_state::CommonLibLevelIncreaseSink& sink_;
};

}  // namespace

// Hand-written in place of add_commonlibsse_plugin's auto-generated
// metadata file -- see the CMakeLists.txt comment next to this target for
// why. USE_ADDRESS_LIBRARY equivalent: SKSE::VersionIndependence::
// AddressLibrary, matching TASK.md's approved toolchain (bridge/README.md).
using namespace std::literals;
SKSEPluginInfo(
    .Version = REL::Version{0, 1, 0, 0},
    .Name = "DovahLink Bridge"sv,
    .Author = "Goncalo"sv,
    .SupportEmail = ""sv,
    .StructCompatibility = SKSE::StructCompatibility::Independent,
    .RuntimeCompatibility = SKSE::VersionIndependence::AddressLibrary,
    .MinimumSKSEVersion = REL::Version{2, 2, 6, 0})

SKSEPluginLoad(const SKSE::LoadInterface* skse) {
    SetupLogging();

    // Runtime guard: reject clearly during plugin initialization
    // (ai/context/skse/architecture.md), before anything else is built.
    // CommonLibSSE-NG's Address Library support (bridge/README.md) makes
    // this plugin *capable* of loading against other runtimes; that is not
    // the same as DovahLink *supporting* them (TASK.md: "Building with a
    // multi-runtime-capable library does not make another runtime
    // supported").
    REL::Version skyrimVersionRel = skse->RuntimeVersion();
    REL::Version skseVersionRel = REL::Version::unpack(skse->SKSEVersion());
    dovahlink::game_state::RuntimeVersion skyrimVersion{skyrimVersionRel[0], skyrimVersionRel[1],
                                                          skyrimVersionRel[2], skyrimVersionRel[3]};
    dovahlink::game_state::RuntimeVersion skseVersion{skseVersionRel[0], skseVersionRel[1], skseVersionRel[2],
                                                        skseVersionRel[3]};
    if (!dovahlink::game_state::IsSupportedSkyrimVersion(skyrimVersion) ||
        !dovahlink::game_state::IsSupportedSkseVersion(skseVersion)) {
        SKSE::log::error("Unsupported runtime: Skyrim {}.{}.{}.{}, SKSE {}.{}.{}.{}. DovahLink Bridge requires "
                          "exactly Skyrim {}.{}.{}, SKSE {}.{}.{}.",
                          skyrimVersion.major, skyrimVersion.minor, skyrimVersion.build, skyrimVersion.revision,
                          skseVersion.major, skseVersion.minor, skseVersion.build, skseVersion.revision,
                          dovahlink::game_state::kSupportedSkyrimVersion.major,
                          dovahlink::game_state::kSupportedSkyrimVersion.minor,
                          dovahlink::game_state::kSupportedSkyrimVersion.build,
                          dovahlink::game_state::kSupportedSkseVersion.major,
                          dovahlink::game_state::kSupportedSkseVersion.minor,
                          dovahlink::game_state::kSupportedSkseVersion.build);
        return false;
    }

    // One-time token: supplied out of band through the launch environment
    // (ai/context/protocol/security.md); a missing or malformed token is a
    // fatal, clearly-reported startup condition, not a silently-degraded
    // one -- there would be nothing a client could ever authenticate with.
    static dovahlink::security::WindowsEnvironmentReader environmentReader;
    auto tokenBytes = dovahlink::security::ReadTokenFromEnvironment(environmentReader, kTokenEnvVar);
    if (!tokenBytes.has_value()) {
        SKSE::log::error("DOVAHLINK_BRIDGE_TOKEN is not set to a valid 64-character hex-encoded 256-bit token; "
                          "the bridge cannot authenticate a client without it.");
        return false;
    }

    // Loopback listeners: both IPv4 and IPv6 are required (TASK.md); the
    // listening address is never configurable
    // (ai/context/protocol/security.md), only the port is, and this is the
    // one place that port value is used.
    static boost::asio::io_context ioc;
    auto listenerV4Result =
        dovahlink::transport::LoopbackListener::Create(ioc, dovahlink::transport::LoopbackListener::IpVersion::kV4,
                                                         kBridgePort);
    if (!listenerV4Result.has_value()) {
        SKSE::log::error("Failed to bind the IPv4 loopback listener on port {}.", kBridgePort);
        return false;
    }
    auto listenerV6Result =
        dovahlink::transport::LoopbackListener::Create(ioc, dovahlink::transport::LoopbackListener::IpVersion::kV6,
                                                         kBridgePort);
    if (!listenerV6Result.has_value()) {
        SKSE::log::error("Failed to bind the IPv6 loopback listener on port {}.", kBridgePort);
        return false;
    }
    static dovahlink::transport::LoopbackListener listenerV4 = std::move(*listenerV4Result);
    static dovahlink::transport::LoopbackListener listenerV6 = std::move(*listenerV6Result);

    // Plugin-lifetime application and security state. Declared as function-
    // local statics, in the exact order they depend on each other: a
    // function-local static is guaranteed initialized the first (and only,
    // for SKSEPluginLoad) time control reaches its declaration, so this
    // ordering is exactly the construction order, without needing a
    // hand-rolled aggregate to express the same dependency graph.
    static dovahlink::transport::ConnectionSlot connectionSlot;
    static dovahlink::security::TokenStore tokenStore(std::move(*tokenBytes));
    static dovahlink::security::FailedTokenThrottle tokenThrottle;
    static dovahlink::application::SessionManager sessionManager;
    static dovahlink::application::CharacterStateStore characterStateStore;

    static dovahlink::game_state::CommonLibLevelAccessor levelAccessor;
    static dovahlink::game_state::LevelIncreaseHandler levelIncreaseHandler(levelAccessor, characterStateStore);
    static dovahlink::game_state::CommonLibLevelIncreaseSink levelIncreaseSink(levelIncreaseHandler);
    static BridgeCallbackRegistry callbackRegistry(levelIncreaseSink);

    static dovahlink::application::BridgeTransport bridgeTransport(listenerV4, listenerV6);
    static dovahlink::application::BridgeWorkerPool bridgeWorkerPool(
        listenerV4, listenerV6, connectionSlot, tokenStore, tokenThrottle, sessionManager, characterStateStore);

    static dovahlink::application::Coordinator coordinator(callbackRegistry, bridgeWorkerPool, bridgeTransport);

    // RE::PlayerCharacter is not valid before the game's data has loaded
    // (ai/context/skse/architecture.md's "approved game-thread callback"),
    // so the initial level capture, the RE::LevelIncrease::Event
    // registration (via Coordinator::Start(), whose first step is
    // CallbackRegistry::RegisterAll()), and starting the worker pool and
    // transport are all deferred to kDataLoaded rather than done here.
    auto* messaging =
        static_cast<SKSE::MessagingInterface*>(skse->QueryInterface(SKSE::LoadInterface::kMessaging));
    if (!messaging) {
        SKSE::log::error("Unable to obtain SKSE's messaging interface; cannot defer startup to kDataLoaded.");
        return false;
    }
    messaging->RegisterListener([](SKSE::MessagingInterface::Message* message) {
        if (message->type != SKSE::MessagingInterface::kDataLoaded) {
            return;
        }
        levelIncreaseHandler.HandleLevelIncrease();
        coordinator.Start();
        SKSE::log::info("DovahLink Bridge listening on loopback port {} (IPv4 and IPv6).", kBridgePort);
    });

    return true;
}
