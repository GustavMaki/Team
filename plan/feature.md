# Feature: a platform AI catalogue for the support assistant (#512)

## Problem
The platform has exactly one model: the single `IChatClient` the host registers for every team. A host that wants to offer
teams a vetted list of models over several providers, managed by its staff, builds all of it itself.

## Decisions
- One PR, built in layers (agreed with the user). Opt-in: a host that registers nothing keeps today's single platform model.
- **The catalogue is one document** (providers with encrypted keys, models, the default), so a staff change is atomic and the
  platform chat client reads one thing per call. `IPlatformAiCatalogueStore` (contract in `Tharga.Team`, Mongo in
  `Tharga.Team.MongoDB`); keys are protected with Data Protection under their own purpose.
- **`IPlatformAiCatalogueService`**: operations, not CRUD, behind one **system scope** `support:aicatalogue:manage`
  (`SystemSupportScopes.AiCatalogueManage`), which no level and no role holds by default: get; set provider (key kept when
  omitted, removed when empty); remove provider; list a provider's models for its stored key; set models and default; test a model.
  Each audited; the test as access. Authorization decorator, then auditing decorator, as the per-team connection has.
- **A team's choice**: `StoredSupportAiConnection`/`SupportAiConnection` get `PlatformModelId`. `UsePlatformModelAsync(teamKey, modelId)`
  is a default member (null keeps today's behaviour, the default model). `GetChoicesAsync` lists the offered models, with no keys.
  A team whose chosen model is no longer offered is answered by the default model.
- **The platform chat client** (`PlatformCatalogueChatClient`) reads the catalogue on every call, so a change applies at once, and
  builds each provider's client through `ISupportChatClientFactory` (so a provider with its own reply shape plugs in there), cached by
  the version of what it was built from.
- REST and MCP over the service, as the per-team connection has. An admin component `PlatformAiCatalogueView`.

## Out of scope
- The team's choice as cards (#513), which this feeds.
