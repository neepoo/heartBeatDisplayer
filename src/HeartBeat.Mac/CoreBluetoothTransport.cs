using AppKit;
using CoreBluetooth;
using CoreFoundation;
using Foundation;
using HeartBeat.Core;

namespace HeartBeat.Mac;

/// <summary>Each operation owns a central manager, so a cancelled attempt cannot complete a later one.</summary>
public sealed class CoreBluetoothTransport : IHeartRateTransport
{
    public async Task<IReadOnlyList<HeartRateDevice>> ScanAsync(CancellationToken cancellationToken)
    {
        await using var session = new BluetoothSession();
        await session.InitializeAsync(cancellationToken);
        await MacMainThread.RunAsync(session.BeginScan);
        await session.WaitAsync(Task.Delay(TimeSpan.FromSeconds(8), cancellationToken), cancellationToken);
        return await MacMainThread.RunAsync(session.EndScan);
    }

    public async Task<IHeartRateConnection> ConnectAsync(HeartRateDevice device, CancellationToken cancellationToken)
    {
        if (!device.Id.StartsWith("mac:", StringComparison.Ordinal) || !Guid.TryParse(device.Id.AsSpan(4), out _))
            throw new ArgumentException("设备标识不是 macOS 蓝牙设备，请重新扫描。", nameof(device));
        var session = new BluetoothSession();
        try
        {
            await session.InitializeAsync(cancellationToken);
            await MacMainThread.RunAsync(() => session.BeginConnect(device.Id[4..]));
            await session.WaitAsync(session.Discovery, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private sealed class BluetoothSession : CBCentralManagerDelegate, IHeartRateConnection
    {
        private static readonly CBUUID HeartRateService = CBUUID.FromString("180D");
        private static readonly CBUUID Measurement = CBUUID.FromString("2A37");
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _discovery = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _notifying = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<Exception> _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly NotificationLifetime _lifetime = new();
        private readonly Dictionary<string, HeartRateDevice> _devices = new(StringComparer.Ordinal);
        private readonly object _startGate = new();
        private CBCentralManager? _central;
        private CBPeripheral? _peripheral;
        private PeripheralDelegate? _peripheralDelegate;
        private CBCharacteristic? _characteristic;
        private Task? _start;
        private volatile bool _closed;
        private bool _scanning;

        public Task Discovery => _discovery.Task;
        public event Action<HeartRateMeasurement>? MeasurementReceived
        {
            add => _lifetime.MeasurementReceived += value;
            remove => _lifetime.MeasurementReceived -= value;
        }
        public event Action? Disconnected
        {
            add => _lifetime.Disconnected += value;
            remove => _lifetime.Disconnected -= value;
        }

        public async Task InitializeAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await MacMainThread.RunAsync(() =>
            {
                // Both callbacks and all native resource access are serialized on the main queue.
                _central = new CBCentralManager(this, DispatchQueue.MainQueue,
                    new CBCentralInitOptions { ShowPowerAlert = false });
                UpdatedState(_central);
            });
            await WaitAsync(_ready.Task, token);
        }

        public async Task WaitAsync(Task operation, CancellationToken token)
        {
            var completed = await Task.WhenAny(operation, _failure.Task)
                .WaitAsync(TimeSpan.FromSeconds(20), token);
            if (completed == _failure.Task) throw await _failure.Task;
            await operation;
            if (_failure.Task.IsCompleted) throw await _failure.Task;
            token.ThrowIfCancellationRequested();
        }

        public override void UpdatedState(CBCentralManager central)
        {
            if (_closed) return;
            switch (central.State)
            {
                case CBManagerState.PoweredOn: _ready.TrySetResult(); break;
                case CBManagerState.Unauthorized:
                    Fail(new UnauthorizedAccessException("macOS 拒绝了蓝牙权限。请在“系统设置 > 隐私与安全性 > 蓝牙”中允许 HeartBeat，然后重新启动应用。")); break;
                case CBManagerState.PoweredOff:
                    Fail(new IOException("蓝牙已关闭，请在 macOS 系统设置中开启蓝牙。")); break;
                case CBManagerState.Unsupported:
                    Fail(new NotSupportedException("未检测到支持低功耗蓝牙的适配器。")); break;
                case CBManagerState.Resetting:
                    if (_ready.Task.IsCompleted) Fail(new IOException("macOS 蓝牙正在重置，请稍后重新连接。"));
                    break;
                // Unknown is the normal initial state; bounded initialization waits for a real state.
            }
        }

        public void BeginScan()
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _scanning = true;
            _central!.ScanForPeripherals([HeartRateService]);
        }

        public IReadOnlyList<HeartRateDevice> EndScan()
        {
            _scanning = false;
            _central!.StopScan();
            return _devices.Values.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        public override void DiscoveredPeripheral(CBCentralManager central, CBPeripheral peripheral,
            NSDictionary advertisementData, NSNumber RSSI)
        {
            if (_closed || !_scanning) return;
            var id = "mac:" + peripheral.Identifier.AsString();
            var name = (advertisementData[CBAdvertisement.DataLocalNameKey] as NSString)?.ToString()
                ?? peripheral.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = _devices.TryGetValue(id, out var existing) ? existing.Name : $"心率设备 ({peripheral.Identifier.AsString()})";
            _devices[id] = new(id, name);
        }

        public void BeginConnect(string uuid)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            using var identifier = new NSUuid(uuid);
            _peripheral = _central!.RetrievePeripheralsWithIdentifiers([identifier]).FirstOrDefault()
                ?? throw new IOException("macOS 已不再识别该设备，请重新扫描后再连接。");
            _peripheralDelegate = new PeripheralDelegate(this);
            _peripheral.Delegate = _peripheralDelegate;
            _central.ConnectPeripheral(_peripheral);
        }

        private bool Owns(CBPeripheral peripheral) => !_closed && _peripheral?.Identifier.Equals(peripheral.Identifier) == true;

        public override void ConnectedPeripheral(CBCentralManager central, CBPeripheral peripheral)
        {
            if (!Owns(peripheral))
            {
                // A cancellation can race with successful connection; never leave it connected.
                if (!_closed) central.CancelPeripheralConnection(peripheral);
                return;
            }
            peripheral.DiscoverServices([HeartRateService]);
        }

        public override void FailedToConnectPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
        {
            if (Owns(peripheral)) Fail(Error("连接心率设备失败", error));
        }

        public override void DisconnectedPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
        {
            if (Owns(peripheral)) Fail(Error("心率设备已断开连接", error));
        }

        private void Fail(Exception error)
        {
            if (_closed) return;
            _failure.TrySetResult(error);
            _lifetime.LoseConnection();
        }

        private static IOException Error(string action, NSError? error) =>
            new(error is null ? action : $"{action}：{error.LocalizedDescription}");

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_closed, this);
            lock (_startGate) return (_start ??= StartCoreAsync(cancellationToken)).WaitAsync(cancellationToken);
        }

