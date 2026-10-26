using System.Globalization;
using SqlExpert.Core.DataForms;
using SqlExpert.Core.Models;

namespace SqlExpert.Tests;

/// <summary>
/// Data grid context menu: "Copy records / Copy selected record(s)" (tab-separated text) and "Copy selected record(s)
/// as INSERT / as UPDATE" (T-SQL with literal values). No database, no GUI.
/// </summary>
public class RecordCopyTextTests
{
    [Theory]
    [InlineData("nvarchar", "N'Zé'")]
    [InlineData("nchar", "N'Zé'")]
    [InlineData("ntext", "N'Zé'")]
    [InlineData("sysname", "N'Zé'")]
    [InlineData("xml", "N'Zé'")]
    [InlineData("sql_variant", "N'Zé'")]
    [InlineData("NVARCHAR(50)", "N'Zé'")]
    [InlineData("", "N'Zé'")]
    [InlineData("varchar", "'Zé'")]
    [InlineData("char", "'Zé'")]
    [InlineData("text", "'Zé'")]
    [InlineData("VarChar(20)", "'Zé'")]
    [InlineData("date", "'Zé'")]
    [InlineData("uniqueidentifier", "'Zé'")]
    [InlineData("hierarchyid", "'Zé'")]
    public void Literal_string_prefix_follows_column_type(string type, string expected)
    {
        Assert.Equal(expected, RecordCopyText.Literal("Zé", Col("x", type)));
    }

    [Fact]
    public void Literal_unknown_column_keeps_N_prefix_and_alias_uses_base_type()
    {
        Assert.Equal("N'a''b'", RecordCopyText.Literal("a'b"));
        Assert.Equal("'a''b'", RecordCopyText.Literal("a'b", new DataColumnInfo { Name = "x", TypeName = "CodigoTipo", BaseTypeName = "varchar" }));
        Assert.Equal("N'a'", RecordCopyText.Literal("a", new DataColumnInfo { Name = "x", TypeName = "NomeTipo", BaseTypeName = "nvarchar" }));
        Assert.Equal("'x'", RecordCopyText.Literal('x', Col("c", "char")));
        Assert.Equal("'d7a1c5e2-0000-0000-0000-000000000001'",
            RecordCopyText.Literal(Guid.Parse("d7a1c5e2-0000-0000-0000-000000000001"), Col("g", "nvarchar")));
    }

    [Fact]
    public void Update_varchar_columns_use_plain_quotes_in_set_and_where()
    {
        var cols = new[] { Col("Codigo", "varchar", pk: true, keyOrd: 1), Col("Descricao", "varchar"), Col("Nome", "nvarchar") };
        var rec = new CopyRecord(new object?[] { "A1", "Caixa d'água", "Zé" });
        var sql = RecordCopyText.BuildUpdates("dbo", "Produto", cols, new[] { rec });
        Assert.Equal("UPDATE [dbo].[Produto] SET [Descricao] = 'Caixa d''água', [Nome] = N'Zé' WHERE [Codigo] = 'A1';\r\n", sql);
    }

    [Fact]
    public void Update_without_key_compares_varchar_with_plain_quotes()
    {
        var cols = new[] { Col("A", "varchar"), Col("B", "nvarchar") };
        var rec = new CopyRecord(new object?[] { "novo", "b" }, new object?[] { "velho", "b" });
        var sql = RecordCopyText.BuildUpdates("dbo", "T", cols, new[] { rec });
        Assert.Contains("SET [A] = 'novo', [B] = N'b' WHERE [A] = 'velho' AND [B] = N'b';", sql);
    }

    [Fact]
    public void Insert_mixed_string_columns()
    {
        var cols = new[] { Col("Id", "int", pk: true, keyOrd: 1), Col("Sigla", "char"), Col("Nome", "nvarchar"), Col("Obs", "text") };
        var sql = RecordCopyText.BuildInserts("dbo", "T", cols, new[] { new CopyRecord(new object?[] { 1, "SP", "São Paulo", "o'k" }) });
        Assert.Equal("INSERT INTO [dbo].[T] ([Id], [Sigla], [Nome], [Obs]) VALUES (1, 'SP', N'São Paulo', 'o''k');\r\n", sql);
    }

