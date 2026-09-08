using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeartBeat.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Security.Cryptography;

namespace HeartBeat.App.Bluetooth;

public sealed class BleHeartRateTransport : IHeartRateTransport
{
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(8);

    private readonly ConcurrentDictionary<string, DeviceTarget> _targets =
        new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<HeartRateDevice>> ScanAsync(
        CancellationToken cancellationToken)
    {
        await EnsureBluetoothReadyAsync(cancellationToken);

        var devices = new ConcurrentDictionary<string, HeartRateDevice>(StringComparer.Ordinal);
        var stopped = new TaskCompletionSource<BluetoothError>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            // Active scanning obtains scan-response packets, which commonly contain the local name.
            ScanningMode = BluetoothLEScanningMode.Active
        };

        void AdvertisementReceived(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                string advertisedName = (args.Advertisement.LocalName ?? string.Empty).Trim();
                bool advertisesHeartRate = args.Advertisement.ServiceUuids.Contains(
                    GattServiceUuids.HeartRate);
                bool isGarmin = advertisedName.Contains("Garmin", StringComparison.OrdinalIgnoreCase) ||
                    advertisedName.Contains("Forerunner", StringComparison.OrdinalIgnoreCase);

                if (!advertisesHeartRate && !isGarmin)
                {
                    return;
                }

                string id = FormatDeviceId(args.BluetoothAddress, args.BluetoothAddressType);
                string name = advertisedName.Length == 0
                    ? $"心率设备 ({FormatAddress(args.BluetoothAddress)})"
                    : advertisedName;
                var target = new DeviceTarget(args.BluetoothAddress, args.BluetoothAddressType, name);
                var descriptor = new HeartRateDevice(id, name);

                _targets.AddOrUpdate(
                    id,
                    target,
                    (_, existing) => PreferNamedTarget(existing, target));
                devices.AddOrUpdate(
                    id,
                    descriptor,
                    (_, existing) => PreferNamedDevice(existing, descriptor));
            }
            catch (Exception exception)
            {
                // WinRT invokes this callback on a native dispatch thread. Never allow user/data
                // errors to escape into that dispatcher.
                Trace.TraceWarning("忽略无法处理的 BLE 广播包: {0}", exception);
            }
        }

        void WatcherStopped(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            stopped.TrySetResult(args.Error);
        }

        watcher.Received += AdvertisementReceived;
        watcher.Stopped += WatcherStopped;

        try
        {
            try
            {
                watcher.Start();
            }
            catch (Exception exception) when (IsAccessDenied(exception))
            {
                throw new InvalidOperationException(
                    "Windows 拒绝了蓝牙扫描权限。请在“设置 > 隐私和安全性”中允许应用访问蓝牙，然后重试。",
                    exception);
            }

            Task delay = Task.Delay(ScanDuration, cancellationToken);
            Task completed = await Task.WhenAny(delay, stopped.Task);

            if (ReferenceEquals(completed, delay))
            {
                await delay;
            }
            else
            {
                BluetoothError error = await stopped.Task;
                if (error != BluetoothError.Success)
                {
                    throw new IOException(
                        $"蓝牙扫描被 Windows 中止（{error}）。请确认蓝牙已开启，并尝试重新插拔蓝牙适配器。");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            watcher.Received -= AdvertisementReceived;
            watcher.Stopped -= WatcherStopped;

            if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started ||
                watcher.Status == BluetoothLEAdvertisementWatcherStatus.Stopping)
            {
                try
                {
                    watcher.Stop();
                }
                catch (Exception exception)
                {
                    Trace.TraceWarning("停止 BLE 扫描时发生错误: {0}", exception);
                }
            }
        }

        return devices.Values
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<IHeartRateConnection> ConnectAsync(
        HeartRateDevice device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureBluetoothReadyAsync(cancellationToken);

        DeviceTarget target = ResolveTarget(device);
        BluetoothLEDevice? bluetoothDevice = null;
        var discoveredServices = new List<GattDeviceService>();

        try
        {
            // WinRT does not reliably cancel BLE connection/GATT operations. Await the real
            // operation and observe caller cancellation immediately afterwards so no native
            // operation outlives resources that this method owns.
            bluetoothDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(
                target.Address,
                target.AddressType);
            cancellationToken.ThrowIfCancellationRequested();

            if (bluetoothDevice is null)
            {
                await WarmAddressCacheAsync(target, cancellationToken);
                bluetoothDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(
                    target.Address,
                    target.AddressType);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (bluetoothDevice is null)
            {
                throw new IOException(
                    $"无法打开“{target.Name}”。设备可能已离开范围；请保持手表靠近电脑并重新扫描。");
            }

            GattDeviceServicesResult serviceResult = await bluetoothDevice.GetGattServicesForUuidAsync(
                GattServiceUuids.HeartRate,
                BluetoothCacheMode.Uncached);
            discoveredServices.AddRange(serviceResult.Services);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureGattSuccess(serviceResult.Status, "读取标准心率服务 (0x180D)");

            if (discoveredServices.Count == 0)
            {
                throw new IOException(
                    $"“{target.Name}”未提供标准心率服务 (0x180D)。请在 Garmin 手表上开启腕式心率广播后重试。");
            }

            GattDeviceService? selectedService = null;
            GattCharacteristic? selectedCharacteristic = null;
            GattCommunicationStatus? characteristicFailure = null;
            bool characteristicQuerySucceeded = false;

            foreach (GattDeviceService service in discoveredServices)
            {
                GattCharacteristicsResult characteristicResult =
                    await service.GetCharacteristicsForUuidAsync(
                        GattCharacteristicUuids.HeartRateMeasurement,
                        BluetoothCacheMode.Uncached);
                cancellationToken.ThrowIfCancellationRequested();

                if (characteristicResult.Status != GattCommunicationStatus.Success)
                {
                    characteristicFailure = characteristicResult.Status;
                    continue;
                }

                characteristicQuerySucceeded = true;
                selectedCharacteristic = characteristicResult.Characteristics.FirstOrDefault(
                    characteristic =>
                        (characteristic.CharacteristicProperties & GattCharacteristicProperties.Notify) != 0);

                if (selectedCharacteristic is not null)
                {
                    selectedService = service;
                    break;
                }
            }

            if (selectedService is null || selectedCharacteristic is null)
            {
                if (!characteristicQuerySucceeded && characteristicFailure.HasValue)
                {
                    EnsureGattSuccess(characteristicFailure.Value, "读取心率测量特征 (0x2A37)");
                }

                throw new IOException(
                    $"“{target.Name}”的心率测量特征 (0x2A37) 不支持 Notify。请确认设备处于 BLE 心率广播模式。");
            }

            foreach (GattDeviceService service in discoveredServices)
            {
                if (!ReferenceEquals(service, selectedService))
                {
                    service.Dispose();
                }
            }

            discoveredServices.RemoveAll(service => !ReferenceEquals(service, selectedService));
            var connection = new BleHeartRateConnection(
                bluetoothDevice,
                selectedService,
                selectedCharacteristic,
                target.Name);
            discoveredServices.Clear();
            return connection;
        }
        catch
        {
            foreach (GattDeviceService service in discoveredServices)
            {
                service.Dispose();
            }

            bluetoothDevice?.Dispose();
            throw;
        }
    }

    private static async Task WarmAddressCacheAsync(
        DeviceTarget target,
        CancellationToken cancellationToken)
    {
        var found = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<BluetoothError>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        void AdvertisementReceived(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                if (args.BluetoothAddress == target.Address &&
                    args.BluetoothAddressType == target.AddressType)
                {
                    found.TrySetResult(true);
                }
            }
            catch (Exception exception)
            {
                Trace.TraceWarning("处理目标 BLE 广播包时发生错误: {0}", exception);
            }
        }

        void WatcherStopped(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            stopped.TrySetResult(args.Error);
        }

        watcher.Received += AdvertisementReceived;
        watcher.Stopped += WatcherStopped;

        try
        {
            try
            {
                watcher.Start();
            }
            catch (Exception exception) when (IsAccessDenied(exception))
            {
                throw new InvalidOperationException(
                    "Windows 拒绝了蓝牙扫描权限。请在“设置 > 隐私和安全性”中允许应用访问蓝牙，然后重试。",
                    exception);
            }

            Task delay = Task.Delay(ScanDuration, cancellationToken);
            Task completed = await Task.WhenAny(delay, found.Task, stopped.Task);

            if (ReferenceEquals(completed, delay))
            {
                await delay;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (ReferenceEquals(completed, stopped.Task))
            {
                BluetoothError error = await stopped.Task;
                if (error != BluetoothError.Success)
                {
                    throw new IOException(
                        $"查找已保存设备时蓝牙扫描被 Windows 中止（{error}）。请确认蓝牙已开启后重试。");
                }
            }
        }
        finally
        {
            watcher.Received -= AdvertisementReceived;
            watcher.Stopped -= WatcherStopped;

            if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started ||
                watcher.Status == BluetoothLEAdvertisementWatcherStatus.Stopping)
            {
                try
                {
                    watcher.Stop();
                }
                catch (Exception exception)
                {
                    Trace.TraceWarning("停止目标 BLE 扫描时发生错误: {0}", exception);
                }
            }
        }
    }

    private DeviceTarget ResolveTarget(HeartRateDevice device)
    {
        if (_targets.TryGetValue(device.Id, out DeviceTarget? remembered))
        {
            return remembered;
        }

        string[] parts = device.Id.Split(':');
        if (parts.Length == 3 &&
            string.Equals(parts[0], "ble", StringComparison.Ordinal) &&
            ulong.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong address) &&
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int typeValue) &&
            Enum.IsDefined(typeof(BluetoothAddressType), typeValue))
        {
            return new DeviceTarget(address, (BluetoothAddressType)typeValue, device.Name);
        }

        throw new ArgumentException("设备标识无效，请重新扫描蓝牙设备后再连接。", nameof(device));
    }

    private static async Task EnsureBluetoothReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BluetoothAdapter? adapter;
        try
        {
            adapter = await BluetoothAdapter.GetDefaultAsync();
        }
        catch (Exception exception) when (IsAccessDenied(exception))
        {
            throw new InvalidOperationException(
                "Windows 拒绝了蓝牙访问权限。请在“设置 > 隐私和安全性”中允许应用访问蓝牙。",
                exception);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (adapter is null)
        {
            throw new InvalidOperationException(
                "未检测到蓝牙适配器。请连接支持 Bluetooth Low Energy 的适配器并安装其 Windows 驱动。");
        }

        if (!adapter.IsLowEnergySupported)
        {
            throw new NotSupportedException(
                "当前蓝牙适配器不支持 Bluetooth Low Energy，无法接收心率广播。请更换支持 BLE 的适配器。");
        }

        Radio radio = await adapter.GetRadioAsync();
        cancellationToken.ThrowIfCancellationRequested();

        switch (radio.State)
        {
            case RadioState.On:
                return;
            case RadioState.Off:
                throw new InvalidOperationException(
                    "蓝牙已关闭。请在 Windows“设置 > 蓝牙和设备”中开启蓝牙后重试。");
            case RadioState.Disabled:
                throw new InvalidOperationException(
                    "蓝牙适配器已被系统或硬件开关禁用。请启用适配器（或关闭飞行模式）后重试。");
            default:
                throw new InvalidOperationException(
                    "无法确认蓝牙无线电状态。请在 Windows 设置中关闭再开启蓝牙，然后重试。");
        }
    }

    private static void EnsureGattSuccess(GattCommunicationStatus status, string operation)
    {
        if (status == GattCommunicationStatus.Success)
        {
            return;
        }

        string advice = status switch
        {
            GattCommunicationStatus.AccessDenied => "Windows 拒绝了访问，请检查蓝牙隐私权限。",
            GattCommunicationStatus.Unreachable => "设备不可达，请保持设备靠近电脑并确认心率广播仍在运行。",
            GattCommunicationStatus.ProtocolError => "设备返回了 GATT 协议错误，请关闭再开启手表的心率广播。",
            _ => "请确认蓝牙已开启并重新扫描设备。"
        };

        throw new IOException($"{operation}失败（{status}）。{advice}");
    }

    private static bool IsAccessDenied(Exception exception) =>
        exception is UnauthorizedAccessException || exception.HResult == unchecked((int)0x80070005);

    private static DeviceTarget PreferNamedTarget(DeviceTarget existing, DeviceTarget candidate) =>
        IsFallbackName(existing.Name) && !IsFallbackName(candidate.Name) ? candidate : existing;

    private static HeartRateDevice PreferNamedDevice(
        HeartRateDevice existing,
        HeartRateDevice candidate) =>
        IsFallbackName(existing.Name) && !IsFallbackName(candidate.Name) ? candidate : existing;

    private static bool IsFallbackName(string name) =>
        name.StartsWith("心率设备 (", StringComparison.Ordinal);

    private static string FormatDeviceId(ulong address, BluetoothAddressType addressType) =>
        $"ble:{address:X12}:{(int)addressType}";

    private static string FormatAddress(ulong address)
    {
        string value = address.ToString("X12", CultureInfo.InvariantCulture);
        return string.Join(":", Enumerable.Range(0, 6).Select(index => value.Substring(index * 2, 2)));
    }

    private sealed record DeviceTarget(
        ulong Address,
        BluetoothAddressType AddressType,
        string Name);

    private sealed class BleHeartRateConnection : IHeartRateConnection
    {
        private readonly BluetoothLEDevice _device;
        private readonly GattDeviceService _service;
        private readonly GattCharacteristic _characteristic;
        private readonly string _deviceName;
        private readonly object _eventGate = new();

        private int _startEntered;
        private int _started;
        private int _notificationHandlerAttached;
        private int _connectionLost;
        private int _disconnectedDelivered;
        private int _disposed;

        public BleHeartRateConnection(
            BluetoothLEDevice device,
            GattDeviceService service,
            GattCharacteristic characteristic,
            string deviceName)
        {
            _device = device;
            _service = service;
            _characteristic = characteristic;
            _deviceName = deviceName;
            _device.ConnectionStatusChanged += DeviceConnectionStatusChanged;

            if (_device.ConnectionStatus != BluetoothConnectionStatus.Connected)
            {
                Volatile.Write(ref _connectionLost, 1);
            }
        }

        public event Action<HeartRateMeasurement>? MeasurementReceived;

        public event Action? Disconnected;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                lock (_eventGate)
                {
                    ThrowIfDisposed();

                    if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                    {
                        throw new InvalidOperationException("此心率连接已经启动。请重新建立连接后再试。");
                    }

                    Volatile.Write(ref _startEntered, 1);
                    _characteristic.ValueChanged += CharacteristicValueChanged;
                    Volatile.Write(ref _notificationHandlerAttached, 1);
                }

                // A disconnect may occur between ConnectAsync returning and the consumer
                // attaching handlers. Re-check after StartAsync marks the event as deliverable.
                if (Volatile.Read(ref _connectionLost) != 0 ||
                    _device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                {
                    Volatile.Write(ref _connectionLost, 1);
                    RaiseDisconnected();
                    throw new IOException($"“{_deviceName}”已断开连接，请重新连接。");
                }

                GattCommunicationStatus status =
                    await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);

                // The WinRT operation above cannot be cancelled reliably; observe cancellation
                // only after its true completion, then dispose this connection in the catch path.
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                EnsureGattSuccess(status, "启用心率通知");

                if (Volatile.Read(ref _connectionLost) != 0 ||
                    _device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                {
                    Volatile.Write(ref _connectionLost, 1);
                    RaiseDisconnected();
                    throw new IOException($"启用通知时“{_deviceName}”断开了连接，请重试。");
                }
            }
            catch
            {
                await DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_eventGate)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return ValueTask.CompletedTask;
                }

                Volatile.Write(ref _notificationHandlerAttached, 0);
                _characteristic.ValueChanged -= CharacteristicValueChanged;
                _device.ConnectionStatusChanged -= DeviceConnectionStatusChanged;
            }

            // Avoid a CCCD "None" write here: Windows cannot reliably cancel that GATT
            // operation and it can hold application shutdown indefinitely. Releasing the local
            // handler and closing the service/device ends this client's subscription.
            _service.Dispose();
            _device.Dispose();

            MeasurementReceived = null;
            Disconnected = null;
            return ValueTask.CompletedTask;
        }

        private void DeviceConnectionStatusChanged(
            BluetoothLEDevice sender,
            object args)
        {
            try
            {
                if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected)
                {
                    return;
                }

                Volatile.Write(ref _connectionLost, 1);
                if (Volatile.Read(ref _startEntered) != 0)
                {
                    RaiseDisconnected();
                }
            }
            catch (Exception exception)
            {
                Trace.TraceWarning("处理 BLE 断开事件时发生错误: {0}", exception);
            }
        }

        private void CharacteristicValueChanged(
            GattCharacteristic sender,
            GattValueChangedEventArgs args)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                Volatile.Read(ref _notificationHandlerAttached) == 0)
            {
                return;
            }

            try
            {
                CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] bytes);
                if (HeartRateParser.TryParse(bytes, out HeartRateMeasurement measurement))
                {
                    InvokeMeasurementHandlers(measurement);
                }
            }
            catch (Exception exception)
            {
                // Malformed packets and subscriber errors must not escape the native callback.
                Trace.TraceWarning("忽略无法处理的心率通知: {0}", exception);
            }
        }

        private void InvokeMeasurementHandlers(HeartRateMeasurement measurement)
        {
            Action<HeartRateMeasurement>? handlers = MeasurementReceived;
            if (handlers is null)
            {
                return;
            }

            foreach (Action<HeartRateMeasurement> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(measurement);
                }
                catch (Exception exception)
                {
                    Trace.TraceWarning("心率数据订阅者发生错误: {0}", exception);
                }
            }
        }

        private void RaiseDisconnected()
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                Interlocked.Exchange(ref _disconnectedDelivered, 1) != 0)
            {
                return;
            }

            Action? handlers = Disconnected;
            if (handlers is null)
            {
                return;
            }

            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception exception)
                {
                    Trace.TraceWarning("断开事件订阅者发生错误: {0}", exception);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
        }
    }
}
