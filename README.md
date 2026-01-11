<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 8.0"/>
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows" alt="Windows"/>
  <img src="https://img.shields.io/badge/License-GPL-3.0-green?style=flat-square" alt="GPL 3.0 License"/>
</p>

# Thread Optimization - CPU 线程优化工具

> 一款专为 Windows 设计的进程 CPU 亲和性管理工具，智能识别 Intel/AMD 混合架构，优化游戏与生产力应用的核心调度策略。

## <img src="https://img.shields.io/badge/-功能特性-blue?style=flat-square" alt="功能特性"/>

### <img src="https://img.shields.io/badge/-智能识别-6c5ce7?style=flat-square" alt="智能识别"/>
- **Intel 混合架构**：自动识别 P核（性能核心）与 E核（能效核心）
- **AMD 多 CCD 架构**：支持 CCD0/CCD1 独立调度
- **AMD X3D 处理器**：精准识别 3D V-Cache 核心
- **超线程/SMT 识别**：区分物理核心与逻辑线程

### <img src="https://img.shields.io/badge/-核心调度-00b894?style=flat-square" alt="核心调度"/>
- **进程绑定**：将目标进程限制在指定 CPU 核心上运行
- **优先核心**：设置首选调度核心
- **进程优先级**：调整目标进程的系统优先级
- **子线程同步**：自动将配置应用到进程的所有子线程

### <img src="https://img.shields.io/badge/-绑定模式-e17055?style=flat-square" alt="绑定模式"/>
| 模式 | 说明 |
|------|------|
| 动态 | 允许系统在选定核心间自由调度 |
| 静态 | 固定绑定到选定核心，不允许迁移 |
| D2 | 智能动态调度（Beta） |
| D3 省电 | 优先使用低功耗核心（Beta） |

### <img src="https://img.shields.io/badge/-实时监控-fdcb6e?style=flat-square" alt="实时监控"/>
- CPU 总体使用率
- 每核心使用率可视化
- CPU 频率监控
- 系统内存使用情况

### <img src="https://img.shields.io/badge/-配置管理-0984e3?style=flat-square" alt="配置管理"/>
- **配置档案**：保存/加载多套配置方案
- **进程组**：批量管理多个进程的核心分配
- **导入/导出**：JSON 格式配置文件，便于备份迁移
- **快速预设**：游戏模式、省电模式、生产力模式一键切换

## <img src="https://img.shields.io/badge/-支持的处理器-74b9ff?style=flat-square" alt="支持的处理器"/>

### Intel（第 12-15 代 & Core Ultra）

| 系列 | 典型型号 | 架构 |
|------|----------|------|
| 12 代 | i9-12900K, i7-12700K, i5-12600K | Alder Lake |
| 13 代 | i9-13900K, i7-13700K, i5-13600K | Raptor Lake |
| 14 代 | i9-14900K, i7-14700K, i5-14600K | Raptor Lake Refresh |
| Core Ultra | Ultra 9 185H, Ultra 7 165H | Meteor Lake |

### AMD Ryzen

| 系列 | 典型型号 | 特性 |
|------|----------|------|
| 9000 | 9950X, 9900X, 9700X, 9600X | Zen 5 |
| 9000X3D | 9950X3D, 9900X3D, 9800X3D | Zen 5 + 3D V-Cache |
| 7000 | 7950X, 7900X, 7800X, 7700X | Zen 4 |
| 7000X3D | 7950X3D, 7900X3D, 7800X3D | Zen 4 + 3D V-Cache |
| 5000 | 5950X, 5900X, 5800X, 5600X | Zen 3 |
| 5000X3D | 5800X3D, 5700X3D | Zen 3 + 3D V-Cache |
| Threadripper | 7980X, 7970X, 5995WX | HEDT |

## <img src="https://img.shields.io/badge/-界面预览-a29bfe?style=flat-square" alt="界面预览"/>

### 核心类型颜色标识
| 颜色 | 类型 | 说明 |
|:----:|------|------|
| <img src="https://img.shields.io/badge/-_-007AFF?style=flat-square" alt="蓝色"/> | P核 | Intel 性能核心 |
| <img src="https://img.shields.io/badge/-_-30D158?style=flat-square" alt="绿色"/> | E核 | Intel 能效核心 |
| <img src="https://img.shields.io/badge/-_-FF9F0A?style=flat-square" alt="橙色"/> | 3D | AMD 3D V-Cache 核心 |
| <img src="https://img.shields.io/badge/-_-BF5AF2?style=flat-square" alt="紫色"/> | 标准 | AMD 标准核心 |

