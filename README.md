# ImmersingHomework - 班级大屏作业板

一个现代化的班级大屏作业展示应用，使用 Avalonia UI 框架开发，支持跨平台运行。

## 功能特性

- 📚 **科目管理** - 自定义添加和管理科目
- ✏️ **作业记录** - 按科目记录和展示作业
- 🏷️ **标签系统** - 使用标签分类和筛选作业
- 🎨 **美观界面** - 使用 FluentAvalonia 和 HarmonyOS Sans 字体
- 💾 **本地存储** - 所有数据本地保存
- 🔄 **ClassIsland 联动** - 支持与 ClassIsland 集成
- 📜 **一言展示** - 可展示随机一言
- 🖥️ **全屏模式** - 支持全屏展示
- 🎯 **浮窗按钮** - 主窗口最小化时显示浮窗按钮
- 🔔 **托盘图标** - 系统托盘支持

## 技术栈

- **框架**: .NET 10.0
- **UI**: Avalonia 12.0.0 + FluentAvalonia 3.0.0
- **日志**: Serilog
- **图像处理**: SixLabors.ImageSharp
- **字体**: HarmonyOS Sans SC

## 平台支持

- ✅ Windows
- ✅ macOS
- ✅ Linux (X11 / Wayland)

## 快速开始

### 构建和运行

```bash
# 克隆项目
git clone <repository-url>
cd ImmersingHomework

# 恢复依赖
dotnet restore

# 构建项目
dotnet build

# 运行项目
dotnet run
```

## 项目结构

```
ImmersingHomework/
├── Abstractions/       # 平台服务抽象
├── Assets/             # 资源文件（字体、图片等）
├── Controls/           # 自定义控件
├── Models/             # 数据模型
├── Services/           # 业务服务
│   └── Platforms/      # 各平台实现
└── Views/              # 视图
    ├── SettingsPages/  # 设置页面
    └── WelcomePages/   # 欢迎向导页面
```

## 主要功能页面

- **欢迎向导** - 首次启动时的初始化配置
- **主窗口** - 作业展示和管理
- **设置窗口** - 基础设置、科目设置、标签设置、一言设置、联动设置
- **关于页面** - 项目信息和许可证

## 许可证

本项目采用 [GNU General Public License v3.0](LICENSE) 许可证。
