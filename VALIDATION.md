# 验证记录

验证日期：2026-10-09（0.2.0）。环境：Windows，Unity **2022.3.22f1**，Built-in Render Pipeline，FBX Exporter **4.2.1**，Autodesk FBX SDK for Unity **4.2.1**，Timeline **1.7.6**。

## PMX 核心读取器

219 项断言通过。测试输入完全合成，覆盖：

- UTF-8 / UTF-16 文本。
- 1 / 2 / 4 字节索引。
- BDEF1 / BDEF2 / BDEF4 / SDEF / QDEF，共 30 种组合。
- 额外 UV、两个材质分组、顶点表情和带权重的组表情。
- 文件截断、非法签名、非法索引宽度、骨骼循环和组表情循环的拒绝处理。

## Unity 实际导出与重新导入

使用独立测试项目运行 `UnityValidation.Run`，通过以下检查：

- 普通 Generic FBX 导出、重新导入与 Prefab 保存。
- VRChat 模式导出后重新创建有效 Humanoid Avatar，Prefab 的 Animator 正确引用该 Avatar。
- 主纹理复制、Unity 材质引用与 FBX 材质重映射。
- 两个顶点 / 组 BlendShape 通道保留，设置表情权重后网格实际变形。
- 左臂 T Pose、1.65 米尺寸和 Mesh Read/Write 设置。
- 烘焙蒙皮后的所有顶点与重新导入的静止网格匹配；旋转手臂后网格实际跟随。
- 连续转换生成独立目录，先前输出保持存在。
- 缺失 Chest 的模型自动补建并通过 Humanoid 验证。
- 缺少必要人形骨骼的模型返回失败和明确错误。
- BDEF2 / BDEF4 / SDEF / QDEF 的实际 FBX 导出与重新导入。
- 两个 PC Toon Shader 的 Unity Shader 错误检查。

## XPS 附属功能

- 核心测试合计 471 项断言：原有 PMX 219 项和新增 XPS 252 项。
- 合成输入覆盖旧版无头部 `.mesh`、XPS 2.12 / 2.15 / 3.15、ASCII；多字节长名称、多网格、多层 UV、纹理 UV 选择、顶点颜色、权重合并 / 截取 / 归一化、无骨骼布局。
- 拒绝截断文件、循环父骨骼、越界三角面与越界蒙皮索引。另有独立手写 ASCII 样本验证无纹理 / 无 UV 网格。
- Unity 实际导出 / 重新导入上述 5 种布局的带骨骼与无骨骼模型，检查材质组、贴图重映射、RGBA 顶点颜色和多层 UV。
- XNALara 骨骼别名生成有效 Humanoid；T Pose 后绑定矩阵匹配，旋转手臂后导入的网格实际变形。
- XPS 特殊材质组、头部姿态、额外纹理自动配置、真实模型视觉效果不在当前测试范围内。

## Unitypackage 安装版本

- `Assets/PMX2FBX` 安装布局已通过与 UPM 版本相同的实际 FBX 转换检查。
- FBX 导出接口改为按需调用；插件不再需要在首次编译前安装 FBX Exporter。
- 转换窗口提供官方 FBX Exporter 的安装按钮，现有可用依赖会直接复用。

## 当前验证边界

这些测试使用合成模型，不代表所有 PMX / XPS 文件都能保持原来的外观。本轮没有使用实际的第三方模型进行视觉验收，也没有运行 VRChat SDK 上传构建。测试项目未安装 Avatars SDK，实际 SDK 环境中的 Descriptor 自动配置分支尚未执行验证。

交付前应在自己的模型上确认骨骼、材质透明排序、SDEF / QDEF 近似变形、视点与口型，并以 VRChat SDK Builder 检查结果为准。测试数据不包含第三方模型或纹理。
