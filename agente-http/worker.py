import argparse
import gzip
import hashlib
import http.client
import json
import os
import re
import ssl
from pathlib import Path
import tempfile
import time
import uuid
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from features import FeatureProcessor, PROFILE

TRANSIENT_HTTP = (429, 500, 502, 503, 504)
# transient transport failures worth retrying (OSError and HTTPException)
TRANSIENT_ERRORS = (OSError, http.client.HTTPException)
# keep reading the old inode until quiet so lines written during rotation are not lost
ROTATION_GRACE_SECONDS = 5
RETRY_INITIAL_SECONDS = 2
RETRY_MAX_SECONDS = 60
# bytes of a file's start kept in the checkpoint to recognize it after a rename
HEAD_BYTES = 1024
# access.log.1, access.log.2.gz, access.log-20261120 (dateext), compressed or not
ROTATED_SUFFIX = r'(?:\.\d+|-\d{8,10})(?:\.gz)?'


class Api:
    def __init__(self, base, key, ca_file=None):
        self.base = base.rstrip('/') + '/internal/ml/jobs'
        self.key = key
        self.ssl_context = ssl.create_default_context(cafile=ca_file) if ca_file else None

    def request(self, path, data=None, download=None, heartbeat=None):
        payload = json.dumps(data, allow_nan=False).encode() if data is not None else None
        req = Request(self.base + path, data=payload, headers={
            'X-ML-Agent-Key': self.key, 'Content-Type': 'application/json'}, method='POST' if data is not None else 'GET')
        with urlopen(req, timeout=100, context=self.ssl_context) as response:
            if download:
                digest = hashlib.sha256()
                last_heartbeat = time.monotonic()
                with open(download, 'wb') as output:
                    while chunk := response.read(131072):
                        output.write(chunk); digest.update(chunk)
                        if heartbeat and time.monotonic() - last_heartbeat > 20:
                            heartbeat(); last_heartbeat = time.monotonic()
                return digest.hexdigest()
            body = response.read()
            return json.loads(body) if body else None

    def retrying(self, path, data):
        """POST with backoff on transient failures; other HTTP errors (4xx) fail at once."""
        for attempt in range(4):
            try:
                return self.request(path, data)
            except HTTPError as error:
                if error.code not in TRANSIENT_HTTP or attempt == 3:
                    raise
            except TRANSIENT_ERRORS:
                if attempt == 3:
                    raise
            time.sleep(2 ** attempt)

    def send(self, job, processor, records, completed=False):
        # same payload is safe to retry: .NET commits progress and results atomically
        return self.retrying(f"/{job['id']}/batches", processor.batch(job['lease'], records, completed))

    def heartbeat(self, job):
        # best effort: a missed renewal is covered by the next one and a lost lease still yields 409
        try:
            self.retrying(f"/{job['id']}/heartbeat?lease={job['lease']}", {})
        except HTTPError as error:
            if error.code not in TRANSIENT_HTTP:
                raise
        except TRANSIENT_ERRORS:
            pass


class Spool:
    """Persistent local queue. A batch is written to disk before it is sent and removed only after the
    backend confirms it, so an outage or a restart never depends on the log files still existing."""
    def __init__(self, root, execution, max_bytes):
        self.dir = Path(root) / str(uuid.UUID(execution))
        self.dir.mkdir(parents=True, exist_ok=True)
        self.max_bytes = max_bytes
        for leftover in self.dir.glob('*.tmp'):
            leftover.unlink()
        self.bytes = sum(entry.stat().st_size for entry in self._entries())

    def _entries(self):
        return sorted(self.dir.glob('*.json'))

    def __len__(self):
        return len(self._entries())

    def full(self):
        return self.bytes >= self.max_bytes

    def put(self, base, payload):
        """base: last line confirmed before this batch, used to prove the queue has no holes."""
        data = json.dumps({'base': base, 'payload': payload}, separators=(',', ':'), allow_nan=False).encode()
        target = self.dir / f"{payload['lastLine']:015d}.json"
        temp = target.with_suffix('.tmp')
        with open(temp, 'wb') as output:
            output.write(data); output.flush(); os.fsync(output.fileno())
        os.replace(temp, target)
        if os.name != 'nt':  # make the rename itself durable
            descriptor = os.open(self.dir, os.O_RDONLY)
            try:
                os.fsync(descriptor)
            finally:
                os.close(descriptor)
        self.bytes += len(data)

    def first(self):
        entries = self._entries()
        return entries[0] if entries else None

    def read(self, entry):
        return json.loads(entry.read_bytes())

    def remove(self, entry):
        size = entry.stat().st_size
        entry.unlink(); self.bytes -= size

    def reconcile(self, confirmed_line):
        """Drops what the backend already confirmed. Returns the newest queued batch when the rest
        continues exactly from confirmed_line; otherwise discards the queue (the log is re-read)."""
        pending = []
        for entry in self._entries():
            item = self.read(entry)
            if item['payload']['lastLine'] <= confirmed_line:
                self.remove(entry)
            else:
                pending.append((entry, item))
        expected = confirmed_line
        for _, item in pending:
            if item['base'] != expected:
                print('La cola local no continúa desde el último lote confirmado; se descarta y se relee el log.', flush=True)
                for entry, _ in pending:
                    self.remove(entry)
                return None
            expected = item['payload']['lastLine']
        return pending[-1][1]['payload'] if pending else None


