namespace Core.UI;

[GenerateSerializer]
public abstract record UIPart;

[GenerateSerializer]
public record TextPart(
    [property: Id(0)] string Content,
    [property: Id(1)] TextStyle Style = TextStyle.Normal) : UIPart;

[GenerateSerializer]
public record OptionsPart(
    [property: Id(0)] string Prompt,
    [property: Id(1)] List<Option> Options,
    [property: Id(2)] string CallbackId,
    [property: Id(3)] bool AllowMultiple = false) : UIPart;

[GenerateSerializer]
public record Option(
    [property: Id(0)] string Label,
    [property: Id(1)] string Value,
    [property: Id(2)] string? Description = null);

[GenerateSerializer]
public record CardPart(
    [property: Id(0)] string? Title,
    [property: Id(1)] List<CardField> Fields,
    [property: Id(2)] string? ImageUrl = null) : UIPart;

[GenerateSerializer]
public record CardField(
    [property: Id(0)] string Label,
    [property: Id(1)] string Value);

[GenerateSerializer]
public record MediaPart(
    [property: Id(0)] string Url,
    [property: Id(1)] string FileName,
    [property: Id(2)] string MimeType,
    [property: Id(3)] string? Caption = null) : UIPart;

[GenerateSerializer]
public record ProgressPart(
    [property: Id(0)] string Message,
    [property: Id(1)] double? Percent = null) : UIPart;

[GenerateSerializer]
public record FormPart(
    [property: Id(0)] string CallbackId,
    [property: Id(1)] string Prompt,
    [property: Id(2)] List<FormField> Fields) : UIPart;

[GenerateSerializer]
public record FormField(
    [property: Id(0)] string Id,
    [property: Id(1)] string Label,
    [property: Id(2)] FormFieldType Type,
    [property: Id(3)] List<Option>? Options = null);

[GenerateSerializer]
public enum TextStyle { Normal, Success, Warning, Error, Muted }

[GenerateSerializer]
public enum FormFieldType { Text, SingleChoice, MultiChoice, Date, Number }
