# DocuArchive Working Rules

Set by Francesco, 2026-11-27. These rules are binding on every builder, agent and scheduled run working on this project. They exist because a burst of untested pushes failed CI repeatedly and flooded his inbox with failure emails. That must never happen again.

## 1. Test before commit — no exceptions
Nothing is committed or pushed until it is fully tested and production-ready:
- `dotnet build` Release: 0 errors, 0 warnings.
- `dotnet test`: unit tests AND integration tests, with exact pass/fail counts recorded. If the local runner cannot run them, say so plainly and use CI as the second proof — never invent a pass.
- `ng build`: exit 0.
- Security check (see rule 4) passes.
If any check fails, fix it first. Never push to "see if CI passes".

## 2. The pipeline never fails
- One finished piece of work = one commit = one CI run.
- No burst pushes, no intermediate or half-finished commits, no pushing a fix on top of a fix within minutes.
- If CI fails on a commit, stop. Get the exact failing job and step from the run, fix the cause locally, verify the fix locally, and only then push once.
- Never retry the same deploy unchanged after a failure.

## 3. One person only
- A single senior builder works on the code at a time: very senior, many years of experience, fast, security expert — a master of everything web, the omni king of coding: frontend, backend, design, DevOps, testing, security, the full stack.
- No parallel builders, no army on the same code. The 10-minute loop must never start a second builder while one is running — check first, wait for its handoff.

## 4. Security first
Every push passes a security review before it goes out:
- No hardcoded passwords, keys, tokens or secrets anywhere in the code or repo.
- ADMIN_PASSWORD and the JWT key come from environment variables only; the dev placeholder is never used in Production.
- Workspace isolation verified (a user can only see their own workspaces).
- Every dependency checked against its official source; free and open-source only, no paid services, no paid GitHub features.

## 5. Live only when verified
A deploy goes live only after CI is green AND the live site is checked end to end: /health and /health/ready return 200, the landing page loads, login works, and the feature being shipped works on the live site. Report to Francesco only when the live link is verified, or when blocked by something only he can do.
