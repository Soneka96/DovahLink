// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'error_payload.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ErrorPayload _$ErrorPayloadFromJson(Map<String, dynamic> json) =>
    $checkedCreate('ErrorPayload', json, ($checkedConvert) {
      $checkKeys(json, requiredKeys: const ['code', 'message', 'retryable']);
      final val = ErrorPayload(
        code: $checkedConvert('code', (v) => v as String),
        message: $checkedConvert('message', (v) => v as String),
        retryable: $checkedConvert('retryable', (v) => v as bool),
        details: $checkedConvert('details', (v) => v as Map<String, dynamic>?),
      );
      return val;
    });
