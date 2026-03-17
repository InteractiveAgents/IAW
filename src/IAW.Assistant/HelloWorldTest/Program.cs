using System.Text.Json;

Console.WriteLine("Hello World");

var result = new
{
    intent = "Hello World test",
    success = true,
    output = "Hello World"
};

var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText("result.json", json);
Console.WriteLine("result.json written.");
