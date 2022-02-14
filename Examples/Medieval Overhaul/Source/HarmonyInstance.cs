﻿using HarmonyLib;
using ProcessorFramework;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DankPyon
{
    [RimWorld.DefOf]
    public static class DankPyonDefOf
    {
        public static ShaderTypeDef TransparentPlant;
        public static ThingDef DankPyon_Artillery_Trebuchet;
        public static ThingDef DankPyon_Artillery_Boulder;
    }
    [StaticConstructorOnStartup]
    public static class HarmonyInstance
    {
        public static Dictionary<Thing, PlantExtension> cachedTransparentablePlants = new Dictionary<Thing, PlantExtension>();

        public static Dictionary<HediffDef, StatDef> statMultipliers = new Dictionary<HediffDef, StatDef>();

        public static Harmony harmony;
        static HarmonyInstance()
        {
            harmony = new Harmony("lalapyhon.rimworld.medievaloverhaul");
            harmony.Patch(AccessTools.Method(typeof(Thing), "ButcherProducts", null, null), null,
                new HarmonyMethod(typeof(HarmonyInstance), "Thing_MakeButcherProducts_FatAndBone_PostFix", null), null);
            harmony.PatchAll();
            foreach (var stat in DefDatabase<StatDef>.AllDefs)
            {
                var extension = stat.GetModExtension<HediffFallRateMultiplier>();
                if (extension != null && extension.hediffDef != null)
                {
                    statMultipliers[extension.hediffDef] = stat;
                }
            }

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.IsChunk() && def.projectileWhenLoaded is null)
                {
                    def.projectileWhenLoaded = DankPyonDefOf.DankPyon_Artillery_Boulder;
                }
            }
        }

        public static bool IsChunk(this ThingDef def)
        {
            if (!def.thingCategories.NullOrEmpty())
            {
                if (!def.thingCategories.Contains(ThingCategoryDefOf.Chunks))
                {
                    return def.thingCategories.Contains(ThingCategoryDefOf.StoneChunks);
                }
            }
            return false;
        }

        private static Dictionary<Thing, HashSet<IntVec3>> cachedCells = new Dictionary<Thing, HashSet<IntVec3>>();
        public static HashSet<IntVec3> GetTransparentCheckArea(this Thing plant, PlantExtension extension)
        {
            if (!cachedCells.TryGetValue(plant, out var cells))
            {
                cachedCells[plant] = cells = GetTransparentCheckAreaInt(plant, extension);
            }
            return cells;
        }
        private static HashSet<IntVec3> GetTransparentCheckAreaInt(Thing plant, PlantExtension extension)
        {
            var cellRect = new CellRect(plant.Position.x, plant.Position.z, extension.firstArea.x, extension.firstArea.z);
            if (extension.firstAreaOffset != IntVec2.Zero)
            {
                cellRect = cellRect.MovedBy(extension.firstAreaOffset);
            }
            var cells = cellRect.Cells.ToList();
            if (extension.secondArea != IntVec2.Zero)
            {
                var cells2 = new CellRect(plant.Position.x, plant.Position.z, extension.secondArea.x, extension.secondArea.z);
                if (extension.secondAreaOffset != IntVec2.Zero)
                {
                    cells2 = cells2.MovedBy(extension.secondAreaOffset);
                }
                cells.AddRange(cells2);
            }
            return cells.Where(x => x.InBounds(plant.Map)).ToHashSet();
        }
        public static bool HasItemsInCell(IntVec3 cell, Map map, PlantExtension extension)
        {
            foreach (var thing in cell.GetThingList(map))
            {
                if (ItemMatches(thing, extension))
                {
                    return true;
                }
            }
            return false;
        }
        public static bool BaseItemMatches(Thing thing)
        {
            return thing is Pawn || thing.def.category == ThingCategory.Item;
        }
        public static bool ItemMatches(Thing thing, PlantExtension extension)
        {
            return (thing is Pawn || thing.def.category == ThingCategory.Item) && (extension.ignoredThings is null || !extension.ignoredThings.Contains(thing.def));
        }

        public static Dictionary<Thing, Shader> lastCachedShaders = new Dictionary<Thing, Shader>();
        public static void RecheckTransparency(Thing plant, Thing otherThing, PlantExtension extension)
        {
            if (plant != otherThing && plant.Spawned && plant.Map == otherThing.Map)
            {
                if (!lastCachedShaders.TryGetValue(plant, out var shader))
                {
                    lastCachedShaders[plant] = shader = plant.Graphic.Shader;
                }

                bool isTransparent = shader == DankPyonDefOf.TransparentPlant.Shader;
                if (!isTransparent && ItemMatches(otherThing, extension))
                {
                    var cells = GetTransparentCheckArea(plant, extension);
                    if (cells.Contains(otherThing.Position))
                    {
                        otherThing.Map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlag.Things);
                        return;
                    }
                }

                if (isTransparent)
                {
                    var cells = GetTransparentCheckArea(plant, extension);
                    if (!cells.Any(x => HasItemsInCell(x, otherThing.Map, extension)))
                    {
                        otherThing.Map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlag.Things);
                    }
                }
            }
        }
        [HarmonyPatch(typeof(Thing), "SpawnSetup")]
        public class Thing_SpawnSetup_Patch
        {
            private static void Prefix(Thing __instance)
            {
                var extension = __instance.def.GetModExtension<PlantExtension>();
                if (extension != null && extension.transparencyWhenPawnOrItemIsBehind)
                {
                    cachedTransparentablePlants[__instance] = extension;
                }
            }
        }

        [HarmonyPatch(typeof(Thing), "Position", MethodType.Setter)]
        public class Thing_Position_Patch
        {
            private static void Postfix(Thing __instance)
            {
                if (BaseItemMatches(__instance) && __instance.Map != null)
                {
                    foreach (var data in cachedTransparentablePlants)
                    {
                        var plant = data.Key;
                        if (__instance.positionInt.z > plant.positionInt.z && plant.positionInt.DistanceTo(__instance.positionInt) < 13)
                        {
                            RecheckTransparency(plant, __instance, data.Value);
                        }
                    }
                }
            }
        }


        [HarmonyPatch(typeof(Plant), "Graphic", MethodType.Getter)]
        public class Patch_Graphic_Postfix
        {
            private static void Postfix(Plant __instance, ref Graphic __result)
            {
                if (cachedTransparentablePlants.TryGetValue(__instance, out var extension))
                {
                    var cells = GetTransparentCheckArea(__instance, extension);
                    bool anyItemsExistsInArea = cells.Any(x => HasItemsInCell(x, __instance.Map, extension));
                    if (anyItemsExistsInArea)
                    {
                        __result = GraphicDatabase.Get(__result.GetType(), "Transparent/" + __result.path, DankPyonDefOf.TransparentPlant.Shader,
                            __instance.def.graphicData.drawSize, __result.color, __result.colorTwo);
                        __instance.Map.mapDrawer.MapMeshDirty(__instance.Position, MapMeshFlag.Things);
                        lastCachedShaders[__instance] = DankPyonDefOf.TransparentPlant.Shader;

                    }
                    else
                    {
                        __result = GraphicDatabase.Get(__result.GetType(), __instance.def.graphicData.texPath, __instance.def.graphicData.shaderType.Shader,
                            __instance.def.graphicData.drawSize, __result.color, __result.colorTwo);
                        __instance.Map.mapDrawer.MapMeshDirty(__instance.Position, MapMeshFlag.Things);
                        lastCachedShaders[__instance] = __instance.def.graphicData.shaderType.Shader;
                    }
                }
            }
        }

        private static ThingDef fat = ThingDef.Named("DankPyon_Fat");
        private static ThingDef bone = ThingDef.Named("DankPyon_Bone");
        private static void Thing_MakeButcherProducts_FatAndBone_PostFix(Thing __instance, ref IEnumerable<Thing> __result, Pawn butcher, float efficiency)
        {
            if (__instance is Pawn pawn && pawn.RaceProps.IsFlesh && pawn.RaceProps.meatDef != null)
            {
                bool boneFlag = true;
                bool fatFlag = true;

                var butcherProperties = ButcherProperties.Get(__instance.def);
                if (butcherProperties != null)
                {
                    boneFlag = butcherProperties.hasBone;
                    fatFlag = butcherProperties.hasFat;
                }

                if (boneFlag || fatFlag)
                {
                    int amount = Math.Max(1, (int)(GenMath.RoundRandom(__instance.GetStatValue(StatDefOf.MeatAmount, true) * efficiency) * 0.2f));
                    if (boneFlag)
                    {
                        Thing Bone = ThingMaker.MakeThing(bone, null);
                        Bone.stackCount = amount;
                        __result = __result.AddItem(Bone);
                    }
                    if (fatFlag)
                    {
                        Thing Fat = ThingMaker.MakeThing(fat, null);
                        Fat.stackCount = amount;
                        __result = __result.AddItem(Fat);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(JobDriver_ManTurret), nameof(JobDriver_ManTurret.FindAmmoForTurret))]
        public static class Patch_TryFindRandomShellDef
        {
            private static bool Prefix(Pawn pawn, Building_TurretGun gun, ref Thing __result)
            {
                if (gun.TryGetArtillery(out var group))
                {
                    StorageSettings allowedShellsSettings = pawn.IsColonist ? gun.gun.TryGetComp<CompChangeableProjectile>().allowedShellsSettings : RetrieveParentSettings(gun);
                    bool validator(Thing t) => !t.IsForbidden(pawn) && pawn.CanReserve(t, 10, 1, null, false) && (allowedShellsSettings == null || allowedShellsSettings.AllowedToAccept(t));
                    __result = GenClosest.ClosestThingReachable(gun.Position, gun.Map, ThingRequest.ForGroup(group), PathEndMode.OnCell, TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false),
                        40f, validator, null, 0, -1, false, RegionType.Set_Passable, false);
                    return false;
                }
                return true;
            }

            private static StorageSettings RetrieveParentSettings(Building_TurretGun gun)
            {
                return gun.gun.TryGetComp<CompChangeableProjectile>().GetParentStoreSettings();
            }
        }
    }

    [StaticConstructorOnStartup]
    public static class ArtillerySearchGroup
    {
        private static readonly Dictionary<ThingDef, ThingRequestGroup> registeredArtillery = new Dictionary<ThingDef, ThingRequestGroup>();
        static ArtillerySearchGroup()
        {
            RegisterArtillery(DankPyonDefOf.DankPyon_Artillery_Trebuchet, ThingRequestGroup.Chunk);
        }

        public static bool RegisterArtillery(ThingDef def, ThingRequestGroup ammoGroup)
        {
            if (!registeredArtillery.ContainsKey(def))
            {
                registeredArtillery.Add(def, ammoGroup);
                return true;
            }
            return false;
        }

        public static bool TryGetArtillery(this Thing thing, out ThingRequestGroup group) => registeredArtillery.TryGetValue(thing.def, out group);
    }

    public class HediffFallRateMultiplier : DefModExtension
    {
        public HediffDef hediffDef;
    }

    [HarmonyPatch(typeof(HediffComp_SeverityPerDay), "SeverityChangePerDay")]
    [HarmonyPatch(typeof(HediffComp_Immunizable), "SeverityChangePerDay")]
    public class HediffComp_Immunizable_Patch
    {
        private static void Postfix(HediffComp_SeverityPerDay __instance, ref float __result)
        {
            if (HarmonyInstance.statMultipliers.TryGetValue(__instance.Def, out var stat))
            {
                __result *= __instance.Pawn.GetStatValue(stat);
            }
        }
    }

    [HarmonyPatch(typeof(StatWorker_MarketValue), "CalculableRecipe")]
    public static class CalculableRecipe_Patch
    {
        private static bool Prefix(BuildableDef def)
        {
            if (def is ThingDef thingDef && thingDef.IsChunk())
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ResearchProjectDef), "CanBeResearchedAt")]
    public static class ResearchProjectDef_CanBeResearchedAt_Patch
    {
        public static void Postfix(Building_ResearchBench bench, bool ignoreResearchBenchPowerStatus, ResearchProjectDef __instance, ref bool __result)
        {
            if (__result)
            {
                var fuelComp = bench.GetComp<CompRefuelable>();
                if (fuelComp != null && !fuelComp.HasFuel)
                {
                    __result = false;
                }
            }
        }
    }

    public class ProcessorExtension : DefModExtension
    {
        public bool outputOnlyButcherProduct;
    }
    [HarmonyPatch(typeof(CompProcessor), "TakeOutProduct")]
    public static class CompProcessor_TakeOutProduct_Patch
    {
        public static bool Prefix(CompProcessor __instance, ref Thing __result, ActiveProcess activeProcess)
        {
            var extension = activeProcess.processDef.GetModExtension<ProcessorExtension>();
            if (extension != null)
            {
                if (extension.outputOnlyButcherProduct)
                {
                    __result = TakeOutButcherProduct(__instance, activeProcess);
                    return false;
                }
            }
            return true;
        }

        public static Thing TakeOutButcherProduct(CompProcessor __instance, ActiveProcess activeProcess)
        {
            Thing thing = null;
            if (!activeProcess.Ruined)
            {
                var thingDefCount = activeProcess.ingredientThings.FirstOrDefault(x => x.def.butcherProducts.Any()).def.butcherProducts.FirstOrDefault();
                thing = ThingMaker.MakeThing(thingDefCount.thingDef);
                thing.stackCount = thingDefCount.count;
                CompIngredients compIngredients = thing.TryGetComp<CompIngredients>();
                List<ThingDef> list = new List<ThingDef>();
                foreach (Thing ingredientThing in activeProcess.ingredientThings)
                {
                    List<ThingDef> list2 = ingredientThing.TryGetComp<CompIngredients>()?.ingredients;
                    if (!list2.NullOrEmpty())
                    {
                        list.AddRange(list2);
                    }
                }
                if (compIngredients != null && !list.NullOrEmpty())
                {
                    compIngredients.ingredients.AddRange(list);
                }
                if (activeProcess.processDef.usesQuality)
                {
                    thing.TryGetComp<CompQuality>()?.SetQuality(activeProcess.CurrentQuality, ArtGenerationContext.Colony);
                }
                foreach (BonusOutput bonusOutput in activeProcess.processDef.bonusOutputs)
                {
                    if (!Rand.Chance(bonusOutput.chance))
                    {
                        continue;
                    }
                    int num = GenMath.RoundRandom((float)activeProcess.ingredientCount * activeProcess.processDef.capacityFactor / (float)__instance.Props.capacity * (float)bonusOutput.amount);
                    if (num <= 0)
                    {
                        continue;
                    }
                    if (bonusOutput.thingDef.race != null)
                    {
                        for (int i = 0; i < num; i++)
                        {
                            GenSpawn.Spawn(PawnGenerator.GeneratePawn(new PawnGenerationRequest(bonusOutput.thingDef.race.AnyPawnKind, null, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: false, newborn: true)), __instance.parent.Position, __instance.parent.Map);
                        }
                    }
                    else
                    {
                        Thing thing2 = ThingMaker.MakeThing(bonusOutput.thingDef);
                        thing2.stackCount = num;
                        GenPlace.TryPlaceThing(thing2, __instance.parent.Position, __instance.parent.Map, ThingPlaceMode.Near);
                    }
                }
            }
            foreach (Thing ingredientThing2 in activeProcess.ingredientThings)
            {
                __instance.innerContainer.Remove(ingredientThing2);
                ingredientThing2.Destroy();
            }
            __instance.activeProcesses.Remove(activeProcess);
            if (Rand.Chance(activeProcess.processDef.destroyChance * (float)activeProcess.ingredientCount * activeProcess.processDef.capacityFactor / (float)__instance.Props.capacity))
            {
                if (PF_Settings.replaceDestroyedProcessors)
                {
                    GenConstruct.PlaceBlueprintForBuild_NewTemp(__instance.parent.def, __instance.parent.Position, __instance.parent.Map, __instance.parent.Rotation, Faction.OfPlayer, null);
                }
                __instance.parent.Destroy();
                return thing;
            }
            if (__instance.Empty)
            {
                __instance.GraphicChange(toEmpty: true);
            }
            if (!__instance.activeProcesses.Any((ActiveProcess x) => x.processDef.usesQuality))
            {
                __instance.emptyNow = false;
            }
            return thing;
        }
    }

}
