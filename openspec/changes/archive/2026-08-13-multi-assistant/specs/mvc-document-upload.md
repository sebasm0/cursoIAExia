# Delta for mvc-document-upload

## ADDED Requirements

### Requirement: UPLOAD-13 — Floating chat renders the same assistant selector

The floating chat composer on the Documents landing page MUST render the same assistant selector as the Ask composer. Submissions MUST post `Query` + `SelectedModelId` through the same `AskController.Ask` flow; ASK-14 selection semantics apply unchanged.

#### Scenario: Selector in floating chat

- GIVEN an authorized user opens the Documents landing page
- THEN the floating chat composer shows the assistant selector with catalog options

#### Scenario: Floating chat submission routed

- GIVEN the user selects an assistant and submits a question from the floating chat
- WHEN the POST reaches `AskController.Ask`
- THEN the selected assistant generates the answer (default fallback per ASEL-2)

## Assumptions

- Requirement IDs UPLOAD-1..UPLOAD-12 are unchanged; this delta adds UPLOAD-13 only.
- The Documents floating chat already requires `documents.upload` plus the Ask flow's `rag.ask` gate (existing multi-permission posture, unchanged).
- Selector markup is shared conceptually with the Ask composer (same catalog, same `SelectedModelId` binding) but each view renders its own form.