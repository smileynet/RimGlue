using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace ArgonTribalMedicine
{
    public class Hediff_CurativeTea : Hediff_High
    {
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            List<Hediff> pawnHediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < pawnHediffs.Count; i++)
            {
                if (pawnHediffs[i].Visible && pawnHediffs[i].TryGetComp<HediffComp_Immunizable>() != null && !pawnHediffs[i].FullyImmune())
                {
                    pawnHediffs[i].Severity -= 0.05f;
                    break;
                }
            }
        }
    }

    public class Hediff_DigestiveTea : Hediff_High
    {
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            List<Hediff> pawnHediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < pawnHediffs.Count; i++)
            {
                if (pawnHediffs[i].def.defName == "FoodPoisoning" && pawnHediffs[i].Severity > 0.2f)
                {
                    pawnHediffs[i].Severity = 0.19f;
                    break;
                }
            }
        }
    }

    public class Hediff_InsanityTea : Hediff_High
    {
        public override void PostRemoved()
        {
            base.PostRemoved();
            Random rnd = new Random();
            if (rnd.Next(0, 100) < 65)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("TM_InsanityTeaEffectThoughtGood"));
            }
            else
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("TM_InsanityTeaEffectThoughtBad"));
            }
        }
    }
}
