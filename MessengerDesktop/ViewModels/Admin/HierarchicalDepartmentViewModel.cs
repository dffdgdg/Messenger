using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerShared.DTO.Department;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MessengerDesktop.ViewModels;

public partial class HierarchicalDepartmentViewModel(DepartmentDTO department, int level) : ObservableObject
{
    public DepartmentDTO Department { get; } = department;
    public int Id => Department.Id;
    public string Name => Department.Name;
    public int Level { get; } = level;
    public int UserCount => Department.UserCount;
    public string? HeadName => Department.HeadName;
    public bool HasHead => Department.Head.HasValue;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private ObservableCollection<HierarchicalDepartmentViewModel> _children = [];

    public bool HasChildren => Children.Count > 0;
    public double Indent => Level * 24;

    public string HeadInitials => HasHead ? GetInitials(HeadName) : "";

    public string UserCountText => UserCount switch
    {
        0 => "Нет сотрудников",
        1 => "1 сотрудник",
        < 5 => $"{UserCount} сотрудника",
        _ => $"{UserCount} сотрудников"
    };

    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (HasHead)
                parts.Add($"?? {HeadName}");

            parts.Add($"?? {UserCountText}");

            return string.Join("  •  ", parts);
        }
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    private static string GetInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var parts = name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        if (parts.Length == 1)
            return parts[0][0].ToString().ToUpper();
        return "?";
    }
}