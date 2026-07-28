-- ================================================================
-- RAG — Inicialización de base de datos PostgreSQL + pgvector
-- ================================================================
-- Ejecutar: psql -U postgres -d rag -f scripts/init-db.sql
-- ================================================================

-- 1. Crear base de datos (ejecutar una sola vez como superusuario)
-- CREATE DATABASE rag;

-- 2. Habilitar pgvector
CREATE EXTENSION IF NOT EXISTS vector;

-- 3. Tabla de documentos
CREATE TABLE IF NOT EXISTS documents (
    id UUID PRIMARY KEY,
    file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    size BIGINT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- 4. Tabla de chunks con embedding vectorial
CREATE TABLE IF NOT EXISTS chunks (
    id UUID PRIMARY KEY,
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    content TEXT NOT NULL,
    chunk_index INT NOT NULL,
    metadata JSONB DEFAULT '{}'::jsonb,
    embedding vector(768)   -- nomic-embed-text: 768 dimensiones
);

-- 5. Índices
CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON chunks(document_id);

-- Índice IVF-Flat para búsqueda por similitud coseno
-- NOTA: requiere al menos ~1000 filas para ser efectivo.
-- Para datasets pequeños, considerar HNSW o no crear índice.
CREATE INDEX IF NOT EXISTS idx_chunks_embedding
    ON chunks USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);

-- Índice GIN para full-text search
CREATE INDEX IF NOT EXISTS idx_chunks_content_fts
    ON chunks USING GIN (to_tsvector('english', content));
