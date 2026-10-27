#!/usr/bin/env bash
# Legt auf dem Proxmox-Host einen LXC-Container mit Docker an und startet Ohmventory darin.
# Mit der CT-ID eines bestehenden Containers installiert bzw. aktualisiert es Ohmventory darin
# (fehlendes Docker wird nachinstalliert); .env und Daten bleiben erhalten.
#
# Aufruf auf dem Proxmox-Host als root, z. B.:
#   bash install-lxc.sh
#   CTID=120 IP=192.168.2.130/24 GATEWAY=192.168.2.1 bash install-lxc.sh
#   CTID=120 bash install-lxc.sh            # Update eines bestehenden Containers
#
# Alle Einstellungen über Umgebungsvariablen (Standardwerte in Klammern):
#   CTID            Container-ID (nächste freie)
#   CT_HOSTNAME     Hostname (ohmventory)
#   STORAGE         Storage für das Root-Dateisystem (local-lvm)
#   TEMPLATE_STORAGE Storage für das Debian-Template (local)
#   BRIDGE          Netzwerkbrücke (vmbr0)
#   IP              dhcp oder statisch mit Präfix, z. B. 192.168.2.130/24 (dhcp)
#   GATEWAY         Gateway bei statischer IP
#   CORES / MEMORY / SWAP / DISK   (2 / 2048 MB / 512 MB / 16 GB)
#   REPO_URL / BRANCH              (https://github.com/Marcel-B/Ohmventory.git / main)
#   LLM_ENDPOINT    LM Studio auf oi-o-mat (http://192.168.2.122:1234/v1)
#   LLM_MODEL       (gemma-4-e4b-it-mlx)
#   WEB_PORT        Port der App im Container (8080)
#   AUTO_UPDATE     1: neue Stände von BRANCH automatisch installieren (siehe deploy/auto-update.sh), 0: aus (1)
set -euo pipefail

CTID="${CTID:-}"
CT_HOSTNAME="${CT_HOSTNAME:-ohmventory}"
STORAGE="${STORAGE:-local-lvm}"
TEMPLATE_STORAGE="${TEMPLATE_STORAGE:-local}"
BRIDGE="${BRIDGE:-vmbr0}"
IP="${IP:-dhcp}"
GATEWAY="${GATEWAY:-}"
CORES="${CORES:-2}"
MEMORY="${MEMORY:-2048}"
SWAP="${SWAP:-512}"
DISK="${DISK:-16}"
REPO_URL="${REPO_URL:-https://github.com/Marcel-B/Ohmventory.git}"
BRANCH="${BRANCH:-main}"
LLM_ENDPOINT="${LLM_ENDPOINT:-http://192.168.2.122:1234/v1}"
LLM_MODEL="${LLM_MODEL:-gemma-4-e4b-it-mlx}"
WEB_PORT="${WEB_PORT:-8080}"
AUTO_UPDATE="${AUTO_UPDATE:-1}"
APP_DIR=/opt/ohmventory

