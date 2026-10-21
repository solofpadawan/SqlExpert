using System.Globalization;
using System.Text;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>
/// Pure T-SQL generation for the Fields tab (New / Edit / Drop field). Every identifier is bracket-quoted.
/// Each method returns the statements to run, in order, inside one transaction.
/// </summary>
public static class FieldDdlBuilder
{
    // ===== Types =====

    public static string FormatType(FieldDefinition f)
    {
        if (!string.IsNullOrWhiteSpace(f.TypeSchema))
            return SqlText.Qualified(f.TypeSchema!, f.DataType);

        var t = f.DataType.Trim().ToLowerInvariant();
        if (SqlTypeCatalog.HasLength(t))
            return f.Length is null ? t : f.Length < 0 ? $"{t}(max)" : $"{t}({f.Length})";
        if (SqlTypeCatalog.HasPrecisionAndScale(t))
            return f.Precision is null ? t : $"{t}({f.Precision},{f.Scale ?? 0})";
        if (SqlTypeCatalog.HasFractionalScale(t))
            return f.Scale is null ? t : $"{t}({f.Scale})";
        return t;
    }

    /// <summary>Column definition without default: [name] type [COLLATE x] [IDENTITY(s,i)] NULL|NOT NULL.</summary>
    public static string ColumnDefinition(FieldDefinition f, bool includeIdentity)
    {
        var sb = new StringBuilder();
        sb.Append(SqlText.Bracket(f.Name)).Append(' ').Append(FormatType(f));
        if (SqlTypeCatalog.IsCharacterType(f.DataType) && !string.IsNullOrWhiteSpace(f.Collation))
            sb.Append(" COLLATE ").Append(f.Collation!.Trim());
        if (includeIdentity && f.IsIdentity)
            sb.Append(" IDENTITY(").Append(Num(f.IdentitySeed)).Append(',').Append(Num(f.IdentityIncrement)).Append(')');
        sb.Append(f.IsNullable ? " NULL" : " NOT NULL");
        return sb.ToString();
    }

    private static string Num(decimal d) => d.ToString("0.############################", CultureInfo.InvariantCulture);

    public static string DefaultConstraintName(string table, string column)
    {
        var name = $"DF_{table}_{column}";
        return name.Length <= 128 ? name : name[..128];
    }

    private static bool HasText(string? s) => !string.IsNullOrWhiteSpace(s);

    /// <summary>"0" → "(0)"; already wrapped expressions are kept.</summary>
    public static string WrapDefault(string expr)
    {
        var e = expr.Trim();
        return SqlText.StripOuterParens(e) == e ? $"({e})" : e;
    }

    public static bool SameDefault(string? a, string? b) =>
        string.Equals(SqlText.StripOuterParens(a), SqlText.StripOuterParens(b), StringComparison.Ordinal);

    // ===== Validation =====

    /// <summary>Returns a user-facing error, or null when the definition is valid.</summary>
    public static string? Validate(FieldDefinition f, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(f.Name)) return "Informe o nome do campo.";
        if (f.Name.Length > 128) return "O nome do campo pode ter no máximo 128 caracteres.";
        if (string.IsNullOrWhiteSpace(f.DataType)) return "Informe o tipo do campo.";
        var t = f.DataType;
        var userType = HasText(f.TypeSchema);

        if (!userType && SqlTypeCatalog.HasLength(t))
        {
            if (f.Length is null) return "Informe o tamanho (Size).";
            if (f.Length < 0 && !SqlTypeCatalog.SupportsMax(t)) return $"{t} não aceita (max).";
            if (f.Length == 0 || f.Length > SqlTypeCatalog.MaxLength(t))
                return $"Tamanho de {t} deve estar entre 1 e {SqlTypeCatalog.MaxLength(t)}" + (SqlTypeCatalog.SupportsMax(t) ? " (ou max)." : ".");
        }

        if (!userType && SqlTypeCatalog.HasPrecisionAndScale(t))
        {
            if (f.Precision is null or < 1 or > 38) return "Precision deve estar entre 1 e 38.";
            if (f.Scale is < 0 || (f.Scale ?? 0) > f.Precision) return "Scale deve estar entre 0 e a precision.";
        }

        if (!userType && SqlTypeCatalog.HasFractionalScale(t) && f.Scale is < 0 or > 7)
            return "Scale de frações de segundo deve estar entre 0 e 7.";

        if (HasText(f.Collation) && !SqlTypeCatalog.IsCharacterType(t))
            return "Collation só se aplica a tipos de caractere.";

