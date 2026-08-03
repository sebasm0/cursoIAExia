# Proposal: MVC RAG Integration

## Intent

Bring existing RAG ingestion and Q&A into the legacy MVC app (rag/) so users can upload documents and search through a web UI — currently only available via Minimal API endpoints. Target documents: code files, Markdown, technical specs, PDFs (all already supported by existing parsers).

## Scope

### In Scope
- Upgrade rag/ from net9.0 to net10.0 for project reference compatibility
- Add rag/ to RAG.slnx; reference RAG.Application + RAG.Domain
- AskController + Ask view — submit questions, get RAG answer with citations
- DocumentsController + Upload view — upload documents via existing ingest pipeline
- Register RAG services in rag/ DI (AddApplication, AddRagInfrastructure, AI clients)

### Out of Scope
- Migrating MVC app to Clean Architecture
- Auth/authorization beyond defaults
- Modifying existing pipeline (IngestionService, RagService, parsers, chunker, vector store)
- Batch ingestion or document listing

## Capabilities

### New Capabilities
- `mvc-rag-ask`: Web form to query the RAG system — user types a question, sees generated answer with context citations
- `mvc-document-upload`: Web form to upload supported documents (.cs, .md, .pdf, etc.) for vector store ingestion

### Modified Capabilities
- None — existing RAG pipeline unchanged

## Approach

1. Update rag.csproj: target net10.0, add project refs to RAG.Application + RAG.Domain
2. Add rag/ to RAG.slnx
3. Register RAG services in rag/Program.cs: AddApplication, AddRagInfrastructure, OllamaChatClient, OllamaEmbeddingGenerator
4. Create AskController (GET form + POST submit via RagService.AskAsync)
5. Create DocumentsController (GET form + POST upload via IngestionService.IngestAsync)
6. Add Razor views: Ask/Index.cshtml, Documents/Upload.cshtml, result partials

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `rag/rag.csproj` | Modified | target net10.0, add project refs |
| `RAG.slnx` | Modified | add rag/ project entry |
| `rag/Program.cs` | Modified | register RAG + AI services |
| `rag/Controllers/AskController.cs` | New | RAG Q&A controller |
| `rag/Controllers/DocumentsController.cs` | New | document upload controller |
| `rag/Views/Ask/` | New | ask form + answer display views |
| `rag/Views/Documents/` | New | upload form + result views |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| net9.0→net10.0 breaks existing routes | Low | Smoke-test after upgrade |
| pgvector/Ollama unavailable in dev | Medium | Health check + graceful error view |
| Double service registration across apps | Low | Each app has isolated DI container |

## Rollback Plan

Revert rag.csproj to net9.0, remove project refs, revert Program.cs and RAG.slnx. New controllers/views remain unused but harmless.

## Dependencies

- Ollama running locally (IChatClient, IEmbeddingGenerator)
- PostgreSQL + pgvector with connection string in rag/appsettings.json

## Success Criteria

- [ ] Upload .cs, .md, or .pdf through MVC UI → success confirmation
- [ ] Ask question through MVC UI → answer with context citations
- [ ] Both work without modifying any file in src/ (Application, Domain, Infrastructure)
