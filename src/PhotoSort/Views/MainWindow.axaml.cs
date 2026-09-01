using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using PhotoSort.Models;
using PhotoSort.ViewModels;

namespace PhotoSort.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// All shortcuts are handled here rather than as key bindings, so that Space and the arrow
    /// keys never reach a focused button and trigger it instead.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            base.OnKeyDown(e);
            return;
        }

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Left or Key.PageUp:
                vm.PreviousCommand.Execute(null);
                break;
            case Key.Right or Key.PageDown:
                vm.NextCommand.Execute(null);
                break;
            case Key.Home:
                vm.FirstCommand.Execute(null);
                break;
            case Key.End:
                vm.LastCommand.Execute(null);
                break;
            case Key.E or Key.Space:
                vm.CategoriseCommand.Execute(PhotoCategory.Edit);
                break;
            case Key.A or Key.K:
                vm.CategoriseCommand.Execute(PhotoCategory.Archive);
                break;
            case Key.D or Key.Delete:
                vm.CategoriseCommand.Execute(PhotoCategory.Delete);
                break;
            case Key.R:
                vm.CategoriseCommand.Execute(PhotoCategory.None);
                break;
            case Key.Tab:
                vm.NextVariantCommand.Execute(null);
                break;
            case Key.Back:
            case Key.Z when control:
                vm.UndoCommand.Execute(null);
                break;
            case Key.O:
                vm.ChooseFolderCommand.Execute(null);
                break;
            case Key.F11:
                WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
                break;
            default:
                base.OnKeyDown(e);
                return;
        }

        e.Handled = true;
    }
}
