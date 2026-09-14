#!/usr/bin/env python3
"""Build and exercise the actual Docker image with disposable PostgreSQL/RabbitMQ.
No cloud credentials or existing Docker volumes are used. Requires Docker + OpenSSL.
"""
import argparse
import base64
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import uuid

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--skip-build', action='store_true')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
docker = shutil.which('docker') or '/Applications/Docker.app/Contents/Resources/bin/docker'
os.environ['PATH'] = str(Path(docker).resolve().parent) + os.pathsep + os.environ.get('PATH', '')
image = 'wida-api:smoke-test'
name = 'wida-smoke-' + uuid.uuid4().hex[:10]
containers = []
network_created = False

def run(*cmd, check=True):
    result = subprocess.run(cmd, text=True, capture_output=True)
    if check and result.returncode:
        raise RuntimeError(f'{Path(cmd[0]).name} {cmd[1]} failed: {result.stderr[-2500:]}')
    return result

def d(*cmd, **kwargs):
    return run(docker, *cmd, **kwargs)

def wait_for(label, test, seconds=180):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        try:
            if test():
                print('PASS ' + label, flush=True)
                return
        except (RuntimeError, OSError, ValueError):
            pass
        time.sleep(2)
    raise RuntimeError('Timed out: ' + label)

def start(suffix, *cmd):
    container = name + '-' + suffix
    containers.append(container)
    d('run', '-d', '--name', container, '--network', name, '--label', 'wida.test=smoke', *cmd)
    return container

