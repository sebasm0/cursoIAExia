using System.Text;
using RAG.Domain.Entities;
using RAG.Infrastructure.VectorStore;
using Xunit;

namespace RAG.Mvc.Tests.Integration;

/// <summary>
/// Gated integration test for the "view original uploaded PDF" byte round-trip.
///
/// The app persists raw file bytes as <c>bytea</c> in <c>documents.content</c>.
/// Every other test exercises this path through a Moq mock of <c>IVectorStore</c>;
/// this test alone proves the bytes survive byte-identically against a REAL
/// PostgreSQL store.
///
/// It is gated so the default <c>dotnet test</c> suite stays green in CI that has
/// no pgvector. It only runs (and reports as SKIPPED, never PASSED) when the
/// <c>RAG_TEST_PG_CONNECTION_STRING</c> env var points at a dedicated throwaway
/// test database.
/// </summary>
public class PgVectorStoreByteRoundTripTests
{
    public const string ConnectionStringEnvVar = "RAG_TEST_PG_CONNECTION_STRING";

    [SkippableFact(DisplayName = "Uploaded PDF bytes survive a real PostgreSQL bytea round-trip")]
    public async Task StoredPdfBytesRoundTripByteIdentically()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            $"{ConnectionStringEnvVar} is not set. Set it to a dedicated (throwaway) PostgreSQL "
            + "connection string pointing at a pgvector-enabled database to run this integration test.");

        // A distinctive payload: a realistic small PDF-ish header, followed by a
        // repeating 0x00..0xFF pattern. The repeat cycle would surface a substring
        // or truncation bug that a single opaque blob might hide.
        var pdfHeader = Encoding.ASCII.GetBytes("%PDF-1.7\n%RAG round-trip probe\n");
        var cycle = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var payload = pdfHeader
            .Concat(Enumerable.Range(0, 16).SelectMany(_ => cycle))
            .ToArray();

        var document = new Document
        {
            FileName = "sample.pdf",
            ContentType = "application/pdf",
            Size = payload.Length,
            Content = payload,
        };

        await using var store = new PgVectorStore(connectionString);
        try
        {
            // No chunks needed for the byte round-trip; the document row alone is inserted.
            await store.StoreChunksBatchAsync(document, []);

            var (roundTripped, content) = await store.GetDocumentWithContentAsync(document.Id);

            Assert.NotNull(roundTripped);
            Assert.Equal(document.FileName, roundTripped!.FileName);
            Assert.Equal(document.ContentType, roundTripped.ContentType);
            Assert.Equal(document.Size, roundTripped.Size);

            Assert.NotNull(content);
            Assert.Equal(payload.Length, content!.Length);
            Assert.Equal(payload, content);
        }
        finally
        {
            await store.DeleteDocumentAsync(document.Id);
        }
    }
}