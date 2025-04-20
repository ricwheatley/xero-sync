// XeroSync.Desktop/Services/UiSettings.cs
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace XeroSync.Desktop.Services;

public sealed record UiSettings(
    Guid TenantGuid,
    string RunMode,
    DateTime FyStart,
    DateTime FyEnd);

public static class UiSettingsStore
{
    private static readonly string Path = System.IO.Path.Combine("config", "ui-settings.json");
    private static readonly JsonSerializerOptions Opt =
        new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static UiSettings Load() =>
        File.Exists(Path)
            ? JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(Path), Opt)
                ?? throw new("Failed to parse ui‑settings")
            : new UiSettings(Guid.Empty, "Both",
                new DateTime(DateTime.Today.Year, 4, 1),
                new DateTime(DateTime.Today.Year + 1, 3, 31));

    public static void Save(UiSettings settings) =>
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, Opt));
}
