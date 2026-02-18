using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExHyperV.Services;
using ExHyperV.Tools;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media; // 引用画笔颜色
using Wpf.Ui.Controls;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace ExHyperV.ViewModels
{
    // 用于 ComboBox 显示的辅助类
    public record SchedulerMode(string Name, HyperVSchedulerType Type);

    public partial class HostPageViewModel : ObservableObject
    {
        private bool _isInitialized = false;

        public CheckStatusViewModel SystemStatus { get; }
        public CheckStatusViewModel CpuStatus { get; }
        public CheckStatusViewModel HyperVStatus { get; }
        public CheckStatusViewModel AdminStatus { get; }
        public CheckStatusViewModel VersionStatus { get; }
        public CheckStatusViewModel IommuStatus { get; }

        [ObservableProperty] private bool _isGpuStrategyEnabled;
        [ObservableProperty] private bool _isGpuStrategyToggleEnabled = false;
        [ObservableProperty] private bool _isServerSystem;
        [ObservableProperty] private bool _isSystemSwitchEnabled = false;
        [ObservableProperty] private string _systemVersionDesc;

        // --- 核心调度器与NUMA相关属性 ---
        [ObservableProperty] private bool _isNumaSpanningEnabled;
        [ObservableProperty] private HyperVSchedulerType _currentSchedulerType;

        // 调度器选项列表
        public ObservableCollection<SchedulerMode> SchedulerModes { get; } = new()
        {
            new SchedulerMode("经典 (Classic)", HyperVSchedulerType.Classic),
            new SchedulerMode("核心 (Core)", HyperVSchedulerType.Core),
            new SchedulerMode("根 (Root)", HyperVSchedulerType.Root)
        };
        // ------------------------------------

        public HostPageViewModel()
        {
            SystemStatus = new CheckStatusViewModel(Properties.Resources.checksys);
            CpuStatus = new CheckStatusViewModel(Properties.Resources.checkcpuct);
            HyperVStatus = new CheckStatusViewModel(Properties.Resources.checkhyperv);
            AdminStatus = new CheckStatusViewModel(Properties.Resources.checkadmin);
            VersionStatus = new CheckStatusViewModel(Properties.Resources.checkversion);
            IommuStatus = new CheckStatusViewModel(Properties.Resources.Status_CheckingBiosIommu);
            _ = LoadInitialStatusAsync();
        }

        private async Task LoadInitialStatusAsync()
        {
            await Task.WhenAll(CheckSystemInfoAsync(), CheckCpuInfoAsync(), CheckHyperVInfoAsync(), CheckServerInfoAsync(), CheckIommuAsync());

            // 加载管理员权限及后续的高级配置
            await CheckAdminInfoAsync();
        }

        // --- 加载高级配置 ---
        private async Task LoadAdvancedConfigAsync()
        {
            try
            {
                // 1. 加载 NUMA 状态
                bool numaEnabled = await HyperVNUMAService.GetNumaSpanningEnabledAsync();

                // 2. 加载 调度器类型 (如果在 Admin 下)
                HyperVSchedulerType schedType = await Task.Run(() => HyperVSchedulerService.GetSchedulerType());
                if (schedType == HyperVSchedulerType.Unknown) schedType = HyperVSchedulerType.Classic; // 默认回退

                // 更新属性（此时 _isInitialized 尚未设为 true，不会触发保存逻辑）
                _isNumaSpanningEnabled = numaEnabled;
                _currentSchedulerType = schedType;

                // 通知前端更新 UI
                OnPropertyChanged(nameof(IsNumaSpanningEnabled));
                OnPropertyChanged(nameof(CurrentSchedulerType));
            }
            catch { /* 忽略加载错误 */ }
        }

        private async Task CheckSystemInfoAsync()
        {
            await Task.Run(() =>
            {
                // 获取 Build 版本号 (例如 22631)
                int build = Environment.OSVersion.Version.Build;

                VersionStatus.IsSuccess = true;
                VersionStatus.StatusText = $"Build {build}";
                VersionStatus.IsChecking = false;
            });
        }
        private async Task CheckCpuInfoAsync()
        {
            await Task.Run(() =>
            {
                var cpuvt1 = Utils.Run("(Get-CimInstance -Class Win32_Processor).VirtualizationFirmwareEnabled");
                var cpuvt2 = Utils.Run("(Get-CimInstance -Class Win32_ComputerSystem).HypervisorPresent");

                bool success = cpuvt1.Count > 0 && cpuvt2.Count > 0 && (cpuvt1[0].ToString() == "True" || cpuvt2[0].ToString() == "True");

                CpuStatus.IsSuccess = success;
                CpuStatus.StatusText = "";

                CpuStatus.IsChecking = false;
            });
        }

        private async Task CheckHyperVInfoAsync()
        {
            await Task.Run(() =>
            {
                // 检测 Hypervisor 是否实际正在运行
                var result = Utils.Run("(Get-CimInstance -Class Win32_ComputerSystem).HypervisorPresent");
                bool isRunning = result.Count > 0 && result[0].ToString() == "True";

                HyperVStatus.IsSuccess = isRunning;
                HyperVStatus.StatusText = isRunning ? "Running" : "Stopped";
                HyperVStatus.IsChecking = false;

                // 核心修复：设置 IsInstalled 属性，控制前端按钮显示
                HyperVStatus.IsInstalled = isRunning;
            });
        }

        private async Task CheckAdminInfoAsync()
        {
            // 这里修改逻辑：先检查权限，如果通过，再加载高级设置，最后设置 _isInitialized
            bool isAdmin = false;
            await Task.Run(() =>
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                AdminStatus.IsSuccess = isAdmin;
                AdminStatus.StatusText = isAdmin ? Properties.Resources.Admin1 : Properties.Resources.Admin2;
                if (isAdmin)
                {
                    IsGpuStrategyToggleEnabled = true;
                    CheckGpuStrategyReg();
                    IsSystemSwitchEnabled = true;
                    InitializeProductType();
                }
                AdminStatus.IsChecking = false;
            });

            if (isAdmin)
            {
                // 只有管理员才加载这些高级设置
                await LoadAdvancedConfigAsync();
            }

            _isInitialized = true; // 初始化完成，后续属性变更将触发保存
        }

        private async Task CheckServerInfoAsync()
        {
            await Task.Run(() =>
            {
                bool isServer = false;
                try
                {
                    // 优化点：直接读取注册表，无需启动 PowerShell
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions"))
                    {
                        if (key != null)
                        {
                            var type = key.GetValue("ProductType")?.ToString();
                            // "WinNT" = Workstation, 其他视为 Server 环境
                            isServer = type != null && !type.Equals("WinNT", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
                catch { }

                SystemStatus.IsSuccess = isServer;
                // 非 Server 不显示文字，保持界面整洁；Server 显示 Active
                SystemStatus.StatusText = isServer ? "Active" : "";
                SystemStatus.IsChecking = false;
            });
        }
        private async Task CheckIommuAsync()
        {
            await Task.Run(() =>
            {
                var io = Utils.Run("(Get-CimInstance -Namespace \"Root\\Microsoft\\Windows\\DeviceGuard\" -ClassName \"Win32_DeviceGuard\").AvailableSecurityProperties -contains 3");
                bool success = io.Count > 0 && io[0].ToString() == "True";
                IommuStatus.IsSuccess = success;
                IommuStatus.StatusText = success ? ExHyperV.Properties.Resources.Info_BiosIommuEnabled : ExHyperV.Properties.Resources.Error_BiosIommuDisabled;
                IommuStatus.IsChecking = false;
            });
        }

        // =========================================================
        // 新增：启用 Hyper-V 逻辑
        // =========================================================

        [RelayCommand]
        private async Task EnableHyperVAsync()
        {
            // 修复 CS0266：bool? 必须显式比较，不能直接用 !Status.IsSuccess
            if (AdminStatus.IsSuccess != true)
            {
                ShowSnackbar(Translate("Status_Title_Error"), "需要管理员权限才能执行此操作。", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                return;
            }

            // 提示用户正在处理
            ShowSnackbar(Translate("Status_Title_Info"), "正在启用 Hyper-V 服务，请稍候...", ControlAppearance.Info, SymbolRegular.Settings24);

            bool success = await Task.Run(() =>
            {
                try
                {
                    string script = "Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -All -NoRestart";
                    var result = Utils.Run(script);
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                    return false;
                }
            });

            if (success)
            {
                ShowRestartPrompt("Hyper-V 服务已启用，重启系统后生效。");
                HyperVStatus.IsChecking = true; // 临时更新状态
            }
            else
            {
                ShowSnackbar(Translate("Status_Title_Error"), "启用 Hyper-V 服务失败，请尝试在“启用或关闭 Windows 功能”中手动开启。", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
            }
        }

        private void ShowRestartPrompt(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var presenter = Application.Current.MainWindow?.FindName("SnackbarPresenter") as SnackbarPresenter;
                if (presenter == null) return;

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var txt = new TextBlock
                {
                    Text = message,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 15, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(txt, 0);

                var btn = new Wpf.Ui.Controls.Button
                {
                    Content = Translate("Global_Restart"),
                    Appearance = ControlAppearance.Primary
                };
                btn.Click += (s, e) => System.Diagnostics.Process.Start("shutdown", "-r -t 0");
                Grid.SetColumn(btn, 1);

                grid.Children.Add(txt);
                grid.Children.Add(btn);

                var snack = new Snackbar(presenter)
                {
                    Title = Translate("Status_Title_Success"),
                    Content = grid,
                    Appearance = ControlAppearance.Success,
                    Icon = new SymbolIcon(SymbolRegular.CheckmarkCircle24),
                    Timeout = TimeSpan.FromSeconds(15)
                };
                snack.Show();
            });
        }

        // --- 属性变更处理逻辑 ---

        partial void OnIsGpuStrategyEnabledChanged(bool value)
        {
            if (!_isInitialized) return;
            if (value) Utils.AddGpuAssignmentStrategyReg(); else Utils.RemoveGpuAssignmentStrategyReg();
        }

        partial void OnIsNumaSpanningEnabledChanged(bool value)
        {
            if (!_isInitialized) return;
            _ = Task.Run(async () =>
            {
                var (success, msg) = await HyperVNUMAService.SetNumaSpanningEnabledAsync(value);
                if (!success)
                {
                    ShowSnackbar(Translate("Status_Title_Error"), msg, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                }
            });
        }

        partial void OnCurrentSchedulerTypeChanged(HyperVSchedulerType value)
        {
            if (!_isInitialized) return;
            _ = Task.Run(async () =>
            {
                bool result = await HyperVSchedulerService.SetSchedulerTypeAsync(value);
                if (result)
                {
                    ShowSnackbar(Translate("Status_Title_Info"), "调度器类型已更改，请重启系统生效。", ControlAppearance.Info, SymbolRegular.Info24);
                }
                else
                {
                    ShowSnackbar(Translate("Status_Title_Error"), "设置调度器类型失败。", ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                }
            });
        }

        private void CheckGpuStrategyReg()
        {
            string script = @"[bool]((Test-Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\HyperV') -and ($k = Get-Item 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\HyperV' -EA 0) -and ('RequireSecureDeviceAssignment', 'RequireSupportedDeviceAssignment' | ForEach-Object { ($k.GetValue($_, $null) -ne $null) }) -notcontains $false)";
            var result = Utils.Run(script);
            bool oldInit = _isInitialized;
            _isInitialized = false;
            SetProperty(ref _isGpuStrategyEnabled, result.Count > 0 && result[0].ToString().ToLower() == "true", nameof(IsGpuStrategyEnabled));
            _isInitialized = oldInit;
        }

        private void InitializeProductType()
        {
            bool isServer = false;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions"))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("ProductType")?.ToString();
                        isServer = val != null && val.Contains("Server");
                    }
                }
            }
            catch { }
            _isServerSystem = isServer;
            UpdateSystemDesc(isServer);
            OnPropertyChanged(nameof(IsServerSystem));
        }

        private void UpdateSystemDesc(bool isServer)
        {
            string current = isServer ? Translate("Status_Edition_Server") : Translate("Status_Edition_Workstation");
            SystemVersionDesc = $"{Translate("Status_Msg_CurrentVer")}: {current}";
        }

        partial void OnIsServerSystemChanged(bool value)
        {
            if (!_isInitialized) return;
            SwitchSystemVersion(value);
        }

        private async void SwitchSystemVersion(bool toServer)
        {
            try
            {
                IsSystemSwitchEnabled = false;
                string result = await Task.Run(() => SystemSwitcher.ExecutePatch(toServer ? 1 : 2));

                if (result == "SUCCESS")
                {
                    SystemVersionDesc = Translate("Status_Msg_OperationPending");
                    ShowRestartPrompt(Translate("Status_Msg_RestartNow"));
                }
                else if (result == "PENDING")
                {
                    ShowSnackbar(Translate("Status_Title_Info"), Translate("Status_Msg_OperationPending"), ControlAppearance.Info, SymbolRegular.Info24);
                }
                else
                {
                    ShowSnackbar(Translate("Status_Title_Error"), result, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    _isInitialized = false;
                    IsServerSystem = !toServer;
                    _isInitialized = true;
                    IsSystemSwitchEnabled = true;
                }
            }
            catch { IsSystemSwitchEnabled = true; }
        }

        private string Translate(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            try { return ExHyperV.Properties.Resources.ResourceManager.GetString(key) ?? key; } catch { return key; }
        }

        public void ShowSnackbar(string title, string message, ControlAppearance appearance, SymbolRegular icon)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var presenter = Application.Current.MainWindow?.FindName("SnackbarPresenter") as SnackbarPresenter;
                if (presenter != null)
                {
                    var snack = new Snackbar(presenter) { Title = title, Content = message, Appearance = appearance, Icon = new SymbolIcon(icon) { FontSize = 20 }, Timeout = TimeSpan.FromSeconds(4) };
                    snack.Show();
                }
            });
        }
    }

    /// <summary>
    /// 表示单个环境检查项的ViewModel。
    /// 已移动至 HostPageViewModel.cs 内部，并修复了属性缺失问题。
    /// </summary>
    public partial class CheckStatusViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isChecking = true;

        [ObservableProperty]
        private string _statusText;

        [ObservableProperty]
        private bool? _isSuccess; // 注意：这是可空的

        // 新增：修复 CS1061 错误，用于绑定 Button 的 Visibility
        [ObservableProperty]
        private bool _isInstalled;

        public string IconGlyph => IsSuccess switch
        {
            true => "\uEC61",
            false => "\uEB90",
            _ => ""
        };

        public Brush IconColor => IsSuccess switch
        {
            true => new SolidColorBrush(Color.FromArgb(255, 0, 138, 23)),
            false => new SolidColorBrush(Colors.Red),
            _ => Brushes.Transparent
        };

        public CheckStatusViewModel(string initialText)
        {
            _statusText = initialText;
        }

        partial void OnIsSuccessChanged(bool? value)
        {
            OnPropertyChanged(nameof(IconGlyph));
            OnPropertyChanged(nameof(IconColor));
        }
    }
}