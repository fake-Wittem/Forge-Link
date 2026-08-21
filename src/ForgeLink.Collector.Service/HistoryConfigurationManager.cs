// 文件说明：协调 TDengine 配置持久化、通道替换和历史门禁状态。
// 责任边界：不参与采集批量写入，不向客户端返回明文密码。

using ForgeLink.Application;
using ForgeLink.History.Abstractions;
using ForgeLink.History.TDengine;

/// <summary>串行化历史配置管理操作，保证配置、测试和启停状态一致。</summary>
internal sealed class HistoryConfigurationManager(
    IConfigurationRepository repository,
    SwitchableHistoryChannel channel,
    HistoryGate gate,
    HistoryBuffer buffer)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
        TDengineConnectionConfiguration? configuration = await repository.GetTDengineConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (configuration is null)
        {
            gate.Restore(false, false, false);
            return;
        }

        channel.Replace(CreateChannel(configuration));
        gate.Restore(true, configuration.TestPassed, configuration.IsEnabled);
    }

    public async Task<HistoryConfigurationResponse> GetAsync(CancellationToken cancellationToken)
    {
        TDengineConnectionConfiguration? configuration = await repository.GetTDengineConfigurationAsync(cancellationToken).ConfigureAwait(false);
        return HistoryConfigurationResponse.From(configuration, gate.State);
    }

    public async Task<(HistoryConfigurationResponse? Response, IReadOnlyList<string> Errors)> SaveAsync(
        HistoryConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TDengineConnectionConfiguration? existing = await repository.GetTDengineConfigurationAsync(cancellationToken).ConfigureAwait(false);
            string password = string.IsNullOrEmpty(request.Password) ? existing?.Password ?? string.Empty : request.Password;
            TDengineConnectionConfiguration configuration = request.ToConfiguration(password);
            TDengineOptions options = ToOptions(configuration);
            IReadOnlyList<string> errors = options.Validate();
            if (errors.Count > 0) return (null, errors);

            gate.MarkConfigurationChanged();
            buffer.DiscardAll();
            await repository.SaveTDengineConfigurationAsync(configuration, cancellationToken).ConfigureAwait(false);
            channel.Replace(new TDengineHistoryChannel(options));
            return (HistoryConfigurationResponse.From(configuration with { TestPassed = false, IsEnabled = false }, gate.State), []);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<HistoryOperationResponse> TestAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TDengineConnectionConfiguration? configuration = await repository.GetTDengineConfigurationAsync(cancellationToken).ConfigureAwait(false);
            if (configuration is null) return new(false, "请先保存完整的 TDengine 连接配置。", gate.State.ToString());
            channel.Replace(CreateChannel(configuration));
            gate.MarkConfigured();
            buffer.DiscardAll();
            HistoryChannelTestResult result = await gate.TestAsync(cancellationToken).ConfigureAwait(false);
            await repository.SetHistoryGateStateAsync(result.Succeeded, false, cancellationToken).ConfigureAwait(false);
            return new(result.Succeeded, result.Message, gate.State.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<HistoryOperationResponse> EnableAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                gate.Enable();
            }
            catch (InvalidOperationException exception)
            {
                return new(false, exception.Message, gate.State.ToString());
            }
            await repository.SetHistoryGateStateAsync(true, true, cancellationToken).ConfigureAwait(false);
            return new(true, "TDengine 历史通道已启用。", gate.State.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<HistoryOperationResponse> DisableAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TDengineConnectionConfiguration? configuration = await repository.GetTDengineConfigurationAsync(cancellationToken).ConfigureAwait(false);
            if (configuration is null) return new(false, "TDengine 连接配置尚未保存。", gate.State.ToString());
            bool testPassed = configuration.TestPassed || gate.State is HistoryGateState.Ready or HistoryGateState.Enabled;
            gate.Disable();
            buffer.DiscardAll();
            await repository.SetHistoryGateStateAsync(testPassed, false, cancellationToken).ConfigureAwait(false);
            return new(true, "TDengine 历史通道已关闭；未启用期间不会保存或补写历史。", gate.State.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    private static TDengineHistoryChannel CreateChannel(TDengineConnectionConfiguration configuration) => new(ToOptions(configuration));

    private static TDengineOptions ToOptions(TDengineConnectionConfiguration configuration) => new(
        configuration.Host, configuration.Username, configuration.Password, configuration.Database,
        configuration.Port, configuration.UseSsl, configuration.EnableCompression,
        configuration.AutoReconnect, configuration.RequestTimeoutMs);
}

internal sealed record HistoryConfigurationRequest(
    string Host,
    int Port,
    string Username,
    string? Password,
    string Database,
    bool UseSsl,
    bool EnableCompression,
    bool AutoReconnect,
    int RequestTimeoutMs)
{
    public TDengineConnectionConfiguration ToConfiguration(string password) => new(
        Host, Port, Username, password, Database, UseSsl, EnableCompression, AutoReconnect, RequestTimeoutMs);
}

internal sealed record HistoryConfigurationResponse(
    bool IsConfigured,
    string Host,
    int Port,
    string Username,
    bool HasPassword,
    string Database,
    bool UseSsl,
    bool EnableCompression,
    bool AutoReconnect,
    int RequestTimeoutMs,
    string State)
{
    public static HistoryConfigurationResponse From(TDengineConnectionConfiguration? configuration, HistoryGateState state) =>
        configuration is null
            ? new(false, "127.0.0.1", 6041, string.Empty, false, string.Empty, false, true, true, 10_000, state.ToString())
            : new(true, configuration.Host, configuration.Port, configuration.Username, true, configuration.Database,
                configuration.UseSsl, configuration.EnableCompression, configuration.AutoReconnect,
                configuration.RequestTimeoutMs, state.ToString());
}

internal sealed record HistoryOperationResponse(bool Succeeded, string Message, string State);
