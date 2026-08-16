# Delta for mvc-rag-ask

> Scope expansion (approved 2026-08-16): the proposal declared `mvc-rag-ask: None`; the approved visual scope now modifies this capability. ASK-1..ASK-15 remain unchanged; the additions are purely presentational. The AskStream wire contract stays untouched (CH-8).

## ADDED Requirements

### Requirement: ASK-16 — Ask answer surfaces render sanitized markdown

The Ask answer surfaces (Ask/Index and Ask/Result) MUST render assistant `Answer` content as formatted markdown — headings, bold, lists, inline code, fenced code blocks as `<pre><code>`, and links. Rendering MUST be server-side (Markdig + Ganss.Xss HtmlSanitizer pipeline, decision D5) before the view outputs it; the sanitized HTML is the ONLY content injected into the answer bubble. Raw markdown MUST NEVER be injected unsanitized. Malicious HTML (`<script>`, event handlers, `javascript:` hrefs) MUST be neutralized before rendering. The user query echo, validation, and error messages MUST remain plain text (Razor-encoded). Unit tests MUST cover the sanitizer pipeline (XSS cases included).

#### Scenario: Markdown answer renders formatted

- GIVEN an assistant answer containing `# Title`, `**bold**`, a list, and a fenced code block
- WHEN the result view renders the answer
- THEN the answer shows heading, bold, list, and code-block formatting with code inside `<pre><code>`

#### Scenario: Malicious HTML is neutralized

- GIVEN an assistant answer containing `<script>alert(1)</script>` and an `<a href="javascript:...">`
- WHEN the answer renders
- THEN the output contains no executable script and no `javascript:` href (verified by sanitizer unit tests)

#### Scenario: Plain-text surfaces stay plain

- GIVEN a rendered answer with its query echo, or an error state
- WHEN the views render
- THEN the query echo and error text render plain (Razor-encoded), never as markdown

### Requirement: ASK-17 — Ask answer surfaces show local-time timestamps

The Ask answer surfaces (Ask/Index and Ask/Result) MUST display a local-time timestamp on each rendered message (user query and assistant answer). The timestamp MUST derive from the render-time clock (the Ask flow is stateless; nothing is persisted) and format as local time per the site locale.

#### Scenario: User and assistant messages show time

- GIVEN the result view renders a question and its answer
- WHEN the view renders
- THEN both bubbles display a local-time timestamp (e.g. "14:32")

#### Scenario: No messages, no timestamps

- GIVEN the Ask form with no answer yet, or the error view
- WHEN the view renders
- THEN no message timestamps appear