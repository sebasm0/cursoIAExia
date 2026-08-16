# Delta for mvc-document-upload

## ADDED Requirements

### Requirement: UPLOAD-14 — Chat panel loads and renders history on open

The Documents chat panel MUST request `GET /Ask/History` when it opens and render the returned messages reusing the existing bubble builders (`createBubble` for user messages, `assistantRow` for assistant answers). An empty history MUST render the existing empty state. UPLOAD-1..UPLOAD-13 remain unchanged.

#### Scenario: History renders on open

- GIVEN an authenticated user with stored chat messages
- WHEN the Documents chat panel opens
- THEN the stored messages render as user/assistant bubbles via `createBubble`/`assistantRow`, in ascending order

#### Scenario: Empty history keeps the empty state

- GIVEN an authenticated user with no stored messages
- WHEN the chat panel opens
- THEN the panel renders the existing empty state and no bubbles

### Requirement: UPLOAD-15 — Panel saves each message on send and on done

The chat panel MUST persist the user message via `POST /Ask/History` at send time and the assistant message on the SSE `done` event. The assistant save MUST include the used assistant label as `modelId` and the done-event `sources` shape `[{fileName, snippet, page}]`. A truncated stream (no `done` event) MUST NOT persist an assistant message. Save failures MUST NOT break the visible conversation.

#### Scenario: User message saved on send

- GIVEN the user submits a question in the panel
- WHEN the message is sent to AskStream
- THEN a `POST /Ask/History` with `{role: "user", content}` is issued for that turn

#### Scenario: Assistant message saved on done

- GIVEN the SSE stream completes with `done`
- WHEN the answer is fully rendered
- THEN a `POST /Ask/History` with `{role: "assistant", content, modelId: <label>, sources}` is issued

#### Scenario: Truncated stream saves nothing for the assistant

- GIVEN the stream is truncated and `done` never fires
- WHEN the turn ends
- THEN only the user message is persisted and no assistant row is created

### Requirement: UPLOAD-16 — History re-render preserves credit and source chips

History re-rendered after reload MUST show the assistant credit ("Generado por {label}") from the stored `modelId` and source chips from the stored `sources`. Messages with null `modelId` or empty `sources` MUST render without credit or chips.

#### Scenario: Stored credit and chips render

- GIVEN a stored assistant message with `modelId` and `sources`
- WHEN the panel renders it from history
- THEN the credit shows the stored label and the source chips match the stored sources

#### Scenario: Missing credit and sources render cleanly

- GIVEN a stored message with null `modelId` and empty `sources`
- WHEN the panel renders it from history
- THEN no credit line and no chips are shown

### Requirement: UPLOAD-17 — Floating chat renders sanitized markdown

The floating chat MUST render assistant content as formatted markdown (headings, bold, lists, code blocks as `<pre><code>`, links) in live bubbles and in history re-render. Rendering MUST be client-side (marked + DOMPurify, decision D5): live bubbles keep plain-text accumulation during streaming and render once on the SSE `done` event; history bubbles render on open through the same path. Sanitization MUST run before every injection — raw markdown MUST NEVER be written via innerHTML; only `DOMPurify.sanitize(marked.parse(text))` output is injected. The site.js posture comment updates from "never innerHTML" to "innerHTML only with DOMPurify-sanitized output".

#### Scenario: Markdown answer formats on done

- GIVEN the SSE stream completes with `done` and the accumulated answer contains markdown
- WHEN the answer bubble re-renders
- THEN the bubble shows formatted markdown (code blocks as `<pre><code>`) with no raw `**`/`#` markers

#### Scenario: History bubbles render formatted

- GIVEN stored assistant messages with markdown content
- WHEN the panel opens and renders history
- THEN the stored content renders formatted through the same sanitized client path

#### Scenario: Malicious content neutralized

- GIVEN a streamed or stored assistant message containing `<script>` or `javascript:` links
- WHEN it renders
- THEN no executable script or dangerous href is present in the DOM

### Requirement: UPLOAD-18 — Chat bubbles show timestamps

Every message bubble in the floating chat MUST show a timestamp. Live user and assistant bubbles show the local render/save time; history bubbles MUST re-render the timestamp from the stored `createdAt` (CH-5) and MAY display relative "hace N min" formatting where the age allows. Timestamps MUST NOT appear in empty or error states.

#### Scenario: Live bubbles show local time

- GIVEN the user sends a message and the assistant answers
- WHEN the bubbles render
- THEN each bubble shows a local-time timestamp

#### Scenario: History timestamp matches createdAt

- GIVEN a stored message with `createdAt` from a previous session
- WHEN the panel re-renders it from history
- THEN the bubble timestamp derives from that `createdAt` (local-time or "hace N min" format)

## Assumptions

- UI copy stays Spanish (neutral/professional); code and comments stay English.
- AskStream and its SSE contract are untouched (see `chat-history` CH-8).
- `content` is stored raw (markdown as sent); formatting is presentation-layer per surface, never altering stored content.
