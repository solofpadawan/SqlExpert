using System.Globalization;
using System.Text;
using SqlExpert.Core.Models;

namespace SqlExpert.Core.Services;

/// <summary>
/// Generates (never executes) a script that rebuilds a table with its columns in a new order:
/// new table → copy data (IDENTITY_INSERT) → drop old → rename → recreate keys, defaults, checks,
/// foreign keys (both directions), indexes, triggers and descriptions. Single batch, one transaction.
/// </summary>
public static class TableRebuildScriptBuilder
{
    public static string TempTableName(string table)
    {
        var n = "SqlExpertTmp_" + table;
        return n.Length <= 128 ? n : n[..128];
    }

    public static string Build(TableStructure t, IReadOnlyList<string> newOrder, DateTime? generatedAt = null)
    {
        var cols = OrderColumns(t, newOrder);
        var target = SqlText.Qualified(t.Schema, t.Name);
        var tmpName = TempTableName(t.Name);
        var tmp = SqlText.Qualified(t.Schema, tmpName);
        var sb = new StringBuilder();
        void L(string line = "") => sb.AppendLine(line);

        L($"-- SqlExpert: reorder fields of {target}");
        L($"-- Generated {(generatedAt ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}. REVIEW BEFORE RUNNING - SqlExpert never runs this script.");
        L("-- New order: " + string.Join(", ", cols.Select(c => c.Name)));
        L("-- Notes:");
        L("--   * Take a backup first. The table is locked while the data is copied.");
        L("--   * Permissions (GRANT/DENY), statistics, schema-bound views/functions, replication/CDC");
        L("--     settings and table options (compression, filegroups, partitioning) are NOT recreated.");
        L("--   * Everything runs in one transaction with XACT_ABORT ON: any error rolls it all back.");
        foreach (var ix in t.Indexes.Where(i => i.Type is not (1 or 2)))
            L($"--   * WARNING: index {SqlText.Bracket(ix.Name)} ({ix.TypeDesc}) is not scripted and will be lost.");
        L();
        L("SET XACT_ABORT ON;");
        L("BEGIN TRANSACTION;");
        L();

        var incoming = t.ForeignKeys.Where(f => !f.IsOutgoing).ToList();
        if (incoming.Count > 0)
        {
            L("-- 1. Drop foreign keys of other tables that reference this table");
            foreach (var fk in incoming)
                L($"ALTER TABLE {SqlText.Qualified(fk.ParentSchema, fk.ParentTable)} DROP CONSTRAINT {SqlText.Bracket(fk.Name)};");
            L();
        }

        L("-- 2. New table with the columns in the new order");
        L($"CREATE TABLE {tmp}");
        L("(");
        for (var i = 0; i < cols.Count; i++)
            L("    " + CreateColumnLine(cols[i]) + (i < cols.Count - 1 ? "," : ""));
        L(");");
        L();

        var copyCols = cols.Where(c => !c.IsComputed && !c.IsRowVersion).ToList();
        var hasIdentity = cols.Any(c => c.IsIdentity);
        L("-- 3. Copy the data");
        if (copyCols.Count > 0)
        {
            var list = string.Join(", ", copyCols.Select(c => SqlText.Bracket(c.Name)));
            if (hasIdentity) L($"SET IDENTITY_INSERT {tmp} ON;");
            L($"INSERT INTO {tmp} ({list})");
            L($"    SELECT {list} FROM {target} WITH (TABLOCKX);");
            if (hasIdentity) L($"SET IDENTITY_INSERT {tmp} OFF;");
        }
        L();

        L("-- 4. Replace the old table");
        L($"DROP TABLE {target};");
        L($"EXEC sys.sp_rename {SqlText.NLiteral(tmp)}, {SqlText.NLiteral(t.Name)}, N'OBJECT';");
        if (hasIdentity && t.RowCount > 0 && t.IdentityCurrent is decimal cur)
            L($"DBCC CHECKIDENT ({SqlText.NLiteral(target)}, RESEED, {cur.ToString("0", CultureInfo.InvariantCulture)});");
        L();

        L("-- 5. Primary key / unique constraints and indexes (clustered first)");
        foreach (var k in t.KeyConstraints.OrderByDescending(k => k.IsClustered).ThenByDescending(k => k.IsPrimaryKey))
            L($"ALTER TABLE {target} ADD CONSTRAINT {SqlText.Bracket(k.Name)} {(k.IsPrimaryKey ? "PRIMARY KEY" : "UNIQUE")} {(k.IsClustered ? "CLUSTERED" : "NONCLUSTERED")} ({IndexCols(k.Columns)});");
        foreach (var ix in t.Indexes.Where(i => i.Type is 1 or 2).OrderByDescending(i => i.IsClustered))
        {
            var line = $"CREATE {(ix.IsUnique ? "UNIQUE " : "")}{(ix.IsClustered ? "CLUSTERED" : "NONCLUSTERED")} INDEX {SqlText.Bracket(ix.Name)} ON {target} ({IndexCols(ix.KeyColumns)})";
            if (ix.IncludedColumns.Count > 0)
                line += $" INCLUDE ({string.Join(", ", ix.IncludedColumns.Select(SqlText.Bracket))})";
            if (!string.IsNullOrWhiteSpace(ix.FilterDefinition))
                line += " WHERE " + ix.FilterDefinition;
            L(line + ";");
        }
        L();

        var defaults = cols.Where(c => !string.IsNullOrWhiteSpace(c.DefaultName) && !string.IsNullOrWhiteSpace(c.DefaultDefinition)).ToList();
        if (defaults.Count > 0 || t.CheckConstraints.Count > 0)
        {
            L("-- 6. Defaults and checks");
            foreach (var c in defaults)
                L($"ALTER TABLE {target} ADD CONSTRAINT {SqlText.Bracket(c.DefaultName!)} DEFAULT {c.DefaultDefinition} FOR {SqlText.Bracket(c.Name)};");
            foreach (var ck in t.CheckConstraints)
            {
                L($"ALTER TABLE {target} WITH {(ck.IsNotTrusted ? "NOCHECK" : "CHECK")} ADD CONSTRAINT {SqlText.Bracket(ck.Name)} CHECK {ck.Definition};");
                if (ck.IsDisabled) L($"ALTER TABLE {target} NOCHECK CONSTRAINT {SqlText.Bracket(ck.Name)};");
            }
            L();
        }

        if (t.ForeignKeys.Count > 0)
        {
            L("-- 7. Foreign keys (this table's and the ones dropped in step 1)");
            foreach (var fk in t.ForeignKeys.OrderByDescending(f => f.IsOutgoing))
            {
                var parent = SqlText.Qualified(fk.ParentSchema, fk.ParentTable);
                var line = $"ALTER TABLE {parent} WITH {(fk.IsNotTrusted ? "NOCHECK" : "CHECK")} ADD CONSTRAINT {SqlText.Bracket(fk.Name)} " +
                           $"FOREIGN KEY ({string.Join(", ", fk.Columns.Select(SqlText.Bracket))}) " +
                           $"REFERENCES {SqlText.Qualified(fk.ReferencedSchema, fk.ReferencedTable)} ({string.Join(", ", fk.ReferencedColumns.Select(SqlText.Bracket))})";
                if (!IsNoAction(fk.DeleteAction)) line += " ON DELETE " + fk.DeleteAction.Replace('_', ' ');
                if (!IsNoAction(fk.UpdateAction)) line += " ON UPDATE " + fk.UpdateAction.Replace('_', ' ');
                L(line + ";");
                if (fk.IsDisabled) L($"ALTER TABLE {parent} NOCHECK CONSTRAINT {SqlText.Bracket(fk.Name)};");
            }
            L();
        }

        if (t.Triggers.Count > 0)
        {
            L("-- 8. Triggers");
            foreach (var tr in t.Triggers)
            {
                if (string.IsNullOrWhiteSpace(tr.Definition))
                {
                    L($"-- WARNING: trigger {SqlText.Bracket(tr.Name)} has no readable definition (encrypted?) and is not recreated.");
                    continue;
                }
                L($"EXEC sys.sp_executesql {SqlText.NLiteral(tr.Definition!.Trim())};");
                if (tr.IsDisabled) L($"DISABLE TRIGGER {SqlText.Qualified(t.Schema, tr.Name)} ON {target};");
            }
            L();
        }

        var described = cols.Where(c => !string.IsNullOrEmpty(c.Description)).ToList();
        if (!string.IsNullOrEmpty(t.Description) || described.Count > 0)
        {
            L("-- 9. Descriptions (MS_Description)");
            if (!string.IsNullOrEmpty(t.Description))
                L($"EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = {SqlText.NLiteral(t.Description!)}, @level0type = N'SCHEMA', @level0name = {SqlText.NLiteral(t.Schema)}, @level1type = N'TABLE', @level1name = {SqlText.NLiteral(t.Name)};");
            foreach (var c in described)
                L(FieldDdlBuilder.AddDescription(t.Schema, t.Name, c.Name, c.Description!));
            L();
        }

        L("COMMIT TRANSACTION;");
        return sb.ToString();
    }

