using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExHyperV.Models;
using ExHyperV.Services;
using ExHyperV.Tools;
using Wpf.Ui.Controls;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;

namespace ExHyperV.ViewModels
{
    public enum VmDetailViewType
    {
        Dashboard, CpuSettings, CpuAffinity, MemorySettings, StorageSettings, AddStorage,
        GpuSettings,
        AddGpuSelect,
        AddGpuProgress, NetworkSettings
    }

    public partial class VirtualMachinesPageViewModel : ObservableObject, IDisposable
    {
        // ----------------------------------------------------------------------------------
        // 私有服务字段与依赖注入
        // ----------------------------------------------------------------------------------
        private readonly VmQueryService _queryService;
        private readonly VmPowerService _powerService;
        private readonly VmProcessorService _vmProcessorService;
        private readonly CpuAffinityService _cpuAffinityService;
        private readonly VmMemoryService _vmMemoryService;
        private readonly VmStorageService _storageService;
        private readonly VmGPUService _vmGpuService;
        private readonly VmNetworkService _vmNetworkService;

        // ----------------------------------------------------------------------------------
        // 监控与后台任务字段
        // ----------------------------------------------------------------------------------
        private CpuMonitorService _cpuService;
        private CancellationTokenSource _monitoringCts;
        private Task _cpuTask;
        private Task _stateTask;
        private DispatcherTimer _uiTimer;
        private DispatcherTimer? _thumbnailTimer;

        // ----------------------------------------------------------------------------------
        // 缓存与状态字段
        // ----------------------------------------------------------------------------------
        private const int MaxHistoryLength = 60;
        private readonly Dictionary<string, LinkedList<double>> _historyCache = new();
        private VmProcessorSettings _originalSettingsCache;
        private Snackbar? _activeSnackbar;
        private bool _isInternalUpdating = false;

        // ----------------------------------------------------------------------------------
        // 视图模型属性 - 页面状态
        // ----------------------------------------------------------------------------------
        [ObservableProperty] private bool _isLoading = true;
        [ObservableProperty] private bool _isLoadingSettings;
        [ObservableProperty] private VmDetailViewType _currentViewType = VmDetailViewType.Dashboard;
        [ObservableProperty] private string _searchText = string.Empty;

        // ----------------------------------------------------------------------------------
        // 视图模型属性 - 虚拟机列表与选择
        // ----------------------------------------------------------------------------------
        [ObservableProperty] private ObservableCollection<VmInstanceInfo> _vmList = new();
        [ObservableProperty] private VmInstanceInfo _selectedVm;
        [ObservableProperty] private BitmapSource? _thumbnail;

        // ----------------------------------------------------------------------------------
        // 视图模型属性 - CPU 设置
        // ----------------------------------------------------------------------------------
        public ObservableCollection<int> PossibleVCpuCounts { get; private set; }
        [ObservableProperty] private ObservableCollection<VmCoreModel> _affinityHostCores;
        [ObservableProperty] private int _affinityColumns = 8;
        [ObservableProperty] private int _affinityRows = 1;

        // ----------------------------------------------------------------------------------
        // 视图模型属性 - 存储管理
        // ----------------------------------------------------------------------------------
        [ObservableProperty] private ObservableCollection<HostDiskInfo> _hostDisks = new();

        // 存储向导属性
        [ObservableProperty] private string _deviceType = "HardDisk";
        [ObservableProperty] private bool _isPhysicalSource = false;
        [ObservableProperty] private bool _autoAssign = true;
        [ObservableProperty] private string _filePath = string.Empty;
        [ObservableProperty] private bool _isNewDisk = false;
        [ObservableProperty] private string _newDiskSize = "128";

        // ISO与高级选项
        [ObservableProperty] private string _selectedVhdType = "Dynamic";
        [ObservableProperty] private string _parentPath = string.Empty;
        [ObservableProperty] private string _sectorFormat = "Default";
        [ObservableProperty] private string _blockSize = "Default";
        [ObservableProperty] private string _isoSourceFolderPath = string.Empty;
        [ObservableProperty] private string _isoVolumeLabel = "NewISO";
        [ObservableProperty] private string _isoOutputPath = string.Empty;

        // 选中的物理磁盘与控制器
        [ObservableProperty] private HostDiskInfo _selectedPhysicalDisk;
        [ObservableProperty] private string _selectedControllerType = "SCSI";
        [ObservableProperty] private int _selectedControllerNumber = 0;
        [ObservableProperty] private int _selectedLocation = 0;

        // 存储验证与提示
        [ObservableProperty] private string _slotWarningMessage = string.Empty;
        [ObservableProperty][NotifyPropertyChangedFor(nameof(SlotWarningVisibility))] private bool _isSlotValid = true;
        public Visibility SlotWarningVisibility => IsSlotValid ? Visibility.Collapsed : Visibility.Visible;

        // 存储只读集合
        public ObservableCollection<string> AvailableControllerTypes { get; } = new();
        public ObservableCollection<int> AvailableControllerNumbers { get; } = new();
        public ObservableCollection<int> AvailableLocations { get; } = new();
        public List<int> NewDiskSizePresets { get; } = new() { 32, 64, 128, 256, 512, 1024 };

        // ----------------------------------------------------------------------------------
        // 视图模型属性 - 网络设置
        // ----------------------------------------------------------------------------------
        [ObservableProperty] private ObservableCollection<string> _availableSwitchNames = new();

