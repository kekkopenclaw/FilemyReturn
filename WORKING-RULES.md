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

## 3. Team: one owner per area, no conflicts
- Francesco's rule (2026-11-27): more than one builder is fine ONLY if they truly work without conflict. Each specialist owns separate files and areas — no two builders on the same files at the same time.
- The senior builder (master of everything web) owns the live code path and deploys.
- Specialists work in their own areas and push only their own files: security audit (backend + config), testing (test files + E2E), design (preview concepts, never the live landing files while the senior builder owns them).
- Pushes still go one at a time, fully tested, one finished piece = one commit = one CI run. Never push another builder's files, never push in bursts.

## 4. Security first
Every push passes a security review before it goes out:
- No hardcoded passwords, keys, tokens or secrets anywhere in the code or repo.
- ADMIN_PASSWORD and the JWT key come from environment variables only; the dev placeholder is never used in Production.
- Workspace isolation verified (a user can only see their own workspaces).
- Every dependency checked against its official source; free and open-source only, no paid services, no paid GitHub features.

## 5. Live only when verified
A deploy goes live only after CI is green AND the live site is checked end to end: /health and /health/ready return 200, the landing page loads, login works, and the feature being shipped works on the live site. Report to Francesco only when the live link is verified, or when blocked by something only he can do.

## 6. No flood — protect Francesco's inbox (added 2026-11-27 02:29, after a second flood)
GitHub emails Francesco once per failed run. A burst of pushes that fails is a burst of emails. These rules stop that:
- **One push at a time, across the whole team.** Before pushing, check the latest CI run on main. If it is red OR still running, WAIT — do not push.
- **If any run on main is red, NOBODY pushes anything** until it is green again. The person whose commit failed owns the fix.
- **If your commit fails CI, you are grounded.** No further pushes from you until you have the exact failing job and step, fixed the cause locally, verified locally, and the parent has said go.
- **Never push while another builder is pushing.** Pushes are serialised through the parent: one finished piece at a time, then wait for green.
- The push helper makes one commit per file, so pushing many files creates many CI runs. Never push a large file set while CI is still running on your earlier files — wait for green between pushes.
- **Dependent files must be pushed with `gh.py commit` (atomic), not `snapshot`.** `snapshot` creates one commit per file, so dependent files (e.g. `package.json` + `package-lock.json`) land in separate commits and the intermediate commit is broken by construction — that caused CI failures and emails in runs 64 and 80. `gh.py commit` creates ONE commit for the whole batch via the Git Data API, so one finished piece = one commit = one CI run. Use `snapshot`/`file-put` only for single, independent files.
- The 10-minute loop checks CI status first: if the last run is red or in progress, that run does no push work.
