// 文件说明：按点位历史模式判断当前采集值是否需要记录。
// 责任边界：只维护进程内最近记录状态，不写数据库或缓冲队列。

using System.Globalization;
using ForgeLink.Domain;

namespace ForgeLink.Application;

/// <summary>实现每次、变化、死区、周期快照和变化加心跳策略。</summary>
public sealed class HistoryRecordPolicy(TimeSpan? snapshotInterval = null)
{
    private readonly TimeSpan _snapshotInterval = snapshotInterval ?? TimeSpan.FromSeconds(60);
    private readonly Dictionary<Guid, RecordedState> _states = [];

    public bool ShouldRecord(PointDefinition definition, PointValue value)
    {
        if (definition.HistoryMode == HistoryRecordMode.None) return false;
        bool hasPrevious = _states.TryGetValue(value.PointId, out RecordedState? previous);
        bool qualityChanged = hasPrevious && previous!.Quality != value.Quality;
        bool heartbeatDue = !hasPrevious || value.CollectTimestampUtc - previous!.RecordedAtUtc >= _snapshotInterval;
        bool changed = !hasPrevious || qualityChanged || !ValuesEqual(previous!.EngineeringValue, value.EngineeringValue);
        bool deadbandExceeded = !hasPrevious || qualityChanged || ExceedsDeadband(previous!.EngineeringValue, value.EngineeringValue, definition.Deadband);

        bool shouldRecord = value.Quality != DataQuality.Good || definition.HistoryMode switch
        {
            HistoryRecordMode.EverySample => true,
            HistoryRecordMode.OnChange => changed,
            HistoryRecordMode.Deadband => deadbandExceeded,
            HistoryRecordMode.PeriodicSnapshot => heartbeatDue,
            HistoryRecordMode.ChangeWithHeartbeat => deadbandExceeded || heartbeatDue,
            _ => false
        };
        if (shouldRecord) _states[value.PointId] = new(value.EngineeringValue, value.Quality, value.CollectTimestampUtc);
        return shouldRecord;
    }

    public void Reset(Guid pointId) => _states.Remove(pointId);
    public void ResetAll() => _states.Clear();

    private static bool ExceedsDeadband(object? previous, object? current, double deadband)
    {
        if (previous is null || current is null) return !Equals(previous, current);
        if (TryNumber(previous, out double left) && TryNumber(current, out double right))
            return Math.Abs(right - left) > deadband;
        return !ValuesEqual(previous, current);
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is null || right is null) return Equals(left, right);
        if (TryNumber(left, out double leftNumber) && TryNumber(right, out double rightNumber))
            return leftNumber.Equals(rightNumber);
        return Equals(left, right);
    }

    private static bool TryNumber(object value, out double number)
    {
        if (value is bool or string)
        {
            number = 0;
            return false;
        }
        try
        {
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(number);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            number = 0;
            return false;
        }
    }

    private sealed record RecordedState(object? EngineeringValue, DataQuality Quality, DateTimeOffset RecordedAtUtc);
}
