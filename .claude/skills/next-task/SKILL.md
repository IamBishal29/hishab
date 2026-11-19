---
name: next-task
description: Pick up the next Hishab backlog issue and take it through plan, build, test, pull request and a learning note.
---

1. Read PROGRESS.md.
   - A branch is in progress: switch to it and resume from "Next step".
   - Otherwise: `git switch main && git pull`, then pick the lowest-numbered open issue
     in the current milestone (GitHub MCP, or `gh issue list --milestone "<name>"`).
   Tell me in one line which issue and why.
2. Explain the concept first, in a few lines with a real-world analogy (and a tiny
   diagram if it helps). Then plan in under 10 lines: files to touch, approach, and the
   failure mode it protects against. If there is a real decision (see CLAUDE.md
   "When to ask me"), ask with AskUserQuestion. Otherwise continue without waiting.
3. Create the branch, implement in small commits, add or update tests (unit, plus
   integration when the change touches the database, HTTP or a courier adapter; a
   cross-tenant test for every new tenant-owned entity).
4. Run until all pass: `dotnet format --verify-no-changes`, `dotnet build`,
   `dotnet test` in `src/api`; and lint, typecheck, test, build in `src/web` when the
   web app changed. Start infra with `docker compose up -d` when needed.
5. While implementing, explain each meaningful step in chat in 2-4 lines: what you did
   and the engineering reason (not the syntax).
6. Write `docs/learning/NN-topic.md` for the concept and commit it on the same branch.
7. Push and open the PR (Closes #n, summary, screenshots for UI, how to test, review
   notes).
8. Update PROGRESS.md (done, next issue, open questions for me), commit, push.
9. Finish with: the PR link, a 5-line concept summary, the interview questions this
   issue prepares me for, and what I should check in "Files changed" before merging.
