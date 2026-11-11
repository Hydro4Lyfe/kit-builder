using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitBuilder.Core.Kit;

namespace KitBuilder.App.ViewModels;

/// <summary>
/// One of settings.json's removal lists (apps, capabilities, features) as a searchable
/// checklist with readable names. Ticked means "remove during setup". Known items not in the
/// list are offered unticked; IDs the catalog does not know are still shown, by their ID.
/// </summary>
public partial class RemovalListViewModel : ViewModelBase
{
    private readonly Action _changed;

    public RemovalListViewModel(IReadOnlyList<CatalogEntry> catalog, IReadOnlyList<string> removed, Action changed)
    {
        _changed = changed;
        var removedSet = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(catalog.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

        var rows = catalog
            .Select(e => new RemovalItemViewModel(e.Id, e.Name, e.Note, removedSet.Contains(e.Id), OnItemChanged))
            .Concat(removed.Where(id => !known.Contains(id))
                .Select(id => new RemovalItemViewModel(id, id, "Added by name", isRemoved: true, OnItemChanged)))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var row in rows) Items.Add(row);

        // Saving keeps the file's existing order, so a one-tick change is a one-line diff.
        _originalOrder = removed.ToList();
        Refilter();
    }

    private readonly List<string> _originalOrder;

    public ObservableCollection<RemovalItemViewModel> Items { get; } = [];

    /// <summary>The items matching the search box.</summary>
    public ObservableCollection<RemovalItemViewModel> Visible { get; } = [];

    [ObservableProperty] private string _filter = "";

    partial void OnFilterChanged(string value) => Refilter();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newId = "";

    public string Summary
    {
        get
        {
            var removed = Items.Count(i => i.IsRemoved);
            return $"{removed} of {Items.Count} will be removed";
        }
    }

    /// <summary>The IDs to write back: the original order first, then newly ticked ones.</summary>
    public IReadOnlyList<string> RemovedIds()
    {
        var ticked = Items.Where(i => i.IsRemoved).Select(i => i.Id).ToList();
        var tickedSet = new HashSet<string>(ticked, StringComparer.OrdinalIgnoreCase);
        var kept = _originalOrder.Where(tickedSet.Contains).ToList();
        var keptSet = new HashSet<string>(kept, StringComparer.OrdinalIgnoreCase);
        return kept.Concat(ticked.Where(id => !keptSet.Contains(id))).ToList();
    }

    [RelayCommand]
    private void TickAll() => SetVisible(true);

    [RelayCommand]
    private void UntickAll() => SetVisible(false);

    private bool CanAdd() => NewId.Trim().Length > 0;

    /// <summary>Adds an ID the catalog does not know (e.g. a manufacturer's app), ticked.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var id = NewId.Trim();
        var existing = Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsRemoved = true;
        }
        else
        {
            Items.Add(new RemovalItemViewModel(id, id, "Added by name", isRemoved: true, OnItemChanged));
            OnItemChanged();
        }
        NewId = "";
        Filter = "";
        Refilter();
    }

    private void SetVisible(bool removed)
    {
        foreach (var item in Visible) item.IsRemoved = removed;
    }

    private void OnItemChanged()
    {
        OnPropertyChanged(nameof(Summary));
        _changed();
    }

    private void Refilter()
    {
        var filter = Filter.Trim();
        Visible.Clear();
        foreach (var item in Items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (filter.Length == 0 ||
                item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
                Visible.Add(item);
        }
        OnPropertyChanged(nameof(Summary));
    }
}

/// <summary>One removable thing: ticked means it is removed during setup.</summary>
public partial class RemovalItemViewModel : ViewModelBase
{
    private readonly Action _changed;

    public RemovalItemViewModel(string id, string name, string? note, bool isRemoved, Action changed)
    {
        Id = id;
        Name = name;
        Note = note;
        _isRemoved = isRemoved;
        _changed = changed;
    }

    public string Id { get; }
    public string Name { get; }
    public string? Note { get; }

    /// <summary>The small grey line: the note if there is one, and the ID unless the name is the ID.</summary>
    public string Detail => string.Join("  ·  ", new[] { Note, Name == Id ? null : Id }.Where(s => !string.IsNullOrEmpty(s)));

    [ObservableProperty] private bool _isRemoved;

    partial void OnIsRemovedChanged(bool value) => _changed();
}
