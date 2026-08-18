// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'pairing_payloads.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

PairingStatusPayload _$PairingStatusPayloadFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('PairingStatusPayload', json, ($checkedConvert) {
  $checkKeys(json, requiredKeys: const ['state', 'expiresInSeconds']);
  final val = PairingStatusPayload(
    state: $checkedConvert('state', (v) => v as String),
    expiresInSeconds: $checkedConvert(
      'expiresInSeconds',
      (v) => (v as num?)?.toInt(),
    ),
  );
  return val;
});

PairingOutcomePayload _$PairingOutcomePayloadFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('PairingOutcomePayload', json, ($checkedConvert) {
  $checkKeys(
    json,
    requiredKeys: const [
      'outcome',
      'credential',
      'shortId',
      'displayName',
      'retryAfterSeconds',
    ],
  );
  final val = PairingOutcomePayload(
    outcome: $checkedConvert('outcome', (v) => v as String),
    credential: $checkedConvert('credential', (v) => v as String?),
    shortId: $checkedConvert('shortId', (v) => v as String?),
    displayName: $checkedConvert('displayName', (v) => v as String?),
    retryAfterSeconds: $checkedConvert(
      'retryAfterSeconds',
      (v) => (v as num?)?.toInt(),
    ),
  );
  return val;
});
