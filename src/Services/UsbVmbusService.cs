using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ExHyperV.Tools;
using ExHyperV.Models;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

namespace ExHyperV.Services
{
    public class UsbVmbusService
    {
        // 全局活动连接记录表 (BusId -> VMName)
        public static ConcurrentDictionary<string, string> ActiveTunnels { get; } = new ConcurrentDictionary<string, string>();

        // 运行中的任务控制
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _activeCts = new();

        [DllImport("ws2_32.dll", SetLastError = true)]
        private static extern IntPtr socket(int af, int type, int protocol);

        private const int AF_HYPERV = 34;
        private const int SOCK_STREAM = 1;
        private const int HV_PROTOCOL_RAW = 1;
        private static readonly Guid ServiceId = Guid.Parse("45784879-7065-7256-5553-4250726F7879");

        private const int ProxyBufSize = 512 * 1024;

        public UsbVmbusService()
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime; } catch { }
        }

        private void Log(string msg) => Debug.WriteLine($"[ExHyperV-USB] [{DateTime.Now:HH:mm:ss.fff}] {msg}");

        // 停止隧道任务
        public void StopTunnel(string busId)
        {
            // 1. 停止后台线程并释放 Socket (解决 Attached 状态)
            if (_activeCts.TryRemove(busId, out var cts))
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                    Log($"[StopTunnel] 隧道已取消: {busId}");
                }
                catch { }
            }

            // 2. 核心补丁：强制执行 unbind (解决 Shared/Attached 状态，让宿主机拿回设备)
            // 这里使用 Task.Run 是为了不阻塞 UI 线程
            _ = Task.Run(async () => {
                Log($"[StopTunnel] 执行命令: usbipd unbind --busid {busId}");
                bool ok = await RunUsbIpCommand($"unbind --busid {busId}");
                if (ok) Log($"[StopTunnel] 设备 {busId} 已成功返回主机");
            });
        }

        // 自动恢复隧道 (由 Watchdog 调用)
        public async Task AutoRecoverTunnel(string busId, string vmName)
        {
            if (_activeCts.ContainsKey(busId)) return;
            var cts = new CancellationTokenSource();
            if (!_activeCts.TryAdd(busId, cts)) { cts.Dispose(); return; }

            try
            {
                Log($"[AutoRecover] 准备连接 {busId} -> {vmName}");

                // 解决手机变身的关键：先强制解绑再绑定
                await RunUsbIpCommand($"unbind --busid {busId}");
                bool bound = await RunUsbIpCommand($"bind --busid {busId}");
                if (!bound) bound = await RunUsbIpCommand($"bind --busid {busId} --force");

                if (!bound) return;

                var vms = await GetRunningVMsAsync();
                var targetVm = vms.FirstOrDefault(v => v.Name == vmName);
                if (targetVm == null) return;

                // 启动阻塞式隧道任务
                await StartTunnelAsync(targetVm.Id, busId, cts.Token);
            }
            catch (Exception ex) { Log($"[AutoRecover] 隧道中断: {ex.Message}"); }
            finally
            {
                _activeCts.TryRemove(busId, out _);
                cts.Dispose();
            }
        }

        public async Task StartTunnelAsync(Guid vmId, string busId, CancellationToken ct)
        {
            // 保持你的 P/Invoke Socket 创建方式
            IntPtr handle = socket(AF_HYPERV, SOCK_STREAM, HV_PROTOCOL_RAW);
            if (handle == (IntPtr)(-1)) throw new SocketException(Marshal.GetLastWin32Error());

            var safeHandle = new System.Net.Sockets.SafeSocketHandle(handle, true);
            Socket hv = new Socket(safeHandle);

            // 这是一个信号，用于监控 Pump 线程是否结束
            var completion = new TaskCompletionSource<bool>();
            ct.Register(() => {
                try { hv.Close(); } catch { }
                completion.TrySetResult(true);
            });

            try
            {
                Log($"Tunnel: Connecting VMBus {vmId}...");
                // 核心修复：在 P/Invoke 句柄上必须使用同步 Connect 配合 Task.Run，否则会报“无效参数”
                await Task.Run(() => hv.Connect(new HyperVEndPoint(vmId, ServiceId)), ct);

                hv.Blocking = true;
                hv.Send(Encoding.ASCII.GetBytes(busId + "\0"));

                Socket tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                tcp.NoDelay = true;

                Log("Tunnel: Connecting Local TCP 3240...");
                bool tcpOk = false;
                for (int i = 0; i < 5; i++)
                {
                    try { await tcp.ConnectAsync("127.0.0.1", 3240, ct); tcpOk = true; break; }
                    catch { await Task.Delay(500, ct); }
                }
                if (!tcpOk) throw new Exception("usbipd service unreachable");

                // 启动泵线程
                StartHighPriorityPump(hv, tcp, "VMBUS_TO_TCP", () => completion.TrySetResult(false), ct);
                StartHighPriorityPump(tcp, hv, "TCP_TO_VMBUS", () => completion.TrySetResult(false), ct);

                Log("Tunnel: Established.");
                // 关键：此处必须阻塞，直到任务被取消或 Pump 线程报错断开
                await completion.Task;
            }
            finally
            {
                try { hv.Dispose(); } catch { }
            }
        }

        private void StartHighPriorityPump(Socket sIn, Socket sOut, string label, Action onFault, CancellationToken ct)
        {
            new Thread(() =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
                byte[] buffer = GC.AllocateArray<byte>(ProxyBufSize, pinned: true);
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        int n = sIn.Receive(buffer, 0, buffer.Length, SocketFlags.None);
                        if (n <= 0) break;

                        int sent = 0;
                        while (sent < n)
                        {
                            int count = sOut.Send(buffer, sent, n - sent, SocketFlags.None);
                            if (count <= 0) break;
                            sent += count;
                        }
                    }
                }
                catch { }
                finally { onFault?.Invoke(); }
            })
            { IsBackground = true, Name = $"Pump_{label}" }.Start();
        }

        public async Task WatchdogLoopAsync(CancellationToken globalCt)
        {
            while (!globalCt.IsCancellationRequested)
            {
                foreach (var entry in ActiveTunnels)
                {
                    if (!_activeCts.ContainsKey(entry.Key))
                    {
                        _ = Task.Run(() => AutoRecoverTunnel(entry.Key, entry.Value));
                    }
                }
                await Task.Delay(2000, globalCt);
            }
        }

        public async Task<bool> EnsureDeviceSharedAsync(string busId)
        {
            return await RunUsbIpCommand($"bind --busid {busId}");
        }

        private async Task<bool> RunUsbIpCommand(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("usbipd", args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                var proc = Process.Start(psi);
                if (proc != null) { await proc.WaitForExitAsync(); return proc.ExitCode == 0; }
                return false;
            }
            catch { return false; }
        }

        public async Task<List<VmInfo>> GetRunningVMsAsync()
        {
            var list = new List<VmInfo>();
            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"Get-VM | Where-Object {$_.State -eq 'Running'} | ForEach-Object { $_.Name + '|' + $_.Id.Guid }\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.Default };
                using var proc = Process.Start(psi);
                string outStr = await proc.StandardOutput.ReadToEndAsync();
                foreach (var line in outStr.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = line.Trim().Split('|');
                    if (p.Length == 2) list.Add(new VmInfo { Name = p[0], Id = Guid.Parse(p[1]) });
                }
            }
            catch { }
            return list;
        }

        public async Task<List<UsbDeviceModel>> GetUsbIpDevicesAsync()
        {
            var list = new List<UsbDeviceModel>();
            try
            {
                var psi = new ProcessStartInfo("usbipd", "list") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.Default };
                using var proc = Process.Start(psi);
                string outStr = await proc.StandardOutput.ReadToEndAsync();
                var lines = outStr.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                bool foundSection = false;
                foreach (var line in lines)
                {
                    if (line.Contains("BUSID")) { foundSection = true; continue; }
                    if (foundSection && line.Trim().Length > 0 && char.IsDigit(line.Trim()[0]))
                    {
                        var m = Regex.Match(line.Trim(), @"^([0-9\-.]+)\s+([0-9a-fA-F:]+)\s+(.*?)\s{2,}");
                        if (m.Success) list.Add(new UsbDeviceModel { BusId = m.Groups[1].Value.Trim(), VidPid = m.Groups[2].Value.Trim(), Description = m.Groups[3].Value.Trim(), Status = "Ready" });
                    }
                }
            }
            catch { }
            return list;
        }

        public void EnsureServiceRegistered()
        {
            try
            {
                string regPath = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices\{ServiceId:B}";
                using var key = Registry.LocalMachine.CreateSubKey(regPath);
                key.SetValue("ElementName", "ExHyperV USB Proxy Infrastructure");
            }
            catch { }
        }
    }
}