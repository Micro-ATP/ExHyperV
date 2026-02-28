using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExHyperV.Models;
using ExHyperV.Services;

namespace ExHyperV.ViewModels
{
    public partial class USBPageViewModel : ObservableObject
    {
        private readonly UsbVmbusService _srv;
        private readonly CancellationTokenSource _viewCts = new();

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _isUiEnabled = true;

        public ObservableCollection<UsbDeviceViewModel> Devices { get; } = new();
        public IAsyncRelayCommand LoadDataCommand { get; }
        public IAsyncRelayCommand<object> ChangeAssignmentCommand { get; }

        public USBPageViewModel()
        {
            _srv = new UsbVmbusService();
            LoadDataCommand = new AsyncRelayCommand(LoadDataAsync);
            ChangeAssignmentCommand = new AsyncRelayCommand<object>(ChangeAssignmentAsync);

            LoadDataCommand.Execute(null);

            // 启动后台监控循环 (维持手机连接)
            _ = Task.Run(() => _srv.WatchdogLoopAsync(_viewCts.Token));
            // 启动设备同步循环 (刷新手机变身后的 Description)
            _ = Task.Run(() => SyncDevicesLoopAsync(_viewCts.Token));
        }

        private async Task LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                _srv.EnsureServiceRegistered();
                await RefreshListInternal();
            }
            finally { IsLoading = false; }
        }

        private async Task SyncDevicesLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(3000, ct);
                await App.Current.Dispatcher.InvokeAsync(RefreshListInternal);
            }
        }

        private async Task RefreshListInternal()
        {
            var vms = await _srv.GetRunningVMsAsync();
            var usbDevices = await _srv.GetUsbIpDevicesAsync();
            var vmNames = vms.Select(v => v.Name).ToList();

            // 增量更新 UI 列表
            var currentBusIds = Devices.Select(d => d.BusId).ToList();
            var newBusIds = usbDevices.Select(d => d.BusId).ToList();

            for (int i = Devices.Count - 1; i >= 0; i--)
            {
                if (!newBusIds.Contains(Devices[i].BusId)) Devices.RemoveAt(i);
            }

            foreach (var dev in usbDevices)
            {
                var existing = Devices.FirstOrDefault(d => d.BusId == dev.BusId);
                if (existing != null)
                {
                    // 更新描述和 VID/PID (处理手机变身)
                    existing.Description = dev.Description;
                    existing.VidPid = dev.VidPid;
                    existing.UpdateOptions(vmNames);
                }
                else
                {
                    Devices.Add(new UsbDeviceViewModel(dev, vmNames));
                }
            }

            // 同步连接状态显示
            foreach (var d in Devices)
            {
                if (UsbVmbusService.ActiveTunnels.TryGetValue(d.BusId, out string vm)) d.CurrentAssignment = vm;
                else if (d.CurrentAssignment != "正在连接...") d.CurrentAssignment = "主机";
            }
        }

        private async Task ChangeAssignmentAsync(object parameter)
        {
            if (parameter is not object[] parameters || parameters.Length < 2 ||
                parameters[0] is not UsbDeviceViewModel deviceVM ||
                parameters[1] is not string selectedTarget) return;

            if (deviceVM.CurrentAssignment == selectedTarget) return;

            IsUiEnabled = false;
            try
            {
                if (selectedTarget == "主机")
                {
                    UsbVmbusService.ActiveTunnels.TryRemove(deviceVM.BusId, out _);
                    _srv.StopTunnel(deviceVM.BusId);
                    deviceVM.CurrentAssignment = "主机";
                }
                else
                {
                    UsbVmbusService.ActiveTunnels[deviceVM.BusId] = selectedTarget;
                    deviceVM.CurrentAssignment = selectedTarget;
                    // 立即触发连接
                    _ = Task.Run(() => _srv.AutoRecoverTunnel(deviceVM.BusId, selectedTarget));
                }
            }
            finally { IsUiEnabled = true; }
        }
    }
}