        // ----------------------------------------------------------------------------------
        // 视图模型属性 - GPU 管理
        // ----------------------------------------------------------------------------------
        [ObservableProperty] private ObservableCollection<GPUInfo> _hostGpus = new();
        [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ConfirmAddGpuCommand))] private GPUInfo _selectedHostGpu;
        [ObservableProperty] private bool _autoInstallDrivers = true;
        [ObservableProperty] private ObservableCollection<TaskItem> _gpuTasks = new();
        [ObservableProperty] private bool _showPartitionSelector = false;
        [ObservableProperty] private ObservableCollection<PartitionInfo> _detectedPartitions = new();
        [ObservableProperty] private PartitionInfo _selectedPartition;
        [ObservableProperty] private bool _showSshForm = false;

        // Linux SSH 凭据
        [ObservableProperty] private string _sshHost = "";
        [ObservableProperty] private string _sshUsername = "root";
        [ObservableProperty] private string _sshPassword = "";
        [ObservableProperty] private int _sshPort = 22;
        [ObservableProperty] private bool _installGraphics = true;
        [ObservableProperty] private string _sshProxyHost = "";
        [ObservableProperty] private string _sshProxyPort = "";

        // 日志与控制台
        [ObservableProperty] private string _gpuDeploymentLog = string.Empty;
        [ObservableProperty] private bool _showLogConsole = false;

        // ----------------------------------------------------------------------------------
        // 构造函数与资源释放
        // ----------------------------------------------------------------------------------

        public VirtualMachinesPageViewModel(VmQueryService queryService, VmPowerService powerService)
        {
            _queryService = queryService;
            _powerService = powerService;
            _vmProcessorService = new VmProcessorService();
            _cpuAffinityService = new CpuAffinityService();
            _vmMemoryService = new VmMemoryService();
            _storageService = new VmStorageService();
            _vmGpuService = new VmGPUService();
            _vmNetworkService = new VmNetworkService();

            InitPossibleCpuCounts();

            for (int i = 0; i < 64; i++)
            {
                AvailableLocations.Add(i);
            }

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += (s, e) => { foreach (var vm in VmList) vm.TickUptime(); };
            _uiTimer.Start();

            Task.Run(async () => {
                await Task.Delay(300);
                Application.Current.Dispatcher.Invoke(() => LoadVmsCommand.Execute(null));
            });
        }

        public void Dispose()
        {
            _monitoringCts?.Cancel();
            _cpuService?.Dispose();
            _uiTimer?.Stop();
        }

        // ----------------------------------------------------------------------------------
        // 导航与页面状态控制
        // ----------------------------------------------------------------------------------

        // 搜索框文本变化时的过滤逻辑
        partial void OnSearchTextChanged(string value)
        {
            var view = CollectionViewSource.GetDefaultView(VmList);
            if (view != null)
            {
                view.Filter = item => (item is VmInstanceInfo vm) && (string.IsNullOrEmpty(value) || vm.Name.Contains(value, StringComparison.OrdinalIgnoreCase));
                view.Refresh();
            }
        }

        // 返回仪表盘
        [RelayCommand]
        private void GoBackToDashboard() => CurrentViewType = VmDetailViewType.Dashboard;

        // 根据当前视图层级返回上一级
        [RelayCommand]
        private void GoBack()
        {
            switch (CurrentViewType)
            {
                case VmDetailViewType.AddStorage:
                    CurrentViewType = VmDetailViewType.StorageSettings;
                    break;
                case VmDetailViewType.GpuSettings:
                case VmDetailViewType.CpuSettings:
                case VmDetailViewType.CpuAffinity:
                case VmDetailViewType.MemorySettings:
                case VmDetailViewType.StorageSettings:
                case VmDetailViewType.NetworkSettings:
                    CurrentViewType = VmDetailViewType.Dashboard;
                    break;
                default:
                    CurrentViewType = VmDetailViewType.Dashboard;
                    break;
            }
        }

        // ----------------------------------------------------------------------------------
        // 虚拟机列表管理与核心操作
        // ----------------------------------------------------------------------------------

        // 当选中的虚拟机发生变化时重置视图
        partial void OnSelectedVmChanged(VmInstanceInfo value)
        {
            CurrentViewType = VmDetailViewType.Dashboard;
            _originalSettingsCache = null;
            HostDisks.Clear();
        }

        public List<string> AvailableOsTypes => Utils.SupportedOsTypes;

        // 加载虚拟机列表
        [RelayCommand]
        private async Task LoadVmsAsync()
        {
            if (IsLoading && VmList.Count > 0) return;
            IsLoading = true;
            try
            {
                var finalCollection = await Task.Run(async () => {
                    var vms = await _queryService.GetVmListAsync();
                    var list = new ObservableCollection<VmInstanceInfo>();
                    foreach (var vm in vms)
                    {
                        if (string.IsNullOrWhiteSpace(vm.Name)) continue;

                        var instance = new VmInstanceInfo(vm.Id, vm.Name)
                        {
                            OsType = vm.OsType,
                            CpuCount = vm.CpuCount,
                            MemoryGb = vm.MemoryGb,
                            Notes = vm.Notes,
                            Generation = vm.Generation,
                            Version = vm.Version,
                            GpuName = vm.GpuName
                        };

                        foreach (var disk in vm.Disks) instance.Disks.Add(disk);

                        instance.SyncBackendData(vm.State, vm.RawUptime);

                        // 绑定电源控制命令
                        instance.ControlCommand = new AsyncRelayCommand<string>(async (action) => {
                            instance.SetTransientState(GetOptimisticText(action));
                            try
                            {
                                await _powerService.ExecuteControlActionAsync(instance.Name, action);
                                await SyncSingleVmStateAsync(instance);
                            }
                            catch (Exception ex)
                            {
                                Application.Current.Dispatcher.Invoke(() => instance.ClearTransientState());
                                var realEx = ex;
                                while (realEx.InnerException != null) { realEx = realEx.InnerException; }
                                ShowSnackbar("操作失败", Utils.GetFriendlyErrorMessages(realEx.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                            }
                        });

                        if (vm.NetworkAdapters != null)
                        {
                            foreach (var net in vm.NetworkAdapters) instance.NetworkAdapters.Add(net);
                        }

                        instance.IpAddress = vm.IpAddress;
                        list.Add(instance);
                    }
                    return list;
                });

                VmList = finalCollection;

                // 配置排序规则
                var view = CollectionViewSource.GetDefaultView(VmList);
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(nameof(VmInstanceInfo.IsRunning), ListSortDirection.Descending));
                view.SortDescriptions.Add(new SortDescription(nameof(VmInstanceInfo.Name), ListSortDirection.Ascending));

                // 开启实时排序
                if (view is System.ComponentModel.ICollectionViewLiveShaping liveView)
                {
                    liveView.IsLiveSorting = true;
                    liveView.LiveSortingProperties.Add(nameof(VmInstanceInfo.IsRunning));
                }

                if (SelectedVm == null || !VmList.Any(x => x.Name == SelectedVm.Name))
                {
                    SelectedVm = VmList.FirstOrDefault();
                }

                StartMonitoring();
            }
            catch (Exception ex)
            {
                ShowSnackbar("加载失败", Utils.GetFriendlyErrorMessages(ex.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                IsLoading = false;
            }
        }

        // 打开官方 vmconnect 连接工具
        [RelayCommand]
        private void OpenNativeConnect()
        {
            if (SelectedVm == null) return;
            try
            {
                System.Diagnostics.Process.Start("vmconnect.exe", $"localhost \"{SelectedVm.Name}\"");
            }
            catch (Exception ex)
            {
                ShowSnackbar("启动失败", "无法打开官方连接工具，请确保已安装 Hyper-V 管理组件。", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
        }

        // 修改操作系统标签
        [RelayCommand]
        private async Task ChangeOsType(string newType)
        {
            if (SelectedVm == null || SelectedVm.OsType == newType) return;
            string oldOsType = SelectedVm.OsType;
            string oldNotes = SelectedVm.Notes;
            SelectedVm.OsType = newType;
            SelectedVm.Notes = Utils.UpdateTagValue(SelectedVm.Notes, "OSType", newType);
            bool success = await _queryService.SetVmOsTypeAsync(SelectedVm.Name, newType);
            if (!success)
            {
                SelectedVm.OsType = oldOsType;
                SelectedVm.Notes = oldNotes;
                ShowSnackbar("修改失败", "无法保存标签，请检查权限", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
        }

        // ----------------------------------------------------------------------------------
        // 后台监控循环与状态更新
        // ----------------------------------------------------------------------------------

        // 启动后台监控线程
        private void StartMonitoring() { if (_monitoringCts != null) return; _monitoringCts = new CancellationTokenSource(); _cpuTask = Task.Run(() => MonitorCpuLoop(_monitoringCts.Token)); _stateTask = Task.Run(() => MonitorStateLoop(_monitoringCts.Token)); }

        // CPU 使用率监控循环
        private async Task MonitorCpuLoop(CancellationToken token)
        {
            try { _cpuService = new CpuMonitorService(); } catch { return; }
            while (!token.IsCancellationRequested)
            {
                try { var rawData = _cpuService.GetCpuUsage(); Application.Current.Dispatcher.Invoke(() => ProcessAndApplyCpuUpdates(rawData)); await Task.Delay(1000, token); }
                catch (TaskCanceledException) { break; }
                catch { await Task.Delay(5000, token); }
            }
            _cpuService?.Dispose();
        }

        // 虚拟机状态与性能数据同步循环
        private async Task MonitorStateLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var updates = await _queryService.GetVmListAsync();
                    var memoryMap = await _queryService.GetVmRuntimeMemoryDataAsync();

                    await _queryService.UpdateDiskPerformanceAsync(VmList);
                    var gpuUsageMap = await _queryService.GetGpuPerformanceAsync(VmList);

                    Application.Current.Dispatcher.Invoke(() => {
                        bool needsResort = false;

                        foreach (var update in updates)
                        {
                            var vm = VmList.FirstOrDefault(v => v.Name == update.Name);
                            if (vm != null)
                            {
                                bool wasRunning = vm.IsRunning;

                                vm.SyncBackendData(update.State, update.RawUptime);

                                if (wasRunning != vm.IsRunning)
                                {
                                    needsResort = true;

                                    // --- [新增逻辑] Root 调度器自动同步补丁 ---
                                    // 当检测到虚拟机从关机/挂起状态进入运行状态时
                                    if (vm.IsRunning)
                                    {
                                        _ = Task.Run(async () => {
                                            // 只有在 Root 调度器模式下才需要这种进程级补偿
                                            if (HyperVSchedulerService.GetSchedulerType() == HyperVSchedulerType.Root)
                                            {
                                                string savedAffinity = Utils.GetTagValue(vm.Notes, "Affinity");
                                                if (!string.IsNullOrEmpty(savedAffinity))
                                                {
                                                    try
                                                    {
                                                        var coreIds = savedAffinity.Split(',')
                                                                        .Select(s => int.Parse(s.Trim()))
                                                                        .ToList();
                                                        // 等待 vmmem 进程完全初始化（防止 PID 还没分配或所有权还没同步）
                                                        await Task.Delay(2000);
                                                        ProcessAffinityManager.SetVmProcessAffinity(vm.Id, coreIds);
                                                        System.Diagnostics.Debug.WriteLine($"[RootAutoSync] 已为虚拟机 {vm.Name} 自动恢复 CPU 绑定: {savedAffinity}");
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        System.Diagnostics.Debug.WriteLine($"[RootAutoSync] 自动同步失败: {ex.Message}");
                                                    }
                                                }
                                            }
                                        });
                                    }
                                }

                                if (CurrentViewType != VmDetailViewType.NetworkSettings && !IsLoadingSettings)
                                {
                                    SyncNetworkAdaptersInternal(vm.NetworkAdapters, update.NetworkAdapters.ToList());
                                }

                                // IP 地址探测逻辑
                                if (vm.IsRunning)
                                {
                                    var allIps = vm.NetworkAdapters
                                                   .SelectMany(a => a.IpAddresses ?? new List<string>())
                                                   .Where(ip => !string.IsNullOrEmpty(ip) && !ip.Contains(":"))
                                                   .ToList();

                                    if (allIps.Count > 0)
                                    {
                                        vm.IpAddress = allIps.First();
                                    }

                                    foreach (var adapter in vm.NetworkAdapters)
                                    {
                                        if (!string.IsNullOrEmpty(adapter.MacAddress) && (adapter.IpAddresses == null || adapter.IpAddresses.Count == 0))
                                        {
                                            _ = Task.Run(async () => {
                                                try
                                                {
                                                    string arpIp = await Utils.GetVmIpAddressAsync(vm.Name, adapter.MacAddress);
                                                    if (!string.IsNullOrEmpty(arpIp))
                                                    {
                                                        Application.Current.Dispatcher.Invoke(() => {
                                                            adapter.IpAddresses = new List<string> { arpIp };
                                                            if (vm.IpAddress == "---" || string.IsNullOrWhiteSpace(vm.IpAddress) || vm.IpAddress.Contains("获取"))
                                                            {
                                                                vm.IpAddress = arpIp;
                                                            }
                                                        });
                                                    }
                                                }
                                                catch { }
                                            });
                                        }
                                    }
                                }
                                else
                                {
                                    vm.IpAddress = "---";
                                }

                                // 增量更新磁盘信息
                                var updatePaths = update.Disks.Select(d => d.Path).ToHashSet();
                                for (int i = vm.Disks.Count - 1; i >= 0; i--)
                                {
                                    if (!updatePaths.Contains(vm.Disks[i].Path))
                                        vm.Disks.RemoveAt(i);
                                }
                                foreach (var newDiskData in update.Disks)
                                {
                                    var existingDisk = vm.Disks.FirstOrDefault(d => d.Path == newDiskData.Path);
                                    if (existingDisk != null)
                                    {
                                        existingDisk.Name = newDiskData.Name;
                                        if (vm.IsRunning && existingDisk.DiskType != "Physical" && File.Exists(existingDisk.Path))
                                        {
                                            try
                                            {
                                                // 使用 long 字节更新，对应 VmDiskDetails 的 CurrentSize
                                                long realSizeBytes = new FileInfo(existingDisk.Path).Length;
                                                if (existingDisk.CurrentSize != realSizeBytes)
                                                {
                                                    existingDisk.CurrentSize = realSizeBytes;
                                                }
                                            }
                                            catch { /* 忽略锁定错误 */ }
                                        }
                                        else
                                        {
                                            // 如果没开机或者查不到，才用 WMI 的数据
                                            existingDisk.CurrentSize = newDiskData.CurrentSize;
                                        }
                                        existingDisk.MaxSize = newDiskData.MaxSize;
                                        existingDisk.DiskType = newDiskData.DiskType;
                                    }
                                    else
                                    {
                                        vm.Disks.Add(newDiskData);
                                    }
                                }

                                vm.GpuName = update.GpuName;

                                if (memoryMap.TryGetValue(vm.Id.ToString(), out var memData))
                                    vm.UpdateMemoryStatus(memData.AssignedMb, memData.AvailablePercent);
                                else if (memoryMap.TryGetValue(vm.Id.ToString().ToUpper(), out var memDataUpper))
                                    vm.UpdateMemoryStatus(memDataUpper.AssignedMb, memDataUpper.AvailablePercent);
                                else
                                    vm.UpdateMemoryStatus(0, 0);
                            }
                        }

                        if (gpuUsageMap.Count > 0)
                        {
                            foreach (var vm in VmList)
                            {
                                if (gpuUsageMap.TryGetValue(vm.Id, out var gpuData))
                                {
                                    vm.UpdateGpuStats(gpuData);
                                }
                            }
                        }
                        if (needsResort)
                        {
                            CollectionViewSource.GetDefaultView(VmList).Refresh();
                        }
                    });

                    // 缩略图更新
                    if (SelectedVm != null)
                    {
                        if (SelectedVm.IsRunning)
                        {
                            var img = await VmThumbnailProvider.GetThumbnailAsync(SelectedVm.Name, 320, 240);
                            if (img != null)
                            {
                                Application.Current.Dispatcher.Invoke(() => SelectedVm.Thumbnail = img);
                            }
                        }
                        else
                        {
                            if (SelectedVm.Thumbnail != null)
                            {
                                Application.Current.Dispatcher.Invoke(() => SelectedVm.Thumbnail = null);
                            }
                        }
                    }

                    if (SelectedVm != null && SelectedVm.IsRunning)
                    {
                        // 仅对当前选中的、且正在运行的 VM 进行磁盘大小轮询
                        await _storageService.RefreshVirtualDiskSizesAsync(SelectedVm);
                    }

                    await Task.Delay(2000, token);
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MonitorLoop Error] {ex.Message}");
                    await Task.Delay(3000, token);
                }
            }
        }

        // 同步单个虚拟机的最新状态
        private async Task SyncSingleVmStateAsync(VmInstanceInfo vm)
        {
            try
            {
                var allVms = await _queryService.GetVmListAsync();
                var freshData = allVms.FirstOrDefault(x => x.Name == vm.Name);
                if (freshData != null)
                {
                    Application.Current.Dispatcher.Invoke(() => {
                        vm.SyncBackendData(freshData.State, freshData.RawUptime);
                        vm.Disks.Clear();
                        foreach (var disk in freshData.Disks) vm.Disks.Add(disk);
                        vm.Generation = freshData.Generation;
                        vm.Version = freshData.Version;
                        vm.GpuName = freshData.GpuName;
                    });
                }
            }
            catch { }
        }

        // 处理 CPU 更新数据
        private void ProcessAndApplyCpuUpdates(List<CpuCoreMetric> rawData) { var grouped = rawData.GroupBy(x => x.VmName); foreach (var group in grouped) { var vm = VmList.FirstOrDefault(v => v.Name == group.Key); if (vm == null) continue; vm.AverageUsage = vm.IsRunning ? group.Average(x => x.Usage) : 0; UpdateVmCores(vm, group.ToList()); } }
        private void UpdateVmCores(VmInstanceInfo vm, List<CpuCoreMetric> metrics) { var metricIds = metrics.Select(m => m.CoreId).ToHashSet(); vm.Cores.Where(c => !metricIds.Contains(c.CoreId)).ToList().ForEach(r => vm.Cores.Remove(r)); foreach (var metric in metrics) { var core = vm.Cores.FirstOrDefault(c => c.CoreId == metric.CoreId); if (core == null) { core = new VmCoreModel { CoreId = metric.CoreId }; int idx = 0; while (idx < vm.Cores.Count && vm.Cores[idx].CoreId < metric.CoreId) idx++; vm.Cores.Insert(idx, core); } core.Usage = metric.Usage; UpdateHistory(vm.Name, core); } vm.Columns = LayoutHelper.CalculateOptimalColumns(vm.Cores.Count); vm.Rows = (vm.Cores.Count > 0) ? (int)Math.Ceiling((double)vm.Cores.Count / vm.Columns) : 1; }
        private void UpdateHistory(string vmName, VmCoreModel core) { string key = $"{vmName}_{core.CoreId}"; if (!_historyCache.TryGetValue(key, out var history)) { history = new LinkedList<double>(); for (int k = 0; k < MaxHistoryLength; k++) history.AddLast(0); _historyCache[key] = history; } history.AddLast(core.Usage); if (history.Count > MaxHistoryLength) history.RemoveFirst(); core.HistoryPoints = CalculatePoints(history); }
        private PointCollection CalculatePoints(LinkedList<double> history) { double w = 100.0, h = 100.0, step = w / (MaxHistoryLength - 1); var points = new PointCollection(MaxHistoryLength + 2) { new Point(0, h) }; int i = 0; foreach (var val in history) points.Add(new Point(i++ * step, h - (val * h / 100.0))); points.Add(new Point(w, h)); points.Freeze(); return points; }

        // ----------------------------------------------------------------------------------
        // CPU 设置与亲和性模块
        // ----------------------------------------------------------------------------------

        // 初始化可能的 vCPU 数量选项
        private void InitPossibleCpuCounts()
        {
            var options = new HashSet<int>();
            int maxCores = Environment.ProcessorCount;
            int current = 1;
            while (current <= maxCores) { options.Add(current); current *= 2; }
            options.Add(maxCores);
            PossibleVCpuCounts = new ObservableCollection<int>(options.OrderBy(x => x));
        }

        // 导航至 CPU 设置页面
        [RelayCommand]
        private async Task GoToCpuSettings()
        {
            if (SelectedVm == null) return;
            CurrentViewType = VmDetailViewType.CpuSettings;
            IsLoadingSettings = true;
            try
            {
                var settings = await _vmProcessorService.GetVmProcessorAsync(SelectedVm.Name);
                if (settings != null)
                {
                    SelectedVm.Processor = settings;
                    _originalSettingsCache = settings.Clone();
                }
            }
            catch (Exception ex) { ShowSnackbar("加载失败", Utils.GetFriendlyErrorMessages(ex.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24); }
            finally
            {
                await Task.Delay(200);
                IsLoadingSettings = false;
            }
        }

        // 应用 CPU 设置更改
        [RelayCommand]
        private async Task ApplyChangesAsync()
        {
            if (IsLoadingSettings || SelectedVm?.Processor == null) return;
            IsLoadingSettings = true;
            try
            {
                var result = await Task.Run(() => _vmProcessorService.SetVmProcessorAsync(SelectedVm.Name, SelectedVm.Processor));
                if (result.Success)
                    _originalSettingsCache = SelectedVm.Processor.Clone();
                else
                {
                    ShowSnackbar("应用失败", Utils.GetFriendlyErrorMessages(result.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    await GoToCpuSettings();
                }
            }
            catch (Exception ex)
            {
                ShowSnackbar("系统异常", Utils.GetFriendlyErrorMessages(ex.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                await GoToCpuSettings();
            }
            finally { IsLoadingSettings = false; }
        }

        // 导航至 CPU 亲和性页面
        [RelayCommand]
        private async Task GoToCpuAffinity()
        {
            if (SelectedVm == null) return;
            CurrentViewType = VmDetailViewType.CpuAffinity;
            IsLoadingSettings = true;

            try
            {
                int totalCores = Environment.ProcessorCount;
                var currentAffinity = await _cpuAffinityService.GetCpuAffinityAsync(SelectedVm.Id);

                var coresList = new List<VmCoreModel>();
                for (int i = 0; i < totalCores; i++)
                {
                    coresList.Add(new VmCoreModel
                    {
                        CoreId = i,
                        IsSelected = currentAffinity.Contains(i),
                        CoreType = CpuMonitorService.GetCoreType(i)
                    });
                }
                AffinityHostCores = new ObservableCollection<VmCoreModel>(coresList);

                int bestCols = 4;
                if (totalCores <= 4)
                {
                    bestCols = totalCores;
                }
                else
                {
                    double minPenalty = double.MaxValue;
                    for (int c = 4; c <= 10; c++)
                    {
                        int r = (int)Math.Ceiling((double)totalCores / c);
                        int remainder = (c - (totalCores % c)) % c;
                        double wasteScore = (double)remainder / c;
                        double aspect = (double)c / r;
                        double aspectScore = Math.Abs(aspect - 1.5);
                        double totalPenalty = (wasteScore * 2.0) + aspectScore;

                        if (totalPenalty < minPenalty)
                        {
                            minPenalty = totalPenalty;
                            bestCols = c;
                        }
                    }
                }

                AffinityColumns = bestCols;
                AffinityRows = (int)Math.Ceiling((double)totalCores / AffinityColumns);
            }
            catch (Exception ex)
            {
                ShowSnackbar("加载亲和性失败", Utils.GetFriendlyErrorMessages(ex.Message),
                    ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // 保存亲和性设置
        [RelayCommand]
        private async Task SaveAffinity()
        {
            if (SelectedVm == null || AffinityHostCores == null) return;
            IsLoadingSettings = true;
            try
            {
                // 1. 获取用户选中的核心索引列表
                var selectedIndices = AffinityHostCores.Where(c => c.IsSelected).Select(c => c.CoreId).ToList();

                // 2. 调用服务应用设置 (内部会自动判断调度器类型)
                bool success = await _cpuAffinityService.SetCpuAffinityAsync(SelectedVm.Id, selectedIndices, SelectedVm.IsRunning);

                // 3. 无论当前是否应用成功（Root 模式关机时会返回 false），我们都将配置持久化到 Notes
                string affinityStr = selectedIndices.Count > 0 ? string.Join(",", selectedIndices) : "";
                SelectedVm.Notes = Utils.UpdateTagValue(SelectedVm.Notes, "Affinity", affinityStr);
                await _queryService.SetVmOsTypeAsync(SelectedVm.Name, SelectedVm.OsType); // 复用保存 Notes 的逻辑

                if (success)
                {
                    ShowSnackbar("保存成功", "CPU 亲和性已实时应用并保存。", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                    GoToCpuSettings();
                }
                else
                {
                    // 如果是因为 Root 模式未开机导致无法实时应用
                    var scheduler = HyperVSchedulerService.GetSchedulerType();
                    if (scheduler == HyperVSchedulerType.Root && !SelectedVm.IsRunning)
                    {
                        ShowSnackbar("设定已排队", "当前为 Root 调度模式，亲和性已保存，将在虚拟机下次启动时自动生效。", ControlAppearance.Info, SymbolRegular.Clock24);
                        GoToCpuSettings();
                    }
                    else
                    {
                        ShowSnackbar("保存失败", "无法应用设置，请检查 HCS 权限或调度器状态。", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowSnackbar("错误", Utils.GetFriendlyErrorMessages(ex.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // ----------------------------------------------------------------------------------
        // 内存设置模块
        // ----------------------------------------------------------------------------------

        // 导航至内存设置
        [RelayCommand]
        private async Task GoToMemorySettings()
        {
            if (SelectedVm == null) return;
            CurrentViewType = VmDetailViewType.MemorySettings;
            IsLoadingSettings = true;
            try
            {
                var settings = await _vmMemoryService.GetVmMemorySettingsAsync(SelectedVm.Name);
                if (settings != null)
                {
                    if (SelectedVm.MemorySettings != null)
                        SelectedVm.MemorySettings.PropertyChanged -= MemorySettings_PropertyChanged;
                    SelectedVm.MemorySettings = settings;
                    SelectedVm.MemorySettings.PropertyChanged += MemorySettings_PropertyChanged;
                }
            }
            catch (Exception ex) { ShowSnackbar("错误", $"加载失败: {Utils.GetFriendlyErrorMessages(ex.Message)}", ControlAppearance.Danger, SymbolRegular.ErrorCircle24); }
            finally
            {
                await Task.Delay(200);
                IsLoadingSettings = false;
            }
        }

        // 监听内存属性变更以实现部分自动应用
        private async void MemorySettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var fastTrackProps = new[] { nameof(VmMemorySettings.BackingPageSize), nameof(VmMemorySettings.DynamicMemoryEnabled), nameof(VmMemorySettings.MemoryEncryptionPolicy) };
            if (fastTrackProps.Contains(e.PropertyName))
            {
                if (IsLoadingSettings || SelectedVm == null || SelectedVm.IsRunning || SelectedVm.MemorySettings == null)
                    return;

                IsLoadingSettings = true;
                try
                {
                    var result = await _vmMemoryService.SetVmMemorySettingsAsync(SelectedVm.Name, SelectedVm.MemorySettings);
                    if (!result.Success)
                    {
                        ShowSnackbar("自动应用失败", result.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                        await GoToMemorySettings();
                    }
                    else OnPropertyChanged(nameof(SelectedVm));
                }
                finally
                {
                    await Task.Delay(200);
                    IsLoadingSettings = false;
                }
            }
        }

        // 手动应用内存设置
        [RelayCommand]
        private async Task ApplyMemorySettings()
        {
            if (SelectedVm?.MemorySettings == null) return;
            IsLoadingSettings = true;
            try
            {
                var result = await _vmMemoryService.SetVmMemorySettingsAsync(SelectedVm.Name, SelectedVm.MemorySettings);
                if (!result.Success) ShowSnackbar("保存失败", Utils.GetFriendlyErrorMessages(result.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                await GoToMemorySettings();
            }
            catch (Exception ex) { ShowSnackbar("异常", Utils.GetFriendlyErrorMessages(ex.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24); }
            finally { IsLoadingSettings = false; }
        }

        // ----------------------------------------------------------------------------------
        // 存储管理模块 - 列表与基础操作
        // ----------------------------------------------------------------------------------

        // 导航至存储设置页面
        [RelayCommand]
        private async Task GoToStorageSettings()
        {
            if (SelectedVm == null) return;
            CurrentViewType = VmDetailViewType.StorageSettings;

            if (SelectedVm.StorageItems.Count == 0)
            {
                IsLoadingSettings = true;
                try
                {
                    await _storageService.LoadVmStorageItemsAsync(SelectedVm);
                    await LoadHostDisksAsync();
                }
                catch (Exception ex) { ShowSnackbar("加载存储失败", Utils.GetFriendlyErrorMessages(ex.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24); }
                finally { IsLoadingSettings = false; }
            }
        }

        // 加载宿主机物理磁盘列表
        private async Task LoadHostDisksAsync()
        {
            try
            {
                var disks = await _storageService.GetHostDisksAsync();
                Application.Current.Dispatcher.Invoke(() => HostDisks = new ObservableCollection<HostDiskInfo>(disks));
            }
            catch { }
        }

        // 移除存储设备
        [RelayCommand]
        private async Task RemoveStorageItem(VmStorageItem item)
        {
            if (SelectedVm == null || item == null) return;
            IsLoadingSettings = true;
            try
            {
                var result = await _storageService.RemoveDriveAsync(SelectedVm.Name, item);
                if (result.Success)
                {
                    ShowSnackbar("成功", result.Message == "Storage_Msg_Ejected" ? "光盘已弹出" : "设备已移除", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                    await _storageService.LoadVmStorageItemsAsync(SelectedVm);
                }
                else ShowSnackbar("移除失败", result.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            catch (Exception ex) { ShowSnackbar("错误", Utils.GetFriendlyErrorMessages(ex.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24); }
            finally { IsLoadingSettings = false; }
        }

        // 判断是否可以编辑存储路径
        private bool CanEditStorage(VmStorageItem item)
        {
            return item != null && item.DiskType != "Physical";
        }

        // 修改存储路径（换盘/换ISO）
        [RelayCommand(CanExecute = nameof(CanEditStorage))]
        private async Task EditStoragePath(VmStorageItem driveItem)
        {
            if (SelectedVm == null || driveItem == null) return;

            if (driveItem.DiskType == "Physical")
            {
                ShowSnackbar("操作受限", "物理直通磁盘无法修改路径，请移除后重新添加。", ControlAppearance.Danger, SymbolRegular.Warning24);
                return;
            }

            if (driveItem.DriveType == "HardDisk" && SelectedVm.IsRunning)
            {
                ShowSnackbar("操作受限", "无法在虚拟机运行时更换虚拟硬盘文件，请先关机。", ControlAppearance.Danger, SymbolRegular.Warning24);
                return;
            }

            string filter = driveItem.DriveType == "DvdDrive"
                ? "镜像文件 (*.iso)|*.iso|所有文件 (*.*)|*.*"
                : "虚拟磁盘 (*.vhdx;*.vhd)|*.vhdx;*.vhd|所有文件 (*.*)|*.*";

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = driveItem.DriveType == "DvdDrive" ? "选择 ISO 镜像" : "选择虚拟磁盘文件",
                Filter = filter
            };

            if (openFileDialog.ShowDialog() == true)
            {
                IsLoadingSettings = true;
                try
                {
                    (bool Success, string Message) result;

                    if (driveItem.DriveType == "DvdDrive")
                    {
                        result = await _storageService.ModifyDvdDrivePathAsync(
                            SelectedVm.Name,
                            driveItem.ControllerNumber,
                            driveItem.ControllerLocation,
                            openFileDialog.FileName);
                    }
                    else
                    {
                        result = await _storageService.ModifyHardDrivePathAsync(
                            SelectedVm.Name,
                            driveItem.ControllerType,
                            driveItem.ControllerNumber,
                            driveItem.ControllerLocation,
                            openFileDialog.FileName);
                    }

                    if (result.Success)
                    {
                        ShowSnackbar("修改成功", "存储路径已更新", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                        await _storageService.LoadVmStorageItemsAsync(SelectedVm);
                    }
                    else
                    {
                        ShowSnackbar("修改失败", result.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    }
                }
                catch (Exception ex)
                {
                    ShowSnackbar("错误", Utils.GetFriendlyErrorMessages(ex.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                }
                finally
                {
                    IsLoadingSettings = false;
                }
            }
        }

        // 判断路径是否可打开文件夹
        private bool CanOpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (int.TryParse(path, out _)) return false;
            if (path.StartsWith("PhysicalDisk", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // 在资源管理器中打开所在文件夹
        [RelayCommand(CanExecute = nameof(CanOpenFolder))]
        private void OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (int.TryParse(path, out _) || path.StartsWith("PhysicalDisk", StringComparison.OrdinalIgnoreCase)) return;

                if (System.IO.File.Exists(path) || System.IO.Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                else
                {
                    string directory = System.IO.Path.GetDirectoryName(path);
                    if (System.IO.Directory.Exists(directory))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", directory);
                    }
                }
            }
            catch (Exception) { }
        }

        // ----------------------------------------------------------------------------------
        // 存储管理模块 - 添加设备向导
        // ----------------------------------------------------------------------------------

        public int NewDiskSizeInt => int.TryParse(NewDiskSize, out int size) && size > 0 ? size : 128;

        public string FilePathPlaceholder => DeviceType == "HardDisk"
            ? "选择或输入 .vhdx / .vhd 文件路径"
            : "选择或输入 .iso 镜像文件路径";

        public string BrowseButtonText => IsNewDisk ? "保存到..." : "浏览...";

        // 属性变更监听 - 自动分配插槽
        partial void OnAutoAssignChanged(bool value)
        {
            if (value)
            {
                CalculateBestSlot();
            }
        }

        // 属性变更监听 - 磁盘大小
        partial void OnNewDiskSizeChanged(string value)
        {
            if (int.TryParse(value, out int size) && size <= 0)
            {
                NewDiskSize = "128";
            }
        }

        // 属性变更监听 - 是否新建磁盘
        partial void OnIsNewDiskChanged(bool value)
        {
            OnPropertyChanged(nameof(BrowseButtonText));
            FilePath = string.Empty;
        }

        // 属性变更监听 - 设备类型
        partial void OnDeviceTypeChanged(string value)
        {
            FilePath = string.Empty;
            IsoOutputPath = string.Empty;

            OnPropertyChanged(nameof(FilePathPlaceholder));
            OnPropertyChanged(nameof(BrowseButtonText));

            RefreshControllerOptions();

            if (AutoAssign) CalculateBestSlot();
            else UpdateAvailableLocations();
        }

        // 属性变更监听 - 控制器类型
        partial void OnSelectedControllerTypeChanged(string value)
        {
            if (_isInternalUpdating || value == null) return;

            Debug.WriteLine($"[DEBUG-STORAGE] [触发] 类型手动变更 -> {value}");
            RefreshAvailableNumbers(value);

            // 手动切换时也使用跳变技巧，确保 UI 同步
            SelectedControllerNumber = -2;
            SelectedControllerNumber = AvailableControllerNumbers.FirstOrDefault();

            UpdateAvailableLocations();
        }

        // 属性变更监听 - 控制器编号
        partial void OnSelectedControllerNumberChanged(int value)
        {
            // 如果是内部设定的跳变值 -2，或者是锁定状态，绝对不要去刷新位置列表，否则会造成闪烁或死循环
            if (value == -2 || _isInternalUpdating) return;

            Debug.WriteLine($"[DEBUG-STORAGE] [触发] 编号手动变更 -> {value}");
            UpdateAvailableLocations();
        }

        // 增加位置变更监听（用于观察是否有 UI 回传 null/默认值的情况）
        partial void OnSelectedLocationChanged(int value)
        {
            Debug.WriteLine($"[DEBUG-STORAGE] [通知] SelectedLocation 当前值为: {value}");
        }


        // 导航至添加存储向导
        [RelayCommand]
        private async Task GoToAddStorage()
        {
            if (SelectedVm == null) return;

            IsLoadingSettings = true;
            try
            {
                await _storageService.LoadVmStorageItemsAsync(SelectedVm);

                RefreshControllerOptions();

                if (AutoAssign) CalculateBestSlot();
                else UpdateAvailableLocations();

                CurrentViewType = VmDetailViewType.AddStorage;
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // 确认添加存储设备
        [RelayCommand]
        private async Task ConfirmAddStorage()
        {
            if (SelectedVm == null) return;

            // 检查插槽冲突
            bool collision = SelectedVm.StorageItems.Any(i =>
                i.ControllerType == SelectedControllerType &&
                i.ControllerNumber == SelectedControllerNumber &&
                i.ControllerLocation == SelectedLocation);

            if (collision)
            {
                ShowSnackbar("位置冲突", "该插槽已被占用，请选择其他位置。", ControlAppearance.Danger, SymbolRegular.Warning24);
                return;
            }

            // 根据设备类型和新建标志验证路径
            string target = IsPhysicalSource ? SelectedPhysicalDisk?.Number.ToString() : FilePath;

            if (string.IsNullOrEmpty(target) && !IsNewDisk)
            {
                ShowSnackbar("缺少参数", "请选择存储设备或文件。", ControlAppearance.Caution, SymbolRegular.Warning24);
                return;
            }

            // 验证 ISO 创建参数
            if (DeviceType == "DvdDrive" && IsNewDisk)
            {
                if (string.IsNullOrWhiteSpace(IsoSourceFolderPath))
                {
                    ShowSnackbar("缺少参数", "创建 ISO 时必须指定源文件夹。", ControlAppearance.Caution, SymbolRegular.Warning24);
                    return;
                }

                if (string.IsNullOrWhiteSpace(IsoOutputPath))
                {
                    ShowSnackbar("缺少参数", "创建 ISO 时必须指定保存路径。", ControlAppearance.Caution, SymbolRegular.Warning24);
                    return;
                }

                target = IsoOutputPath;

                var outputDir = Path.GetDirectoryName(IsoOutputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    try
                    {
                        Directory.CreateDirectory(outputDir);
                    }
                    catch (Exception ex)
                    {
                        ShowSnackbar("错误", $"无法创建输出目录: {ex.Message}", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                        return;
                    }
                }

                if (!Directory.Exists(IsoSourceFolderPath))
                {
                    ShowSnackbar("错误", "源文件夹不存在。", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    return;
                }
            }

            await AddDriveWrapperAsync(
                DeviceType,
                IsPhysicalSource,
                target,
                IsNewDisk,
                NewDiskSizeInt,
                SelectedVhdType,
                ParentPath,
                IsoSourceFolderPath,
                IsoVolumeLabel);

            CurrentViewType = VmDetailViewType.StorageSettings;
        }

        // 取消添加存储
        [RelayCommand]
        private void CancelAddStorage() => CurrentViewType = VmDetailViewType.StorageSettings;

        // 浏览文件
        [RelayCommand]
        private void BrowseFile()
        {
            if (IsNewDisk && DeviceType == "HardDisk")
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "创建虚拟磁盘",
                    Filter = "虚拟磁盘 (*.vhdx)|*.vhdx|旧版虚拟磁盘 (*.vhd)|*.vhd",
                    DefaultExt = ".vhdx",
                    FileName = "NewVirtualDisk.vhdx"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    FilePath = saveDialog.FileName;
                }
            }
            else
            {
                var openDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = DeviceType == "HardDisk" ? "选择虚拟磁盘" : "选择 ISO 镜像",
                    Filter = DeviceType == "HardDisk" ?
                             "虚拟磁盘 (*.vhdx;*.vhd)|*.vhdx;*.vhd" :
                             "镜像文件 (*.iso)|*.iso"
                };

                if (openDialog.ShowDialog() == true)
                {
                    FilePath = openDialog.FileName;
                }
            }
        }

        // 浏览文件夹 (用于ISO制作)
        [RelayCommand]
        private void BrowseFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true) IsoSourceFolderPath = dialog.FolderName;
        }

        // 浏览父级磁盘
        [RelayCommand]
        private void BrowseParentFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "虚拟磁盘 (*.vhdx;*.vhd)|*.vhdx;*.vhd" };
            if (dialog.ShowDialog() == true) ParentPath = dialog.FileName;
        }

        // 浏览保存ISO路径
        [RelayCommand]
        private void BrowseSaveIso()
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存 ISO 镜像文件",
                Filter = "ISO 镜像 (*.iso)|*.iso",
                DefaultExt = ".iso",
                FileName = $"{IsoVolumeLabel}.iso"
            };

            if (saveDialog.ShowDialog() == true)
            {
                IsoOutputPath = saveDialog.FileName;
            }
        }

        // 添加驱动器的包装函数
        public async Task AddDriveWrapperAsync(string driveType, bool isPhysical, string pathOrNumber, bool isNew, int sizeGb = 128, string vhdType = "Dynamic", string parentPath = "", string isoSourcePath = null, string isoVolumeLabel = null)
        {
            if (SelectedVm == null) return;
            IsLoadingSettings = true;
            try
            {
                // --- 核心修复：直接读取 UI 属性，不再调用后端 GetNextAvailableSlotAsync ---
                string targetType = SelectedControllerType;
                int targetNumber = SelectedControllerNumber;
                int targetLocation = SelectedLocation;

                Debug.WriteLine($"[STORAGE-ACTION] 执行添加操作: {driveType} -> 控制器:{targetType} #{targetNumber} 位置:{targetLocation}");

                if (isPhysical && int.TryParse(pathOrNumber, out int diskNum))
                    await _storageService.SetDiskOfflineStatusAsync(diskNum, true);

                var result = await _storageService.AddDriveAsync(
                    vmName: SelectedVm.Name,
                    controllerType: targetType,   // 传递界面显示的值
                    controllerNumber: targetNumber, // 传递界面显示的值
                    location: targetLocation,       // 传递界面显示的值
                    driveType: driveType,
                    pathOrNumber: pathOrNumber,
                    isPhysical: isPhysical,
                    isNew: isNew,
                    sizeGb: sizeGb,
                    vhdType: vhdType,
                    parentPath: parentPath,
                    sectorFormat: SectorFormat,
                    blockSize: BlockSize,
                    isoSourcePath: isoSourcePath,
                    isoVolumeLabel: isoVolumeLabel
                );

                if (result.Success)
                {
                    ShowSnackbar("添加成功", $"设备已连接到 {result.ActualType} {result.ActualNumber}:{result.ActualLocation}", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                    await _storageService.LoadVmStorageItemsAsync(SelectedVm);
                }
                else
                {
                    ShowSnackbar("添加失败", result.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                }
            }
            catch (Exception ex)
            {
                ShowSnackbar("异常", Utils.GetFriendlyErrorMessages(ex.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }
        // 计算最佳可用插槽
        private void CalculateBestSlot()
        {
            if (SelectedVm == null) return;

            bool isRunning = SelectedVm.IsRunning;
            bool isGen1 = SelectedVm.Generation == 1;
            bool isDvd = DeviceType == "DvdDrive";

            // --- 核心修复：开始计算前清除旧警告 ---
            IsSlotValid = true;
            SlotWarningMessage = string.Empty;

            // 1. 处理 1 代机光驱的特殊物理约束
            if (isGen1 && isDvd)
            {
                if (isRunning)
                {
                    IsSlotValid = false;
                    SlotWarningMessage = "第 1 代虚拟机的光盘无法在运行状态下添加（IDE 不支持热插拔）。请先关机。";
                    return;
                }

                for (int c = 0; c < 2; c++)
                {
                    for (int l = 0; l < 2; l++)
                    {
                        if (!IsSlotOccupied("IDE", c, l)) { SetSlot("IDE", c, l); return; }
                    }
                }
                IsSlotValid = false;
                SlotWarningMessage = "第 1 代虚拟机的 IDE 控制器已满，无法添加光盘。";
                return;
            }

            // 2. 运行中或二代机 -> 找 SCSI
            if (isRunning || !isGen1)
            {
                for (int c = 0; c < 4; c++)
                {
                    for (int l = 0; l < 64; l++)
                    {
                        if (!IsSlotOccupied("SCSI", c, l)) { SetSlot("SCSI", c, l); return; }
                    }
                }
                IsSlotValid = false;
                SlotWarningMessage = isRunning ? "运行中的虚拟机没有可用的 SCSI 插槽。" : "没有可用的 SCSI 插槽。";
                return;
            }

            // 3. 关机状态的 1 代机硬盘 -> 优先 IDE
            if (isGen1)
            {
                for (int c = 0; c < 2; c++)
                {
                    for (int l = 0; l < 2; l++)
                    {
                        if (!IsSlotOccupied("IDE", c, l)) { SetSlot("IDE", c, l); return; }
                    }
                }
            }

            // 4. 兜底方案：最后尝试 SCSI
            for (int c = 0; c < 4; c++)
            {
                for (int l = 0; l < 64; l++)
                {
                    if (!IsSlotOccupied("SCSI", c, l)) { SetSlot("SCSI", c, l); return; }
                }
            }

            IsSlotValid = false;
            SlotWarningMessage = "该虚拟机没有可用的存储插槽。";
        }
        // 检查插槽是否被占用
        private bool IsSlotOccupied(string type, int ctrlNum, int loc)
        {
            return SelectedVm.StorageItems.Any(i =>
                i.ControllerType == type &&
                i.ControllerNumber == ctrlNum &&
                i.ControllerLocation == loc);
        }

        // 设置当前选中的插槽
        private void SetSlot(string type, int ctrlNum, int loc)
        {
            Debug.WriteLine($"[DEBUG-STORAGE] >>> 开始自动分配: {type} #{ctrlNum} Loc:{loc}");

            _isInternalUpdating = true; // 锁定拦截器
            try
            {
                // 1. 设置接口类型并立即刷新列表数据源
                SelectedControllerType = type;
                RefreshAvailableNumbers(type);
                RefreshAvailableLocations(type, ctrlNum);

                // 2. 关键步骤：使用 Dispatcher 确保 UI 已处理完 ItemsSource 的变更通知
                // 使用 Loaded 优先级，这会等待 ComboBox 完成内部项的生成
                Application.Current.Dispatcher.BeginInvoke(new Action(() => {

                    // --- 强刷 [编号] ---
                    var targetNum = AvailableControllerNumbers.Contains(ctrlNum) ? ctrlNum : (AvailableControllerNumbers.Count > 0 ? AvailableControllerNumbers[0] : 0);

                    // 用 -2 强制触发 PropertyChanged，因为 -1 可能已经是当前 UI 的内部错误状态
                    SelectedControllerNumber = -2;
                    SelectedControllerNumber = targetNum;
                    Debug.WriteLine($"[DEBUG-STORAGE] 编号强刷完成 -> {SelectedControllerNumber}");

                    // --- 强刷 [位置] ---
                    SelectedLocation = -2;
                    if (AvailableLocations.Contains(loc))
                    {
                        SelectedLocation = loc;
                    }
                    else if (AvailableLocations.Count > 0)
                    {
                        SelectedLocation = AvailableLocations[0];
                    }
                    Debug.WriteLine($"[DEBUG-STORAGE] 位置强刷完成 -> {SelectedLocation}");

                    IsSlotValid = true;
                    SlotWarningMessage = string.Empty;

                    // 全部完成后解锁
                    _isInternalUpdating = false;
                    Debug.WriteLine("[DEBUG-STORAGE] <<< 自动分配与 UI 同步全部结束");

                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                _isInternalUpdating = false;
                Debug.WriteLine($"[DEBUG-STORAGE] SetSlot 异常: {ex.Message}");
            }
        }

        private void RefreshAvailableNumbers(string type)
        {
            Debug.WriteLine($"[DEBUG-STORAGE] 正在刷新 [编号] 列表，类型: {type}");
            AvailableControllerNumbers.Clear();
            int maxCtrl = (type == "IDE") ? 2 : 4;
            for (int i = 0; i < maxCtrl; i++)
                AvailableControllerNumbers.Add(i);
        }

        private void RefreshAvailableLocations(string type, int ctrlNum)
        {
            if (SelectedVm == null || type == null) return;

            Debug.WriteLine($"[DEBUG-STORAGE] 正在刷新 [位置] 列表: {type}, #{ctrlNum}");
            var usedLocations = SelectedVm.StorageItems
                .Where(i => i.ControllerType == type && i.ControllerNumber == ctrlNum)
                .Select(i => i.ControllerLocation)
                .ToHashSet();

            int maxLoc = (type == "IDE") ? 2 : 64;
            AvailableLocations.Clear();
            for (int i = 0; i < maxLoc; i++)
            {
                if (!usedLocations.Contains(i)) AvailableLocations.Add(i);
            }
        }
        // 更新可用的位置列表
        private void UpdateAvailableLocations()
        {
            if (_isInternalUpdating) return;

            if (SelectedVm == null || string.IsNullOrEmpty(SelectedControllerType)) return;

            // --- 核心修复：每次计算前先重置错误状态 ---
            IsSlotValid = true;
            SlotWarningMessage = string.Empty;

            RefreshAvailableLocations(SelectedControllerType, SelectedControllerNumber);

            // 如果该控制器下确实一个位置都没有
            if (AvailableLocations.Count == 0)
            {
                SelectedLocation = -1;
                IsSlotValid = false;
                SlotWarningMessage = $"控制器 {SelectedControllerType} #{SelectedControllerNumber} 已满";
                Debug.WriteLine($"[DEBUG-STORAGE] 验证失败: {SelectedControllerType} #{SelectedControllerNumber} 已满");
                return;
            }

            // 如果当前位置不在新列表中，重置为第一个可用位置
            if (!AvailableLocations.Contains(SelectedLocation))
            {
                SelectedLocation = AvailableLocations[0];
                Debug.WriteLine($"[DEBUG-STORAGE] 手动切换后重置位置为: {SelectedLocation}");
            }
        }
        // 刷新控制器选项
        private void RefreshControllerOptions()
        {
            if (SelectedVm == null) return;

            bool isGen1 = SelectedVm.Generation == 1;
            bool isDvd = DeviceType == "DvdDrive";

            AvailableControllerTypes.Clear();

            // --- 核心物理约束逻辑 ---
            if (isGen1)
            {
                if (isDvd)
                {
                    // 法则 1：1 代机光驱必须在 IDE 上
                    AvailableControllerTypes.Add("IDE");
                }
                else
                {
                    // 1 代机硬盘
                    if (SelectedVm.IsRunning)
                    {
                        // 法则 2：运行中只能热插拔 SCSI
                        AvailableControllerTypes.Add("SCSI");
                    }
                    else
                    {
                        // 关机状态，IDE 和 SCSI 都可以
                        AvailableControllerTypes.Add("IDE");
                        AvailableControllerTypes.Add("SCSI");
                    }
                }
            }
            else
            {
                // 法则 3：2 代机永远只有 SCSI
                AvailableControllerTypes.Add("SCSI");
            }

            // 纠正当前选中项
            if (!AvailableControllerTypes.Contains(SelectedControllerType))
            {
                SelectedControllerType = AvailableControllerTypes.FirstOrDefault() ?? "SCSI";
            }
            else
            {
                // 强制刷新一次编号列表
                OnSelectedControllerTypeChanged(SelectedControllerType);
            }
        }
        // ----------------------------------------------------------------------------------
        // 网络设置模块
        // ----------------------------------------------------------------------------------

        // 导航至网络设置
        [RelayCommand]
        private async Task GoToNetworkSettings()
        {
            if (SelectedVm == null) return;

            CurrentViewType = VmDetailViewType.NetworkSettings;
            IsLoadingSettings = true;

            try
            {
                var switchesTask = _vmNetworkService.GetAvailableSwitchesAsync();
                var adaptersTask = _vmNetworkService.GetNetworkAdaptersAsync(SelectedVm.Name);

                await Task.WhenAll(switchesTask, adaptersTask);

                if (!AvailableSwitchNames.SequenceEqual(switchesTask.Result))
                {
                    AvailableSwitchNames = new ObservableCollection<string>(switchesTask.Result);
                }

                var firstAdapter = adaptersTask.Result.FirstOrDefault();
                System.Diagnostics.Debug.WriteLine($"[DEBUG] GoToNetworkSettings is syncing. IsConnected = {firstAdapter?.IsConnected}");
                SyncNetworkAdaptersInternal(SelectedVm.NetworkAdapters, adaptersTask.Result);

                // IP 探测
                if (SelectedVm.IsRunning)
                {
                    _ = Task.Run(async () => {
                        await _vmNetworkService.FillDynamicIpsAsync(SelectedVm.Name, SelectedVm.NetworkAdapters);
                    });
                }
            }
            catch (Exception ex)
            {
                ShowSnackbar("加载失败", ex.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                await Task.Delay(300);
                IsLoadingSettings = false;
            }
        }

        // 智能同步网卡列表，避免 UI 闪烁
        private void SyncNetworkAdaptersInternal(ObservableCollection<VmNetworkAdapter> currentList, List<VmNetworkAdapter> newList)
        {
            if (newList == null) return;

            // 1. 移除已经不存在的网卡
            var toRemove = currentList.Where(c => !newList.Any(n => n.Id == c.Id)).ToList();
            foreach (var item in toRemove)
            {
                currentList.Remove(item);
            }

            // 2. 更新现有的 或 添加新的
            foreach (var newItem in newList)
            {
                var existingItem = currentList.FirstOrDefault(c => c.Id == newItem.Id);
                if (existingItem != null)
                {
                    // === 存在则更新属性 ===
                    existingItem.Name = newItem.Name;
                    existingItem.IsConnected = newItem.IsConnected;
                    existingItem.SwitchName = newItem.SwitchName;
                    existingItem.MacAddress = newItem.MacAddress;
                    existingItem.IsStaticMac = newItem.IsStaticMac;

                    if (newItem.IpAddresses != null && newItem.IpAddresses.Count > 0)
                    {
                        existingItem.IpAddresses = newItem.IpAddresses;
                    }

                    // VLAN 设置
                    existingItem.VlanMode = newItem.VlanMode;
                    existingItem.AccessVlanId = newItem.AccessVlanId;
                    existingItem.NativeVlanId = newItem.NativeVlanId;
                    existingItem.TrunkAllowedVlanIds = newItem.TrunkAllowedVlanIds;
                    existingItem.PvlanMode = newItem.PvlanMode;
                    existingItem.PvlanPrimaryId = newItem.PvlanPrimaryId;
                    existingItem.PvlanSecondaryId = newItem.PvlanSecondaryId;

                    // 带宽与安全
                    existingItem.BandwidthLimit = newItem.BandwidthLimit;
                    existingItem.BandwidthReservation = newItem.BandwidthReservation;
                    existingItem.MacSpoofingAllowed = newItem.MacSpoofingAllowed;
                    existingItem.DhcpGuardEnabled = newItem.DhcpGuardEnabled;
                    existingItem.RouterGuardEnabled = newItem.RouterGuardEnabled;
                    existingItem.MonitorMode = newItem.MonitorMode;
                    existingItem.StormLimit = newItem.StormLimit;
                    existingItem.TeamingAllowed = newItem.TeamingAllowed;

                    // 硬件卸载
                    existingItem.VmqEnabled = newItem.VmqEnabled;
                    existingItem.SriovEnabled = newItem.SriovEnabled;
                    existingItem.IpsecOffloadEnabled = newItem.IpsecOffloadEnabled;
                }
                else
                {
                    currentList.Add(newItem);
                }
            }
        }

        // 添加新的网络适配器
        [RelayCommand]
        private async Task AddNetworkAdapter()
        {
            if (SelectedVm == null) return;
            IsLoadingSettings = true;
            try
            {
                var result = await _vmNetworkService.AddNetworkAdapterAsync(SelectedVm.Name);

                if (result.Success)
                {
                    ShowSnackbar("添加成功", "已添加新的网络适配器", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                    await GoToNetworkSettings();
                }
                else
                {
                    ShowSnackbar("添加失败", Utils.GetFriendlyErrorMessages(result.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                }
            }
            catch (Exception ex)
            {
                ShowSnackbar("添加异常", Utils.GetFriendlyErrorMessages(ex.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // 移除网络适配器
        [RelayCommand]
        private async Task RemoveNetworkAdapter(string adapterId)
        {
            if (SelectedVm == null || string.IsNullOrEmpty(adapterId)) return;

            IsLoadingSettings = true;
            try
            {
                var result = await _vmNetworkService.RemoveNetworkAdapterAsync(SelectedVm.Name, adapterId);

                if (result.Success)
                {
                    ShowSnackbar("移除成功", "网络适配器已移除", ControlAppearance.Success, SymbolRegular.Delete24);
                    await GoToNetworkSettings();
                }
                else
                {
                    ShowSnackbar("移除失败", Utils.GetFriendlyErrorMessages(result.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                }
            }
            catch (Exception ex)
            {
                ShowSnackbar("移除异常", Utils.GetFriendlyErrorMessages(ex.Message), ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // 更新网卡连接状态
        [RelayCommand]
        private async Task UpdateAdapterConnection(VmNetworkAdapter adapter)
        {
            if (SelectedVm == null || adapter == null) return;
            IsLoadingSettings = true;
            try
            {
                var result = await _vmNetworkService.UpdateConnectionAsync(SelectedVm.Name, adapter);
                if (!result.Success)
                {
                    ShowSnackbar("操作失败", result.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    adapter.IsConnected = !adapter.IsConnected;
                }
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // 应用 VLAN 设置
        [RelayCommand]
        private async Task ApplyVlanSettings(VmNetworkAdapter adapter)
        {
            if (SelectedVm == null || adapter == null) return;
            IsLoadingSettings = true;
            try
            {
                var result = await _vmNetworkService.ApplyVlanSettingsAsync(SelectedVm.Name, adapter);
                if (result.Success) ShowSnackbar("成功", "VLAN 设置已应用", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                else ShowSnackbar("失败", result.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // 应用 QoS 设置
        [RelayCommand]
        private async Task ApplyQosSettings(VmNetworkAdapter adapter)
        {
            if (SelectedVm == null || adapter == null) return;
            IsLoadingSettings = true;
            try
            {
                var result = await _vmNetworkService.ApplyBandwidthSettingsAsync(SelectedVm.Name, adapter);
                if (result.Success) ShowSnackbar("成功", "流量控制设置已应用", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                else ShowSnackbar("失败", result.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // 应用安全与监控设置
        [RelayCommand]
        private async Task ApplySecuritySettings(VmNetworkAdapter adapter)
        {
            if (SelectedVm == null || adapter == null) return;
            IsLoadingSettings = true;
            try
            {
                var secResult = await _vmNetworkService.ApplySecuritySettingsAsync(SelectedVm.Name, adapter);
                if (!secResult.Success)
                {
                    ShowSnackbar("失败", $"安全设置应用失败: {secResult.Message}", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    return;
                }

                var offloadResult = await _vmNetworkService.ApplyOffloadSettingsAsync(SelectedVm.Name, adapter);
                if (!offloadResult.Success)
                {
                    ShowSnackbar("失败", $"硬件加速设置应用失败: {offloadResult.Message}", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    return;
                }

                ShowSnackbar("成功", "所有相关设置已应用", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // 切换硬件加速设置
        [RelayCommand]
        private async Task ToggleOffloadSetting(VmNetworkAdapter adapter)
        {
            if (SelectedVm == null || adapter == null) return;
            var result = await _vmNetworkService.ApplyOffloadSettingsAsync(SelectedVm.Name, adapter);
            if (!result.Success)
            {
                ShowSnackbar("设置应用失败", result.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
        }

        // 切换安全防护设置
        [RelayCommand]
        private async Task ToggleSecuritySetting(VmNetworkAdapter adapter)
        {
            if (SelectedVm == null || adapter == null) return;
            var result = await _vmNetworkService.ApplySecuritySettingsAsync(SelectedVm.Name, adapter);
            if (!result.Success)
            {
                ShowSnackbar("安全设置应用失败", result.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
        }

        // ----------------------------------------------------------------------------------
        // GPU 管理模块 - 列表与基础操作
        // ----------------------------------------------------------------------------------

        // 导航至 GPU 管理页面
        [RelayCommand]
        private async Task GoToGpuSettings()
        {
            if (SelectedVm == null) return;

            CurrentViewType = VmDetailViewType.GpuSettings;
            IsLoadingSettings = true;
            try
            {
                await RefreshCurrentVmGpuAssignments();
            }
            catch (Exception ex)
            {
                ShowSnackbar("加载失败", "无法读取 GPU 配置信息: " + ex.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // 刷新当前虚拟机的显卡分配情况
        private async Task RefreshCurrentVmGpuAssignments()
        {
            if (SelectedVm == null) return;
            try
            {
                var vmAdapters = await _vmGpuService.GetVmGpuAdaptersAsync(SelectedVm.Name);
                var hostGpus = await _vmGpuService.GetHostGpusAsync();

                var tempList = new List<VmGpuAssignment>();

                foreach (var adapter in vmAdapters)
                {
                    var matchedHostGpu = hostGpus.FirstOrDefault(h =>
                        !string.IsNullOrEmpty(h.InstanceId) &&
                        !string.IsNullOrEmpty(adapter.InstancePath) &&
                        (adapter.InstancePath.Contains(h.InstanceId, StringComparison.OrdinalIgnoreCase) ||
                         NormalizeDeviceId(h.InstanceId) == NormalizeDeviceId(adapter.InstancePath)));

                    var assignment = new VmGpuAssignment { AdapterId = adapter.Id, InstanceId = adapter.InstancePath };

                    if (matchedHostGpu != null)
                    {
                        assignment.Name = matchedHostGpu.Name;
                        assignment.Manu = matchedHostGpu.Manu;
                        assignment.Vendor = matchedHostGpu.Vendor;
                        assignment.DriverVersion = matchedHostGpu.DriverVersion;
                        assignment.Ram = matchedHostGpu.Ram;
                        assignment.PName = matchedHostGpu.Pname;
                    }
                    else
                    {
                        assignment.Name = "Unknown Device";
                        assignment.Manu = "Default";
                    }
                    tempList.Add(assignment);
                }

                Application.Current.Dispatcher.Invoke(() => {
                    bool isHardwareSame = SelectedVm.AssignedGpus.Count == tempList.Count &&
                                         SelectedVm.AssignedGpus.Select(x => x.AdapterId)
                                                      .SequenceEqual(tempList.Select(x => x.AdapterId));

                    if (isHardwareSame)
                    {
                        for (int i = 0; i < tempList.Count; i++)
                        {
                            var target = SelectedVm.AssignedGpus[i];
                            var source = tempList[i];
                            target.Name = source.Name;
                            target.Manu = source.Manu;
                            target.Vendor = source.Vendor;
                            target.DriverVersion = source.DriverVersion;
                            target.Ram = source.Ram;
                            target.PName = source.PName;
                        }
                    }
                    else
                    {
                        SelectedVm.AssignedGpus.Clear();
                        foreach (var item in tempList) SelectedVm.AssignedGpus.Add(item);
                    }

                    SelectedVm.RefreshGpuSummary();
                });
            }
            catch (Exception ex)
            {
                ShowSnackbar("刷新显卡失败", ex.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
        }

        // 移除 GPU 分区
        [RelayCommand]
        private async Task RemoveGpu(string adapterId)
        {
            if (SelectedVm == null || string.IsNullOrEmpty(adapterId)) return;

            var itemToRemove = SelectedVm.AssignedGpus.FirstOrDefault(x => x.AdapterId == adapterId);
            if (itemToRemove == null) return;

            IsLoadingSettings = true;
            try
            {
                bool success = await _vmGpuService.RemoveGpuPartitionAsync(SelectedVm.Name, adapterId);

                if (success)
                {
                    Application.Current.Dispatcher.Invoke(() => {
                        SelectedVm.AssignedGpus.Remove(itemToRemove);
                        if (SelectedVm.AssignedGpus.Count == 0)
                        {
                            SelectedVm.GpuName = string.Empty;
                        }
                    });

                    ShowSnackbar("成功", "GPU 分区已从虚拟机移除。", ControlAppearance.Success, SymbolRegular.Checkmark24);

                    await Task.Delay(2000);
                    await RefreshCurrentVmGpuAssignments();
                }
                else
                {
                    ShowSnackbar("移除失败", "未能成功移除 GPU 分区，请检查权限。", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                }
            }
            catch (Exception ex)
            {
                ShowSnackbar("操作异常", ex.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // ----------------------------------------------------------------------------------
        // GPU 管理模块 - 部署向导与自动化
        // ----------------------------------------------------------------------------------

        // 导航至添加 GPU 向导
        [RelayCommand]
        private async Task GoToAddGpu()
        {
            if (SelectedVm == null) return;

            IsLoadingSettings = true;
            try
            {
                var gpus = await _vmGpuService.GetHostGpusAsync();
                HostGpus = new ObservableCollection<GPUInfo>(gpus);
                SelectedHostGpu = null;

                CurrentViewType = VmDetailViewType.AddGpuSelect;
            }
            catch (Exception ex)
            {
                ShowSnackbar("错误", "无法加载宿主机显卡: " + ex.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
            finally
            {
                IsLoadingSettings = false;
            }
        }

        // 取消添加 GPU
        [RelayCommand]
        private void CancelAddGpu()
        {
            CurrentViewType = VmDetailViewType.GpuSettings;
            GpuTasks.Clear();
        }

        // 检查是否可以确认添加
        private bool CanConfirmAddGpu() => SelectedHostGpu != null;

        // 确认添加 GPU 并开始流程
        [RelayCommand(CanExecute = nameof(CanConfirmAddGpu))]
        private async Task ConfirmAddGpu()
        {
            if (SelectedHostGpu == null) return;

            CurrentViewType = VmDetailViewType.AddGpuProgress;
            ShowPartitionSelector = false;

            GpuDeploymentLog = string.Empty;
            ShowLogConsole = true;

            AppendLog($">>> 开始为虚拟机 [{SelectedVm.Name}] 配置 GPU 分区");
            AppendLog($"选中物理显卡: {SelectedHostGpu.Name}");
            AppendLog($"设备路径: {SelectedHostGpu.Pname}");

            GpuTasks.Clear();
            GpuTasks.Add(new TaskItem { Name = "环境准备", Description = "正在准备宿主机环境...", Status = ExHyperV.Models.TaskStatus.Pending });
            GpuTasks.Add(new TaskItem { Name = "电源检查", Description = "检查虚拟机电源状态...", Status = ExHyperV.Models.TaskStatus.Pending });
            GpuTasks.Add(new TaskItem { Name = "系统优化", Description = "正在配置 MMIO 地址空间...", Status = ExHyperV.Models.TaskStatus.Pending });
            GpuTasks.Add(new TaskItem { Name = "分配显卡", Description = "正在创建 GPU 分区...", Status = ExHyperV.Models.TaskStatus.Pending });

            if (AutoInstallDrivers)
            {
                GpuTasks.Add(new TaskItem { Name = "驱动安装", Description = "等待扫描分区...", Status = ExHyperV.Models.TaskStatus.Pending });
            }

            await RunRealGpuWorkflowAsync(0);
        }

        // 执行 GPU 部署工作流
        private async Task RunRealGpuWorkflowAsync(int startIndex)
        {
            var tasks = GpuTasks;

            for (int i = startIndex; i < tasks.Count; i++)
            {
                var task = tasks[i];
                task.Status = ExHyperV.Models.TaskStatus.Running;
                AppendLog($"正在执行: {task.Name}...");

                try
                {
                    switch (task.Name)
                    {
                        case "环境准备":
                            await _vmGpuService.PrepareHostEnvironmentAsync();
                            task.Description = "宿主机策略已成功应用。";
                            break;

                        case "电源检查":
                            var (isOff, state) = await _vmGpuService.IsVmPoweredOffAsync(SelectedVm.Name);
                            if (!isOff)
                            {
                                task.Description = $"当前状态: {state}。正在强制关闭虚拟机...";
                                AppendLog(task.Description);
                                await _powerService.ExecuteControlActionAsync(SelectedVm.Name, "TurnOff");
                                while (!(await _vmGpuService.IsVmPoweredOffAsync(SelectedVm.Name)).IsOff)
                                {
                                    await Task.Delay(1000);
                                }
                            }
                            task.Description = "虚拟机已就绪 (Off)。";
                            break;

                        case "系统优化":
                            bool optOk = await _vmGpuService.OptimizeVmForGpuAsync(SelectedVm.Name);
                            task.Description = optOk ? "MMIO 地址空间配置完成 (64GB High / 1GB Low)。" : "优化配置失败，将尝试继续。";
                            break;

                        case "分配显卡":
                            string targetPath = !string.IsNullOrEmpty(SelectedHostGpu.Pname)
                                                ? SelectedHostGpu.Pname
                                                : SelectedHostGpu.InstanceId;

                            var assignRes = await _vmGpuService.AssignGpuPartitionAsync(SelectedVm.Name, targetPath);
                            if (!assignRes.Success) throw new Exception(assignRes.Message);
                            task.Description = "GPU 分区已成功创建并绑定。";
                            break;

                        case "驱动安装":
                            task.Description = "正在分析虚拟机磁盘分区...";
                            AppendLog(task.Description);
                            var partitions = await _vmGpuService.GetPartitionsFromVmAsync(SelectedVm.Name);

                            if (partitions.Count == 1 && partitions[0].OsType == OperatingSystemType.Windows)
                            {
                                task.Description = "已识别到 Windows 主分区，正在同步驱动...";
                                var syncRes = await _vmGpuService.SyncWindowsDriversAsync(
                                    SelectedVm.Name,
                                    SelectedHostGpu.Pname,
                                    SelectedHostGpu.Manu,
                                    partitions[0],
                                    msg => {
                                        task.Description = msg;
                                        AppendLog(msg);
                                    });

                                if (!syncRes.Success) throw new Exception(syncRes.Message);
                                task.Description = "Windows 驱动安装成功。";
                            }
                            else
                            {
                                Application.Current.Dispatcher.Invoke(() => {
                                    DetectedPartitions = new ObservableCollection<PartitionInfo>(partitions);
                                    ShowPartitionSelector = true;
                                    ShowSshForm = false;
                                });
                                task.Description = "检测到多个环境或 Linux，等待用户选择注入目标...";
                                AppendLog(task.Description);
                                return;
                            }
                            break;
                    }

                    task.Status = ExHyperV.Models.TaskStatus.Success;
                    AppendLog($"[成功] {task.Name}: {task.Description}");
                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    task.Status = ExHyperV.Models.TaskStatus.Failed;
                    task.Description = $"失败: {ex.Message}";
                    AppendLog($"[错误] {task.Name} 环节异常: {ex.Message}");
                    ShowSnackbar("操作失败", $"{task.Name} 环节出现异常", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    return;
                }
            }

            await FinishWorkflowAsync();
        }

        // 选择分区并继续驱动安装
        [RelayCommand]
        private async Task SelectPartitionAndContinue(PartitionInfo partition)
        {
            var driveTask = GpuTasks.FirstOrDefault(t => t.Name == "驱动安装");
            if (driveTask == null) return;

            if (partition.OsType == OperatingSystemType.Windows)
            {
                ShowPartitionSelector = false;
                driveTask.Status = ExHyperV.Models.TaskStatus.Running;
                driveTask.Description = $"正在同步驱动至分区 {partition.PartitionNumber}...";
                AppendLog(driveTask.Description);

                var result = await _vmGpuService.SyncWindowsDriversAsync(
                    SelectedVm.Name,
                    SelectedHostGpu.Pname,
                    SelectedHostGpu.Manu,
                    partition,
                    msg => {
                        driveTask.Description = msg;
                        AppendLog(msg);
                    });

                if (result.Success)
                {
                    driveTask.Status = ExHyperV.Models.TaskStatus.Success;
                    await FinishWorkflowAsync();
                }
                else
                {
                    driveTask.Status = ExHyperV.Models.TaskStatus.Failed;
                    driveTask.Description = result.Message;
                    AppendLog($"[错误] Windows 驱动同步失败: {result.Message}");
                }
            }
            else if (partition.OsType == OperatingSystemType.Linux)
            {
                SelectedPartition = partition;
                IsLoadingSettings = true;
                driveTask.Description = "正在分析 Linux 环境并获取网络地址...";
                AppendLog($">>> 用户选择了 Linux 分区: {partition.DisplayName}");

                try
                {
                    var (pHost, pPort) = Utils.GetWindowsSystemProxy();
                    SshProxyHost = pHost;
                    SshProxyPort = pPort;
                    if (!string.IsNullOrEmpty(pHost)) AppendLog($"已自动识别系统代理: {pHost}:{pPort}");

                    var status = await _vmGpuService.IsVmPoweredOffAsync(SelectedVm.Name);
                    if (status.IsOff)
                    {
                        driveTask.Description = "正在自动启动虚拟机以初始化网络...";
                        AppendLog(driveTask.Description);
                        await _powerService.ExecuteControlActionAsync(SelectedVm.Name, "Start");
                        await Task.Delay(3000);
                    }

                    driveTask.Description = "正在嗅探虚拟机 IP 地址...";
                    AppendLog(driveTask.Description);

                    string vmIp = await Task.Run(async () =>
                    {
                        string getMacScript = $"(Get-VMNetworkAdapter -VMName '{SelectedVm.Name}').MacAddress | Select-Object -First 1";
                        var macResult = Utils.Run(getMacScript);

                        if (macResult != null && macResult.Count > 0)
                        {
                            string rawMac = macResult[0].ToString();
                            string formattedMac = System.Text.RegularExpressions.Regex.Replace(rawMac, "(.{2})", "$1:").TrimEnd(':');
                            AppendLog($"获取到 MAC 地址: {formattedMac}，正在匹配 IP...");

                            for (int i = 0; i < 3; i++)
                            {
                                var ip = await Utils.GetVmIpAddressAsync(SelectedVm.Name, formattedMac);
                                if (!string.IsNullOrEmpty(ip)) return ip;
                                await Task.Delay(2000);
                            }
                        }
                        return string.Empty;
                    });

                    if (!string.IsNullOrEmpty(vmIp))
                    {
                        SshHost = vmIp.Split(',')
                                     .Select(ip => ip.Trim())
                                     .FirstOrDefault(ip => System.Net.IPAddress.TryParse(ip, out var addr)
                                                     && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                     ?? string.Empty;
                        AppendLog($"成功获取 IP 地址: {SshHost}");
                    }
                    else
                    {
                        AppendLog("未能自动获取到 IP，请手动输入。");
                    }

                    ShowSshForm = true;
                    driveTask.Description = "请确认 SSH 凭据以开始部署...";
                }
                catch (Exception ex)
                {
                    ShowSnackbar("环境获取失败", ex.Message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    AppendLog($"[警告] 获取环境异常: {ex.Message}");
                    ShowSshForm = true;
                }
                finally
                {
                    IsLoadingSettings = false;
                }
            }
        }

        // 开始 Linux 部署
        [RelayCommand]
        private async Task StartLinuxDeploy()
        {
            var driveTask = GpuTasks.FirstOrDefault(t => t.Name == "驱动安装");
            if (driveTask == null) return;

            if (string.IsNullOrWhiteSpace(SshHost))
            {
                ShowSnackbar("验证失败", "主机 IP 地址不能为空", ControlAppearance.Danger, SymbolRegular.Warning24);
                return;
            }

            AppendLog($">>> 开始 Linux 部署流程");
            AppendLog($"连接参数 - Host: {SshHost}, Port: {SshPort}, User: {SshUsername}");
            if (!string.IsNullOrEmpty(SshProxyHost)) AppendLog($"使用代理: {SshProxyHost}:{SshProxyPort}");

            ShowPartitionSelector = false;
            ShowSshForm = false;

            driveTask.Status = ExHyperV.Models.TaskStatus.Running;

            var creds = new SshCredentials
            {
                Host = SshHost,
                Port = SshPort,
                Username = SshUsername,
                Password = SshPassword,
                ProxyHost = SshProxyHost,
                ProxyPort = int.TryParse(SshProxyPort, out int pp) ? pp : null,
                InstallGraphics = InstallGraphics
            };

            string result = await _vmGpuService.ProvisionLinuxGpuAsync(
                SelectedVm.Name,
                SelectedHostGpu.InstanceId,
                creds,
                msg => {
                    driveTask.Description = msg;
                    AppendLog(msg);
                },
                CancellationToken.None
            );

            if (result == "OK")
            {
                driveTask.Status = ExHyperV.Models.TaskStatus.Success;
                AppendLog(">>> Linux 部署任务全部完成。");
                await FinishWorkflowAsync();
            }
            else
            {
                driveTask.Status = ExHyperV.Models.TaskStatus.Failed;
                driveTask.Description = result;
                AppendLog($">>> [严重错误] Linux 部署失败: {result}");
            }
        }

        // 返回分区选择列表
        [RelayCommand]
        private void GoBackToPartitionList()
        {
            ShowSshForm = false;
            var driveTask = GpuTasks.FirstOrDefault(t => t.Name == "驱动安装");
            if (driveTask != null)
            {
                driveTask.Description = "请选择目标分区进行适配...";
            }
        }

        // 完成 GPU 部署工作流
        private async Task FinishWorkflowAsync()
        {
            await Task.Delay(1000);
            await RefreshCurrentVmGpuAssignments();
            CurrentViewType = VmDetailViewType.GpuSettings;
            ShowSnackbar("配置成功", $"{SelectedHostGpu.Name} 已准备就绪", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
        }

        // 设备 ID 格式化辅助
        private string NormalizeDeviceId(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return string.Empty;
            var normalizedId = deviceId.ToUpper();
            if (normalizedId.StartsWith(@"\\?\")) normalizedId = normalizedId.Substring(4);
            int suffixIndex = normalizedId.IndexOf("#{");
            if (suffixIndex != -1) normalizedId = normalizedId.Substring(0, suffixIndex);
            return normalizedId.Replace('\\', '#').Replace("#", "");
        }

        // ----------------------------------------------------------------------------------
        // UI 辅助方法
        // ----------------------------------------------------------------------------------

        // 显示 Snackbar 通知
        private void ShowSnackbar(string title, string message, ControlAppearance appearance, SymbolRegular icon)
        {
            Application.Current.Dispatcher.Invoke(() => {
                var presenter = Application.Current.MainWindow?.FindName("SnackbarPresenter") as SnackbarPresenter;
                if (presenter != null)
                {
                    var snack = new Snackbar(presenter) { Title = title, Content = message, Appearance = appearance, Icon = new SymbolIcon(icon), Timeout = TimeSpan.FromSeconds(3) };
                    snack.Show();
                }
            });
        }

        // 获取操作状态的乐观显示文本
        private string GetOptimisticText(string action) => action switch { "Start" => "正在启动", "Restart" => "正在重启", "Stop" => "正在关闭", "TurnOff" => "已关机", "Save" => "正在保存", "Suspend" => "正在暂停", _ => "处理中..." };

        // 追加日志到控制台
        private void AppendLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            Application.Current.Dispatcher.Invoke(() => {
                GpuDeploymentLog += $"[{timestamp}] {message}{Environment.NewLine}";
            });
        }

        // 复制日志
        [RelayCommand]
        private void CopyLog()
        {
            if (!string.IsNullOrEmpty(GpuDeploymentLog))
            {
                Clipboard.SetText(GpuDeploymentLog);
                ShowSnackbar("复制成功", "配置日志已拷贝到剪贴板", ControlAppearance.Success, SymbolRegular.Copy24);
            }
        }

        // 复制文本到剪贴板
        [RelayCommand]
        private void CopyToClipboard(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text == "---" || text == "00-00-00-00-00-00") return;
            Clipboard.SetText(text);
        }
    }
}