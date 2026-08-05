# Delta for mvc-document-upload

## MODIFIED Requirements

### Requirement: UPLOAD-1 — Upload form is reachable and re-renders on validation error

The upload form MUST be reachable via GET at `/Documents/Upload` and MUST render a file input with accepted-format hints (.cs, .md, .pdf). When a POST fails validation, the system MUST re-render the upload form with the validation errors in place. The Documents landing page MUST link to this reachable route.
(Previously: "Render a GET form with a file input and accepted format hints" — no GET action existed, so `/Documents/Upload` returned 404 and validation errors re-rendered the landing page instead of the form.)

#### Scenario: GET renders the upload form

- GIVEN an authenticated user with `documents.upload`
- WHEN they request `/Documents/Upload` via GET
- THEN the form renders with the file input and accepted-format hints (200)
- AND the landing page's Upload action targets this route without 404

#### Scenario: Happy path — supported file uploaded

- GIVEN the user selects a `.cs`, `.md`, or `.pdf` file under the configured size limit
- WHEN they submit the upload form
- THEN the file stream is passed to `IngestionService.IngestAsync`
- AND a success view renders with document name, file size, and ingestion timestamp

#### Scenario: Unsupported file type rejected

- GIVEN the user selects a `.exe` or `.zip` file
- WHEN they submit the upload form
- THEN the form re-renders with an error message listing supported types (.cs, .md, .pdf)
- AND no ingestion pipeline call is made

#### Scenario: Empty file rejected

- GIVEN the user selects a 0-byte file
- WHEN they submit the upload form
- THEN the form re-renders with a validation error indicating the file is empty
- AND no ingestion pipeline call is made

#### Scenario: File exceeds size limit

- GIVEN the user selects a 50 MB file
- WHEN they submit the upload form
- THEN the system rejects the upload with a validation error
- AND the error message includes the maximum allowed size

## ADDED Requirements

### Requirement: UPLOAD-10 — Upload screens follow the design system

The upload form, documents landing, success, and error views MUST follow the design system (UDS-1..UDS-4) and MUST preserve the data shown by UPLOAD-5/UPLOAD-6 (name, size, timestamp; supported types in errors).

#### Scenario: Upload form styled per design system

- GIVEN the light or dark theme
- WHEN an authorized user opens the upload form
- THEN the form renders with token-based styling and the shared layout

#### Scenario: Success view shows document details

- GIVEN a successful ingestion
- WHEN the result view renders
- THEN the document name, file size, and ingestion timestamp are displayed per the design system

#### Scenario: Error view lists supported types

- GIVEN an unsupported file upload fails
- WHEN the error view renders
- THEN a token-styled error message lists the supported types (.cs, .md, .pdf)
- AND no stack trace is shown

## Assumptions

- Requirement IDs UPLOAD-2..UPLOAD-9 are unchanged.
- The UPLOAD-1 fix is a render/route correction only; ingestion behavior is untouched.