log() { printf '\033[1;32m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m!!\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31mFehler:\033[0m %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "Bitte als root auf dem Proxmox-Host ausführen."
command -v pct >/dev/null || die "pct nicht gefunden – läuft das Skript auf dem Proxmox-Host?"

in_ct() { pct exec "$CTID" -- bash -c "$1"; }

create_container() {
  log "Debian-Template suchen"
  pveam update >/dev/null
  local template
  template=$(pveam available --section system | awk '/debian-12-standard/ {print $2}' | sort -V | tail -n 1)
  [[ -n $template ]] || die "Kein debian-12-standard-Template gefunden."
  if ! pveam list "$TEMPLATE_STORAGE" | grep -q "$template"; then
    log "Template $template herunterladen"
    pveam download "$TEMPLATE_STORAGE" "$template"
  fi

  local net="name=eth0,bridge=$BRIDGE,ip=$IP"
  if [[ $IP != dhcp ]]; then
    [[ -n $GATEWAY ]] || die "Bei statischer IP bitte GATEWAY angeben."
    net+=",gw=$GATEWAY"
  fi

  log "Container $CTID ($CT_HOSTNAME) anlegen"
  # nesting und keyctl braucht Docker in einem unprivilegierten Container.
  pct create "$CTID" "$TEMPLATE_STORAGE:vztmpl/$template" \
    --hostname "$CT_HOSTNAME" \
    --cores "$CORES" --memory "$MEMORY" --swap "$SWAP" \
    --rootfs "$STORAGE:$DISK" \
    --net0 "$net" \
    --features nesting=1,keyctl=1 \
    --unprivileged 1 \
    --onboot 1 \
    --start 1

  log "Auf Netzwerk warten"
  for _ in $(seq 1 60); do
    if in_ct "getent hosts deb.debian.org >/dev/null 2>&1"; then
      break
    fi
    sleep 2
  done
  in_ct "getent hosts deb.debian.org >/dev/null" || die "Container hat kein Netzwerk."
}

# Auch für Container, die von Hand angelegt wurden (Debian/Ubuntu): fehlende Pakete und Docker nachinstallieren.
ensure_docker() {
  if in_ct "command -v docker >/dev/null && command -v git >/dev/null && command -v openssl >/dev/null && command -v curl >/dev/null && command -v jq >/dev/null"; then
    return
  fi
  log "Docker und Hilfsprogramme installieren"
  in_ct "export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq ca-certificates curl git jq openssl >/dev/null
    command -v docker >/dev/null || curl -fsSL https://get.docker.com | sh >/dev/null
    systemctl enable --now docker >/dev/null"
  in_ct "docker info >/dev/null 2>&1" || die "Docker startet nicht. Hat der Container die Features nesting=1 und keyctl=1? (pct set $CTID --features nesting=1,keyctl=1, dann neu starten)"
}

# systemd-Timer im Container, der deploy/auto-update.sh regelmäßig ausführt.
setup_auto_update() {
  if [[ $AUTO_UPDATE != 1 ]]; then
    in_ct "systemctl disable --now ohmventory-update.timer >/dev/null 2>&1 || true"
    log "Auto-Update ist abgeschaltet"
    return
  fi
  log "Auto-Update einrichten (alle 5 Minuten)"
  in_ct "cat > /etc/systemd/system/ohmventory-update.service <<UNIT
[Unit]
Description=Ohmventory: neuen Stand von $BRANCH installieren
Wants=network-online.target docker.service
After=network-online.target docker.service

[Service]
Type=oneshot
Environment=APP_DIR=$APP_DIR BRANCH=$BRANCH
ExecStart=$APP_DIR/deploy/auto-update.sh
UNIT
cat > /etc/systemd/system/ohmventory-update.timer <<UNIT
[Unit]
Description=Ohmventory regelmäßig aktualisieren

[Timer]
OnBootSec=5min
OnUnitActiveSec=5min
RandomizedDelaySec=30

[Install]
WantedBy=timers.target
UNIT
systemctl daemon-reload
systemctl enable --now ohmventory-update.timer >/dev/null 2>&1"
}

install_app() {
  if in_ct "test -d $APP_DIR/.git"; then
    log "Ohmventory aktualisieren ($BRANCH)"
    in_ct "cd $APP_DIR && git fetch --quiet origin $BRANCH && git checkout --quiet $BRANCH && git reset --quiet --hard origin/$BRANCH"
  else
    log "Ohmventory klonen ($BRANCH)"
    in_ct "git clone --quiet --branch $BRANCH $REPO_URL $APP_DIR"
  fi

  local ct_ip
  ct_ip=$(in_ct "hostname -I | awk '{print \$1}'")
  [[ -n $ct_ip ]] || die "IP des Containers nicht ermittelbar."

  if in_ct "test -f $APP_DIR/.env"; then
    log ".env existiert bereits und bleibt unverändert"
  else
    log ".env mit zufälligen Passwörtern anlegen"
    in_ct "cat > $APP_DIR/.env <<ENV
POSTGRES_PASSWORD=\$(openssl rand -hex 16)
SEARXNG_SECRET=\$(openssl rand -hex 32)
LLM_ENDPOINT=$LLM_ENDPOINT
LLM_MODEL=$LLM_MODEL
LLM_CONTEXT_LENGTH=8192
LLM_CONTROL_TOKEN=
WEB_PORT=$WEB_PORT
PUBLIC_BASE_URL=http://$ct_ip:$WEB_PORT
ENV
chmod 600 $APP_DIR/.env"
  fi

  log "Container bauen und starten (dauert beim ersten Mal einige Minuten)"
  in_ct "cd $APP_DIR && docker compose up -d --build --remove-orphans"

  log "Auf die App warten"
  for _ in $(seq 1 60); do
    if in_ct "curl -fsS http://127.0.0.1:$WEB_PORT/api/health >/dev/null 2>&1"; then
      break
    fi
    sleep 3
  done
  in_ct "curl -fsS http://127.0.0.1:$WEB_PORT/api/health >/dev/null" || warn "App antwortet noch nicht – Logs: pct exec $CTID -- docker compose -f $APP_DIR/docker-compose.yml logs api"

  if in_ct "curl -fsS -m 5 ${LLM_ENDPOINT%/}/models >/dev/null 2>&1"; then
    log "LM Studio unter $LLM_ENDPOINT erreichbar"
  else
    warn "LM Studio unter $LLM_ENDPOINT nicht erreichbar. Läuft der Server mit "Serve on Local Network"?"
  fi

  setup_auto_update

  cat <<SUMMARY

Ohmventory läuft: http://$ct_ip:$WEB_PORT  (Container $CTID)

Für yue-guard auf oi-o-mat (~/.config/yue-tools/guard.d/ohmventory.conf):

PAUSE='ssh proxmox "pct exec $CTID -- curl -fsS -X POST http://127.0.0.1:$WEB_PORT/api/llm/pause"'
RESUME='ssh proxmox "pct exec $CTID -- curl -fsS -X POST http://127.0.0.1:$WEB_PORT/api/llm/resume"'
MODELS='$LLM_MODEL'
PAUSE_FOR='lyrics images ace singing speech voices'

Auto-Update: Neue Stände von $BRANCH werden alle 5 Minuten installiert, sobald die CI grün ist
(abschalten mit AUTO_UPDATE=0; Logs: pct exec $CTID -- journalctl -u ohmventory-update).
SUMMARY
}

if [[ -n $CTID ]] && pct status "$CTID" >/dev/null 2>&1; then
  log "Container $CTID existiert, Ohmventory darin installieren bzw. aktualisieren"
  [[ $(pct status "$CTID") == *running* ]] || pct start "$CTID"
else
  CTID="${CTID:-$(pvesh get /cluster/nextid)}"
  create_container
fi

ensure_docker
install_app
