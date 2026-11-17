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

Stage 3: PetEngine owns animation/physics coordination, drag state routing, frame motion in surface coordinates, climbing contact and corner transitions, automatic and explicit leap selection, independent flight target, and landing interruption. Host forwards input and renders snapshots. This is still a fixed-primary-work-area prototype; food/forage, precise Android anchor-aware boundaries, automatic behavior tuning, final settings, and multi-monitor hit testing are pending. Android references: PetView.kt:206–251, 670–702, 810–894, 1552–1668, 1763–1811, 2020–2140.

Stage 4: FoodWorld owns independent food cursors, ownership, expiry, automatic spawn, manual drop and lure/forage sessions. PerimeterRoute chooses shortest boundary travel; PetEngine applies frame motion only. WPF food images use one alpha-union crop per mode chain, shared cache, and a central UI tick. Android reference: PetService.kt:290–342, 737–807, 1343–1363, 1520–1660, 1667–1764, 1856–1896; ImageModeManager.kt:268–329; AppDefaults.kt:538–557. The current desktop UI uses fixed prototype controls and one pet instance per role. Size conversion and precise anchor/boundary matching are still approximate; auto spawn is opt-in for this test build. No GUI interaction verified on a real desktop yet.

Stage 4 follow-up: food placement uses actual cropped width/height, Android 1.4 logical scale and a shared alpha-union crop; resize preserves existing velocity and support-side position semantics. Mouse drag threshold and capture-loss cancellation separate clicks from feeding, and forage contact is edge-triggered per food/pet pair. The Windows implementation currently handles one mouse pointer; core held-food state supports multiple independent food instances.

Stage 6a: desktop coordinates use Win32 physical pixels, native monitor work/full rectangles, and SetWindowPos; WPF drawing converts only the local canvas to DIPs. Dragging selects the monitor under the pointer; autonomous motion remains on the selected monitor, avoiding gaps between displays. Ctrl temporarily overrides click-through; launcher can restore all input. Monitor topology is refreshed every 500 ms. Physical-size policy is intentional for this prototype; no actual mixed-DPI desktop interaction verified yet.

Stage 6b: shared frame geometry feeds the physical anchor range and inverse alpha hit mapping. Support-side anchor permits overhang, other edges constrain the logical canvas; food range remains the actual activity rectangle. Pixel alpha mask is cached per decoded bitmap; HWND click-through follows cursor hit, Ctrl overrides both modes. This polling approach is prototype-level (one UI tick latency), and support corners/snap behavior still need actual desktop tests.

Stage 7: timestamped UTC+8 artifacts and assembly informational versions; independent static frame snapshots owned by pet windows, draggable live debug panels, notification-area lifecycle (hide launcher on close, explicit exit disposes all overlays). Tray uses a temporary Windows application icon. No persistent role/settings store yet.

Stage 8: local catalog separates imported immutable package copies, persistent roles and per-role settings from transient pet windows. First install imports the bundled pack once. Role cards summon/reconcile independent instances; settings update running windows without restarting all roles. Data stored under LocalApplicationData/CakePet. This first management UI covers directory import and basic switches, not ZIP/backup or full threshold settings. Food schedules use the first instance and shared role caps; per-instance context menu remains a prototype.

Stage 9: expanded persistent physics, snap, leap, food and boundary settings; tabbed role settings; right-click commands update catalog and all role instances; default preferences affect new roles unless explicitly applied to all. Numeric limits are provisional Windows ranges; configurable per-setting thresholds and per-item apply are pending. Existing food opacity updates live; size and lifetime apply to new food.

Stage 10 UI: shared WPF resource dictionary, slider/value rows, inline role/default settings navigation, wrap-layout role cards with real package previews. Changes apply immediately and persistence is debounced. Material editor and threshold work remain deferred. Native file pickers and confirmation dialogs are retained.

Stage 10 follow-up: grouped setting cards and expandable gravity/bounce sections, inline package selection, tray-first explicit shutdown and single-instance activation, original 白酥 sit_clam-1.png copied byte-for-byte by CI to assets/tray.png. The tray icon is cropped/scaled only in memory; no generated art. Native confirmation/file dialogs retained.

Stage 11 UI: explicit SettingCard ownership, main/sub remarks, independent expand and enable, inline numeric/direction rows, shared-track two-handle food interval, per-field patches against latest catalog state, reorganized role pages and card headers. No material editor or threshold changes.

Stage 12a: bounded safe ZIP extraction, root/nested package import, package export, Windows backup selective overlay, reset to bundled default, role settings JSON import/export. Android backup schema is recognized but not yet imported; package ZIP format is shared. Existing roles not included in a backup are retained. Physical package folders use fresh UUIDs for transactional replacement.

Stage 12b: initial inline package editor with metadata, function pools, animation properties/frames, image import/deletion guards and thinking text edits. JSON node edits retain unknown fields, no whole-package draft copy. Save validates mandatory standby and reloads affected running roles. Group ordering, animation preview, thinking bindings, cross-package resources and fine-grained delete impact dialogs are pending. JSONC comments are not preserved on serialization.

