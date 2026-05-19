# 购物优先级重构

## 现状

`JobGiver_CouponShopBuy.GetPriority()` 恒定返回 10，导致购物碾压一切（工作 9、休息 8），
用户反馈囚犯"日程不严格"。

但 10 > GetFood 的 9.5 是有意为之——囚犯应该优先用券买食物而非在地上捡吃。

## 目标

购物优先级感知饥饿程度，区分保命 / 进食 / 奢侈三个档次。

## 方案

```
Emergency（背包口粮 < 0.5 天）→ 9.7 — 保命，高于一切
Normal   （背包口粮 < 2.0 天）→ 8.5 — Work(9) > 购物(8.5) > 休息(8)
Luxury   （有钱买高档货）     → 4~6 — 进一步可选感知日程（下详）
None     （无购物需求）       → 0   — 不买
```

## 优先关系总览

| 场景 | 竞品 | 竞品优先级 | 我们 | 胜负 | 效果 |
|------|------|-----------|------|------|------|
| Emergency | GetFood | 9.5 | 9.7 | 赢 | 先买别捡吃 |
| Emergency | Work | 9.0 | 9.7 | 赢 | 保命优先 |
| Emergency | GetRest | 8.0 | 9.7 | 赢 | 保命优先 |
| Normal | GetFood | 0(不饿) | 8.5 | - | 无竞争 |
| Normal | Work | 9.0 | 8.5 | **输** | 先工作后购物 ✓ |
| Normal | GetRest | 8.0 | 8.5 | 赢 | 允许夜购 |
| Luxury | Work | 9.0 | 4~6 | **输** | 只闲暇购物 ✓ |

## Emergency 与 Normal 分界

- **背包口粮（天）= 背包营养总和 / 每日营养需求**
- 阈值：Emergency < 0.5 天，Normal < 2.0 天
- 计算只需遍历 `pawn.inventory.innerContainer`，O(背包物品数)，轻量

## Luxury 日程感知（可选追加）

如果 Luxury 进一步感知日程：

| 日程 | Luxury 优先级 | 效果 |
|------|-------------|------|
| Work | 0 | 专心工作 |
| Anything | 6 | 可以购物 |
| Joy | 5 | 放松时顺便 |
| Sleep | 0 | 不买 |

个人倾向：Luxury 日程感知可做可不做，Normal/Emergency 保命逻辑优先实现。

## 需要新增的方法

`PrisonerShoppingService` 加一个轻量公开方法：

```csharp
public static float GetInventoryFoodDays(Pawn pawn)
```

只读背包营养，不刷新缓存、不查 shop、不判断余额。给 `GetPriority()` 做预判用。

## 涉及改动的文件

- `Source/PrisonLabor/ThinkNodes/JobGiver_CouponShopBuy.cs` — 重写 `GetPriority()`
- `Source/PrisonLabor/PrisonerShoppingService.cs` — 加 `GetInventoryFoodDays()`
