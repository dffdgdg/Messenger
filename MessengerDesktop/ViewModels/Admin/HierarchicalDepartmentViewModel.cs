using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerShared.DTO.Department;
using System.Collections.ObjectModel;

namespace MessengerDesktop.ViewModels;

public partial class HierarchicalDepartmentViewModel(DepartmentDTO department, int level) : ObservableObject
{
    public DepartmentDTO Department { get; } = department;
    public int Level { get; } = level;

    public int Id => Department.Id;
    public string Name => Department.Name;
    public string? HeadName => Department.HeadName;
    public int UserCount => Department.UserCount;
    public int? ParentId => Department.ParentDepartmentId;
    public bool HasHead => !string.IsNullOrEmpty(HeadName);
    public bool HasChildren => Children.Count > 0;
    public bool IsRoot => Level == 0;
    public bool IsLeaf => !HasChildren;

    /// <summary>
    /// Показывать ли счётчик сотрудников (только если > 0)
    /// </summary>
    public bool ShowUserCount => UserCount > 0;

    /// <summary>
    /// Текст счётчика с правильным склонением
    /// </summary>
    public string UserCountText => UserCount switch
    {
        1 => "1 сотрудник",
        >= 2 and <= 4 => $"{UserCount} сотрудника",
        _ => $"{UserCount} сотрудников"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpanderRotation))]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>
    /// Угол поворота шеврона
    /// </summary>
    public double ExpanderRotation => IsExpanded ? 0 : -90;

    // === Дочерние элементы ===
    public ObservableCollection<HierarchicalDepartmentViewModel> Children { get; } = [];

    [RelayCommand]
    private void ToggleExpand()
    {
        if (HasChildren)
        {
            IsExpanded = !IsExpanded;
        }
    }

    [RelayCommand]
    private void Select() => IsSelected = true;
}