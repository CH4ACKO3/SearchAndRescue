# CE Loadout 与 SAR 医疗补给互操作审计（2026-09-06）

## 结论

本轮已完成实机验证：本人医药的 CE 稳定、2→1 消费与补回 2 份全部通过，特殊器材完成库存选择与原生任务构造验证。详见 [实机报告](2026-09-06-ce-dms-runtime.zh-CN.md)。下文保留静态依据与诊断设计，逐项运行覆盖范围以实机报告为准。

CE loadout 中由救援者本人日常携带的药品、血袋和专用医疗设备可以被 SAR 使用。SAR 的资源账本优先扫描 `worker.inventory.innerContainer`，并把这类资源标记为 `FromInventory`；治疗任务会把该标记交给 More Injuries 的原生 dispatcher，CE `Stabilize` 也原生支持医生库存。资源被消耗后，CE 会按 loadout 的目标数量计算缺口并生成 `TakeCountToInventory`。CE 的卸载逻辑按 loadout 槽位扣除应保留数量，因此普通的 `UnloadEverything` 不会卸掉配额内物品。

静态审计发现一个确定的 P2 互操作问题，以及一个需要产品决定的共享行为：

1. **设计选择：SAR 会共享其他殖民者 loadout 配额内的战备物品。** `MedicalResourceLedger.AvailableInOtherPawnInventories` 把同阵营、可接近且未被当前工作使用的携带物视为紧急公共补给，之后 `TryTransferFromInventoryHolder` 可直接转移。这样能在救命时使用全队携带的血袋和设备，但会暂时打破供体的 CE 战备配额，由 CE 事后补货。是否保护供体配额需要明确产品取舍；本轮保留“生命优先、允许共享”的现状，不作为缺陷修复。证据位置：`MedicalLogistics.cs:1153-1174`、`:1184-1247`。

2. **P2（已修复）：最近的 SAR 保护补给会遮蔽更远的普通补货源。** 旧补丁在 CE 选中最近保护来源后把整个结果置空。现补丁只在 `JobGiver_UpdateLoadout.GetUpdateLoadoutJob` 的动态作用域内令 `ReservationUtility.CanReserve` 拒绝保护/认领物资，CE 原生 finder 会继续搜索下一个普通来源，同时保留其距离、容量、食物限制和携带者规则。实现位置：`DynamicCompatibilityPatches.cs` 的 `CombatExtendedLoadoutSearchContext`、`CombatExtendedLoadoutPickup_SearchAndRescueSupplyPatch` 和 `CombatExtendedLoadoutReservation_SearchAndRescueSupplyPatch`。

没有发现 CE `CompInventory` 缓存不同步：CE 对 `ThingOwner.NotifyAdded`、`NotifyRemoved`、`Take` 和合并操作均安装了刷新补丁。`HoldTracker` 用于玩家临时要求保留的物品；具体 loadout 槽位本身通过目标数量维持，不要求 SAR 在消耗时写入 HoldTracker。

## CE 1.6 API 调用签名

以下签名来自已安装的 `CombatExtended.dll`：

```csharp
// namespace CombatExtended
public Loadout(string label);
public LoadoutSlot(ThingDef def, int count = 1);
public void Loadout.AddSlot(LoadoutSlot slot);
public static void LoadoutManager.AddLoadout(Loadout loadout);
public static void LoadoutManager.RemoveLoadout(Loadout loadout);
public static Loadout LoadoutManager.DefaultLoadout { get; }
public static Loadout Utility_Loadouts.GetLoadout(Pawn pawn);
public static void Utility_Loadouts.SetLoadout(Pawn pawn, Loadout loadout);
public static Job JobGiver_UpdateLoadout.GetUpdateLoadoutJob(Pawn pawn);
public override Job JobGiver_UpdateLoadout.TryGiveJob(Pawn pawn);
public void CompInventory.UpdateInventory();
public bool Utility_HoldTracker.GetAnythingForDrop(Pawn pawn, out Thing thing, out int count);
```

SAR 不引用 CE 程序集，因此游戏内诊断应通过 Harmony `AccessTools`/反射调用，不要在诊断源码中直接写 CE 类型。

## 可放入 root 诊断的反射辅助代码

```csharp
private sealed class CeLoadoutApi
{
    internal readonly Type Loadout = AccessTools.TypeByName("CombatExtended.Loadout");
    internal readonly Type Slot = AccessTools.TypeByName("CombatExtended.LoadoutSlot");
    internal readonly Type Manager = AccessTools.TypeByName("CombatExtended.LoadoutManager");
    internal readonly Type Utility = AccessTools.TypeByName("CombatExtended.Utility_Loadouts");
    internal readonly Type UpdateGiver = AccessTools.TypeByName("CombatExtended.JobGiver_UpdateLoadout");
    internal readonly Type Inventory = AccessTools.TypeByName("CombatExtended.CompInventory");

    internal object CreateAndAssign(Pawn pawn, string label, params (ThingDef def, int count)[] items)
    {
        object loadout = Activator.CreateInstance(Loadout, new object[] { label });
        MethodInfo addSlot = AccessTools.Method(Loadout, "AddSlot", new[] { Slot });
        foreach (var item in items)
        {
            object slot = Activator.CreateInstance(Slot, new object[] { item.def, item.count });
            addSlot.Invoke(loadout, new[] { slot });
        }
        AccessTools.Method(Manager, "AddLoadout", new[] { Loadout }).Invoke(null, new[] { loadout });
        AccessTools.Method(Utility, "SetLoadout", new[] { typeof(Pawn), Loadout })
            .Invoke(null, new[] { (object)pawn, loadout });
        return loadout;
    }

    internal Job GetUpdateJob(Pawn pawn) =>
        (Job)AccessTools.Method(UpdateGiver, "GetUpdateLoadoutJob", new[] { typeof(Pawn) })
            .Invoke(null, new object[] { pawn });

    internal void Refresh(Pawn pawn)
    {
        ThingComp comp = pawn.AllComps.FirstOrDefault(Inventory.IsInstanceOfType);
        AccessTools.Method(Inventory, "UpdateInventory").Invoke(comp, Array.Empty<object>());
    }

    internal void Cleanup(Pawn pawn, object loadout)
    {
        object fallback = AccessTools.Property(Manager, "DefaultLoadout").GetValue(null);
        AccessTools.Method(Utility, "SetLoadout", new[] { typeof(Pawn), Loadout })
            .Invoke(null, new[] { (object)pawn, fallback });
        AccessTools.Method(Manager, "RemoveLoadout", new[] { Loadout })
            .Invoke(null, new[] { loadout });
    }
}
```

