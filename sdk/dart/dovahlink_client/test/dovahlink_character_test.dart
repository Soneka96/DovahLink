import 'package:test/test.dart';

import 'package:dovahlink_client_sdk/dovahlink_client.dart';
import 'package:dovahlink_client_sdk/src/dovahlink_character.dart'
    show DovahLinkCharacter;

/// Tests that the Character facade exposes its domain tracker streams.
void main() {
  late Stream<StateSynchronization<CharacterVitalsState>> vitalsChanges;
  late Stream<StateSynchronization<CharacterXpState>> xpChanges;
  late Stream<StateSynchronization<CharacterLevelState>> levelChanges;
  late Stream<StateSynchronization<CharacterIdentityState?>> identityChanges;
  late Stream<StateSynchronization<CharacterSupernaturalTraitsState?>>
  supernaturalTraitsChanges;
  late DovahLinkCharacter character;

  setUp(() {
    vitalsChanges =
        const Stream<StateSynchronization<CharacterVitalsState>>.empty();
    xpChanges = const Stream<StateSynchronization<CharacterXpState>>.empty();
    levelChanges =
        const Stream<StateSynchronization<CharacterLevelState>>.empty();
    identityChanges =
        const Stream<StateSynchronization<CharacterIdentityState?>>.empty();
    supernaturalTraitsChanges =
        const Stream<
          StateSynchronization<CharacterSupernaturalTraitsState?>
        >.empty();
    character = DovahLinkCharacter(
      vitalsChanges: vitalsChanges,
      xpChanges: xpChanges,
      levelChanges: levelChanges,
      identityChanges: identityChanges,
      supernaturalTraitsChanges: supernaturalTraitsChanges,
    );
  });

  group('Property vitalsChanges behaves correctly', () {
    test('Property vitalsChanges returns the Vitals tracker stream', () {
      expect(character.vitalsChanges, same(vitalsChanges));
    });
  });

  group('Property xpChanges behaves correctly', () {
    test('Property xpChanges returns the XP tracker stream', () {
      expect(character.xpChanges, same(xpChanges));
    });
  });

  group('Property levelChanges behaves correctly', () {
    test('Property levelChanges returns the Level tracker stream', () {
      expect(character.levelChanges, same(levelChanges));
    });
  });

  group('Property identityChanges behaves correctly', () {
    test('Property identityChanges returns the Identity tracker stream', () {
      expect(character.identityChanges, same(identityChanges));
    });
  });

  group('Property supernaturalTraitsChanges behaves correctly', () {
    test(
      'Property supernaturalTraitsChanges returns the supernatural-traits tracker stream',
      () {
        expect(
          character.supernaturalTraitsChanges,
          same(supernaturalTraitsChanges),
        );
      },
    );
  });
}
