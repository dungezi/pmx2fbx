# PMX / XPS → FBX / VRChat PC Unity 插件

Unity 编辑器插件：选择 PMX，快速生成带骨骼、蒙皮、材质、贴图、BlendShape 的 FBX；附属功能支持 XPS / XNALara 模型转换。勾选 **VRChat PC 适配** 后整理人形骨架、调整尺寸、转换手臂 T Pose，验证重新导入的 Humanoid，并生成头像 Prefab。

适用：**Unity 2022.3 / Built-in Render Pipeline / VRChat PC 人形头像**。VRChat 当前推荐的编辑器版本为 [2022.3.22f1](https://creators.vrchat.com/sdk/upgrade/current-unity-version/)。不需要 Blender 或外部转换程序。

## 安装

### Unitypackage 版本

1. 在 Unity 2022.3 项目中双击 `PMX2FBX-0.2.0.unitypackage`，或选择 **Assets → Import Package → Custom Package**。
2. 导入全部文件，插件位于 `Assets/PMX2FBX`。
3. 打开 **Tools → PMX to FBX → 转换 PMX 模型**。若项目没有 FBX Exporter，点击窗口中的 **安装 FBX Exporter**，等待 Package Manager 下载和编译完成。
4. 按下方流程转换模型。已有可用的 FBX Exporter 时会直接使用它。

此版本无需预先安装 FBX Exporter 即可编译和打开窗口；实际转换需要该依赖。官方依赖及 VRChat SDK 没有打包进 unitypackage。若已安装本插件的 UPM 版本，请先在 Package Manager 移除它；两种安装方式不要同时使用。

### Package Manager 版本

1. 在 Unity 打开 **Window → Package Manager**。
2. 点击 **+ → Add package from tarball**，选择发行文件 `com.pmx2fbx.converter-0.2.0.tgz`。Unity 会安装官方 FBX Exporter 4.2.1 及其依赖。
3. 若直接使用源码，选择 **Add package from disk**，打开本目录的 `package.json`。
4. 为生成带 Avatar Descriptor 的头像，先通过 VRChat Creator Companion 创建 **Avatars** 项目并安装 Avatars SDK。SDK 是可选依赖：没有 SDK 仍可转换模型。

需要访问 Unity 包仓库下载依赖；已有 FBX Exporter 的项目可由 Package Manager 解析兼容版本。

## 使用

1. 打开 **Tools → PMX to FBX → 转换 PMX 模型**。
2. 浏览选择 `.pmx` 文件，或将其拖入窗口。PMX 可以在 Unity 项目外；纹理按 PMX 内的相对路径查找，保持原文件夹结构。
3. 指定输出目录，默认 `Assets/PMXConverted`。
4. 普通模型取消 **VRChat PC 适配**；PC 头像保留勾选，按需要设置目标身高。
5. 点击 **读取模型 / 查看骨骼映射**。常见日文 / 英文骨骼自动匹配，非标准命名可手动指定。
6. 点击 **转换并导出**。
7. 将生成的 `_VRChat.prefab` 拖到场景，确认视点、材质、关节与口型，然后在 **VRChat SDK → Builder** 执行检查和构建。

每次转换使用新的独立目录，不覆盖源模型或已有转换。输出包括：

| 文件 | 用途 |
| --- | --- |
| `模型名.fbx` | 网格、蒙皮、骨架、BlendShape；Unity 导入器保存 Humanoid 配置 |
| `模型名_VRChat.prefab` | Animator、材质引用、可选 Avatar Descriptor |
| `Materials/*.mat` | 基础 PC Toon 材质 |
| `Textures/*` | 复制的原纹理，FBX 以路径引用 |
| `conversion-report.json` | 成功状态、错误、未支持项、模型统计、原表情名称对应关系 |

移动到其他 Unity 项目时，应保留整个输出文件夹和插件 Shader，不能只复制 FBX。Humanoid 映射保存在 FBX 的 Unity `.meta` 中；Avatar Descriptor 是 Unity / SDK 组件，保存在 Prefab 中。

## XPS 附属功能

打开 **Tools → PMX to FBX → 转换 XPS 模型（附属功能）**，选择 `.xps`、旧版二进制 `.mesh` 或 `.mesh.ascii` / `.ascii`。也可在转换窗口切换源模型格式，或拖入文件。默认输出到 `Assets/XPSConverted`，普通转换采用 1 个模型单位 = 1 米，可自行调整比例。

XPS 使用相同的 FBX 导出、贴图复制、材质重映射、独立输出目录和转换报告流程。需要 PC 头像时勾选 **VRChat PC 适配**：常见 XNALara `root hips`、`spine lower`、`arm left shoulder` 等骨骼名称会自动匹配，其他命名可以手动映射。缺少人形骨骼的静态道具使用普通转换；不会自动生成完整人体骨架。

- 支持无头部旧版 `.mesh`、带头部 XPS 2.x / 3.x、UTF-8 ASCII；旧格式切线读取后由 Unity 重新计算。
- 保留网格分组、法线、RGBA 顶点颜色、最多 8 层 UV、骨架与蒙皮。多网格合并成一个带多个材质槽的网格。
- 每个分组第一张纹理作为主贴图；其指定的 UV 层映射到 Unity UV0，其他层保留在后续 UV 通道。无纹理、无 UV 的网格也可转换。
- 其他纹理一并复制，但法线 / 高光 / 光照贴图、特殊 XPS 渲染组和头部保存的姿态不自动重建。DDS 等不支持的纹理需先转为 PNG；报告注明缺失与未支持项。纹理原路径不可用时尝试模型旁的同名文件。
- 超过 4 个骨骼影响时合并重复影响，保留最高 4 项并归一化，在报告中提示。未绑定的网格通过虚拟骨骼导出。
- 保持 XPS 的 Y 轴朝上和原坐标，翻转 UV 的 V；不同来源模型的朝向可用 **额外旋转 180°** 修正。
- XPS 文件不携带 PMX 的表情数据，本功能不生成 BlendShape 或完整口型。原有 PMX 功能保持可用。

升级 0.1.0 的 Assets 安装可直接导入 0.2.0 包，保留原有文件 GUID。

## VRChat PC 选项做什么

- 将 PMX 朝向转换为 Unity 的 +Z 朝向，主 UV 的 V 坐标翻转；额外 UV 数据保留。
- 自动匹配躯干、手脚、肩颈、眼睛与手指，可手动覆盖映射。
- 修复常见 MMD 上下半身分离：Hips 成为所有映射骨骼的祖先。
- 缺失 Chest、Neck、Shoulder 时可根据已有关节补建；其他必要骨骼缺失时报错。
- 整理四肢链顺序；保留未映射的辅助骨骼。
- 可归一化身高与地面位置；根节点保持单位缩放。
- 可调整双臂为 T Pose，并同时烘焙网格、法线和表情位移，再更新绑定矩阵。
- FBX 重新导入后验证 Humanoid、三角面数、材质槽、蒙皮与表情通道。验证失败不会报告转换成功。
- 保持 Mesh Read/Write 开启，保留骨骼节点，禁用导入动画和骨架优化。
- 检测到 Avatars SDK 时添加 Avatar Descriptor，估计视点；存在 `あ` / `a` / `aa` / `mouthopen` 表情时配置基础张嘴口型。
- 提示三角面、材质槽与骨骼超过 PC Good 参考值的情况，**不自动减面，也不声明完整性能评级**。

这是转换与准备工具。有效 Humanoid 不等于任何源模型都能自动达到上传要求。最终以实际项目的 SDK Builder 校验为准；补建骨骼的位置、视点和材质效果需要检查。完整音素口型、眨眼 / Eye Look、表情菜单、PhysBones 和性能优化需要按模型另行设置。暂不支持 Quest / Android。

## 支持范围

| 项目 | 状态 |
| --- | --- |
| PMX 2.0 / 2.1，UTF-8 / UTF-16，1 / 2 / 4 字节索引 | 支持 |
| 网格、法线、主 UV、最多 4 个额外 UV、材质分组 | 支持 |
| BDEF1 / BDEF2 / BDEF4 | 支持，合并重复骨骼权重并归一化 |
| SDEF / QDEF | 读取后近似为线性蒙皮，变形可能与 MMD 不同 |
| 骨架与辅助骨骼 | 支持；转换后的稳定 ASCII 名称避免 FBX 重命名 |
| 顶点表情与嵌套组表情 | 转为 BlendShape；循环引用报错 |
| 骨骼 / UV / 材质 / Flip / 冲量表情 | 不转换；包含它们的组表情整体跳过并报告 |
| PNG / JPG / TGA / BMP / PSD / TIFF | 由 Unity 导入；缺失或不支持格式报告提示 |
| PMX Toon / Sphere / 描边 | 基础颜色与主纹理保留；原效果需手动重建 |
| MMD IK、追加变换、刚体、关节、软体 | 不转换；VRChat 需使用其 IK / PhysBones |
| VMD 动作、PMD 文件、URP / HDRP、非人形 VRChat 适配 | 暂不支持 |

表情通道使用 `Morph_00000` 等稳定名称，报告记录它们对应的 PMX 名称，避免日文或重复名称在 FBX 导入中损坏。

## 验证与开发

`Tests~/CoreTests.cs` 使用完全合成的 PMX 数据测试读取器。`Tests~/UnityValidation.cs` 验证实际 FBX 导出 / 重新导入、Humanoid、骨骼绑定、材质、表情、尺寸、重复导出、骨骼修复与失败处理。`Tests~/UnityPackageValidation.cs` 负责原生 unitypackage 导出与无依赖导入验证。可参考 `Tools~/PrepareValidation.ps1` 和 `Tools~/PrepareUnityPackage.ps1` 在独立目录准备验证项目；其缓存路径与 Unity 路径需要按本机调整。

公开转换入口为 `PmxConverter.Convert(sourcePath, outputFolder, options)`（兼容原 API，支持两种格式），以及 `XpsConverter.Convert(sourcePath, outputFolder, options)`，返回 `ConversionReport`。XPS 入口省略 options 时采用普通转换 / 比例 1；传入 options 时按参数执行。输出目录必须位于当前项目 `Assets` 内。应检查 `ConversionSucceeded` 和 `Errors`；不要仅凭 FBX 文件存在判断成功。

## 参考与许可

本插件的转换器和读取器为独立实现；未复制或打包第三方 PMX / XPS 导入器代码。开发时查阅了：

- [saba PMXFile.cpp](https://github.com/benikabocha/saba/blob/master/src/Saba/Model/MMD/PMXFile.cpp)：PMX 数据布局参考，MIT。
- [Unity MMD Tools](https://github.com/CandidumGames/UnityMMDTools)：PMX 朝向、UV、Humanoid 构建思路参考，MIT；未引入其物理或 Burst 运行库。
- [XNALaraMesh](https://github.com/johnzero7/XNALaraMesh)：XPS 二进制 / ASCII 字段布局、字符串长度编码、UV 与权重布局参考；未包含其 Blender 脚本。
- [Unity 官方 FBX Exporter](https://github.com/Unity-Technologies/com.unity.formats.fbx)：作为 Package Manager 依赖，按 Unity Companion License 与 Autodesk SDK 的许可提供。
- [VRChat Rig Requirements](https://creators.vrchat.com/avatars/rig-requirements/)：骨架要求；该页面标注部分内容过时，SDK3 不采用 SDK2 的 UpperChest / 手指限制。
- [VRChat Performance Ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)：PC 参考性能阈值。

本仓库代码使用 MIT 许可，见 `LICENSE`。模型与纹理的许可不由本插件改变。
