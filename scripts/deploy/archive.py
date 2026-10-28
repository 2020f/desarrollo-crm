#!/usr/bin/env python3
"""Private backup archives, checksums and safe restore SQL; standard library only."""
import argparse
import datetime as dt
import hashlib
import json
import os
import re
import shutil
import stat
import tarfile
from pathlib import Path, PurePosixPath

def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()

def write_json(path, value):
    with path.open('x', encoding='utf-8') as output:
        json.dump(value, output, indent=2, ensure_ascii=False)
        output.write('\n')
    path.chmod(0o600)

parser = argparse.ArgumentParser(description=__doc__)
commands = parser.add_subparsers(dest='command', required=True)
pack = commands.add_parser('pack')
pack.add_argument('data_dir', type=Path); pack.add_argument('archive', type=Path)
meta = commands.add_parser('metadata')
meta.add_argument('directory', type=Path); meta.add_argument('database'); meta.add_argument('image'); meta.add_argument('commit')
verify = commands.add_parser('verify'); verify.add_argument('directory', type=Path)
unpack = commands.add_parser('unpack'); unpack.add_argument('archive', type=Path); unpack.add_argument('destination', type=Path)
restore = commands.add_parser('restore-sql')
restore.add_argument('directory', type=Path); restore.add_argument('database'); restore.add_argument('container_backup'); restore.add_argument('output', type=Path)
args = parser.parse_args()

if args.command == 'pack':
    if args.archive.exists():
        parser.error('archive already exists')
    root = args.data_dir.resolve(strict=True)
    for directory in ('files', 'keys'):
        folder = root / directory
        for path in [folder, *folder.rglob('*')]:
            mode = path.lstat().st_mode
            if path.is_symlink() or not (stat.S_ISDIR(mode) or stat.S_ISREG(mode)):
                parser.error('files/keys contains a symbolic link or special file; backup refused')
    with tarfile.open(args.archive, 'x:gz', dereference=False) as archive:
        for directory in ('files', 'keys'):
            archive.add(root / directory, arcname=directory)
    args.archive.chmod(0o600)
elif args.command == 'metadata':
    files_path = args.directory / 'logical-files.json'
    logical = json.loads(files_path.read_text(encoding='utf-8-sig'))
    files_path.unlink()  # This generated intermediate is not an independent recovery artifact.
    write_json(args.directory / 'metadata.json', {
        'formatVersion': 1, 'createdAtUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
        'database': args.database, 'sourceImageId': args.image, 'sourceCommit': args.commit,
        'logicalFiles': logical, 'backupChecksummed': True,
        'storageRoots': ['files', 'keys'], 'encryptedAtRestByThisTool': False,
    })
    names = ('database.bak', 'files-and-keys.tar.gz', 'metadata.json')
    write_json(args.directory / 'manifest.json', {'formatVersion': 1, 'sha256': {name: digest(args.directory / name) for name in names}})
elif args.command == 'verify':
    manifest = json.loads((args.directory / 'manifest.json').read_text(encoding='utf-8'))
    names = {'database.bak', 'files-and-keys.tar.gz', 'metadata.json'}
    if manifest.get('formatVersion') != 1 or set(manifest.get('sha256', {})) != names:
        parser.error('unsupported or incomplete backup manifest')
    for name, expected in manifest['sha256'].items():
        path = args.directory / name
        if path.is_symlink() or not path.is_file() or digest(path) != expected:
            parser.error(f'backup checksum mismatch: {name}')
    metadata = json.loads((args.directory / 'metadata.json').read_text(encoding='utf-8'))
    if metadata.get('formatVersion') != 1 or metadata.get('storageRoots') != ['files', 'keys']:
        parser.error('unsupported metadata')
    print('Backup artifact checksums verified.')
elif args.command == 'unpack':
    destination = args.destination.resolve(strict=True)
    with tarfile.open(args.archive, 'r:gz') as archive:
        members = archive.getmembers()
        seen = set()
        for member in members:
            relative = PurePosixPath(member.name)
            if relative.is_absolute() or '..' in relative.parts or not relative.parts or relative.parts[0] not in ('files', 'keys'):
                parser.error('archive contains an unsafe path')
            if member.name in seen or not (member.isdir() or member.isfile()):
                parser.error('archive contains a duplicate, link or special file')
            seen.add(member.name)
        for member in members:
            target = destination.joinpath(*PurePosixPath(member.name).parts)
            if member.isdir():
                target.mkdir(mode=0o700, parents=True, exist_ok=True)
            else:
                target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
                source = archive.extractfile(member)
                if source is None:
                    parser.error('archive file content is missing')
                with source, target.open('xb') as output:
                    shutil.copyfileobj(source, output)
                target.chmod(0o600)
    if not all((destination / name).is_dir() for name in ('files', 'keys')):
        parser.error('archive is missing files/keys directories')
elif args.command == 'restore-sql':
    if not re.fullmatch(r'[A-Za-z][A-Za-z0-9_]{0,62}', args.database) or not re.fullmatch(r'/var/opt/mssql/backup/crm-restore-[a-f0-9]{32}\.bak', args.container_backup):
        parser.error('invalid SQL restore target')
    metadata = json.loads((args.directory / 'metadata.json').read_text(encoding='utf-8'))
    logical = metadata.get('logicalFiles', [])
    if not logical or any(item.get('type') not in (0, 1) or not isinstance(item.get('logicalName'), str) for item in logical):
        parser.error('backup logical files must contain only SQL data/log files')
    literal = lambda value: "N'" + value.replace("'", "''") + "'"
    moves = []
    for index, item in enumerate(logical):
        suffix = 'ldf' if item['type'] == 1 else ('mdf' if index == 0 else 'ndf')
        target = f'/var/opt/mssql/data/{args.database}_restored_{index}.{suffix}'
        moves.append(f'MOVE {literal(item["logicalName"])} TO {literal(target)}')
    sql = f"SET NOCOUNT ON;\nUSE [master];\nIF DB_ID({literal(args.database)}) IS NOT NULL THROW 51000, 'Target database already exists; no overwrite allowed.', 1;\n"
    sql += f'RESTORE VERIFYONLY FROM DISK={literal(args.container_backup)} WITH CHECKSUM;\n'
    sql += f'RESTORE DATABASE [{args.database}] FROM DISK={literal(args.container_backup)} WITH ' + ', '.join(moves) + ', CHECKSUM, RECOVERY;\n'
    sql += f'DBCC CHECKDB([{args.database}]) WITH NO_INFOMSGS;\n'
    with args.output.open('x', encoding='utf-8') as output:
        output.write(sql)
    args.output.chmod(0o600)
