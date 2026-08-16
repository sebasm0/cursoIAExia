# chat-history Specification

## Purpose

Persistent per-user chat history for the Documents chat. Messages survive page reload with faithful assistant credit and source re-render. Persistence is client-triggered (save on send and on SSE `done`); the AskStream pipeline stays store-free. No FK to `identity."AspNetUsers"`; `user_id` is always claim-derived.

## Requirements

| ID | Requirement | Strength |
|----|-------------|----------|
| CH-1 | `public.chat_messages` table with idempotent bootstrap | MUST |
| CH-2 | `IChatHistoryStore` abstraction (Domain) | MUST |
| CH-3 | `ChatHistoryService` validation and mapping (Application) | MUST |
| CH-4 | `PgChatHistoryStore` (Infrastructure/Chat) | MUST |
| CH-5 | `GET /Ask/History` returns own last 50 ascending | MUST |
| CH-6 | `POST /Ask/History` validates and persists | MUST |
| CH-7 | Per-user isolation — never another user's messages | MUST |
| CH-8 | AskStream wire contract and RAG surfaces unchanged | MUST |

### Requirement: CH-1 — `public.chat_messages` table with idempotent bootstrap

The system MUST create `public.chat_messages` at startup using the PgVectorStore bootstrap pattern (`CREATE TABLE IF NOT EXISTS`), with no EF migration. Columns MUST be `id UUID PK`, `user_id UUID NOT NULL`, `role TEXT NOT NULL` restricted to `user`/`assistant`, `content TEXT NOT NULL`, `model_id TEXT NULL`, `sources JSONB NULL`, `created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()`. An index on `(user_id, created_at)` MUST exist. There MUST NOT be a FK to `identity."AspNetUsers"`.

#### Scenario: Bootstrap creates the table

- GIVEN the database has no `public.chat_messages`
- WHEN the app starts and the store bootstrap runs
- THEN the table is created with the exact columns and the `(user_id, created_at)` index

#### Scenario: Bootstrap is idempotent

- GIVEN `public.chat_messages` already exists
- WHEN the app starts again
- THEN bootstrap succeeds without error and does not alter the existing table

### Requirement: CH-2 — `IChatHistoryStore` abstraction (Domain)

The system MUST expose a domain abstraction `IChatHistoryStore` with `AddAsync(message, ct)` and `GetRecentAsync(userId, limit, ct)`. It MUST be persistence-agnostic (no Npgsql/Dapper types) and MUST be mockable for unit tests. `GetRecentAsync` MUST return messages ordered ascending by `created_at`.

#### Scenario: Contract used by unit tests

- GIVEN a mocked `IChatHistoryStore`
- WHEN the service calls `AddAsync` or `GetRecentAsync`
- THEN the mock records the call with the exact message/userId/limit arguments

### Requirement: CH-3 — `ChatHistoryService` validation and mapping (Application)

The service MUST accept a `role` only of `user` or `assistant`; any other value MUST be rejected. `content` MUST be non-empty after trimming and MUST be bounded to a maximum length. `sources` MUST be normalized so null/empty becomes an empty list. The `user_id` MUST always come from the caller's principal — a user id supplied in the request body MUST be ignored. `model_id` MUST be stored as the assistant label as sent (credit snapshot).

#### Scenario: Valid user message accepted

- GIVEN a principal and a message with role `user` and non-empty content
- WHEN the service persists it
- THEN the stored message carries the principal's user id and the trimmed content

#### Scenario: Invalid role rejected

- GIVEN a message with role `system` or empty/whitespace content
- WHEN the service validates it
- THEN an error is returned and nothing is persisted

### Requirement: CH-4 — `PgChatHistoryStore` (Infrastructure/Chat)

The system MUST provide `PgChatHistoryStore` implementing `IChatHistoryStore` following the PgVectorStore pattern (`NpgsqlDataSource` + `EnsureSchemaAsync`), using Dapper for queries. `sources` MUST be persisted as JSONB with camelCase property names (`fileName`, `snippet`, `page`). No new packages.

