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

        // 1. 确定对齐基数 (2MB 或 1024MB)
        if (memorySettings.BackingPageSize.HasValue && HasProperty(memData, "BackingPageSize"))
        {
            byte pageSize = memorySettings.BackingPageSize.Value;
            if (!isVmRunning)
            {
                memData["BackingPageSize"] = pageSize;
            }

            if (pageSize == 1) alignment = 2;      // 2MB 模式
            else if (pageSize == 2) alignment = 1024; // 1GB 巨页模式
        }

        // 2. 计算对齐后的启动内存
        long originalStartup = memorySettings.Startup;
        long alignedStartup = (originalStartup + alignment - 1) / alignment * alignment;

        // 设置基础内存值
        memData["VirtualQuantity"] = (ulong)alignedStartup;
        memData["Weight"] = (uint)(memorySettings.Priority * 100);

        // 处理加密策略
        if (!isVmRunning && memorySettings.MemoryEncryptionPolicy.HasValue && HasProperty(memData, "MemoryEncryptionPolicy"))
        {
            memData["MemoryEncryptionPolicy"] = memorySettings.MemoryEncryptionPolicy.Value;
        }

        // --- 核心修复部分 ---

        if (!isVmRunning)
        {
            // 如果开启了巨页 (1GB 或 2MB)
            if (memorySettings.BackingPageSize > 0)
            {
                // 巨页模式必须禁用动态内存
                memData["DynamicMemoryEnabled"] = false;
                memData["Reservation"] = (ulong)alignedStartup;
                memData["Limit"] = (ulong)alignedStartup;

                // 【关键修复】：修正 MaxMemoryBlocksPerNumaNode 对齐
                // 很多时候 6962 报错就是因为这个值不是 1024 的倍数
                if (HasProperty(memData, "MaxMemoryBlocksPerNumaNode"))
                {
                    ulong currentMaxNuma = (ulong)memData["MaxMemoryBlocksPerNumaNode"];

                    // 执行向下对齐：(6962 / 1024) * 1024 = 6144
                    ulong correctedMaxNuma = (currentMaxNuma / (ulong)alignment) * (ulong)alignment;

                    // 确保对齐后的值不为 0 (至少应等于对齐基数)
                    if (correctedMaxNuma == 0) correctedMaxNuma = (ulong)alignment;

                    memData["MaxMemoryBlocksPerNumaNode"] = correctedMaxNuma;
                }
            }
            else
            {
                // 非巨页模式：正常动态内存逻辑
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
            // 运行时热调整（热调整通常不涉及 BackingPageSize 的改变）
            if (memorySettings.DynamicMemoryEnabled)
            {
                memData["Reservation"] = (ulong)memorySettings.Minimum;
                memData["Limit"] = (ulong)memorySettings.Maximum;
                if (HasProperty(memData, "TargetMemoryBuffer"))
                    memData["TargetMemoryBuffer"] = (uint)memorySettings.Buffer;
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