class Sender:
    """Sends the local queue in order. Transient failures keep batches queued and are retried with
    backoff instead of marking the source as failed."""
    def __init__(self, api, job, spool, queued_line):
        self.api, self.job, self.spool, self.queued_line = api, job, spool, queued_line
        self.offline = False; self.retry_at = 0.; self.delay = RETRY_INITIAL_SECONDS

    def put(self, payload):
        self.spool.put(self.queued_line, payload)
        self.queued_line = payload['lastLine']

    def drain(self):
        if time.monotonic() < self.retry_at:
            return False
        while (entry := self.spool.first()) is not None:
            # the backend acknowledges an already committed batch, so a lost response is safe to resend
            payload = {**self.spool.read(entry)['payload'], 'lease': self.job['lease']}
            try:
                self.api.request(f"/{self.job['id']}/batches", payload)
            except HTTPError as error:
                if error.code not in TRANSIENT_HTTP:
                    raise
                return self._later(f'HTTP {error.code}')
            except TRANSIENT_ERRORS as error:  # URLError, timeouts, resets and truncated responses
                return self._later(type(error).__name__)
            self.spool.remove(entry)
        if self.offline:
            print('Conexión con el backend restablecida; cola local enviada.', flush=True)
        self.offline = False; self.delay = RETRY_INITIAL_SECONDS
        return True

    def _later(self, reason):
        if not self.offline:
            print(f'Backend no disponible ({reason}); los lotes se guardan en la cola local y se reintentarán.', flush=True)
        self.offline = True
        self.retry_at = time.monotonic() + self.delay
        self.delay = min(max(self.delay * 2, RETRY_INITIAL_SECONDS), RETRY_MAX_SECONDS)
        return False

    def heartbeat(self):
        try:
            self.api.request(f"/{self.job['id']}/heartbeat?lease={self.job['lease']}", {})
        except HTTPError as error:
            if error.code not in TRANSIENT_HTTP:
                raise
        except TRANSIENT_ERRORS:
            pass


def file_identity(path):
    stat = path.stat()
    return str((stat.st_dev, stat.st_ino))


def open_source(path):
    return gzip.open(path, 'rb') if path.suffix == '.gz' else path.open('rb')


def head_of(path, length):
    with open_source(path) as source:
        data = source.read(length)
    return [len(data), hashlib.sha256(data).hexdigest()]


def update_head(file, state):
    """Fingerprints the already-read start of the current file (stable bytes only)."""
    known = (state.get('fileHead') or [0])[0]
    wanted = min(HEAD_BYTES, state['offset'])
    if wanted > known:
        position = file.tell(); file.seek(0)
        data = file.read(wanted); file.seek(position)
        state['fileHead'] = [len(data), hashlib.sha256(data).hexdigest()]


def same_file(candidate, state):
    try:
        head = state.get('fileHead')
        if head and head[0]:
            return head_of(candidate, head[0]) == list(head)
        return candidate.suffix != '.gz' and file_identity(candidate) == state['fileIdentity']
    except (OSError, EOFError):
        return False


def same_start(a, b):
    try:
        head = head_of(a, 256)
        return head[0] > 0 and head == head_of(b, 256)
    except (OSError, EOFError):
        return False