Stage 13a: Windows screen connection option (role settings/activity range), real adjacent work-area crossings and horizontal outer wrap, anchor-based seams, physics substep/frame motion transition preserving velocity; climbing crosses only same-height support surfaces, gaps remain closed, manual dragging unchanged. Legacy Python references: ScreenMonitor.py:153–190,240–263; speed_util.py:80–147. No portal effects yet.

Stage 13b: EXE application icon and main/tray icon from the original 白酥 static first frame, CI generates seven ICO sizes. Global ThinkingLocation persisted and restored by Windows backup; primary bottom, tray left, taskbar left. One click-through presenter combines visible role texts; fallback to primary work area when taskbar unavailable. Legacy TrayMsgController.py:111–146. Native notification icons are not modified. Real Windows shell/DPI verification pending.

Stage 14: opt-in anonymous keyboard count via raw-input make events; only aggregate date/count persisted and temporary rolling timestamps in memory. General IThinkingSource arbitration, role-ID grouping, typing expiry and refresh, per-source ThinkingStyle, role rest interval, shared appearance controls and one multi-element presenter. Actual raw-input and Windows shell rendering require manual validation; no input content is stored or transmitted.

Stage 15: global None/Latest/Overlap thinking policy, strict latest across all instances, no revival of interrupted messages, common anchored grid with z-order (no stacking), global disable preserves individual settings and stops typing input. Old SingleThinking JSON accepted on import/load. Manual desktop validation pending.

Stage 16a: restore Android role-setting taxonomy, friendly role-specific remarks, move maximum speed/screen connection/climb/forage/leap into movement, independent snap page, fixed headings, state summaries, fold dependencies and forced-on movement switch, exclusive gravity directions. Dark blue-gray shared theme and compact controls. No new random-mode runtime or mobile-only sensor settings.

Stage 16b: compact dark home and pack management, grouped global settings, action menus, shared dark dropdown popup and menu colors, editor Chinese function labels and playback labels, top save action. Full multi-column editor and theme picker deferred.

Stage 16c: fix local function syntax, hide empty movement section headings when parent disabled, preserve warning headings, and keep searchable animation list beside animation detail. Existing frame editor and save behavior retained; resource groups and full animation playback still pending.

Stage 16 follow-up: role settings navigation summaries and true static preview, metadata-driven role short name, Android feeding group ordering, compact and narrow-window numeric rows, explicit startup setting row. Expiration uses existing zero=never semantics until a separate stored enable flag is implemented.

Stage 16 editor follow-up: function groups in Android order and split function tree / searchable animation selection, true image thumbnails, dedicated resource checkbox, selected navigation highlight. Does not add animation-group editing or full playback preview.

Stage 17a: Android-compatible modeGroups/thinkingGroups and per-item groups arrays, empty groups and ungrouped fallback, group dissolve and ordering, split thinking editor, draft animation playback using the existing cursor without copying or saving the package. Preview updates on replay; no GUI validation yet.

Stage 17b: function and animation thinking policy/pool editing with per-text min/max milliseconds, text reference list and cancellation, range slider unit correction. No cross-package resource copy yet.

Stage 18: resource folder tree, actual folder creation/order, image reference list and replacement, cross-package image/text/animation import with chain and referenced text copy, collision-safe names and image byte deduplication. Imports are explicitly chosen from other packages and copied locally. Images imported immediately; config requires save. Partial failed imports may leave unreferenced images. Folder rename/dissolve and picker integration pending.

Stage 19a: per-role random settings and schedule, Android-compatible semantics (user enabling triggers immediate persistent random values, existing enabled roles wait after summon, change period restarts timer). Desktop physics random ranges adapted; no tilt gravity. Random timer is per role, not per instance. UI uses Android group order. Existing role settings UI does not yet refresh all controls after periodic random changes.

Stage 19b: user habits explicit group/all application to existing roles, new roles inherit defaults/limits; numeric threshold range editor and clamping at catalog updates. Supports scalar settings initially; range controls and style threshold settings pending, threshold UI changes apply on reopening settings. No changes to existing roles unless confirmed.

Stage 19c: fix CS0136 threshold variable collision; user habits standalone Windows JSON import/export/reset, scalar item application, rebuild settings workspace in the same section after threshold change. Existing roles unaffected by import/reset. Non-scalar item application remains pending.

Stage 19d: nested thinking appearance thresholds (size, opacity, offset, background opacity), individual style value application, clamp imported/catalog-loaded values, refresh open settings after random changes when keyboard focus is outside the page; immediate manual enable refresh. Paired interval thresholds and full nonnumeric item defaults pending.

Stage 20: restore editor interaction separation: lightweight bound-resource summaries, grouped selection dialogs, independent frame editor with previous/next and paired XY, frame drag reorder, persistent manual folder expansion, grouped counts, consistent icons and indentation. RangeSlider track and endpoints share a row; narrow settings move the whole control together. Role settings child rows inset, forage/leap nested under climbing. Native WPF dialog chrome retained; cross-package integration in shared picker and exact per-frame card-only refresh pending.

Stage 21: menu/pool is a user-selected named action list, not a random action. Pet context submenu uses valid candidate names in package order; exact mode trigger shares function context, physical gates and snap behavior, keeps animation loop and nextMode semantics. menu/preview and menu/summoned remain separate homepage functions.
