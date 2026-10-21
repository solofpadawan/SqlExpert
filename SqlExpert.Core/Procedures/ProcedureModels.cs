using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SqlExpert.Core.Procedures;

/// <summary>CREATE / ALTER / CREATE OR ALTER (the verb of the generated header).</summary>
public enum ProcedureVerb
{
    Create,
    Alter,
    CreateOrAlter
}

/// <summary>
/// One row of the parameter grid of the procedure editor. Editing a row regenerates the header
/// (<see cref="Changed"/> is raised for every property change).
/// </summary>
public sealed class ProcedureParameter : INotifyPropertyChanged
{
    private string _name = "";
    private string _typeName = "int";
    private string? _size;
    private string? _scale;
    private string? _default;
    private bool _isOutput;
    private bool _isReadOnly;
    private string? _description;

    /// <summary>Parameter name with the leading "@" (added when typed without it).</summary>
    public string Name { get => _name; set => Set(ref _name, NormalizeName(value)); }
    /// <summary>Type name without size, e.g. "varchar", "decimal", "dbo.TipoLista".</summary>
    public string TypeName { get => _typeName; set => Set(ref _typeName, (value ?? "").Trim()); }
    /// <summary>Length / precision ("50", "max", "18"), empty when the type takes none.</summary>
    public string? Size { get => _size; set => Set(ref _size, Blank(value)); }
    /// <summary>Scale (decimal/numeric), empty otherwise.</summary>
    public string? Scale { get => _scale; set => Set(ref _scale, Blank(value)); }
    /// <summary>Default value as T-SQL text ("0", "NULL", "N'abc'"), empty = no default.</summary>
    public string? Default { get => _default; set => Set(ref _default, Blank(value)); }
    public bool IsOutput { get => _isOutput; set { if (Set(ref _isOutput, value) && value) IsReadOnly = false; } }
    /// <summary>READONLY (table-valued parameters).</summary>
    public bool IsReadOnly { get => _isReadOnly; set { if (Set(ref _isReadOnly, value) && value) IsOutput = false; } }
    /// <summary>MS_Description of the parameter (saved as an extended property after a successful compile).</summary>
    public string? Description { get => _description; set => Set(ref _description, Blank(value)); }

    /// <summary>"varchar(50)", "decimal(18,2)", "int".</summary>
    public string FullType => ProcedureScriptBuilder.FormatType(TypeName, Size, Scale);

    public ProcedureParameter Clone() => new()
    {
        Name = Name, TypeName = TypeName, Size = Size, Scale = Scale, Default = Default,
        IsOutput = IsOutput, IsReadOnly = IsReadOnly, Description = Description
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after any property changed (header regeneration / dirty flag).</summary>
    public event EventHandler? Changed;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(TypeName) or nameof(Size) or nameof(Scale))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullType)));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public static string NormalizeName(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return "";
        return v.StartsWith('@') ? v : "@" + v;
    }
}

/// <summary>A procedure split into its parts: name, parameters, WITH options and the body after AS.</summary>
public sealed class ProcedureDefinition
{
    public string Schema { get; set; } = "dbo";
    public string Name { get; set; } = "";
    public List<ProcedureParameter> Parameters { get; set; } = new();
    /// <summary>Text of the WITH options as written ("RECOMPILE, EXECUTE AS OWNER"), null when none.</summary>
    public string? Options { get; set; }
    public bool ForReplication { get; set; }
    /// <summary>Everything after the AS keyword, comments and formatting preserved.</summary>
    public string Body { get; set; } = "";
    /// <summary>Text before CREATE/ALTER (usually comments), kept verbatim.</summary>
    public string? Preamble { get; set; }
    /// <summary>Verb found in the parsed text (informative).</summary>
    public ProcedureVerb SourceVerb { get; set; } = ProcedureVerb.Create;
    /// <summary>The header (between CREATE and AS) contained comments, which a regenerated header would lose.</summary>
    public bool HeaderHasComments { get; set; }
}

/// <summary>Result of <see cref="ProcedureScriptParser.Parse"/>.</summary>
public sealed class ProcedureParseResult
{
    public ProcedureDefinition? Definition { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public bool Success => Definition is not null;
}

/// <summary>A variable or cursor declared in the body (Variables / Cursors lists).</summary>
public sealed record DeclaredItem(string Name, string Type, string? Value, int Line);

/// <summary>Row of the Dependencies tab.</summary>
public sealed record ObjectDependency(string Direction, string? Schema, string Name, string? Type);

/// <summary>Row of the Grants tab.</summary>
public sealed record ObjectPermission(string State, string Permission, string Grantee, string GranteeType, string? Grantor, string? Column);

/// <summary>Procedure as read from the server (definition + metadata for the editor tabs).</summary>
public sealed class ProcedureInfo
{
    public string Schema { get; init; } = "dbo";
    public string Name { get; init; } = "";
    public string? Definition { get; init; }
    public string? Description { get; init; }
    /// <summary>MS_Description per parameter name ("@id" → text).</summary>
    public IReadOnlyDictionary<string, string> ParameterDescriptions { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool IsClr { get; init; }
    public DateTime? Created { get; init; }
    public DateTime? Modified { get; init; }
}

/// <summary>One failure of a "Recompile all …" / "Recompute selectivity" run.</summary>
public sealed record BatchOperationFailure(string Object, string Message);

/// <summary>Result of a batch maintenance run over many objects.</summary>
public sealed class BatchOperationResult
{
    public int Total { get; set; }
    public int Done { get; set; }
    public bool Cancelled { get; set; }
    public List<BatchOperationFailure> Failures { get; } = new();
    public TimeSpan Elapsed { get; set; }
}
