#!/usr/bin/env bash
# Installiert neue Stände von main automatisch. Läuft im Ohmventory-Container über den systemd-Timer
# ohmventory-update.timer (alle 5 Minuten), eingerichtet von deploy/proxmox/install-lxc.sh.
#
# Ablauf: neuen Commit holen → warten, bis die CI (backend, frontend) dafür grün ist → bauen und starten
# → Health-Check. Schlägt der Start fehl, wird der vorherige Stand wiederhergestellt und der Commit
# übersprungen, bis ein neuer kommt. Fehlgeschlagene CI wird nie installiert.
#
# Logs: journalctl -u ohmventory-update
# Von Hand: systemctl start ohmventory-update
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/ohmventory}"
BRANCH="${BRANCH:-main}"
REPO_SLUG="${REPO_SLUG:-Marcel-B/Ohmventory}"
GITHUB_API="${GITHUB_API:-https://api.github.com}"
REQUIRED_CHECKS="${REQUIRED_CHECKS:-backend frontend}"
REQUIRE_CI="${REQUIRE_CI:-1}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-300}"
STATE_DIR="${STATE_DIR:-/var/lib/ohmventory-update}"
LOCK_FILE="${LOCK_FILE:-/run/lock/ohmventory-update.lock}"

log() { echo "$*"; }

mkdir -p "$STATE_DIR" "$(dirname "$LOCK_FILE")"
exec 9>"$LOCK_FILE"
flock -n 9 || { log "Update läuft bereits"; exit 0; }

cd "$APP_DIR"
web_port=$(sed -n 's/^WEB_PORT=//p' .env 2>/dev/null | tail -n 1)
web_port="${web_port:-8080}"

git fetch --quiet origin "$BRANCH"
current=$(git rev-parse HEAD)
target=$(git rev-parse "origin/$BRANCH")
[[ $current != "$target" ]] || exit 0

if grep -qx "$target" "$STATE_DIR/skipped" 2>/dev/null; then
  exit 0
fi

# Gibt success, pending oder failure für einen Commit zurück.
ci_state() {
  local json
  if ! json=$(curl -fsS -m 20 -H "Accept: application/vnd.github+json" \
      "$GITHUB_API/repos/$REPO_SLUG/commits/$1/check-runs?per_page=100"); then
    echo pending
    return
  fi
  local name status conclusion
  for name in $REQUIRED_CHECKS; do
    status=$(jq -r --arg n "$name" '[.check_runs[] | select(.name == $n)][0].status // "missing"' <<<"$json")
    conclusion=$(jq -r --arg n "$name" '[.check_runs[] | select(.name == $n)][0].conclusion // ""' <<<"$json")
    if [[ $status != completed ]]; then
      echo pending
      return
    fi
    if [[ $conclusion != success ]]; then
      echo failure
      return
    fi
  done
  echo success
}

if [[ $REQUIRE_CI == 1 ]]; then
  case $(ci_state "$target") in
    pending)
      log "Neuer Stand ${target:0:7}: CI läuft noch oder ist nicht erreichbar, nächster Versuch beim nächsten Lauf"
      exit 0 ;;
    failure)
      log "Neuer Stand ${target:0:7}: CI ist rot, wird nicht installiert"
      echo "$target" >>"$STATE_DIR/skipped"
      exit 0 ;;
  esac
fi

healthy() {
  local deadline=$((SECONDS + HEALTH_TIMEOUT))
  while ((SECONDS < deadline)); do
    if curl -fsS -m 5 "http://127.0.0.1:$web_port/api/health" >/dev/null 2>&1; then
      return 0
    fi
    sleep 5
  done
  return 1
}

deploy() {
  git reset --quiet --hard "$1"
  docker compose up -d --build --remove-orphans && healthy
}

log "Update ${current:0:7} → ${target:0:7}: $(git log -1 --format=%s "$target")"
if deploy "$target"; then
  log "Update auf ${target:0:7} erfolgreich"
  docker image prune -f >/dev/null 2>&1 || true
  exit 0
fi

log "Update auf ${target:0:7} fehlgeschlagen, zurück auf ${current:0:7}"
echo "$target" >>"$STATE_DIR/skipped"
if deploy "$current"; then
  log "Vorheriger Stand ${current:0:7} läuft wieder"
else
  log "Auch der vorherige Stand startet nicht – bitte von Hand prüfen: docker compose -f $APP_DIR/docker-compose.yml logs"
fi
exit 1
