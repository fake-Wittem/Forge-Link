// 文件说明：为尚未进入实施阶段的菜单页提供独立 ViewModel 类型。
// 责任边界：仅建立页面边界，后续功能在对应类型中扩展。

using ForgeLink.Desktop.Services;

namespace ForgeLink.Desktop.ViewModels.Pages;

public sealed class HistoryViewModel(NotificationService notification) : PageViewModelBase(notification);
public sealed class TransportViewModel(NotificationService notification) : PageViewModelBase(notification);
public sealed class AlarmsViewModel(NotificationService notification) : PageViewModelBase(notification);
public sealed class SettingsViewModel(NotificationService notification) : PageViewModelBase(notification);
