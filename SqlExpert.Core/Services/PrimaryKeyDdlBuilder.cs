using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>
/// Statements for a primary-key change made from the Field editor or the Constraints tab.
/// <see cref="Before"/> runs before the column statements (DROP of the old key), <see cref="After"/> after them
/// (ADD of the new key). <see cref="Blockers"/> non-empty = the change is refused (e.g. foreign keys depend on the key).
/// </summary>
public sealed record PrimaryKeyPlan(IReadOnlyList<string> Before, IReadOnlyList<string> After, IReadOnlyList<string> Blockers, string? Summary)
{
    public static PrimaryKeyPlan None { get; } = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null);
    public bool IsEmpty => Before.Count == 0 && After.Count == 0 && Blockers.Count == 0;
    public bool IsBlocked => Blockers.Count > 0;
    /// <summary>True when an existing key is dropped and re-created (the dialog asks for confirmation).</summary>
    public bool RecreatesExistingKey { get; init; }
}

/// <summary>Pure T-SQL generation for PRIMARY KEY constraints (bracket-quoted identifiers, one transaction).</summary>
public static class PrimaryKeyDdlBuilder
{
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static KeyConstraintStructure? PrimaryKey(TableStructure t) => t.KeyConstraints.FirstOrDefault(k => k.IsPrimaryKey);

    public static bool IsInPrimaryKey(TableStructure t, string column) =>
        PrimaryKey(t)?.Columns.Any(c => Same(c.Name, column)) == true;

    /// <summary>Foreign keys (of any table, self references included) whose referenced columns are exactly the key's columns.</summary>
    public static IReadOnlyList<ForeignKeyStructure> ForeignKeysUsing(TableStructure t, KeyConstraintStructure key)
    {
        var keyCols = key.Columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return t.ForeignKeys
            .Where(fk => Same(fk.ReferencedSchema, t.Schema) && Same(fk.ReferencedTable, t.Name)
                         && fk.ReferencedColumns.Count == keyCols.Count && fk.ReferencedColumns.All(keyCols.Contains))
            .ToList();
    }

    /// <summary>"[FK_x] de [dbo].[Pedidos]" lines for messages.</summary>
    public static IReadOnlyList<string> DescribeForeignKeys(IEnumerable<ForeignKeyStructure> fks) =>
        fks.Select(fk => $"{SqlText.Bracket(fk.Name)} de {SqlText.Qualified(fk.ParentSchema, fk.ParentTable)}").ToList();

