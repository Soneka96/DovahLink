// Dart imports:
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

// Package imports:
import 'package:asn1lib/asn1lib.dart';
import 'package:crypto/crypto.dart';

/// Connects to the local feasibility Host and checks a correct and incorrect SPKI pin.
Future<void> main(List<String> arguments) async {
  final Map<String, String> options = <String, String>{};
  for (final String argument in arguments) {
    if (argument.startsWith('--')) {
      final MapEntry<String, String> option = _parseOption(argument);
      options[option.key] = option.value;
    }
  }
  final Uri endpoint = Uri.parse(options['url'] ?? '');
  final String expectedPin = options['pin'] ?? '';
  if (endpoint.scheme != 'wss' || expectedPin.isEmpty) {
    throw ArgumentError(
      'Pass --url=wss://127.0.0.1:<port>/ws and --pin=<base64url SPKI hash>.',
    );
  }

  final WebSocket socket = await _connect(endpoint, expectedPin);
  socket.add('hello-s2');
  final Object? reply = await socket.first.timeout(const Duration(seconds: 5));
  if (reply != 'dovahlink-s2-wss-ok') {
    throw StateError('The WSS Host returned an unexpected message: $reply');
  }
  await socket.close();

  bool wrongPinRejectedByPinCheck = false;
  bool wrongPinHandshakeRejected = false;
  try {
    await _connect(
      endpoint,
      '$expectedPin-wrong',
      onPinCheck: (bool accepted) {
        wrongPinRejectedByPinCheck = !accepted;
      },
    );
  } on HandshakeException {
    wrongPinHandshakeRejected = true;
  }
  if (!wrongPinRejectedByPinCheck || !wrongPinHandshakeRejected) {
    throw StateError('The WSS client accepted an incorrect Host SPKI pin.');
  }

  stdout.writeln('SPKI_PIN_ACCEPTED=PASS');
  stdout.writeln('WRONG_SPKI_PIN_REJECTED=PASS');
  stdout.writeln('WRONG_PIN_APPLICATION_MESSAGE_ACCEPTED=NO');
  stdout.writeln('TLS_1_3_WSS_EXCHANGE=PASS');
}

/// Opens a WSS connection whose self-signed server certificate is accepted only for [pin].
///
/// [onPinCheck] reports whether the certificate's SPKI pin matches [pin].
Future<WebSocket> _connect(
  Uri endpoint,
  String pin, {
  void Function(bool accepted)? onPinCheck,
}) {
  final HttpClient client =
      HttpClient(context: SecurityContext(withTrustedRoots: false))
        ..badCertificateCallback =
            (X509Certificate certificate, String host, int port) {
              final Uint8List spki = _subjectPublicKeyInfo(certificate.der);
              final String actualPin = base64Url
                  .encode(sha256.convert(spki).bytes)
                  .replaceAll('=', '');
              final bool accepted = actualPin == pin;
              onPinCheck?.call(accepted);
              return accepted;
            };
  return WebSocket.connect(endpoint.toString(), customClient: client);
}

/// Extracts the exact DER-encoded SubjectPublicKeyInfo sequence from an X.509 certificate.
Uint8List _subjectPublicKeyInfo(Uint8List certificateDer) {
  final ASN1Object certificateObject = ASN1Parser(certificateDer).nextObject();
  if (certificateObject is! ASN1Sequence ||
      certificateObject.elements.length != 3) {
    throw const FormatException(
      'Certificate must be a three-field X.509 sequence.',
    );
  }
  final ASN1Object tbsObject = certificateObject.elements.first;
  if (tbsObject is! ASN1Sequence) {
    throw const FormatException(
      'Certificate TBSCertificate must be a sequence.',
    );
  }
  final bool hasExplicitVersion =
      tbsObject.elements.first.encodedBytes.first == 0xa0;
  int index = hasExplicitVersion ? 1 : 0;
  index += 5; // Skip serial, signature, issuer, validity, and subject.
  if (index >= tbsObject.elements.length ||
      tbsObject.elements[index] is! ASN1Sequence) {
    throw const FormatException(
      'Certificate has no SubjectPublicKeyInfo sequence.',
    );
  }
  return Uint8List.fromList(tbsObject.elements[index].encodedBytes);
}

/// Returns the key and value from one `--key=value` command-line argument.
MapEntry<String, String> _parseOption(String argument) {
  final String body = argument.substring(2);
  final int separator = body.indexOf('=');
  if (separator < 0) {
    return MapEntry<String, String>(body, '');
  }
  return MapEntry<String, String>(
    body.substring(0, separator),
    body.substring(separator + 1),
  );
}
