# Flutter error handling

## Failure hierarchy

Datasources catch source-specific exceptions and return `Either<Failure, T>` via `fpdart`.
Repositories and use cases propagate the same `Either` unchanged; they do not unwrap it. Domain
never catches raw exceptions from transport, persistence, or `dart:io`.

`Either` flows from datasource to repository to use case and stops at Redux middleware. Middleware
folds the result into a plain success or failure action. Reducers, Redux state, and widgets never
see an `Either`. A complete SDK-owned operation may instead be called directly by middleware using
its registered SDK contract; middleware carries its typed result or exception into a typed Redux
action without converting it to user-facing text.

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

- User-visible error, disconnected, stale, and recovery states expose typed, user-safe status
  models or localized messages.
- Widgets own selecting and rendering user-visible error copy. Middleware, datasources,
  repositories, use cases, selectors, and ViewModels must carry typed failure information without
  constructing, inspecting, or reformatting display text.
- Middleware may pass the original typed error object through Redux without reading its message. A
  widget may use its type to choose safe copy, with a generic fallback for unknown types; never
  display diagnostic `.message`/`toString()` text, stack traces, tokens, or protocol payloads.
- Inline validation belongs in the native field error affordance.
- Unexpected or blocking failures go through the approved logging/popup boundary once one exists.
- Background failures that should not interrupt the user remain silent.
- Never expose generic raw exceptions, stack traces, tokens, or protocol payloads to the UI.