    private static DataColumnInfo Col(string name, string type, bool pk = false, int keyOrd = 0, bool identity = false,
        bool computed = false, bool rowVersion = false, bool clr = false, int scale = 0) =>
        new() { Name = name, TypeName = type, BaseTypeName = type, IsPrimaryKey = pk, KeyOrdinal = keyOrd, IsIdentity = identity, IsComputed = computed, IsRowVersion = rowVersion, IsClrType = clr, Scale = scale };

    private static CopyRecord Rec(params object?[] values) => new(values);

    // ===== literals =====

    [Fact]
    public void Literal_Null_And_DbNull()
    {
        Assert.Equal("NULL", RecordCopyText.Literal(null));
        Assert.Equal("NULL", RecordCopyText.Literal(DBNull.Value));
    }

    [Fact]
    public void Literal_String_IsUnicode_WithQuotesDoubled()
    {
        Assert.Equal("N'abc'", RecordCopyText.Literal("abc"));
        Assert.Equal("N'O''Brien ''x'''", RecordCopyText.Literal("O'Brien 'x'"));
        Assert.Equal("N''", RecordCopyText.Literal(""));
        Assert.Equal("N'ação'", RecordCopyText.Literal("ação"));
        Assert.Equal("N'a\r\nb'", RecordCopyText.Literal("a\r\nb"));
    }

    [Fact]
    public void Literal_Numbers_UseInvariantCulture_EvenUnderPtBr()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            Assert.Equal("1234.5", RecordCopyText.Literal(1234.5m));
            Assert.Equal("-0.001", RecordCopyText.Literal(-0.001m));
            Assert.Equal("1.25", RecordCopyText.Literal(1.25d));
            Assert.Equal("0.5", RecordCopyText.Literal(0.5f));
            Assert.Equal("42", RecordCopyText.Literal(42));
            Assert.Equal("-9000000000", RecordCopyText.Literal(-9000000000L));
            Assert.Equal("7", RecordCopyText.Literal((short)7));
            Assert.Equal("255", RecordCopyText.Literal((byte)255));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Literal_NonFiniteFloat_IsNull()
    {
        Assert.Equal("NULL", RecordCopyText.Literal(double.NaN));
        Assert.Equal("NULL", RecordCopyText.Literal(float.PositiveInfinity));
    }

    [Fact]
    public void Literal_Bit_Binary_Guid()
    {
        Assert.Equal("1", RecordCopyText.Literal(true));
        Assert.Equal("0", RecordCopyText.Literal(false));
        Assert.Equal("0x00FF10", RecordCopyText.Literal(new byte[] { 0x00, 0xFF, 0x10 }));
        Assert.Equal("0x", RecordCopyText.Literal(Array.Empty<byte>()));
        var big = Enumerable.Repeat((byte)0xAB, 100).ToArray();
        Assert.Equal("0x" + string.Concat(Enumerable.Repeat("AB", 100)), RecordCopyText.Literal(big)); // never cut
        var g = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        Assert.Equal("'6f9619ff-8b86-d011-b42d-00c04fc964ff'", RecordCopyText.Literal(g));
    }

    [Fact]
    public void Literal_Dates_AreIso8601_ByColumnType()
    {
        var d = new DateTime(2026, 10, 26, 14, 5, 9, 123).AddTicks(4567);
        Assert.Equal("'2026-10-26T14:05:09.123'", RecordCopyText.Literal(d));
        Assert.Equal("'2026-10-26T14:05:09.123'", RecordCopyText.Literal(d, Col("x", "datetime")));
        Assert.Equal("'2026-10-26T14:05:09.123'", RecordCopyText.Literal(d, Col("x", "smalldatetime")));
        Assert.Equal("'2026-10-26T14:05:09.1234567'", RecordCopyText.Literal(d, Col("x", "datetime2")));
        Assert.Equal("'2026-10-26'", RecordCopyText.Literal(d.Date, Col("x", "date")));
        var o = new DateTimeOffset(2026, 10, 26, 14, 5, 9, TimeSpan.FromHours(-3));
        Assert.Equal("'2026-10-26T14:05:09.0000000-03:00'", RecordCopyText.Literal(o));
        Assert.Equal("'08:30:00.0000000'", RecordCopyText.Literal(new TimeSpan(8, 30, 0)));
    }