        private async Task StartCoreAsync(CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                await MacMainThread.RunAsync(() =>
                {
                    _lifetime.Start();
                    if (_characteristic is null) throw new IOException("未发现心率测量特征 (0x2A37)。");
                    _peripheral!.SetNotifyValue(true, _characteristic);
                });
                await WaitAsync(_notifying.Task, token);
            }
            catch
            {
                await DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync() => new(MacMainThread.RunAsync(() =>
        {
            if (_closed) return;
            _closed = true;
            _lifetime.Dispose();
            _failure.TrySetResult(new ObjectDisposedException(nameof(BluetoothSession)));
            if (_central is not null)
            {
                _central.StopScan();
                if (_peripheral is not null)
                {
                    // Detach first: cancellation may result in already queued native callbacks.
                    _peripheral.WeakDelegate = null;
                    if (_central.State == CBManagerState.PoweredOn)
                    {
                        if (_characteristic?.IsNotifying == true) _peripheral.SetNotifyValue(false, _characteristic);
                        _central.CancelPeripheralConnection(_peripheral);
                    }
                }
                _central.WeakDelegate = null;
                _central.Dispose();
                _central = null;
            }
            _peripheralDelegate?.Dispose();
            _peripheralDelegate = null;
            _peripheral?.Dispose();
            _peripheral = null;
            _characteristic = null;
            base.Dispose();
        }));

        private sealed class PeripheralDelegate(BluetoothSession owner) : CBPeripheralDelegate
        {
            public override void DiscoveredService(CBPeripheral peripheral, NSError? error)
            {
                if (!owner.Owns(peripheral)) return;
                if (error is not null) { owner.Fail(Error("读取心率服务失败", error)); return; }
                var service = peripheral.Services?.FirstOrDefault(s => s.UUID.Equals(HeartRateService));
                if (service is null) { owner.Fail(Error("未发现标准心率服务 (0x180D)，请开启手表心率广播", null)); return; }
                peripheral.DiscoverCharacteristics([Measurement], service);
            }

            public override void DiscoveredCharacteristics(CBPeripheral peripheral, CBService service, NSError? error)
            {
                if (!owner.Owns(peripheral)) return;
                if (error is not null) { owner.Fail(Error("读取心率特征失败", error)); return; }
                owner._characteristic = service.Characteristics?.FirstOrDefault(c => c.UUID.Equals(Measurement)
                    && (c.Properties & (CBCharacteristicProperties.Notify | CBCharacteristicProperties.Indicate)) != 0);
                if (owner._characteristic is null) { owner.Fail(Error("心率测量特征 (0x2A37) 不支持通知", null)); return; }
                owner._discovery.TrySetResult();
            }

            public override void UpdatedNotificationState(CBPeripheral peripheral, CBCharacteristic characteristic, NSError? error)
            {
                if (!owner.Owns(peripheral) || !characteristic.UUID.Equals(Measurement)) return;
                if (error is not null || !characteristic.IsNotifying)
                    owner.Fail(Error("启用心率通知失败", error));
                else owner._notifying.TrySetResult();
            }

            public override void UpdatedCharacterteristicValue(CBPeripheral peripheral, CBCharacteristic characteristic, NSError? error)
            {
                if (!owner.Owns(peripheral) || !characteristic.UUID.Equals(Measurement)) return;
                if (error is not null) { owner.Fail(Error("读取心率通知失败", error)); return; }
                if (characteristic.Value is { } value && HeartRateParser.TryParse(value.ToArray(), out var measurement))
                    owner._lifetime.Deliver(measurement);
            }
        }
    }
}

internal static class MacMainThread
{
    public static Task RunAsync(Action action) => RunAsync(() => { action(); return true; });
    public static Task<T> RunAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Invoke()
        {
            try { completion.TrySetResult(action()); }
            catch (Exception error) { completion.TrySetException(error); }
        }
        if (NSThread.IsMain) Invoke();
        else NSApplication.SharedApplication.BeginInvokeOnMainThread(Invoke);
        return completion.Task;
    }
}
