namespace LightPCG.Core
{
    public struct MidValues
    {
        public float ClLegacy, MidLegacy;   // old formula, kept only for sensitivity analysis
        public float ClNorm, Ch, Ci, MidStatic;
        public float MidMobility, MidM;
    }

    /// <summary>
    /// MID_static = (Cl~ + Ch + Ci) / 3,      Cl~ = Nobj / Nobj_max
    /// MID_mob    = (Mr + Mo + Mi + Md) / 4
    /// MID-M      = ALPHA * MID_static + (1 - ALPHA) * MID_mob,  ALPHA = 0.5
    /// All weights are fixed BEFORE the evaluation run and never tuned on it.
    /// </summary>
    public static class MidCalculator
    {
        public const float ALPHA = 0.5f;

        public static MidValues Compute(int totalObjs, int nObjMax, int gridW, int gridH,
                                        int solObjs, int ruleTransitions, int refractorCount,
                                        MobilityFeatures mob)
        {
            var v = new MidValues();
            v.Ch = solObjs > 1 ? (float)ruleTransitions / (solObjs - 1) : 0f;
            v.Ci = solObjs > 0 ? (float)refractorCount / solObjs : 0f;

            v.ClLegacy = (float)totalObjs / (gridW * gridH);
            v.MidLegacy = 0.5f * v.ClLegacy + 0.3f * v.Ch + 0.2f * v.Ci;

            v.ClNorm = nObjMax > 0 ? (float)totalObjs / nObjMax : 0f;
            v.MidStatic = (v.ClNorm + v.Ch + v.Ci) / 3f;

            v.MidMobility = (mob.Mr + mob.Mo + mob.Mi + mob.Md) / 4f;
            v.MidM = ALPHA * v.MidStatic + (1f - ALPHA) * v.MidMobility;
            return v;
        }
    }
}
