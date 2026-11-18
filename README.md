# CakePet Windows Dev

C# / .NET 10，原生 Win32 常驻宿主与独立 WPF 设置界面。素材包遵循共享格式，但 Windows 内置素材独立维护。

## 素材

`assets/default/` 是 Windows 自己的完整白酥素材包，包含 `modes.jsonc` 与全部原始 PNG。首次迁入来自 Android 提交 `8ccf31b3a4427e143d2ab106b85f7f9833482479`；之后 Windows 可独立修改，不自动同步 Android。

构建、测试及托盘图标均使用本仓库资源。托盘 PNG 从静坐第一帧生成，ICO 为构建产物，不重复提交。发布包的 `assets/windows-source.txt` 记录当前素材树 SHA。

## 开发

在仓库根目录运行：

```powershell
dotnet run --project tests/CakePet.Core.Tests -c Release -- assets/default
dotnet run --project src/CakePet.Windows -c Release -- --validate-assets assets/default
dotnet build src/CakePet.Host -c Release
dotnet build src/CakePet.Windows -c Release
```

GitHub Actions 仅依赖本仓库源码与素材，发布自包含 Windows x64 完整包。解压后运行根目录 CakePet.exe，无需额外安装 .NET。开发包名为 CakePet_winx64-v1.YY.MM.DD.hh.mm-gt（北京时间）。

## 使用

常驻托盘；左键打开设置，右键召唤、召回、归位、恢复点击或退出。角色右键支持投食、定格、调试与设置。关闭设置后独立 WPF 进程退出（未保存草稿除外）。

内存诊断入口：全局设置 → 系统与调试 → 调试信息 → 内存占用。仅在页面可见时采集，可复制或导出，不强制 GC，不上传数据。

## 性能测试

`tests/CakePet.NativeProbe` 为开发实验；正式宿主为 Native AOT。CI 使用同一份本地素材比较 JIT 与 Native AOT 的内存和调度频率。
