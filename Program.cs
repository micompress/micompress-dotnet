using System.Net.Http.Headers;
using System.Text.Json;

const string BaseUrl = "https://micompress.com";
const string Version = "0.1.1";
const string UserAgent = "micompress-dotnet/" + Version + " (+https://micompress.com)";
const string AccountUrl = "https://micompress.com/account";
const string PlansUrl = "https://micompress.com/plans";

var extCategory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["jpg"] = "image", ["jpeg"] = "image", ["png"] = "image", ["webp"] = "image", ["gif"] = "image",
    ["bmp"] = "image", ["tiff"] = "image", ["tif"] = "image", ["avif"] = "image", ["svg"] = "image",
    ["heic"] = "image", ["heif"] = "image", ["ico"] = "image", ["psd"] = "image", ["dng"] = "image",
    ["raw"] = "image", ["cr2"] = "image", ["arw"] = "image", ["nef"] = "image",
    ["mp4"] = "video", ["mov"] = "video", ["avi"] = "video", ["mkv"] = "video", ["webm"] = "video",
    ["mpeg"] = "video", ["mpg"] = "video", ["m4v"] = "video", ["wmv"] = "video", ["flv"] = "video", ["3gp"] = "video",
    ["mp3"] = "audio", ["wav"] = "audio", ["flac"] = "audio", ["ogg"] = "audio", ["m4a"] = "audio",
    ["aac"] = "audio", ["wma"] = "audio", ["opus"] = "audio",
    ["pdf"] = "pdf",
    ["csv"] = "document", ["json"] = "document", ["xml"] = "document", ["md"] = "document",
    ["odt"] = "document", ["ods"] = "document", ["docx"] = "document", ["doc"] = "document",
    ["ppt"] = "document", ["pptx"] = "document", ["xls"] = "document", ["xlsx"] = "document",
    ["epub"] = "document", ["html"] = "document", ["htm"] = "document", ["hwp"] = "document",
    ["txt"] = "document", ["rtf"] = "document",
    ["zip"] = "archive", ["7z"] = "archive", ["tar"] = "archive", ["gz"] = "archive", ["tgz"] = "archive", ["rar"] = "archive",
};
var categoryPath = new Dictionary<string, string>
{
    ["image"] = "image-compressors", ["video"] = "video-compressors", ["audio"] = "audio-compressors",
    ["document"] = "document-compressors", ["pdf"] = "pdf-compressors", ["archive"] = "archive-compressors",
};

// ---- Parse args ----
var files = new List<string>();
string preset = "recommended", apiBase = BaseUrl, suffix = "-min";
string? outputDir = null, token = null, apiKey = null;
bool quiet = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-h" or "--help": PrintHelp(); return 0;
        case "-v" or "--version": Console.WriteLine(Version); return 0;
        case "-q" or "--quiet": quiet = true; break;
        case "-o" or "--output": outputDir = args[++i]; break;
        case "-p" or "--preset": preset = args[++i]; break;
        case "--api": apiBase = args[++i]; break;
        case "--token": token = args[++i]; break;
        case "--api-key": apiKey = args[++i]; break;
        case "--suffix": suffix = args[++i]; break;
        default:
            if (args[i].StartsWith('-')) { Console.Error.WriteLine($"Unknown option: {args[i]}"); return 1; }
            files.Add(args[i]);
            break;
    }
}
// --api-key wins over the MICOMPRESS_API_KEY environment variable.
if (string.IsNullOrWhiteSpace(apiKey)) apiKey = Environment.GetEnvironmentVariable("MICOMPRESS_API_KEY");
apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
if (files.Count == 0) { PrintHelp(); return 1; }

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
http.DefaultRequestHeaders.Add("X-MiCompress-Client", "dotnet/" + Version);
if (token is not null)
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

int failed = 0;
foreach (var file in files)
{
    try
    {
        var (bytes, meta) = await CompressAsync(file);
        var dest = OutPath(file);
        await File.WriteAllBytesAsync(dest, bytes);
        if (!quiet)
            Console.WriteLine($"✔ {file} → {dest}  {Human(meta.InputSize)} → {Human(meta.OutputSize)} (-{meta.SavedPercent}%)");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"✗ {file}: {ex.Message}");
    }
}
return failed == 0 ? 0 : 1;