        if (f.IsIdentity && isNew)
        {
            if (!SqlTypeCatalog.SupportsIdentity(t, f.Scale)) return $"IDENTITY não é permitido para {t}.";
            if (f.IsNullable) return "Um campo IDENTITY não pode aceitar NULL.";
            if (HasText(f.DefaultExpression)) return "Um campo IDENTITY não pode ter Default.";
            if (f.IdentityIncrement == 0) return "O incremento do IDENTITY não pode ser 0.";
        }

        if (SqlTypeCatalog.IsRowVersion(t) && HasText(f.DefaultExpression))
            return "rowversion não aceita Default.";

        return null;
    }

    /// <summary>True when adding this field to a table with rows will fail (NOT NULL without default/identity).</summary>
    public static bool NeedsRowsWarning(FieldDefinition f, long rowCount) =>
        rowCount > 0 && !f.IsNullable && !f.IsIdentity && !HasText(f.DefaultExpression) && !SqlTypeCatalog.IsRowVersion(f.DataType);

    // ===== New field =====

    public static IReadOnlyList<string> BuildAddColumn(string schema, string table, FieldDefinition f)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        sb.Append("ALTER TABLE ").Append(SqlText.Qualified(schema, table))
          .Append(" ADD ").Append(ColumnDefinition(f, includeIdentity: true));
        if (HasText(f.DefaultExpression))
        {
            sb.Append(" CONSTRAINT ").Append(SqlText.Bracket(DefaultConstraintName(table, f.Name)))
              .Append(" DEFAULT ").Append(WrapDefault(f.DefaultExpression!));
            // With a default on a nullable column, existing rows get NULL unless WITH VALUES is used.
            if (f.IsNullable) sb.Append(" WITH VALUES");
        }
        sb.Append(';');
        list.Add(sb.ToString());

        if (HasText(f.Description))
            list.Add(AddDescription(schema, table, f.Name, f.Description!));
        return list;
    }

    // ===== Edit field =====

    /// <summary>
    /// Builds the change script from <paramref name="original"/> to <paramref name="updated"/>.
    /// Order: drop default → ALTER COLUMN → sp_rename → add default → description.
    /// Returns an empty list when nothing changed.
    /// </summary>
    public static IReadOnlyList<string> BuildAlterColumn(
        string schema, string table, ColumnStructure original, FieldDefinition updated)
    {
        var before = FieldDefinition.FromColumn(original);
        var list = new List<string>();
        var target = SqlText.Qualified(schema, table);

        var typeChanged = !string.Equals(FormatType(before), FormatType(updated), StringComparison.OrdinalIgnoreCase);
        var nullChanged = before.IsNullable != updated.IsNullable;
        var collationChanged = SqlTypeCatalog.IsCharacterType(updated.DataType)
            && !string.Equals((before.Collation ?? "").Trim(), (updated.Collation ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
            && HasText(updated.Collation);
        var alterColumn = typeChanged || nullChanged || collationChanged;

        var renamed = !string.Equals(before.Name, updated.Name, StringComparison.Ordinal);
        var hadDefault = HasText(original.DefaultDefinition) && HasText(original.DefaultName);
        var defaultChanged = !SameDefault(original.DefaultDefinition, updated.DefaultExpression);
        // A bound default blocks ALTER COLUMN, so it is dropped and re-created around it.
        var dropDefault = hadDefault && (defaultChanged || alterColumn);
        var addDefault = HasText(updated.DefaultExpression) && (defaultChanged || dropDefault);

        if (dropDefault)
            list.Add($"ALTER TABLE {target} DROP CONSTRAINT {SqlText.Bracket(original.DefaultName!)};");

        if (alterColumn)
        {
            var asOld = updated.Clone();
            asOld.Name = before.Name;
            list.Add($"ALTER TABLE {target} ALTER COLUMN {ColumnDefinition(asOld, includeIdentity: false)};");
        }

        if (renamed)
            list.Add($"EXEC sys.sp_rename {SqlText.NLiteral(target + "." + SqlText.Bracket(before.Name))}, {SqlText.NLiteral(updated.Name)}, N'COLUMN';");

        if (addDefault)
        {
            var name = hadDefault && !original.DefaultIsSystemNamed
                ? original.DefaultName!
                : DefaultConstraintName(table, updated.Name);
            list.Add($"ALTER TABLE {target} ADD CONSTRAINT {SqlText.Bracket(name)} DEFAULT {WrapDefault(updated.DefaultExpression!)} FOR {SqlText.Bracket(updated.Name)};");
        }

        var oldDesc = original.Description ?? "";
        var newDesc = updated.Description ?? "";
        if (!string.Equals(oldDesc, newDesc, StringComparison.Ordinal))
        {
            if (oldDesc.Length == 0) list.Add(AddDescription(schema, table, updated.Name, newDesc));
            else if (newDesc.Length == 0) list.Add(DropDescription(schema, table, updated.Name));
            else list.Add(UpdateDescription(schema, table, updated.Name, newDesc));
        }

        return list;
    }

    // ===== Drop field =====

    /// <summary>Reasons the column cannot be dropped by the Fields tab (empty = allowed).</summary>
    public static IReadOnlyList<string> GetDropBlockers(TableStructure t, string column)
    {
        var reasons = new List<string>();
        bool Is(string c) => string.Equals(c, column, StringComparison.OrdinalIgnoreCase);

        foreach (var k in t.KeyConstraints.Where(k => k.Columns.Any(c => Is(c.Name))))
            reasons.Add(k.IsPrimaryKey
                ? $"faz parte da chave primária {SqlText.Bracket(k.Name)}"
                : $"faz parte da constraint UNIQUE {SqlText.Bracket(k.Name)}");

        foreach (var fk in t.ForeignKeys)
        {
            if (fk.IsOutgoing && fk.Columns.Any(Is))
                reasons.Add($"faz parte da foreign key {SqlText.Bracket(fk.Name)} (→ {SqlText.Qualified(fk.ReferencedSchema, fk.ReferencedTable)})");
            var referencesThis = string.Equals(fk.ReferencedSchema, t.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fk.ReferencedTable, t.Name, StringComparison.OrdinalIgnoreCase);
            if (referencesThis && fk.ReferencedColumns.Any(Is))
                reasons.Add($"é referenciado pela foreign key {SqlText.Bracket(fk.Name)} de {SqlText.Qualified(fk.ParentSchema, fk.ParentTable)}");
        }

        foreach (var ix in t.Indexes.Where(i => i.KeyColumns.Any(c => Is(c.Name)) || i.IncludedColumns.Any(Is)))
            reasons.Add($"faz parte do índice {SqlText.Bracket(ix.Name)}");

        if (t.Columns.Count <= 1 && t.FindColumn(column) is not null)
            reasons.Add("é o único campo da tabela");

        return reasons;
    }

    /// <summary>Drops the column's default and column-level CHECK constraints, then the column.</summary>
    public static IReadOnlyList<string> BuildDropColumn(TableStructure t, string column)
    {
        var target = SqlText.Qualified(t.Schema, t.Name);
        var list = new List<string>();
        var col = t.FindColumn(column);
        if (col is not null && HasText(col.DefaultName))
            list.Add($"ALTER TABLE {target} DROP CONSTRAINT {SqlText.Bracket(col.DefaultName!)};");
        foreach (var ck in t.CheckConstraints.Where(c => string.Equals(c.ColumnName, column, StringComparison.OrdinalIgnoreCase)))
            list.Add($"ALTER TABLE {target} DROP CONSTRAINT {SqlText.Bracket(ck.Name)};");
        list.Add($"ALTER TABLE {target} DROP COLUMN {SqlText.Bracket(column)};");
        return list;
    }

    // ===== MS_Description =====

    private static string DescriptionArgs(string schema, string table, string column) =>
        $"@level0type = N'SCHEMA', @level0name = {SqlText.NLiteral(schema)}, " +
        $"@level1type = N'TABLE', @level1name = {SqlText.NLiteral(table)}, " +
        $"@level2type = N'COLUMN', @level2name = {SqlText.NLiteral(column)}";

    public static string AddDescription(string schema, string table, string column, string text) =>
        $"EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = {SqlText.NLiteral(text)}, {DescriptionArgs(schema, table, column)};";

    public static string UpdateDescription(string schema, string table, string column, string text) =>
        $"EXEC sys.sp_updateextendedproperty @name = N'MS_Description', @value = {SqlText.NLiteral(text)}, {DescriptionArgs(schema, table, column)};";

    public static string DropDescription(string schema, string table, string column) =>
        $"EXEC sys.sp_dropextendedproperty @name = N'MS_Description', {DescriptionArgs(schema, table, column)};";

    /// <summary>Statements as shown in the preview box.</summary>
    public static string ToScript(IEnumerable<string> statements)
    {
        var s = string.Join(Environment.NewLine + Environment.NewLine, statements);
        return s.Length == 0 ? "-- Nenhuma alteração." : s;
    }
}
