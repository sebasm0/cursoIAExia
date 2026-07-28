# Manual Test: Graceful Error When Ollama Unavailable

## Test ID
5.6

## Prerequisites
- Ollama is NOT running (or running on a different port)
- PostgreSQL is running
- The `rag/` project is built and ready

## Steps

1. Start the application:
   ```
   dotnet run --project rag/
   ```

2. Open a browser at `http://localhost:5000/Ask` (or the URL shown in the console output)

3. Type a question (e.g., "What is the capital of France?") and submit

4. Verify that:
   - The page shows a user-friendly error message indicating the service is temporarily unavailable
   - The error includes a suggestion to try again later
   - No stack trace or technical details are exposed to the user
   - The application does not crash or return a 500 error

5. Repeat the test for the Upload page:
   - Navigate to `http://localhost:5000/Documents/Upload`
   - Upload a small `.md` file
   - Verify the same graceful error behavior

## Expected Result

The application displays a styled error view (not a raw exception page) explaining that the RAG service is unavailable and suggesting the user retry later.