// ---- Helpers ----
async Task<(byte[] Bytes, Meta Meta)> CompressAsync(string path)
{
    var name = Path.GetFileName(path);
    var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
    if (!extCategory.TryGetValue(ext, out var category))
        throw new Exception($"Unsupported file type \".{ext}\". See {apiBase}/all-tools");
    if (!File.Exists(path))
        throw new FileNotFoundException($"File not found: {path}");

    using var form = new MultipartFormDataContent();
    var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(path));
    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    form.Add(fileContent, "file", name);
    form.Add(new StringContent(preset), "preset");

    var url = $"{apiBase.TrimEnd('/')}/{categoryPath[category]}/compress";
    using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
    req.Headers.Accept.ParseAdd("application/json");
    // The API key goes on the compress request only (not the download).
    if (apiKey is not null) req.Headers.Add("X-API-Key", apiKey);
    using var res = await http.SendAsync(req);

    var body = await res.Content.ReadAsStringAsync();
    Meta? meta;
    try { meta = JsonSerializer.Deserialize<Meta>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
    catch (JsonException) { throw new Exception($"Unexpected response (HTTP {(int)res.StatusCode})"); }
    if (meta is null || !meta.Success || string.IsNullOrEmpty(meta.DownloadUrl))
        throw new Exception(DescribeError((int)res.StatusCode, meta));

    var bytes = await http.GetByteArrayAsync($"{apiBase.TrimEnd('/')}{meta.DownloadUrl}");
    return (bytes, meta);
}

// Turn a failed API response into a helpful message (adds hints for auth/quota codes).
static string DescribeError(int status, Meta? meta)
{
    var msg = meta?.Error ?? $"HTTP {status}";
    return meta?.Code switch
    {
        "LOGIN_REQUIRED" => "File exceeds the anonymous size limit. Pass --api-key <key> or set MICOMPRESS_API_KEY " +
                            $"(free key at {AccountUrl}).",
        // Only append a link when the server text doesn't already include it.
        "INVALID_API_KEY" when !msg.Contains(AccountUrl) => $"{msg} Check your key at {AccountUrl}",
        "API_QUOTA_REACHED" or "USAGE_LIMIT_REACHED" when !msg.Contains(PlansUrl) => $"{msg} See plans and limits at {PlansUrl}",
        _ => msg,
    };
}

string OutPath(string input)
{
    var name = Path.GetFileName(input);
    if (outputDir is not null)
    {
        Directory.CreateDirectory(outputDir);
        return Path.Combine(outputDir, name);
    }
    var dir = Path.GetDirectoryName(input) ?? ".";
    var stem = Path.GetFileNameWithoutExtension(name);
    return Path.Combine(dir, $"{stem}{suffix}{Path.GetExtension(name)}");
}

static string Human(long n)
{
    string[] u = { "B", "KB", "MB", "GB" };
    double v = n; int i = 0;
    while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
    return i == 0 ? $"{Math.Round(v)}{u[i]}" : $"{v:0.0}{u[i]}";
}

static void PrintHelp() => Console.WriteLine(
    "micompress <file...> [options]\n" +
    "  -o, --output DIR   Output directory\n" +
    "  -p, --preset NAME  lossless|recommended|extreme\n" +
    "      --api-key KEY  Personal API key from micompress.com/account\n" +
    "                     (or set MICOMPRESS_API_KEY)\n" +
    "      --token JWT    Legacy login token (still supported)\n" +
    "      --suffix STR   Suffix for output files (default -min)\n" +
    "      --api URL      API base URL\n" +
    "  -q, --quiet        Only print errors\n" +
    $"Plans and limits: {PlansUrl}\n" +
    "Powered by micompress.com");

record Meta
{
    public bool Success { get; init; }
    public string? Code { get; init; }
    public string? Error { get; init; }
    public string? DownloadUrl { get; init; }
    public long InputSize { get; init; }
    public long OutputSize { get; init; }
    public int SavedPercent { get; init; }
    public string? Format { get; init; }
}
