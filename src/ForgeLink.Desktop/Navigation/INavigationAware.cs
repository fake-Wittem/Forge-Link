// 文件说明：声明菜单页面进入和离开时的异步生命周期。
// 责任边界：用于按页面启停刷新，不依赖具体导航控件。

namespace ForgeLink.Desktop.Navigation;

/// <summary>定义页面导航生命周期。</summary>
public interface INavigationAware
{
    Task OnNavigatedToAsync(CancellationToken cancellationToken);
    Task OnNavigatedFromAsync(CancellationToken cancellationToken);
}
