#!/usr/bin/env bash
set -euo pipefail
# Compose passes secrets in the one-time container environment, never CLI arguments.
: "${CRM_SQL_PASSWORD:?Administrative SQL credential is required}"
: "${CRM_SQL_RUNTIME_PASSWORD:?Independent runtime SQL credential is required}"
: "${CRM_SQL_RUNTIME_USER:=crm_runtime}"
: "${CRM_SQL_DATABASE:=Desarrollo}"
[[ "$CRM_SQL_RUNTIME_USER" =~ ^[a-z][a-z0-9_]{2,40}$ ]] || { echo 'Invalid runtime login name.' >&2; exit 1; }
[[ "$CRM_SQL_DATABASE" =~ ^[A-Za-z][A-Za-z0-9_]{0,62}$ ]] || { echo 'Invalid database name.' >&2; exit 1; }
# These generated-password characters also prevent sqlcmd variable injection and
# Compose dollar interpolation. Use create-env.py instead of shell-escaping secrets.
[[ "$CRM_SQL_RUNTIME_PASSWORD" =~ ^[A-Za-z0-9_+/!=%-]{24,128}$ ]] || { echo 'Use a generated 24-128 character runtime password.' >&2; exit 1; }
[[ "$CRM_SQL_RUNTIME_PASSWORD" != "$CRM_SQL_PASSWORD" ]] || { echo 'Runtime and administrative passwords must differ.' >&2; exit 1; }
export SQLCMDPASSWORD="$CRM_SQL_PASSWORD"
export CRM_SQL_RUNTIME_USER CRM_SQL_RUNTIME_PASSWORD CRM_SQL_DATABASE
exec /opt/mssql-tools18/bin/sqlcmd -S sql -U sa -C -b -r 1 -i /opt/crm/initialize-runtime.sql
