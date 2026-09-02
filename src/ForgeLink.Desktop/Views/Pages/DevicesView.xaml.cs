// 文件说明：设备页面视图初始化与模态遮罩交互。
// 责任边界：仅识别遮罩背景点击，关闭行为仍由页面命令处理。
using System.Windows.Input;
using ForgeLink.Desktop.ViewModels.Pages;

namespace ForgeLink.Desktop.Views.Pages;

public partial class DevicesView
{
    public DevicesView() => InitializeComponent();

    /// <summary>仅点击弹窗外的遮罩背景时关闭设备配置。</summary>
    private void ModalOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender) || DataContext is not DevicesViewModel viewModel) return;
        if (viewModel.CancelDeviceEditCommand.CanExecute(null)) viewModel.CancelDeviceEditCommand.Execute(null);
    }
}
