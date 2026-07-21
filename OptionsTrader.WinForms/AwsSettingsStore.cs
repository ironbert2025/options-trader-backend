using System.Text.Json;

namespace OptionsTrader.WinForms;

public record AwsSettings(string AccessKey, string SecretKey, string BucketName, string Region);

public static class AwsSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "aws_settings.json");

    public static AwsSettings Load()
    {
        if (!File.Exists(FilePath)) return new AwsSettings(string.Empty, string.Empty, "options-trader-screenshots", "us-east-2");
        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<AwsSettings>(json) ?? new AwsSettings(string.Empty, string.Empty, "options-trader-screenshots", "us-east-2");
    }

    public static void Save(AwsSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
    }
}
