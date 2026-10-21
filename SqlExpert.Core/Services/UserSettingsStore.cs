using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>
/// Loads/saves <see cref="UserSettings"/> as JSON. Missing or corrupt files give defaults;
/// saving keeps any other keys already present in the file.
/// </summary>
public sealed class UserSettingsStore
{
    private const string FontKey = "dataGridFontSize";
    private const string WindowKey = "mainWindow";
    private const string EditorFontKey = "sqlEditorFontSize";
    private const string FetchRowsKey = "sqlFetchRows";
    private const string SortByAliasKey = "explorerSortByAlias";
    private const string HideDisconnectedKey = "hideDisconnectedDatabases";
    private const string ShowDescriptionKey = "showObjectsDescription";
    private const string CheckUpdatesKey = "checkForUpdatesOnStartup";
    private const string PreReleasesKey = "includePreReleases";
    private const string SkippedVersionKey = "skippedUpdateVersion";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public UserSettingsStore(string? path = null)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            StorePath = path;
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.GetTempPath();
        StorePath = Path.Combine(appData, "SqlExpert", "settings.json");
    }

    public string StorePath { get; }

    public UserSettings Load()
    {
        var settings = new UserSettings();
        try
        {
            if (!File.Exists(StorePath)) return settings;
            var node = JsonNode.Parse(File.ReadAllText(StorePath, Encoding.UTF8)) as JsonObject;
            if (node?[FontKey] is JsonValue v && v.TryGetValue<int>(out var size))
                settings.DataGridFontSize = UserSettings.ClampFontSize(size);
            if (node?[EditorFontKey] is JsonValue ef && ef.TryGetValue<int>(out var efs))
                settings.SqlEditorFontSize = UserSettings.ClampFontSize(efs);
            if (node?[FetchRowsKey] is JsonValue fr && fr.TryGetValue<int>(out var frs))
                settings.SqlFetchRows = UserSettings.ClampFetchRows(frs);
            settings.ExplorerSortByAlias = ReadBool(node?[SortByAliasKey]);
            settings.HideDisconnectedDatabases = ReadBool(node?[HideDisconnectedKey]);
            settings.ShowObjectsDescription = ReadBool(node?[ShowDescriptionKey]);
            if (node?[CheckUpdatesKey] is JsonValue cu && cu.TryGetValue<bool>(out var cub))
                settings.CheckForUpdatesOnStartup = cub;
            if (node?[PreReleasesKey] is JsonValue pr && pr.TryGetValue<bool>(out var prb))
                settings.IncludePreReleases = prb;
            if (node?[SkippedVersionKey] is JsonValue sk && sk.TryGetValue<string>(out var sks) && !string.IsNullOrWhiteSpace(sks))
                settings.SkippedUpdateVersion = sks;
            if (node?[WindowKey] is JsonObject w)
                settings.MainWindow = ReadPlacement(w);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            // Corrupt/unreadable settings: fall back to defaults.
        }

        return settings;
    }

    private static bool ReadBool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static WindowPlacement? ReadPlacement(JsonObject w)
    {
        static double? D(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
        var left = D(w["left"]); var top = D(w["top"]); var width = D(w["width"]); var height = D(w["height"]);
        if (left is null || top is null || width is null || height is null) return null;
        var max = w["maximized"] is JsonValue mv && mv.TryGetValue<bool>(out var b) && b;
        var p = new WindowPlacement(left.Value, top.Value, width.Value, height.Value, max);
        return p.IsValid ? p : null;
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        JsonObject root;
        try
        {
            root = File.Exists(StorePath)
                ? JsonNode.Parse(File.ReadAllText(StorePath, Encoding.UTF8)) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root[FontKey] = UserSettings.ClampFontSize(settings.DataGridFontSize);
        root[EditorFontKey] = UserSettings.ClampFontSize(settings.SqlEditorFontSize);
        root[FetchRowsKey] = UserSettings.ClampFetchRows(settings.SqlFetchRows);
        root[SortByAliasKey] = settings.ExplorerSortByAlias;
        root[HideDisconnectedKey] = settings.HideDisconnectedDatabases;
        root[ShowDescriptionKey] = settings.ShowObjectsDescription;
        root[CheckUpdatesKey] = settings.CheckForUpdatesOnStartup;
        if (settings.IncludePreReleases is bool pre) root[PreReleasesKey] = pre; else root.Remove(PreReleasesKey);
        if (string.IsNullOrWhiteSpace(settings.SkippedUpdateVersion)) root.Remove(SkippedVersionKey);
        else root[SkippedVersionKey] = settings.SkippedUpdateVersion;
        // Null placement = "not changed": keep whatever is already saved.
        if (settings.MainWindow is { } p && p.IsValid)
        {
            root[WindowKey] = new JsonObject
            {
                ["left"] = p.Left,
                ["top"] = p.Top,
                ["width"] = p.Width,
                ["height"] = p.Height,
                ["maximized"] = p.IsMaximized
            };
        }

        var dir = Path.GetDirectoryName(StorePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var tmp = StorePath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
        File.Move(tmp, StorePath, overwrite: true);
    }
}
