using System;
using System.Linq;
using System.Management; // 需要 NuGet: System.Management
using System.Threading.Tasks;

namespace ExHyperV.Services
{
    /// <summary>
    /// 提供 Hyper-V 环境、CPU 虚拟化及 IOMMU 状态的底层检测服务。
    /// 使用原生 WMI 替代 PowerShell 以提高性能。
    /// </summary>
    public static class HyperVEnvironmentService
    {
        /// <summary>
        /// 检测 CPU 虚拟化是否可用（BIOS开启 且 CPU支持）。
        /// 逻辑：如果 Hypervisor 正在运行，则虚拟化必定开启；否则检查 CPU 固件标志。
        /// </summary>
        public static bool IsVirtualizationEnabled()
        {
            try
            {
                // 1. 检查 Hypervisor 是否已呈现 (如果 Hyper-V/WSL2 正在运行，这项为 True)
                if (IsHypervisorPresent()) return true;

                // 2. 如果 Hypervisor 没运行，检查 BIOS 设置
                using var searcher = new ManagementObjectSearcher("SELECT VirtualizationFirmwareEnabled FROM Win32_Processor");
                using var collection = searcher.Get();
                foreach (var item in collection)
                {
                    if (item["VirtualizationFirmwareEnabled"] is bool enabled && enabled)
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 仅检测 Hypervisor (Hyper-V) 是否正在运行。
        /// </summary>
        public static bool IsHypervisorPresent()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT HypervisorPresent FROM Win32_ComputerSystem");
                using var collection = searcher.Get();
                foreach (var item in collection)
                {
                    if (item["HypervisorPresent"] is bool present && present)
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检测 IOMMU (VT-d / AMD-Vi) 状态。
        /// 通过 Win32_DeviceGuard 获取可用安全属性。
        /// </summary>
        public static bool IsIommuEnabled()
        {
            try
            {
                // Win32_DeviceGuard 在 Root\Microsoft\Windows\DeviceGuard 命名空间下
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\DeviceGuard");
                scope.Connect();
                var query = new ObjectQuery("SELECT AvailableSecurityProperties FROM Win32_DeviceGuard");
                using var searcher = new ManagementObjectSearcher(scope, query);
                using var collection = searcher.Get();

                foreach (var item in collection)
                {
                    // AvailableSecurityProperties 是一个 int 数组
                    // 1: Hypervisor Enforced Code Integrity
                    // 2: Local VBS
                    // 3: DMA Protection (IOMMU)
                    if (item["AvailableSecurityProperties"] is int[] props)
                    {
                        return props.Contains(3);
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检测 Hyper-V 虚拟机管理服务 (vmms) 的运行状态。
        /// </summary>
        /// <returns>
        /// 0: 未安装 (Service not found)
        /// 1: 正在运行 (Running)
        /// 2: 已停止 (Stopped / Manual / Disabled)
        /// </returns>
        public static int GetVmmsStatus()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT State FROM Win32_Service WHERE Name = 'vmms'");
                using var collection = searcher.Get();

                if (collection.Count == 0) return 0; // 未找到服务，说明没装 Hyper-V

                foreach (var item in collection)
                {
                    string state = item["State"]?.ToString();
                    if (state != null && state.Equals("Running", StringComparison.OrdinalIgnoreCase))
                        return 1;
                }
                return 2; // 服务存在但没跑
            }
            catch
            {
                return 0;
            }
        }
    }
}