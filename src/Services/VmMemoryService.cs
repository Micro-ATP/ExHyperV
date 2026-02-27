using ExHyperV.Models;
using ExHyperV.Tools;
using System.Diagnostics;
using System.Management;

namespace ExHyperV.Services;

public class VmMemoryService
{
    public async Task<VmMemorySettings?> GetVmMemorySettingsAsync(string vmName)
    {
        try
        {
            string vmWql = $"SELECT * FROM Msvm_ComputerSystem WHERE ElementName = '{vmName.Replace("'", "''")}'";
            var vmInstanceId = (await WmiTools.QueryAsync(vmWql, obj => obj["Name"]?.ToString())).FirstOrDefault();

            if (string.IsNullOrEmpty(vmInstanceId)) return null;

            string memWql = $"SELECT * FROM Msvm_MemorySettingData WHERE InstanceID LIKE 'Microsoft:{vmInstanceId}%' AND ResourceType = 4";

            var settingsList = await WmiTools.QueryAsync(memWql, obj => {
                var s = new VmMemorySettings();

                s.Startup = Convert.ToInt64(obj["VirtualQuantity"] ?? 0);
                s.Minimum = Convert.ToInt64(obj["Reservation"] ?? 0);
                s.Maximum = Convert.ToInt64(obj["Limit"] ?? 0);
                s.Priority = obj["Weight"] != null ? Convert.ToInt32(obj["Weight"]) / 100 : 50;
                s.DynamicMemoryEnabled = Convert.ToBoolean(obj["DynamicMemoryEnabled"] ?? false);
                s.Buffer = obj["TargetMemoryBuffer"] != null ? Convert.ToInt32(obj["TargetMemoryBuffer"]) : 20;

                s.BackingPageSize = GetNullableByteProperty(obj, "BackingPageSize");
                s.MemoryEncryptionPolicy = GetNullableByteProperty(obj, "MemoryEncryptionPolicy");

                return s;
            });

            return settingsList.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"读取内存配置时发生严重异常: {ex}");
            return null;
        }
    }

    public async Task<(bool Success, string Message)> SetVmMemorySettingsAsync(string vmName, VmMemorySettings newSettings, bool isVmRunning)
    {
        return await Task.Run(async () =>
        {
            try
            {
                string vmWql = $"SELECT * FROM Msvm_ComputerSystem WHERE ElementName = '{vmName.Replace("'", "''")}'";
                var vmList = await WmiTools.QueryAsync(vmWql, obj => obj["Name"]?.ToString());
                string vmId = vmList.FirstOrDefault();
                if (string.IsNullOrEmpty(vmId)) return (false, Properties.Resources.Error_Memory_VmNotFound);

                string memWql = $"SELECT * FROM Msvm_MemorySettingData WHERE InstanceID LIKE 'Microsoft:{vmId}%' AND ResourceType = 4";

                using var searcher = new ManagementObjectSearcher(WmiTools.HyperVScope, memWql);
                using var collection = searcher.Get();
                using var memObj = collection.Cast<ManagementObject>().FirstOrDefault();

                if (memObj == null) return (false, Properties.Resources.Error_Memory_ObjNotFound);

                ApplyMemorySettingsToWmiObject(memObj, newSettings, isVmRunning);

                string xml = memObj.GetText(TextFormat.CimDtd20);

                string serviceWql = "SELECT * FROM Msvm_VirtualSystemManagementService";
                var parameters = new Dictionary<string, object>
            {
                { "ResourceSettings", new string[] { xml } }
            };

                var result = await WmiTools.ExecuteMethodAsync(serviceWql, "ModifyResourceSettings", parameters);

                if (!result.Success)
                {
                    return (false, string.Format(Properties.Resources.VmMemory_ModFailed, result.Message));
                }

                return (true, Properties.Resources.Msg_Memory_Applied);
            }
            catch (Exception ex)
            {
                return (false, string.Format(Properties.Resources.VmMemory_AdvSetException, ex.Message));
            }
        });
    }
    private void ApplyMemorySettingsToWmiObject(ManagementObject memData, VmMemorySettings memorySettings, bool isVmRunning)
    {
        long alignment = 1;

        // 处理大页内存对齐 (逻辑保留)
        if (memorySettings.BackingPageSize.HasValue && HasProperty(memData, "BackingPageSize"))
        {
            byte pageSize = memorySettings.BackingPageSize.Value;
            if (!isVmRunning) // 只有关机才能改大页
            {
                memData["BackingPageSize"] = pageSize;
            }
            if (pageSize == 1) alignment = 2; // 2MB
            else if (pageSize == 2) alignment = 1024; // 1GB
        }

        // 计算对齐后的启动内存
        long originalStartup = memorySettings.Startup;
        long alignedStartup = (originalStartup + alignment - 1) / alignment * alignment;

        // 无论何时，修改 VirtualQuantity 都是安全的
        memData["VirtualQuantity"] = (ulong)alignedStartup;
        memData["Weight"] = (uint)(memorySettings.Priority * 100);

        // 处理加密策略 (仅关机)
        if (!isVmRunning && memorySettings.MemoryEncryptionPolicy.HasValue && HasProperty(memData, "MemoryEncryptionPolicy"))
        {
            memData["MemoryEncryptionPolicy"] = memorySettings.MemoryEncryptionPolicy.Value;
        }

        // --- 重点：动态内存属性的条件修改 ---

        // 如果是关机状态，可以随便改
        if (!isVmRunning)
        {
            if (memorySettings.BackingPageSize > 0)
            {
                memData["DynamicMemoryEnabled"] = false;
                memData["Reservation"] = (ulong)alignedStartup;
                memData["Limit"] = (ulong)alignedStartup;
            }
            else
            {
                memData["DynamicMemoryEnabled"] = memorySettings.DynamicMemoryEnabled;
                if (memorySettings.DynamicMemoryEnabled)
                {
                    memData["Reservation"] = (ulong)memorySettings.Minimum;
                    memData["Limit"] = (ulong)memorySettings.Maximum;
                    if (HasProperty(memData, "TargetMemoryBuffer"))
                        memData["TargetMemoryBuffer"] = (uint)memorySettings.Buffer;
                }
                else
                {
                    memData["Reservation"] = (ulong)alignedStartup;
                    memData["Limit"] = (ulong)alignedStartup;
                }
            }
        }
        else
        {
            // --- 运行时热调整逻辑 ---
            if (memorySettings.DynamicMemoryEnabled)
            {
                // 运行时开启了动态内存：允许调整 Min/Max
                memData["Reservation"] = (ulong)memorySettings.Minimum;
                memData["Limit"] = (ulong)memorySettings.Maximum;
                if (HasProperty(memData, "TargetMemoryBuffer"))
                    memData["TargetMemoryBuffer"] = (uint)memorySettings.Buffer;
            }
            else
            {
            }
        }
    }
    private static bool HasProperty(ManagementObject obj, string propName) =>
        obj.Properties.Cast<PropertyData>().Any(p => p.Name.Equals(propName, StringComparison.OrdinalIgnoreCase));

    private static byte? GetNullableByteProperty(ManagementObject obj, string propName)
    {
        if (!HasProperty(obj, propName)) return null;
        var val = obj[propName];
        return val == null ? null : Convert.ToByte(val);
    }
}