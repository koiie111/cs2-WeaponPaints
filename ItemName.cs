using System.Text;

namespace WeaponPaints;

internal static class ItemName
{
    // m_szCustomName has 161 bytes, including its terminating zero.
    internal static string Truncate(string? name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        if (Encoding.UTF8.GetByteCount(name) <= 160) return name;

        var result = new StringBuilder();
        int bytes = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 160) break;
            result.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
}
