using System.Globalization;
using SqlExpert.Core.DataForms;
using SqlExpert.Core.Models;
using static SqlExpert.Core.DataForms.CalcKey;

namespace SqlExpert.Tests;

public class CalculatorEngineTests
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static CalculatorEngine Calc(decimal? initial = null) => new(PtBr, initial);

    private static CalculatorEngine Keys(string keys, decimal? initial = null)
    {
        var c = Calc(initial);
        foreach (var ch in keys)
        {
            var k = CalculatorEngine.MapChar(ch);
            Assert.True(k.HasValue, $"no key for '{ch}'");
            c.Press(k!.Value);
        }
        return c;
    }

    [Fact]
    public void StartsAtZero_OrAtTheFieldValue()
    {
        Assert.Equal("0", Calc().Display);
        Assert.Equal("1234,5", Calc(1234.50m).Display);
        Assert.Equal(1234.5m, Calc(1234.50m).Value);
    }

    [Fact]
    public void Typing_UsesThePtBrComma_AndKeepsWhatWasTyped()
    {
        var c = Keys("12,50");
        Assert.Equal("12,50", c.Display);
        Assert.Equal(12.5m, c.Value);
        Assert.Equal("0,5", Keys(".5").Display); // '.' is also the decimal key
        Assert.Equal("3,25", Keys("3,2,5").Display); // a second separator is ignored
    }

    [Fact]
    public void TypingAfterAFieldValue_ReplacesIt()
    {
        var c = Calc(99m);
        c.Press(D7);
        Assert.Equal("7", c.Display);
    }

    [Theory]
    [InlineData("2+3=", 5)]
    [InlineData("10-4=", 6)]
    [InlineData("6*7=", 42)]
    [InlineData("1/4=", 0.25)]
    [InlineData("2+3*4=", 20)] // immediate execution, classic calculator
    [InlineData("0,1+0,2=", 0.3)] // decimal: no binary floating-point error
    [InlineData("5+=", 10)] // missing operand repeats the shown value
    [InlineData("8-+2=", 10)] // second operator replaces the first
    public void BasicArithmetic(string keys, double expected)
    {
        var c = Keys(keys);
        Assert.False(c.HasError);
        Assert.Equal((decimal)expected, c.Value);
    }

    [Fact]
    public void Chaining_ShowsIntermediateResults()
    {
        var c = Keys("2+3+");
        Assert.Equal("5", c.Display);
        Assert.Equal("5 +", c.PendingText);
        c.Press(D1, CalcKey.Equals);
        Assert.Equal(6m, c.Value);
        Assert.Equal("", c.PendingText);
    }

    [Fact]
    public void Back_CE_C()
    {
        var c = Keys("1234");
        c.Press(Back);
        Assert.Equal("123", c.Display);
        c.Press(Back, Back, Back, Back);
        Assert.Equal("0", c.Display);

        c = Keys("7+8");
        c.Press(ClearEntry);
        Assert.Equal("0", c.Display);
        c.Press(D2, CalcKey.Equals);
        Assert.Equal(9m, c.Value); // CE keeps the pending "7 +"

        c = Keys("7+8");
        c.Press(Clear);
        Assert.Equal("0", c.Display);
        Assert.Null(c.PendingOperator);
        c.Press(D2, CalcKey.Equals);
        Assert.Equal(2m, c.Value);
    }

    [Fact]
    public void Back_DoesNothingOnAResult()
    {
        var c = Keys("12+3=");
        c.Press(Back);
        Assert.Equal("15", c.Display);
    }

    [Fact]
    public void UnaryKeys()
    {
        var c = Keys("9");
        c.Press(Sqrt);
        Assert.Equal(3m, c.Value);
        c = Keys("2,25");
        c.Press(Sqrt);
        Assert.Equal("1,5", c.Display);
        c = Keys("2");
        c.Press(Sqrt);
        Assert.StartsWith("1,41421356237309", c.Display);

        c = Keys("4");
        c.Press(Reciprocal);
        Assert.Equal(0.25m, c.Value);

        c = Keys("12");
        c.Press(Negate);
        Assert.Equal("-12", c.Display);
        c.Press(Negate);
        Assert.Equal("12", c.Display);
        c = Keys("3+4=");
        c.Press(Negate);
        Assert.Equal(-7m, c.Value);
    }

    [Fact]
    public void Percent_OfThePendingOperand()
    {
        var c = Keys("200+10");
        c.Press(Percent);
        Assert.Equal(20m, c.Value);
        c.Press(CalcKey.Equals);
        Assert.Equal(220m, c.Value);
        c = Keys("50");
        c.Press(Percent);
        Assert.Equal(0.5m, c.Value);
    }

    [Fact]
    public void Memory()
    {
        var c = Keys("5");
        Assert.False(c.HasMemory);
        c.Press(MemoryStore);
        Assert.True(c.HasMemory);
        c.Press(D3, MemoryAdd);
        Assert.Equal(8m, c.Memory);
        c.Press(Clear);
        Assert.True(c.HasMemory); // C keeps the memory
        c.Press(MemoryRecall);
        Assert.Equal(8m, c.Value);
        c.Press(Multiply, D2, CalcKey.Equals);
        Assert.Equal(16m, c.Value);
        c.Press(MemoryClear);
        Assert.False(c.HasMemory);
    }

    [Fact]
    public void Errors_DivideByZero_NegativeRoot_Overflow()
    {
        var c = Keys("5/0=");
        Assert.True(c.HasError);
        Assert.Contains("dividir por zero", c.Display);
        c.Press(Add); // ignored
        Assert.True(c.HasError);
        c.Press(D4);
        Assert.False(c.HasError);
        Assert.Equal("4", c.Display);

        c = Keys("0");
        c.Press(Reciprocal);
        Assert.True(c.HasError);
        c.Press(Clear);
        Assert.False(c.HasError);

        c = Keys("9");
        c.Press(Negate, Sqrt);
        Assert.True(c.HasError);

        c = Calc(decimal.MaxValue);
        c.Press(Multiply, D2, CalcKey.Equals);
        Assert.True(c.HasError);
        Assert.Contains("Estouro", c.Display);
    }

    [Fact]
    public void EntryIsLimitedToDecimalPrecision()
    {
        var c = Calc();
        for (var i = 0; i < 40; i++) c.Press(D9);
        Assert.Equal(CalculatorEngine.MaxEntryDigits, c.Display.Length);
        Assert.False(c.HasError);
    }

    [Fact]
    public void ResultFormat_NoGroupingNoExponentNoTrailingZeros()
    {
        Assert.Equal("1234567,89", CalculatorEngine.Format(1234567.890m, PtBr));
        Assert.Equal("0,0000001", CalculatorEngine.Format(0.0000001m, PtBr));
        Assert.Equal("0", CalculatorEngine.Format(-0.0m, PtBr));
        Assert.Equal("1.5", CalculatorEngine.Format(1.5m, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData('7', D7)]
    [InlineData(',', CalcKey.Decimal)]
    [InlineData('.', CalcKey.Decimal)]
    [InlineData('+', Add)]
    [InlineData('-', Subtract)]
    [InlineData('*', Multiply)]
    [InlineData('/', Divide)]
    [InlineData('=', CalcKey.Equals)]
    [InlineData('%', Percent)]
    [InlineData('r', Reciprocal)]
    [InlineData('@', Sqrt)]
    public void KeyboardCharacters(char ch, CalcKey key) => Assert.Equal(key, CalculatorEngine.MapChar(ch));

    [Fact]
    public void UnknownCharacters_AreIgnored() => Assert.Null(CalculatorEngine.MapChar('a'));
}

public class NumericFitTests
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static DataColumnInfo Col(string type, int precision = 0, int scale = 0) =>
        new() { Name = "Valor", TypeName = type, BaseTypeName = type, Precision = precision, Scale = scale };

    [Fact]
    public void Integer_RoundsHalfAwayFromZero_AndKeepsTheClrType()
    {
        var r = NumericFit.Fit(2.5m, typeof(int), Col("int"), PtBr);
        Assert.Equal(NumericFitStatus.Rounded, r.Status);
        Assert.Equal(3, Assert.IsType<int>(r.Value));
        Assert.Contains("inteiro", r.Message);
        Assert.Equal(-3, Assert.IsType<int>(NumericFit.Fit(-2.5m, typeof(int), Col("int"), PtBr).Value));
        var exact = NumericFit.Fit(42m, typeof(long), Col("bigint"), PtBr);
        Assert.Equal(NumericFitStatus.Exact, exact.Status);
        Assert.Equal(42L, Assert.IsType<long>(exact.Value));
        Assert.IsType<short>(NumericFit.Fit(1m, typeof(short), Col("smallint"), PtBr).Value);
        Assert.IsType<byte>(NumericFit.Fit(1m, typeof(byte), Col("tinyint"), PtBr).Value);
    }

    [Theory]
    [InlineData(typeof(byte), "tinyint", 256)]
    [InlineData(typeof(byte), "tinyint", -1)]
    [InlineData(typeof(short), "smallint", 32768)]
    [InlineData(typeof(int), "int", 2147483648)]
    public void Integer_OutOfRange_IsRejected(Type type, string sqlType, double value)
    {
        var r = NumericFit.Fit((decimal)value, type, Col(sqlType), PtBr);
        Assert.Equal(NumericFitStatus.Overflow, r.Status);
        Assert.Null(r.Value);
        Assert.Contains("não cabe em " + sqlType.ToUpperInvariant(), r.Message);
    }

    [Fact]
    public void Decimal_RoundsToTheScale()
    {
        var r = NumericFit.Fit(10m / 3m, typeof(decimal), Col("numeric", 15, 2), PtBr);
        Assert.Equal(NumericFitStatus.Rounded, r.Status);
        Assert.Equal(3.33m, Assert.IsType<decimal>(r.Value));
        Assert.Contains("NUMERIC(15,2) tem 2 casa(s)", r.Message);
        Assert.Equal(1.01m, Assert.IsType<decimal>(NumericFit.Fit(1.005m, typeof(decimal), Col("decimal", 10, 2), PtBr).Value)); // half away from zero
        Assert.Equal(NumericFitStatus.Exact, NumericFit.Fit(12.5m, typeof(decimal), Col("decimal", 10, 2), PtBr).Status);
    }

    [Fact]
    public void Decimal_TooManyIntegerDigits_IsRejected()
    {
        var col = Col("decimal", 5, 2); // max 999,99
        Assert.Equal(NumericFitStatus.Exact, NumericFit.Fit(999.99m, typeof(decimal), col, PtBr).Status);
        var r = NumericFit.Fit(1000m, typeof(decimal), col, PtBr);
        Assert.Equal(NumericFitStatus.Overflow, r.Status);
        Assert.Contains("3 dígito(s) antes da vírgula", r.Message);
        // Rounding can push it over the limit too.
        Assert.Equal(NumericFitStatus.Overflow, NumericFit.Fit(999.996m, typeof(decimal), col, PtBr).Status);
    }

    [Fact]
    public void Money_KeepsFourDecimals_AndItsRange()
    {
        var r = NumericFit.Fit(1m / 3m, typeof(decimal), Col("money"), PtBr);
        Assert.Equal(0.3333m, Assert.IsType<decimal>(r.Value));
        Assert.Equal(NumericFitStatus.Overflow, NumericFit.Fit(214_748.3648m, typeof(decimal), Col("smallmoney"), PtBr).Status);
        Assert.Equal(NumericFitStatus.Exact, NumericFit.Fit(214_748.3647m, typeof(decimal), Col("smallmoney"), PtBr).Status);
    }

    [Fact]
    public void AliasType_UsesTheBaseType()
    {
        var col = new DataColumnInfo { Name = "Preco", TypeName = "D_VALOR", BaseTypeName = "numeric", Precision = 9, Scale = 3 };
        Assert.Equal(1.235m, Assert.IsType<decimal>(NumericFit.Fit(1.2345m, typeof(decimal), col, PtBr).Value));
    }

    [Fact]
    public void FloatAndReal_TakeTheValue()
    {
        Assert.Equal(0.1, Assert.IsType<double>(NumericFit.Fit(0.1m, typeof(double), Col("float"), PtBr).Value));
        Assert.Equal(0.5f, Assert.IsType<float>(NumericFit.Fit(0.5m, typeof(float), Col("real"), PtBr).Value));
    }

    [Fact]
    public void NonNumericColumn_IsRejected() =>
        Assert.Equal(NumericFitStatus.Overflow, NumericFit.Fit(1m, typeof(string), Col("nvarchar"), PtBr).Status);

    [Theory]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(decimal), true)]
    [InlineData(typeof(double), true)]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(DateTime), false)]
    [InlineData(typeof(bool), false)]
    public void CalculatorColumns(Type type, bool expected) => Assert.Equal(expected, NumericFit.IsCalculatorType(type));
}

