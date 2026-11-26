# Agente HTTP: sigue un access.log de NGINX y envía lotes a la API del sistema. Solo biblioteca estándar.
# Construir desde la raíz del repositorio: docker build -f deploy/docker/agente.Dockerfile .
FROM python:3.11-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /app
COPY agente-http/features.py agente-http/worker.py ./
# Usuario sin privilegios; para leer /var/log/nginx se añade el grupo del host (adm, GID 4) en compose.
RUN useradd --system --uid 10001 --no-create-home agente \
 && mkdir -p /var/lib/dismac-agente && chown agente /var/lib/dismac-agente
# Cola local persistente: lotes pendientes de confirmar por la API (volumen en compose).
ENV AGENTE_ESTADO=/var/lib/dismac-agente
USER agente
# AGENTE_LOG: nombre del archivo dentro de /logs. AGENTE_EJECUCION: ID de la fuente registrada en Web.
# Sin AGENTE_EJECUCION el contenedor explica qué falta y queda en espera en lugar de reiniciarse sin fin;
# al definirla en deploy/.env, `docker compose --profile agente up -d` lo recrea con el valor.
ENTRYPOINT ["sh", "-c", "if [ -z \"${AGENTE_EJECUCION}\" ]; then echo 'AGENTE_EJECUCION está vacío: registra la fuente en Web (Monitoreo) y define su ID en deploy/.env. El agente queda en espera.' >&2; exec sleep infinity; fi; exec python worker.py --env /dev/null --follow \"/logs/${AGENTE_LOG:?Define AGENTE_LOG}\" --execution \"${AGENTE_EJECUCION}\" $([ \"${AGENTE_SOLO_NUEVOS:-1}\" = 1 ] && echo --solo-nuevos)"]
