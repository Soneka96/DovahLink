#pragma once

//  Curated aggregator for every registered protocol message payload, per
//  ai/context/common.md's file-organization rule: each payload type (and the
//  small helper types it composes) lives in its own file, and this barrel
//  declares no types of its own so existing
//  `#include "protocol/messages.hpp"` call sites across the codebase keep
//  working unchanged.
//
//  Payload codecs validate structural shape and types only. Stateful rules such
//  as revision sequencing and stale/gap detection belong to the application
//  layer. occurredAt is required to be a non-empty string but is not parsed as
//  an ordering source.

#include "protocol/capabilities_payload.hpp"
#include "protocol/capability.hpp"
#include "protocol/constants.hpp"
#include "protocol/decode_error.hpp"
#include "protocol/error_payload.hpp"
#include "protocol/hello_ack_payload.hpp"
#include "protocol/hello_payload.hpp"
#include "protocol/pairing_ack_payload.hpp"
#include "protocol/pairing_confirm_payload.hpp"
#include "protocol/pairing_outcome_payload.hpp"
#include "protocol/pairing_status_payload.hpp"
#include "protocol/rename_outcome_payload.hpp"
#include "protocol/rename_request_payload.hpp"
#include "protocol/snapshot_request_payload.hpp"
#include "protocol/state_event_payload.hpp"
#include "protocol/state_snapshot_payload.hpp"
#include "protocol/subscribe_payload.hpp"
#include "protocol/subscription_ack_payload.hpp"

//  `pairing_request` carries no payload (like `ping`), so it has no dedicated
//  struct or decode function -- see application/message_dispatcher.cpp's
//  BuildPong for the established empty-payload precedent.
