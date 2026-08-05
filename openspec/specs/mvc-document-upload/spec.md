# mvc-document-upload Specification

## Purpose

Web form for uploading supported documents (.cs, .md, .pdf) into the vector store via the existing ingestion pipeline. Supports manual upload through the web UI and automatic directory scanning from the server. Multi-tenant: each document is associated with the uploading user's identity.

## Requirements

| ID | Requirement | Strength |
|----|-------------|----------|
| UPLOAD-1 | Upload form reachable via GET at `/Documents/Upload` with a file input and accepted-format hints (.cs, .md, .pdf); POST validation failures re-render the form with errors in place; Documents landing page links to this route | MUST |
| UPLOAD-2 | POST handler calls `IngestionService.IngestAsync(fileName, contentType, stream)` | MUST |
| UPLOAD-3 | Reject unsupported content types with a clear error listing supported types (.cs, .md, .pdf) | MUST |
| UPLOAD-4 | Enforce a maximum file upload size (default 10 MB, configurable via `appsettings.json`) | SHOULD |
| UPLOAD-5 | Show success confirmation with document name, size, and ingestion timestamp | MUST |
| UPLOAD-6 | Handle parser exceptions and storage failures with a user-friendly error message | MUST |
| UPLOAD-7 | Associate each ingested document with the current user's identity for multi-tenant isolation | SHOULD |
| UPLOAD-8 | Support a background directory scanner that ingests new files from a configured watch path | SHOULD |
| UPLOAD-9 | Upload flow requires authentication and `documents.upload` permission; anonymous → login redirect, missing permission → access-denied, gate before ingestion | MUST |
| UPLOAD-10 | Upload form, documents landing, success, and error views follow the design system (UDS-1..UDS-4); UPLOAD-5/UPLOAD-6 data (name, size, timestamp; supported types in errors) preserved | MUST |

### Scenario: GET renders the upload form

- GIVEN an authenticated user with `documents.upload`
- WHEN they request `/Documents/Upload` via GET
- THEN the form renders with the file input and accepted-format hints (200)
- AND the landing page's Upload action targets this route without 404

### Scenario: Happy path — supported file uploaded

- GIVEN the user selects a `.cs`, `.md`, or `.pdf` file under the configured size limit
- WHEN they submit the upload form
- THEN the file stream is passed to `IngestionService.IngestAsync`
- AND a success view is rendered showing the document name, file size, and ingestion timestamp

### Scenario: Unsupported file type rejected

- GIVEN the user selects a `.exe` or `.zip` file
- WHEN they submit the upload form
- THEN the form re-renders with an error message listing supported types (.cs, .md, .pdf)
- AND no ingestion pipeline call is made

### Scenario: Empty file rejected

- GIVEN the user selects a 0-byte file
- WHEN they submit the upload form
- THEN the form re-renders with a validation error indicating the file is empty
- AND no ingestion pipeline call is made

### Scenario: File exceeds size limit

- GIVEN the user selects a 50 MB file
- WHEN they submit the upload form
- THEN the system rejects the upload with a validation error
- AND the error message includes the maximum allowed size

### Scenario: Parser failure on corrupt file

- GIVEN the user uploads a corrupted PDF
- WHEN `IngestionService.IngestAsync` throws a `NotSupportedException` or parse error
- THEN the system shows an error message indicating the file could not be parsed
- AND no partial chunks are persisted in the vector store

### Scenario: Background directory scanner ingests files

- GIVEN a configured watch directory path in `appsettings.json`
- WHEN a supported file is added to that directory
- THEN the scanner automatically calls `IngestionService.IngestAsync` for that file
- AND the document is associated with a system-level user identifier

### Scenario: Multi-tenant document isolation

- GIVEN User A and User B both upload files with the same name `readme.md`
- WHEN both files are ingested successfully
- THEN each document is stored under its respective user's document space
- AND User A's queries only see User A's `readme.md` content

### Scenario: Anonymous user redirected to login

- GIVEN no authentication cookie
- WHEN the user requests the upload page or submits a file
- THEN the response redirects to `/Account/Login?returnUrl=...`
- AND no ingestion pipeline call is made

### Scenario: User with documents.upload can upload

- GIVEN an authenticated principal with `permission: documents.upload`
- WHEN the user uploads a supported file
- THEN the existing UPLOAD-1..UPLOAD-8 behavior applies (ingest + success view)

### Scenario: User without documents.upload sees access denied

- GIVEN an authenticated principal without `permission: documents.upload`
- WHEN the user submits the upload form
- THEN the access-denied page is shown (403)
- AND no file is persisted

### Scenario: Upload form styled per design system

- GIVEN the light or dark theme
- WHEN an authorized user opens the upload form
- THEN the form renders with token-based styling and the shared layout

### Scenario: Success view shows document details

- GIVEN a successful ingestion
- WHEN the result view renders
- THEN the document name, file size, and ingestion timestamp are displayed per the design system

### Scenario: Error view lists supported types

- GIVEN an unsupported file upload fails
- WHEN the error view renders
- THEN a token-styled error message lists the supported types (.cs, .md, .pdf)
- AND no stack trace is shown