def rotated_files(path):
    pattern = re.compile(re.escape(path.name) + ROTATED_SUFFIX)
    found = [c for c in path.parent.iterdir() if pattern.fullmatch(c.name) and c.is_file()]
    return sorted(found, key=lambda c: (c.stat().st_mtime, c.suffix == '.gz'))


def locate_sources(path, state, skip_history):
    """Files still to read, oldest first; state['offset'] applies to the first one."""
    if state['fileIdentity'] is None:
        if skip_history:
            state['offset'] = path.stat().st_size
            print(f'Fuente nueva: se omiten {state["offset"]} bytes de historial; solo se analizarán registros nuevos.', flush=True)
        state['fileIdentity'] = file_identity(path)
        return [path]
    if path.exists() and same_file(path, state):
        if path.stat().st_size < state['offset']:
            print('El archivo se truncó (¿copytruncate?); se lee desde su inicio.', flush=True)
            state['offset'] = 0; state['fileHead'] = None
        return [path]
    rotated = rotated_files(path)
    for index, candidate in enumerate(rotated):
        if same_file(candidate, state):
            later = [c for c in rotated[index + 1:] if not same_start(c, candidate)]
            print(f'Rotación detectada: se completan {candidate.name} y {len(later)} archivo(s) rotado(s) '
                  f'posteriores antes de {path.name}.', flush=True)
            return [candidate, *later, path]
    state['rotationGaps'] += 1
    print('ADVERTENCIA: no se encontró el archivo rotado con las líneas pendientes (eliminado o comprimido en un '
          'formato no soportado). Se registra un hueco de continuidad y se sigue con el archivo actual.', flush=True)
    state['offset'] = 0; state['fileIdentity'] = file_identity(path); state['fileHead'] = None
    return [path]


def is_quiet(file):
    return time.time() - os.fstat(file.fileno()).st_mtime >= ROTATION_GRACE_SECONDS


def process_job(api, job, follow=None, ip_ttl_hours=72, max_ips=100_000, solo_nuevos=False, spool=None):
    if job['profile'] != PROFILE:
        raise ValueError('Perfil incompatible')
    if follow:
        return follow_job(api, job, Path(follow), spool, ip_ttl_hours, max_ips, solo_nuevos)
    processor = FeatureProcessor(api.key, job['checkpoint'])
    if processor.state['line'] != job['lastLine'] or processor.state['offset'] != job['offset']:
        raise ValueError('Checkpoint incompatible')
    with tempfile.TemporaryDirectory(prefix='http-analysis-') as temp:
        path = Path(temp) / 'source.log'
        source = api.request(f"/{job['id']}/source?lease={job['lease']}")
        digest = api.request(f"/{job['id']}/file?lease={job['lease']}", download=path,
                             heartbeat=lambda: api.heartbeat(job))
        if digest != source['sha256']:
            raise ValueError('file_changed')
        records = []; lines = 0; flushed = time.monotonic(); heartbeat = flushed
        replay_original = None; replay_clock = None
        with path.open('rb') as file:
            processor.state['fileIdentity'] = file_identity(path)
            file.seek(processor.state['offset'])
            while True:
                if time.monotonic() - heartbeat > 20:
                    api.heartbeat(job)
                    heartbeat = time.monotonic()
                raw = file.readline(65537)
                if not raw:
                    if records or lines:
                        api.send(job, processor, records)
                    api.send(job, processor, [], completed=True)
                    return
                if len(raw) > 65536:
                    while raw and not raw.endswith(b'\n'):
                        raw = file.readline(65537)
                    row = processor.process(None, file.tell())
                else:
                    row = processor.process(raw.decode('utf-8', errors='replace'), file.tell())
                lines += 1
                if row and job['kind'] == 'replay':
                    from datetime import datetime
                    timestamp = datetime.fromisoformat(row['occurredAt'].replace('Z', '+00:00')).timestamp()
                    if replay_original is None:
                        replay_original = timestamp; replay_clock = time.monotonic()
                    target = replay_clock + (timestamp - replay_original) / job['speed']
                    while time.monotonic() < target:
                        time.sleep(min(1, target - time.monotonic()))
                        if time.monotonic() - heartbeat > 20:
                            api.heartbeat(job)
                            heartbeat = time.monotonic()
                if row and job['kind'] != 'validation':
                    records.append(row)
                if len(records) >= 256 or lines >= 2048 or time.monotonic() - flushed > 2:
                    api.send(job, processor, records); records = []; lines = 0; flushed = time.monotonic()


