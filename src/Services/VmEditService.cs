using System;
using System.Management;
using System.Threading.Tasks;
using System.Linq;

namespace ExHyperV.Services
{
    public class VmEditService
    {
        private const string Namespace = @"root\virtualization\v2";

        /// <summary>
        /// 使用 WMI 修改虚拟机名称（ElementName）
        /// </summary>
        /// <param name="oldName">当前的虚拟机显示名称</param>
        /// <param name="newName">目标显示名称</param>
        /// <returns>操作是否成功以及错误信息</returns>
        /// <summary>
        /// 使用 WMI 精确修改虚拟机名称
        /// </summary>
        /// <param name="vmGuid">虚拟机的唯一 ID (Guid)</param>
        /// <param name="newName">新的显示名称</param>
        public async Task<(bool Success, string Message)> RenameVmAsync(Guid vmGuid, string newName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var managementService = GetVirtualSystemManagementService();

                    // --- 核心修复：使用 Name (即 GUID) 而不是 ElementName 查询 ---
                    using var vm = GetComputerSystemByGuid(vmGuid);
                    if (vm == null) return (false, "找不到指定的虚拟机实例。");

                    using var settings = GetVmSettings(vm);
                    if (settings == null) return (false, "无法获取配置数据。");

                    settings["ElementName"] = newName;

                    using var inParams = managementService.GetMethodParameters("ModifySystemSettings");
                    inParams["SystemSettings"] = settings.GetText(TextFormat.CimDtd20);

                    using var outParams = managementService.InvokeMethod("ModifySystemSettings", inParams, null);

                    uint errorCode = (uint)outParams["ReturnValue"];
                    return errorCode == 0 || errorCode == 4096
                        ? (true, "成功")
                        : (false, $"WMI 错误代码: {errorCode}");
                }
                catch (Exception ex) { return (false, ex.Message); }
            });
        }
        private ManagementObject GetComputerSystemByGuid(Guid guid)
        {
            var scope = new ManagementScope(Namespace);
            // 在 Msvm_ComputerSystem 类中，"Name" 属性存放的就是虚拟机的 GUID
            var query = new ObjectQuery($"SELECT * FROM Msvm_ComputerSystem WHERE Name = '{guid}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            return searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        }

        private ManagementObject GetVirtualSystemManagementService()
        {
            var scope = new ManagementScope(Namespace);
            var query = new ObjectQuery("SELECT * FROM Msvm_VirtualSystemManagementService");
            using var searcher = new ManagementObjectSearcher(scope, query);
            return searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        }

        private ManagementObject GetComputerSystemByName(string name)
        {
            var scope = new ManagementScope(Namespace);
            // 注意：ElementName 对应 Hyper-V 管理器里看到的名字
            var query = new ObjectQuery($"SELECT * FROM Msvm_ComputerSystem WHERE ElementName = '{name.Replace("'", "''")}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            return searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        }

        private ManagementObject GetVmSettings(ManagementObject vm)
        {
            // 通过关联获取设置数据，通常 VirtualSystemType 为 Microsoft:Hyper-V:System:Realized
            using var settingsCollection = vm.GetRelated("Msvm_VirtualSystemSettingData", "Msvm_SettingsDefineState", null, null, null, null, false, null);
            return settingsCollection.Cast<ManagementObject>().FirstOrDefault();
        }
    }
}