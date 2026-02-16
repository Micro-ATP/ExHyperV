using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ExHyperV.Tools
{
    public static class SystemSwitcher
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetCurrentProcess();
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, uint ulOptions, int samDesired, out IntPtr phkResult);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        static extern int RegSaveKey(IntPtr hKey, string lpFile, IntPtr lpSecurityAttributes);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        static extern int RegReplaceKey(IntPtr hKey, string lpSubKey, string lpNewFile, string lpOldFile);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern int RegCloseKey(IntPtr hKey);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        static extern int RegLoadKey(IntPtr hKey, string lpSubKey, string lpFile);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        static extern int RegUnLoadKey(IntPtr hKey, string lpSubKey);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern int RegFlushKey(IntPtr hKey);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        static extern int RegSetValueEx(IntPtr hKey, string lpValueName, int Reserved, int dwType, byte[] lpData, int cbData);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        static extern int RegQueryValueEx(IntPtr hKey, string lpValueName, IntPtr lpReserved, ref int lpType, ref int lpData, ref int lpcbData);

        [StructLayout(LayoutKind.Sequential)]
        struct LUID { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_PRIVILEGES { public uint PrivilegeCount; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)] public LUID_AND_ATTRIBUTES[] Privileges; }
        [StructLayout(LayoutKind.Sequential)]
        struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

        const uint KEY_READ = 0x20019;
        const uint KEY_SET_VALUE = 0x0002;
        const int REG_SZ = 1;
        static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));

        public static bool EnablePrivilege(string privilegeName)
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out IntPtr hToken)) return false;
            try
            {
                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid)) return false;
                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Privileges = new LUID_AND_ATTRIBUTES[1] };
                tp.Privileges[0].Luid = luid;
                tp.Privileges[0].Attributes = 0x00000002;
                return AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { CloseHandle(hToken); }
        }

        public static string ExecutePatch(int mode)
        {
            string tempDir = @"C:\temp";
            string hiveFile = Path.Combine(tempDir, "sys_mod_exec.hiv");
            string backupFile = Path.Combine(tempDir, "sys_bak_exec.hiv");

            // 1. 运行环境检查：28000 严禁 32 位运行
            if (!Environment.Is64BitProcess)
            {
                return "错误: 必须编译为 x64 并在 64 位环境下运行！";
            }

            try
            {
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                // 清理旧文件
                if (File.Exists(hiveFile)) { try { File.Delete(hiveFile); } catch { return "无法删除旧的临时 Hive 文件，请确保没有被挂载"; } }
                if (File.Exists(backupFile)) { try { File.Delete(backupFile); } catch { } }

                // 2. 增强提权：增加 TakeOwnership 权限
                bool p1 = EnablePrivilege("SeBackupPrivilege");
                bool p2 = EnablePrivilege("SeRestorePrivilege");
                bool p3 = EnablePrivilege("SeTakeOwnershipPrivilege"); // 应对 28000 可能需要的权限

                if (!p1 || !p2) return "权限提升失败 (SeBackup/Restore)";

                // 3. 打开 SYSTEM 键
                // 在 28000 中，导出 Hive 必须使用 READ_CONTROL (0x00020000)
                const uint READ_CONTROL = 0x00020000;
                int openRet = RegOpenKeyEx(HKEY_LOCAL_MACHINE, "SYSTEM", 0, (int)READ_CONTROL, out IntPtr hKey);
                if (openRet != 0) return $"打不开 SYSTEM 键, 错误码: {openRet}";

                // 4. 导出 Hive
                int saveRet = RegSaveKey(hKey, hiveFile, IntPtr.Zero);
                RegCloseKey(hKey); // 导出完立即关闭句柄

                if (saveRet != 0) return $"导出 Hive 失败: {saveRet} (检查是否有杀毒软件拦截)";

                // 5. 关键验证：检查导出的文件是否有效（必须大于 5MB）
                FileInfo fi = new FileInfo(hiveFile);
                if (!fi.Exists || fi.Length < 1024 * 1024 * 5)
                {
                    return $"导出异常: 文件大小仅为 {fi.Length / 1024} KB，导出不完整。";
                }

                // 6. 执行离线修改
                string targetType = (mode == 1) ? "ServerNT" : "WinNT";
                if (!PatchHiveOffline(hiveFile, targetType)) return "离线修改失败 (Load/Unload 环节错误)";

                // 7. 替换原系统 Hive
                // 注意：在 28000 中，如果 ret 为 5 (Access Denied)，说明内核锁定了该操作
                int replaceRet = RegReplaceKey(HKEY_LOCAL_MACHINE, "SYSTEM", hiveFile, backupFile);

                if (replaceRet == 0)
                {
                    return "SUCCESS";
                }
                else if (replaceRet == 5)
                {
                    return "替换失败: 拒绝访问 (Build 28000 内核已锁定 SYSTEM 蜂巢。请尝试在 PE 环境下替换程序导出的 C:\\temp\\sys_mod_exec.hiv)";
                }
                else
                {
                    return $"替换失败: 错误码 {replaceRet}";
                }
            }
            catch (Exception ex)
            {
                return $"异常: {ex.Message}";
            }
        }
        private static bool PatchHiveOffline(string hivePath, string targetType)
        {
            string tempKeyName = "TEMP_OFFLINE_SYS_MOD";

            if (RegLoadKey(HKEY_LOCAL_MACHINE, tempKeyName, hivePath) != 0) return false;

            try
            {
                int currentSet = 1;
                string selectPath = tempKeyName + "\\Select";
                if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, selectPath, 0, (int)KEY_READ, out IntPtr hKeySelect) == 0)
                {
                    int type = 0;
                    int data = 0;
                    int size = 4;
                    if (RegQueryValueEx(hKeySelect, "Current", IntPtr.Zero, ref type, ref data, ref size) == 0)
                    {
                        currentSet = data;
                    }
                    RegCloseKey(hKeySelect);
                }

                string setPath = $"{tempKeyName}\\ControlSet{currentSet:D3}\\Control\\ProductOptions";
                if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, setPath, 0, (int)KEY_SET_VALUE, out IntPtr hKey) != 0)
                {
                    setPath = $"{tempKeyName}\\ControlSet001\\Control\\ProductOptions";
                    if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, setPath, 0, (int)KEY_SET_VALUE, out hKey) != 0) return false;
                }

                byte[] dataBytes = Encoding.ASCII.GetBytes(targetType + "\0");
                int writeRet = RegSetValueEx(hKey, "ProductType", 0, REG_SZ, dataBytes, dataBytes.Length);

                RegCloseKey(hKey);
                RegFlushKey(HKEY_LOCAL_MACHINE);

                return writeRet == 0;
            }
            finally
            {
                RegUnLoadKey(HKEY_LOCAL_MACHINE, tempKeyName);
            }
        }
    }
}