    [Fact]
    public void Literal_Dates_IgnoreCurrentCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            Assert.Equal("'2026-01-02T03:04:05.000'", RecordCopyText.Literal(new DateTime(2026, 1, 2, 3, 4, 5)));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    // ===== INSERT =====

    [Fact]
    public void Insert_OneLinePerRecord_BracketedNames()
    {
        var cols = new[] { Col("Id", "int", pk: true, keyOrd: 1), Col("Nome", "nvarchar"), Col("Valor", "decimal") };
        var sql = RecordCopyText.BuildInserts("dbo", "Cliente", cols, new[] { Rec(1, "Ana", 10.5m), Rec(2, null, DBNull.Value) });
        Assert.Equal(
            "INSERT INTO [dbo].[Cliente] ([Id], [Nome], [Valor]) VALUES (1, N'Ana', 10.5);\r\n" +
            "INSERT INTO [dbo].[Cliente] ([Id], [Nome], [Valor]) VALUES (2, NULL, NULL);\r\n", sql);
    }

    [Fact]
    public void Insert_SkipsComputedAndRowVersion()
    {
        var cols = new[] { Col("Id", "int", pk: true, keyOrd: 1), Col("Total", "decimal", computed: true), Col("Ver", "timestamp", rowVersion: true), Col("Obs", "varchar") };
        var sql = RecordCopyText.BuildInserts("dbo", "T", cols, new[] { Rec(5, 99m, new byte[] { 1 }, "x") });
        Assert.Equal("INSERT INTO [dbo].[T] ([Id], [Obs]) VALUES (5, 'x');\r\n", sql);
    }

    [Fact]
    public void Insert_IdentityColumn_IsKept_AndWrappedInIdentityInsert()
    {
        var cols = new[] { Col("Id", "int", pk: true, keyOrd: 1, identity: true), Col("Nome", "nvarchar") };
        var sql = RecordCopyText.BuildInserts("vendas", "Pedido", cols, new[] { Rec(7, "a"), Rec(8, "b") });
        Assert.Equal(
            "SET IDENTITY_INSERT [vendas].[Pedido] ON;\r\n" +
            "INSERT INTO [vendas].[Pedido] ([Id], [Nome]) VALUES (7, N'a');\r\n" +
            "INSERT INTO [vendas].[Pedido] ([Id], [Nome]) VALUES (8, N'b');\r\n" +
            "SET IDENTITY_INSERT [vendas].[Pedido] OFF;\r\n", sql);
    }

    [Fact]
    public void Insert_EscapesClosingBracketsInNames()
    {
        var cols = new[] { Col("a]b", "int") };
        var sql = RecordCopyText.BuildInserts("s]1", "t]2", cols, new[] { Rec(1) });
        Assert.Equal("INSERT INTO [s]]1].[t]]2] ([a]]b]) VALUES (1);\r\n", sql);
    }

    [Fact]
    public void Insert_NoRecords_IsEmpty()
    {
        Assert.Equal("", RecordCopyText.BuildInserts("dbo", "T", new[] { Col("A", "int") }, Array.Empty<CopyRecord>()));
    }

    // ===== UPDATE =====

    [Fact]
    public void Update_WhereUsesPrimaryKey_AndSetExcludesIt()
    {
        var cols = new[] { Col("Id", "int", pk: true, keyOrd: 1), Col("Nome", "nvarchar"), Col("Ativo", "bit") };
        var sql = RecordCopyText.BuildUpdates("dbo", "Cliente", cols, new[] { Rec(3, "Zé d'Água", true) });
        Assert.Equal("UPDATE [dbo].[Cliente] SET [Nome] = N'Zé d''Água', [Ativo] = 1 WHERE [Id] = 3;\r\n", sql);
    }