def follow_job(api, job, path, spool, ip_ttl_hours, max_ips, solo_nuevos):
    """Follows a live NGINX log without losing lines across rotations, restarts or backend outages.
    Feature rules are those of FeatureProcessor; this function only decides which bytes it reads."""
    if spool is None:
        raise ValueError('El seguimiento requiere la cola local')
    queued = spool.reconcile(job['lastLine'])
    resume = queued or job
    # solo el seguimiento continuo acota el estado por IP; el historico conserva la paridad
    processor = FeatureProcessor(api.key, resume['checkpoint'], ip_ttl_hours, max_ips)
    s = processor.state
    if s['line'] != resume['lastLine'] or s['offset'] != resume['offset']:
        raise ValueError('Checkpoint incompatible')
    s.setdefault('rotationGaps', 0)
    if queued:
        print(f'Cola local: {len(spool)} lote(s) pendientes de envío; se continúa desde la línea {s["line"]}.', flush=True)
    sender = Sender(api, job, spool, s['line'])
    sources = locate_sources(path, s, solo_nuevos and s['line'] == 0)
    file = open_source(sources.pop(0)); file.seek(s['offset'])
    live = not sources
    records = []; lines = 0; flushed = time.monotonic(); beat = flushed; throttled = False

    def handle(raw):
        nonlocal lines
        row = processor.process(raw, file.tell())
        lines += 1
        if row and job['kind'] != 'validation':
            records.append(row)

    def flush():
        nonlocal records, lines, flushed
        if records or lines:
            update_head(file, s)
            sender.put(processor.batch(None, records)); records = []; lines = 0
        flushed = time.monotonic()

    try:
        while True:
            if time.monotonic() - beat > 20:
                sender.heartbeat(); beat = time.monotonic()
            if spool.full() and not sender.drain():
                if not throttled:
                    print('Cola local llena: se pausa la lectura hasta que el backend confirme lotes.', flush=True)
                    throttled = True
                time.sleep(1)
                continue
            throttled = False
            start = file.tell()
            raw = file.readline(65537)
            if raw and not raw.endswith(b'\n') and len(raw) <= 65536:
                file.seek(start); raw = b''  # wait for the producer to finish its line
            if raw:
                if len(raw) > 65536:
                    while raw and not raw.endswith(b'\n'):
                        raw = file.readline(65537)
                    handle(None)
                else:
                    handle(raw.decode('utf-8', errors='replace'))
                # while offline, batches are cut only by size to avoid tiny checkpoints
                if len(records) >= 256 or lines >= 2048 or (not sender.offline and time.monotonic() - flushed > 2):
                    flush(); sender.drain()
                continue
            sender.drain()
            if (records or lines) and not sender.offline:
                flush(); sender.drain()
            if live:
                if path.exists():
                    if file_identity(path) != s['fileIdentity']:
                        # rotated: finish the old inode first, it may receive lines until nginx reopens
                        print(f'Rotación detectada en {path.name}; se completa el archivo anterior.', flush=True)
                        live = False; sources = [path]
                        continue
                    if path.stat().st_size < file.tell():
                        print('El archivo se truncó (¿copytruncate?); se continúa desde su inicio.', flush=True)
                        file.seek(0); s['offset'] = 0; s['fileHead'] = None
                        continue
            elif is_quiet(file):
                tail = file.read(65537)  # a final line without newline is still a line
                if tail:
                    handle(None if len(tail) > 65536 else tail.decode('utf-8', errors='replace'))
                file.close()
                following = sources.pop(0)
                file = open_source(following); live = not sources
                s['offset'] = 0; s['fileHead'] = None
                s['fileIdentity'] = None if following.suffix == '.gz' else file_identity(following)
                continue
            time.sleep(1)
    finally:
        file.close()


