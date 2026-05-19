# ThingDef → WorkType 反向索引（兼容模式）

## 问题

`Patch_WorkTickTracker` 通过 `pawn.CurJob.workGiverDef.workType` 确定工作类型并计算工资。当其他 Mod 通过非标准方式（如 Component 直接调 `JobMaker.MakeJob`）分发工作时，`workGiverDef` 为 null，现有 `JobToWorkTypeMapper` 只能做 JobDef→WorkType 的 1:1 映射，无法处理 `DoBill` 等被多个 WorkType 共用的 JobDef。

## 方案

利用每个 `WorkGiver_Scanner` 自带的 `PotentialWorkThingRequest` 属性，启动时构建 **ThingDef → WorkType[]** 反向索引。

### 启动时（StaticConstructorOnStartup）

```
遍历所有 WorkGiverDef:
  获取 workerClass 实例
  调用 PotentialWorkThingRequest
  展开 ThingRequest:
    singleDef  → defName → WorkType 列表追加此 workType
    group      → 展开 ThingRequestGroup 内所有 ThingDef → 同上
  写入 Dictionary<string, WorkTypeDef[]> （key = defName）
```

### 运行时（Patch_WorkTickTracker 兜底）

```
workGiverDef 为 null 时:
  targetThing = job.targetA.Thing
  if targetThing == null → 回退到 JobToWorkTypeMapper
  
  candidates = lookup[targetThing.def.defName]
  if candidates == null → 回退到 JobToWorkTypeMapper
  
  按 pawn.workSettings 当前优先级排序 candidates，取最高者
  用该 WorkType 的工资标准调用 Notify_WorkTick
```

### 为什么能解决 DoBill 多义性

- `DoBill` target 是工作台（ElectricStove/FueledStove/Smithy/ElectricSmithy/...）
- 炉子 → Cooking，铁砧 → Smithing，裁缝台 → Tailoring
- ThingDef 层面即可区分，无需解析 Bill/Recipe

## 局限

- ThingRequestGroup 展开可能映射到多个 WorkType（如 `Production` 组含 Crafting/Smithing/Tailoring）。此时按 pawn 优先级排序取最高，大概率正确
- `PotentialWorkThingRequest` 是实例方法，需要实例化 WorkGiver（反射 `Activator.CreateInstance`），有一定启动开销，但仅一次
- 无法覆盖 WorkGiver 在运行时通过代码逻辑动态决定的工作目标（极端罕见）

## 启用条件

作为**可选兼容模式**，通过 Mod 设置开关控制。默认关闭，用户遇到兼容问题手动开启。

理由：
- 正常运行中所有 Job 都有 workGiverDef，不需要此功能
- 反向索引有一定启动开销和误判风险
- 仅在有 Mod 接管工作分发时才需要

## 实现位置

- 新建 `Source/Compat/WorkTypeReverseIndex.cs` — 启动时构建索引
- 修改 `Source/Patches/Patch_WorkTickTracker.cs` — 兜底路径加入反查逻辑
- 修改 `Source/RimPrisonSettings.cs` — 加入 `CompatibilityMode` 开关
