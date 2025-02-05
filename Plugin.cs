﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using IAmFuture.Data.Enemies;
using IAmFuture.Gameplay.Buildings;
using IAmFuture.Gameplay.Electricity;
using IAmFuture.Gameplay.HealthSystems;

namespace BetterNoHarmCultivator;

[BepInPlugin(PluginInfo.PluginGuid, PluginInfo.PluginName, PluginInfo.PluginVersion)]
[BepInProcess("I Am Future.exe")]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<bool> _blockDamage;
    private ConfigEntry<bool> _requireElectricity;
    private ConfigEntry<bool> _useCustomElectricityUsage;
    private ConfigEntry<float> _electricityUsage;

    private void LoadConfigs()
    {
        _blockDamage = Config.Bind("Damage",
            "blockDamage",
            true,
            "Block damage to cultivator");

        _requireElectricity = Config.Bind("Electricity",
            "requireElectricity",
            true,
            "If cultivator needs electricity to work (will still use electricity if connected)");

        _useCustomElectricityUsage = Config.Bind("Electricity Usage",
            "useCustomElectricityUsage",
            false,
            "If the cultivator should use custom amount of electricity");

        _electricityUsage = Config.Bind("Electricity Usage",
            "electricityUsage",
            0f,
            "Cultivator\'s electricity usage");
    }

    private void Awake()
    {
        LoadConfigs();
        var harmony = new Harmony(PluginInfo.PluginGuid);

        if (_blockDamage.Value)
        {
            harmony.PatchAll(typeof(BlockDamage));
        }

        if (!_requireElectricity.Value)
        {
            harmony.PatchAll(typeof(NoElectricityRequired));
        }

        if (_useCustomElectricityUsage.Value)
        {
            CustomElectricityUsage.ElectricityUsage = _electricityUsage.Value;
            harmony.PatchAll(typeof(CustomElectricityUsage));
        }

        Logger.LogInfo($"Plugin {PluginInfo.PluginGuid} is loaded!");
    }

    [HarmonyPatch(typeof(EnemyNestCultivatorBuilding))]
    internal class CustomElectricityUsage
    {
        public static float ElectricityUsage;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(EnemyNestCultivatorBuilding), "UpdateElectricityConsumption")]
        private static void Prefix(ref EnemyNestCultivatorBuilding __instance)
        {
            var electricityConsumerField =
                AccessTools.Field(typeof(EnemyNestCultivatorBuilding), "electricityConsumer");
            var electricityConsumer = (ElectricityConsumer)electricityConsumerField.GetValue(__instance);
            electricityConsumer.ConsumeAmount = Math.Abs(ElectricityUsage);
            electricityConsumerField.SetValue(__instance, electricityConsumer);
        }

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
    }

    [HarmonyPatch(typeof(EnemyNestCultivatorBuilding))]
    internal class NoElectricityRequired
    {
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
        [HarmonyPrefix]
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
            else
            {
                AccessTools.Field(typeof(EnemyNestCultivatorBuilding), "damageReceivedWhenBlockingEnemy")
                    .SetValue(__instance, 1);
            }
        }
    }
}