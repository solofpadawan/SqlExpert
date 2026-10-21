using System.Globalization;
using SqlExpert.Core.Explorer;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.DataForms;

/// <summary>
/// Pure rules of the Data tab foreign-key lookup ("Data of [TABLE]" dropdown):
/// metadata grouping, which grid columns get a dropdown, which referenced columns are shown,
/// which row is preselected and how a picked value is converted to the local column type.
/// </summary>
public static class ForeignKeyLookup
{
    /// <summary>Default number of columns shown in the popup (keys included).</summary>
    public const int DefaultMaxColumns = 6;

    /// <summary>Text shown for the "set NULL" entry of nullable FKs.</summary>
    public const string NullEntryText = "(null)";

    /// <summary>Name fragments that make a character column a preferred display column (upper-case, accents ignored).</summary>
    public static readonly IReadOnlyList<string> PreferredNameParts = new[]
    {
        "DESC", "NOME", "NAME", "CODIGO", "CODE", "COD", "TITULO", "TITLE", "SIGLA", "RAZAO", "FANTASIA", "APELIDO", "LABEL"
    };

    /// <summary>Groups the per-column metadata rows into foreign keys (column pairs in constraint order).</summary>
    public static IReadOnlyList<ForeignKeyInfo> Group(IEnumerable<ForeignKeyColumnRow> rows) =>
        rows.GroupBy(r => r.Name, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.OrderBy(r => r.ColumnId).First();
                return new ForeignKeyInfo
                {
                    Name = first.Name,
                    ReferencedSchema = first.ReferencedSchema,
                    ReferencedTable = first.ReferencedTable,
                    IsDisabled = first.IsDisabled,
                    Columns = g.OrderBy(r => r.ColumnId).Select(r => new ForeignKeyColumnPair(r.Column, r.ReferencedColumn)).ToList()
                };
            })
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Lookup per local column (case-insensitive name → target). A multi-column FK is enabled only when every
    /// one of its columns is in the grid and editable; it then appears on each of its columns and sets them all.
    /// When a column belongs to several FKs, an enabled one wins, then the one with fewer columns, then by name.
    /// </summary>
    public static IReadOnlyDictionary<string, ForeignKeyLookupTarget> ResolveTargets(
        IReadOnlyList<ForeignKeyInfo> foreignKeys, IReadOnlyList<LookupLocalColumn> gridColumns)
    {
        var byName = new Dictionary<string, LookupLocalColumn>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in gridColumns) byName.TryAdd(c.Name, c);

        var targets = new List<ForeignKeyLookupTarget>();
        foreach (var fk in foreignKeys)
        {
            if (fk.Columns.Count == 0) continue;
            var locals = fk.LocalColumns;
            string? reason = null;
            var missing = locals.Where(n => !byName.ContainsKey(n)).ToList();
            if (missing.Count > 0)
                reason = "coluna(s) fora da grade: " + string.Join(", ", missing);
            else if (locals.Any(n => !byName[n].IsEditable))
                reason = "coluna(s) somente leitura: " + string.Join(", ", locals.Where(n => !byName[n].IsEditable));
            targets.Add(new ForeignKeyLookupTarget
            {
                ForeignKey = fk,
                LocalColumns = locals,
                IsEnabled = reason is null,
                DisabledReason = reason,
                AllowsNull = reason is null && locals.All(n => byName[n].IsNullable)
            });
        }

        var result = new Dictionary<string, ForeignKeyLookupTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in gridColumns)
        {
            var best = targets
                .Where(t => t.LocalColumns.Contains(column.Name, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(t => t.IsEnabled)
                .ThenBy(t => t.LocalColumns.Count)
                .ThenBy(t => t.ForeignKey.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (best is not null) result.TryAdd(column.Name, best);
        }
        return result;
    }

    private static string Base(DataColumnInfo c) => (string.IsNullOrEmpty(c.BaseTypeName) ? c.TypeName : c.BaseTypeName).ToLowerInvariant();

    public static bool IsCharacterColumn(DataColumnInfo c) => Base(c) is "char" or "varchar" or "nchar" or "nvarchar";

    /// <summary>Types never shown in the popup (binary, xml, CLR, rowversion, sql_variant, legacy text).</summary>
    public static bool IsDisplayable(DataColumnInfo c) =>
        !c.IsClrType && !c.IsRowVersion &&
        Base(c) is not ("binary" or "varbinary" or "image" or "xml" or "timestamp" or "rowversion" or "sql_variant" or "text" or "ntext"
            or "geography" or "geometry" or "hierarchyid");

    /// <summary>Index into <see cref="PreferredNameParts"/> of the first fragment found in the name (-1 = none).</summary>
    public static int PreferredRank(string columnName)
    {
        var n = ExplorerFilterText.Fold(columnName).ToUpperInvariant();
        for (var i = 0; i < PreferredNameParts.Count; i++)
            if (n.Contains(PreferredNameParts[i], StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>
    /// Popup columns: the referenced key columns (FK order) first, then up to <paramref name="maxColumns"/> in total of
    /// display columns chosen by tier — character columns with a preferred name (DESC, NOME, NAME, CODIGO…), other
    /// short character columns, long (MAX) character columns, then any other displayable column — shown in table order.
    /// </summary>
    public static IReadOnlyList<DataColumnInfo> SelectDisplayColumns(
        IReadOnlyList<DataColumnInfo> referencedColumns, IReadOnlyList<string> keyColumns, int maxColumns = DefaultMaxColumns)
    {
        var keys = new List<DataColumnInfo>();
        foreach (var k in keyColumns)
        {
            var c = referencedColumns.FirstOrDefault(x => string.Equals(x.Name, k, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Coluna referenciada não encontrada: {k}", nameof(keyColumns));
            keys.Add(c);
        }

        var room = Math.Max(1, maxColumns - keys.Count);
        int Tier(DataColumnInfo c)
        {
            if (IsCharacterColumn(c))
            {
                if (c.MaxLength == -1) return 2;
                return PreferredRank(c.Name) >= 0 ? 0 : 1;
            }
            return 3;
        }

        var chosen = referencedColumns
            .Where(c => !keys.Contains(c) && IsDisplayable(c))
            .OrderBy(Tier)
            .ThenBy(c => Tier(c) == 0 ? PreferredRank(c.Name) : 0)
            .ThenBy(c => c.Ordinal)
            .Take(room)
            .OrderBy(c => c.Ordinal)
            .ToList();
        return keys.Concat(chosen).ToList();
    }

    /// <summary>SQL Server-like equality of key values: numbers by value, strings case-insensitive ignoring trailing spaces.</summary>
    public static bool KeyEquals(object? a, object? b)
    {
        var an = DataValueText.IsNull(a);
        var bn = DataValueText.IsNull(b);
        if (an || bn) return an && bn;
        switch (a)
        {
            case string sa when b is string sb:
                return string.Equals(sa.TrimEnd(' '), sb.TrimEnd(' '), StringComparison.OrdinalIgnoreCase);
            case byte[] xa when b is byte[] xb:
                return xa.AsSpan().SequenceEqual(xb);
        }
        if (IsNumber(a!) && IsNumber(b!))
        {
            try { return Convert.ToDecimal(a, CultureInfo.InvariantCulture) == Convert.ToDecimal(b, CultureInfo.InvariantCulture); }
            catch (OverflowException) { return Convert.ToDouble(a, CultureInfo.InvariantCulture).Equals(Convert.ToDouble(b, CultureInfo.InvariantCulture)); }
        }
        if (a is Guid ga && b is string gs) return Guid.TryParse(gs, out var g) && g == ga;
        if (b is Guid gb && a is string gs2) return Guid.TryParse(gs2, out var g2) && g2 == gb;
        return a!.Equals(b);
    }

    private static bool IsNumber(object v) => v is byte or sbyte or short or ushort or int or uint or long or ulong or decimal or float or double;

    /// <summary>
    /// Row of <paramref name="rows"/> whose first <c>current.Count</c> values (the key) equal the current value(s);
    /// -1 when not found or when any current value is NULL (nothing to preselect).
    /// </summary>
    public static int FindRowIndex(IReadOnlyList<object[]> rows, IReadOnlyList<object?> current)
    {
        if (current.Count == 0 || current.Any(DataValueText.IsNull)) return -1;
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Length < current.Count) continue;
            var match = true;
            for (var i = 0; i < current.Count && match; i++)
                match = KeyEquals(row[i], current[i]);
            if (match) return r;
        }
        return -1;
    }

    /// <summary>Picked referenced value → value for the local DataTable column (NULL → DBNull).</summary>
    public static object CoerceValue(object? value, Type targetType)
    {
        if (DataValueText.IsNull(value)) return DBNull.Value;
        if (targetType == typeof(object) || targetType.IsInstanceOfType(value)) return value!;
        if (targetType == typeof(Guid)) return value is string s ? Guid.Parse(s) : (Guid)value!;
        if (targetType == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (targetType == typeof(DateTimeOffset) && value is DateTime dt) return new DateTimeOffset(dt);
        if (targetType == typeof(TimeSpan) && value is TimeSpan ts) return ts;
        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture)!;
    }

    /// <summary>Indices of the rows whose text contains every word of <paramref name="term"/> (case/accent-insensitive).</summary>
    public static IReadOnlyList<int> FilterRows(IReadOnlyList<IReadOnlyList<string>> rowTexts, string? term)
    {
        var words = ExplorerFilterText.Fold(term ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<int>();
        for (var r = 0; r < rowTexts.Count; r++)
        {
            if (words.Length == 0) { result.Add(r); continue; }
            var text = ExplorerFilterText.Fold(string.Join(" \u0001 ", rowTexts[r]));
            if (words.All(w => text.Contains(w, StringComparison.Ordinal))) result.Add(r);
        }
        return result;
    }

    /// <summary>"5 records fetched" / "1000 records fetched (TOP 1000 — Fetch all para ler tudo)".</summary>
    public static string FetchedText(LookupData data, int shown)
    {
        var total = data.Rows.Count;
        var head = shown == total ? $"{total} records fetched" : $"{shown} of {total} records";
        if (!data.IsComplete) head += $" (TOP {data.Top})";
        return head;
    }
}
