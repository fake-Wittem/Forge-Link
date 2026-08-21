// 文件说明：提供历史点位选择、时间范围校验、查询和趋势数据。
// 责任边界：只调用 Collector Service，不直接连接 TDengine。

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLink.Desktop.Services;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.ViewModels.Pages;

public partial class HistoryViewModel(ICollectorApiClient apiClient, NotificationService notification)
    : PageViewModelBase(notification)
{
    [ObservableProperty] private PointDefinition? _selectedPoint;
    [ObservableProperty] private string _fromLocal = DateTimeOffset.Now.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    [ObservableProperty] private string _toLocal = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    [ObservableProperty] private string _resultSummary = "请选择点位并查询";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<PointDefinition> Points { get; } = [];
    public ObservableCollection<PointValue> Values { get; } = [];
    public NotificationService Messages => Notification;

    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken) =>
        await RunOperationAsync(LoadPointsAsync, cancellationToken);

    [RelayCommand]
    private async Task QueryAsync() => await RunOperationAsync(async token =>
    {
        if (SelectedPoint is null) { Notification.Show("请先选择一个历史点位。"); return; }
        if (!DateTimeOffset.TryParse(FromLocal, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset from)
            || !DateTimeOffset.TryParse(ToLocal, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset to))
        {
            Notification.Show("时间格式无效，请使用 yyyy-MM-dd HH:mm:ss。");
            return;
        }
        if (from > to || to - from > TimeSpan.FromDays(7))
        {
            Notification.Show("查询开始时间不得晚于结束时间，单次范围不得超过 7 天。");
            return;
        }

        IsBusy = true;
        try
        {
            IReadOnlyList<PointValue> values = await apiClient.QueryHistoryAsync([SelectedPoint.Id], from, to, 10_000, token);
            Values.Clear();
            foreach (PointValue value in values) Values.Add(value);
            ResultSummary = $"返回 {values.Count:N0} 条 · {SelectedPoint.Name}";
        }
        finally { IsBusy = false; }
    }, timeout: TimeSpan.FromSeconds(35));

    private async Task LoadPointsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PointDefinition> points = await apiClient.GetPointsAsync(cancellationToken);
        Points.Clear();
        foreach (PointDefinition point in points.Where(static point => point.IsEnabled && point.HistoryMode != HistoryRecordMode.None)) Points.Add(point);
        SelectedPoint = Points.FirstOrDefault();
    }
}
