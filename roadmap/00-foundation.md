# Stage 0 — Foundation

[Back to the roadmap index](../ROADMAP.md). [Next stage](./01-skyrim-bridge-foundation.md)

## 0. Documentation baseline

**Status:** Complete

### Outcome

DovahLink has a self-contained source of truth for product scope, architecture, protocol ownership, security constraints, development conventions, and maintainer authority before implementation begins.

### Scope and behavior

- Establish the product direction and initial architecture boundaries.
- Define the canonical protocol boundary and initial security constraints.
- Define the contribution, branch, review, and maintainer-approval workflow.
- Copy the AI development conventions needed for Flutter, SKSE, protocol, and integration work into this repository.
- Keep the repository implementation-free until the maintainer explicitly starts the first implementation phase.

### Dependencies and boundaries

This phase has no implementation dependency. It establishes direction and authority but does not approve or pre-create Flutter, bridge, networking, protocol, or feature code.

### Acceptance criteria

The required documentation exists in the repository, agrees on ownership and safety boundaries, and does not depend on instructions from another local project. Later phases still require direct maintainer authorization.

## 0.5 Client and Protocol Foundation

**Status:** Complete

### Outcome

DovahLink has the smallest replaceable Flutter and protocol-facing foundation needed to begin
bridge integration without mixing client structure with transport implementation.

### Scope and behavior

- Establish the Flutter client shell and manual dependency-injection boundary.
- Generate and validate the first protocol-facing client models.
- Define connection domain entities, repository contracts, and use cases.
- Define Redux connection state, actions, reducers, selectors, and a read-only status screen.
- Establish client conventions, fixtures, generated-code rules, and test coverage for this
  foundation.

### Dependencies and boundaries

This foundation does not implement Skyrim integration, a transport, pairing, reconnection, or an
external validation client. It prepares those boundaries without claiming that a connection works.

### Acceptance criteria

The Flutter project analyzes cleanly, its foundation tests pass, protocol models map the approved
fixtures, and the client can render explicit disconnected and connection-error states without
real transport access.
