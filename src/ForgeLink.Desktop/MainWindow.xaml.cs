// 文件说明：处理主窗口纯生命周期事件并托管视图模型。
// 责任边界：不包含采集、配置、存储或导航业务逻辑。

using ForgeLink.Desktop.ViewModels;

namespace ForgeLink.Desktop;

/// <summary>表示 ForgeLink 桌面管理主窗口。</summary>
public partial class MainWindow
{
    private readonly MainViewModel _viewModel = new();

    /// <summary>初始化窗口并绑定视图模型。</summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += HandleLoaded;
        Closed += HandleClosed;
    }

    /// <summary>在窗口完成加载后启动周期刷新。</summary>
    private void HandleLoaded(object sender, System.Windows.RoutedEventArgs eventArgs) => _viewModel.Start();

    /// <summary>在窗口关闭时释放刷新循环和网络资源。</summary>
    private async void HandleClosed(object? sender, EventArgs eventArgs) => await _viewModel.DisposeAsync();
}