## 建议诊断断言

每种资源单独创建一个 worker/loadout，避免 CE 按槽位顺序选择缺口导致断言含糊。资源可使用：

```csharp
ThingDef medicine = ThingDefOf.MedicineIndustrial;
ThingDef blood = DefDatabase<ThingDef>.GetNamedSilentFail("BloodBag");
ThingDef device = DefDatabase<ThingDef>.GetNamedSilentFail("Defibrillator") ??
                  DefDatabase<ThingDef>.GetNamedSilentFail("SuctionDevice");
```

### 1. 本人携带的 loadout 资源被 SAR 优先使用

```csharp
object loadout = ce.CreateAndAssign(worker, "SAR CE carried resource", (def, desired));
Thing carried = ThingMaker.MakeThing(def);
carried.stackCount = desired;
worker.inventory.innerContainer.TryAdd(carried);
ce.Refresh(worker);

var ledger = new MedicalResourceLedger(map);
Thing selected = def.IsMedicine
    ? ledger.AvailableMedicines(worker, patient).FirstOrDefault(t => t == carried)
    : ledger.FindBest(worker, patient, def, reusable, 1);
Check(selected == carried, $"SAR selects carried CE loadout resource {def.defName}");
Check(worker.inventory.innerContainer.Contains(selected), "selection stays in worker inventory");
```

对真实治疗 fixture 再断言 `Compatibility.FindTreatmentOptions(...)` 中对应 intervention 的 `Resource == carried` 且 `FromInventory == true`。药品应覆盖 `CombatExtendedStabilize`，血袋覆盖 `Blood`，设备覆盖 `Defibrillate` 或 `Suction`。

### 2. 消耗后 CE 产生精确补货任务

地图上生成一个未禁用、未保留且在 20 格内的 `reserve`。loadout 中只放当前 `def`：

```csharp
Thing consumed = carried.stackCount == 1 ? carried : carried.SplitOff(1);
consumed.Destroy();
ce.Refresh(worker);

Job refill = ce.GetUpdateJob(worker);
Check(refill?.def == JobDefOf.TakeCountToInventory, "CE schedules loadout refill after SAR consumption");
Check(refill.targetA.Thing == reserve, "CE refill uses available map stock");
Check(refill.count == 1, "CE refill restores exact one-unit deficit");
```

实际运行完补货 Job 后再断言：

```csharp
Check(worker.inventory.innerContainer.TotalStackCountOfDef(def) == desired,
    "CE restores configured medical loadout count");
Check(ce.GetUpdateJob(worker) == null,
    "CE reports no remaining loadout deficit");
```

### 3. 卸载不会移除配额内医疗品

反射 `CombatExtended.Utility_HoldTracker.GetAnythingForDrop(Pawn,out Thing,out int)`：库存恰好等于 loadout 数量时应返回 `false`；再增加一单位同 ThingDef 时应返回 `true`、输出该 ThingDef 且 `dropCount == 1`。

### 4. 记录当前共享语义：可抽取其他殖民者的 loadout 配额

给 donor 配置一单位 `BloodBag`/`Defibrillator`，库存也只有一单位；worker 自己与地图均无该资源：

```csharp
Thing selected = new MedicalResourceLedger(map)
    .FindBest(worker, patient, def, reusable, 1);
Check(selected != null,
    "SAR may share another pawn's CE loadout reserve in a medical emergency");
```

当前实现会返回 donor 的 Thing。若继续调用 `TryTransferFromInventoryHolder`，还可断言 donor 库存变为 0，从而证明影响不是单纯的候选排序。

### 5. 保护补给不能遮蔽普通补货源

让 worker 的单项 loadout 缺一单位；在近处放置并通过 `coordinator.NotifyFieldSupplyDelivered(...)` 注册一个患者保护栈，在较远但 80 格内放置普通 `reserve`：

```csharp
Check(protectedSupply.Position.DistanceToSquared(worker.Position) <
      reserve.Position.DistanceToSquared(worker.Position),
    "fixture places protected supply closer than ordinary stock");
Job refill = ce.GetUpdateJob(worker);
Check(refill?.def == JobDefOf.TakeCountToInventory && refill.targetA.Thing == reserve,
    "CE skips protected SAR supply and refills from ordinary stock");
```

修复补丁挂在 `GetUpdateLoadoutJob`，因此直接调用该方法会走与 CE 正常 `TryGiveJob` 相同的保护来源过滤。
