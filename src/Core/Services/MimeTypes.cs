namespace Core.Services;

public static class MimeTypes
{
    static readonly Dictionary<string, string> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".cs"] = "text/x-csharp",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
        [".ts"] = "text/typescript",
        [".md"] = "text/markdown",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".webp"] = "image/webp",
        [".csv"] = "text/csv",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".sln"] = "text/plain",
        [".slnx"] = "application/xml",
        [".csproj"] = "application/xml",
        [".yaml"] = "text/yaml",
        [".yml"] = "text/yaml",
        [".log"] = "text/plain",
        [".sql"] = "text/x-sql",
        [".sh"] = "text/x-shellscript",
        [".ps1"] = "text/x-powershell",
        [".py"] = "text/x-python",
        [".rs"] = "text/x-rust",
        [".go"] = "text/x-go",
        [".java"] = "text/x-java",
    };

    public static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ext is not null && ExtensionMap.TryGetValue(ext, out var mime)
            ? mime
            : "application/octet-stream";
    }
}
