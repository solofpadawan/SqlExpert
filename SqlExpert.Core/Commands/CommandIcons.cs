using SqlExpert.Core.Explorer;

namespace SqlExpert.Core.Commands;

/// <summary>
/// Vector icon (Themes/Icons.xaml key) of each command shown as a button on the inner toolbars of the table editor
/// (Fields, Constraints, Indices, Dependencies, Grants). The button shows the icon followed by the caption.
/// </summary>
public static class CommandIcons
{
    /// <summary>Command categories rendered as inner toolbars (every command in them must have an icon).</summary>
    public static readonly IReadOnlyList<string> ToolbarCategories = new[] { "Fields", "Constraints", "Indices", "Dependencies", "Grants" };

    private const string P = ObjectIcons.Prefix;

    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fields.new_field"] = P + "InsertRecord",
        ["fields.edit_field"] = P + "EditRecord",
        ["fields.drop_field"] = P + "DeleteRecord",
        ["fields.reorder_fields"] = P + "Reorder",
        ["fields.copy_fields_list_to_clipboard"] = P + "Paste",
        ["constraints.new_constraint"] = P + "InsertRecord",
        ["constraints.drop_constraint"] = P + "DeleteRecord",
        ["constraints.autowidth"] = P + "AutoWidth",
        ["indices.new_index"] = P + "InsertRecord",
        ["indices.drop_index"] = P + "DeleteRecord",
        ["indices.copy_index_name"] = P + "Copy",
        ["dependencies.expand_all"] = P + "ExpandAll",
        ["dependencies.collapse_all"] = P + "CollapseAll",
        ["dependencies.display_operations"] = P + "Description",
        ["dependencies.print_dependencies_tree"] = P + "Print",
        ["dependencies.apply_block_to_selected_objects"] = P + "Constraint",
        ["grants.grant_to_all"] = P + "GrantManager",
        ["grants.grant_all_with_grant_option"] = P + "GrantManager",
        ["grants.revoke_from_all"] = P + "Revoke",
        ["grants.grant_to_all_2"] = P + "GrantManager",
        ["grants.grant_all_with_grant_option_2"] = P + "GrantManager",
        ["grants.revoke_from_all_2"] = P + "Revoke",
        ["grants.revoke_all_from_all"] = P + "Revoke",
    };

    /// <summary>Icon key of <paramref name="commandId"/>, or null when it has none.</summary>
    public static string? For(string commandId) => Map.TryGetValue(commandId, out var key) ? key : null;

    /// <summary>Every icon key used here (audited against Icons.xaml).</summary>
    public static IReadOnlyCollection<string> AllKeys() => Map.Values.Distinct(StringComparer.Ordinal).ToList();
}