    /// <summary>Columns in the requested order; names missing from <paramref name="newOrder"/> keep their relative order at the end.</summary>
    public static List<ColumnStructure> OrderColumns(TableStructure t, IReadOnlyList<string> newOrder)
    {
        var result = new List<ColumnStructure>();
        foreach (var name in newOrder)
        {
            var c = t.FindColumn(name);
            if (c is not null && !result.Contains(c)) result.Add(c);
        }
        result.AddRange(t.Columns.OrderBy(c => c.Ordinal).Where(c => !result.Contains(c)));
        return result;
    }

    private static string CreateColumnLine(ColumnStructure c)
    {
        if (c.IsComputed)
            return $"{SqlText.Bracket(c.Name)} AS {c.ComputedDefinition}{(c.IsPersisted ? " PERSISTED" : "")}";
        var f = FieldDefinition.FromColumn(c);
        if (c.IsRowVersion)
            return $"{SqlText.Bracket(c.Name)} rowversion NOT NULL";
        return FieldDdlBuilder.ColumnDefinition(f, includeIdentity: true);
    }

    private static string IndexCols(IEnumerable<IndexColumnRef> cols) =>
        string.Join(", ", cols.Select(c => SqlText.Bracket(c.Name) + (c.IsDescending ? " DESC" : " ASC")));

    private static bool IsNoAction(string a) => string.IsNullOrWhiteSpace(a) || a.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase);
}