## <img src="https://img.shields.io/badge/-安装与运行-55efc4?style=flat-square" alt="安装与运行"/>

### 系统要求
- Windows 10/11 (x64)
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- 管理员权限（修改进程亲和性需要）

### 快速开始
1. 下载最新 Release
2. **以管理员身份运行** `CoreX.exe`
3. 输入目标进程名（如 `game`，无需 `.exe` 后缀）
4. 选择要分配的 CPU 核心
5. 点击「启动」开始监控

## <img src="https://img.shields.io/badge/-从源码编译-fab1a0?style=flat-square" alt="从源码编译"/>

### 环境准备
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 / VS Code / Rider

### 命令行编译

```bash
# 克隆仓库
git clone https://github.com/YOUR_USERNAME/CoreX.git
cd CoreX

# 还原依赖
dotnet restore

# Debug 编译
dotnet build

# Release 编译
dotnet build -c Release

# 发布为单文件可执行程序
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Visual Studio 编译
1. 打开 `Thread Optimization.sln`
2. 选择 `Release` 配置
3. 生成 → 生成解决方案（Ctrl+Shift+B）

## <img src="https://img.shields.io/badge/-使用场景与建议-81ecec?style=flat-square" alt="使用场景与建议"/>

### 游戏优化

**Intel 混合架构**
- 游戏进程 → 仅使用 P 核
- 后台程序（录制/直播）→ 绑定到 E 核
- 推荐：高优先级 + 动态模式

**AMD X3D 处理器**
- 游戏进程 → 绑定到 3D V-Cache 核心（CCD0）
- 7950X3D/7900X3D：游戏用 CCD0，生产力用 CCD1
- 7800X3D/9800X3D：全部核心均为 V-Cache

**AMD 多 CCD 处理器**
- 延迟敏感应用 → 绑定到单个 CCD
- 避免跨 CCD 调度带来的访存延迟

### 生产力优化
- 渲染/编译 → 使用全部核心
- 日常办公 → D3 省电模式减少功耗

## <img src="https://img.shields.io/badge/-项目结构-dfe6e9?style=flat-square" alt="项目结构"/>

```
Thread Optimization/
├── Thread Optimization.sln          # 解决方案
└── Thread Optimization/
    ├── App.xaml                     # WPF 应用入口
    ├── MainWindow.xaml              # 主界面
    ├── Converters/                  # 值转换器
    ├── Models/                      # 数据模型
    │   ├── AppConfig.cs             # 应用配置
    │   ├── CpuCore.cs               # CPU 核心模型
    │   ├── ProcessGroup.cs          # 进程组
    │   └── ProcessInfo.cs           # 进程信息
    ├── Services/                    # 业务服务
    │   ├── AffinityService.cs       # 亲和性设置
    │   ├── CpuService.cs            # CPU 信息检测
    │   ├── MonitorService.cs        # 实时监控
    │   └── ProcessService.cs        # 进程管理
    ├── ViewModels/                  # MVVM ViewModel
    │   └── MainViewModel.cs
    ├── Views/                       # 窗口视图
    └── Styles/                      # UI 样式
```

## <img src="https://img.shields.io/badge/-技术栈-636e72?style=flat-square" alt="技术栈"/>

| 组件 | 技术 |
|------|------|
| 框架 | .NET 8.0 + WPF |
| 架构 | MVVM |
| UI 工具包 | CommunityToolkit.Mvvm |
| 系统 API | System.Management, Win32 API |

## <img src="https://img.shields.io/badge/-注意事项-d63031?style=flat-square" alt="注意事项"/>

1. **管理员权限**：修改进程亲和性和优先级必须以管理员身份运行
2. **系统进程**：部分受保护的系统进程无法修改亲和性
3. **CPU 识别**：未知型号将使用估算值，可能不完全准确
4. **X3D 识别**：V-Cache 核心识别基于 CCD 位置规则

## <img src="https://img.shields.io/badge/-许可证-00cec9?style=flat-square" alt="许可证"/>

本项目基于 [MIT License](LICENSE) 开源。

## <img src="https://img.shields.io/badge/-致谢-fd79a8?style=flat-square" alt="致谢"/>

- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 提供 MVVM 基础设施

---

<p align="center">
  <sub>Made with care for PC enthusiasts</sub>
</p>
