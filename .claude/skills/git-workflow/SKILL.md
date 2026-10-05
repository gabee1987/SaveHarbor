---
name: git-workflow
description: SaveHarbor git branching rules and the hard ban on agent-created commits and pull requests. Use before any git operation (branch, stage, commit, push, PR) in this repository.
---

# Git Workflow (SaveHarbor)

## Hard rules

1. **Never create commits.** Do not run `git commit`, `git commit --amend`, `git merge`,
   `git rebase`, `git cherry-pick`, or `git stash` without an explicit, per-request instruction.
   The owner commits manually.
2. **Never open, update, or merge pull requests.** No `gh pr create`, `gh pr merge`, or similar.
   The owner opens PRs manually.
3. **Never push.** No `git push` of any kind.
4. Never run destructive commands (`git reset --hard`, `git checkout -- <path>`,
   `git clean -fd`, `git branch -D`) unless the owner explicitly asks and the target is verified.
5. Staging (`git add`) is also left to the owner unless explicitly requested.

Allowed without asking: `git status`, `git diff`, `git log`, `git branch`, `git show`,
and `git switch -c <new-branch>` when a task needs a new branch.

## Branch naming

| Prefix      | Use for                                                    | Example                         |
|-------------|------------------------------------------------------------|---------------------------------|
| `feature/`  | New user-facing capability                                 | `feature/dragonwilds-support`   |
| `fix/`      | Bugs, defects, issues                                      | `fix/session-lock-heartbeat`    |
| `chore/`    | Everything else: deps, refactors, tooling, docs, skills    | `chore/dotnet10-upgrade`        |

- Lowercase kebab-case after the prefix. Short and descriptive.
- Branch from `develop` (the integration branch). `main` is release-only; never target it directly.
- Large features may use sub-branches off the feature branch, e.g.
  `feature/dragonwilds-support` → `chore/dotnet10-upgrade` (merged back into the feature branch by the owner).

## Current long-running branch

- `feature/dragonwilds-support` — Dragonwilds support, per-game separation, launch-flow UX,
  dependency upgrade. Plans live in `docs/plans/dragonwilds/`.

## End-of-task report

When work is done, state the branch name and list changed files so the owner can review,
stage, and commit. Suggest a commit message only if asked.
