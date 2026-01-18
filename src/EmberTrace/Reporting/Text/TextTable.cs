using System.Text;

namespace EmberTrace.Reporting.Text;

internal sealed class TextTable
{
    private readonly List<string[]> _rows = new();
    private readonly int[] _widths;

    public TextTable(params string[] header)
    {
        _widths = new int[header.Length];
        AddRow(header);
    }

    public void AddRow(params string[] cols)
    {
        var row = new string[_widths.Length];
        for (int i = 0; i < row.Length; i++)
        {
            var s = i < cols.Length ? cols[i] : "";
            row[i] = s ?? "";
            if (row[i].Length > _widths[i])
                _widths[i] = row[i].Length;
        }

        _rows.Add(row);
    }

    public void AddSeparator(char c = '-')
    {
        var parts = new string[_widths.Length];
        for (int i = 0; i < parts.Length; i++)
            parts[i] = new string(c, Math.Max(1, _widths[i]));
        _rows.Add(parts);
    }

    public void WriteTo(StringBuilder sb, int gap = 2)
    {
        for (int r = 0; r < _rows.Count; r++)
        {
            var row = _rows[r];
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0)
                    sb.Append(' ', gap);

                var s = row[i];
                sb.Append(s);

                var pad = _widths[i] - s.Length;
                if (pad > 0)
                    sb.Append(' ', pad);
            }

            sb.AppendLine();
        }
    }
}
