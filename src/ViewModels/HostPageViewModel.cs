using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExHyperV.Services;
using ExHyperV.Tools;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;

namespace ExHyperV.ViewModels
{
    public record SchedulerMode(string Name, HyperVSchedulerType Type);

    public partial class HostPageViewModel : ObservableObject
    {
        private bool _isInitialized = false;

        public CheckStatusViewModel SystemStatus { get; } = new("");
        public CheckStatusViewModel CpuStatus { get; } = new("");
        public CheckStatusViewModel HyperVStatus { get; } = new("");
        public CheckStatusViewModel AdminStatus { get; } = new("");
        public CheckStatusViewModel VersionStatus { get; } = new("");
        public CheckStatusViewModel IommuStatus { get; } = new("");

        [ObservableProperty] private bool _isGpuStrategyEnabled;
        [ObservableProperty] private bool _isGpuStrategyToggleEnabled = false;
        [ObservableProperty] private bool _isServerSystem;
        [ObservableProperty] private bool _isSystemSwitchEnabled = false;
        [ObservableProperty] private string _systemVersionDesc;
        [ObservableProperty] private bool _isNumaSpanningEnabled;
        [ObservableProperty] private HyperVSchedulerType _currentSchedulerType;

        public ObservableCollection<SchedulerMode> SchedulerModes { get; } = new()
        {
            new SchedulerMode(ExHyperV.Properties.Resources.Scheduler_Classic, HyperVSchedulerType.Classic),
            new SchedulerMode(ExHyperV.Properties.Resources.Scheduler_Core, HyperVSchedulerType.Core),
            new SchedulerMode(ExHyperV.Properties.Resources.Scheduler_Root, HyperVSchedulerType.Root)
        };

        public HostPageViewModel() => _ = LoadInitialStatusAsync();

        private async Task LoadInitialStatusAsync()
        {
            await Task.WhenAll(CheckSystemInfoAsync(), CheckCpuInfoAsync(), CheckHyperVInfoAsync(), CheckServerInfoAsync(), CheckIommuAsync());
            await CheckAdminInfoAsync();
            _isInitialized = true;
        }

        private async Task CheckSystemInfoAsync() => await Task.Run(() => {
            VersionStatus.StatusText = Environment.OSVersion.Version.Build.ToString();
            VersionStatus.IsSuccess = true;
            VersionStatus.IsChecking = false;
        });

        private async Task CheckCpuInfoAsync()
        {
            CpuStatus.IsSuccess = await Task.Run(() => HyperVEnvironmentService.IsVirtualizationEnabled());
            CpuStatus.IsChecking = false;
        }

        private async Task CheckHyperVInfoAsync()
        {
            var hTask = Task.Run(() => HyperVEnvironmentService.IsHypervisorPresent());
            var vTask = Task.Run(() => HyperVEnvironmentService.GetVmmsStatus());
            await Task.WhenAll(hTask, vTask);
            HyperVStatus.IsInstalled = (vTask.Result != 0);
            HyperVStatus.IsSuccess = hTask.Result && (vTask.Result == 1);
            HyperVStatus.IsChecking = false;
        }

        private async Task CheckIommuAsync()
        {
            IommuStatus.IsSuccess = await Task.Run(() => HyperVEnvironmentService.IsIommuEnabled());
            IommuStatus.IsChecking = false;
        }

        private async Task CheckAdminInfoAsync()
        {
            bool isAdmin = await Task.Run(() => {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            });
            AdminStatus.IsSuccess = isAdmin;
            AdminStatus.IsChecking = false;
            if (isAdmin)
            {
                CheckGpuStrategyReg();
                InitializeProductType();
                await LoadAdvancedConfigAsync();
                IsGpuStrategyToggleEnabled = true;
                IsSystemSwitchEnabled = true;
            }
        }

