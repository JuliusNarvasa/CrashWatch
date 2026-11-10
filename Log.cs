namespace CrashWatch;

internal static class Log
{
    private static readonly object _lock = new();

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashWatch");

    public static string FilePath => Path.Combine(Dir, "crashwatch.log");

    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            lock (_lock)
            {
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // never let logging kill the app
        }
    }
}
