namespace SWEN3.Sprint5.GenAI;

/// <summary>
///     Provides AI-powered text summarization capabilities for document content.
///     <para>Implementations should handle API failures gracefully and return <c>null</c> when summarization fails.</para>
/// </summary>
public interface ITextSummarizer
{
    /// <summary>
    ///     Generates a structured summary for the supplied OCR-extracted text.
    ///     The summary includes key points, document type identification, and important entities.
    /// </summary>
    /// <param name="text">The text content to summarize. Should not be null or empty.</param>
    /// <param name="cancellationToken">Token to cancel the summarization operation.</param>
    /// <returns>
    ///     A structured summary of the text, or <c>null</c> if summarization failed due to API errors,
    ///     invalid input, or service unavailability.
    /// </returns>
    /// <example>
    ///     <code>
    /// var text = "This is a financial report with quarterly earnings...";
    /// var summary = await summarizer.SummarizeAsync(text, cancellationToken);
    /// if (summary != null)
    /// {
    ///     // Process the summary
    /// }
    ///     </code>
    /// </example>
    Task<string?> SummarizeAsync(string text, CancellationToken cancellationToken = default);
}