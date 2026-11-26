# reglas verificadas contra dimatecIA/filtro.py, sin paquetes externos
import base64
import gzip
import hashlib
import hmac
import json
import io
import math
import re
from collections import defaultdict
from datetime import datetime, timedelta, timezone

MESES = {
    "Jan": 1, "Feb": 2, "Mar": 3, "Apr": 4, "May": 5, "Jun": 6,
    "Jul": 7, "Aug": 8, "Sep": 9, "Oct": 10, "Nov": 11, "Dec": 12,
}

PATRON_LINEA = re.compile(
    r'^(?P<ip>\S+) \S+ \S+ \[(?P<time>[^\]]+)\] '
    r'"(?P<method>[A-Z]+) (?P<uri>.*?) HTTP/\d(?:\.\d)?" '
    r'(?P<status>\d{3}) (?P<bytes>\d+|-) '
    r'"(?P<referer>[^"]*)" "(?P<ua>[^"]*)"'
    r'(?: (?P<req_time>\d+(?:\.\d+)?|-))?'
    r'(?: (?P<upstream_time>[^"\s][^"]*?))?'
    r'(?: "(?P<xff>[^"]*)")?\s*$'
)
_NUMERO = re.compile(r"\d+(?:\.\d+)?")

EXT_ESTATICAS = (
    ".js", ".css", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp", ".avif",
    ".woff", ".woff2", ".ttf", ".eot", ".otf", ".map", ".mp4", ".webm",
)
PATRON_BOT_BENIGNO = re.compile(
    r"googlebot|bingbot|duckduckbot|yandexbot|baiduspider|applebot|slurp|facebookexternalhit"
    r"|uptimerobot|pingdom|statuscake|site24x7|elb-healthchecker|kube-probe|zabbix|nagios"
    r"|prometheus|blackbox|datadog|healthcheck",
    re.IGNORECASE,
)

UMBRAL_SESION = timedelta(minutes=30)
UMBRAL_PERIODO = timedelta(days=3)


def parsear_timestamp_nginx(ts_str: str) -> datetime:
    """Parseo manual (sin strptime/%b): el locale del sistema puede no reconocer
    las abreviaturas de mes en inglés. Formato: 25/Sep/2025:19:14:32 -0400"""
    return datetime(
        int(ts_str[7:11]), MESES[ts_str[3:6]], int(ts_str[0:2]),
        int(ts_str[12:14]), int(ts_str[15:17]), int(ts_str[18:20]),
    )


def segundos_a_ms(texto):
    """'0.045' -> 45.0 ; '-', '' o None -> '' (dato ausente). Si hay varios
    valores ('0.1, 0.2' por reintentos de upstream) toma el primero."""
    if not texto:
        return ""
    m = _NUMERO.match(texto.strip())
    return round(float(m.group()) * 1000, 2) if m else ""


def shannon_entropy(s: str) -> float:
    if not s:
        return 0.0
    freq = defaultdict(int)
    for c in s:
        freq[c] += 1
    n = len(s)
    return -sum((f / n) * math.log2(f / n) for f in freq.values())


PROFILE = 'nginx-request-v1'


