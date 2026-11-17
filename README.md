# CakePet Windows Dev

全新 C# / .NET 10 / WPF 实现，基于 Android 素材包 v1.12 与当前运行时语义。旧 Python 项目仅作为 Windows 平台行为参考。

## 开发

```powershell
dotnet run --project tests/CakePet.Core.Tests
dotnet run --project src/CakePet.Windows -- --package "素材包目录"
```

自动构建会从 Android 的固定提交复制白酥模板到发布包，解压后直接运行 CakePet.exe，不需要额外安装 .NET。开发环境也可通过 --package 指定包含 modes.jsonc 的目录。

当前为引擎验证版：素材包读取、动画链、功能上下文、碎碎念时间线、独立角色实例、四边基础物理、攀岩/飞跃原型和主显示器悬浮宿主。已加入投食、手持吸引、吸引落空、自动觅食、跨角色投食变大与自动刷新食物。尚未完成完整设置、多屏幕与跨 DPI 等适配；不包含素材包编辑器。

右键角色可投食、开关自动刷新、自动觅食与投喂变大；左键拖动食物到宠物上松手。当前使用主显示器工作区域，鼠标为主要输入方式。

开发包命名：CakePet_winx64-v1.YY.MM.DD.hh.mm-gt（北京时间，Actions 自动生成）。关闭主窗口隐藏到托盘，托盘菜单退出软件。角色右键可定格当前画面、清除定格和打开实时调试窗口。
