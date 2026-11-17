# CakePet Windows Dev

全新 C# / .NET 10 / WPF 实现，基于 Android 素材包 v1.12 与当前运行时语义。旧 Python 项目仅作为 Windows 平台行为参考。

## 开发

```powershell
dotnet run --project tests/CakePet.Core.Tests
dotnet run --project src/CakePet.Windows -- --package "素材包目录"
```

自动构建会从 Android 的固定提交复制白酥模板到发布包，解压后直接运行 CakePet.exe，不需要额外安装 .NET。开发环境也可通过 --package 指定包含 modes.jsonc 的目录。

当前为引擎验证版：素材包读取、动画链、功能上下文、碎碎念时间线、独立角色实例、四边基础物理、攀岩/飞跃原型和主显示器悬浮宿主。尚未完成投食、完整设置、多屏幕与跨 DPI 等适配；不包含素材包编辑器。
