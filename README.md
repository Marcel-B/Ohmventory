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
| LLM | LM Studio (oder jeder andere OpenAI-kompatible Server) auf dem Host |
| Websuche | SearXNG im Container |

Das LLM wird über die OpenAI-kompatible API angesprochen; Endpoint und Modellnamen stehen in der Konfiguration (`Llm:*`) und lassen sich frei tauschen. Ob ein Bauteil vorhanden ist, entscheidet nicht das Modell, sondern ein deterministischer Abgleich im Backend (`InventoryMatcher`).

## Starten mit Docker

1. In LM Studio unter *Developer* den lokalen Server starten (Port 1234) und das Modell laden.
2. `cp .env.example .env` und `PUBLIC_BASE_URL` auf die LAN-Adresse des Rechners setzen, damit QR-Codes vom Handy aus funktionieren.
3. `docker compose up -d --build`
4. App unter <http://localhost:8080> öffnen.

Erreicht der API-Container LM Studio nicht, in LM Studio „Serve on Local Network“ aktivieren.

## Entwicklung

```bash
# Datenbank und Suche
docker compose up -d db searxng

# Backend (http://localhost:5080)
cd backend
dotnet run --project src/Ohmventory.Api --urls http://localhost:5080
dotnet test

# Frontend (http://localhost:5173, /api wird an das Backend weitergeleitet)
cd frontend
npm install
npm run dev
```

Neue Migration: `cd backend && dotnet tool restore && dotnet ef migrations add <Name> -p src/Ohmventory.Api -o Data/Migrations`

Die OpenAPI-Beschreibung liegt im Development-Modus unter `/openapi/v1.json`.