try:
    if not args.skip_build:
        print('Building image (first download can take several minutes)...', flush=True)
        subprocess.run([docker, 'build', '-t', image, str(root)], check=True)
    d('image', 'inspect', image)
    with tempfile.TemporaryDirectory(prefix='wida-smoke-') as directory:
        tmp = Path(directory)
        password = secrets.token_hex(24)
        proxy_secret = secrets.token_hex(32)
        cert_password = secrets.token_hex(24)
        def envfile(filename, entries):
            path = tmp / filename
            path.write_text(''.join(f'{k}={v}\n' for k, v in entries.items()))
            path.chmod(0o600)
            return str(path)
        certpass = tmp / 'cert-password'
        certpass.write_text(cert_password)
        certpass.chmod(0o600)
        run('openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1', '-subj', '/CN=Wida smoke test',
            '-keyout', str(tmp / 'key.pem'), '-out', str(tmp / 'cert.pem'))
        run('openssl', 'pkcs12', '-export', '-inkey', str(tmp / 'key.pem'), '-in', str(tmp / 'cert.pem'),
            '-out', str(tmp / 'cert.pfx'), '-passout', 'file:' + str(certpass))
        d('network', 'create', name)
        network_created = True
        print('Starting isolated PostgreSQL and RabbitMQ...', flush=True)
        pg = start('postgres', '--env-file', envfile('pg.env', {'POSTGRES_PASSWORD': password, 'POSTGRES_DB': 'wida'}), 'postgres:17-alpine')
        rabbit = start('rabbit', '--env-file', envfile('rabbit.env', {'RABBITMQ_DEFAULT_USER': 'wida', 'RABBITMQ_DEFAULT_PASS': password}), 'rabbitmq:4.2-alpine')
        wait_for('PostgreSQL ready', lambda: d('exec', pg, 'pg_isready', '-U', 'postgres', check=False).returncode == 0)
        wait_for('RabbitMQ ready', lambda: d('exec', rabbit, 'rabbitmq-diagnostics', '-q', 'ping', check=False).returncode == 0)
        settings = {
            'ASPNETCORE_ENVIRONMENT': 'Production',
            'ConnectionStrings__DefaultConnection': f'Host={pg};Database=wida;Username=postgres;Password={password}',
            'Database__ApplyMigrations': 'true',
            'Storage__Provider': 'Local',
            'Authentication__PublicOrigin': 'https://wida-smoke.test',
            'Authentication__AdminEmail': '',
            'Authentication__ProxySecret': proxy_secret,
            'Authentication__DataProtectionProvider': 'Database',
            'Authentication__DataProtectionCertificateBase64': base64.b64encode((tmp / 'cert.pfx').read_bytes()).decode(),
            'Authentication__DataProtectionCertificatePassword': cert_password,
            'ProcessingQueue__Enabled': 'true',
            'RabbitMQ__Uri': f'amqp://wida:{password}@{rabbit}/%2F',
            'RabbitMQ__QueueType': 'classic',
            'RabbitMQ__QueueName': 'wida.smoke.v1',
        }
        app = start('api', '--env-file', envfile('app.env', settings), '--memory', '512m', '--cpus', '0.5', '-p', '127.0.0.1::10000', image)
        port = d('port', app, '10000/tcp').stdout.strip().rsplit(':', 1)[1]
        origin = 'http://127.0.0.1:' + port
        def request(path, headers=None):
            try:
                response = urllib.request.urlopen(urllib.request.Request(origin + path, headers=headers or {}), timeout=5)
            except urllib.error.HTTPError as error:
                response = error
            with response:
                return response.status, response.read(), response.headers
        wait_for('container HTTP health', lambda: request('/healthz')[0] == 200)
        assert d('exec', app, 'id', '-u').stdout.strip() != '0'
        assert d('exec', app, 'sh', '-c', 'test -w /app/uploads').returncode == 0
        print('PASS non-root runtime and writable temporary uploads', flush=True)
        assert request('/api/auth/session')[0] == 403
        headers = {'X-Wida-Proxy-Secret': proxy_secret, 'X-Wida-Client-IP': '192.0.2.1'}
        status, body, response_headers = request('/api/auth/session', headers)
        assert status == 200 and json.loads(body)['csrfToken']
        cookie = response_headers.get('Set-Cookie').split(';', 1)[0]
        assert request('/api/documents', headers)[0] == 401
        print('PASS proxy protection, anonymous session and authenticated data boundary', flush=True)
        def sql(query):
            return d('exec', pg, 'psql', '-U', 'postgres', '-d', 'wida', '-Atc', query).stdout.strip()
        assert sql('SELECT count(*) FROM "__EFMigrationsHistory"') == '4'
        assert sql('SELECT count(*) FROM "DataProtectionKeys" WHERE "Xml" LIKE \'%encryptedSecret%\'') == '1'
        print('PASS PostgreSQL migrations and encrypted session key persistence', flush=True)
        def consumer_ready():
            queues = json.loads(d('exec', rabbit, 'rabbitmqctl', 'list_queues', '--formatter=json', 'name', 'type', 'consumers').stdout)
            return any(q['name'] == 'wida.smoke.v1' and q['type'] == 'classic' and q['consumers'] == 1 for q in queues)
        wait_for('embedded worker subscribed to RabbitMQ classic queue', consumer_ready)
        d('restart', app)
        # Docker can allocate a new ephemeral host port on restart.
        port = d('port', app, '10000/tcp').stdout.strip().rsplit(':', 1)[1]
        origin = 'http://127.0.0.1:' + port
        wait_for('HTTP health after restart', lambda: request('/healthz')[0] == 200)
        status, body, response_headers = request('/api/auth/session', {**headers, 'Cookie': cookie})
        assert status == 200 and json.loads(body)['csrfToken']
        assert response_headers.get('Set-Cookie') is None, 'Persisted antiforgery cookie was replaced after restart'
        wait_for('worker reconnected after restart', consumer_ready)
        assert sql('SELECT count(*) FROM "DataProtectionKeys"') == '1'
        print('PASS cookie/key reuse and idempotent migrations after restart', flush=True)
        print('SUCCESS: actual Linux image runs with PostgreSQL, encrypted keys and embedded RabbitMQ worker. Cloud storage, Google and Azure were not contacted.', flush=True)
except Exception:
    for container in containers:
        if container.endswith('-api'):
            print(d('logs', '--tail', '70', container, check=False).stdout, flush=True)
    raise
finally:
    for container in reversed(containers):
        d('rm', '-f', '-v', container, check=False)
    if network_created:
        d('network', 'rm', name, check=False)
    print('Temporary containers, volumes, network and test secrets removed.', flush=True)