def load_env(path):
    # match the simple quoted values used by the .NET development configuration
    if not path.exists():
        return
    for raw in path.read_text(encoding='utf-8-sig').splitlines():
        line = raw.strip()
        if not line or line.startswith('#'):
            continue
        if line.startswith('export '):
            line = line[7:].lstrip()
        key, value = line.split('=', 1); value = value.strip()
        if value.startswith('"'):
            value = json.loads(value)
        elif value.startswith("'") and value.endswith("'"):
            value = value[1:-1].replace("\\'", "'").replace('\\\\', '\\')
        os.environ.setdefault(key.strip(), value)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--api', default=None)
    parser.add_argument('--ca-file', type=Path, help='Certificado PEM de confianza para HTTPS local')
    parser.add_argument('--env', type=Path, default=Path(__file__).resolve().parents[1] / '.env')
    parser.add_argument('--once', action='store_true', help='Process at most one available file job')
    parser.add_argument('--follow', type=Path, help='Authorized NGINX file to follow')
    parser.add_argument('--execution', help='Live execution ID created by an administrator')
    parser.add_argument('--ip-ttl-hours', type=float, default=72,
                        help='Con --follow: horas de inactividad tras las que una IP empieza de cero (predeterminado 72, como el corte de periodo)')
    parser.add_argument('--max-ips', type=int, default=100_000,
                        help='Con --follow: máximo de IP en memoria/checkpoint; se descartan las menos recientes')
    parser.add_argument('--solo-nuevos', action='store_true',
                        help='Con --follow y una fuente nueva: empezar al final del archivo, sin analizar el historial existente')
    parser.add_argument('--estado', type=Path,
                        default=Path(os.getenv('AGENTE_ESTADO') or Path(__file__).resolve().parent / '.data' / 'cola'),
                        help='Con --follow: carpeta persistente de la cola local de lotes (variable AGENTE_ESTADO)')
    parser.add_argument('--cola-max-mb', type=int, default=512,
                        help='Con --follow: tamaño máximo de la cola local; al llenarse se pausa la lectura sin descartar datos')
    args = parser.parse_args(); load_env(args.env)
    if args.ip_ttl_hours <= 0 or args.max_ips < 1 or args.cola_max_mb < 1:
        parser.error('--ip-ttl-hours, --max-ips y --cola-max-mb deben ser positivos.')
    key = os.getenv('Ml__AgentKey', '')
    if len(key) < 32:
        parser.error('Configura Ml__AgentKey con al menos 32 caracteres.')
    if args.follow and not args.execution:
        parser.error('--follow requiere --execution.')
    spool = None
    if args.follow:
        try:
            spool = Spool(args.estado, args.execution, args.cola_max_mb * 1024 * 1024)
        except ValueError:
            parser.error('--execution debe ser el identificador (GUID) de la fuente.')
    ca_file = args.ca_file or os.getenv('Ml__CaFile') or None
    if ca_file and not Path(ca_file).is_file():
        parser.error('No existe el certificado indicado en Ml__CaFile o --ca-file.')
    api = Api(args.api or os.getenv('Ml__BackendUrl', 'http://localhost:5016/api'), key, ca_file)
    print('Agente HTTP iniciado. Esperando trabajos del backend.', flush=True)
    while True:
        job = None
        try:
            suffix = '?executionId=' + args.execution if args.execution else ''
            job = api.request('/claim' + suffix, {})
            if job:
                print(f"Procesando {job['id']} ({job['kind']})", flush=True)
                process_job(api, job, args.follow, args.ip_ttl_hours, args.max_ips, args.solo_nuevos, spool)
                print('Trabajo finalizado.', flush=True)
        except KeyboardInterrupt:
            return
        except Exception as error:
            print(f'Procesamiento interrumpido ({type(error).__name__}); se conserva el progreso confirmado.', flush=True)
            if isinstance(error, HTTPError):
                print(f'El backend respondió HTTP {error.code}. Verifica la dirección y la clave del agente.', flush=True)
            elif isinstance(error, URLError):
                print('No se pudo conectar al backend. Verifica Ml__BackendUrl y el certificado HTTPS (Ml__CaFile).', flush=True)
            if job and not (isinstance(error, HTTPError) and error.code == 409):
                try:
                    code = 'worker_error'
                    if isinstance(error, ValueError) and str(error) == 'file_changed':
                        code = 'file_changed'
                    elif isinstance(error, HTTPError):
                        detail = error.read(4096).decode(errors='replace')
                        code = 'model_changed' if 'modelo activo cambi' in detail else 'model_unavailable' if error.code in TRANSIENT_HTTP else 'worker_error'
                    elif isinstance(error, (URLError, TimeoutError)):
                        code = 'model_unavailable'
                    api.request(f"/{job['id']}/fail", {'lease': job['lease'], 'code': code})
                except Exception:
                    pass
            if args.once and not (isinstance(error, HTTPError) and error.code == 409):
                raise SystemExit(1)
        if args.once:
            return
        time.sleep(2)


if __name__ == '__main__':
    main()
