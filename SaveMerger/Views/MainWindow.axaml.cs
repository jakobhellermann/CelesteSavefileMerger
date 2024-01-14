using System;
using Avalonia.Controls;

namespace SaveMerger.Views;

public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();


        // TODO: find a better place for this?
        App.SavefileService.StorageProvider = GetTopLevel(this)?.StorageProvider;
        Console.WriteLine(App.SavefileService.StorageProvider);
    }
}