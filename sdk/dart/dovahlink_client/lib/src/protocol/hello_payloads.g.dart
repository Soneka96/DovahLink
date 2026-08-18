// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'hello_payloads.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

HelloAckPayload _$HelloAckPayloadFromJson(Map<String, dynamic> json) =>
    $checkedCreate('HelloAckPayload', json, ($checkedConvert) {
      $checkKeys(
        json,
        requiredKeys: const ['bridgeVersion', 'clientIdentityKind'],
      );
      final val = HelloAckPayload(
        bridgeVersion: $checkedConvert('bridgeVersion', (v) => v as String),
        clientIdentityKind: $checkedConvert(
          'clientIdentityKind',
          (v) => v as String,
        ),
      );
      return val;
    });
