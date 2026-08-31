#pragma once

#include <cstddef>
#include <cstdint>
#include <expected>
#include <optional>
#include <span>
#include <vector>

#include "ipc/ipc_enums.hpp"
#include "ipc/ipc_message.hpp"

namespace dovahlink::adapter::ipc {

///  Encodes and decodes private host-to-adapter IPC frames. The wire layout is:
///  a 4-byte little-endian frame length (the byte count of everything after
///  this field), a 1-byte message kind, an 8-byte little-endian correlation id,
///  then a kind-specific payload. Host and adapter are shipped as one package;
///  peer ownership and lifetime proof, rather than a negotiated protocol
///  version, determine whether the connection belongs to this installation.
///  This codec performs no I/O; it operates on already-read frame bytes and
///  produces owned plain values only.
class IIpcFrameCodec {
public:
  virtual ~IIpcFrameCodec() = default;

  ///  Encodes a message into a complete frame, including its length prefix.
  virtual std::vector<std::byte> Encode(const IpcMessage &message) const = 0;

  ///  Reads and validates a frame's 4-byte length prefix before any payload
  ///  bytes are read or allocated, so an over-limit declared length is rejected
  ///  without allocating a buffer for it.
  ///  @return The validated header-plus-payload byte length, or `std::nullopt`
  ///  if the prefix is the wrong size or declares an out-of-range length.
  virtual std::optional<std::size_t>
  TryReadFrameLength(std::span<const std::byte> lengthPrefix) const = 0;

  ///  Decodes one frame's header-plus-payload bytes (excluding the length
  ///  prefix) into a message, or the fail-closed reason it was rejected.
  ///  @param frame The header-plus-payload bytes, as validated by
  ///  `TryReadFrameLength`.
  virtual std::expected<IpcMessage, IpcRejectReason>
  Decode(std::span<const std::byte> frame) const = 0;
};

///  @copydoc IIpcFrameCodec
class IpcFrameCodec final : public IIpcFrameCodec {
public:
  ///  @copydoc IIpcFrameCodec::Encode
  std::vector<std::byte> Encode(const IpcMessage &message) const override;

  ///  @copydoc IIpcFrameCodec::TryReadFrameLength
  std::optional<std::size_t>
  TryReadFrameLength(std::span<const std::byte> lengthPrefix) const override;

  ///  @copydoc IIpcFrameCodec::Decode
  std::expected<IpcMessage, IpcRejectReason>
  Decode(std::span<const std::byte> frame) const override;

private:
  ///  Encodes an `IpcHelloMessage` payload: 16 identity bytes, a length byte,
  ///  the token, then the fixed-size challenge and owner-lifetime-id fields.
  ///  @throws std::invalid_argument The peer-proof token exceeds
  ///  `kMaxIpcPeerProofTokenBytes`.
  static std::vector<std::byte> EncodeHello(const IpcHelloMessage &hello);

  ///  Encodes an `IpcHelloAckMessage` payload: accepted, reject reason, then
  ///  the fixed-size host proof.
  static std::vector<std::byte>
  EncodeHelloAck(const IpcHelloAckMessage &helloAck);

  ///  Decodes an `IpcHelloMessage` payload, validating the bounded token
  ///  length.
  static std::expected<IpcMessage, IpcRejectReason>
  DecodeHello(std::uint64_t correlationId, std::span<const std::byte> payload);

  ///  Decodes an `IpcHelloAckMessage` payload, validating its boolean and enum
  ///  fields.
  static std::expected<IpcMessage, IpcRejectReason>
  DecodeHelloAck(std::uint64_t correlationId,
                 std::span<const std::byte> payload);

  ///  Decodes an `IpcResynchronizeResultMessage` payload, validating its
  ///  boolean field.
  static std::expected<IpcMessage, IpcRejectReason>
  DecodeResynchronizeResult(std::uint64_t correlationId,
                            std::span<const std::byte> payload);

  ///  Decodes an `IpcCloseMessage` payload, validating its reason enum.
  static std::expected<IpcMessage, IpcRejectReason>
  DecodeClose(std::uint64_t correlationId, std::span<const std::byte> payload);

  ///  Decodes an `IpcRejectMessage` payload, validating its reason enum.
  static std::expected<IpcMessage, IpcRejectReason>
  DecodeReject(std::uint64_t correlationId, std::span<const std::byte> payload);

  ///  Encodes a host-owned event key payload.
  static std::vector<std::byte>
  EncodeListenEvent(const IpcListenEventMessage &listenEvent);

  ///  Encodes a host-owned sample token payload.
  static std::vector<std::byte>
  EncodeReadSample(const IpcReadSampleMessage &readSample);

  ///  Decodes a host-directed event-listening request.
  static std::expected<IpcMessage, IpcRejectReason>
  DecodeListenEvent(std::uint64_t correlationId,
                    std::span<const std::byte> payload);

  ///  Decodes a host-directed sample-read request.
  static std::expected<IpcMessage, IpcRejectReason>
  DecodeReadSample(std::uint64_t correlationId,
                   std::span<const std::byte> payload);
};

} //  namespace dovahlink::adapter::ipc
