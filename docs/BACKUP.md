# Backup and restore

DocuArchive has no paid backup service and no scheduled backup job — the
free hosting tier would store the backup on the same disk that is wiped on
every redeploy, which protects nothing. Instead, a system administrator
downloads a complete backup on demand from the app, and keeps it somewhere
safe off the server.

## What a backup contains

Download from **Backup** in the sidebar (system administrators only) or
`GET /api/admin/backup/download`. The result is a ZIP named
`docuarchive-backup-YYYYMMDD-HHmmss.zip` holding:

| Entry | Contents |
| --- | --- |
| `database.sqlite` | A consistent snapshot of the whole database, taken with SQLite `VACUUM INTO` while the app keeps running. No documents, users, workspaces, tags, rules or boxes are left out. |
| `files/<storage key>` | Every uploaded document, stored under its original storage key, byte-for-byte. |
| `manifest.json` | `BackupVersion`, `Provider`, `GeneratedAtUtc`, and the database size and document/workspace/file counts, so a restore can be checked without opening the database. |

`GET /api/admin/backup/info` shows the same counts and sizes before you
download, so you can see what the backup would contain.

Every download is written to the audit log as `Backup.Downloaded`
(workspace: none — a backup spans all workspaces). Only a system
administrator can download one; other users get 403, and the Backup page
says so in plain language.

## When to download

- **Before every deploy, at least.** On the Render Free tier the database
  and the uploaded files live on ephemeral disk: each new deploy starts
  with a fresh, empty database. A backup that is still on the server is
  gone with the data it was meant to protect.
- After a large import or a day of heavy use, if you want to be safe.

## Retention

- Keep backups **off the server**: your own encrypted drive, or any
  storage you already trust. A backup holds every workspace's documents,
  so treat the ZIP like a password — encrypted storage, never emailed,
  never shared.
- Keep at least the last two or three downloads, so a corrupt or
  incomplete one is not your only copy.
- A backup is a point in time: anything uploaded after the download is
  not in it.

## Restore (SQLite)

There is deliberately no in-app restore endpoint — uploading a database
file to overwrite production data is too big a security surface for a
one-click button. Restoring is a manual, admin-only procedure:

1. Stop the app (on Render: suspend the service, or stop it from the
   dashboard).
2. Unzip the backup somewhere safe.
3. Replace the database file with `database.sqlite` from the backup:
   the live file is `/app/data/docuarchive.db` on Render
   (`ConnectionStrings__Default`), or `docuarchive.db` in the WebApi
   directory locally.
4. Replace the storage folder with the backup's `files/` contents:
   `/app/data/storage` on Render (`Storage__RootPath`), or `storage/`
   locally. The folder must contain the files themselves, not a nested
   `files/` folder.
5. Start the app. Database migrations run automatically at startup, so
   a backup from an older version is migrated forward — but a backup from
   a **newer** version than the code being deployed will not migrate
   backwards. Restore onto the same (or newer) code version.
6. Check the manifest counts against the live dashboard, and spot-check
   that a document downloads and its OCR text searches.

## PostgreSQL deployments

In-app backup is SQLite-only. On PostgreSQL, `GET /api/admin/backup/info`
still reports the counts, but the download answers **501** with a message
pointing here, and the Backup page shows the same guidance. Take database
backups with `pg_dump` (or your database provider's backup feature) and
copy the storage folder separately:

```sh
pg_dump --format=custom --file=docuarchive-backup.dump "$DATABASE_URL"
# plus a copy of the storage folder (Storage__RootPath)
```

Restore with `pg_restore` into an empty database, then start the app —
migrations run at startup as above.

## What is not backed up

- Configuration and secrets (environment variables, JWT key, admin
  password) — keep those in the hosting dashboard, never in a backup.
- The application code itself — that lives in Git.
