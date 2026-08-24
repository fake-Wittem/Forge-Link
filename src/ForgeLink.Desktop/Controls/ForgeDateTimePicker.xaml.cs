// 文件说明：实现合并日期时间选择器的浮层状态和双向绑定。
// 责任边界：只产生 DateTime 本地值，不连接服务或执行历史查询。

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace ForgeLink.Desktop.Controls;

/// <summary>提供日期、小时、分钟和秒的一体化选择体验。</summary>
public partial class ForgeDateTimePicker : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty SelectedDateTimeProperty = DependencyProperty.Register(
        nameof(SelectedDateTime), typeof(DateTime?), typeof(ForgeDateTimePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateTimeChanged));

    private DateTime? _pendingDate;
    private int _pendingHour;
    private int _pendingMinute;
    private int _pendingSecond;

    public ForgeDateTimePicker()
    {
        InitializeComponent();
        Hours = Enumerable.Range(0, 24).ToArray();
        Minutes = Enumerable.Range(0, 60).ToArray();
        Seconds = Enumerable.Range(0, 60).ToArray();
        PickerPopup.DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>获取或设置已经确认的系统本地日期时间。</summary>
    public DateTime? SelectedDateTime
    {
        get => (DateTime?)GetValue(SelectedDateTimeProperty);
        set => SetValue(SelectedDateTimeProperty, value);
    }

    public IReadOnlyList<int> Hours { get; }
    public IReadOnlyList<int> Minutes { get; }
    public IReadOnlyList<int> Seconds { get; }
    public string DisplayText => SelectedDateTime?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "请选择日期时间";

    public DateTime? PendingDate { get => _pendingDate; set => SetField(ref _pendingDate, value); }
    public int PendingHour { get => _pendingHour; set => SetField(ref _pendingHour, value); }
    public int PendingMinute { get => _pendingMinute; set => SetField(ref _pendingMinute, value); }
    public int PendingSecond { get => _pendingSecond; set => SetField(ref _pendingSecond, value); }

    /// <summary>打开浮层前复制当前值，取消操作不会污染已确认时间。</summary>
    private void OpenPicker_Click(object sender, RoutedEventArgs e)
    {
        SetPending(SelectedDateTime ?? DateTime.Now);
        PickerPopup.IsOpen = true;
    }

    /// <summary>把待选值定位到系统当前时间。</summary>
    private void UseNow_Click(object sender, RoutedEventArgs e) => SetPending(DateTime.Now);

    /// <summary>关闭浮层并放弃本次修改。</summary>
    private void Cancel_Click(object sender, RoutedEventArgs e) => PickerPopup.IsOpen = false;

    /// <summary>组合日期和时分秒，提交给双向绑定属性。</summary>
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (PendingDate is null) return;
        SelectedDateTime = PendingDate.Value.Date
            .AddHours(PendingHour).AddMinutes(PendingMinute).AddSeconds(PendingSecond);
        PickerPopup.IsOpen = false;
    }

    private void SetPending(DateTime value)
    {
        PendingDate = value.Date;
        PendingHour = value.Hour;
        PendingMinute = value.Minute;
        PendingSecond = value.Second;
    }

    private static void OnSelectedDateTimeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ForgeDateTimePicker picker = (ForgeDateTimePicker)dependencyObject;
        picker.OnPropertyChanged(nameof(DisplayText));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