#### Scenario: Sources round-trip as camelCase JSONB

- GIVEN a message with sources `[{fileName, snippet, page}]`
- WHEN it is stored and read back
- THEN the returned sources match the original values with camelCase keys

### Requirement: CH-5 — `GET /Ask/History` returns own last 50 ascending

The system MUST expose `GET /Ask/History` returning HTTP 200 with a JSON array of the caller's 50 most recent messages ascending by `created_at`, each shaped `{id, role, content, createdAt, modelId, sources}`. `sources` MUST serialize as `[]` when null/empty; `modelId` MUST be null when absent. Only messages of the authenticated principal's `user_id` MUST be returned.

#### Scenario: Empty history

- GIVEN an authenticated user with no stored messages
- WHEN they request `GET /Ask/History`
- THEN the response is 200 with an empty array

#### Scenario: History with stored messages

- GIVEN a user with more than 50 stored messages
- WHEN they request `GET /Ask/History`
- THEN the response contains exactly the 50 latest, ordered ascending by `created_at`

#### Scenario: Null sources serialize as empty array

- GIVEN a stored message with no `sources` value
- WHEN `GET /Ask/History` returns it
- THEN `sources` is serialized as `[]` and `modelId` as `null`

### Requirement: CH-6 — `POST /Ask/History` validates and persists

The system MUST expose `POST /Ask/History` accepting `{role, content, modelId?, sources?}` and MUST return HTTP 201 with `{id, createdAt}` on success. The action MUST require a valid antiforgery token (per-action posture); a missing token MUST yield HTTP 400 before any validation or persistence. An invalid `role` or empty/whitespace `content` MUST yield HTTP 400 and persist nothing.

#### Scenario: Valid POST persists and returns 201

- GIVEN an authenticated user with a valid antiforgery token
- WHEN they POST `{role: "user", content: "Hola"}`
- THEN the response is 201 with the new `id` and `createdAt`

#### Scenario: Invalid role or empty content rejected

- GIVEN a POST with role `system` or empty content
- WHEN the action validates it
- THEN the response is 400 and no row is persisted

#### Scenario: POST without antiforgery token rejected

- GIVEN an authenticated user POSTing without a `__RequestVerificationToken`
- WHEN the request reaches the action
- THEN the response is 400 and no row is persisted

### Requirement: CH-7 — Per-user isolation

The system MUST NOT ever return, list, or expose one user's messages to another user. Isolation MUST be verified with two distinct authenticated users.

#### Scenario: Two-user isolation

- GIVEN Users A and B each with stored messages
- WHEN each requests `GET /Ask/History`
- THEN User A receives only A's messages and User B only B's messages

### Requirement: CH-8 — AskStream wire contract and RAG surfaces unchanged

The AskStream SSE wire contract MUST remain byte-for-byte unchanged, and AskStream MUST NOT write to the chat store. `RAG.Api`, the RAG pipeline, AI providers, and the `identity` schema MUST NOT be modified. Multi-turn context remains out of scope.

#### Scenario: Streamed answer unchanged

- GIVEN an AskStream request
- WHEN the answer streams
- THEN the SSE events are identical to pre-change behavior and no history write occurs

#### Scenario: Truncated stream persists no assistant answer

- GIVEN the stream is truncated before completion
- WHEN the client-side `done` event never fires
- THEN no assistant message is saved for that turn (the already-rendered error bubble stands)

## Assumptions

- `createdAt` serializes as the stored `created_at` timestamp; no timezone conversion beyond the driver default.
- Content length bound is a configurable application constant; the exact default is decided in design.
- `content` is stored raw (markdown as sent by the client); formatting is a presentation-layer concern per surface (server-side Markdig for Ask views, client-side marked+DOMPurify for the floating chat), never altering stored content.
