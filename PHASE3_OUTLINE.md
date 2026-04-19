# Phase 3 Outline

## Purpose

Phase 3 is the product-workflow phase.

Phase 1 made the worker reliable.
Phase 2 made the drafting decision layer cleaner and more product-ready.
Phase 3 should turn that into a user-manageable product surface.

The goal is to move from:

- a solid run-once CLI copilot

to:

- a system with clear user controls
- review and selection workflow
- policy management
- foundations for team/shared-mailbox/business usage

## Phase 3 Focus

Phase 3 should focus on the control surface around the core engine, not on rewriting the core engine again.

Main themes:

- settings and policy management
- review and selection workflow
- confidence and approval model
- user feedback capture
- clean path toward team/shared mailbox / CRM modes

## What Phase 3 Should Include

### 1. User-facing settings / policy layer

Users should be able to manage:

- reply scope
- exclusion rules
- later allow rules
- configurable built-in policies

The important product distinction should become explicit:

- locked built-in safety policies
- configurable built-in default policies
- mailbox-specific user rules

Examples:

- skip review reminders
- skip job-match emails
- skip policy/privacy updates
- draft only from allowed domains

### 2. Review workflow for draft sets

The system should move beyond “a row exists in SQLite” toward a real review flow.

Users should be able to:

- see pending draft sets
- inspect one or more variants
- choose a preferred variant
- dismiss all variants
- optionally edit a selected variant

This should make `DraftSetStatus` and variant metadata more meaningful in practice.

### 3. Confidence and approval model

Phase 3 should introduce clearer workflow states around draft confidence.

Likely concepts:

- `SuggestOnly`
- `ReviewBeforeSend`
- later `AutoSend` only if confidence and controls are strong enough

Phase 3 should likely stop at:

- suggest
- review
- approve

Auto-send should stay out of scope until later.

### 4. Feedback capture

Once users can review variants, the system should record what they actually do.

Useful future signals:

- selected variant
- dismissed draft set
- edited before send
- amount of edit distance

This should eventually inform:

- better reply-shape calibration
- better ambiguity thresholds
- better confidence heuristics

### 5. Better policy model for built-ins

Built-in rules should eventually carry metadata such as:

- `RuleId`
- `DisplayName`
- `Description`
- `Category`
- `DefaultEnabled`
- `IsUserConfigurable`

That allows a UI to show product policies instead of forcing users to understand code or raw JSON patterns.

### 6. Shared mailbox / business foundations

Phase 3 does not need to fully implement shared mailbox or CRM workflows, but it should stop blocking them.

Good foundations:

- clearer draft ownership concepts
- room for `AssignedToUserId`
- better status transitions
- cleaner audit trail

## What Phase 3 Does Not Need Yet

Not necessary yet:

- CRM entity mapping
- pipeline-stage-aware business automation
- multi-user style models
- automatic sending without review
- user-defined reply-shape types

Those are later concerns.

## Suggested Implementation Order

1. Add a thin user-facing presenter / review contract for `DraftSet`
2. Make `DraftSetStatus` and variant selection workflow real
3. Add policy metadata for built-in rules
4. Introduce configurable built-in policy toggles
5. Add feedback capture for selected / dismissed / edited variants
6. Revisit confidence and approval workflow once the above exists

## Definition of Done for Phase 3

Phase 3 is complete when:

- users can understand and manage reply policy without touching code
- users can review and choose between draft variants in a clear workflow
- selected / dismissed / edited outcomes are captured for future tuning
- built-in policies are visible and configurable where appropriate
- the architecture is ready for a later UI/team/business layer without another foundational rewrite

## Relationship To Phase 2

Phase 2 built the engine for:

- draft sets
- reply shapes
- ambiguity-aware drafting
- reply scope

Phase 3 should build the user-control layer on top of that engine.

It should not undo the phase-2 separation of:

- skip assessment
- draft eligibility
- reply-shape planning
- draft generation
