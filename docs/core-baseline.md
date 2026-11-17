# Core stage 1

Android reference: cx229/CakePet_Android_dev @ 8ccf31b3a4427e143d2ab106b85f7f9833482479.
- RolePackageContract.kt: runtime readiness; only a playable standby/pool chain is mandatory.
- ImageModeManager.kt: off/timed/infinite, nextMode, frame delay, displacement, anchor and transform.
- PetAnimationController.kt: function context retained across mode transitions; sustained state resumes after a chain.
- 白酥/modes.jsonc: actual v1.12 template.

Stage 1: independent parser, cursor, basic runtime routing and bottom-gravity physics prototype. WPF host, actual image decoding, frame geometry, complete physical state rules, thinking scheduler, settings and multi-monitor integration remain subsequent stages. Not yet Android feature parity. No material editor.
