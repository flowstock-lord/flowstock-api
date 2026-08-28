# FlowStock API — documentation

| Document | Contents |
| --- | --- |
| [PLAN.md](PLAN.md) | The full development plan: domain model, API surface, error codes, phases 0–12, MVP definition of done. The reference document. |
| [../CLAUDE.md](../CLAUDE.md) | Working agreement for coding agents: architecture rules, conventions, current phase. |

## Planned documents

These are required by section 38 of the plan and will be added as the phases that produce them
land — do not create empty placeholders ahead of time:

- `architecture.md` — module boundaries, layering, dependency rules (Phase 0).
- `setup.md` — local development setup, Docker Compose, running migrations (Phase 0).
- `environment.md` — environment variables and configuration (Phase 0).
- `database.md` — schema, constraints, indexes, migration workflow (Phase 0+).
- `authentication.md` — JWT, roles, authorization policies (Phase 1).
- `api-conventions.md` — pagination, filtering, sorting, error envelope, status codes (Phase 2).
- `domain.md` — domain concepts: stock, movements, BOM, production orders (Phase 4+).
- `workflow.md` — branching, testing and review workflow.

Swagger/OpenAPI is the live API reference and must stay accurate; these documents explain the
things Swagger cannot.
