# EfMcp — AGENTS.md

## Purpose

Engineering constraints and implementation guidance for AI coding agents contributing to EfMcp.

**First read [README.md](README.md)** for the project overview, quick start, and tool reference.

---

## GitHub

- **Repo:** `khurram-uworx/EfMcp`
- All `gh` commands require `--repo khurram-uworx/EfMcp`

---

## Domain Boundaries

Do not reinvent infrastructure. Prefer existing .NET and ecosystem primitives over custom solutions.

---

### ADR Style

ADRs (Architecture Decision Records) should describe **what** the system does at an architectural level and **why**, without pinning down implementation method names, parameter types, or class signatures that can drift during implementation. Include intent, constraints, and tradeoffs; leave concrete API surface to the code. If an ADR contradicts the implementation, update the ADR toward the abstract intent — the implementation is the source of truth for specifics.

---

## Task Format

Use `docs/TASKS-TEMPLATE.md` for new task breakdowns. Each task must include: Priority, Goal, Scope, Acceptance Criteria, Files Likely Involved.

---
