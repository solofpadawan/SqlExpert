using System.Reflection;
using System.Runtime.InteropServices;
using SqlExpert.Core.About;
using SqlExpert.Core.Commands;

namespace SqlExpert.Tests;

public class ProductVersionInfoTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.CreateCustomTimeZone("UTC-3", TimeSpan.FromHours(-3), "UTC-3", "UTC-3");

    private static ProductVersionInfo Sample(DateTimeOffset? built = null) =>
        new("SqlExpert", "1.0.0", built ?? new DateTimeOffset(2026, 10, 20, 13, 58, 0, TimeSpan.Zero), "x64", "8.0.21");

    [Fact]
    public void Format_ShortLine()
    {
        Assert.Equal("SqlExpert 1.0.0 (build 2026-10-20) - x64 - .NET 8.0.21",
            ProductVersionInfo.Format(Sample(), includeTime: false, SaoPaulo));
    }

    [Fact]
    public void Format_DetailedLine_UsesLocalZone()
    {
        Assert.Equal("SqlExpert 1.0.0 (build 2026-10-20 10:58) - x64 - .NET 8.0.21",
            ProductVersionInfo.Format(Sample(), includeTime: true, SaoPaulo));
    }

    [Fact]
    public void Format_BuildDateCrossesMidnight_InLocalZone()
    {
        var built = new DateTimeOffset(2026, 10, 21, 1, 30, 0, TimeSpan.Zero); // 22:30 on the 20th in UTC-3
        Assert.Equal("SqlExpert 1.0.0 (build 2026-10-20) - x64 - .NET 8.0.21",
            ProductVersionInfo.Format(Sample(built), includeTime: false, SaoPaulo));
    }

    [Fact]
    public void Format_WithoutBuildDate_OmitsBuildPart()
    {
        var info = Sample() with { BuildDate = null };
        Assert.Equal("SqlExpert 1.0.0 - x64 - .NET 8.0.21", ProductVersionInfo.Format(info, false, SaoPaulo));
    }

    [Fact]
    public void Format_SkipsEmptyParts()
    {
        var info = new ProductVersionInfo("SqlExpert", "", null, "", "");
        Assert.Equal("SqlExpert", ProductVersionInfo.Format(info, true, SaoPaulo));
    }

    [Theory]
    [InlineData("1.0.0+3f2a9c1", null, "1.0.0")]
    [InlineData("1.2.3-beta.1", "1.2.3.0", "1.2.3-beta.1")]
    [InlineData(null, "1.0.0.0", "1.0.0")]
    [InlineData("  ", "2.1.0.7", "2.1.0.7")]
    [InlineData(null, null, "3.4.5")]
    [InlineData(null, "not-a-version", "3.4.5")]
    public void NormalizeVersion_PrefersInformationalThenFile(string? info, string? file, string expected)
    {
        Assert.Equal(expected, ProductVersionInfo.NormalizeVersion(info, file, new Version(3, 4, 5, 0)));
    }

    [Fact]
    public void NormalizeVersion_NothingKnown_IsEmpty()
    {
        Assert.Equal("", ProductVersionInfo.NormalizeVersion(null, null, null));
    }

    [Theory]
    [InlineData("2026-10-20T13:58:00.1234567Z", 2026, 10, 20, 13, 58)]
    [InlineData("2026-10-20T10:58:00-03:00", 2026, 10, 20, 13, 58)]
    public void ParseBuildDate_RoundTripFormats(string value, int y, int mo, int d, int h, int mi)
    {
        var parsed = ProductVersionInfo.ParseBuildDate(value);
        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), parsed!.Value.UtcDateTime.AddTicks(-(parsed.Value.UtcDateTime.Ticks % TimeSpan.TicksPerMinute)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    public void ParseBuildDate_Invalid_IsNull(string? value)
    {
        Assert.Null(ProductVersionInfo.ParseBuildDate(value));
    }

    [Theory]
    [InlineData(Architecture.X64, "x64")]
    [InlineData(Architecture.X86, "x86")]
    [InlineData(Architecture.Arm64, "ARM64")]
    public void ArchitectureName_Maps(Architecture arch, string expected)
    {
        Assert.Equal(expected, ProductVersionInfo.ArchitectureName(arch));
    }

    [Fact]
    public void FromAssembly_ReadsRealMetadataAndRuntime()
    {
        var info = ProductVersionInfo.FromAssembly(typeof(ProductVersionInfo).Assembly, "SqlExpert");
        Assert.Equal("SqlExpert", info.ProductName);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.DoesNotContain("+", info.Version);
        Assert.Equal(Environment.Version.ToString(), info.RuntimeVersion);
        Assert.Equal(ProductVersionInfo.ArchitectureName(RuntimeInformation.ProcessArchitecture), info.Architecture);
        Assert.StartsWith("SqlExpert " + info.Version, info.DisplayText);
        Assert.EndsWith(" - .NET " + Environment.Version, info.DisplayText);
    }

    [Fact]
    public void AppProject_StampsVersionAndBuildDate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SqlExpert.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var csproj = File.ReadAllText(Path.Combine(dir!.FullName, "SqlExpert.App", "SqlExpert.App.csproj"));
        Assert.Contains("<Version>", csproj);
        Assert.Contains($"<AssemblyMetadata Include=\"{ProductVersionInfo.BuildDateMetadataKey}\"", csproj);
    }

    [Fact]
    public void HelpAbout_IsEnabled_OtherHelpItemsStayGrey()
    {
        Assert.True(CommandAvailability.IsMenuActionImplemented("About"));
        Assert.True(CommandAvailability.CanExecute(CommandCatalog.Find("help.about")!, false, false));
        Assert.False(CommandAvailability.IsMenuActionImplemented("What's new?"));
        Assert.False(CommandAvailability.IsMenuActionImplemented("Product Home Page"));
        Assert.False(CommandAvailability.IsMenuActionImplemented("Support"));
    }
}
