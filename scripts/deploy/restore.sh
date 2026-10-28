#!/usr/bin/env bash
set -euo pipefail
umask 077
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
DEPLOY_ENV_FILE="$DEPLOY_REPO_DIR/.env"; DEPLOY_PROJECT=''; backup=''; confirmed=''
while (($#)); do
    case "$1" in
        --env-file) DEPLOY_ENV_FILE="${2:?}"; shift 2;;
        --project) DEPLOY_PROJECT="${2:?}"; shift 2;;
        --backup) backup="${2:?}"; shift 2;;
        --confirm-database) confirmed="${2:?}"; shift 2;;
        *) die 'Usage: bash scripts/deploy/restore.sh --env-file .env --backup /absolute/backup --confirm-database Desarrollo [--project isolated-name]';;
    esac
done
[[ "$backup" == /* && -d "$backup" ]] || die 'An existing absolute backup directory is required.'
load_deployment
[[ "$confirmed" == "$DEPLOY_DATABASE" ]] || die 'Explicit --confirm-database must match CRM_SQL_DATABASE.'
web_is_running && die 'Stop the target web host first. Restore never modifies a running installation.'
for directory in files keys; do
    [[ -z "$(find "$DEPLOY_DATA_DIR/$directory" -mindepth 1 -print -quit)" ]] || die 'Restore requires empty files/keys directories; existing data is never removed.'
done
python3 "$DEPLOY_REPO_DIR/scripts/deploy/archive.py" verify "$backup"
exists="$(sql_admin "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$DEPLOY_DATABASE') IS NULL THEN 0 ELSE 1 END;" | tr -d '[:space:]')"
[[ "$exists" == 0 ]] || die 'Target SQL database already exists; restore will not overwrite it.'
staging="$(mktemp -d "$DEPLOY_DATA_DIR/.crm-restore.XXXXXXXX")"
python3 "$DEPLOY_REPO_DIR/scripts/deploy/archive.py" unpack "$backup/files-and-keys.tar.gz" "$staging"
restore_id="$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
container_backup="/var/opt/mssql/backup/crm-restore-$restore_id.bak"
"${COMPOSE[@]}" exec -T sql bash -c 'set -euo pipefail; umask 077; mkdir -p /var/opt/mssql/backup; set -o noclobber; cat > "$1"' crm-restore "$container_backup" < "$backup/database.bak"
restore_sql="$staging/restore.sql"
python3 "$DEPLOY_REPO_DIR/scripts/deploy/archive.py" restore-sql "$backup" "$DEPLOY_DATABASE" "$container_backup" "$restore_sql"
sql_admin_file "$restore_sql" >/dev/null
# Only empty target directories are removed. No recursive delete or WITH REPLACE.
rmdir -- "$DEPLOY_DATA_DIR/files" "$DEPLOY_DATA_DIR/keys"
mv -- "$staging/files" "$DEPLOY_DATA_DIR/files"
mv -- "$staging/keys" "$DEPLOY_DATA_DIR/keys"
chown -R 1654:1654 -- "$DEPLOY_DATA_DIR/files" "$DEPLOY_DATA_DIR/keys"
find "$DEPLOY_DATA_DIR/files" "$DEPLOY_DATA_DIR/keys" -type d -exec chmod 700 {} +
find "$DEPLOY_DATA_DIR/files" "$DEPLOY_DATA_DIR/keys" -type f -exec chmod 600 {} +
printf 'SQL/files/keys restored and DBCC CHECKDB passed. Web remains stopped.\n'
printf 'Next: run sql-init to map the runtime login, then start web and verify authorized reads. Do not bootstrap an existing database.\n'
