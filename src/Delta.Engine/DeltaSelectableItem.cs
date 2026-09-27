using System.ComponentModel;
using Autodesk.DesignScript.Runtime;

namespace Delta.Core;

/// <summary>
/// One row in a Delta multi-select combobox. <see cref="Id"/> is the stable key
/// persisted with the graph; <see cref="Name"/> is the label shown in the UI.
/// </summary>
[SupressImportIntoVM]
public sealed class DeltaSelectableItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public DeltaSelectableItem(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetSelected(value, notifyNode: true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<DeltaSelectableItem>? SelectionChanged;

    public void SetSelected(bool value, bool notifyNode)
    {
        if (_isSelected == value)
        {
            return;
        }

        _isSelected = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        if (notifyNode)
        {
            SelectionChanged?.Invoke(this);
        }
    }
}
