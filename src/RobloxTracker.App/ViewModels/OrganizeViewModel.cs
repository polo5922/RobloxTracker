using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobloxTracker.Core.Services;

namespace RobloxTracker.App.ViewModels;

public partial class CategoryEditItem(CategoryInfo info) : ObservableObject
{
    public int Id { get; } = info.Id;
    public int GameCount { get; } = info.GameCount;

    [ObservableProperty] private string name = info.Name;
    [ObservableProperty] private string colorHex = info.ColorHex;
}

public partial class GroupEditItem(GroupInfo info) : ObservableObject
{
    public int Id { get; } = info.Id;
    public int GameCount { get; } = info.GameCount;
    public IReadOnlyList<string?> IconPaths { get; } = info.IconPaths;

    [ObservableProperty] private string name = info.Name;
    [ObservableProperty] private string description = info.Description ?? "";
}

public partial class OrganizeViewModel(LibraryService library) : PageViewModel
{
    public IReadOnlyList<string> Palette { get; } =
    [
        "#3B82F6", "#22C55E", "#EF4444", "#F59E0B", "#A855F7",
        "#EC4899", "#14B8A6", "#F97316", "#64748B", "#84CC16",
    ];

    [ObservableProperty] private IReadOnlyList<CategoryEditItem> categories = [];
    [ObservableProperty] private IReadOnlyList<GroupEditItem> groups = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCategoryCommand))]
    private string newCategoryName = "";
    [ObservableProperty] private string newCategoryColor = "#3B82F6";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddGroupCommand))]
    private string newGroupName = "";
    [ObservableProperty] private string newGroupDescription = "";

    public override void Refresh()
    {
        Categories = library.GetCategories().Select(c => new CategoryEditItem(c)).ToList();
        Groups = library.GetGroups().Select(g => new GroupEditItem(g)).ToList();
    }

    [RelayCommand(CanExecute = nameof(CanAddCategory))]
    private void AddCategory()
    {
        library.AddCategory(NewCategoryName, NewCategoryColor);
        NewCategoryName = "";
        Refresh();
    }

    private bool CanAddCategory() => !string.IsNullOrWhiteSpace(NewCategoryName);

    [RelayCommand]
    private void PickNewCategoryColor(string color) => NewCategoryColor = color;

    [RelayCommand]
    private void SaveCategory(CategoryEditItem item)
    {
        library.UpdateCategory(item.Id, item.Name, item.ColorHex);
        Refresh();
    }

    [RelayCommand]
    private void CycleCategoryColor(CategoryEditItem item)
    {
        var index = Palette.ToList().IndexOf(item.ColorHex);
        item.ColorHex = Palette[(index + 1) % Palette.Count];
        library.UpdateCategory(item.Id, item.Name, item.ColorHex);
    }

    [RelayCommand]
    private void DeleteCategory(CategoryEditItem item)
    {
        if (MessageBox.Show($"Supprimer la catégorie « {item.Name} » ? Les jeux ne seront pas supprimés.",
                "Roblox Tracker", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        library.DeleteCategory(item.Id);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanAddGroup))]
    private void AddGroup()
    {
        library.AddGroup(NewGroupName, NewGroupDescription);
        NewGroupName = "";
        NewGroupDescription = "";
        Refresh();
    }

    private bool CanAddGroup() => !string.IsNullOrWhiteSpace(NewGroupName);

    [RelayCommand]
    private void SaveGroup(GroupEditItem item)
    {
        library.UpdateGroup(item.Id, item.Name, item.Description);
        Refresh();
    }

    [RelayCommand]
    private void DeleteGroup(GroupEditItem item)
    {
        if (MessageBox.Show($"Supprimer le groupe « {item.Name} » ? Les jeux ne seront pas supprimés.",
                "Roblox Tracker", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        library.DeleteGroup(item.Id);
        Refresh();
    }
}
