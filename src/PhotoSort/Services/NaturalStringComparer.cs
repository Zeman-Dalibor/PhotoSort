namespace PhotoSort.Services;

/// <summary>
/// Orders names the way a person expects: IMG_2 before IMG_10.
/// Digit runs are compared numerically, everything else case-insensitively.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;

        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                var numberX = x.AsSpan(startX, i - startX).TrimStart('0');
                var numberY = y.AsSpan(startY, j - startY).TrimStart('0');

                if (numberX.Length != numberY.Length)
                {
                    return numberX.Length - numberY.Length;
                }

                var digits = numberX.SequenceCompareTo(numberY);
                if (digits != 0)
                {
                    return digits;
                }
            }
            else
            {
                var chars = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (chars != 0)
                {
                    return chars;
                }

                i++;
                j++;
            }
        }

        return (x.Length - i) - (y.Length - j);
    }
}
