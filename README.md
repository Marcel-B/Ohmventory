# Ohmventory

Lagerverwaltung für Elektronikbauteile mit Fotoerkennung und Projektvorschlägen über ein lokales LLM.

- **Erfassen:** Foto eines Bauteils oder Breakout-Boards hochladen, ein Vision-Modell erkennt es, du bestätigst und gibst Menge und Container an.
- **Container:** Lagerorte (z. B. gedruckte Catch-all-Trays) mit kurzen Codes wie `T-001`, verschachtelbar, mit druckbaren QR-Etiketten. Der QR-Code öffnet die Inhaltsansicht des Containers.
- **Projektassistent:** Frag z. B. „Kann ich ein Thermometer bauen?“. Das Modell durchsucht Lager und Web (SearXNG), schlägt Projekte mit Stückliste vor, das Backend gleicht die Stückliste mit dem Bestand ab, und Fehlendes landet auf Knopfdruck in der Einkaufsliste.

## Aufbau

| Teil | Technik |
| --- | --- |
| `backend/` | ASP.NET Core (.NET 10), Minimal APIs, EF Core + PostgreSQL, Microsoft.Extensions.AI |
| `frontend/` | Vue 3, TypeScript, Vite, Tailwind CSS 4, Pinia, PrimeVue 4 |
| LLM | LM Studio auf oi-o-mat (oder jeder andere OpenAI-kompatible Server), Standardmodell `gemma-4-e4b-it-mlx` |
| Websuche | SearXNG im Container |

Das LLM wird über die OpenAI-kompatible API angesprochen; Endpoint und Modell stehen in der Konfiguration (`Llm:*`). Ob ein Bauteil vorhanden ist, entscheidet nicht das Modell, sondern ein deterministischer Abgleich im Backend (`InventoryMatcher`).

## Zusammenspiel mit Tonwerk und yue-guard

LM Studio auf oi-o-mat teilt sich den Speicher mit Tonwerk-AI. Damit Ohmventory nicht stört:

- **Kleines, festes Modell:** `gemma-4-e4b-it-mlx` (≈ 7 GB, kann Bilder und Tool-Calls).
- **Feste Kontextlänge:** Vor jeder Anfrage prüft das Backend über `GET /api/v1/models`, ob das Modell geladen ist, und lädt es sonst mit `POST /api/v1/models/load` und `context_length` 8192. Andere Modelle werden nie angefasst, entladen wird nichts.
- **Eingaben gekürzt:** Anfragen werden vorab auf das Kontextbudget geschätzt; lange Werkzeugergebnisse und Recherchetexte werden gekürzt, statt an LM Studio zu scheitern.
- **Entladenes Modell:** Verbindungs- und Serverfehler werden bis zu 5 Minuten mit Backoff wiederholt. Gespeichert wird erst, wenn die Antwort vollständig ist.
- **Anhaltbar:** `POST /api/llm/pause` und `POST /api/llm/resume` (idempotent, Zustand übersteht Neustarts). Während der Pause antworten Erkennung und Assistent sofort mit 503, die App zeigt einen Hinweis; Lager, Container und Einkaufsliste laufen weiter. `GET /api/llm/status` zeigt den Zustand.

Die Vorlage für den Wächter liegt in [`docker/yue-guard/ohmventory.conf`](docker/yue-guard/ohmventory.conf). Danach mit `yue-guard status` prüfen und z. B. in Tonwerk „Cover malen“ starten: nach höchstens 30 s steht `ohmventory: angehalten`.

## Installation auf Proxmox (empfohlen)

Auf dem Proxmox-Host als root:

```bash
curl -fsSLO https://raw.githubusercontent.com/Marcel-B/Ohmventory/main/deploy/proxmox/install-lxc.sh
bash install-lxc.sh                                        # DHCP, nächste freie CT-ID
CTID=120 IP=192.168.2.130/24 GATEWAY=192.168.2.1 bash install-lxc.sh   # statische IP
```

Das Skript legt einen unprivilegierten Debian-12-Container mit Docker an (`nesting`, `keyctl`, Autostart), klont das Repository nach `/opt/ohmventory`, erzeugt eine `.env` mit zufälligen Passwörtern, startet den Stack, prüft, ob LM Studio erreichbar ist, und gibt am Ende die fertige yue-guard-Konfiguration mit der richtigen CT-ID aus. Alle Optionen stehen im Kopf des Skripts.

Update: dasselbe Skript mit der CT-ID des bestehenden Containers aufrufen (`CTID=120 bash install-lxc.sh`). `.env` und Daten bleiben erhalten.

## Starten mit Docker

1. In LM Studio muss der Server laufen und „Serve on Local Network“ an sein; das Modell lädt Ohmventory selbst.
2. `cp .env.example .env`, ein `POSTGRES_PASSWORD` eintragen und `PUBLIC_BASE_URL` auf die LAN-Adresse des Rechners setzen, damit QR-Codes vom Handy aus funktionieren.
3. `docker compose up -d --build`
4. App unter <http://localhost:8080> öffnen.

5. yue-guard-Eintrag anlegen (siehe oben).

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
