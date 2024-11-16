using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using IAmFuture.Data.Enemies;
using IAmFuture.Gameplay.Buildings;
using IAmFuture.Gameplay.Electricity;
using IAmFuture.Gameplay.Enemies.Nests;
using IAmFuture.Gameplay.HealthSystems;
using IAmFuture.Gameplay.TimeSystems;

namespace BetterNoHarmCultivator;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("I Am Future.exe")]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<bool> _requireElectricity;

    private void Awake()
    {
        _requireElectricity = Config.Bind("Electricity",
            "requireElectricity",
            true,
            "If cultivator needs electricity to work");
        
        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll(typeof(BlockDamage));
        if (!_requireElectricity.Value)
        {
            harmony.PatchAll(typeof(NoElectricityRequired));
        }

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    [HarmonyPatch(typeof(EnemyNestCultivatorBuilding))]
    internal class NoElectricityRequired
    {
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(EnemyNestCultivatorBuilding), "UpdateElectricityConsumption")]
        private static IEnumerable<CodeInstruction> ClearUpdateElectricityConsumptionTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return new List<CodeInstruction>
            {
                new(OpCodes.Ret)
            };
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(EnemyNestCultivatorBuilding), "UpdateEnemyNestBlocking")]
        private static IEnumerable<CodeInstruction> EditUpdateEnemyNestBlockingTranspiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldfld) continue;
                var fieldInfo = codes[i].operand as FieldInfo;
                if (fieldInfo == null || fieldInfo.FieldType != typeof(ElectricityConsumer)) continue;
                codes.Insert(i - 1, new CodeInstruction(OpCodes.Ldc_I4_1));
                while (i < codes.Count && codes[i].opcode != OpCodes.Stloc_0)
                {
                    codes.RemoveAt(i);
                }

                break;
            }

            return codes;
        }
    }

    [HarmonyPatch(typeof(EnemyNestCultivatorBuilding))]
    internal class BlockDamage
    {
        [HarmonyPatch(typeof(EnemyNestCultivatorBuilding), "ResolveEnemySpawnSuccessfullyBlocked")]
        private static void Prefix(ref EnemyNestCultivatorBuilding __instance, EnemyEntity enemy)
        {
            var healthComponent = (HealthEntityBase)AccessTools
                .Field(typeof(EnemyNestCultivatorBuilding), "healthComponent").GetValue(__instance);
            if (healthComponent != null && healthComponent.HealthRatio < 1f)
            {
                AccessTools.Field(typeof(EnemyNestCultivatorBuilding), "damageReceivedWhenBlockingEnemy")
                    .SetValue(__instance, 0);
            }
        }
    }
}