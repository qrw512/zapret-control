using System.Collections.Generic;

namespace ZapretGui.Models;

public class FakeFileItem
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ReplaceFakesInfo
{
    public List<FakeFileItem> Files { get; set; } = new();
    public string CurrentDiscordFake { get; set; } = "(not found)";
    public string CurrentGameFake { get; set; } = "(not found)";
}
