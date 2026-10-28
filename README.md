# Ohmventory

Lagerverwaltung für Elektronikbauteile mit Fotoerkennung und Projektvorschlägen über ein lokales LLM.

- **Erfassen:** Foto eines Bauteils oder Breakout-Boards hochladen, ein Vision-Modell erkennt es, du bestätigst und gibst Menge und Container an. Ist keine Typbezeichnung lesbar, liest ein zweiter Durchgang die Aufdrucke (auch senkrechte, über gedrehte Ansichten), sucht mit Modulaufdrucken wie `1.30" IIC V2.2` per SearXNG im Web und bestimmt das konkrete Board; Bestückungsdrucke (C1, R1) und Pin-Namen zählen dabei nicht. Die App zeigt die Quellen an.
- **Container:** Lagerorte (z. B. gedruckte Catch-all-Trays) mit kurzen Codes wie `T-001`, verschachtelbar, mit druckbaren QR-Etiketten. Der QR-Code öffnet die Inhaltsansicht des Containers.
- **Projektassistent:** Frag z. B. „Kann ich ein Thermometer bauen?“. Das Modell durchsucht Lager und Web (SearXNG), schlägt Projekte mit Stückliste vor, das Backend gleicht die Stückliste mit dem Bestand ab, und Fehlendes landet auf Knopfdruck in der Einkaufsliste.

## Aufbau

| Teil | Technik |
| --- | --- |
| `backend/` | ASP.NET Core (.NET 10), Minimal APIs, EF Core + PostgreSQL, Microsoft.Extensions.AI |
| `frontend/` | Vue 3, TypeScript, Vite, Tailwind CSS 4, Pinia, PrimeVue 4 |
| LLM | LM Studio auf oi-o-mat hinter der LLM-Schranke (Port 1235), Standardmodell `gemma-4-e4b-it-mlx` |
| Websuche | SearXNG im Container |

Das LLM wird über die OpenAI-kompatible API der LLM-Schranke angesprochen; Adresse, App-Name und Modelle stehen in der Konfiguration (`LlmGate:*`, `Llm:*`). Ob ein Bauteil vorhanden ist, entscheidet nicht das Modell, sondern ein deterministischer Abgleich im Backend (`InventoryMatcher`).

## LLM-Schranke auf oi-o-mat

LM Studio auf oi-o-mat teilt sich den Speicher mit Tonwerk-AI, das immer Vorrang hat. Ohmventory spricht LM Studio deshalb nie direkt an (Port 1234), sondern nur die **LLM-Schranke** davor (`http://192.168.2.122:1235/v1`, Beschreibung in `docs/llm-gate.md` im Repository `Marcel-B/scripts`). Die Schranke reiht die Anfragen ein, hält sie zurück, solange Tonwerk arbeitet, und lädt und entlädt die Modelle selbst.

- **Anmeldung beim Start:** `PUT /gate/apps/ohmventory` mit beiden Modellen, Priorität (20), `holdFor` und Notiz aus `LlmGate:*` in `appsettings.json`. Ist die Schranke nicht erreichbar, versucht es das Backend weiter (10 s, verdoppelt bis 10 min); die App startet trotzdem. Was in der Oberfläche `/gate` festgelegt ist, gewinnt immer.
- **Freigabe:** Bis Marcel die Modelle unter <http://192.168.2.122:1235/gate> für `ohmventory` freigibt oder installiert, antwortet die Schranke mit 403. Erkennung und Assistent zeigen dann „noch nicht freigegeben“.
- **Fehler der Schranke:** 503 wird nach `Retry-After` wiederholt, insgesamt bis `Llm:Timeout` (70 min). 403, 424 und 507 sind Konfigurationsfehler: Sie stehen im Log, und für `Llm:ConfigErrorCooldown` (2 min) wird nicht erneut gefragt. Abgebrochene Antworten (Modell mitten in der Antwort entladen) und 502 werden bis zu 5 Minuten mit Backoff wiederholt. Gespeichert wird erst, wenn die Antwort vollständig ist.
- **Zwei Modelle:** Die Fotoerkennung nutzt `gemma-4-26b-a4b-it-qat-mlx` (≈ 16 GB, MoE mit 4B aktiven Parametern, kann Bilder), Projektassistent und Websuche das kleine `gemma-4-e4b-it-mlx` (≈ 7 GB, Tool-Calls). Umstellen mit `LLM_VISION_MODEL` und `LLM_MODEL`. Das große Modell passt nicht neben die großen Tonwerk-Modelle, deshalb ist `holdFor` leer: Ohmventory weicht bei jeder Tonwerk-Arbeit. In `/gate` muss `ramBudgetGb` mindestens etwa 20 sein, sonst lehnt die Schranke das große Modell mit 507 ab.
- **Feste Kontextlänge:** Die Schranke lädt das Modell mit `Llm:ContextLength` (8192). Anfragen werden vorab auf dieses Budget geschätzt; lange Werkzeugergebnisse und Recherchetexte werden gekürzt, statt abgelehnt zu werden.
- **Lange Wartezeiten:** nginx wartet bis zu 75 Minuten auf `/api/`, damit eine zurückgehaltene Anfrage nicht vorher abbricht.
- **Pausenwächter (optional):** Anhalten muss yue-guard Ohmventory nicht, denn außer dem Modell belegt es nichts auf oi-o-mat. Ein Eintrag nach [`docker/yue-guard/ohmventory.conf`](docker/yue-guard/ohmventory.conf) sorgt aber dafür, dass der Wächter das Modell entlädt, sobald Tonwerk anfängt (`MODELS`); `POST /api/llm/pause` und `/resume` bleiben dafür erhalten (idempotent, Zustand übersteht Neustarts).

