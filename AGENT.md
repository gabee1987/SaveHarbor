# Agent Coding Rules

This file defines the general coding and behavior rules for AI agents working in
this repository. App-specific product, architecture, domain, and UI rules live in
the project skills under `.claude/skills/`; read and follow the relevant skills
before making app-specific changes.

These guidelines bias toward caution and maintainability over speed. For trivial
tasks, use judgment, but do not ignore the principles.

## 1. Think Before Coding

Do not assume. Do not hide confusion. Surface tradeoffs clearly.

Before implementing:

- State assumptions explicitly when they affect the solution.
- If multiple interpretations exist, present them instead of silently choosing.
- If a simpler approach exists, say so.
- Push back when a request would make the code harder to maintain, slower, less
  safe, or inconsistent with the project skills.
- If something important is unclear, stop, name what is confusing, and ask.

## 2. Simplicity First

Write the minimum code that solves the actual problem. Do not add speculative
features.

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or configurability that was not requested.
- No error handling for impossible internal states.
- No clever code when clear code works.
- If a solution is 200 lines and could be 50, rewrite it.
- Ask: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Clean Code Standards

Clean code here means readable, maintainable, simple, and effective.

- Follow SOLID principles where they help clarity and maintainability.
- Keep functions focused on one job.
- Break complex functions into named helper functions, services, or modules.
- Prefer explicit names over comments.
- Comment only non-obvious why, not what the code mechanically does.
- Avoid long nested conditionals, nested ternaries, and dense inline logic.
- Keep dependencies flowing in obvious directions.
- Prefer composition over large multipurpose objects.
- Make invalid states hard to represent through types and structure.
- Keep public APIs small and intentional.

## 4. File And Module Size

Do not create giant files. Split by responsibility before files become hard to
review.

- Avoid 700+ line files. A file approaching that size must be split into logical,
  structured modules unless there is a very strong reason.
- Prefer smaller files organized by responsibility, not arbitrary line count.
- Extract helpers when they make the main flow easier to read.
- Extract services when behavior has state, orchestration, IO, or multiple public
  operations.
- Extract pure utilities when logic is reusable and independent.
- Do not create many tiny files that obscure the flow. Structure should improve
  navigation.

## 5. Surgical Changes

Touch only what the task requires. Clean up only your own mess.

When editing existing code:

- Do not "improve" adjacent code, comments, naming, or formatting.
- Do not refactor things that are not part of the request.
- Match the existing local style, even if you would choose differently.
- If you notice unrelated dead code or design issues, mention them instead of
  deleting them.
- Remove imports, variables, functions, files, and tests that your own changes
  made unused.
- Do not remove pre-existing dead code unless asked.
- Every changed line should trace directly to the user's request.

## 6. Performance Is Key

Prefer efficient code, especially in UI rendering paths, realtime paths, loops,
and frequently called helpers.

- Avoid unnecessary renders, allocations, deep clones, polling, timers, and
  repeated expensive computations.
- Keep hot paths simple and measurable.
- Do not trade clear correctness for micro-optimizations.
- Avoid adding dependencies for small logic that can be written clearly.
- Consider mobile performance and battery impact for frontend work.
- Consider network, serialization, and broadcast costs for realtime/server work.
- Use memoization or caching only when there is a real repeated cost and clear
  invalidation.

## 7. Goal-Driven Execution

Define success criteria before changing code. Loop until the work is verified or
you can clearly explain why verification was not possible.

Transform tasks into verifiable goals:

- "Add validation" means write or update checks for invalid inputs, then make
  them pass.
- "Fix the bug" means reproduce the bug with a test or clear manual check, then
  make it pass.
- "Refactor X" means preserve behavior and ensure relevant tests pass before and
  after when practical.

For multi-step tasks, state a brief plan:

1. Change the smallest responsible area. Verify with the narrowest relevant
   check.
2. Update affected callers, contracts, or tests. Verify affected boundaries.
3. Run broader validation only when the blast radius warrants it.

Strong success criteria allow independent progress. Weak criteria such as "make
it work" require clarification.

## 8. Testing And Verification

Verification is part of the task, not an optional finish.

- Prefer focused tests for changed behavior.
- Use existing test patterns and tools.
- Do not use logs as a substitute for tests.
- Run the narrowest command that proves the change.
- Broaden verification when shared contracts, architecture boundaries, or
  user-facing flows changed.
- **Phase completion is the exception to narrow verification:** before declaring a phase
  implementation finished, run the complete local gate defined in the `build-and-verify`
  skill (full solution build and all tests). Do not mark the phase complete while any
  required gate is unrun or failing.
- If tests cannot be run, state exactly why and what remains unverified.

## 9. Project-Specific Rules

For product behavior, architecture boundaries, game rules, design system,
frontend/backend ownership, and workflow details:

- Read the relevant skills in `.claude/skills/` and the plans in `docs/plans/`.
- Follow them.
- If this file and a skill appear to conflict, prefer the stricter rule and
  call out the conflict.
- If the user asks to change app behavior in a way that conflicts with
  a skill, ask before implementing.

## 10. Git And Workspace Safety

- Assume the working tree may contain user changes.
- Never push directly to `main`. Publish a feature branch and merge it only through a pull
  request.
- Keep Husky enabled so the repository's tracked push guard is active.
- Do not revert, overwrite, or reformat unrelated changes.
- Do not run destructive commands such as `git reset --hard`, `git checkout --`,
  recursive deletion, or mass moves unless the user explicitly asks and target
  paths are verified.
- Prefer `rg` and `rg --files` for search.
- Prefer `apply_patch` for manual edits.
- Keep dependency changes intentional.

## 11. Final Response Requirements

When finishing work, report:

- what changed,
- where it changed,
- what verification ran,
- any verification that could not be run,
- any meaningful remaining risk or follow-up.

Be concise. Do not claim success without verification or without clearly stating
what was not verified.
