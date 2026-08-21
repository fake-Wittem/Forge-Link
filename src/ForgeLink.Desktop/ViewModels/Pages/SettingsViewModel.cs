// 文件说明：管理 TDengine 连接表单、完整测试和历史通道启停操作。
// 责任边界：只通过 Collector Service API 工作，不接触 SQLite、DPAPI 或 TDengine 客户端。

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLink.Desktop.Models;
using ForgeLink.Desktop.Services;

namespace ForgeLink.Desktop.ViewModels.Pages;

/// <summary>表示系统设置页中的 TDengine 历史配置。</summary>
public partial class SettingsViewModel(ICollectorApiClient apiClient, NotificationService notification)
    : PageViewModelBase(notification)
{
    [ObservableProperty] private string _host = "127.0.0.1";
    [ObservableProperty] private string _port = "6041";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _database = string.Empty;
    [ObservableProperty] private bool _useSsl;
    [ObservableProperty] private bool _enableCompression = true;
    [ObservableProperty] private bool _autoReconnect = true;
    [ObservableProperty] private string _requestTimeoutSeconds = "10";
    [ObservableProperty] private string _state = "NotConfigured";
    [ObservableProperty] private bool _hasSavedPassword;
    [ObservableProperty] private bool _isBusy;

    public NotificationService Messages => Notification;
    public string PasswordHint => HasSavedPassword ? "留空表示保留已加密保存的密码" : "首次配置必须输入密码";
    public bool CanEnable => State == "Ready" && !IsBusy;
    public bool CanDisable => State is "Enabled" or "Ready" && !IsBusy;

    partial void OnHasSavedPasswordChanged(bool value) => OnPropertyChanged(nameof(PasswordHint));
    partial void OnStateChanged(string value)
    {
        OnPropertyChanged(nameof(CanEnable));
        OnPropertyChanged(nameof(CanDisable));
    }
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEnable));
        OnPropertyChanged(nameof(CanDisable));
    }

    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken) =>
        await RunOperationAsync(LoadAsync, cancellationToken);

    [RelayCommand]
    private async Task SaveAsync() => await RunOperationAsync(async token =>
    {
        if (!TryBuild(out HistoryConfigurationUpdateDto? configuration, out string error))
        {
            Notification.Show(error);
            return;
        }

        IsBusy = true;
        try
        {
            HistoryConfigurationDto saved = await apiClient.SaveHistoryConfigurationAsync(configuration!, token);
            Apply(saved);
            Password = string.Empty;
            Notification.Show("TDengine 配置已加密保存。配置变化已关闭历史，请重新执行完整测试。");
        }
        finally { IsBusy = false; }
    });

    [RelayCommand]
    private async Task TestAsync() => await RunOperationAsync(async token =>
    {
        IsBusy = true;
        try
        {
            HistoryOperationDto result = await apiClient.TestHistoryAsync(token);
            State = result.State;
            Notification.Show(result.Message);
        }
        finally { IsBusy = false; }
    }, timeout: TimeSpan.FromSeconds(125));

    [RelayCommand]
    private async Task EnableAsync() => await RunOperationAsync(async token =>
    {
        IsBusy = true;
        try
        {
            HistoryOperationDto result = await apiClient.EnableHistoryAsync(token);
            State = result.State;
            Notification.Show(result.Message);
        }
        finally { IsBusy = false; }
    });

    [RelayCommand]
    private async Task DisableAsync() => await RunOperationAsync(async token =>
    {
        IsBusy = true;
        try
        {
            HistoryOperationDto result = await apiClient.DisableHistoryAsync(token);
            State = result.State;
            Notification.Show(result.Message);
        }
        finally { IsBusy = false; }
    });

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try { Apply(await apiClient.GetHistoryConfigurationAsync(cancellationToken)); }
        finally { IsBusy = false; }
    }

    private void Apply(HistoryConfigurationDto configuration)
    {
        Host = configuration.Host;
        Port = configuration.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Username = configuration.Username;
        Database = configuration.Database;
        UseSsl = configuration.UseSsl;
        EnableCompression = configuration.EnableCompression;
        AutoReconnect = configuration.AutoReconnect;
        RequestTimeoutSeconds = (configuration.RequestTimeoutMs / 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        State = configuration.State;
        HasSavedPassword = configuration.HasPassword;
    }

    private bool TryBuild(out HistoryConfigurationUpdateDto? configuration, out string error)
    {
        configuration = null;
        if (!int.TryParse(Port, out int port) || port is < 1 or > 65535)
        {
            error = "WebSocket 端口必须是 1 到 65535 之间的整数。";
            return false;
        }
        if (!double.TryParse(RequestTimeoutSeconds, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double seconds) || seconds is < 1 or > 120)
        {
            error = "请求超时必须是 1 到 120 秒之间的数值。";
            return false;
        }
        configuration = new(Host.Trim(), port, Username.Trim(), string.IsNullOrEmpty(Password) ? null : Password,
            Database.Trim(), UseSsl, EnableCompression, AutoReconnect, checked((int)(seconds * 1000)));
        error = string.Empty;
        return true;
    }
}
