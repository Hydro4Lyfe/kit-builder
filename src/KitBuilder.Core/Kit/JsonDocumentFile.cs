using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KitBuilder.Core.Kit;

/// <summary>
/// A JSON file edited in place. It is held as a <see cref="JsonObject"/> rather than a typed
/// model so keys this app does not know about survive a save: someone may add a setting for
/// a new script by hand, and the app must not silently drop it.
/// </summary>
public sealed class JsonDocumentFile
{
    // The kit's files are read by Windows tools and edited by hand: CRLF, 2-space indent,
    // and no \uXXXX escaping of characters like + or ' that the default encoder would mangle.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\r\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private JsonDocumentFile(string path, JsonObject root, DateTime? lastWriteUtc)
    {
        Path = path;
        Root = root;
        LastWriteUtc = lastWriteUtc;
    }

    public string Path { get; }
    public JsonObject Root { get; }

    /// <summary>When the file was last written as far as we know; null if it did not exist.</summary>
    public DateTime? LastWriteUtc { get; private set; }

    public static JsonDocumentFile Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        }) as JsonObject ?? throw new JsonException($"{System.IO.Path.GetFileName(path)} must contain a JSON object.");
        return new JsonDocumentFile(path, root, File.GetLastWriteTimeUtc(path));
    }

    /// <summary>Loads the file, or starts an empty object when it does not exist yet.</summary>
    public static JsonDocumentFile LoadOrEmpty(string path) =>
        File.Exists(path) ? Load(path) : new JsonDocumentFile(path, new JsonObject(), null);

    /// <summary>True when something else wrote the file after we loaded or saved it.</summary>
    public bool ChangedOnDisk()
    {
        DateTime? current = File.Exists(Path) ? File.GetLastWriteTimeUtc(Path) : null;
        return current != LastWriteUtc;
    }

    public void Save()
    {
        var text = Root.ToJsonString(WriteOptions) + "\r\n";
        File.WriteAllText(Path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        LastWriteUtc = File.GetLastWriteTimeUtc(Path);
    }
}
