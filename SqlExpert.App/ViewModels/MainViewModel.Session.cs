using System.IO;
using System.Text.Json;
using SqlExpert.App.Services;
using SqlExpert.Core.Documents;
using SqlExpert.Core.Models;

namespace SqlExpert.App.ViewModels;

/// <summary>
/// Open windows per database registration: the Windows-bar tabs of a registration (tables with their active page,
/// procedures, SQL Editors with their text) are saved when it is disconnected or the program closes, and reopened
/// in the same order on its next connect (%AppData%\SqlExpert\sessions\&lt;registration id&gt;.json).
/// </summary>
public sealed partial class MainViewModel
{
    private readonly OpenTabsSessionStore _tabsStore = new();
    private bool _restoringTabs;
    private bool _tabsSavedForExit;

    /// <summary>Folder of the saved sessions (one file per registration).</summary>
    public string SavedTabsDirectory => _tabsStore.Directory;

    /// <summary>Registrations that have open documents, plus the connected one (its empty set is saved too).</summary>
    private IReadOnlyList<string> ProfilesWithTabs()
    {
        var ids = _docs.Documents.Select(d => d.ProfileId).ToList();
        if (IsConnected && _activeProfileId is not null) ids.Add(_activeProfileId);
        return ids.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Saved form of one document; null for documents that cannot be reopened (e.g. a New Procedure not compiled yet).</summary>
    internal static SessionTab? CaptureTab(DocumentViewModel doc) => doc switch
    {
        TableDocumentViewModel t => new SessionTab
        {
            Kind = SessionTabKind.Table, Database = t.Database, Schema = t.Schema, Name = t.Table, SelectedTab = t.SelectedTabIndex
        },
        ProcedureDocumentViewModel { Exists: true } p => new SessionTab
        {
            Kind = SessionTabKind.Procedure, Database = p.Database, Schema = p.ServerSchema, Name = p.ServerName, SelectedTab = p.SelectedTabIndex
        },
        SqlEditorDocumentViewModel s => s.CaptureSessionTab(),
        _ => null
    };

    /// <summary>
    /// Closes the documents of a registration one by one (same prompts as Close), remembering each one as it closes,
    /// then saves them as that registration's open windows. False = the user cancelled a prompt (nothing saved).
    /// </summary>
    private async Task<bool> CloseProfileDocumentsAsync(string? profileId)
    {
        if (profileId is null) return true;
        var activeKey = ActiveDocument is { } a && a.ProfileId == profileId ? a.Key : null;
        var captured = new List<(SessionTab Tab, string Key)>();
        foreach (var doc in DocumentsOf(profileId))
        {
            if (!_docs.Documents.Contains(doc)) continue;
            if (doc.HasUnsavedChanges)
                ActiveDocument = doc;
            if (!await doc.ConfirmCloseAsync().ConfigureAwait(true)) return false;
            // Captured after the prompt: a SQL Editor saved to a file there is remembered with that file.
            if (CaptureTab(doc) is { } tab) captured.Add((tab, doc.Key));
            RemoveDocument(doc);
        }
        SaveTabs(profileId, captured, activeKey);
        return true;
    }

    /// <summary>
    /// Program closing without prompts (nothing needed confirmation, or Windows is ending the session):
    /// saves the open windows as they are. Skipped when <see cref="CloseAllForExitAsync"/> already saved them.
    /// </summary>
    public void SaveOpenTabsForExit()
    {
        if (_tabsSavedForExit) return;
        _tabsSavedForExit = true;
        foreach (var profileId in ProfilesWithTabs())
        {
            var activeKey = ActiveDocument is { } a && a.ProfileId == profileId ? a.Key : null;
            var captured = new List<(SessionTab Tab, string Key)>();
            foreach (var doc in DocumentsOf(profileId))
                if (CaptureTab(doc) is { } tab) captured.Add((tab, doc.Key));
            SaveTabs(profileId, captured, activeKey);
        }
    }

    private void SaveTabs(string profileId, IReadOnlyList<(SessionTab Tab, string Key)> tabs, string? activeKey)
    {
        try
        {
            var active = -1;
            for (var i = 0; i < tabs.Count; i++)
                if (string.Equals(tabs[i].Key, activeKey, StringComparison.OrdinalIgnoreCase)) active = i;
            _tabsStore.Save(new OpenTabsSession
            {
                ProfileId = profileId,
                SavedAt = DateTimeOffset.Now,
                Tabs = tabs.Select(t => t.Tab).ToList(),
                ActiveIndex = active
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _tabsStore.Log($"{ProfileLabelOf(profileId)}: janelas abertas não gravadas ({ex.Message}).");
        }
    }

    private void DeleteSavedTabs(string profileId)
    {
        try { _tabsStore.Delete(profileId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _tabsStore.Log($"Sessão de {profileId} não removida ({ex.Message})."); }
    }

    private string ProfileLabelOf(string profileId) => FindProfile(profileId) is { } p ? ProfileLabel(p) : profileId;

    /// <summary>
    /// Reopens the saved windows of <paramref name="profile"/> (just connected) in their order and activates the one that was active.
    /// Objects that no longer exist are skipped (noted in sessions\session.log); SQL Editor text is put back, never executed.
    /// </summary>
    private async Task RestoreOpenTabsAsync(ConnectionProfile profile)
    {
        if (!UiSettings.Instance.RestoreOpenTabs || _restoringTabs) return;
        var profileId = profile.Id;
        var label = ProfileLabel(profile);
        void Log(string message) => _tabsStore.Log($"{label}: {message}");

        var plan = OpenTabsRestorePlan.Normalize(_tabsStore.Load(profileId), Log);
        if (plan.Tabs.Count == 0) return;
        var activeTab = plan.ActiveIndex >= 0 ? plan.Tabs[plan.ActiveIndex] : null;

        _restoringTabs = true;
        var opened = 0;
        DocumentViewModel? activeDoc = null;
        try
        {
            StatusText = $"Reabrindo as janelas de {label}...";
            var tabs = await OpenTabsExistence.FilterExistingAsync(plan.Tabs,
                async (db, ct) => await OpenDatabaseConnectionAsync(profileId, db, ct).ConfigureAwait(true), Log).ConfigureAwait(true);

            foreach (var tab in tabs)
            {
                if (!IsProfileConnected(profileId)) break; // disconnected meanwhile
                DocumentViewModel? doc;
                try
                {
                    doc = await RestoreTabAsync(profile, tab).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log($"{tab.DisplayText}: não reaberta ({ex.Message}).");
                    continue;
                }
                if (doc is null) continue;
                opened++;
                if (ReferenceEquals(tab, activeTab)) activeDoc = doc;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log($"restauração interrompida ({ex.Message}).");
        }
        finally
        {
            _restoringTabs = false;
        }

        if (activeDoc is not null && _docs.Documents.Contains(activeDoc))
            ActiveDocument = activeDoc;
        var skipped = plan.Tabs.Count - opened;
        StatusText = skipped == 0
            ? $"{opened} janela(s) de {label} reaberta(s)."
            : $"{opened} janela(s) de {label} reaberta(s); {skipped} não reaberta(s) (detalhes em sessions\\session.log).";
    }

    private async Task<DocumentViewModel?> RestoreTabAsync(ConnectionProfile profile, SessionTab tab)
    {
        switch (tab.Kind)
        {
            case SessionTabKind.Table:
            {
                var doc = await OpenTableDocumentAsync(tab.Database, tab.Schema!, tab.Name!).ConfigureAwait(true);
                if (doc is not null && tab.SelectedTab < TableDocumentViewModel.TabCount)
                    doc.SelectedTabIndex = tab.SelectedTab;
                return doc;
            }
            case SessionTabKind.Procedure:
            {
                var doc = await OpenProcedureDocumentAsync(tab.Database, tab.Schema!, tab.Name!).ConfigureAwait(true);
                if (doc is not null && tab.SelectedTab < ProcedureDocumentViewModel.TabCount)
                    doc.SelectedTabIndex = tab.SelectedTab;
                return doc;
            }
            case SessionTabKind.SqlEditor:
            {
                var used = _docs.Documents.OfType<SqlEditorDocumentViewModel>().Select(e => e.Number).ToHashSet();
                var number = tab.Number;
                if (used.Contains(number))
                {
                    number = 1;
                    while (used.Contains(number)) number++;
                }
                var database = !string.IsNullOrWhiteSpace(tab.Database) ? tab.Database
                    : _activeDatabase ?? (string.IsNullOrWhiteSpace(profile.InitialCatalog) ? "master" : profile.InitialCatalog);
                var doc = CreateSqlEditorDocument(profile, database, number);
                _docs.OpenOrActivate(doc.Key, () => doc);
                doc.RestoreSessionText(tab);
                return doc;
            }
            default:
                return null;
        }
    }
}
