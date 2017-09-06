using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace ActiveTerrain
{
    /// <summary>
    /// This terrain comp will push heat endlessly with no regard for ambient temperature.
    /// </summary>
    public class TerrainComp_HeatPush : TerrainComp
    {
        public TerrainCompProperties_HeatPush Props { get { return (TerrainCompProperties_HeatPush)props; } }

        protected virtual bool ShouldPushHeat { get { return true; } }

        protected virtual float PushAmount { get { return Props.pushAmount; } }

        public override void CompTick()
        {
            base.CompTick();
            if (Find.TickManager.TicksGame % 60 == this.HashCodeToMod(60) && ShouldPushHeat)
            {
                GenTemperature.PushHeat(parent.Position, parent.Map, PushAmount);
            }
        }
    }
    /// <summary>
    /// Self-cleaning floors.
    /// </summary>
    public class TerrainComp_SelfClean : TerrainComp
    {
        public float cleanProgress = float.NaN;

        public Filth currentFilth;

        public TerrainCompProperties_SelfClean Props { get { return (TerrainCompProperties_SelfClean)props; } }

        protected virtual bool CanClean { get { return true; } }

        public void StartClean()
        {
            if (currentFilth == null)
            {
                Log.Warning("Cannot start clean for filth because there is no filth selected. Canceling.");
                return;
            }
            if (currentFilth.def.filth == null)
            {
                Log.Error($"Filth of def {currentFilth.def.defName} cannot be cleaned because it has no FilthProperties.");
                return;
            }
            cleanProgress = currentFilth.def.filth.cleaningWorkToReduceThickness;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (CanClean)
            {
                DoCleanWork();
            }
        }

        public virtual void DoCleanWork()
        {
            if (currentFilth == null)
            {
                cleanProgress = float.NaN;
                if (!FindFilth())
                    return;
            }

            if (float.IsNaN(cleanProgress))
                StartClean();

            if (cleanProgress > 0f)
                cleanProgress -= 1f;
            else
                FinishClean();
        }

        public bool FindFilth()
        {
            if (currentFilth != null)
            {
                return true;
            }
            var filth = (Filth)parent.Position.GetThingList(parent.Map).Find(f => f is Filth);
            if (filth != null)
            {
                currentFilth = filth;
                return true;
            }
            return false;
        }

        public void FinishClean()
        {
            if (currentFilth == null)
            {
                Log.Warning("Cannot finish clean for filth because there is no filth selected. Canceling.");
                return;
            }
            currentFilth.ThinFilth();
            if (currentFilth.Destroyed)
                currentFilth = null;
            else
                cleanProgress = float.NaN;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref cleanProgress, "cleanProgress", float.NaN);
            Scribe_References.Look(ref currentFilth, "currentFilth");
        }
    }
    /// <summary>
    /// Terrain comp that controls and maintains temperature.
    /// </summary>
    public class TerrainComp_TempControl : TerrainComp_HeatPush
    {
        public bool operatingAtHighPower;

        public new TerrainCompProperties_TempControl Props { get { return (TerrainCompProperties_TempControl)props; } }
        public float AmbientTemperature { get { return GenTemperature.GetTemperatureForCell(parent.Position, parent.Map); } }
        public float PowerConsumptionNow
        {
            get
            {
                float basePowerConsumption = parent.def.GetCompProperties<TerrainCompProperties_PowerTrader>().basePowerConsumption;
                return operatingAtHighPower ? basePowerConsumption : basePowerConsumption * Props.lowPowerConsumptionFactor;
            }
        }

        [Unsaved] CompTempControl parentTempControl;
        public virtual CompTempControl HeaterToConformTo { get
            {
                if (parentTempControl != null && parentTempControl.parent.Spawned)
                {
                    parentTempControl = null;
                    return parentTempControl;
                }

                var room = parent.Position.GetRoom(parent.Map);
                if (room == null) return null;

                return parentTempControl = room.GetTempControl(this.AnalyzeType());
            } }
        public float TargetTemperature
        {
            get
            {
                return HeaterToConformTo?.targetTemperature ?? 21;
            }
        }
        protected override float PushAmount
        {
            get
            {
                //Code mimicked from Building_Heater
                if (!Props.reliesOnPower || (parent.GetComp<TerrainComp_PowerTrader>()?.PowerOn ?? true))
                {
                    float ambientTemperature = AmbientTemperature;
                    //Ternary expression... Mathf.InverseLerp is already clamped though so Mathf.InverseLerp(120f, 20f, ambientTemperature) itself should yield same results
                    float heatPushEfficiency = (ambientTemperature < 20f) ? 1f : (ambientTemperature > 120f) ? 0f : Mathf.InverseLerp(120f, 20f, ambientTemperature);
                    float energyLimit = Props.energyPerSecond * heatPushEfficiency * 4.16666651f;
                    float num2 = GenTemperature.ControlTemperatureTempChange(parent.Position, parent.Map, energyLimit, TargetTemperature);
                    bool flag = !Mathf.Approximately(num2, 0f) && parent.Position.GetRoomGroup(parent.Map) != null;//Added room group check
                    var powerTraderComp = parent.GetComp<TerrainComp_PowerTrader>();
                    if (flag)
                    {
                        GenTemperature.PushHeat(parent.Position, parent.Map, num2);
                    }
                    if (powerTraderComp != null)
                    {
                        powerTraderComp.PowerOutput = flag ? -powerTraderComp.Props.basePowerConsumption : -powerTraderComp.Props.basePowerConsumption * Props.lowPowerConsumptionFactor;
                    }
                    operatingAtHighPower = flag;
                    return (flag) ? num2 : 0f;
                }
                operatingAtHighPower = false;
                return 0f;
            }
        }
        public override void CompTick()
        {
            base.CompTick();
            if (Props.cleansSnow && Find.TickManager.TicksGame % 60 == this.HashCodeToMod(60))
            {
                CleanSnow();
                UpdatePowerConsumption();
            }
        }
        public virtual void CleanSnow()
        { 
            var snowDepth = parent.Map.snowGrid.GetDepth(parent.Position);
            if (!Mathf.Approximately(0f, snowDepth))
            {
                operatingAtHighPower = true;
                float newDepth = Mathf.Max(snowDepth - Props.snowMeltAmountPerSecond, 0f);
                parent.Map.snowGrid.SetDepth(parent.Position, newDepth);
            }
        }

        public void UpdatePowerConsumption()
        {
            TerrainComp_PowerTrader powerComp = parent.GetComp<TerrainComp_PowerTrader>();
            if (powerComp != null)
            {
                powerComp.PowerOutput = -PowerConsumptionNow;
            }
        }

        public override string TransformLabel(string label) { return base.TransformLabel(label) + " " + (operatingAtHighPower ? "HeatedFloor_HighPower".Translate() : "HeatedFloor_LowPower".Translate()); }
    }
    /// <summary>
    /// Glower except for terrain.
    /// </summary>
    public class TerrainComp_Glower : TerrainComp
    {
        //Unsaved fields
        [Unsaved] protected bool currentlyOn;
        /// <summary>
        /// Kept for later use
        /// </summary>
        [Unsaved] CompGlower instanceGlowerComp;
        
        public CompGlower AsThingComp { get { return (instanceGlowerComp == null) ? instanceGlowerComp = (CompGlower)this : instanceGlowerComp; }}

        public TerrainCompProperties_Glower Props { get { return (TerrainCompProperties_Glower)props; } }

        public virtual bool ShouldBeLitNow
        {
            get
            {
                return (parent.GetComp<TerrainComp_PowerTrader>()?.PowerOn ?? true) || !Props.powered;
            }
        }

        //Main fields and their properties
        ColorInt colorInt;
        float glowRadius;
        float overlightRadius;
        public float OverlightRadius { get => overlightRadius; set => overlightRadius = value; }
        public float GlowRadius { get => glowRadius; set => glowRadius = value; }
        public ColorInt Color { get => colorInt; set => colorInt = value; }

        public void UpdateLit()
        {
            bool shouldBeLitNow = ShouldBeLitNow;
            if (currentlyOn == shouldBeLitNow)
            {
                return;
            }
            currentlyOn = shouldBeLitNow;
            parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlag.Things);
            //Ternary logic statement.
            (currentlyOn ? (Action<CompGlower>)parent.Map.glowGrid.RegisterGlower : parent.Map.glowGrid.DeRegisterGlower)(AsThingComp);
        }

        public override void ReceiveCompSignal(string sig)
        {
            base.ReceiveCompSignal(sig);
            if (sig == CompSignals.PowerTurnedOff || sig == CompSignals.PowerTurnedOn)
            {
                UpdateLit();
            }
        }

        public override void PostPostLoad()
        {
            UpdateLit();
            if (ShouldBeLitNow)
            {
                parent.Map.glowGrid.RegisterGlower(AsThingComp);
            }
        }

        public override void Initialize(TerrainCompProperties props)
        {
            base.Initialize(props);
            Color = Props.glowColor;
            GlowRadius = Props.glowRadius;
            OverlightRadius = Props.overlightRadius;
        }

        /// <summary>
        /// Hacked-together method to convert terrain comp into thing comp for map glower component. 
        /// Note: Glower comp only works as a dummy, and many pieces of information are missing which may cause quirks/errors outside of intended use
        /// Note2: TerrainComp_Glower has its own property for storing a CompGlower. Use that as that way the object reference would be kept the same.
        /// </summary>
        public static explicit operator CompGlower(TerrainComp_Glower inst)
        {
            var glower = new CompGlower()//Create instance
            {
                parent = (ThingWithComps)ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel) //The most generic ThingWithComps there is
            };
            glower.parent.SetPositionDirect(inst.parent.Position);//Set position
            glower.Initialize(new CompProperties_Glower()//Copy props
            {
                glowColor = inst.Color,
                glowRadius = inst.GlowRadius,
                overlightRadius = inst.OverlightRadius
            });
            return glower;
        }
    }
}
