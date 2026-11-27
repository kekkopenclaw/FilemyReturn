# Monitoring

DocuArchive has no paid monitoring service and no external uptime checker —
that would need a third-party account (e.g. UptimeRobot) and this project
stays free with no accounts beyond the ones Francesco already has. Instead,
a system administrator opens **Monitoring** in the sidebar and sees a
read-only health snapshot of the running app, right now.

## What it shows

`GET /api/admin/monitoring` (system administrators only) returns:

| Field | Meaning |
| --- | --- |
| `uptimeSeconds` | How long this process has been running (time since it started). A short uptime after a deploy is normal; a short uptime at any other time means the process restarted. |
| `version` | App version from the assembly informational version. |
| `commit` | The deployed git SHA, from Render's `RENDER_GIT_COMMIT` env var. Null on a local run, where no deployed commit exists — never invented. |
| `databaseProvider`, `databaseSizeBytes` | `Sqlite` or `Postgres`, and the live database size. |
| `userCount`, `workspaceCount`, `documentCount` | System-wide totals. |
| `documentsByStatus` | Documents split by processing status (`uploaded`, `processing`, `ready`, `failed`), by OCR status (`ocrQueued`, `ocrProcessing`, `ocrFailed`), and `needsReview` — the human-review queue depth. |
| `fileCount`, `storageSizeBytes` | Stored files and their total size. |
| `failedLoginsLast24h` | Failed logins for real accounts in the last 24 hours (audit action `Auth.LoginFailed`). A spike here means someone is guessing passwords — check the audit log. Failed logins for unknown emails are not counted (they are not audited, so account existence is never leaked). |
| `lastBackupAtUtc`, `lastBackupDetails` | When the last backup was downloaded (audit action `Backup.Downloaded`) and what it contained. Null when no backup has ever been downloaded — download one from the Backup page; see `BACKUP.md`. |

The page has a manual **Refresh** button: the numbers are a point in time,
so refresh to see the latest. There is no polling.

## Who can see it

Only system administrators. The endpoint requires the `SystemAdmin` role
claim and re-checks the flag against the database (same pattern as the
backup endpoints), so a user demoted after login loses access immediately.
Other users get 403, and the Monitoring page says so in plain language;
anonymous callers get 401.

The response carries no secrets and no per-user or per-workspace detail —
only system-wide aggregates. It cannot be used to read anyone's documents.

## What it does NOT do

- **No external uptime pings.** Nothing outside the app checks that the site
  answers — if the process is down, this page is down with it. An external
  checker (e.g. UptimeRobot) would need Francesco's account on that service.
- **No alerts.** Nothing emails or messages anyone when a number looks
  wrong. Check the page after a deploy and when something feels off.
- **No history.** Each snapshot is the present moment only; nothing is
  stored. Trends (growing storage, rising failed logins) are visible only
  by comparing snapshots, or in the audit log.
- **No per-workspace or per-user breakdown.** An admin sees totals, not
  whose documents are where — workspace isolation still applies everywhere
  else in the app.

## What to look for

- **OCR queue stuck:** `ocrQueued` stays above 0 for a long time, or
  `ocrFailed` climbs — the worker or the OCR engine needs attention
  (see `OCR.md`).
- **Review queue growing:** `needsReview` climbs — documents are waiting
  for a human to confirm their category on the Documents page.
- **Failed logins spike:** investigate the audit log; consider whether the
  account under attack needs a password change.
- **No backup:** `lastBackupAtUtc` is null or old — download a backup
  (see `BACKUP.md`) before the next deploy.
- **Storage growing fast:** the Render Free tier keeps files on ephemeral
  disk; a backup is the only copy that survives a redeploy.
