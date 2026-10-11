import 'dart:async';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fpdart/fpdart.dart';
import 'package:mocktail/mocktail.dart';

import 'package:dovahlink_client/features/pairing/data/datasources/pairing_remote.datasource.dart';
import 'package:dovahlink_client/shared/constants/enums.dart';
import 'package:dovahlink_client/shared/failures/failures.dart';
import '../../../../fixtures/fixtures.dart';

import 'package:dovahlink_client/features/pairing/domain/entities/pairing_renotify_result.entity.dart'
    as app;

/// Mocks the wrapped SDK client for [PairingRemoteDataSource] tests.
class MockDovahLinkClient extends Mock implements DovahLinkClient {}

/// Mocks the grouped SDK connection contract.
class MockDovahLinkConnections extends Mock implements IDovahLinkConnections {}

/// Mocks the grouped SDK pairing contract.
class MockDovahLinkPairing extends Mock implements IDovahLinkPairing {}

/// Mocks the grouped SDK current-Host contract that owns the authoritative trust state.
class MockDovahLinkCurrentHost extends Mock implements IDovahLinkCurrentHost {}

/// Exercises [PairingRemoteDataSource]'s exception-to-[Failure] mapping.
void main() {
  late MockDovahLinkClient mockClient;
  late MockDovahLinkConnections mockConnections;
  late MockDovahLinkPairing mockPairing;
  late MockDovahLinkCurrentHost mockCurrentHost;
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
    mockCurrentHost = MockDovahLinkCurrentHost();
    when(
      () => mockCurrentHost.trustState,
    ).thenReturn(DovahLinkTrustState.trusted);
    when(() => mockClient.currentHost).thenReturn(mockCurrentHost);
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
          Right<Failure, PairingAuthentication>(
            Fixtures.buildPairingAuthentication(trusted: true),
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
          Right<Failure, PairingAuthentication>(
            Fixtures.buildPairingAuthentication(),
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
          Right<Failure, PairingAuthentication>(
            Fixtures.buildPairingAuthentication(
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
      'Method authenticate maps each rejected credential reason to its typed reason and copy',
      () async {
        final mappings =
            <
              (
                CredentialRejectionReason,
                PairingCredentialRejectionReason,
                String,
              )
            >[
              (
                CredentialRejectionReason.revoked,
                PairingCredentialRejectionReason.revoked,
                "This device's trust was revoked.",
              ),
              (
                CredentialRejectionReason.unrecognized,
                PairingCredentialRejectionReason.unrecognized,
                "This device isn't recognized by this host.",
              ),
              (
                CredentialRejectionReason.blocked,
                PairingCredentialRejectionReason.blocked,
                'This device is blocked by the host and cannot be paired again until an '
                    'administrator unblocks it.',
              ),
            ];

        for (final (reason, appReason, message) in mappings) {
          when(() => mockPairing.authenticateCandidate(hostUri)).thenAnswer(
            (_) async => DovahLinkPairingHandshake(
              hello: Fixtures.buildSdkHelloResult(
                hostVersion: '2.0.0',
                trustState: DovahLinkTrustState.unpaired,
                recoveredFromRejectedCredential: reason,
              ),
              trustState: DovahLinkTrustState.unpaired,
            ),
          );

          final result = await dataSource.authenticate(
            target: Left<Uri, String>(hostUri),
          );

          expect(
            result,
            Right<Failure, PairingAuthentication>(
              Fixtures.buildPairingAuthentication(
                hostVersion: '2.0.0',
                trusted: false,
                credentialRejectionReason: appReason,
                credentialRejectedMessage: message,
              ),
            ),
          );
        }
      },
    );

    test(
      'Method authenticate reports trusted from the SDK-resolved trust state, not from hello',
      () async {
        when(() => mockPairing.authenticateCandidate(hostUri)).thenAnswer(
          (_) async => DovahLinkPairingHandshake(
            hello: Fixtures.buildSdkHelloResult(
              trustState: DovahLinkTrustState.unpaired,
            ),
            trustState: DovahLinkTrustState.trusted,
          ),
        );
        final recovered = await dataSource.authenticate(
          target: Left<Uri, String>(hostUri),
        );
        when(() => mockPairing.authenticateCandidate(hostUri)).thenAnswer(
          (_) async => DovahLinkPairingHandshake(
            hello: Fixtures.buildSdkHelloResult(
              trustState: DovahLinkTrustState.trusted,
            ),
            trustState: DovahLinkTrustState.unpaired,
          ),
        );
        final unresolved = await dataSource.authenticate(
          target: Left<Uri, String>(hostUri),
        );

        expect(
          recovered.map((a) => a.trusted),
          const Right<Failure, bool>(true),
        );
        expect(
          unresolved.map((a) => a.trusted),
          const Right<Failure, bool>(false),
        );
      },
    );

    test(
      'Method authenticate maps a Known Host rejection and a non-default Host version',
      () async {
        const String hostId = '81869993-955c-4ba3-a7d0-d35ca86078ea';
        when(
          () => mockPairing.authenticateKnownHost(DovahLinkHostId(hostId)),
        ).thenAnswer(
          (_) async => DovahLinkPairingHandshake(
            hello: Fixtures.buildSdkHelloResult(
              hostVersion: '9.8.7',
              trustState: DovahLinkTrustState.unpaired,
              recoveredFromRejectedCredential:
                  CredentialRejectionReason.unrecognized,
            ),
            trustState: DovahLinkTrustState.unpaired,
          ),
        );

        final result = await dataSource.authenticate(
          target: const Right(hostId),
        );

        expect(
          result,
          Right<Failure, PairingAuthentication>(
            Fixtures.buildPairingAuthentication(
              hostVersion: '9.8.7',
              trusted: false,
              credentialRejectionReason:
                  PairingCredentialRejectionReason.unrecognized,
              credentialRejectedMessage:
                  "This device isn't recognized by this host.",
            ),
          ),
        );
      },
    );

    test(
      'Method authenticate keeps the rejection copy when the SDK then resolves the session trusted',
      () async {
        when(() => mockPairing.authenticateCandidate(hostUri)).thenAnswer(
          (_) async => DovahLinkPairingHandshake(
            hello: Fixtures.buildSdkHelloResult(
              trustState: DovahLinkTrustState.unpaired,
              recoveredFromRejectedCredential:
                  CredentialRejectionReason.revoked,
            ),
            trustState: DovahLinkTrustState.trusted,
          ),
        );

        final result = await dataSource.authenticate(
          target: Left<Uri, String>(hostUri),
        );

        expect(
          result,
          Right<Failure, PairingAuthentication>(
            Fixtures.buildPairingAuthentication(
              trusted: true,
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
          const Left<Failure, PairingAuthentication>(
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
          const Left<Failure, PairingAuthentication>(
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
          const Left<Failure, PairingAuthentication>(
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
          const Left<Failure, PairingAuthentication>(
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
          const Left<Failure, PairingAuthentication>(
            PairingFailure(
              'Pairing could not be completed. Please try again.',
              outcome: PairingFailureOutcome.pendingNotFound,
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
            PairingFailure(
              'Pairing could not be completed. Please try again.',
              outcome: PairingFailureOutcome.expired,
            ),
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
        ).thenThrow(
          const DovahLinkPairingException(
            PairingOutcome.invalid,
            attemptsRemaining: 2,
          ),
        );

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '000000');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingRetriableFailure(
              'Pairing could not be completed. Please try again.',
              outcome: PairingFailureOutcome.invalid,
              attemptsRemaining: 2,
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
            PairingRetriableFailure(
              'Pairing could not be completed. Please try again.',
              outcome: PairingFailureOutcome.pacingLimited,
            ),
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
              'Pairing could not be completed. Please try again.',
              outcome: PairingFailureOutcome.hardLimitReached,
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
              'Pairing could not be completed. Please try again.',
              outcome: PairingFailureOutcome.pendingNotFound,
            ),
          ),
        );
      },
    );

    test(
      'Method confirmPairingCode preserves the typed administrative invalidation outcome',
      () async {
        when(
          () => mockPairing.confirmCode(
            code: any(named: 'code'),
            displayName: any(named: 'displayName'),
          ),
        ).thenThrow(
          const DovahLinkPairingException(PairingOutcome.pairingInvalidated),
        );

        final Either<Failure, Unit> result = await dataSource
            .confirmPairingCode(code: '123456');

        expect(
          result,
          const Left<Failure, Unit>(
            PairingFailure(
              'Pairing could not be completed. Please try again.',
              outcome: PairingFailureOutcome.pairingInvalidated,
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

        final Either<Failure, app.PairingRenotifyResult> result =
            await dataSource.requestPairingRenotify();

        expect(
          result,
          const Right<Failure, app.PairingRenotifyResult>(
            app.PairingRenotifyResult(
              outcome: PairingRenotifyOutcome.renotified,
              retryAfterSeconds: 5,
            ),
          ),
        );
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

        final Either<Failure, app.PairingRenotifyResult> result =
            await dataSource.requestPairingRenotify();

        expect(
          result,
          const Right<Failure, app.PairingRenotifyResult>(
            app.PairingRenotifyResult(
              outcome: PairingRenotifyOutcome.cooldown,
              retryAfterSeconds: 3,
            ),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify preserves the already-idle Host status',
      () async {
        when(() => mockPairing.renotify()).thenAnswer(
          (_) async => const PairingRenotifyResult(
            status: PairingRenotifyStatus.alreadyIdle,
          ),
        );

        final Either<Failure, app.PairingRenotifyResult> result =
            await dataSource.requestPairingRenotify();

        expect(
          result,
          const Right<Failure, app.PairingRenotifyResult>(
            app.PairingRenotifyResult(
              outcome: PairingRenotifyOutcome.alreadyIdle,
            ),
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

        final Either<Failure, app.PairingRenotifyResult> result =
            await dataSource.requestPairingRenotify();

        expect(
          result,
          const Left<Failure, app.PairingRenotifyResult>(
            NetworkFailure('socket failed'),
          ),
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

        final Either<Failure, app.PairingRenotifyResult> result =
            await dataSource.requestPairingRenotify();

        expect(
          result,
          const Left<Failure, app.PairingRenotifyResult>(
            NetworkFailure('bad reply'),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps an expired pairing outcome to a user-safe PairingFailure',
      () async {
        when(
          () => mockPairing.renotify(),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.expired));

        final Either<Failure, app.PairingRenotifyResult> result =
            await dataSource.requestPairingRenotify();

        expect(
          result,
          const Left<Failure, app.PairingRenotifyResult>(
            PairingFailure(
              'Pairing could not be completed. Please try again.',
              outcome: PairingFailureOutcome.expired,
            ),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps an invalid pairing outcome to a typed PairingFailure',
      () async {
        when(
          () => mockPairing.renotify(),
        ).thenThrow(const DovahLinkPairingException(PairingOutcome.invalid));

        final Either<Failure, app.PairingRenotifyResult> result =
            await dataSource.requestPairingRenotify();

        expect(
          result,
          const Left<Failure, app.PairingRenotifyResult>(
            PairingFailure(
              'Pairing could not be completed. Please try again.',
              outcome: PairingFailureOutcome.invalid,
            ),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps a pairing outcome outside the explicit message set to a generic PairingFailure',
      () async {
        // credentialIssued is a real PairingOutcome value, just never a valid reply to
        // pairing_renotify -- exercises the typed converter's defensive fallback arm the same
        // way an unrecognized wire value used to, before PairingOutcome became a closed enum.
        when(() => mockPairing.renotify()).thenThrow(
          const DovahLinkPairingException(PairingOutcome.credentialIssued),
        );

        final Either<Failure, app.PairingRenotifyResult> result =
            await dataSource.requestPairingRenotify();

        expect(
          result,
          const Left<Failure, app.PairingRenotifyResult>(
            PairingFailure('Pairing could not be completed. Please try again.'),
          ),
        );
      },
    );

    test(
      'Method requestPairingRenotify maps an unexpected exception to a user-safe PairingFailure',
      () async {
        when(() => mockPairing.renotify()).thenThrow(StateError('boom'));

        final Either<Failure, app.PairingRenotifyResult> result =
            await dataSource.requestPairingRenotify();

        expect(
          result,
          const Left<Failure, app.PairingRenotifyResult>(
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
            PairingFailure(
              'Pairing could not be completed. Please try again.',
              outcome: PairingFailureOutcome.expired,
            ),
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
      'Property connectionStatus does not claim restored when recovery ends connected but unpaired',
      () async {
        when(
          () => mockCurrentHost.trustState,
        ).thenReturn(DovahLinkTrustState.unpaired);
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
            PairingConnectionStatus.lost,
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
      'Property connectionStatus reads the SDK trust state at each connected event',
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

        connectionStates.add(DovahLinkConnectionState.connected);
        await pumpEventQueue();
        when(
          () => mockCurrentHost.trustState,
        ).thenReturn(DovahLinkTrustState.unpaired);
        connectionStates.add(DovahLinkConnectionState.connected);
        await pumpEventQueue();
        when(
          () => mockCurrentHost.trustState,
        ).thenReturn(DovahLinkTrustState.trusted);
        connectionStates.add(DovahLinkConnectionState.connected);
        await pumpEventQueue();

        expect(received, <PairingConnectionStatus>[
          PairingConnectionStatus.restored,
          PairingConnectionStatus.lost,
          PairingConnectionStatus.restored,
        ]);
      },
    );

    test(
      'Property connectionStatus does not claim restored when the SDK reports no current trust state',
      () async {
        when(() => mockCurrentHost.trustState).thenReturn(null);
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
