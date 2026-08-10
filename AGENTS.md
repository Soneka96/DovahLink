# DovahLink — AI development instructions

DovahLink is a maintainer-owned project developed with AI assistance. The maintainer owns the product direction, architecture, protocol decisions, and final approval of every change.

## Read first

- `README.md` — project overview and current status
- `PRODUCT.md` — product boundaries and principles
- `ARCHITECTURE.md` — system boundaries and technical direction
- `ROADMAP.md` — milestone order and deferred work
- `CONTRIBUTING.md` — branch and pull request workflow for maintainer-led, AI-assisted development
- `ai/context/common.md` — rules shared by every part of the product
- `ai/context/flutter/` — Flutter client conventions; read for client work
- `ai/context/skse/` — native Skyrim bridge conventions; read for SKSE work

## Non-negotiable rules

- Do not add Flutter, Skyrim bridge, networking, or protocol implementation until the maintainer explicitly requests that feature.
- Do not invent folders, layers, abstractions, packages, or services for hypothetical future work.
- Do not work around, replace, or significantly alter a documented architectural decision without asking the maintainer first.
- Do not broaden a feature beyond the requested scope.
- Do not modify unrelated files or perform opportunistic cleanup.
- Do not treat a technically working alternative as automatically acceptable; it must fit the project's documented direction and existing conventions.
- Keep `main` stable and use one feature branch and one pull request per feature.
- Update the relevant documentation when a product, architecture, roadmap, or workflow decision changes.

## How AI should work

1. Read the relevant project documents before proposing or changing a design.
2. Inspect existing code and the conventions documented in this repository before creating new patterns.
3. Prefer the smallest complete change that fits the existing architecture.
4. Explain important trade-offs when a decision is not already documented.
5. Add focused tests for non-trivial behavior once implementation begins.
6. Check the diff for unrelated changes before presenting the work.
7. Leave `main` clean and summarize the branch, checks, and remaining limitations.

## Architecture direction

The first implementation milestone is only a reliable connection between Skyrim and one external client. The bridge, protocol, and client boundaries should remain explicit and independently replaceable.

The first client should be read-only. Do not add remote gameplay actions, hosted services, accounts, plugin systems, or multi-client abstractions unless the maintainer adds them to the roadmap.

When Flutter implementation begins, use the Price check conventions that have been deliberately copied and adapted into `ai/context/flutter/`. The copied repository must remain self-contained: never depend on another local project, an absolute path, or instructions that are unavailable here.

Only rules documented in this repository are binding for DovahLink. If a Price check convention has not yet been copied here, treat it as a proposal to discuss rather than an external requirement.

## Decision authority

When sources conflict, follow this order:

1. The maintainer's current request
2. Decisions documented in this repository
3. Conventions explicitly documented in this repository
4. Framework and package defaults

If the correct choice is still unclear, stop and ask the maintainer rather than making a structural assumption.
