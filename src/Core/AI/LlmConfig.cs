namespace Core.AI;

public static class LlmConfig
{
    public const string AnthropicApiKey = "AI:LLM:AnthropicApiKey";
    public const string OpenAiApiKey = "AI:LLM:OpenAiApiKey";
    public const string OllamaEndpoint = "AI:LLM:OllamaEndpoint";
    public const string GitHubModelsApiKey = "AI:LLM:GitHubToken";
    public const string GitHubModelsEndpoint = "https://models.inference.ai.azure.com";
    public const string GitHubToken = "GitHub:Token";
    public const string WhisperEndpoint = "AI:Whisper:Endpoint";
    public const string WhisperModelId = "AI:Whisper:ModelId";

    public const string EmbeddingModelId = "AI:Embedding:ModelId";
    public const string EmbeddingProvider = "AI:Embedding:Provider";
    public const string EmbeddingServiceKey = "AI:Embedding:ServiceKey";
    public const string EmbeddingDimensions = "AI:Embedding:Dimensions";
}