class FeatureProcessor:
    """Causal per-request features. Reporting windows never reset this state."""
    def __init__(self, key, checkpoint='', ip_ttl_hours=None, max_ips=None):
        """ip_ttl_hours/max_ips solo para seguimiento continuo (--follow): acotan el estado
        por IP. El modo histórico no los usa y conserva la paridad con el entrenamiento."""
        self.key = key.encode()
        restored = None
        if checkpoint:
            with gzip.GzipFile(fileobj=io.BytesIO(base64.b64decode(checkpoint))) as stream:
                restored = stream.read(64 * 1024 * 1024 + 1)
            if len(restored) > 64 * 1024 * 1024:
                raise ValueError('Checkpoint demasiado grande')
            restored = json.loads(restored)
        self.state = restored if restored is not None else {
            'profile': PROFILE, 'keyId': hashlib.sha256(self.key).hexdigest(), 'ip': {}, 'sessions': {}, 'previous': None, 'lastUtc': None,
            'line': 0, 'offset': 0, 'read': 0, 'valid': 0, 'excluded': 0, 'rejected': 0,
            'empty': 0, 'from': None, 'to': None, 'reasons': {}, 'fileIdentity': None}
        if self.state['profile'] != PROFILE or self.state['keyId'] != hashlib.sha256(self.key).hexdigest():
            raise ValueError('Perfil incompatible')
        # limites fijados en el checkpoint para que una reanudacion calcule igual
        if ip_ttl_hours is not None and self.state.get('ipTtlSeconds') is None:
            if ip_ttl_hours <= 0:
                raise ValueError('ip_ttl_hours debe ser positivo')
            self.state['ipTtlSeconds'] = int(ip_ttl_hours * 3600)
        if max_ips is not None and self.state.get('maxIps') is None:
            if max_ips < 1:
                raise ValueError('max_ips debe ser positivo')
            self.state['maxIps'] = int(max_ips)
        self.state.setdefault('ipEvicted', 0)

    def _prune_ips(self, wall):
        s = self.state
        ttl = s.get('ipTtlSeconds')
        if ttl:
            cutoff = wall - timedelta(seconds=ttl)
            s['ip'] = {k: v for k, v in s['ip'].items() if datetime.fromisoformat(v[0]) >= cutoff}
        limit = s.get('maxIps')
        if limit and len(s['ip']) > limit:
            # ultimo recurso ante trafico masivo: se descartan las IP menos recientes
            keep = sorted(s['ip'].items(), key=lambda item: item[1][0], reverse=True)[:limit]
            s['ipEvicted'] += len(s['ip']) - limit
            s['ip'] = dict(keep)

    def checkpoint(self):
        return base64.b64encode(gzip.compress(json.dumps(self.state, separators=(',', ':'), allow_nan=False).encode())).decode()

    def fingerprint(self, value):
        return hmac.new(self.key, value.encode(), hashlib.sha256).hexdigest()

    def reject(self, reason):
        self.state['rejected'] += 1
        self.state['reasons'][reason] = self.state['reasons'].get(reason, 0) + 1
        return None

    def process(self, raw, offset):
        s = self.state
        s['line'] += 1; s['read'] += 1; s['offset'] = offset
        if raw is None:
            return self.reject('line_too_long')
        line = raw.rstrip('\r\n')
        if not line.strip():
            s['empty'] += 1
            return None
        m = PATRON_LINEA.match(line)
        if not m:
            return self.reject('invalid_format')
        d = m.groupdict()
        try:
            wall = parsear_timestamp_nginx(d['time'])
            zone = datetime.strptime(d['time'][-5:], '%z').tzinfo
            utc = wall.replace(tzinfo=zone).astimezone(timezone.utc)
            if s['lastUtc'] and utc < datetime.fromisoformat(s['lastUtc']):
                return self.reject('out_of_order')
            status = int(d['status'])
            if not 100 <= status <= 599 or len(d['method']) > 32:
                return self.reject('invalid_http_attributes')
        except (ValueError, KeyError, OverflowError):
            return self.reject('invalid_timestamp')
        # the original extractor advances period detection even for excluded static requests
        previous = datetime.fromisoformat(s['previous']) if s['previous'] else None
        if previous and (wall - previous > UMBRAL_PERIODO or wall < previous - timedelta(minutes=5)):
            s['ip'].clear(); s['sessions'].clear()
        s['previous'] = wall.isoformat(); s['lastUtc'] = utc.isoformat()
        # expired sessions are pruned; per-IP error history is kept until the period boundary
        if s['read'] % 2048 == 0:
            cutoff = wall - UMBRAL_SESION
            s['sessions'] = {k: v for k, v in s['sessions'].items() if datetime.fromisoformat(v[0]) >= cutoff}
            self._prune_ips(wall)
        uri = d['uri']
        if uri.split('?', 1)[0].lower().endswith(EXT_ESTATICAS):
            s['excluded'] += 1
            s['reasons']['static_resource'] = s['reasons'].get('static_resource', 0) + 1
            return None
        # training kept bots and duplicate requests and did not use X-Forwarded-For
        subject = self.fingerprint(d['ip'])
        prior = s['ip'].get(subject)
        ttl = s.get('ipTtlSeconds')
        if prior is not None and ttl and (wall - datetime.fromisoformat(prior[0])).total_seconds() > ttl:
            # una IP inactiva mas que el TTL empieza un periodo propio, comprobado al consultar
            prior = None
        interval = None if prior is None else round(max(0., (wall - datetime.fromisoformat(prior[0])).total_seconds() * 1000), 2)
        count = 1 if prior is None else prior[1] + 1
        errors = int(status >= 400) + (prior[2] if prior else 0)
        session_key = self.fingerprint(d['ip'] + '\0' + d['ua'])
        session = s['sessions'].get(session_key)
        session_count = 1 if session is None or wall - datetime.fromisoformat(session[0]) > UMBRAL_SESION else session[1] + 1
        response = segundos_a_ms(d.get('req_time'))
        upstream = segundos_a_ms(d.get('upstream_time'))
        features = {
            'codigo_status': status, 'bytes_enviados': int(d['bytes']) if d['bytes'].isdigit() else 0,
            'tiempo_respuesta_ms': response if response != '' else None,
            'upstream_response_ms': upstream if upstream != '' else None,
            'interval_request_ms': interval, 'request_sessions': session_count,
            'errors_ip': round(min(errors / count, 1.), 4), 'entropy_ruta': round(shannon_entropy(uri), 4),
            'hora': wall.hour, 'dia_semana': wall.weekday(), 'metodo': d['method']}
        if any(isinstance(value, float) and not math.isfinite(value) for value in features.values()):
            return self.reject('non_finite_feature')
        s['ip'][subject] = [wall.isoformat(), count, errors]
        s['sessions'][session_key] = [wall.isoformat(), session_count]
        s['valid'] += 1
        timestamp = utc.isoformat().replace('+00:00', 'Z')
        s['from'] = s['from'] or timestamp; s['to'] = timestamp
        return {'line': s['line'], 'occurredAt': timestamp, 'subject': subject,
                'route': uri.split('?', 1)[0][:2048], 'features': features}

    def batch(self, lease, records, completed=False):
        s = self.state
        return {'lease': lease, 'lastLine': s['line'], 'offset': s['offset'],
                **{k: s[k] for k in ('read', 'valid', 'excluded', 'rejected', 'empty', 'from', 'to', 'reasons')},
                'checkpoint': self.checkpoint(), 'completed': completed, 'records': records}


