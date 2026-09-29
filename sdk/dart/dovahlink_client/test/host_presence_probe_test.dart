import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkCompatibilityException,
        DovahLinkConnectionException,
        DovahLinkHost,
        DovahLinkProtocolException,
        HostPresenceProbe;
import 'package:dovahlink_client_sdk/src/shared/constants.dart'
    show kHostProbeResponseMaxBytes;

/// The test-only decoded shape of the public probe metadata.
typedef JsonMap = Map<String, Object?>;

/// Builds the small, unauthenticated metadata response served by the Host probe route.
/// @param hostId The asserted stable Host UUID.
/// @param hostName The asserted current Host display name.
/// @param hostVersion The asserted Host release version.
/// @return The public metadata fields.
JsonMap buildHostProbeMetadata({
  String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea',
  String hostName = 'Soneka-Desktop',
  String hostVersion = '0.5.0',
}) => <String, Object?>{
  'hostId': hostId,
  'hostName': hostName,
  'hostVersion': hostVersion,
};

void main() {
  group('Method probe behaves correctly', () {
    test(
      'Method probe sends one bodyless unauthenticated GET and returns a normalized Host claim',
      () async {
        final HttpServer server = await HttpServer.bind(
          InternetAddress.loopbackIPv4,
          0,
        );
        addTearDown(() => server.close(force: true));
        final Completer<HttpRequest> receivedRequest = Completer<HttpRequest>();
        server.listen((HttpRequest request) async {
          receivedRequest.complete(request);
          request.response.headers.contentType = ContentType.json;
          request.response.write(
            jsonEncode(
              buildHostProbeMetadata(
                hostId: 'A1869993-955C-4BCA-BA1D-D35CA86078EA',
                hostName: 'Soneka "Desktop"',
              ),
            ),
          );
          await request.response.close();
        });
        final Uri endpoint = Uri.parse(
          'ws://private:credential@127.0.0.1:${server.port}/ignored-path?token=private#fragment',
        );

        final DovahLinkHost host = await HostPresenceProbe().probe(endpoint);
        final HttpRequest request = await receivedRequest.future;

        expect(request.method, 'GET');
        expect(request.uri.path, '/.well-known/dovahlink');
        expect(request.uri.userInfo, isEmpty);
        expect(request.uri.hasQuery, isFalse);
        expect(
          await request.fold<List<int>>(
            <int>[],
            (body, chunk) => body..addAll(chunk),
          ),
          isEmpty,
        );
        expect(request.headers.value(HttpHeaders.authorizationHeader), isNull);
        expect(host.hostId, 'a1869993-955c-4bca-ba1d-d35ca86078ea');
        expect(host.hostName, 'Soneka "Desktop"');
        expect(host.endpoint, endpoint);
      },
    );

    test(
      'Method probe preserves the HTTP status from a rejected endpoint',
      () async {
        final HttpServer server = await HttpServer.bind(
          InternetAddress.loopbackIPv4,
          0,
        );
        addTearDown(() => server.close(force: true));
        server.listen((HttpRequest request) async {
          request.response.statusCode = HttpStatus.serviceUnavailable;
          await request.response.close();
        });

        await expectLater(
          HostPresenceProbe().probe(
            Uri.parse('ws://127.0.0.1:${server.port}/'),
          ),
          throwsA(
            isA<DovahLinkConnectionException>().having(
              (DovahLinkConnectionException error) => error.httpStatusCode,
              'httpStatusCode',
              HttpStatus.serviceUnavailable,
            ),
          ),
        );
      },
    );

    test(
      'Method probe translates a refused connection into a typed connection failure',
      () async {
        final HttpServer server = await HttpServer.bind(
          InternetAddress.loopbackIPv4,
          0,
        );
        final int port = server.port;
        await server.close(force: true);

        await expectLater(
          HostPresenceProbe().probe(Uri.parse('ws://127.0.0.1:$port/')),
          throwsA(
            isA<DovahLinkConnectionException>().having(
              (DovahLinkConnectionException error) => error.httpStatusCode,
              'httpStatusCode',
              isNull,
            ),
          ),
        );
      },
    );

    test(
      'Method probe rejects malformed JSON and invalid identity metadata',
      () async {
        final List<String> responses = <String>[
          '{not-json',
          jsonEncode(<Object?>[]),
          jsonEncode(buildHostProbeMetadata(hostId: 'not-a-uuid')),
          jsonEncode(buildHostProbeMetadata(hostName: 'bad\nname')),
          jsonEncode(buildHostProbeMetadata(hostVersion: '')),
        ];
        int responseIndex = 0;
        final HttpServer server = await HttpServer.bind(
          InternetAddress.loopbackIPv4,
          0,
        );
        addTearDown(() => server.close(force: true));
        server.listen((HttpRequest request) async {
          request.response.headers.contentType = ContentType.json;
          request.response.write(responses[responseIndex++]);
          await request.response.close();
        });
        final HostPresenceProbe probe = HostPresenceProbe();
        final Uri endpoint = Uri.parse('ws://127.0.0.1:${server.port}/');

        for (int index = 0; index < responses.length; index++) {
          await expectLater(
            probe.probe(endpoint),
            throwsA(isA<DovahLinkProtocolException>()),
          );
        }
      },
    );

    test('Method probe rejects an unsupported Host version', () async {
      final HttpServer server = await HttpServer.bind(
        InternetAddress.loopbackIPv4,
        0,
      );
      addTearDown(() => server.close(force: true));
      server.listen((HttpRequest request) async {
        request.response.headers.contentType = ContentType.json;
        request.response.write(
          jsonEncode(buildHostProbeMetadata(hostVersion: '0.4.0')),
        );
        await request.response.close();
      });

      await expectLater(
        HostPresenceProbe().probe(Uri.parse('ws://127.0.0.1:${server.port}/')),
        throwsA(isA<DovahLinkCompatibilityException>()),
      );
    });

    test('Method probe rejects responses above the fixed body limit', () async {
      final String oversizedBody = List<String>.filled(
        kHostProbeResponseMaxBytes + 1,
        'x',
      ).join();
      final HttpServer server = await HttpServer.bind(
        InternetAddress.loopbackIPv4,
        0,
      );
      addTearDown(() => server.close(force: true));
      server.listen((HttpRequest request) async {
        request.response.headers.contentLength = oversizedBody.length;
        request.response.write(oversizedBody);
        await request.response.close();
      });

      await expectLater(
        HostPresenceProbe().probe(Uri.parse('ws://127.0.0.1:${server.port}/')),
        throwsA(isA<DovahLinkProtocolException>()),
      );
    });

    test(
      'Method probe stops reading when a chunked response exceeds the body limit',
      () async {
        final String oversizedBody = List<String>.filled(
          kHostProbeResponseMaxBytes + 1,
          'x',
        ).join();
        final HttpServer server = await HttpServer.bind(
          InternetAddress.loopbackIPv4,
          0,
        );
        addTearDown(() => server.close(force: true));
        server.listen((HttpRequest request) async {
          request.response.headers.chunkedTransferEncoding = true;
          request.response.write(oversizedBody);
          await request.response.close();
        });

        await expectLater(
          HostPresenceProbe().probe(
            Uri.parse('ws://127.0.0.1:${server.port}/'),
          ),
          throwsA(isA<DovahLinkProtocolException>()),
        );
      },
    );

    test('Method probe rejects an invalid UTF-8 response body', () async {
      final HttpServer server = await HttpServer.bind(
        InternetAddress.loopbackIPv4,
        0,
      );
      addTearDown(() => server.close(force: true));
      server.listen((HttpRequest request) async {
        request.response.headers.contentType = ContentType.json;
        request.response.add(<int>[0xff, 0xfe]);
        await request.response.close();
      });

      await expectLater(
        HostPresenceProbe().probe(Uri.parse('ws://127.0.0.1:${server.port}/')),
        throwsA(isA<DovahLinkProtocolException>()),
      );
    });

    test(
      'Method probe times out a Host that leaves the response open',
      () async {
        final HttpServer server = await HttpServer.bind(
          InternetAddress.loopbackIPv4,
          0,
        );
        addTearDown(() => server.close(force: true));
        final Completer<void> receivedRequest = Completer<void>();
        server.listen((HttpRequest request) {
          receivedRequest.complete();
        });

        await expectLater(
          HostPresenceProbe.forTesting(
            timeout: const Duration(milliseconds: 50),
          ).probe(Uri.parse('ws://127.0.0.1:${server.port}/')),
          throwsA(isA<DovahLinkConnectionException>()),
        );
        await receivedRequest.future.timeout(const Duration(seconds: 5));
      },
    );
  });
}