    [Fact]
    public void Update_CompositeKey_InKeyOrder()
    {
        var cols = new[] { Col("Item", "int", pk: true, keyOrd: 2), Col("Pedido", "int", pk: true, keyOrd: 1), Col("Qtd", "int") };
        var sql = RecordCopyText.BuildUpdates("dbo", "Item", cols, new[] { Rec(2, 10, 5) });
        Assert.Equal("UPDATE [dbo].[Item] SET [Qtd] = 5 WHERE [Pedido] = 10 AND [Item] = 2;\r\n", sql);
    }

    [Fact]
    public void Update_SkipsIdentityComputedRowVersionInSet()
    {
        var cols = new[] { Col("Id", "int", pk: true, keyOrd: 1, identity: true), Col("Seq", "int", identity: false), Col("Calc", "int", computed: true), Col("Ver", "rowversion", rowVersion: true) };
        var sql = RecordCopyText.BuildUpdates("dbo", "T", cols, new[] { Rec(1, 2, 3, new byte[] { 1 }) });
        Assert.Equal("UPDATE [dbo].[T] SET [Seq] = 2 WHERE [Id] = 1;\r\n", sql);
    }

    [Fact]
    public void Update_PendingEdit_WhereUsesOriginalKey()
    {
        var cols = new[] { Col("Codigo", "varchar", pk: true, keyOrd: 1), Col("Nome", "nvarchar") };
        var rec = new CopyRecord(new object?[] { "NOVO", "x" }, new object?[] { "VELHO", "y" });
        var sql = RecordCopyText.BuildUpdates("dbo", "T", cols, new[] { rec });
        Assert.Equal("UPDATE [dbo].[T] SET [Nome] = N'x' WHERE [Codigo] = 'VELHO';\r\n", sql);
    }

    [Fact]
    public void Update_NoPrimaryKey_WhereComparesEveryNonBlobColumn_NullAsIsNull()
    {
        var cols = new[] { Col("A", "int"), Col("B", "nvarchar"), Col("Foto", "varbinary"), Col("Memo", "ntext"), Col("X", "xml"), Col("C", "datetime") };
        var sql = RecordCopyText.BuildUpdates("dbo", "Log", cols,
            new[] { Rec(1, null, new byte[] { 1 }, "m", "<a/>", new DateTime(2026, 1, 1)) });
        Assert.Equal(
            "-- [dbo].[Log]: no primary key, WHERE compares every column.\r\n" +
            "UPDATE [dbo].[Log] SET [A] = 1, [B] = NULL, [Foto] = 0x01, [Memo] = N'm', [X] = N'<a/>', [C] = '2026-01-01T00:00:00.000' " +
            "WHERE [A] = 1 AND [B] IS NULL AND [C] = '2026-01-01T00:00:00.000';\r\n", sql);
    }

    [Fact]
    public void Update_EveryColumnInKey_GivesComment()
    {
        var cols = new[] { Col("A", "int", pk: true, keyOrd: 1), Col("B", "int", pk: true, keyOrd: 2) };
        var sql = RecordCopyText.BuildUpdates("dbo", "Link", cols, new[] { Rec(1, 2) });
        Assert.StartsWith("-- [dbo].[Link]: nothing to update", sql);
        Assert.DoesNotContain("UPDATE", sql);
    }

    [Fact]
    public void IsComparable_ExcludesBlobXmlSpatial()
    {
        Assert.True(RecordCopyText.IsComparable(Col("a", "int")));
        Assert.True(RecordCopyText.IsComparable(Col("a", "nvarchar")));
        Assert.True(RecordCopyText.IsComparable(Col("a", "hierarchyid", clr: true)));
        Assert.False(RecordCopyText.IsComparable(Col("a", "varbinary")));
        Assert.False(RecordCopyText.IsComparable(Col("a", "image")));
        Assert.False(RecordCopyText.IsComparable(Col("a", "text")));
        Assert.False(RecordCopyText.IsComparable(Col("a", "xml")));
        Assert.False(RecordCopyText.IsComparable(Col("a", "geography", clr: true)));
    }

