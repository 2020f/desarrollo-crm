#!/usr/bin/env bash
# Shared by the Linux maintenance commands. Never source the private .env as code.
set -euo pipefail
DEPLOY_REPO_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)"
die() { printf '%s\n' "$*" >&2; exit 1; }
env_value() {
    python3 - "$DEPLOY_ENV_FILE" "$1" <<'PY'
import sys
value = ''
for line in open(sys.argv[1], encoding='utf-8-sig'):
    key, separator, candidate = line.rstrip('\r\n').partition('=')
    if separator and key == sys.argv[2]:
        value = candidate.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
            value = value[1:-1]
print(value)
PY
}
load_deployment() {
    for tool in docker python3 flock realpath sha256sum; do command -v "$tool" >/dev/null || die "Required command missing: $tool"; done
    DEPLOY_ENV_FILE="$(realpath -e -- "$DEPLOY_ENV_FILE")"
    [[ -f "$DEPLOY_ENV_FILE" ]] || die 'Private environment file is missing.'
    local mode
    mode="$(stat -c %a -- "$DEPLOY_ENV_FILE")"
    (( (8#$mode & 077) == 0 )) || die 'Environment file must not be readable by group/others; chmod 600 it first.'
    DEPLOY_PROJECT="${DEPLOY_PROJECT:-$(env_value CRM_COMPOSE_PROJECT)}"
    DEPLOY_PROJECT="${DEPLOY_PROJECT:-desarrollo-crm}"
    [[ "$DEPLOY_PROJECT" =~ ^[a-z][a-z0-9-]{2,60}$ ]] || die 'Invalid Compose project name.'
    DEPLOY_DATABASE="$(env_value CRM_SQL_DATABASE)"; DEPLOY_DATABASE="${DEPLOY_DATABASE:-Desarrollo}"
    [[ "$DEPLOY_DATABASE" =~ ^[A-Za-z][A-Za-z0-9_]{0,62}$ ]] || die 'Invalid database name.'
    DEPLOY_DATA_DIR="$(env_value CRM_DATA_DIR)"
    [[ "$DEPLOY_DATA_DIR" == /* && "$DEPLOY_DATA_DIR" != / ]] || die 'CRM_DATA_DIR must be an absolute Linux data directory.'
    DEPLOY_DATA_DIR="$(realpath -e -- "$DEPLOY_DATA_DIR")"
    for directory in files keys; do
        [[ -d "$DEPLOY_DATA_DIR/$directory" && ! -L "$DEPLOY_DATA_DIR/$directory" ]] || die 'Persistent files/keys directories must exist and must not be symbolic links.'
    done
    # The specified .env is authoritative. Ambient shell variables must not point
    # Compose at a different storage tree or SQL instance than the snapshot code.
    local variable
    for variable in CRM_COMPOSE_PROJECT CRM_IMAGE CRM_SQL_PASSWORD CRM_SQL_DATABASE CRM_SQL_RUNTIME_USER CRM_SQL_RUNTIME_PASSWORD CRM_DATA_DIR CRM_HTTP_PORT CRM_SECURE_COOKIES CRM_TRUSTED_PROXY_IP CRM_SQL_EDITION CRM_BOOTSTRAP_EMAIL CRM_BOOTSTRAP_PASSWORD CRM_BOOTSTRAP_FULL_NAME CRM_BOOTSTRAP_ORGANIZATION_NAME; do unset "$variable"; done
    COMPOSE=(docker compose --project-name "$DEPLOY_PROJECT" --env-file "$DEPLOY_ENV_FILE" --file "$DEPLOY_REPO_DIR/compose.yaml")
    "${COMPOSE[@]}" config --quiet
    exec 9>"$DEPLOY_DATA_DIR/.maintenance.lock"
    flock -n 9 || die 'Another backup/restore command owns this data directory.'
}
sql_admin() {
    local query="$1" database="${2:-master}"
    "${COMPOSE[@]}" exec -T sql bash -c '
        set -euo pipefail
        export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
        export LANG=C.UTF-8
        # -y 0 suppresses headers already; combining it with -h -1 is rejected
        # by the SQL Server ODBC sqlcmd shipped in the container.
        exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -r 1 -y 0 -w 65535 -d "$2" -Q "$1"
    ' crm-sql "$query" "$database"
}
sql_admin_file() {
    "${COMPOSE[@]}" exec -T sql bash -c '
        set -euo pipefail
        export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
        export LANG=C.UTF-8
        exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -r 1 -i /dev/stdin
    ' < "$1"
}
web_is_running() { [[ -n "$("${COMPOSE[@]}" ps --status running -q web)" ]]; }
