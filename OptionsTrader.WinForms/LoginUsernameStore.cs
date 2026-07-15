namespace OptionsTrader.WinForms;

internal static class LoginUsernameStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptionsTrader", "last_login_username.txt");

    public static string Load()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void Save(string username)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, username);
    }
}
