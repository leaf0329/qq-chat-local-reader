namespace QQChatLocalReader.Infrastructure;

internal static class SqlCipherRuntime
{
    private static readonly object Gate = new();
    private static bool initialized;

    public static void Initialize()
    {
        if (initialized) return;
        lock (Gate)
        {
            if (initialized) return;
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlcipher());
            SQLitePCL.raw.FreezeProvider(true);
            initialized = true;
        }
    }
}
