# Phase 2 Scope

## Purpose

Phase 1 got the CLI worker to a solid product baseline:

- unread email ingestion works
- skip vs draft classification is stable enough
- style learning is adaptive rather than hardcoded
- run records and regression coverage exist
- recent failures are mostly product-policy edge cases, not structural instability

Phase 2 should build on that without losing the original goal of turning the CLI into a clean product core.

## Original Phase 2 Intent

Before the newer multi-variant discussion, phase 2 was meant to focus on productization:

- move from a good CLI prototype toward a general product core
- make exclusions and preferences more user-manageable
- improve decision transparency and confidence signals
- prepare for a later UI without another architecture cleanup pass

That original intent still stands.

## Updated Phase 2 Focus

Phase 2 now has a clearer centerpiece:

- add lightweight reply-shape planning
- support one best draft for clear cases
- support 2-3 shape-distinct draft variants for ambiguous cases
- keep the system compatible with future personal, team, and CRM/business modes

Important constraint:

- variants must differ in strategy/shape, not just wording

## What Phase 2 Includes

### 1. DraftSet / DraftVariant model

Replace the current single-draft persistence shape with:

- `DraftSet` as the top-level unit
- `DraftVariant` as child records

Even a simple email should still be represented as a draft set with one variant.

### 2. Reply-shape planning

Introduce a lightweight reply-shape planning stage that:

- returns one reply shape for clear emails
- returns 2-3 viable reply shapes for ambiguous emails

Initial supported reply shapes should stay small and practical:

- `DirectAnswer`
- `Acknowledge`
- `AcknowledgeAndAsk`
- `ConfirmAndClose`
- `ConfirmAndRequest`
- `Decline`

### 3. Variant-aware generation

Generation should take:

- incoming email
- style profile
- resolved reply shape

Prompt behavior should differ by reply shape at the structure level, not only by wording.

### 4. Reply scope safety gate

Phase 2 also includes an optional reply-scope gate:

- default mode: `All`
- optional mode: `OnlyAllowedDomains`

If enabled, drafting is limited to configured sender domains. Out-of-scope mail is recorded as `DRAFT_INELIGIBLE` rather than treated as a built-in skip.

### 5. CLI presentation

CLI output should surface:

- whether the email produced one draft or a set
- plain-English labels for each variant
- enough metadata to understand why variants exist

### 6. Product-facing foundations

Phase 2 should also preserve the original productization goal by making room for:

- user-managed exclusions/preferences
- future confidence-based routing
- future approval workflows
- future UI presentation

## What Phase 2 Does Not Include

Not in scope yet:

- auto-send
- shared mailbox assignment workflows
- CRM entity mapping
- pipeline-stage-aware business logic
- user-defined reply shape types
- multi-user/team style models

Those are later-phase concerns.

## Implementation Order

1. Introduce `DraftSet` and `DraftVariant` models and persistence.
2. Keep the current single-draft path working as a one-variant set.
3. Add reply-shape models and a lightweight planner.
4. Make generation reply-shape-aware.
5. Add optional reply-scope configuration.
6. Update CLI display/output and tests.
7. Revisit configured exclusions/preferences after the new draft flow is stable.

## Definition of Done for Phase 2

Phase 2 is complete when:

- the system persists draft sets rather than only single draft rows
- clear emails still yield one sensible variant
- ambiguous emails can yield 2-3 shape-distinct variants
- tests cover both single-shape and multi-shape paths
- optional reply-scope settings work without changing the default behavior
- the architecture is still clean enough to support a later UI without another foundational rewrite

## Current Implementation Note

This is still a run-once CLI worker:

- each run scans candidate windows until it finds the first actionable email
- once a draft set is created, the worker exits

That behavior is intentional for the current phase and is not a bug.
