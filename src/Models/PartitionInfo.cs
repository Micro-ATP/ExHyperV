using System;

namespace ExHyperV.Models 
{
    public enum OperatingSystemType
    {
        Unknown,
        Windows,
        Linux,
        EFI,
        Other
    }

    public class PartitionInfo
    {
        public int PartitionNumber { get; }
        public ulong StartOffset { get; }
        public ulong SizeInBytes { get; }
        public OperatingSystemType OsType { get; }
        public string TypeDescription { get; }

        public string DiskPath { get; set; }        // 所属 VHDX 路径或物理磁盘编号
        public string DiskDisplayName { get; set; } // 友好显示：如 "Disk 0 (System.vhdx)"
        public bool IsPhysicalDisk { get; set; }    // 标记是否为物理直通盘


        public PartitionInfo(int number, ulong startOffset, ulong size, OperatingSystemType osType, string typeDescription)
        {
            PartitionNumber = number;
            StartOffset = startOffset;
            SizeInBytes = size;
            OsType = osType;
            TypeDescription = typeDescription;
        }

        public double SizeInGb => SizeInBytes / (1024.0 * 1024.0 * 1024.0);

        public string DisplayName => $"[{DiskDisplayName}] Partition {PartitionNumber} （{SizeInGb:F2} GB) - {TypeDescription}";
        public string IconPath
        {
            get
            {
                switch (OsType)
                {
                    case OperatingSystemType.Windows:
                        return "pack://application:,,,/Assets/Microsoft.png";
                    case OperatingSystemType.Linux:
                        return "pack://application:,,,/Assets/Linux.png";
                    default:
                        return null;
                }
            }
        }
    }
}