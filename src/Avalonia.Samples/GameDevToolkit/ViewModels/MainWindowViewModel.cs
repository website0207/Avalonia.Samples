using ReactiveUI;

namespace GameDevToolkit.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private int _selectedTabIndex = 0;
    private PerforceToolsViewModel _perforceToolsViewModel;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
    }

    public PerforceToolsViewModel PerforceToolsViewModel => _perforceToolsViewModel;

    public MainWindowViewModel()
    {
        // 初始化Perforce工具ViewModel
        _perforceToolsViewModel = new PerforceToolsViewModel();
    }
}