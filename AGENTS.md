# AGENTS.md

## Repo state

- An existing modular monolith is present under `src/`, with React in `src/Desarrollo.Web/ClientApp`, EF migrations and SQL Server integration tests. Inspect current code before extending it; preserve existing work.
- `crm.md` is the authoritative, complete product specification (written in Spanish). Read it in full before building. `crm.txt` is an empty placeholder.
- `Desarrollo.slnx` is the solution. The task diagram extends the same WorkItem records used by Board, List and task detail. Demo setup is explicit and isolated; see `docs/mapa-tareas.md`.

## Mandated stack (non-negotiable)

- C# / ASP.NET Core MVC (host + backend), React + TypeScript (frontend), EF Core (data access), SQL Server (relational DB).
- Do NOT substitute SQL Server (no SQLite/Postgres), and do NOT replace React with Blazor or a full-Razor UI.
- MVC interpretation: a single Razor view mounts React as the shell; controllers return JSON. React owns navigation, screens, editor, table, board, calendar, panels. Server owns auth, validation, business rules. Do not duplicate screens in two tech stacks.
- Modular monolith only — no microservices, no multi-DB.

## Project layout

- Suggested namespaces: `Desarrollo.Domain` (entities/rules), `Desarrollo.Application` (use cases/contracts), `Desarrollo.Infrastructure` (EF Core + adapters), `Desarrollo.Web` (MVC, Razor shell, React, hubs), plus test projects and a persistent background worker (in-host or separate project).
- Server-side authorization on every query/operation; never hide restricted data with CSS. Use DTOs, never expose EF entities.

## Execution phases

- Fases 0–3 = the first complete version. Do not stop at a mockup, CRUD, or fixed-data frontend.
- Fase 0: inspection, architecture, routes, schema, permission matrix, states, visual tokens, traceable requirements plan.
- Future/roadmap features (co-editing, advanced metrics, client portal, AI, etc.) stay documented as backlog — never present them as built.

## Hard rules / gotchas

- Demo seed data (GOGO Deliveri, Odontari, TEresuelvo) is dev/demo only — never in production or normal startup.
- Don't commit secrets, send real emails, fire real webhooks/external integrations, or deploy to production without explicit authorization.
- Test against real SQL Server where its behavior matters; don't substitute an in-memory DB for those verifications. Cover negative permission cases.
- The reference images are visual guides only; their numbers/names/percentages are not authoritative data.
- Required dark palette tokens: bg `#0B1020`, nav `#10172A`, main panel `#172238`, lavender `#9B8AFB` (primary), turquoise `#57D6C7` (success), amber `#EAC385` (warn), coral (error only), text `#F0F3FC` / `#A8B7D1`.