    // ===== tab-separated =====

    [Fact]
    public void TabSeparated_HeaderAndRows_CrLfBetweenLines_NoTrailingNewline()
    {
        var text = RecordCopyText.ToTabSeparated(new[] { "Id", "Nome" }, new[] { new string?[] { "1", "Ana" }, new string?[] { "2", null } });
        Assert.Equal("Id\tNome\r\n1\tAna\r\n2\t", text);
        Assert.Equal("Id\tNome", RecordCopyText.ToTabSeparated(new[] { "Id", "Nome" }, Array.Empty<string?[]>()));
    }

    [Fact]
    public void TabSeparated_FieldsAsIs_TabsAndLineBreaksBecomeSpaces()
    {
        Assert.Equal("plain", RecordCopyText.TabField("plain"));
        Assert.Equal("a b", RecordCopyText.TabField("a\tb"));
        Assert.Equal("l1 l2", RecordCopyText.TabField("l1\r\nl2"));
        Assert.Equal("l1 l2 l3", RecordCopyText.TabField("l1\nl2\rl3"));
        Assert.Equal("say \"hi\"", RecordCopyText.TabField("say \"hi\""));
        Assert.Equal("", RecordCopyText.TabField(null));
    }

    /// <summary>Header, raw values, TAB, CRLF, no trailing newline; the regional settings do not matter.</summary>
    [Fact]
    public void CopyRecords_SampleGrid_ExactText()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        try { CopyRecordsSample(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void CopyRecordsSample()
    {
        var cols = new[] { Col("BC_ID", "int"), Col("BC_FK_EMPRESA", "int"), Col("BC_CONTA", "varchar"), Col("BC_DESCRICAO", "varchar") };
        var descriptions = new[]
        {
            "EDIFICAÇÕES SP", "EDIFICAÇÕES CENTRO", "INSTALAÇÕES ELÉTRICAS", "MÁQUINAS E EQUIPAMENTOS", "MÓVEIS E UTENSÍLIOS",
            "VEÍCULOS", "BENFEITORIAS EM IMÓVEIS", "COMPUTADORES E PERIFÉRICOS", "APARELHOS DE AR-CONDICIONADO", "EQUIPAMENTOS DE COMUNICAÇÃO"
        };
        var rows = Enumerable.Range(0, 10).Select(i => new object?[] { 88189 + i, 1086, $"1.02.04.01.{40 + i:00000}", descriptions[i] });

        var text = RecordCopyText.ToTabSeparated(
            cols.Select(c => c.Name).ToList(),
            rows.Select(r => (IReadOnlyList<string?>)r.Select((v, i) => RecordCopyText.RecordFieldText(v, cols[i])).ToList()));

        var expected = string.Join("\r\n",
            "BC_ID\tBC_FK_EMPRESA\tBC_CONTA\tBC_DESCRICAO",
            "88189\t1086\t1.02.04.01.00040\tEDIFICAÇÕES SP",
            "88190\t1086\t1.02.04.01.00041\tEDIFICAÇÕES CENTRO",
            "88191\t1086\t1.02.04.01.00042\tINSTALAÇÕES ELÉTRICAS",
            "88192\t1086\t1.02.04.01.00043\tMÁQUINAS E EQUIPAMENTOS",
            "88193\t1086\t1.02.04.01.00044\tMÓVEIS E UTENSÍLIOS",
            "88194\t1086\t1.02.04.01.00045\tVEÍCULOS",
            "88195\t1086\t1.02.04.01.00046\tBENFEITORIAS EM IMÓVEIS",
            "88196\t1086\t1.02.04.01.00047\tCOMPUTADORES E PERIFÉRICOS",
            "88197\t1086\t1.02.04.01.00048\tAPARELHOS DE AR-CONDICIONADO",
            "88198\t1086\t1.02.04.01.00049\tEQUIPAMENTOS DE COMUNICAÇÃO");
        Assert.Equal(expected, text);
        Assert.False(text.EndsWith("\n", StringComparison.Ordinal));
    }

    [Fact]
    public void RecordFieldText_Numbers_InvariantDot_NoGrouping_BitAsDigit()
    {
        Assert.Equal("88189", RecordCopyText.RecordFieldText(88189, Col("a", "int")));
        Assert.Equal("-1234567890123", RecordCopyText.RecordFieldText(-1234567890123L, Col("a", "bigint")));
        Assert.Equal("255", RecordCopyText.RecordFieldText((byte)255, Col("a", "tinyint")));
        Assert.Equal("1234567.89", RecordCopyText.RecordFieldText(1234567.89m, Col("a", "decimal")));
        Assert.Equal("10.5000", RecordCopyText.RecordFieldText(10.5000m, Col("a", "money")));
        Assert.Equal("-0.5", RecordCopyText.RecordFieldText(-0.5m, Col("a", "decimal")));
        Assert.Equal("1234567.25", RecordCopyText.RecordFieldText(1234567.25d, Col("a", "float")));
        Assert.Equal("0.1", RecordCopyText.RecordFieldText(0.1f, Col("a", "real")));
        Assert.Equal("1", RecordCopyText.RecordFieldText(true, Col("a", "bit")));
        Assert.Equal("0", RecordCopyText.RecordFieldText(false, Col("a", "bit")));
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        try { Assert.Equal("1234567.89", RecordCopyText.RecordFieldText(1234567.89m, Col("a", "decimal"))); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void RecordFieldText_DatesAsSqlServer()
    {
        var d = new DateTime(2026, 10, 26, 14, 30, 0);
        var frac = d.AddTicks(1234567);
        Assert.Equal("2026-10-26", RecordCopyText.RecordFieldText(d.Date, Col("a", "date")));
        Assert.Equal("2026-10-26 14:30:00", RecordCopyText.RecordFieldText(d, Col("a", "smalldatetime")));
        Assert.Equal("2026-10-26 14:30:00.000", RecordCopyText.RecordFieldText(d, Col("a", "datetime")));
        Assert.Equal("2026-10-26 00:00:00.000", RecordCopyText.RecordFieldText(d.Date, Col("a", "datetime")));
        Assert.Equal("2026-10-26 14:30:00.123", RecordCopyText.RecordFieldText(d.AddMilliseconds(123), Col("a", "datetime")));
        Assert.Equal("2026-10-26 14:30:00.0000000", RecordCopyText.RecordFieldText(d, Col("a", "datetime2", scale: 7)));
        Assert.Equal("2026-10-26 14:30:00.1234567", RecordCopyText.RecordFieldText(frac, Col("a", "datetime2", scale: 7)));
        Assert.Equal("2026-10-26 14:30:00.123", RecordCopyText.RecordFieldText(frac, Col("a", "datetime2", scale: 3)));
        Assert.Equal("2026-10-26 14:30:00", RecordCopyText.RecordFieldText(d, Col("a", "datetime2", scale: 0)));
        Assert.Equal("2026-10-26 14:30:00.0000000 -03:00",
            RecordCopyText.RecordFieldText(new DateTimeOffset(d, TimeSpan.FromHours(-3)), Col("a", "datetimeoffset", scale: 7)));
        Assert.Equal("2026-10-26 14:30:00.1234567 +00:00",
            RecordCopyText.RecordFieldText(new DateTimeOffset(frac, TimeSpan.Zero), Col("a", "datetimeoffset", scale: 7)));
        Assert.Equal("14:30:00.0000000", RecordCopyText.RecordFieldText(new TimeSpan(14, 30, 0), Col("a", "time", scale: 7)));
        Assert.Equal("14:30:00.12", RecordCopyText.RecordFieldText(new TimeSpan(14, 30, 0).Add(TimeSpan.FromTicks(1234567)), Col("a", "time", scale: 2)));
        // no column metadata: datetime style; datetimeoffset / time with 7 digits
        Assert.Equal("2026-10-26 14:30:00.000", RecordCopyText.RecordFieldText(d, null));
        Assert.Equal("14:30:00.0000000", RecordCopyText.RecordFieldText(new TimeSpan(14, 30, 0), null));
        // the regional settings do not change the text
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        try { Assert.Equal("2026-10-26 14:30:00.000", RecordCopyText.RecordFieldText(d, Col("a", "datetime"))); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void RecordFieldText_NullAsNULL_TextAsIs_GuidUpper_Binary()
    {
        Assert.Equal("NULL", RecordCopyText.RecordFieldText(DBNull.Value, Col("a", "int")));
        Assert.Equal("NULL", RecordCopyText.RecordFieldText(null, null));
        Assert.Equal("", RecordCopyText.RecordFieldText("", Col("a", "varchar")));
        Assert.Equal("AÇÃO \"x\" 1.234", RecordCopyText.RecordFieldText("AÇÃO \"x\" 1.234", Col("a", "nvarchar")));
        Assert.Equal("0x0102FF", RecordCopyText.RecordFieldText(new byte[] { 1, 2, 255 }, Col("a", "varbinary")));
        var g = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        Assert.Equal("0F8FAD5B-D9CB-469F-A165-70867728950E", RecordCopyText.RecordFieldText(g, Col("a", "uniqueidentifier")));
        // embedded TAB / line breaks are flattened by the TSV writer
        var line = RecordCopyText.ToTabSeparated(new[] { "a", "b", "c" },
            new[] { new string?[] { RecordCopyText.RecordFieldText("l1\r\nl2\tx", Col("a", "varchar")), "", RecordCopyText.RecordFieldText(DBNull.Value, null) } });
        Assert.Equal("a\tb\tc\r\nl1 l2 x\t\tNULL", line);
    }

    [Fact]
    public void ValueText_NullEmpty_BinaryFullHex_DatesByKind()
    {
        var inv = CultureInfo.InvariantCulture;
        Assert.Equal("", RecordCopyText.ValueText(DBNull.Value, null, inv));
        Assert.Equal("0x" + string.Concat(Enumerable.Repeat("01", 40)), RecordCopyText.ValueText(Enumerable.Repeat((byte)1, 40).ToArray(), null, inv));
        Assert.Equal("42", RecordCopyText.ValueText(42, null, inv));
        // copies what the grid shows (ISO dates, the column scale)
        Assert.Equal("2026-10-26", RecordCopyText.ValueText(new DateTime(2026, 10, 26), Col("d", "date"), inv));
        Assert.Equal("2037-10-24 11:42:51.000", RecordCopyText.ValueText(new DateTime(2037, 10, 24, 11, 42, 51), Col("d", "datetime"), inv));
        Assert.Equal("2026-10-26 14:30:00.123", RecordCopyText.ValueText(new DateTime(2026, 10, 26, 14, 30, 0).AddTicks(1234567), Col("d", "datetime2", scale: 3), inv));
        Assert.Equal("14:30:00.12", RecordCopyText.ValueText(new TimeSpan(14, 30, 0).Add(TimeSpan.FromTicks(1234567)), Col("t", "time", scale: 2), inv));
        Assert.Equal(RecordCopyText.RecordFieldText(new DateTime(2026, 10, 26, 14, 30, 0), Col("d", "smalldatetime")),
            RecordCopyText.ValueText(new DateTime(2026, 10, 26, 14, 30, 0), Col("d", "smalldatetime"), inv));
    }

    // ===== Set as NOW =====

    [Fact]
    public void NowValue_ByColumnType()
    {
        var now = new DateTimeOffset(new DateTime(2026, 10, 26, 9, 48, 37, 123).AddTicks(4567), TimeSpan.FromHours(-3));
        Assert.Equal(new DateTime(2026, 10, 26), RecordCopyText.NowValue(Col("d", "date"), typeof(DateTime), now));
        Assert.Equal(new DateTime(2026, 10, 26, 9, 48, 0), RecordCopyText.NowValue(Col("d", "smalldatetime"), typeof(DateTime), now));
        Assert.Equal(new DateTime(2026, 10, 26, 9, 48, 37, 123), RecordCopyText.NowValue(Col("d", "datetime"), typeof(DateTime), now));
        Assert.Equal(now.DateTime, RecordCopyText.NowValue(Col("d", "datetime2"), typeof(DateTime), now));
        Assert.Equal(now, RecordCopyText.NowValue(Col("d", "datetimeoffset"), typeof(DateTimeOffset), now));
        Assert.Equal(now.DateTime.TimeOfDay, RecordCopyText.NowValue(Col("t", "time"), typeof(TimeSpan), now));
        Assert.Null(RecordCopyText.NowValue(Col("n", "int"), typeof(int), now));
        Assert.Null(RecordCopyText.NowValue(Col("s", "nvarchar"), typeof(string), now));
    }
}

/// <summary>Data grid context menu layout: order, separators, implemented items, icons.</summary>
public class GridContextMenuLayoutTests
{
    private static string AppFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName, "SqlExpert.App" }.Concat(parts).ToArray());
    }

    [Fact]
    public void Captions_InClassicOrder()
    {
        var captions = GridContextMenuLayout.Entries.Select(e => e.Caption ?? "-").ToArray();
        Assert.Equal(new[]
        {
            "Cut cell value", "Copy cell value", "Paste cell value", "-",
            "Incremental Search", "-",
            "Adjust column widths", "-",
            "Set NULL", "Set empty string", "Set as NOW", "-",
            "Copy records to clipboard", "Copy selected record(s) to clipboard", "Copy selected record(s) as INSERT", "Copy selected record(s) as UPDATE", "-",
            "Duplicate record", "-",
            "Reset fields order", "Reorder grid columns", "-",
            "Group fields", "-",
            "Apply Filter", "Show Filter Panel", "Quick Add Filter Criteria"
        }, captions);
    }

    [Fact]
    public void CopyItems_AndCellActions_AreImplemented_RestDisabled()
    {
        var implemented = GridContextMenuLayout.Entries.Where(e => e.IsImplemented).Select(e => e.Id).ToHashSet();
        foreach (var id in new[] { "CopyAll", "CopySelected", "CopyInsert", "CopyUpdate", "CopyCell", "SetNull", "SetEmpty", "SetNow", "AdjustWidths" })
            Assert.Contains(id, implemented);
        foreach (var id in new[] { "IncrementalSearch", "Duplicate", "ReorderColumns", "GroupFields", "ApplyFilter", "FilterPanel", "QuickFilter" })
            Assert.DoesNotContain(id, implemented);
    }

    [Fact]
    public void Ids_AreUnique_AndShortcutsShown()
    {
        var ids = GridContextMenuLayout.Entries.Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        var byId = GridContextMenuLayout.Entries.ToDictionary(e => e.Id);
        Assert.Equal("Ctrl+F", byId["IncrementalSearch"].Shortcut);
        Assert.Equal("Ctrl+NUMMULT", byId["AdjustWidths"].Shortcut);
        Assert.Equal("Ctrl+Alt+F", byId["FilterPanel"].Shortcut);
    }

    [Fact]
    public void EveryIcon_ExistsInIconsXaml()
    {
        var icons = File.ReadAllText(AppFile("Themes", "Icons.xaml"));
        foreach (var e in GridContextMenuLayout.Entries.Where(e => e.IconKey.Length > 0))
            Assert.Contains($"x:Key=\"Icon.{e.IconKey}\"", icons);
    }

    [Fact]
    public void Grid_UsesTheContextMenu_AndExtendedSelection()
    {
        var xaml = File.ReadAllText(AppFile("Views", "TableDataGrid.xaml"));
        Assert.Contains("SelectionMode=\"Extended\"", xaml);
        var code = File.ReadAllText(AppFile("Views", "TableDataGrid.ContextMenu.cs"));
        Assert.Contains("GridContextMenuLayout.Entries", code);
        Assert.Contains("RecordCopyText.BuildInserts", code);
        Assert.Contains("RecordCopyText.BuildUpdates", code);
        Assert.Contains("ainda não implementado", code);
    }
}
