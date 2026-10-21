using System.Collections.ObjectModel;
using System.Windows.Input;
using SqlExpert.App.Commands;
using SqlExpert.Core.Commands;

namespace SqlExpert.App.Services;

/// <summary>
/// Wires <see cref="CommandCatalog"/> to WPF <see cref="ICommand"/> instances.
/// Menus, toolbars and context menus resolve commands by Id.
/// </summary>
public sealed class AppCommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<bool> _isConnected;
    private readonly Func<bool> _hasSelection;
    private readonly Action<CommandDefinition> _onExecute;

    public AppCommandRegistry(
        Func<bool> isConnected,
        Func<bool> hasSelection,
        Action<CommandDefinition> onExecute)
    {
        _isConnected = isConnected;
        _hasSelection = hasSelection;
        _onExecute = onExecute;

        foreach (var def in CommandCatalog.All)
        {
            var local = def;
            _commands[def.Id] = new RelayCommand(
                _ => _onExecute(local),
                _ => CanExecute(local));
        }
    }

    public ICommand this[string id] =>
        _commands.TryGetValue(id, out var cmd) ? cmd : Disabled;

    public ICommand Get(string id) => this[id];

    public CommandDefinition? Definition(string id) => CommandCatalog.Find(id);

    /// <summary>Menu/toolbar rows for a category, including separator markers.</summary>
    public IReadOnlyList<CommandDefinition> Category(string category) =>
        CommandCatalog.ByCategory(category).ToList();

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();

    /// <summary>False for commands without a handler (greyed out), otherwise the command's Connected/selection rule.</summary>
    private bool CanExecute(CommandDefinition def) =>
        CommandAvailability.CanExecute(def, _isConnected(), _hasSelection());

    private static readonly ICommand Disabled = new RelayCommand(_ => { }, _ => false);
}

/// <summary>Row used to build dynamic menus from the registry.</summary>
public sealed class CommandMenuItem
{
    public required string Id { get; init; }
    public required string Caption { get; init; }
    public string? Shortcut { get; init; }
    public string? ToolTip { get; init; }
    public bool IsSeparator { get; init; }
    /// <summary>Null for a submenu parent (its <see cref="Children"/> carry the commands).</summary>
    public ICommand? Command { get; init; }
    public object? CommandParameter { get; init; }
    /// <summary>Shows the check mark (toggles / current choice). The menu is rebuilt each time it opens.</summary>
    public bool IsChecked { get; init; }
    /// <summary>Submenu rows (null = plain item).</summary>
    public IReadOnlyList<CommandMenuItem>? Children { get; init; }
}
