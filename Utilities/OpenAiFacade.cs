using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenAI.Chat;
using OpenAI.Responses;

namespace Utilities;

/// <summary>
/// Facade for the OpenAI API. Simplifies sending prompts and receiving text responses.
/// </summary>
#pragma warning disable OPENAI001 // Experimental API
public sealed class OpenAiFacade
{
    private const string ResponsesEndpoint = "https://api.openai.com/v1/responses";

    private readonly ResponsesClient _responsesClient;
    private readonly ChatClient _chatClient;
    private readonly HttpClient _httpClient;
    private readonly string _model;

    /// <summary>
    /// Creates a new facade with the given API key.
    /// </summary>
    /// <param name="apiKey">OpenAI API key for authentication.</param>
    /// <param name="model">Optional model name. Defaults to "gpt-5.2".</param>
    public OpenAiFacade(string apiKey, string model = "gpt-5.2")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _model = model;
        _responsesClient = new ResponsesClient(model: model, apiKey: apiKey);
        _chatClient = new ChatClient(model: model, apiKey: apiKey);
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Sends a text prompt to the model and returns the text response.
    /// </summary>
    /// <param name="textPrompt">The prompt text to send.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The model's text response.</returns>
    public async Task<string> Prompt(string textPrompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textPrompt);

        ResponseResult response = await _responsesClient.CreateResponseAsync(textPrompt, cancellationToken: cancellationToken);
        return response.GetOutputText() ?? string.Empty;
    }

    /// <summary>
    /// Sends a text prompt along with an image to the model and returns the text response.
    /// Uses the Chat API with vision support.
    /// </summary>
    /// <param name="text">The prompt text to send.</param>
    /// <param name="imageDataUrl">The image as a data URL (e.g. data:image/png;base64,...).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The model's text response.</returns>
    public async Task<string> Prompt(string text, string imageDataUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageDataUrl);

        if (!imageDataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Image must be a data URL (data:image/...;base64,...).", nameof(imageDataUrl));

        var textPart = ChatMessageContentPart.CreateTextPart(text);
        var imagePart = ChatMessageContentPart.CreateImagePart(new Uri(imageDataUrl));
        var message = ChatMessage.CreateUserMessage(textPart, imagePart);

        ChatCompletion completion = await _chatClient.CompleteChatAsync([message], cancellationToken: cancellationToken);
        return completion.Content[0].Text ?? string.Empty;
    }

    /// <summary>
    /// Sends a text prompt with a file attachment to the model and returns the text response.
    /// Uses the Responses API with file input support. For PDF extraction, use a vision-capable model (e.g. gpt-4o).
    /// </summary>
    /// <param name="text">The prompt text (e.g. instructions to extract information from the file).</param>
    /// <param name="fileBytes">The file content as raw bytes.</param>
    /// <param name="filename">The filename with extension (e.g. "invoice.pdf"). Used for format detection.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The model's text response.</returns>
    public async Task<string> PromptAndFile(string text, byte[] fileBytes, string filename, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(fileBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        string fileDataBase64 = Convert.ToBase64String(fileBytes);
        string mediaType = GetMediaTypeFromFilename(filename);
        string fileData = $"data:{mediaType};base64,{fileDataBase64}";

        var requestBody = new
        {
            model = _model,
            input = new object[]
            {
                new
                {
                    type = "message",
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_file", file_data = fileData, filename },
                        new { type = "input_text", text }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint);
        request.Headers.TryAddWithoutValidation("OpenAI-Beta", "responses=v1");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI API error ({(int)response.StatusCode}): {responseJson}");

        using var doc = JsonDocument.Parse(responseJson);
        var outputItems = doc.RootElement.GetProperty("output");
        foreach (var item in outputItems.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "message"
                && item.TryGetProperty("content", out var contentProp))
            {
                foreach (var part in contentProp.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var partType) && partType.GetString() == "output_text"
                        && part.TryGetProperty("text", out var textProp))
                    {
                        return textProp.GetString() ?? string.Empty;
                    }
                }
            }
        }

        return string.Empty;
    }

    static string GetMediaTypeFromFilename(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".json" => "application/json",
            ".html" or ".htm" => "text/html",
            ".xml" => "text/xml",
            ".csv" => "text/csv",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream"
        };
    }
}
#pragma warning restore OPENAI001
