import 'package:dovahlink_client_sdk/dovahlink_client.dart'
    show
        DovahLinkHost,
        DovahLinkHostAvailability,
        DovahLinkKnownHostSessionState,
        DovahLinkKnownHostState;

import 'package:flutter_test/flutter_test.dart';

import 'package:dovahlink_client/features/connection/domain/entities/known_host.entity.dart';
import 'package:dovahlink_client/features/connection/host.mapper.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import '../../fixtures/fixtures.dart';

/// Exercises the single SDK-to-app Host mapping boundary.
void main() {
  group('Method fromSdk behaves correctly', () {
    test('HostMapper.fromSdk maps identity, name, and endpoint', () {
      final DovahLinkHost sdkHost = DovahLinkHost(
        hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
        hostName: 'SKYRIM-PC',
        endpoint: Uri.parse('ws://127.0.0.1:58231/'),
      );

      final host = HostMapper.fromSdk(sdkHost);

      expect(host.hostId, isA<String>());
      expect(host.hostId, sdkHost.hostId);
      expect(host.displayName, isA<String>());
      expect(host.displayName, 'SKYRIM-PC');
      expect(host.uri, isA<Uri>());
      expect(host.uri, sdkHost.endpoint);
    });
  });

  group('Method fromSdkKnownHostState behaves correctly', () {
    test(
      'HostMapper.fromSdkKnownHostState maps metadata and every availability',
      () {
        final DovahLinkHost sdkHost = DovahLinkHost(
          hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
          hostName: 'SKYRIM-PC',
          endpoint: Uri.parse('ws://127.0.0.1:58231/'),
        );
        final List<DovahLinkHostAvailability> sdkAvailabilities = [
          DovahLinkHostAvailability.unknown,
          DovahLinkHostAvailability.online,
          DovahLinkHostAvailability.offline,
          DovahLinkHostAvailability.checking,
        ];
        final List<HostAvailability> appAvailabilities = [
          HostAvailability.unknown,
          HostAvailability.online,
          HostAvailability.offline,
          HostAvailability.checking,
        ];

        final List<KnownHost> mapped = [
          for (int index = 0; index < sdkAvailabilities.length; index++)
            HostMapper.fromSdkKnownHostState(
              DovahLinkKnownHostState(
                host: sdkHost,
                availability: sdkAvailabilities[index],
              ),
            ),
        ];

        expect(
          mapped.every(
            (KnownHost state) => state.host == HostMapper.fromSdk(sdkHost),
          ),
          isTrue,
        );
        expect(
          mapped.map((KnownHost state) => state.availability),
          appAvailabilities,
        );

        final List<KnownHostSessionState> mappedSessionStates =
            DovahLinkKnownHostSessionState.values
                .map(
                  (DovahLinkKnownHostSessionState sessionState) =>
                      HostMapper.fromSdkKnownHostState(
                        DovahLinkKnownHostState(
                          host: sdkHost,
                          availability: DovahLinkHostAvailability.unknown,
                          sessionState: sessionState,
                        ),
                      ).sessionState,
                )
                .toList();

        expect(mappedSessionStates, KnownHostSessionState.values);
      },
    );

    test('HostMapper.fromSdkKnownHostState maps the repair hint', () {
      final KnownHost knownHost = HostMapper.fromSdkKnownHostState(
        Fixtures.buildSdkKnownHostState(
          host: DovahLinkHost(
            hostId: '81869993-955c-4ba3-a7d0-d35ca86078ea',
            hostName: 'SKYRIM-PC',
            endpoint: Uri.parse('ws://127.0.0.1:58231/'),
          ),
          pairingRequired: true,
        ),
      );

      expect(knownHost.pairingRequired, isA<bool>());
      expect(knownHost.pairingRequired, isTrue);
    });
  });
}
