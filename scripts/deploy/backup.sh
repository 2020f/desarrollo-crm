#!/usr/bin/env bash
set -euo pipefail
umask 077
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
DEPLOY_ENV_FILE="$DEPLOY_REPO_DIR/.env"; DEPLOY_PROJECT=''; output=''
while (($#)); do
    case "$1" in
        --env-file) DEPLOY_ENV_FILE="${2:?}"; shift 2;;
        --project) DEPLOY_PROJECT="${2:?}"; shift 2;;
        --output) output="${2:?}"; shift 2;;
        *) die 'Usage: bash scripts/deploy/backup.sh --env-file .env --output /absolute/new-backup-directory [--project name]';;
    esac
done
[[ "$output" == /* && "$output" != / ]] || die 'Choose a new absolute backup directory.'
[[ ! -e "$output" ]] || die 'Backup output already exists; no overwrite allowed.'
load_deployment
output="$(realpath -m -- "$output")"
[[ "$output" != "$DEPLOY_DATA_DIR" && "$output" != "$DEPLOY_DATA_DIR/"* ]] || die 'Backup output must be outside the live data directory.'
mkdir -m 700 -- "$output"
was_running=false
if web_is_running; then was_running=true; "${COMPOSE[@]}" stop -t 30 web; fi
resume() {
    local result=$?
    trap - EXIT
    if "$was_running"; then "${COMPOSE[@]}" up -d web || result=1; fi
    exit "$result"
}
trap resume EXIT
backup_id="$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
container_backup="/var/opt/mssql/backup/crm-backup-$backup_id.bak"
"${COMPOSE[@]}" exec -T sql mkdir -p /var/opt/mssql/backup
sql_admin "BACKUP DATABASE [$DEPLOY_DATABASE] TO DISK=N'$container_backup' WITH COPY_ONLY,CHECKSUM,NOFORMAT,NOINIT; RESTORE VERIFYONLY FROM DISK=N'$container_backup' WITH CHECKSUM;" >/dev/null
"${COMPOSE[@]}" cp "sql:$container_backup" "$output/database.bak"
chmod 600 -- "$output/database.bak"
sql_admin 'SET NOCOUNT ON; SELECT name AS logicalName,type FROM sys.database_files ORDER BY file_id FOR JSON PATH;' "$DEPLOY_DATABASE" > "$output/logical-files.json"
python3 "$DEPLOY_REPO_DIR/scripts/deploy/archive.py" pack "$DEPLOY_DATA_DIR" "$output/files-and-keys.tar.gz"
image="$("${COMPOSE[@]}" images -q web | head -n 1)"
commit="$(git -C "$DEPLOY_REPO_DIR" rev-parse HEAD 2>/dev/null || printf unknown)"
python3 "$DEPLOY_REPO_DIR/scripts/deploy/archive.py" metadata "$output" "$DEPLOY_DATABASE" "$image" "$commit"
python3 "$DEPLOY_REPO_DIR/scripts/deploy/archive.py" verify "$output"
printf 'Consistent SQL/files/keys backup created: %s\n' "$output"
