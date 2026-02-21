<h1>
  <img src="https://github.com/Justsenger/ExHyperV/blob/main/img/logo.png?raw=true" width="32" alt="ExHyperV logo"> 
  ExHyperV
</h1>

<div align="center">

**一款图形化的 Hyper-V 管理工具，能让凡人也能轻松玩转Hyper-V的高级功能。**

</div>

<p align="center">
  <a href="https://github.com/Justsenger/ExHyperV/releases/latest"><img src="https://img.shields.io/github/v/release/Justsenger/ExHyperV.svg?style=flat-square" alt="Latest release"></a>
  <img width="3" src="data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7">
  <a href="https://github.com/Justsenger/ExHyperV/releases"><img src="https://aged-moon-0505.shalingye.workers.dev/" alt="Downloads"></a>
  <img width="3" src="data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7">
  <a href="https://t.me/ExHyperV"><img src="https://img.shields.io/badge/discussion-Telegram-blue.svg?style=flat-square" alt="Telegram"></a>
  <img width="3" src="data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7">
  <a href="https://github.com/Justsenger/ExHyperV/blob/main/LICENSE"><img src="https://img.shields.io/github/license/Justsenger/ExHyperV.svg?style=flat-square" alt="License"></a>
</p>

[English](https://github.com/Justsenger/ExHyperV) | **中文**

---

ExHyperV 通过深入研究 Hyper-V 文档、 [WMI](https://github.com/Justsenger/HyperV-WMI-Documentation) 以及 [HCS](https://learn.microsoft.com/en-us/virtualization/api/hcs/overview) 等技术细节，旨在为用户提供一个图形化的、易于使用的 Hyper-V 高级功能配置工具。

由于个人时间和精力有限，项目可能存在未经测试的场景或错误。如果您在使用中遇到任何关于硬件/软件的问题，欢迎通过 [Issues](https://github.com/Justsenger/ExHyperV/issues) 提出！

各项功能将随着时间的推进逐步完善。如果您有特别希望优先添加的功能，或非常喜爱此项目，可以通过[赞赏](https://afdian.com/a/saniye)按钮提供赞助并留言！

## 🎨 界面一览

ExHyperV 使用 [WPF-UI](https://github.com/lepoco/wpfui) 框架，提供流畅现代的用户界面体验和科幻的视觉效果。支持黑色主题和白色主题，并且会根据系统主题自动切换。

![主界面](https://github.com/Justsenger/ExHyperV/blob/main/img/01.png)

<details>
<summary>点击查看更多界面截图</summary>
  
![功能](https://github.com/Justsenger/ExHyperV/blob/main/img/02.png)
![功能](https://github.com/Justsenger/ExHyperV/blob/main/img/03.png)
![功能](https://github.com/Justsenger/ExHyperV/blob/main/img/04.png)
![功能](https://github.com/Justsenger/ExHyperV/blob/main/img/05.png)
![功能](https://github.com/Justsenger/ExHyperV/blob/main/img/06.png)
![功能](https://github.com/Justsenger/ExHyperV/blob/main/img/07.png)
![功能](https://github.com/Justsenger/ExHyperV/blob/main/img/08.png)
![功能](https://github.com/Justsenger/ExHyperV/blob/main/img/09.png)
![功能](https://github.com/Justsenger/ExHyperV/blob/main/img/10.png)
![功能](https://github.com/Justsenger/ExHyperV/blob/main/img/11.png)
![功能](https://github.com/Justsenger/ExHyperV/blob/main/img/12.png)

</details>

## 🚀 快速开始
---
### 1. 下载与运行
- **下载**: 前往 [Releases 页面](https://github.com/Justsenger/ExHyperV/releases/latest)下载最新版本。
- **运行**: 解压后直接运行 `ExHyperV.exe` 即可。
---
### 2. 构建 (可选)
1. 安装 [Visual Studio](https://visualstudio.microsoft.com/vs/)，并确保勾选 .NET 桌面开发。
2. 使用 GitHub Desktop 或 Git 克隆本仓库。
3. 使用 Visual Studio 打开 `/src/ExHyperV.sln` 文件，即可编译。

除此之外，您也可以直接下载 [.NET SDK](https://dotnet.microsoft.com/zh-cn/download) ,打开项目目录：
```pwsh
cd src
dotnet build
```

## 📖 技术文档

这部分内容将长期维护，根据 HyperV 相关文档以及开发实践编写而成，可能会存在问题。

---
### Hyper-V 简介
> [!NOTE]
> Hyper-V 是基于 Type-1 架构的高性能虚拟机管理软件（Hypervisor）。

当您开启Hyper-V功能后，宿主系统将变成属于根分区的一个具有特权的虚拟机。创建的虚拟机属于子分区，它们相互隔离，无法感知彼此的存在。

属于 Type-1 架构的虚拟化技术包括：Hyper-V、Proxmox (KVM)、VMware ESXi (VMkernel)、Xen 等，性能利用率大约在98%以上。

属于 Type-2 架构的虚拟化技术包括：VMware Workstation、Oracle VirtualBox、Parallels Desktop 等，性能利用率大约在90%~95%。

基于这样的事实，您可以将虚拟机看作一个个隔离的小房间，在里面运行具有潜在威胁的程序、测试系统功能、多开游戏或者其他用途，而不用担心弄糟宿主系统（1.PCIe 直通中不支持 FLR 的情况除外，虚拟机重启会连带宿主重启。2.具有横向渗透能力的病毒也不在此列，请注意网络安全）。

您可以通过控制面板开启/关闭 HyperV 功能，或一行简单的命令 Powershell 并按 Y 确认（需要专业版或服务器版），重启后vmms.exe、vmcompute.exe以及vmmem等进程将在后台持续运行，同时开始菜单将出现 HyperV 管理器图标。
```
Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All
```
---
### 调度器
> [!NOTE]
> 调度器用于协调如何将物理处理器的 CPU 时间分配给虚拟机处理器。

Hyper-V 具有三种调度器：Classic / Core / Root ，分别叫经典调度器、核心调度器和根调度器。可以分为两类：手动挡（Classic、Core）和自动挡（Root）。Core可以视作Classic的变种，提高了安全性，但是会降低部分场景下的性能。

Classic 调度器的出现时间可以追溯到 Windows Server 2008，是基于传统时间片轮换的公平分配原则，将虚拟机的处理器时间随机分给宿主所有可用的逻辑处理器；在宿主资源空闲的情况下，更可能分配到不同物理核心的逻辑处理器，而不是超线程，从而获取更好的性能。

Core 调度器出现时间稍晚，自 Windows Server 2016 以及 Windows 10 Build 14393推出，目的在于缓解侧信道攻击，即使是宿主资源空闲的情况下，也更倾向于分配同一个物理核心的两个线程，而不是更多物理核心。这样的策略有助于提高安全性和虚拟机隔离，但是会显著降低宿主资源空闲时虚拟机能分配到的CPU性能。从 Windows Server 2019 起，Windows Server 将默认使用核心调度器。 

Root 调度器发布于Windows 10 Build 17134，它会收集工作负荷 CPU 使用情况的指标，自动作出调度决策，对于大小核的CPU架构来说非常适合。从 Build 17134 起，专业版Windows Hyper-V 将默认使用 Root 调度器。

系统类型与调度器的类型无关，可以任意切换，重启宿主后生效。

---
### 处理器（vCPU）
> [!NOTE]
> 虚拟机向宿主申请逻辑处理器执行时间的调度能力。

#### 计算资源

##### 核心数

通常设定为2、4、8、16等偶数。

增加 vCPU 会显著提升并行任务的处理速度，但过多不必要的 vCPU 可能为 Hypervisor 带来调度压力。

若所有虚拟机的核心总数超过宿主物理逻辑核心数（超售），需要即时响应的应用会受到极大影响。

##### 预留

为该虚拟机提供的执行时间下限百分比。预留值=预留*核心数。

##### 限制

为该虚拟机提供的执行时间上限百分比。限制值=限制*核心数。

##### 权重

该虚拟机在 CPU 执行时间竞争中的优先级，范围是0~10000。

#### 高级功能

##### 主机资源保护

开启后对虚拟机通过 VMBus 通信的 I/O 请求进行监测，出现中断风暴等异常行为时降低 CPU 执行时间分配，以防止影响根分区的宿主系统。普通用户无需开启。

##### 嵌套虚拟化

开启后透传 CPU 的 VT-x/AMD-V 指令集扩展，允许在 Hyper-V 虚拟机里再运行虚拟机，将略微增加 CPU 虚拟化开销。

虚拟机开启嵌套虚拟化以及 HyperV 功能后，虚拟机的任务管理器将显示L1/L2/L3缓存拓扑，并不再标记为“虚拟机：是”，对于过虚拟化有一定帮助。

<div align="center">
<img width="616" height="532" alt="image" src="https://github.com/user-attachments/assets/00c838f1-91ef-42db-bf21-34c5b49b08b9" />
</div>


##### 迁移兼容性

开启后将屏蔽 CPU 的高级指令集（如 AVX-512 等），便于在不同硬件的宿主上进行实时迁移。普通用户无需开启。

##### 旧系统兼容性

开启后将大幅精简 CPU 指令集，有利于运行 Windows 7 甚至更早的操作系统，不利于运行现代操作系统。

##### 虚拟机 SMT

开启后虚拟机能感知到它的 vCPU 是成对出现的逻辑核心，有助于虚拟机内部的操作系统内核更好地进行 L1/L2 缓存优化和进程调度。

##### 隐藏 Hypervisor 标识 

这是一个早期开关，功能尚不明朗。位于[Msvm_ProcessorSettingData/HideHypervisorPresent](https://github.com/Justsenger/HyperV-WMI-Documentation/blob/main/docs/Msvm_ProcessorSettingData.md)。

指示 Hyper-V 是否应向嵌套客体报告存在虚拟机监控程序。

##### 暴露架构性能监控单元

开启后透传 CPU 的硬件计数器，允许虚拟机里的开发工具直接访问物理 CPU 的性能监测硬件。

##### 暴露频率监视寄存器

开启后允许虚拟机操作系统读取物理处理器的真实频率，默认是开启的。

##### 禁用侧信道攻击缓解 

开启后关闭对 Spectre、Meltdown等漏洞的软件补丁，小幅提升性能，虚拟化安全性下降。

##### 启用插槽拓扑 

开启后将为虚拟机模拟多个物理插槽，对于多个物理处理器的系统或许有用。

##### CPU 绑定

CPU 绑定实现基于 CPU 组（经典调度器+核心调度器）+ 进程亲和性（根调度器），允许您将vCPU强制锁定到指定的核心上。

最好的实践是4个vCPU绑定到4个核心。2个vCPU绑定到4个核心将发生随机漂移，4个vCPU绑定到2个核心将发生排队，以此类推。

如果您发现调度器的表现很糟糕，或者对于 Intel 的混合架构和 AMD 的多芯片架构有任何顾虑，请尝试使用此功能。

---
### 内存

虚拟机能支配运行内存的能力。Hyper-V 通过二级地址转换技术将虚拟机的物理内存请求直接映射到真实的物理内存条。

#### 计算资源

##### 启动内存

虚拟机在开机时必须占用的物理内存量。若宿主空闲内存不够，可能会启动失败。若操作系统支持，可在运行时修改此数值从而增大或缩减内存分配量（待做，没做踢我）。

##### 内存权重

当宿主机物理内存不足时，多个虚拟机之间争抢内存的优先级。

##### 动态内存

允许 Hypervisor 根据虚拟机内部的实际需求，实时伸缩分配的内存量。

最小内存不能大于启动内存。最大内存的最终实现不会超过宿主机物理内存上限。

当使用 GPU-PV 或 DDA 时，启动内存必须和最小内存相同以便确定内存地址映射关系。

#### 高级功能

##### 内存页大小

决定虚拟内存与物理内存映射时的“颗粒度”，可选4K、2M、1G。该选项从12.0配置版本的虚拟机上可用，有利于大型数据库或高性能计算任务。

##### 内存加密

开启后将利用硬件特性（AMD SEV 或 Intel TDX）对内存数据进行实时加密，即使是宿主机也无法读取内存数据，开启后会带来轻微的内存延迟和 CPU 负载增加。


---
### 存储

虚拟机能访问的存储介质。分为虚拟文件和物理设备。虚拟文件有vhdx、vhd和iso等格式，模拟的是一个完整的物理硬盘或物理光驱。物理设备可选择宿主机上可用的硬盘或光驱进行直通。

下拉菜单可实时查看读写速率以及容量变化。左侧的数字是文件大小，右侧的数字是容量上限。

#### 插槽配置

Hyper-V 要求您将虚拟文件或物理设备挂载到IDE控制器或SCSI控制器上来供虚拟机访问。但是ExhyperV简化这个操作为自动分配，允许您只关心媒体来源而无需关心插槽分配。

如果您尝试理解复杂的插槽逻辑，请参考以下规则：

1.对于运行中的1代虚拟机，IDE控制器是无法被卸载的，但是ISO可以弹出和插入。

2.对于1代虚拟机，ISO只能插入IDE控制器。1代虚拟机只能从IDE控制器的介质上启动。

3.对于1代虚拟机，总共有2个IDE控制器x2+4个SCSI控制器x64=260个位置可以使用。

4.对于2代虚拟机，SCSI控制器及里面的存储介质随时都可以弹出和移除，因此请小心操作。

5.对于2代虚拟机，总共有4个SCSI控制器x64=256个位置可以使用。


#### 媒体设置

##### 来源

选择虚拟文件或者可用的物理设备。部分USB存储介质由于无法脱机，不会出现在可用列表。

##### 虚拟文件

###### 类型为硬盘

当磁盘类型为动态扩展，初始值为一个很小的值（取决于块大小和容量）而不是容量大小，会逐渐增大。

当磁盘类型为固定大小，初始值即为容量大小且不会变化，读写效率更高。

当磁盘类型为差分类型，需要指定一个动态扩展/固定大小的虚拟硬盘从而继承它的一切参数（实际上可以通过二进制自定义，但是意义不大）。

扇区格式：512n、512e（默认）、4kn，分别是物理扇区大小和逻辑扇区大小为（512+512、4096+512和4096+4096的组合），普通用户保持默认即可。

块大小：最小存储单位。块越大，读写效率越高，空间利用率越低；块越小，读写效率越低，空间利用率越高。

###### 类型为光驱

利用 DiscUtils 实现的ISO创建，可实现将指定文件夹快速打包并挂载的功能，文档待完善。

---
### 显卡

虚拟机能通过GPU-PV技术访问宿主机物理显卡的能力。该功能非常依赖于WDDM版本，宿主和虚拟机尽量使用最新的系统版本。

GPU-PV (或称 GPU-P) 是一种半虚拟化技术，它允许多个虚拟机共享使用物理 GPU 的计算能力，而无需完整直通。

- **资源限制**: 目前 Hyper-V 原生无法有效限制每个虚拟机使用的 GPU 资源。`Set-VMGpuPartitionAdapter` 中的参数并不生效 ([相关讨论](https://github.com/jamesstringerparsec/Easy-GPU-PV/issues/298))。因此，本工具暂不提供资源分配功能。
- **驱动与兼容性**: GPU-P 创建的虚拟设备虽然能调用物理 GPU，但并未完整继承其硬件特征和驱动细节。某些依赖特定硬件ID或驱动签名的软件/游戏可能无法运行。

下拉菜单可查看该虚拟机上所有GPU-PV分区的图形引擎利用率，包含四个常用引擎：3D、Copy、编码和解码。

#### 系统要求

宿主和虚拟机必须是如下版本才能启用此能力。

- Windows 10 （Build 17134+）
- Windows 11
- Windows Server 2019 
- Windows Server 2022
- Windows Server 2025

虚拟机必须是大于9.0的配置版本才能分配GPU-PV显卡。不限制是1代虚拟机或2代虚拟机。

启用了GPU-PV的虚拟机不支持检查点功能。

启用了GPU-PV的显卡必须存在于宿主机，不能同时用于DDA直通。

从同一张显卡获取的多个GPU-PV显卡分区不能提供超过物理上限的算力。

虚拟机可以获取来自不同显卡的GPU-PV显卡分区。

可能存在[内存泄露问题](https://github.com/jamesstringer90/Easy-GPU-PV/issues/446)，建议将宿主机系统版本更新到 `26100.4946`以上。

#### WDDM 版本与 GPU-P 功能演进
> WDDM (Windows Display Driver Model) 版本越高，GPU-P 功能越完善。建议宿主和虚拟机都使用最新的 Windows 版本。

| Windows 版本 (Build) | WDDM 版本 | 主要虚拟化功能更新 |
| :--- | :--- | :--- |
| 17134 | 2.4 | 首次引入基于 IOMMU 的 GPU 隔离。 |
| 17763 | 2.5 | 优化宿主与虚拟机间的资源管理与通信。 |
| 18362 | 2.6 | 提升显存管理效率，优先分配连续物理显存。 |
| 19041 | 2.7 | 虚拟机设备管理器可正确识别物理显卡型号。 |
| 20348 | 2.9 | **开始支持 Linux 虚拟机及 WSL2。** 支持跨适配器资源扫描输出，延迟更低。 |
| 22000 | 3.0 | 支持 DMA 重映射，突破 GPU 内存地址限制。 |
| 22621 | 3.1 | UMD/KMD 内存共享，减少数据复制，提升效率。 |
| 26100 | 3.2 | 引入 GPU 实时迁移、WDDM 功能查询等新特性。虚拟机任务管理器可查看GPU性能。 |

#### GPU-PV 显卡兼容性列表 (使用Gpu Caps Viewer+DXVA Checker测试)

| 品牌 | 型号 | 架构 | 识别 | DirectX 12 | OpenGL | Vulkan | Codec | CUDA/OpenCL | 备注 |
| :--- | :--- | :--- | :--- |:--- | :--- | :--- | :--- | :--- | :--- |
| **Nvidia** | RTX 4090 | Ada Lovelace | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | |
| **Nvidia** | RTX 4080 Super | Ada Lovelace | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | |
| **Nvidia** | RTX 2080 Super | Turing | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | |
| **Nvidia** | GTX 1050 | Pascal | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | |
| **Nvidia** | GT 210 | Tesla | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | 不支持 |
| **Intel**| Iris Xe Graphics| Xe-LP | ⚠️ | ✅ | ✅ | ✅ | ✅ | ❌ | 硬件识别残缺| 
| **Intel**| A380 | Xe-HPG | ⚠️ | ✅ | ✅ | ✅ | ✅ | ❌ | 硬件识别残缺|
| **Intel**| UHD Graphics 730 | Xe-LP | ⚠️ | ✅ | ✅ | ✅ | ✅ | ❌ | 硬件识别残缺|
| **Intel**| UHD Graphics 620 Mobile | Generation 9.5 | ⚠️ | ✅ | ✅ | ✅ | ✅ | ❌ | 硬件识别残缺|
| **Intel**| HD Graphics 530 | Generation 9.0 | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | 不支持 |
| **AMD** | Radeon Vega 3 | GCN 5.0 | ⚠️ | ✅ | ✅ | ✅ | ✅ | ✅ | 硬件识别残缺|
| **AMD** | Radeon 8060S | RDNA 3.5 | ⚠️ | ✅ | ✅ | ✅ | ✅ | ❌ | 硬件识别残缺 |
| **AMD** | Radeon 890M | RDNA 3.5 | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | 启动会导致宿主崩溃 |
| **Moore Threads** | MTT S80 | MUSA | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | 不支持 |

#### 如何从虚拟机输出画面？

GPU-P 模式下，物理 GPU 作为“渲染设备”，需要搭配一个“显示设备”来输出画面。有以下三种方案：

1.  **Microsoft Hyper-V 视频 (默认)**
    - **优点**: 兼容性好，开箱即用。
    - **缺点**: 分辨率最高 1080p，刷新率低 (约 60Hz)。

2.  **间接显示驱动 + 串流 (推荐)**
    - 安装 [虚拟显示驱动](https://github.com/VirtualDrivers/Virtual-Display-Driver) 创建一个高性能的虚拟显示器（似乎需要次新版本）。
    - 使用 Parsec, Sunshine, 或 Moonlight 等串流软件，配对并设置好开机启动，在关闭RDP以及其他远程桌面的情况下连接，从而获得高分辨率、高刷新率的流畅体验。
    - ![Sunshine+PV 示例](https://github.com/user-attachments/assets/e25fce26-6158-4052-9759-6d5d1ebf1c5d)

3.  **USB 显卡 + GPU-PV**
    - **思路**: 通过 DDA 直通一个 USB 控制器给虚拟机，再连接一个 USB 显卡（如基于 [DisplayLink DL-6950](https://www.synaptics.com/cn/products/displaylink-graphics/integrated-chipsets/dl-6000) 或 [Silicon Motion SM768](https://www.siliconmotion.com/product/cht/Graphics-Display-SoCs.html) 芯片的产品）作为显示设备。
    - **状态**: 此方案与大显存显卡存在内存资源冲突问题，目前不推荐普通用户尝试。

#### 配置流程

##### 环境准备

为宿主系统环境添加注册表，禁用安全策略等避免分配GPU-PV后无法启动虚拟机。

##### 电源检查

配置MMIO空间和开启写合并缓存需要关闭虚拟机电源。

##### 系统优化

配置MMIO空间和开启写合并缓存。

##### 分配显卡

将选择的显卡创建GPU-PV分区并分配到虚拟机。

##### 驱动安装

可选项，对于Windows虚拟机，将自动检查显卡驱动文件夹（失败则全量注入）并注入到虚拟机指定位置（可能需要手动选择分区），并添加针对Nvidia的注册表修复。

对于Linux虚拟机，会执行另一套SSH相关流程进行模块编译和驱动安装，兼容列表之外的系统或内核需要更多测试。

![Linux&Blender](https://github.com/Justsenger/ExHyperV/blob/main/img/Linux.png)

已知兼容性：
| 系统 | 内核版本 | Dxgkrnl | CUDA | Vulkan | OpenGL | Codec |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | 
| Ubuntu 24.04  | 6.14.0-36-generic | ❌ | \ | \ | \ | \ |
| Ubuntu 22.04  | 6.8.0-87-generic | ✅ | ✅ | ✅ | ✅ | ✅ |
| Arch Linux | 6.6.119-1-lts66 | ✅ | ✅ | 未测试 | 未测试 | 未测试 |
| fnOS 0.9.2 | 6.12.18-trim | ❌ | \ | \ | \ | \ |

---
### 网络

待完善

---
### DDA (离散设备分配)

DDA 允许将一个完整的 PCIe 设备（如显卡、网卡、声卡、USB 控制器）从宿主机卸载并直接分配给虚拟机。

#### 可分配设备

DDA以PCIe设备为单位划分可分配设备。如果设备未显示在列表中，意味着它无法被独立分配，您需要尝试分配其更上一级的 PCIe 控制器。

#### 虚拟机系统

推荐使用 Windows，Linux 未经测试。

#### 宿主系统要求

- Windows Server 2019 
- Windows Server 2022
- Windows Server 2025

**黑魔法**：如果您想在非Server系统上使用 DDA 功能，可以尝试切换系统版本的开关，将标识位从WinNT变为ServerNT，从而欺骗Hypervisor。

#### DDA 设备的三种状态

1.  **主机态 (Host)**: 设备正常挂载到宿主系统，可被宿主使用。
2.  **卸除态 (Dismounted)**: 设备已从宿主卸载 (`Dismount-VMHostAssignableDevice`)，但未分配给虚拟机。此时设备在宿主设备管理器中不可用，可通过本工具重新挂载到宿主或分配给虚拟机。
3.  **虚拟态 (Guest)**: 设备已成功挂载到虚拟机。

#### DDA 显卡兼容性列表 (持续更新)
> 兼容性表现需要在虚拟机中安装驱动后才能确认。欢迎通过 [Issues](https://github.com/Justsenger/ExHyperV/issues) 分享您的测试结果！

| 品牌 | 型号 | 架构 | 启动 | 功能层复位 (FLR) | 物理显示输出 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Nvidia** | RTX 5090 | Blackwell 2.0 | ✅ | ✅ | ✅ |
| **Nvidia** | RTX 4090 | Ada Lovelace | ✅ | ✅ | ✅ |
| **Nvidia** | RTX 4080 Super | Ada Lovelace | ✅ | ✅ | ✅ |
| **Nvidia** | RTX 4070 | Ada Lovelace | ✅ | ✅ | ✅ |
| **Nvidia** | RTX 2080 Super | Turing | ✅ | ✅ | ✅ |
| **Nvidia** | GTX 1660 Super | Turing | ✅ | ✅ | ✅ |
| **Nvidia** | GTX 1050 | Pascal | ✅ | ✅ | ✅ |
| **Nvidia** | GT 1030 | Pascal | ✅ | ✅ | ✅ |
| **Nvidia** | GT 210 | Tesla | ✅ | ✅ | ❌ |
| **Intel** | DG1 | Xe-LP | ✅ | ❌ | [特定驱动](https://www.shengqipc.cn/d21.html) ✅ |
| **Intel** | A380 | Xe-HPG | Code 43 ❌ | ✅ | ❌ |
| **Intel**| UHD Graphics 620 Mobile | Generation 9.5 | 无法直通❌ | ❌ | ❌ | 
| **Intel**| HD Graphics 610 | Generation 9.5 | 无法直通❌ | ❌ | ❌ | 
| **Intel**| HD Graphics 530 | Generation 9.0 | 无法直通❌ | ❌ | ❌ | ❌ |
| **AMD** | RX 580 | GCN 4.0 | Code 43 ❌ | ✅ | ❌ |
| **AMD** | Radeon Vega 3 | GCN 5.0 | Code 43 ❌ | ❌ | ❌ |

- **启动**: 分配到虚拟机后能否成功安装驱动并被识别。代码43 说明驱动不允许显卡在虚拟机内工作。
- **功能层复位 (FLR)**: 若不支持此功能，重启虚拟机会导致宿主机也重启。
- **物理显示输出**: 虚拟机能否通过显卡的物理接口（HDMI/DP）输出画面。
---
### 虚拟交换机

待完善




## 🤝 贡献
欢迎任何形式的贡献！
- **测试与反馈**: 帮助我们完善兼容性列表或测试潜在的Bug。
- **报告 Bug**: 通过 [Issues](https://github.com/Justsenger/ExHyperV/issues) 提交您遇到的问题。
- **代码贡献**: Fork 项目并提交 Pull Request。

## ❤️ 支持项目
如果你觉得这个项目对你有帮助，欢迎考虑赞助我，这能激励我持续进行维护和开发！

[![Sponsor](https://img.shields.io/static/v1?label=Sponsor&message=%E2%9D%A4&logo=GitHub&color=%23fe8e86)](https://afdian.com/a/saniye)
