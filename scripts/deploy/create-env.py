#!/usr/bin/env python3
"""Create local deployment credentials without exposing values or overwriting a file."""
import argparse
import os
import re
import secrets
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', default='.env')
parser.add_argument('--data-dir', default='/srv/crm')
parser.add_argument('--email', required=True)
parser.add_argument('--name', required=True)
parser.add_argument('--organization', required=True)
parser.add_argument('--project', default='desarrollo-crm')
parser.add_argument('--port', type=int, default=5080)
parser.add_argument('--local-http', action='store_true', help='Only for a loopback test; HTTPS deployment keeps secure cookies.')
args = parser.parse_args()
if not Path(args.data_dir).is_absolute() or '\n' in args.data_dir or '\r' in args.data_dir:
    parser.error('data-dir must be an absolute single-line Linux path')
if not re.fullmatch(r'[a-z][a-z0-9-]{2,60}', args.project) or not 1024 <= args.port <= 65535:
    parser.error('invalid Compose project or port')
if not re.fullmatch(r'[^\s@]+@[^\s@]+\.[^\s@]+', args.email):
    parser.error('a valid administrator email is required')
for value in (args.data_dir, args.email, args.name, args.organization):
    if not value or any(c in value for c in '\r\n\x00\'"$#'):
        parser.error('configuration text must be nonempty, single-line, without quotes, dollar signs or hashes')
password = lambda: secrets.token_urlsafe(36) + 'Aa1!'
values = {
    'CRM_COMPOSE_PROJECT': args.project, 'CRM_IMAGE': 'desarrollo-crm:local',
    'CRM_SQL_PASSWORD': password(), 'CRM_SQL_DATABASE': 'Desarrollo',
    'CRM_SQL_RUNTIME_USER': 'crm_runtime', 'CRM_SQL_RUNTIME_PASSWORD': password(),
    'CRM_DATA_DIR': args.data_dir, 'CRM_HTTP_PORT': str(args.port),
    'CRM_SECURE_COOKIES': 'false' if args.local_http else 'true', 'CRM_TRUSTED_PROXY_IP': '', 'CRM_SQL_EDITION': 'Express',
    'CRM_BOOTSTRAP_EMAIL': args.email, 'CRM_BOOTSTRAP_PASSWORD': password(),
    'CRM_BOOTSTRAP_FULL_NAME': args.name, 'CRM_BOOTSTRAP_ORGANIZATION_NAME': args.organization,
}
path = Path(args.output)
descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
with os.fdopen(descriptor, 'w', encoding='utf-8', newline='\n') as output:
    output.write('# Private generated deployment settings. Never commit or print this file.\n')
    for key, value in values.items():
        output.write(f'{key}={value}\n')
print(f'Private settings created at {path}. Open with a private editor for the initial password; values were not printed.')
