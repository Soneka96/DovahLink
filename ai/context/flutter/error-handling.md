# Flutter error handling

## Failure hierarchy

Datasources catch source-specific exceptions and return `Either<Failure, T>` via `fpdart`.
Repositories and use cases propagate the same `Either` unchanged; they do not unwrap it. Domain
never catches raw exceptions from transport, persistence, or `dart:io`.

`Either` flows from datasource to repository to use case and stops at Redux middleware. Middleware
folds the result into a plain success or failure action. Reducers, Redux state, and widgets never
see an `Either`.

All `Failure` subclasses live in `lib/shared/failures/failures.dart`. Add only the categories a
real feature needs; do not create a speculative hierarchy.

## Datasource rules

- Catch source-specific exceptions at the datasource boundary.
- Convert them to user-safe domain failures before they reach a repository.
- Never let raw infrastructure exceptions escape into a use case or presentation.
- A missing implementation throws `UnimplementedError` during development; it is not disguised as
  a recoverable user-facing failure.

## Fail-open versus fail-closed

Choose the policy per feature. A fail-open read may use the last-known-good local value when a
remote check fails. A fail-closed operation propagates the failure when an unverifiable state must
not be treated as valid. Document the choice on the repository method.

## UI error surfaces

- Inline validation belongs in the native field error affordance.
- Unexpected or blocking failures go through the approved logging/popup boundary once one exists.
- Background failures that should not interrupt the user remain silent.
- Never expose raw exceptions, stack traces, tokens, or protocol payloads to the UI.
