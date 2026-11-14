using System.Collections.Concurrent;

namespace LegoList.Services;

/// <summary>Remembers Rebrickable theme names so each theme is only fetched once.</summary>
public class ThemeNameCache
{
    private readonly ConcurrentDictionary<int, string> names = new();

    public bool TryGet(int themeId, out string? name)
    {
        var found = names.TryGetValue(themeId, out var value);
        name = value;
        return found;
    }

    public void Set(int themeId, string name) => names[themeId] = name;
}
