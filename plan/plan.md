# Plan: platform AI catalogue (#512)

Branch: feature/platform-ai-catalogue

- [x] 1. Contract: catalogue records, `IPlatformAiCatalogueService`, `IPlatformAiCatalogueStore`, system scope, `PlatformModelId` and choices, `UsePlatformModelAsync(teamKey, modelId)`
- [ ] 2. MongoDB: entity, collection, store, option name, registration
- [ ] 3. Service: catalogue service and decorators, registration and scope, platform chat client, resolver and connection service
- [ ] 4. REST and MCP
- [ ] 5. `PlatformAiCatalogueView` and texts, inside a tabbed `SupportAssistantAdminView` with the token usage per team beside it (user's request mid-session; modelled on FortDocs' AssistantUsage page: Usage tab with a period, a totals line and a grid of answers and tokens per team, provider and model, and a Configuration tab shown only with the catalogue scope; `?tab=` selects the tab)
- [ ] 6. Tests, docs, full suites; push for testing; on the user's word close-out, PR, close #512
