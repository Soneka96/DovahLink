import 'dart:async';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/features/pairing/data/models/pairing_handshake.model.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import '../../../../fixtures/fixtures.dart';

/// Mocks the wrapped SDK client for [PairingRemoteDataSource] tests.
class MockDovahLinkClient extends Mock implements DovahLinkClient {}

/// Mocks the grouped SDK connection contract.
class MockDovahLinkConnections extends Mock implements IDovahLinkConnections {}

/// Mocks the grouped SDK pairing contract.
class MockDovahLinkPairing extends Mock implements IDovahLinkPairing {}

/// Exercises [PairingRemoteDataSource]'s exception-to-[Failure] mapping.
void main() {
  late MockDovahLinkClient mockClient;
  late MockDovahLinkConnections mockConnections;
  late MockDovahLinkPairing mockPairing;
  late PairingRemoteDataSource dataSource;
  final Uri hostUri = Uri.parse('ws://192.168.1.20:4000/');

  setUpAll(() {
    registerFallbackValue(Uri.parse('ws://127.0.0.1:58231/'));
    registerFallbackValue(
      DovahLinkHostId('81869993-955c-4ba3-a7d0-d35ca86078ea'),
    );
  });

  setUp(() {
    mockClient = MockDovahLinkClient();
    mockConnections = MockDovahLinkConnections();
    mockPairing = MockDovahLinkPairing();
    when(() => mockClient.connections).thenReturn(mockConnections);
    when(
      () => mockConnections.state,
    ).thenReturn(DovahLinkConnectionState.disconnected);
    when(() => mockClient.pairing).thenReturn(mockPairing);
    dataSource = PairingRemoteDataSource(mockClient);
  });

  group('Method authenticate behaves correctly', () {
    test(
      'Method authenticate delegates candidate lifecycle to the SDK pairing API',
      () async {
        final HelloResult hello = Fixtures.buildSdkHelloResult(
          hostVersion: '1.2.3',
          trustState: DovahLinkTrustState.unpaired,
        );
        when(() => mockPairing.authenticateCandidate(hostUri)).thenAnswer(
          (_) async => DovahLinkPairingHandshake(
            hello: hello,
            trustState: DovahLinkTrustState.trusted,
          ),
        );

        final result = await dataSource.authenticate(
          target: Left<Uri, String>(hostUri),
        );

        expect(
          result,
          Right<Failure, PairingHandshakeModel>(
            Fixtures.buildPairingHandshakeModel(trusted: true),
          ),
        );
        verify(() => mockPairing.authenticateCandidate(hostUri)).called(1);
        verifyNever(() => mockConnections.connectCandidate(any()));
        verifyNever(() => mockPairing.recoverPendingPairing());
      },
    );

    test(
      'Method authenticate delegates Known Host authentication by identity to the SDK',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        final HelloResult hello = Fixtures.buildSdkHelloResult(
          hostVersion: '1.2.3',
          trustState: DovahLinkTrustState.trusted,
        );
        when(
          () => mockPairing.authenticateKnownHost(DovahLinkHostId(hostId)),
        ).thenAnswer(
          (_) async => DovahLinkPairingHandshake(
            hello: hello,
            trustState: DovahLinkTrustState.trusted,
          ),
        );

        final result = await dataSource.authenticate(
          target: const Right(hostId),
        );

        expect(
          result,
          Right<Failure, PairingHandshakeModel>(
            Fixtures.buildPairingHandshakeModel(),
          ),
        );
        verify(
          () => mockPairing.authenticateKnownHost(DovahLinkHostId(hostId)),
        ).called(1);
        verifyNever(() => mockConnections.connectKnownHost(any()));
      },
    );

    test(
      'Method authenticate maps SDK-resolved unpaired state and rejected credential copy',
      () async {
        final HelloResult hello = Fixtures.buildSdkHelloResult(
          hostVersion: '1.2.3',
          trustState: DovahLinkTrustState.unpaired,
          recoveredFromRejectedCredential: CredentialRejectionReason.revoked,
        );
        when(() => mockPairing.authenticateCandidate(hostUri)).thenAnswer(
          (_) async => DovahLinkPairingHandshake(
            hello: hello,
            trustState: DovahLinkTrustState.unpaired,
          ),
        );

        final result = await dataSource.authenticate(
          target: Left<Uri, String>(hostUri),
        );

        expect(
          result,
          Right<Failure, PairingHandshakeModel>(
            Fixtures.buildPairingHandshakeModel(
              trusted: false,
              credentialRejectionReason:
                  PairingCredentialRejectionReason.revoked,
              credentialRejectedMessage: "This device's trust was revoked.",
            ),
          ),
        );
      },
    );

    test(
      'Method authenticate maps administrative invalidation separately from network failure',
      () async {
        when(() => mockPairing.authenticateCandidate(hostUri)).thenThrow(
          const DovahLinkConnectionException('Host invalidated session'),
        );
        when(
          () => mockConnections.state,
        ).thenReturn(DovahLinkConnectionState.administrativelyInvalidated);

        final result = await dataSource.authenticate(
          target: Left<Uri, String>(hostUri),
        );

        expect(
          result,
          const Left<Failure, PairingHandshakeModel>(
            SessionInvalidatedFailure.administrative,
          ),
        );
      },
    );

    test(
      'Method authenticate maps a connection failure to NetworkFailure',
      () async {
        when(
          () => mockPairing.authenticateCandidate(hostUri),
        ).thenThrow(const DovahLinkConnectionException('unreachable'));
        when(
          () => mockConnections.state,
        ).thenReturn(DovahLinkConnectionState.disconnected);

        final result = await dataSource.authenticate(
          target: Left<Uri, String>(hostUri),
        );

        expect(
          result,
          const Left<Failure, PairingHandshakeModel>(
            NetworkFailure('unreachable'),
          ),
        );
      },
    );

    test(
      'Method authenticate maps a terminal protocol failure to a safe PairingFailure',
      () async {
        when(() => mockPairing.authenticateCandidate(hostUri)).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'raw protocol diagnostic',
            retryable: false,
          ),
        );

        final result = await dataSource.authenticate(
          target: Left<Uri, String>(hostUri),
        );

        expect(
          result,
          const Left<Failure, PairingHandshakeModel>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
        expect(result.toString(), isNot(contains('raw protocol diagnostic')));
      },
    );

    test(
      'Method authenticate maps an escaping retryable protocol failure to the same safe PairingFailure',
      () async {
        when(() => mockPairing.authenticateCandidate(hostUri)).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.rateLimited,
            message: 'retry diagnostic',
            retryable: true,
          ),
        );

        final result = await dataSource.authenticate(
          target: Left<Uri, String>(hostUri),
        );

        expect(
          result,
          const Left<Failure, PairingHandshakeModel>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
        expect(result.toString(), isNot(contains('retry diagnostic')));
      },
    );

    test(
      'Method authenticate maps typed pairing failures to PairingFailure',
      () async {
        when(() => mockPairing.authenticateCandidate(hostUri)).thenThrow(
          const DovahLinkPairingException(PairingOutcome.pendingNotFound),
        );

        final result = await dataSource.authenticate(
          target: Left<Uri, String>(hostUri),
        );

        expect(
          result,
          const Left<Failure, PairingHandshakeModel>(
            PairingFailure(
              'This pairing attempt is no longer recognized. Request a new code.',
            ),
          ),
        );
      },
    );
  });
  group('Method requestPairingCode behaves correctly', () {
    test(
      'Method requestPairingCode returns Right with expiresInSeconds when a fresh code is shown',
      () async {
        when(() => mockPairing.requestCode()).thenAnswer(
          (_) async => const PairingChallengeStatus(
            availability: PairingAvailability.available,
            expiresInSeconds: 30,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(result, const Right<Failure, int?>(30));
      },
    );

    test(
      'Method requestPairingCode returns Right with expiresInSeconds when a challenge is already in progress',
      () async {
        when(() => mockPairing.requestCode()).thenAnswer(
          (_) async => const PairingChallengeStatus(
            availability: PairingAvailability.inProgress,
            expiresInSeconds: 15,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(result, const Right<Failure, int?>(15));
      },
    );

    test(
      'Method requestPairingCode returns a PairingFailure revealing nothing when a different device owns the challenge',
      () async {
        when(() => mockPairing.requestCode()).thenAnswer(
          (_) async => const PairingChallengeStatus(
            availability: PairingAvailability.otherDevicePairing,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure(
              'Another device is already pairing. Try again in a moment.',
            ),
          ),
        );
      },
    );

    test(
      'Method requestPairingCode returns Right with null when available but the host does not report an expiry',
      () async {
        when(() => mockPairing.requestCode()).thenAnswer(
          (_) async => const PairingChallengeStatus(
            availability: PairingAvailability.available,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(result, const Right<Failure, int?>(null));
      },
    );

    test(
      'Method requestPairingCode returns a PairingFailure and ignores expiresInSeconds when pairing is unavailable',
      () async {
        when(() => mockPairing.requestCode()).thenAnswer(
          (_) async => const PairingChallengeStatus(
            availability: PairingAvailability.unavailable,
            expiresInSeconds: 30,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure(
              'Pairing is not available right now. Try again in a moment.',
            ),
          ),
        );
      },
    );

    test(
      'Method requestPairingCode maps a connection failure to NetworkFailure',
      () async {
        when(
          () => mockPairing.requestCode(),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(
          result,
          const Left<Failure, int?>(NetworkFailure('socket failed')),
        );
      },
    );

    test(
      'Method requestPairingCode maps a protocol failure to NetworkFailure',
      () async {
        when(() => mockPairing.requestCode()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'bad reply',
            retryable: false,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(result, const Left<Failure, int?>(NetworkFailure('bad reply')));
      },
    );

    test(
      'Method requestPairingCode maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(() => mockPairing.requestCode()).thenThrow(StateError('boom'));

        final Either<Failure, int?> result = await dataSource
            .requestPairingCode();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );
  });

  group('Method confirmPairingCode behaves correctly', () {
    test(
      'Method confirmPairingCode delegates the complete operation to the SDK',
      () async {
        when(
          () => mockPairing.confirmCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenAnswer((_) async {});
        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456', displayName: 'Desktop');

        expect(result, const Right<Failure, Unit>(unit));
        verify(
          () => mockPairing.confirmCode(code: '123456', displayName: 'Desktop'),
        ).called(1);
      },
    );

    test(
      'Method confirmPairingCode maps an expired code to a user-safe PairingFailure',
      () async {
        when(
          () => mockPairing.confirmCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.expired));

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure('That pairing code has expired. Request a new one.'),
          ),
        );
      },
    );

    test(
      'Method confirmPairingCode maps an invalid code to a retriable PairingRetriableFailure',
      () async {
        when(
          () => mockPairing.confirmCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.invalid));

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '000000');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingRetriableFailure(
              "That code isn't correct. Check Skyrim and try again.",
            ),
          ),
        );
      },
    );

    test(
      'Method confirmPairingCode maps a pacing-limited attempt to a retriable PairingRetriableFailure',
      () async {
        when(
          () => mockPairing.confirmCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(
          const DovahLinkPairingException(PairingOutcome.pacingLimited),
        );

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '000000');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingRetriableFailure('Slow down a little, then try again.'),
          ),
        );
      },
    );

    test(
      'Method confirmPairingCode maps a hard-limit-reached code to a non-retriable PairingFailure',
      () async {
        when(
          () => mockPairing.confirmCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(
          const DovahLinkPairingException(PairingOutcome.hardLimitReached),
        );

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '000000');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure(
              'Too many wrong attempts. Request a new pairing code.',
            ),
          ),
        );
        expect(
          result.fold((f) => f, (_) => null),
          isNot(isA<PairingRetriableFailure>()),
        );
      },
    );

    test(
      'Method confirmPairingCode maps a pending-not-found result to a non-retriable PairingFailure',
      () async {
        when(
          () => mockPairing.confirmCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(
          const DovahLinkPairingException(PairingOutcome.pendingNotFound),
        );

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure(
              'This pairing attempt is no longer recognized. Request a new code.',
            ),
          ),
        );
      },
    );

    test(
      'Method confirmPairingCode maps a connection failure to NetworkFailure',
      () async {
        when(
          () => mockPairing.confirmCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(
          result,
          const Left<Failure, Unit>(NetworkFailure('socket failed')),
        );
      },
    );

    test(
      'Method confirmPairingCode maps a protocol failure to NetworkFailure',
      () async {
        when(
          () => mockPairing.confirmCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'bad reply',
            retryable: false,
          ),
        );

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(result, const Left<Failure, Unit>(NetworkFailure('bad reply')));
      },
    );

    test(
      'Method confirmPairingCode maps a storage failure to DatabaseFailure',
      () async {
        when(
          () => mockPairing.confirmCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(const DovahLinkStorageException('corrupt store'));

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(
          result,
          const Left<Failure, Unit>(DatabaseFailure('corrupt store')),
        );
      },
    );

    test(
      'Method confirmPairingCode maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(
          () => mockPairing.confirmCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(StateError('boom'));

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );
  });

  group('Method disconnect behaves correctly', () {
    test('Method disconnect returns Right on a clean disconnect', () async {
      when(() => mockConnections.disconnect()).thenAnswer((_) async {});

      final Either<Failure, Unit> result = await dataSource.disconnect();

      expect(result, const Right<Failure, Unit>(unit));
    });

    test(
      'Method disconnect maps a connection failure to NetworkFailure',
      () async {
        when(
          () => mockConnections.disconnect(),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));

        final Either<Failure, Unit> result = await dataSource.disconnect();

        expect(
          result,
          const Left<Failure, Unit>(NetworkFailure('socket failed')),
        );
      },
    );

    test(
      'Method disconnect maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(() => mockConnections.disconnect()).thenThrow(StateError('boom'));

        final Either<Failure, Unit> result = await dataSource.disconnect();

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );
  });

  group('Method requestPairingRenotify behaves correctly', () {
    test(
      'Method requestPairingRenotify returns Host retry seconds after successful redisplay',
      () async {
        when(() => mockPairing.renotify()).thenAnswer(
          (_) async => const PairingRenotifyResult(
            status: PairingRenotifyStatus.renotified,
            retryAfterSeconds: 5,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(result, const Right<Failure, int?>(5));
      },
    );

    test(
      'Method requestPairingRenotify returns Right with cooldown seconds when cooldown is active',
      () async {
        when(() => mockPairing.renotify()).thenAnswer(
          (_) async => const PairingRenotifyResult(
            status: PairingRenotifyStatus.cooldown,
            retryAfterSeconds: 3,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(result, const Right<Failure, int?>(3));
      },
    );

    test(
      'Method requestPairingRenotify returns Left with PairingFailure when nothing is owned',
      () async {
        when(() => mockPairing.renotify()).thenAnswer(
          (_) async => const PairingRenotifyResult(
            status: PairingRenotifyStatus.alreadyIdle,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure('No pairing is currently active.'),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps a connection failure to NetworkFailure',
      () async {
        when(
          () => mockPairing.renotify(),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(NetworkFailure('socket failed')),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps a protocol failure to NetworkFailure',
      () async {
        when(() => mockPairing.renotify()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'bad reply',
            retryable: false,
          ),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(result, const Left<Failure, int?>(NetworkFailure('bad reply')));
      },
    );

    test(
      'Method requestPairingRenotify maps an expired pairing outcome to a user-safe PairingFailure',
      () async {
        when(
          () => mockPairing.renotify(),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.expired));

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure('That pairing code has expired. Request a new one.'),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps an invalid pairing outcome to a user-safe PairingFailure',
      () async {
        when(
          () => mockPairing.renotify(),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.invalid));

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure(
              "That code isn't correct. Check Skyrim and try again.",
            ),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps a pairing outcome outside the explicit message set to a generic PairingFailure',
      () async {
        // credentialIssued is a real PairingOutcome value, just never a valid reply to
        // pairing_renotify -- exercises _pairingOutcomeMessage's defensive fallback arm the same
        // way an unrecognized wire value used to, before PairingOutcome became a closed enum.
        when(() => mockPairing.renotify()).thenThrow(
          const DovahLinkPairingException(PairingOutcome.credentialIssued),
        );

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(() => mockPairing.renotify()).thenThrow(StateError('boom'));

        final Either<Failure, int?> result = await dataSource
            .requestPairingRenotify();

        expect(
          result,
          const Left<Failure, int?>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );
  });

  group('Method cancelPairing behaves correctly', () {
    test(
      'Method cancelPairing returns Right when a challenge was cancelled',
      () async {
        when(() => mockPairing.cancel()).thenAnswer(
          (_) async =>
              const PairingCancelOutcome(status: PairingCancelStatus.cancelled),
        );

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(result, const Right<Failure, Unit>(unit));
      },
    );

    test(
      'Method cancelPairing returns Right when nothing was owned (already idle)',
      () async {
        when(() => mockPairing.cancel()).thenAnswer(
          (_) async => const PairingCancelOutcome(
            status: PairingCancelStatus.alreadyIdle,
          ),
        );

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(result, const Right<Failure, Unit>(unit));
      },
    );

    test(
      'Method cancelPairing maps a connection failure to NetworkFailure',
      () async {
        when(
          () => mockPairing.cancel(),
        ).thenThrow(const DovahLinkConnectionException('socket failed'));

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(
          result,
          const Left<Failure, Unit>(NetworkFailure('socket failed')),
        );
      },
    );

    test(
      'Method cancelPairing maps a protocol failure to NetworkFailure',
      () async {
        when(() => mockPairing.cancel()).thenThrow(
          const DovahLinkProtocolException(
            code: ProtocolErrorCode.malformedMessage,
            message: 'bad reply',
            retryable: false,
          ),
        );

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(result, const Left<Failure, Unit>(NetworkFailure('bad reply')));
      },
    );

    test(
      'Method cancelPairing maps a pairing exception to a user-safe PairingFailure',
      () async {
        when(
          () => mockPairing.cancel(),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.expired));

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure('That pairing code has expired. Request a new one.'),
          ),
        );
      },
    );

    test(
      'Method cancelPairing maps a storage failure to DatabaseFailure',
      () async {
        when(
          () => mockPairing.cancel(),
        ).thenThrow(const DovahLinkStorageException('corrupt store'));

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(
          result,
          const Left<Failure, Unit>(DatabaseFailure('corrupt store')),
        );
      },
    );

    test(
      'Method cancelPairing maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(() => mockPairing.cancel()).thenThrow(StateError('boom'));

        final Either<Failure, Unit> result = await dataSource.cancelPairing();

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );
  });

  group('Property connectionStatus behaves correctly', () {
    test(
      'Property connectionStatus emits lost when the client becomes reconnecting',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockConnections.stateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emits(PairingConnectionStatus.lost),
        );
        connectionStates.add(DovahLinkConnectionState.reconnecting);

        await expectation;
      },
    );

    test(
      'Property connectionStatus emits lost when the client becomes reauthenticating',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockConnections.stateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emits(PairingConnectionStatus.lost),
        );
        connectionStates.add(DovahLinkConnectionState.reauthenticating);

        await expectation;
      },
    );

    test(
      'Property connectionStatus emits lost when the client becomes disconnected',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockConnections.stateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emits(PairingConnectionStatus.lost),
        );
        connectionStates.add(DovahLinkConnectionState.disconnected);

        await expectation;
      },
    );

    test(
      'Property connectionStatus emits restored when the client becomes connected',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockConnections.stateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emits(PairingConnectionStatus.restored),
        );
        connectionStates.add(DovahLinkConnectionState.connected);

        await expectation;
      },
    );

    test(
      'Property connectionStatus emits invalidated when the client becomes administratively '
      'invalidated',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockConnections.stateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emits(PairingConnectionStatus.invalidated),
        );
        connectionStates.add(
          DovahLinkConnectionState.administrativelyInvalidated,
        );

        await expectation;
      },
    );

    test(
      'Property connectionStatus does not emit for a connecting connectionState transition',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockConnections.stateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final List<PairingConnectionStatus> received =
            <PairingConnectionStatus>[];
        final StreamSubscription<PairingConnectionStatus> subscription =
            dataSource.connectionStatus.listen(received.add);
        addTearDown(subscription.cancel);

        connectionStates.add(DovahLinkConnectionState.connecting);
        await pumpEventQueue();

        expect(received, isEmpty);
      },
    );

    test(
      'Property connectionStatus emits a separate lost for each of two consecutive '
      'lost-mapped transitions, rather than coalescing them',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockConnections.stateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emitsInOrder(<Object>[
            PairingConnectionStatus.lost,
            PairingConnectionStatus.lost,
          ]),
        );
        connectionStates
          ..add(DovahLinkConnectionState.reconnecting)
          ..add(DovahLinkConnectionState.disconnected);

        await expectation;
      },
    );

    test(
      'Property connectionStatus does not report restored until a reauthenticating recovery '
      'attempt actually resolves to connected',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockConnections.stateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emitsInOrder(<Object>[
            PairingConnectionStatus.lost,
            PairingConnectionStatus.lost,
            PairingConnectionStatus.restored,
          ]),
        );
        connectionStates
          ..add(DovahLinkConnectionState.reconnecting)
          ..add(DovahLinkConnectionState.reauthenticating)
          ..add(DovahLinkConnectionState.connected);

        await expectation;
      },
    );

    test(
      'Property connectionStatus emits one status per transition in a mixed sequence',
      () async {
        final StreamController<DovahLinkConnectionState> connectionStates =
            StreamController<DovahLinkConnectionState>.broadcast();
        addTearDown(connectionStates.close);
        when(
          () => mockConnections.stateChanges,
        ).thenAnswer((_) => connectionStates.stream);

        final Future<void> expectation = expectLater(
          dataSource.connectionStatus,
          emitsInOrder(<Object>[
            PairingConnectionStatus.lost,
            PairingConnectionStatus.restored,
            PairingConnectionStatus.lost,
            PairingConnectionStatus.invalidated,
          ]),
        );
        connectionStates
          ..add(DovahLinkConnectionState.reconnecting)
          ..add(DovahLinkConnectionState.connecting)
          ..add(DovahLinkConnectionState.connected)
          ..add(DovahLinkConnectionState.disconnected)
          ..add(DovahLinkConnectionState.administrativelyInvalidated);

        await expectation;
      },
    );
  });
}
