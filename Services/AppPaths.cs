using System;
using System.IO;

namespace ZapretGui.Services;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "zapret");

    public static string Versions { get; } = Path.Combine(Root, "versions");

    public static string Logs { get; } = Path.Combine(Root, "logs");

    public static string Config { get; } = Path.Combine(Root, "config.json");

    public static void EnsureCreated()
    {
        try
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Versions);
            Directory.CreateDirectory(Logs);
        }
        catch { }
    }
}