    /// <summary>
    /// "PK_&lt;table&gt;", or "PK_&lt;table&gt;_2", "_3"… when the name is taken by a constraint of the table or by
    /// <paramref name="reservedNames"/> (other objects of the schema). At most 128 characters.
    /// </summary>
    public static string NewPrimaryKeyName(TableStructure t, IEnumerable<string>? reservedNames = null)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in t.KeyConstraints) used.Add(k.Name);
        foreach (var c in t.CheckConstraints) used.Add(c.Name);
        foreach (var f in t.ForeignKeys) used.Add(f.Name);
        foreach (var i in t.Indexes) used.Add(i.Name);
        foreach (var c in t.Columns) if (!string.IsNullOrEmpty(c.DefaultName)) used.Add(c.DefaultName!);
        foreach (var n in t.SchemaPrimaryKeyNames) used.Add(n);
        if (reservedNames is not null) foreach (var n in reservedNames) used.Add(n);

        static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
        var baseName = Cut("PK_" + t.Name, 128);
        if (!used.Contains(baseName)) return baseName;
        for (var i = 2; ; i++)
        {
            var suffix = "_" + i;
            var candidate = Cut(baseName, 128 - suffix.Length) + suffix;
            if (!used.Contains(candidate)) return candidate;
        }
    }

    /// <summary>A new key may be CLUSTERED only when the table has no clustered index (other than <paramref name="replacing"/>).</summary>
    public static bool CanBeClustered(TableStructure t, KeyConstraintStructure? replacing = null) =>
        !t.Indexes.Any(i => i.IsClustered)
        && !t.KeyConstraints.Any(k => k.IsClustered && !ReferenceEquals(k, replacing) && !(replacing is not null && Same(k.Name, replacing.Name)));

    public static string ColumnList(IEnumerable<IndexColumnRef> columns) =>
        string.Join(", ", columns.Select(c => SqlText.Bracket(c.Name) + (c.IsDescending ? " DESC" : "")));

    public static string AddPrimaryKey(string schema, string table, string name, bool clustered, IEnumerable<IndexColumnRef> columns) =>
        $"ALTER TABLE {SqlText.Qualified(schema, table)} ADD CONSTRAINT {SqlText.Bracket(name)} PRIMARY KEY {(clustered ? "CLUSTERED" : "NONCLUSTERED")} ({ColumnList(columns)});";

    public static string DropConstraint(string schema, string table, string name) =>
        $"ALTER TABLE {SqlText.Qualified(schema, table)} DROP CONSTRAINT {SqlText.Bracket(name)};";

    /// <summary>
    /// Key change caused by the Field editor. <paramref name="originalColumn"/> is null for a new field.
    /// Rules: adding a field to a table without key creates PK_&lt;table&gt; (CLUSTERED when possible) with that field;
    /// adding it to an existing key, removing it, or altering the type/NULL of a key field drops and re-creates the key
    /// with the same name, clustering and column order (the field is appended at the end). Refused when foreign keys
    /// reference the key, since dropping it would break them.
    /// </summary>
    public static PrimaryKeyPlan PlanFieldChange(TableStructure t, string? originalColumn, string newColumn,
        bool willBeInKey, bool columnAltered, IEnumerable<string>? reservedNames = null)
    {
        var pk = PrimaryKey(t);
        var wasInKey = originalColumn is not null && pk is not null && pk.Columns.Any(c => Same(c.Name, originalColumn));
        if (!wasInKey && !willBeInKey) return PrimaryKeyPlan.None;
        if (wasInKey && willBeInKey && !columnAltered) return PrimaryKeyPlan.None; // a rename is handled by sp_rename

        if (pk is null)
        {
            var name = NewPrimaryKeyName(t, reservedNames);
            var clustered = CanBeClustered(t);
            return new PrimaryKeyPlan(
                Array.Empty<string>(),
                new[] { AddPrimaryKey(t.Schema, t.Name, name, clustered, new[] { new IndexColumnRef(newColumn) }) },
                Array.Empty<string>(),
                $"Cria a chave primária {SqlText.Bracket(name)} ({(clustered ? "CLUSTERED" : "NONCLUSTERED")}) com {SqlText.Bracket(newColumn)}.");
        }

        var columns = new List<IndexColumnRef>();
        foreach (var c in pk.Columns)
        {
            if (wasInKey && Same(c.Name, originalColumn!))
            {
                if (willBeInKey) columns.Add(c with { Name = newColumn });
                continue;
            }
            columns.Add(c);
        }
        if (!wasInKey && willBeInKey) columns.Add(new IndexColumnRef(newColumn));

        var fks = ForeignKeysUsing(t, pk);
        var blockers = fks.Count == 0
            ? Array.Empty<string>()
            : DescribeForeignKeys(fks).Select(f => $"a chave primária {SqlText.Bracket(pk.Name)} é referenciada pela foreign key {f}").ToArray();

        var before = new[] { DropConstraint(t.Schema, t.Name, pk.Name) };
        var after = columns.Count == 0
            ? Array.Empty<string>()
            : new[] { AddPrimaryKey(t.Schema, t.Name, pk.Name, pk.IsClustered, columns) };
        var summary = columns.Count == 0
            ? $"Remove a chave primária {SqlText.Bracket(pk.Name)} (o campo era o único da chave)."
            : !wasInKey
                ? $"Inclui {SqlText.Bracket(newColumn)} na chave primária {SqlText.Bracket(pk.Name)}: ({ColumnList(columns)})."
                : willBeInKey
                    ? $"Recria a chave primária {SqlText.Bracket(pk.Name)} para alterar o campo: ({ColumnList(columns)})."
                    : $"Tira {SqlText.Bracket(originalColumn!)} da chave primária {SqlText.Bracket(pk.Name)}: ({ColumnList(columns)}).";
        return new PrimaryKeyPlan(before, after, blockers, summary) { RecreatesExistingKey = true };
    }

    /// <summary>Constraints tab "New constraint" (primary key): validation message, or null when the columns are usable.</summary>
    public static string? ValidateNewPrimaryKey(TableStructure t, IReadOnlyList<string> columns, string name)
    {
        if (PrimaryKey(t) is { } pk) return $"A tabela já tem a chave primária {SqlText.Bracket(pk.Name)}.";
        if (string.IsNullOrWhiteSpace(name)) return "Informe o nome da constraint.";
        if (name.Length > 128) return "O nome pode ter no máximo 128 caracteres.";
        if (columns.Count == 0) return "Escolha ao menos um campo.";
        if (columns.Count > 16) return "Uma chave primária pode ter no máximo 16 campos.";
        foreach (var c in columns)
        {
            var col = t.FindColumn(c);
            if (col is null) return $"O campo [{c}] não existe.";
            var problem = KeyTypeProblem(col.TypeName, col.Length, col.IsUserDefinedType);
            if (problem is not null) return problem;
            if (col.IsComputed && !col.IsPersisted) return $"[{c}] é uma coluna computada não persistida.";
        }
        return null;
    }

    /// <summary>
    /// Constraints tab: ALTER COLUMN … NOT NULL for nullable key fields, then ADD CONSTRAINT … PRIMARY KEY.
    /// </summary>
    public static IReadOnlyList<string> BuildCreatePrimaryKey(TableStructure t, IReadOnlyList<string> columns, string name, bool clustered)
    {
        var list = new List<string>();
        foreach (var c in columns)
        {
            var col = t.FindColumn(c);
            if (col is null || !col.IsNullable || col.IsComputed) continue;
            var f = FieldDefinition.FromColumn(col);
            f.IsNullable = false;
            // A bound default does not block a NULL → NOT NULL change; type and collation stay the same.
            list.Add($"ALTER TABLE {SqlText.Qualified(t.Schema, t.Name)} ALTER COLUMN {FieldDdlBuilder.ColumnDefinition(f, includeIdentity: false)};");
        }
        list.Add(AddPrimaryKey(t.Schema, t.Name, name.Trim(), clustered, columns.Select(c => new IndexColumnRef(t.FindColumn(c)?.Name ?? c))));
        return list;
    }

    /// <summary>Why a column of this type cannot be a key column (null = it can).</summary>
    public static string? KeyTypeProblem(string type, int? length, bool userDefined)
    {
        if (userDefined) return null;
        var n = type.Trim().ToLowerInvariant();
        if (n is "text" or "ntext" or "image" or "xml" or "geography" or "geometry")
            return $"Campos {n} não podem fazer parte de uma chave primária.";
        if (length == -1 && SqlTypeCatalog.SupportsMax(n))
            return $"Campos {n}(max) não podem fazer parte de uma chave primária.";
        return null;
    }

    /// <summary>Reasons the Constraints tab refuses to drop <paramref name="constraintName"/> (empty = allowed).</summary>
    public static IReadOnlyList<string> GetDropConstraintBlockers(TableStructure t, string constraintName)
    {
        var key = t.KeyConstraints.FirstOrDefault(k => Same(k.Name, constraintName));
        if (key is null) return Array.Empty<string>();
        return DescribeForeignKeys(ForeignKeysUsing(t, key))
            .Select(f => $"é referenciada pela foreign key {f}")
            .ToList();
    }

    /// <summary>Kind of a constraint of the table by name ("PRIMARY KEY", "UNIQUE", "FOREIGN KEY", "CHECK"), or null.</summary>
    public static string? ConstraintKind(TableStructure t, string name)
    {
        if (t.KeyConstraints.FirstOrDefault(k => Same(k.Name, name)) is { } k) return k.IsPrimaryKey ? "PRIMARY KEY" : "UNIQUE";
        if (t.ForeignKeys.Any(f => f.IsOutgoing && Same(f.Name, name))) return "FOREIGN KEY";
        if (t.CheckConstraints.Any(c => Same(c.Name, name))) return "CHECK";
        return null;
    }
}
