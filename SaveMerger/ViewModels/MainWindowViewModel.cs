using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using SaveMerger.Services;

namespace SaveMerger.ViewModels;

public enum TabIndex {
    Select,
    Merge,
    Save,
}

public partial class MainWindowViewModel : ViewModelBase {
    [NotifyPropertyChangedFor(nameof(TabMergeEnabled), nameof(TabSaveEnabled))] [ObservableProperty]
    private TabIndex _tabIndex = TabIndex.Select;

    public bool TabMergeEnabled => TabIndex >= TabIndex.Merge || EnoughSelectedToMerge;
    public bool TabSaveEnabled => TabIndex >= TabIndex.Save;

    // Select Tab
    public ObservableCollection<Savefile> Savefiles { get; private set; } = [];
    public SelectionModel<Savefile> Selection { get; } = new();
    public bool EnoughSelectedToMerge => Selection.Count >= 2;

    // Merge Tab

    // Save Tab

    // Services

    private readonly ISavefileService _savefileService;

    public void Merge() {
        TabIndex = TabIndex.Merge;

        var saves = Selection.SelectedItems
            .Select(savefile => savefile.Document);

        try {
            var (result, resolutions) = CelesteSaveMerger.SaveMerger.Merge(saves);
            Console.WriteLine(resolutions.Count);
        } catch (Exception e) {
            Console.WriteLine(e);
        }
    }


    private void LoadSavefiles() {
        Savefiles = new ObservableCollection<Savefile>(_savefileService.List());
    }

    public MainWindowViewModel(ISavefileService savefileService) {
        _savefileService = savefileService;
        LoadSavefiles();

        Selection.SelectionChanged += (_, _) => {
            OnPropertyChanged(nameof(EnoughSelectedToMerge));
            OnPropertyChanged(nameof(TabMergeEnabled));
        };
        Selection.SingleSelect = false;
    }

    public MainWindowViewModel() : this(new DummySavefileService()) {
    }
}