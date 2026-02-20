using System;
using System.Text.RegularExpressions;

//映射工具

namespace ExHyperV.Services
{
    /// <summary>
    /// 内部映射器：负责将 WMI/PS 的原始数据转换为 UI 易读数据
    /// </summary>
    internal static class VmMapper
    {
        public static string ParseOsTypeFromNotes(string notes)
        {
            if (string.IsNullOrEmpty(notes)) return "windows";
            var match = Regex.Match(notes, @"\[OSType:([^\]]+)\]", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim().ToLower();
            if (notes.Contains("linux", StringComparison.OrdinalIgnoreCase)) return "linux";
            return "windows";
        }

        public static string ParseNotes(object notesObj)
        {
            if (notesObj is string[] arr) return string.Join("\n", arr);
            return notesObj?.ToString() ?? "";
        }

        public static bool IsRunning(ushort code) => code == 2;

        public static string MapStateCodeToText(ushort code)
        {
            return code switch
            {
                2 => Properties.Resources.Status_Running,       // Enabled
                3 => Properties.Resources.Status_Off,       // Disabled
                6 => Properties.Resources.Status_Saved,       // Enabled but Offline
                9 => Properties.Resources.Status_Suspended,       // Quiesce
                32768 => Properties.Resources.Status_Suspended,   // Paused
                32769 => Properties.Resources.Status_Saved,   // Saved
                32770 => Properties.Resources.Status_Starting,
                32771 => Properties.Resources.Status_Snapshotting,
                32773 => Properties.Resources.Status_Saving,
                32774 => Properties.Resources.Status_Stopping,
                32776 => Properties.Resources.Status_Suspending,
                32777 => Properties.Resources.Status_Resuming,
                _ => $"未知状态({code})"
            };
        }
    }
}