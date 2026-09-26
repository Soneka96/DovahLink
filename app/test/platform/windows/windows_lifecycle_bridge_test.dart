import 'package:flutter/services.dart';

import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/platform/windows/windows_lifecycle_bridge.dart';
import 'package:dovahlink_client/shared/utils/app_shutdown_service.dart';

/// Mocks the shared app shutdown service used by the Windows bridge.
class MockAppShutdownService extends Mock implements IAppShutdownService {}

/// Exercises native Windows lifecycle requests routed through the method channel.
void main() {
  const MethodChannel channel = MethodChannel(
    'test/dovahlink_windows_lifecycle',
  );
  late MockAppShutdownService shutdownService;
  late WindowsLifecycleBridge bridge;

  setUp(() {
    TestWidgetsFlutterBinding.ensureInitialized();
    shutdownService = MockAppShutdownService();
    when(() => shutdownService.shutdown()).thenAnswer((_) async {});
    bridge = WindowsLifecycleBridge(
      channel: channel,
      shutdownService: shutdownService,
    );
  });

  tearDown(() {
    channel.setMethodCallHandler(null);
  });

  group('Method register behaves correctly', () {
    for (final String method in <String>['requestClose', 'requestSessionEnd']) {
      test('Method register routes $method to shared shutdown', () async {
        bridge.register();

        final ByteData? response = await TestDefaultBinaryMessengerBinding
            .instance
            .defaultBinaryMessenger
            .handlePlatformMessage(
              channel.name,
              channel.codec.encodeMethodCall(MethodCall(method)),
              null,
            );

        expect(response, isNotNull);
        expect(channel.codec.decodeEnvelope(response!), isNull);
        verify(() => shutdownService.shutdown()).called(1);
      });
    }

    test('Method register ignores unsupported lifecycle methods', () async {
      bridge.register();

      final ByteData? response = await TestDefaultBinaryMessengerBinding
          .instance
          .defaultBinaryMessenger
          .handlePlatformMessage(
            channel.name,
            channel.codec.encodeMethodCall(const MethodCall('unsupported')),
            null,
          );

      expect(response, isNull);
      verifyNever(() => shutdownService.shutdown());
    });
  });
}
