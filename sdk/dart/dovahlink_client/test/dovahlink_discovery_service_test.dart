import 'package:mocktail/mocktail.dart';
import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_discovery_service.dart'
    show buildDovahLinkDiscoveryServiceForTesting;
import 'fixtures/fixtures.dart';

/// A controlled probe that records local discovery requests.
class MockHostPresenceProbe extends Mock implements IHostPresenceProbe {}

void main() {
  setUpAll(() {
    registerFallbackValue(Uri.parse('ws://127.0.0.1:58231/'));
  });

  group('Method discover behaves correctly', () {
    late MockHostPresenceProbe probe;
    late Uri endpoint;

    setUp(() {
      probe = MockHostPresenceProbe();
      endpoint = Uri.parse('ws://127.0.0.1:58231/test-endpoint');
    });

    test(
      'Method discover probes its configured endpoint once and returns the Host claim',
      () async {
        final DovahLinkHost host = Fixtures.buildDovahLinkHost(
          endpoint: endpoint.toString(),
        );
        when(() => probe.probe(endpoint)).thenAnswer((_) async => host);
        final IDovahLinkDiscoveryService service =
            buildDovahLinkDiscoveryServiceForTesting(
              endpoint: endpoint,
              hostPresenceProbe: probe,
            );

        expect(await service.discover(), <DovahLinkHost>[host]);

        verify(() => probe.probe(endpoint)).called(1);
      },
    );

    test(
      'Method discover preserves a matching Known Host ID as an unverified claim',
      () async {
        final DovahLinkHost knownHost = Fixtures.buildDovahLinkHost(
          endpoint: 'ws://127.0.0.1:58230/',
        );
        final DovahLinkHost claimedHost = Fixtures.buildDovahLinkHost(
          hostId: knownHost.hostId,
          hostName: 'Claimed-Name',
          endpoint: endpoint.toString(),
        );
        when(() => probe.probe(endpoint)).thenAnswer((_) async => claimedHost);
        final IDovahLinkDiscoveryService service =
            buildDovahLinkDiscoveryServiceForTesting(
              endpoint: endpoint,
              hostPresenceProbe: probe,
            );

        final List<DovahLinkHost> candidates = await service.discover();

        expect(candidates, <DovahLinkHost>[claimedHost]);
        expect(candidates.single.hostId, knownHost.hostId);
        expect(candidates.single.endpoint, endpoint);
        verify(() => probe.probe(endpoint)).called(1);
      },
    );

    test(
      'Method discover returns no candidates when the endpoint cannot be reached',
      () async {
        when(() => probe.probe(endpoint)).thenThrow(
          const DovahLinkConnectionException('Could not reach the Host.'),
        );
        final IDovahLinkDiscoveryService service =
            buildDovahLinkDiscoveryServiceForTesting(
              endpoint: endpoint,
              hostPresenceProbe: probe,
            );

        expect(await service.discover(), isEmpty);

        verify(() => probe.probe(endpoint)).called(1);
      },
    );

    test('Method discover preserves an HTTP rejection status', () async {
      when(() => probe.probe(endpoint)).thenThrow(
        const DovahLinkConnectionException(
          'The Host probe returned HTTP 503.',
          httpStatusCode: 503,
        ),
      );
      final IDovahLinkDiscoveryService service =
          buildDovahLinkDiscoveryServiceForTesting(
            endpoint: endpoint,
            hostPresenceProbe: probe,
          );

      await expectLater(
        service.discover(),
        throwsA(
          isA<DovahLinkConnectionException>().having(
            (DovahLinkConnectionException error) => error.httpStatusCode,
            'httpStatusCode',
            503,
          ),
        ),
      );
      verify(() => probe.probe(endpoint)).called(1);
    });

    test(
      'Method discover preserves malformed and incompatible response failures',
      () async {
        final List<Exception> failures = <Exception>[
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'Malformed metadata.',
            retryable: false,
          ),
          const DovahLinkCompatibilityException(
            hostVersion: '0.4.0',
            supportedHostVersionRange: '0.5.x',
            failure: HostVersionCompatibilityFailure.hostTooOld,
          ),
        ];
        final IDovahLinkDiscoveryService service =
            buildDovahLinkDiscoveryServiceForTesting(
              endpoint: endpoint,
              hostPresenceProbe: probe,
            );
        for (final Exception failure in failures) {
          when(() => probe.probe(endpoint)).thenThrow(failure);

          await expectLater(service.discover(), throwsA(same(failure)));
        }
        verify(() => probe.probe(endpoint)).called(failures.length);
      },
    );
  });
}
