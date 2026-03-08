using System.ComponentModel;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Samples.Agents;

public interface IWeatherAgent : IAgent;

public class WeatherAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IWeatherAgent
{
    protected override string Instructions =>
        "You're a weather assistant. Use the available tools to answer questions about weather conditions, forecasts, and alerts.";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(GetCurrentWeather),
        AIFunctionFactory.Create(GetForecast),
        AIFunctionFactory.Create(GetWeatherAlerts)
    ];

    [Description("Gets the current weather for a given city")]
    static WeatherInfo GetCurrentWeather(string city) => new(
        City: city,
        TemperatureCelsius: Random.Shared.Next(-10, 40),
        Condition: PickRandom("Sunny", "Cloudy", "Rainy", "Snowy", "Windy"),
        Humidity: Random.Shared.Next(20, 100));

    [Description("Gets a 3-day weather forecast for a given city")]
    static List<ForecastDay> GetForecast(string city) =>
    [.. Enumerable.Range(1, 3)
        .Select(i => new ForecastDay(
            Date: DateOnly.FromDateTime(DateTime.Now.AddDays(i)),
            HighCelsius: Random.Shared.Next(15, 40),
            LowCelsius: Random.Shared.Next(-5, 15),
            Condition: PickRandom("Sunny", "Cloudy", "Rainy", "Snowy")))];

    [Description("Gets active weather alerts for a given city")]
    static List<string> GetWeatherAlerts(string city) => Random.Shared.Next(3) switch
    {
        0 => [],
        1 => [$"Heat advisory for {city} until 8 PM"],
        _ => [$"Thunderstorm warning for {city}", $"Flood watch in {city} area"]
    };

    static string PickRandom(params string[] options) => options[Random.Shared.Next(options.Length)];
}

public record WeatherInfo(string City, int TemperatureCelsius, string Condition, int Humidity);
public record ForecastDay(DateOnly Date, int HighCelsius, int LowCelsius, string Condition);
