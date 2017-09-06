using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace ActiveTerrain
{
    public class SpecialTerrainList : MapComponent
    {
    	//.ctor... need we say more
        public SpecialTerrainList(Map map) : base(map) { }
        
        public Dictionary<IntVec3, TerrainInstance> terrains = new Dictionary<IntVec3, TerrainInstance>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref terrains, "terrains", LookMode.Value, LookMode.Deep);
        }

        /// <summary>
        /// Ticker for terrains
        /// </summary>
        public override void MapComponentTick()
        {
            base.MapComponentTick();
			foreach (var terr in terrains)
			{
				terr.Value.Tick();
			}
        }
        
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            RefreshAllCurrentTerrain();
            CallPostLoad();
        }

        public void CallPostLoad()
        {
            foreach (var k in terrains.Keys)
            {
                terrains[k].PostLoad();
            }
        }
		
        /// <summary>
        /// Registers terrain currently present to terrain list, called on init
        /// </summary>
        public void RefreshAllCurrentTerrain()
        {
            foreach (var cell in map) //Map is IEnumerable...
            {
                TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                if (terrain is SpecialTerrain special)
                {
                    RegisterAt(special, cell);
                }
            }
        }

		public void RegisterAt(SpecialTerrain special, int i)
		{
			RegisterAt(special, map.cellIndices.IndexToCell(i));
		}

		public void RegisterAt(SpecialTerrain special, IntVec3 cell)
		{
            if (!terrains.ContainsKey(cell))
            {
                var newTerr = special.MakeTerrainInstance(map, cell);
                newTerr.Init();
                terrains.Add(cell, newTerr);
            }
        }

        /// <summary>
        /// Updater for terrains
        /// </summary>
        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
			foreach (var terr in terrains)
			{
				terr.Value.Update();
			}
        }
        
        public void Notify_RemovedTerrainAt(IntVec3 c)
        {
        	var terr = terrains[c];
        	terrains.Remove(c);
        	terr.PostRemove();
        }
    }
}
