# CakePet Windows Dev

全新 C# / .NET 10 / WPF 实现，共享 Android 素材包 v1.12 语义。核心引擎与平台宿主分离。

## 开发

```powershell
dotnet run --project tests/CakePet.Core.Tests
dotnet run --project src/CakePet.Windows -- --package "素材包目录"
```

自动构建从 Android 固定提交复制白酥模板，只发布自包含 Windows x64 完整包，解压运行 CakePet.exe，无需额外安装 .NET。开发包名称为 CakePet_winx64-v1.YY.MM.DD.hh.mm-gt（北京时间）。

## 使用

启动后常驻托盘；托盘左键打开设置，右键可以召唤、召回、归位、恢复点击、调整系统托盘设置或退出。关闭设置窗口会释放其窗口与界面；未保存的素材包草稿会提示。角色右键可投食、定格、调试、调整设置；支持多实例、多屏幕与素材包编辑。

## 内存诊断

托盘右键「内存诊断」记录当前工作集、专用提交内存、托管内存估算、GC 提交内存和图片缓存，并保存 JSON 到本地数据目录 diagnostics。建议分别在启动后、打开设置后、关闭设置后、召回全部后记录，比较相同场景的工作集和专用提交内存。诊断不会强制 GC 或清空工作集，不会上传数据。当前角色渲染仍使用 WPF；托盘使用原生 Win32，设置窗口和主题按需加载。
