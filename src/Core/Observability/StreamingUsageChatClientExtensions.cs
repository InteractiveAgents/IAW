using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Core.Observability;

public static class StreamingUsageChatClientExtensions
{
    private const string StreamOptionsKey = "stream_options";

    private static readonly JsonDocument StreamOptionsDoc = JsonDocument.Parse("""{"include_usage": true}""");

    extension(ChatClientBuilder builder)
    {
        public ChatClientBuilder UseStreamingUsage()
        {
            return builder.Use(async (messages, options, next, cancellationToken) =>
            {
                options = EnsureStreamOptions(options);
                await next(messages, options, cancellationToken);
            });
        }
    }

    private static ChatOptions EnsureStreamOptions(ChatOptions? options)
    {
        if (options is null)
        {
            return new ChatOptions
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    [StreamOptionsKey] = StreamOptionsDoc.RootElement.Clone()
                }
            };
        }

        var clonedOptions = options.Clone();
        clonedOptions.AdditionalProperties ??= [];

        if (!clonedOptions.AdditionalProperties.ContainsKey(StreamOptionsKey))
        {
            clonedOptions.AdditionalProperties[StreamOptionsKey] = StreamOptionsDoc.RootElement.Clone();
        }

        return clonedOptions;
    }
}
