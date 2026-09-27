# Roadmap Deviations

A **Roadmap Deviation** is intentional work performed outside the normal ordered roadmap because a
newly discovered architectural, product, UX, security, or development concern should be addressed
before or alongside normal progression.

## Why deviations exist

The roadmap orders product delivery deliberately, but development can uncover work that should
happen earlier. Recording that work separately preserves what happened without silently rewriting
roadmap history.

## Rules

A deviation record should state:

- why work left the normal roadmap and where it started;
- its scope, important decisions, and what it does not change;
- its relationship to the main roadmap;
- when or how normal roadmap work resumes;
- links to authoritative architecture, security, or product documents; and
- whether the deviation is active, complete, paused, or transferred elsewhere.

A deviation does not replace the main roadmap, renumber completed stages, or automatically complete
a stage. It may pause normal progression or pull future work forward, but it must preserve the
historical reasoning and remain a record of the detour rather than a second competing roadmap.

The main roadmap answers **“What is the normal product delivery order?”** Deviation documents answer
**“Why did development intentionally leave that order, and what happened while it did?”**

## Deviations

- [Prototype → Flutter Convergence](prototype-flutter-convergence/README.md) — staged work to align
  the production Flutter client with the approved prototype while preserving SDK/domain authority
  over real connection and pairing semantics.
- [Initial Pairing Security Investigation and Extraction](initial-pairing-security/README.md)
