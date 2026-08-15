// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'protocol_envelope.model.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ProtocolEnvelopeModel _$ProtocolEnvelopeModelFromJson(
  Map<String, dynamic> json,
) => $checkedCreate('ProtocolEnvelopeModel', json, ($checkedConvert) {
  $checkKeys(
    json,
    requiredKeys: const [
      'protocolVersion',
      'messageType',
      'messageId',
      'sessionId',
      'correlationId',
      'payload',
    ],
  );
  final val = ProtocolEnvelopeModel(
    protocolVersion: $checkedConvert('protocolVersion', (v) => _readInteger(v)),
    messageType: $checkedConvert('messageType', (v) => v as String),
    messageId: $checkedConvert('messageId', (v) => v as String),
    sessionId: $checkedConvert('sessionId', (v) => v as String?),
    correlationId: $checkedConvert('correlationId', (v) => v as String?),
    payload: $checkedConvert('payload', (v) => v as Map<String, dynamic>),
    bridgeInstanceId: $checkedConvert('bridgeInstanceId', (v) => v as String?),
    playContextId: $checkedConvert('playContextId', (v) => v as String?),
    clientId: $checkedConvert('clientId', (v) => v as String?),
  );
  return val;
});

Map<String, dynamic> _$ProtocolEnvelopeModelToJson(
  ProtocolEnvelopeModel instance,
) => <String, dynamic>{
  'protocolVersion': instance.protocolVersion,
  'messageType': instance.messageType,
  'messageId': instance.messageId,
  'sessionId': instance.sessionId,
  'correlationId': instance.correlationId,
  'payload': instance.payload,
  'bridgeInstanceId': instance.bridgeInstanceId,
  'playContextId': instance.playContextId,
  'clientId': instance.clientId,
};