/// <summary>Static checks of the calculator wiring in the Data grid / Form View (the popup is WPF, the logic is Core).</summary>
public class CalculatorWiringAuditTests
{
    private static string Views()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SqlExpert.App", "Views");
    }

    private static string Src(string file) => File.ReadAllText(Path.Combine(Views(), file));

    [Fact]
    public void Grid_NumericColumnsOpenTheCalculator()
    {
        var grid = Src("TableDataGrid.xaml.cs");
        Assert.Contains("isCalc ? OpenGridCalculator", grid);
        Assert.Contains("IsCalculator: true", grid); // F4 / Alt+Down on a selected cell
        Assert.Contains("NumericFit.Fit(", grid);
    }

    [Fact]
    public void Form_NumericFieldsOpenTheCalculator()
    {
        var form = Src("TableDataGrid.Form.cs");
        Assert.Contains("case FormFieldKind.Number when !spec.IsReadOnly", form);
        Assert.Contains("OpenFormCalculator(", form);
    }

    [Fact]
    public void Popup_HasEveryClassicKey_AndEnterEscape()
    {
        var popup = Src("CalculatorPopup.cs");
        foreach (var caption in new[] { "\"Back\"", "\"CE\"", "\"C\"", "\"MC\"", "\"MR\"", "\"MS\"", "\"M+\"", "\"sqrt\"", "\"%\"", "\"1/x\"", "\"+/-\"", "\"=\"", "\"/\"", "\"*\"", "\"-\"", "\"+\"" })
            Assert.Contains(caption, popup);
        for (var d = 0; d <= 9; d++) Assert.Contains($"(\"{d}\", CalcKey.D{d}", popup);
        Assert.Contains("NumberDecimalSeparator, CalcKey.Decimal", popup);
        Assert.Contains("Key.Enter => CalcKey.Equals", popup);
        Assert.Contains("Key.Escape", popup);
    }
}
