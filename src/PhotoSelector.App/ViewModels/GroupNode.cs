using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhotoSelector.App.ViewModels;

public sealed class GroupNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private string _title = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Key { get; init; }
    public required string Title
    {
        get => _title;
        set
        {
            _title = value;
            NotifyChanged(nameof(Title));
        }
    }
    public string ColorSwatch { get; set; } = string.Empty;
    public ObservableCollection<GroupNode> Children { get; } = new();
    public bool IsLeaf => Children.Count == 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public void NotifyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
