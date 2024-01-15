using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Controls.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using SaveMerger.Services;

namespace SaveMerger.ViewModels;

public enum TabIndex {
    Select,
    Merge,
    Save,
}

public partial class Resolution : ObservableObject {
    public required string Path { get; init; }
    public required string Values { get; init; }
    [ObservableProperty] private string _newText = "";
}

public partial class MainWindowViewModel : ViewModelBase {
    [NotifyPropertyChangedFor(nameof(TabMergeEnabled), nameof(TabSaveEnabled))] [ObservableProperty]
    private TabIndex _tabIndex = TabIndex.Select;

    public bool TabMergeEnabled => TabIndex >= TabIndex.Merge;
    public bool TabSaveEnabled => TabIndex >= TabIndex.Save;

    [ObservableProperty] private string? _error;

    // Select Tab
    public ObservableCollection<Savefile> Savefiles { get; private set; } = [];
    public SelectionModel<Savefile> Selection { get; } = new();
    public bool EnoughSelectedToMerge => Selection.Count >= 1;

    // Merge Tab
    public ObservableCollection<Resolution> Resolutions { get; } = [];
    public bool ResolutionsResolved => Resolutions.All(item => item.NewText.Length > 0);
    private XDocument? _document;

    // Save Tab
    [ObservableProperty] private string? _mergedXml;

    // Services
    private readonly ISavefileService _savefileService;


    // Commands
    public void Merge() {
        Error = "";

        TryError(() => {
            var saves = Selection.SelectedItems.Select(savefile => savefile.Document);
            var (result, resolutions, errors) = CelesteSaveMerger.SaveMerger.Merge(saves);
            _document = result;

            if (errors.Count > 0) {
                Error = string.Join('\n', errors);
                return;
            }

            Resolutions.Clear();
            foreach (var resolution in resolutions) {
                var resolutionItem = new Resolution {
                    Path = resolution.Path,
                    Values = string.Join(", ", resolution.Values.Distinct()),
                };
                resolutionItem.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ResolutionsResolved));
                Resolutions.Add(resolutionItem);
            }

            TabIndex = TabIndex.Merge;
        });
    }

    public void Resolve() {
        Error = "";

        TryError(() => {
            var xmlText = CelesteSaveMerger.SaveMerger.Resolve(_document!,
                Resolutions.Select(resolution => new CelesteSaveMerger.Resolution {
                    Path = resolution.Path,
                    NewValue = resolution.NewText,
                }));
            MergedXml = xmlText;

            TabIndex = TabIndex.Save;
        });
    }

    public async void Save() {
        var text = MergedXml!;

        var joined = string.Join('+', Selection.SelectedItems.Select(savefile => savefile.Index));
        var path = await _savefileService.Save(text, joined + ".celeste");
        if (path is null) return;

        var proc = new Process();
        proc.StartInfo = new ProcessStartInfo { UseShellExecute = true, FileName = path };
        proc.Start();
        await proc.WaitForExitAsync();
    }

    private void LoadSavefiles() {
        Savefiles = new ObservableCollection<Savefile>(_savefileService.List());
    }

    // Construtor
    public MainWindowViewModel(ISavefileService savefileService) {
        _savefileService = savefileService;
        LoadSavefiles();

        Selection.SelectionChanged += (_, _) => {
            OnPropertyChanged(nameof(EnoughSelectedToMerge));
            OnPropertyChanged(nameof(TabMergeEnabled));
        };
        Selection.SingleSelect = false;

        Resolutions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ResolutionsResolved));
    }

    public MainWindowViewModel() : this(new DummySavefileService()) {
        TabIndex = TabIndex.Save;

        Resolutions.Add(new Resolution { Path = "Name", Values = "Madeline, Archie" });
        Resolutions.Add(new Resolution { Path = "AssistMode", Values = "true, false" });
        Resolutions.Add(new Resolution { Path = "Assists/GameSpeed", Values = "8, 12" });

        MergedXml = "<xml>";
    }

    private void TryError(Action f) {
        try {
            f();
        } catch (Exception e) {
            Error = e.ToString();
        }
    }
}