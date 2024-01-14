using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using SaveMerger.Services;
using SaveMerger.ViewModels;
using SaveMerger.Views;

namespace SaveMerger;

public class App : Application {
    public static SavefileService SavefileService { get; } = new();

    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            // Line below is needed to remove Avalonia data validation.
            // Without this line you will get duplicate validations from both Avalonia and CT
            BindingPlugins.DataValidators.RemoveAt(0);
            desktop.MainWindow = new MainWindow {
                DataContext = new MainWindowViewModel(SavefileService),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}