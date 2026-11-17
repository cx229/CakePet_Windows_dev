# Core stages 1–2

Android reference: cx229/CakePet_Android_dev @ 8ccf31b3a4427e143d2ab106b85f7f9833482479.
- RolePackageContract.kt:57–129: runtime readiness; only a playable standby/pool chain is mandatory.
- ImageModeManager.kt:185–237, 408–479, 540–733: off/timed/infinite, nextMode, frame delay, displacement, logical canvas, anchor and transform.
- PetAnimationController.kt:58–115, 191–303: function context retained across mode transitions; sustained state resumes after a chain.
- PetView.kt:670–702, 967–1065: displacement and state selection.
- PetPhysics.kt:1–196: directional gravity, rebound, snap and landing.
- PackThinking.kt:1–100, TrayMsgManager.kt:184–310: effective policy and role timeline.
- 白酥/modes.jsonc: actual v1.12 template. CI pins and copies the exact assets.

Stage 1: independent parser, cursor, basic runtime routing and bottom-gravity physics prototype.
Stage 2: Android-aligned default frame geometry, separate thinking timeline and display-only cross-role coordinator, directional gravity/rebound and snap prototype, minimal WPF host with real assets, image decoder and launcher. The host deliberately uses the primary monitor's work area and fixed-size canvas; native multi-monitor/DPI coordinate handling, precise anchor boundary rules, climbing/leap, food, complete settings and tray integration remain subsequent stages. GUI interaction requires manual Windows testing. No material editor.
