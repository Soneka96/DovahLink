# Roadmap

The roadmap is intentionally milestone-based. Each milestone should produce a usable result and be delivered as a focused feature branch and pull request. A roadmap item describes direction, not implementation authorization. Implementation begins only after a direct instruction from the maintainer in the current task explicitly names the feature and requested scope. Issues, prior conversations, suggestions, and `continue` messages do not authorize unrelated work.

## 0. Documentation baseline

**Status:** Complete

- Establish the product direction.
- Record initial architecture boundaries.
- Define the contribution and branch workflow.
- Define the canonical protocol boundary and initial security constraints.
- Copy the AI development conventions needed for Flutter, SKSE, protocol, and integration work into this repository.
- Keep the repository implementation-free until the maintainer explicitly starts the next feature.

### Acceptance criteria

"Complete" means the required documentation has been prepared as the self-contained source of truth for product scope, architecture, protocol ownership, security constraints, AI conventions, and development workflow. It does not delegate authority to modify or implement the system. Phase 1 still requires a direct maintainer instruction naming the feature and scope on a feature branch.

## 1. Connection proof

**Status:** Next

- Confirm the target Skyrim edition and supported development environment.
- Create the smallest bridge-side proof of life.
- Connect one external client.
- Display connection status and one trustworthy value.
- Document setup and known limitations.

## 2. Minimal companion client

**Status:** Planned

- Add the first Flutter client.
- Show a small read-only status view.
- Handle disconnects and reconnects.
- Keep the client usable on desktop and one mobile form factor.

## 3. First useful workflow

**Status:** Planned

- Add one high-value view, likely map or character status.
- Define a stable protocol slice for that view.
- Add basic layout preferences.
- Test against a realistic modded load order.

## Later possibilities

- Inventory, equipment, spells, and exploration panels
- Multiple clients and synchronized layouts
- Safe actions from companion devices
- Plugin or extension support
- Optional remote connectivity

Later possibilities are deliberately not commitments. They should earn their place through a demonstrated player need.