        private async Task CheckServerInfoAsync()
        {
            SystemStatus.IsSuccess = await Task.Run(() => {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions");
                    var type = key?.GetValue("ProductType")?.ToString();
                    return type != null && !type.Equals("WinNT", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            });
            SystemStatus.IsChecking = false;
        }

        private async Task LoadAdvancedConfigAsync()
        {
            try
            {
                bool numa = await HyperVNUMAService.GetNumaSpanningEnabledAsync();
                var sched = await Task.Run(() => HyperVSchedulerService.GetSchedulerType());
                IsNumaSpanningEnabled = numa;
                CurrentSchedulerType = (sched == HyperVSchedulerType.Unknown) ? HyperVSchedulerType.Classic : sched;
            }
            catch { }
        }

        partial void OnIsGpuStrategyEnabledChanged(bool value)
        {
            if (!_isInitialized) return;
            if (value) Utils.AddGpuAssignmentStrategyReg(); else Utils.RemoveGpuAssignmentStrategyReg();
        }

        partial void OnIsNumaSpanningEnabledChanged(bool value)
        {
            if (!_isInitialized) return;
            _ = Task.Run(async () => {
                var (ok, msg) = await HyperVNUMAService.SetNumaSpanningEnabledAsync(value);
                if (!ok)
                {
                    ShowSnackbar(Translate("Status_Title_Error"), msg, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    Application.Current.Dispatcher.Invoke(() => {
                        _isInitialized = false;
                        IsNumaSpanningEnabled = !value; // 遭遇错误回滚按钮
                        _isInitialized = true;
                    });
                }
            });
        }

        partial void OnCurrentSchedulerTypeChanged(HyperVSchedulerType value)
        {
            if (!_isInitialized) return;
            _ = Task.Run(async () => {
                if (await HyperVSchedulerService.SetSchedulerTypeAsync(value))
                    ShowSnackbar(Translate("Status_Title_Info"), ExHyperV.Properties.Resources.Msg_Host_SchedulerChanged, ControlAppearance.Info, SymbolRegular.Info24);
                else
                {
                    ShowSnackbar(Translate("Status_Title_Error"), ExHyperV.Properties.Resources.Error_Host_SchedulerFail, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    var actual = HyperVSchedulerService.GetSchedulerType();
                    Application.Current.Dispatcher.Invoke(() => {
                        _isInitialized = false;
                        CurrentSchedulerType = actual; // 遭遇错误回滚选项
                        _isInitialized = true;
                    });
                }
            });
        }

        partial void OnIsServerSystemChanged(bool value)
        {
            if (!_isInitialized) return;
            SwitchSystemVersion(value);
        }

        [RelayCommand]
        private async Task EnableHyperVAsync()
        {
            if (AdminStatus.IsSuccess != true) return;
            ShowSnackbar(Translate("Status_Title_Info"), ExHyperV.Properties.Resources.Msg_Host_EnableHyperV, ControlAppearance.Info, SymbolRegular.Settings24);
            bool ok = await Task.Run(() => {
                try { Utils.Run("Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All -NoRestart"); return true; } catch { return false; }
            });
            if (ok) ShowRestartPrompt(ExHyperV.Properties.Resources.Msg_Host_EnableSuccess);
        }

        private void CheckGpuStrategyReg()
        {
            var result = Utils.Run(@"[bool]((Test-Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\HyperV') -and ($k = Get-Item 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\HyperV' -EA 0) -and ('RequireSecureDeviceAssignment', 'RequireSupportedDeviceAssignment' | ForEach-Object { ($k.GetValue($_, $null) -ne $null) }) -notcontains $false)");
            IsGpuStrategyEnabled = result.Count > 0 && result[0].ToString().ToLower() == "true";
        }

        private void InitializeProductType()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions");
                var val = key?.GetValue("ProductType")?.ToString();
                IsServerSystem = val != null && val.Contains("Server");
            }
            catch { }
            UpdateSystemDesc(IsServerSystem);
        }

        private void UpdateSystemDesc(bool isServer) =>
            SystemVersionDesc = $"{Translate("Status_Msg_CurrentVer")}: {(isServer ? Translate("Status_Edition_Server") : Translate("Status_Edition_Workstation"))}";

        private async void SwitchSystemVersion(bool toServer)
        {
            try
            {
                IsSystemSwitchEnabled = false;
                string result = await Task.Run(() => SystemSwitcher.ExecutePatch(toServer ? 1 : 2));
                if (result == "SUCCESS") ShowRestartPrompt(Translate("Status_Msg_RestartNow"));
                else
                {
                    ShowSnackbar(Translate("Status_Title_Error"), result, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
                    _isInitialized = false; IsServerSystem = !toServer; _isInitialized = true;
                }
            }
            finally { IsSystemSwitchEnabled = true; }
        }

        private string Translate(string key) => ExHyperV.Properties.Resources.ResourceManager.GetString(key) ?? key;

        public void ShowSnackbar(string title, string msg, ControlAppearance app, SymbolRegular icon)
        {
            Application.Current.Dispatcher.Invoke(() => {
                if (Application.Current.MainWindow?.FindName("SnackbarPresenter") is SnackbarPresenter p)
                    new Snackbar(p) { Title = title, Content = msg, Appearance = app, Icon = new SymbolIcon(icon) { FontSize = 20 }, Timeout = TimeSpan.FromSeconds(4) }.Show();
            });
        }

        private void ShowRestartPrompt(string message)
        {
            Application.Current.Dispatcher.Invoke(() => {
                if (Application.Current.MainWindow?.FindName("SnackbarPresenter") is not SnackbarPresenter p) return;
                var grid = new System.Windows.Controls.Grid();
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
                var txt = new Wpf.Ui.Controls.TextBlock { Text = message, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), TextWrapping = TextWrapping.Wrap };
                var btn = new Wpf.Ui.Controls.Button { Content = Translate("Global_Restart"), Appearance = ControlAppearance.Primary };
                btn.Click += (s, e) => System.Diagnostics.Process.Start("shutdown", "-r -t 0");
                System.Windows.Controls.Grid.SetColumn(btn, 1); grid.Children.Add(txt); grid.Children.Add(btn);
                new Snackbar(p) { Title = Translate("Status_Title_Success"), Content = grid, Appearance = ControlAppearance.Success, Icon = new SymbolIcon(SymbolRegular.CheckmarkCircle24), Timeout = TimeSpan.FromSeconds(15) }.Show();
            });
        }
    }

    public partial class CheckStatusViewModel : ObservableObject
    {
        [ObservableProperty] private bool _isChecking = true;
        [ObservableProperty] private string _statusText;
        [ObservableProperty] private bool? _isSuccess;
        [ObservableProperty] private bool _isInstalled;
        public string IconGlyph => IsSuccess switch { true => "\uEC61", false => "\uEB90", _ => "\uE946" };
        public System.Windows.Media.Brush IconColor => IsSuccess switch
        {
            true => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0, 138, 23)),
            false => System.Windows.Media.Brushes.Red,
            _ => System.Windows.Media.Brushes.Gray
        };
        public CheckStatusViewModel(string initialText) => _statusText = initialText;
        partial void OnIsSuccessChanged(bool? value) { OnPropertyChanged(nameof(IconGlyph)); OnPropertyChanged(nameof(IconColor)); }
    }
}