## Installation auf Proxmox (empfohlen)

Auf dem Proxmox-Host als root:

```bash
curl -fsSLO https://raw.githubusercontent.com/Marcel-B/Ohmventory/main/deploy/proxmox/install-lxc.sh
bash install-lxc.sh                                        # DHCP, nächste freie CT-ID
CTID=120 IP=192.168.2.130/24 GATEWAY=192.168.2.1 bash install-lxc.sh   # statische IP
```

Das Skript legt einen unprivilegierten Debian-12-Container mit Docker an (`nesting`, `keyctl`, Autostart), klont das Repository nach `/opt/ohmventory`, erzeugt eine `.env` mit zufälligen Passwörtern, startet den Stack und prüft, ob die LLM-Schranke erreichbar ist. Alle Optionen stehen im Kopf des Skripts.

Update von Hand: dasselbe Skript mit der CT-ID des bestehenden Containers aufrufen (`CTID=120 bash install-lxc.sh`). `.env` und Daten bleiben erhalten.

### Auto-Update

Das Installationsskript richtet im Container den systemd-Timer `ohmventory-update.timer` ein. Er führt alle 5 Minuten [`deploy/auto-update.sh`](deploy/auto-update.sh) aus:

1. Gibt es einen neuen Commit auf `main`, wartet das Skript, bis die CI-Checks `backend` und `frontend` dafür grün sind. Ein Commit mit roter CI wird nie installiert.
2. Dann baut und startet es den neuen Stand und prüft `/api/health`.
3. Startet die App nicht, kehrt es zum vorherigen Stand zurück und überspringt diesen Commit, bis ein neuer kommt.

Logs: `pct exec <CTID> -- journalctl -u ohmventory-update`. Sofort prüfen: `pct exec <CTID> -- systemctl start ohmventory-update`. Abschalten: `AUTO_UPDATE=0 CTID=<CTID> bash install-lxc.sh`.

Hinweis: Datenbank-Migrationen laufen beim Start der API. Ein Zurückrollen setzt den Code zurück, nicht die Datenbank.

## Starten mit Docker

1. Auf oi-o-mat muss die LLM-Schranke laufen (`bin/llm-gate status` im scripts-Repo); das Modell lädt sie selbst.
2. `cp .env.example .env`, ein `POSTGRES_PASSWORD` eintragen und `PUBLIC_BASE_URL` auf die LAN-Adresse des Rechners setzen, damit QR-Codes vom Handy aus funktionieren.
3. `docker compose up -d --build`
4. App unter <http://localhost:8080> öffnen.
5. Das Modell unter <http://192.168.2.122:1235/gate> für `ohmventory` freigeben (siehe oben).

## Entwicklung

```bash
# Datenbank und Suche (POSTGRES_PASSWORD muss in .env gesetzt sein)
docker compose up -d db searxng

# Backend (http://localhost:5080); Passwort einmalig als User-Secret hinterlegen
cd backend
dotnet user-secrets set --project src/Ohmventory.Api "ConnectionStrings:Ohmventory" \
  "Host=localhost;Database=ohmventory;Username=ohmventory;Password=<dein Passwort>"
dotnet run --project src/Ohmventory.Api --urls http://localhost:5080
dotnet test

# Frontend (http://localhost:5173, /api wird an das Backend weitergeleitet)
cd frontend
npm install
npm run dev
```

Neue Migration: `cd backend && dotnet tool restore && dotnet ef migrations add <Name> -p src/Ohmventory.Api -o Data/Migrations`

Die OpenAPI-Beschreibung liegt im Development-Modus unter `/openapi/v1.json`.
