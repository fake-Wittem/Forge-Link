// 文件说明：提供主窗口导航、定时刷新和服务状态展示数据。
// 责任边界：通过 CollectorApiClient 获取数据，不访问底层存储和设备。

using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLink.Desktop.Models;
using ForgeLink.Desktop.Services;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.ViewModels;

/// <summary>协调主窗口的只读监控状态。</summary>
public partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly CollectorApiClient _apiClient = new();
    private readonly PointCsvFileService _csvFiles = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _refreshLoop;

    [ObservableProperty] private string _currentPage = "Overview";
    [ObservableProperty] private string _serviceState = "正在连接服务…";
    [ObservableProperty] private string _lastRefreshText = "尚未刷新";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _deviceCount;
    [ObservableProperty] private int _onlineDeviceCount;
    [ObservableProperty] private int _enabledPointCount;
    [ObservableProperty] private int _realtimeValueCount;
    [ObservableProperty] private string _successRateText = "—";
    [ObservableProperty] private string _historyStatus = "NotConfigured";
    [ObservableProperty] private string _runtimeText = "—";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _operationMessage = string.Empty;
    [ObservableProperty] private DeviceDefinition? _selectedDevice;
    [ObservableProperty] private PointDefinition? _selectedPoint;
    [ObservableProperty] private bool _isDeviceEditorOpen;
    [ObservableProperty] private bool _isPointEditorOpen;
    [ObservableProperty] private bool _isNewDevice;
    [ObservableProperty] private bool _isNewPoint;
    [ObservableProperty] private bool _isDeviceDeletePending;
    [ObservableProperty] private bool _isPointDeletePending;
    [ObservableProperty] private Guid _editDeviceId;
    [ObservableProperty] private string _editDeviceName = string.Empty;
    [ObservableProperty] private string _editDeviceProtocol = "Simulation";
    [ObservableProperty] private string _editDeviceHost = "127.0.0.1";
    [ObservableProperty] private int _editDevicePort = 502;
    [ObservableProperty] private int _editDeviceIntervalMs = 1000;
    [ObservableProperty] private int _editDeviceUnitId = 1;
    [ObservableProperty] private int _editDeviceConnectionTimeoutMs = 3000;
    [ObservableProperty] private int _editDeviceReadTimeoutMs = 2000;
    [ObservableProperty] private bool _editDeviceEnabled = true;
    [ObservableProperty] private Guid _editPointId;
    [ObservableProperty] private Guid _editPointDeviceId;
    [ObservableProperty] private string _editPointCode = string.Empty;
    [ObservableProperty] private string _editPointName = string.Empty;
    [ObservableProperty] private string _editPointAddress = string.Empty;
    [ObservableProperty] private PointDataType _editPointDataType = PointDataType.Double;
    [ObservableProperty] private double _editPointScale = 1D;
    [ObservableProperty] private double _editPointOffset;
    [ObservableProperty] private string _editPointUnit = string.Empty;
    [ObservableProperty] private int _editPointIntervalMs = 1000;
    [ObservableProperty] private double _editPointDeadband;
    [ObservableProperty] private HistoryRecordMode _editPointHistoryMode = HistoryRecordMode.ChangeWithHeartbeat;
    [ObservableProperty] private bool _editPointEnabled = true;
    [ObservableProperty] private bool _editPointAllowWrite;
    [ObservableProperty] private RegisterByteOrder _editPointByteOrder = RegisterByteOrder.BigEndian;
    [ObservableProperty] private RegisterWordOrder _editPointWordOrder = RegisterWordOrder.HighWordFirst;
    [ObservableProperty] private int _editPointStringLength;

    /// <summary>获取设备配置的界面快照。</summary>
    public ObservableCollection<DeviceDefinition> Devices { get; } = [];

    /// <summary>获取点位配置的界面快照。</summary>
    public ObservableCollection<PointDefinition> Points { get; } = [];

    /// <summary>获取实时值的界面快照。</summary>
    public ObservableCollection<RealtimeValueDto> RealtimeValues { get; } = [];

    /// <summary>指示是否存在需要展示的服务连接错误。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>指示是否存在需要展示的配置操作消息。</summary>
    public bool HasOperationMessage => !string.IsNullOrWhiteSpace(OperationMessage);

    /// <summary>获取点位数据类型的下拉选项。</summary>
    public IReadOnlyList<PointDataType> PointDataTypes { get; } = Enum.GetValues<PointDataType>();

    /// <summary>获取历史记录模式的下拉选项。</summary>
    public IReadOnlyList<HistoryRecordMode> HistoryModes { get; } = Enum.GetValues<HistoryRecordMode>();

    /// <summary>获取当前已注册驱动的协议选项。</summary>
    public IReadOnlyList<string> Protocols { get; } = ["Simulation", "Modbus TCP"];

    /// <summary>获取寄存器内部字节序选项。</summary>
    public IReadOnlyList<RegisterByteOrder> ByteOrders { get; } = Enum.GetValues<RegisterByteOrder>();

    /// <summary>获取多寄存器字序选项。</summary>
    public IReadOnlyList<RegisterWordOrder> WordOrders { get; } = Enum.GetValues<RegisterWordOrder>();

    /// <summary>在错误文本变化时同步通知 InfoBar 显隐状态。</summary>
    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>在操作消息变化时同步通知 InfoBar 显隐状态。</summary>
    partial void OnOperationMessageChanged(string value) => OnPropertyChanged(nameof(HasOperationMessage));

    /// <summary>启动受生命周期管理的定时刷新。</summary>
    public void Start()
    {
        _refreshLoop ??= RefreshLoopAsync(_lifetime.Token);
    }

    /// <summary>切换当前主页面。</summary>
    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;

    /// <summary>打开空白设备编辑器。</summary>
    [RelayCommand]
    private void NewDevice()
    {
        IsNewDevice = true;
        EditDeviceId = Guid.Empty;
        EditDeviceName = string.Empty;
        EditDeviceProtocol = "Simulation";
        EditDeviceHost = "127.0.0.1";
        EditDevicePort = 502;
        EditDeviceIntervalMs = 1000;
        EditDeviceUnitId = 1;
        EditDeviceConnectionTimeoutMs = 3000;
        EditDeviceReadTimeoutMs = 2000;
        EditDeviceEnabled = true;
        IsDeviceEditorOpen = true;
        IsDeviceDeletePending = false;
    }

    /// <summary>把当前选中设备加载到编辑器。</summary>
    [RelayCommand]
    private void EditDevice()
    {
        if (SelectedDevice is null)
        {
            OperationMessage = "请先选择一台设备。";
            return;
        }
        IsNewDevice = false;
        EditDeviceId = SelectedDevice.Id;
        EditDeviceName = SelectedDevice.Name;
        EditDeviceProtocol = SelectedDevice.Protocol;
        EditDeviceHost = SelectedDevice.Host;
        EditDevicePort = SelectedDevice.Port;
        EditDeviceIntervalMs = SelectedDevice.DefaultScanIntervalMs;
        EditDeviceUnitId = SelectedDevice.UnitId;
        EditDeviceConnectionTimeoutMs = SelectedDevice.ConnectionTimeoutMs;
        EditDeviceReadTimeoutMs = SelectedDevice.ReadTimeoutMs;
        EditDeviceEnabled = SelectedDevice.IsEnabled;
        IsDeviceEditorOpen = true;
        IsDeviceDeletePending = false;
    }

    /// <summary>关闭设备编辑器且不提交更改。</summary>
    [RelayCommand]
    private void CancelDeviceEdit() => IsDeviceEditorOpen = false;

    /// <summary>校验并保存设备配置，然后刷新配置快照。</summary>
    [RelayCommand]
    private async Task SaveDeviceAsync()
    {
        if (EditDeviceUnitId is < byte.MinValue or > byte.MaxValue)
        {
            OperationMessage = "Unit ID 必须在 0 到 255 之间。";
            return;
        }
        DeviceDefinition device = new(EditDeviceId, EditDeviceName.Trim(), EditDeviceProtocol.Trim(), EditDeviceHost.Trim(),
            EditDevicePort, EditDeviceEnabled, EditDeviceIntervalMs, checked((byte)EditDeviceUnitId),
            EditDeviceConnectionTimeoutMs, EditDeviceReadTimeoutMs);
        IReadOnlyList<string> errors = (IsNewDevice ? device with { Id = Guid.NewGuid() } : device).Validate();
        if (errors.Count > 0)
        {
            OperationMessage = string.Join(' ', errors);
            return;
        }
        await RunOperationAsync(async token =>
        {
            await _apiClient.SaveDeviceAsync(device, IsNewDevice, token);
            IsDeviceEditorOpen = false;
            OperationMessage = "设备配置已保存，采集任务正在热加载。";
            await RefreshCoreAsync(includeConfiguration: true, token);
        });
    }

    /// <summary>对当前选中设备执行独立连接测试。</summary>
    [RelayCommand]
    private async Task TestDeviceAsync()
    {
        if (SelectedDevice is null)
        {
            OperationMessage = "请先选择一台设备。";
            return;
        }
        await RunOperationAsync(async token =>
        {
            DeviceConnectionTestDto result = await _apiClient.TestDeviceAsync(SelectedDevice.Id, token);
            OperationMessage = result.Message;
        });
    }

    /// <summary>显示设备删除的界面内二次确认。</summary>
    [RelayCommand]
    private void RequestDeleteDevice()
    {
        if (SelectedDevice is null) OperationMessage = "请先选择一台设备。";
        else IsDeviceDeletePending = true;
    }

    /// <summary>取消设备删除确认。</summary>
    [RelayCommand]
    private void CancelDeleteDevice() => IsDeviceDeletePending = false;

    /// <summary>确认删除没有关联点位的设备。</summary>
    [RelayCommand]
    private async Task ConfirmDeleteDeviceAsync()
    {
        if (SelectedDevice is null) return;
        Guid id = SelectedDevice.Id;
        await RunOperationAsync(async token =>
        {
            await _apiClient.DeleteDeviceAsync(id, token);
            IsDeviceDeletePending = false;
            OperationMessage = "设备已删除。";
            await RefreshCoreAsync(includeConfiguration: true, token);
        });
    }

    /// <summary>打开空白点位编辑器。</summary>
    [RelayCommand]
    private void NewPoint()
    {
        if (Devices.Count == 0)
        {
            OperationMessage = "请先创建设备。";
            return;
        }
        IsNewPoint = true;
        EditPointId = Guid.Empty;
        EditPointDeviceId = Devices[0].Id;
        EditPointCode = string.Empty;
        EditPointName = string.Empty;
        EditPointAddress = string.Empty;
        EditPointDataType = PointDataType.Double;
        EditPointScale = 1D;
        EditPointOffset = 0D;
        EditPointUnit = string.Empty;
        EditPointIntervalMs = 1000;
        EditPointDeadband = 0D;
        EditPointHistoryMode = HistoryRecordMode.ChangeWithHeartbeat;
        EditPointEnabled = true;
        EditPointAllowWrite = false;
        EditPointByteOrder = RegisterByteOrder.BigEndian;
        EditPointWordOrder = RegisterWordOrder.HighWordFirst;
        EditPointStringLength = 0;
        IsPointEditorOpen = true;
        IsPointDeletePending = false;
    }

    /// <summary>把当前选中点位加载到编辑器。</summary>
    [RelayCommand]
    private void EditPoint()
    {
        if (SelectedPoint is null)
        {
            OperationMessage = "请先选择一个点位。";
            return;
        }
        IsNewPoint = false;
        EditPointId = SelectedPoint.Id;
        EditPointDeviceId = SelectedPoint.DeviceId;
        EditPointCode = SelectedPoint.Code;
        EditPointName = SelectedPoint.Name;
        EditPointAddress = SelectedPoint.Address;
        EditPointDataType = SelectedPoint.DataType;
        EditPointScale = SelectedPoint.Scale;
        EditPointOffset = SelectedPoint.Offset;
        EditPointUnit = SelectedPoint.Unit;
        EditPointIntervalMs = SelectedPoint.ScanIntervalMs;
        EditPointDeadband = SelectedPoint.Deadband;
        EditPointHistoryMode = SelectedPoint.HistoryMode;
        EditPointEnabled = SelectedPoint.IsEnabled;
        EditPointAllowWrite = SelectedPoint.AllowWrite;
        EditPointByteOrder = SelectedPoint.ByteOrder;
        EditPointWordOrder = SelectedPoint.WordOrder;
        EditPointStringLength = SelectedPoint.StringLength;
        IsPointEditorOpen = true;
        IsPointDeletePending = false;
    }

    /// <summary>关闭点位编辑器且不提交更改。</summary>
    [RelayCommand]
    private void CancelPointEdit() => IsPointEditorOpen = false;

    /// <summary>校验并保存点位配置，然后刷新配置快照。</summary>
    [RelayCommand]
    private async Task SavePointAsync()
    {
        PointDefinition point = new(EditPointId, EditPointDeviceId, EditPointCode.Trim(), EditPointName.Trim(),
            EditPointAddress.Trim(), EditPointDataType, EditPointScale, EditPointOffset, EditPointUnit.Trim(),
            EditPointIntervalMs, EditPointDeadband, EditPointHistoryMode, EditPointEnabled, EditPointAllowWrite,
            EditPointByteOrder, EditPointWordOrder, EditPointStringLength);
        IReadOnlyList<string> errors = (IsNewPoint ? point with { Id = Guid.NewGuid() } : point).Validate();
        if (errors.Count > 0)
        {
            OperationMessage = string.Join(' ', errors);
            return;
        }
        await RunOperationAsync(async token =>
        {
            await _apiClient.SavePointAsync(point, IsNewPoint, token);
            IsPointEditorOpen = false;
            OperationMessage = "点位配置已保存，采集任务正在热加载。";
            await RefreshCoreAsync(includeConfiguration: true, token);
        });
    }

    /// <summary>显示点位删除的界面内二次确认。</summary>
    [RelayCommand]
    private void RequestDeletePoint()
    {
        if (SelectedPoint is null) OperationMessage = "请先选择一个点位。";
        else IsPointDeletePending = true;
    }

    /// <summary>取消点位删除确认。</summary>
    [RelayCommand]
    private void CancelDeletePoint() => IsPointDeletePending = false;

    /// <summary>确认删除当前选中点位。</summary>
    [RelayCommand]
    private async Task ConfirmDeletePointAsync()
    {
        if (SelectedPoint is null) return;
        Guid id = SelectedPoint.Id;
        await RunOperationAsync(async token =>
        {
            await _apiClient.DeletePointAsync(id, token);
            IsPointDeletePending = false;
            OperationMessage = "点位已删除。";
            await RefreshCoreAsync(includeConfiguration: true, token);
        });
    }

    /// <summary>选择文件并导出当前点位配置 CSV。</summary>
    [RelayCommand]
    private async Task ExportPointsAsync() => await RunOperationAsync(async token =>
    {
        byte[] csv = await _apiClient.ExportPointsAsync(token);
        if (await _csvFiles.SaveAsync(csv, token)) OperationMessage = "点位 CSV 已导出。";
    });

    /// <summary>选择 CSV 文件并提交给服务端导入。</summary>
    [RelayCommand]
    private async Task ImportPointsAsync() => await RunOperationAsync(async token =>
    {
        string? csv = await _csvFiles.OpenAsync(token);
        if (csv is null) return;
        int count = await _apiClient.ImportPointsAsync(csv, token);
        OperationMessage = $"已导入 {count} 个点位，采集任务正在热加载。";
        await RefreshCoreAsync(includeConfiguration: true, token);
    });

    /// <summary>立即从服务端刷新全部只读页面数据。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RefreshCoreAsync(includeConfiguration: true, _lifetime.Token);
    }

    /// <summary>按需刷新配置，并始终刷新运行状态和实时值。</summary>
    private async Task RefreshCoreAsync(bool includeConfiguration, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            StatusDto status = await _apiClient.GetStatusAsync(timeout.Token);
            IReadOnlyList<RealtimeValueDto> values = await _apiClient.GetRealtimeAsync(timeout.Token);

            DeviceCount = status.DeviceCount;
            OnlineDeviceCount = status.OnlineDeviceCount;
            EnabledPointCount = status.EnabledPointCount;
            RealtimeValueCount = status.RealtimeValueCount;
            SuccessRateText = status.SuccessRate.ToString("P1", System.Globalization.CultureInfo.CurrentCulture);
            HistoryStatus = status.HistoryStatus;
            RuntimeText = FormatRuntime(DateTimeOffset.UtcNow - status.StartedAtUtc);
            if (includeConfiguration)
            {
                IReadOnlyList<DeviceDefinition> devices = await _apiClient.GetDevicesAsync(timeout.Token);
                IReadOnlyList<PointDefinition> points = await _apiClient.GetPointsAsync(timeout.Token);
                Replace(Devices, devices);
                Replace(Points, points);
                SelectedDevice = null;
                SelectedPoint = null;
            }
            Replace(RealtimeValues, values);
            ServiceState = "Collector Service 运行中";
            ErrorMessage = string.Empty;
            LastRefreshText = $"最后刷新：{DateTimeOffset.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            ServiceState = "Collector Service 响应超时";
            ErrorMessage = "本机服务在 5 秒内未响应，请检查服务状态。";
        }
        catch (HttpRequestException)
        {
            ServiceState = "Collector Service 未连接";
            ErrorMessage = "请先启动 ForgeLink.Collector.Service，再刷新管理端。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>按固定频率刷新界面快照，避免 UI 消费每个采集事件。</summary>
    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
            bool includeConfiguration = true;
            do
            {
                await RefreshCoreAsync(includeConfiguration, cancellationToken).ConfigureAwait(true);
                includeConfiguration = false;
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 窗口关闭时取消属于正常生命周期结束，不应显示为运行错误。
        }
    }

    /// <summary>统一执行管理操作并把服务端错误转换为界面消息。</summary>
    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await operation(timeout.Token);
        }
        catch (CollectorApiException exception)
        {
            OperationMessage = exception.Message;
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            OperationMessage = "操作已取消或超时。";
        }
        catch (IOException exception)
        {
            OperationMessage = $"文件操作失败：{exception.Message}";
        }
    }

    /// <summary>替换可观察集合内容，确保数据绑定收到批量刷新结果。</summary>
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source) target.Add(item);
    }

    /// <summary>把运行时长格式化为紧凑中文文本。</summary>
    private static string FormatRuntime(TimeSpan runtime) => runtime.TotalDays >= 1
        ? $"{(int)runtime.TotalDays} 天 {runtime.Hours} 小时"
        : $"{runtime.Hours} 小时 {runtime.Minutes} 分钟 {runtime.Seconds} 秒";

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        if (_refreshLoop is not null)
        {
            try { await _refreshLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _lifetime.Dispose();
        _apiClient.Dispose();
    }
}
