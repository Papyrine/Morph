static class MarkupDiff
{
    public static string? Diff(string before, string after)
    {
        if (before == after)
        {
            return null;
        }

        var left = before.Split('\n');
        var right = after.Split('\n');
        var table = new int[left.Length + 1, right.Length + 1];
        for (var i = left.Length - 1; i >= 0; i--)
        {
            for (var j = right.Length - 1; j >= 0; j--)
            {
                table[i, j] = left[i] == right[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var builder = new StringBuilder();
        var x = 0;
        var y = 0;
        while (x < left.Length && y < right.Length)
        {
            if (left[x] == right[y])
            {
                x++;
                y++;
            }
            else if (table[x + 1, y] >= table[x, y + 1])
            {
                builder.Append("- ").Append(left[x++]).Append('\n');
            }
            else
            {
                builder.Append("+ ").Append(right[y++]).Append('\n');
            }
        }

        while (x < left.Length)
        {
            builder.Append("- ").Append(left[x++]).Append('\n');
        }

        while (y < right.Length)
        {
            builder.Append("+ ").Append(right[y++]).Append('\n');
        }

        return builder.ToString();
    }
}
