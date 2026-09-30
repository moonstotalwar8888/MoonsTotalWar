using System.Collections.Generic;

namespace MoonsTotalWar.Engine
{
	public class UnitDef
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public int Atk { get; set; }
		public int Def { get; set; }
		public int Hp { get; set; }
		public int Cargo { get; set; }
		public int H3Drain { get; set; }
		public long CostE { get; set; }
		public long CostI { get; set; }
		public long CostT { get; set; }
		public int AssemblyTimeSec { get; set; }
		public int ShipyardReqLvl { get; set; }
		public float Speed { get; set; }
		public string Desc { get; set; }
	}

	public class DistrictDef
	{
		public int Id { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public string Icon { get; set; }
		public string ColorHex { get; set; }
		public long BaseE { get; set; }
		public long BaseI { get; set; }
		public long BaseT { get; set; }
		public long BaseH { get; set; }
		public string Desc { get; set; }
	}

	public class FacilityDef
	{
		public int Id { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public string Icon { get; set; }
		public string ColorHex { get; set; }
		public string ReqText { get; set; }
		public long BaseE { get; set; }
		public long BaseI { get; set; }
		public long BaseT { get; set; }
		public long BaseH { get; set; }
		public string Desc { get; set; }
	}

	public class ServerDef
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public float Speed { get; set; }
		public string Status { get; set; }
		public string Desc { get; set; }
	}

	/// <summary>
	/// MOONS TOTAL WAR: MASTER DATA REGISTRY (v40.0 Addictive Army Rebalance Edition)
	/// - 25 Modular Buildings Data Blueprint Contract.
	/// - Troop costs slashed by 40-60% for rapid armada assembly.
	/// - H3 Upkeep strictly hard-capped between 1 and 10 H3/h to prevent instant starvation.
	/// </summary>
	public static class GameData
	{
		public static readonly List<UnitDef> Units = new List<UnitDef>
		{
			new UnitDef { Id = "u1", Name = "Moon Ranger", Atk = 120, Def = 80, Hp = 100, Cargo = 500, H3Drain = 1, CostE = 200, CostI = 100, CostT = 40, AssemblyTimeSec = 30, ShipyardReqLvl = 1, Speed = 100f, Desc = "Rapid response scout vehicle equipped with light kinetic armaments." },
			new UnitDef { Id = "u2", Name = "Defender", Atk = 80, Def = 350, Hp = 250, Cargo = 200, H3Drain = 1, CostE = 300, CostI = 400, CostT = 150, AssemblyTimeSec = 45, ShipyardReqLvl = 2, Speed = 80f, Desc = "Heavy defensive fortification platform designed to withstand orbital raids." },
			new UnitDef { Id = "u3", Name = "Light Raider", Atk = 100, Def = 100, Hp = 80, Cargo = 4500, H3Drain = 2, CostE = 600, CostI = 400, CostT = 300, AssemblyTimeSec = 60, ShipyardReqLvl = 5, Speed = 150f, Desc = "High-speed logistics strike vessel engineered for mass resource extraction." },
			new UnitDef { Id = "u4", Name = "Rebel", Atk = 1200, Def = 1000, Hp = 800, Cargo = 1200, H3Drain = 3, CostE = 2000, CostI = 1500, CostT = 1000, AssemblyTimeSec = 90, ShipyardReqLvl = 7, Speed = 90f, Desc = "Armored frontline assault rover capable of breaking through enemy perimeter walls." },
			new UnitDef { Id = "u6", Name = "Laser Cannon", Atk = 5500, Def = 250, Hp = 1500, Cargo = 0, H3Drain = 4, CostE = 6000, CostI = 4000, CostT = 4000, AssemblyTimeSec = 120, ShipyardReqLvl = 10, Speed = 0f, Desc = "Stationary high-output energy battery. Supreme destructive yield." },
			new UnitDef { Id = "u5", Name = "Battle Tank", Atk = 9500, Def = 8500, Hp = 4500, Cargo = 8000, H3Drain = 5, CostE = 12000, CostI = 10000, CostT = 9000, AssemblyTimeSec = 180, ShipyardReqLvl = 13, Speed = 60f, Desc = "Heavy dreadnought crawler engineered to lead major planetary invasions." },
			new UnitDef { Id = "u7", Name = "Heavy Raider", Atk = 2500, Def = 2500, Hp = 3500, Cargo = 45000, H3Drain = 6, CostE = 20000, CostI = 20000, CostT = 15000, AssemblyTimeSec = 240, ShipyardReqLvl = 17, Speed = 120f, Desc = "Mass-capacity armed transport ship designed for inter-sector looting campaigns." },
			new UnitDef { Id = "u8", Name = "Warlock", Atk = 250000, Def = 150000, Hp = 250000, Cargo = 25000, H3Drain = 10, CostE = 200000, CostI = 150000, CostT = 250000, AssemblyTimeSec = 600, ShipyardReqLvl = 15, Speed = 40f, Desc = "Super-heavy orbital dreadnought. Decimates entire planetary garrisons." },
			new UnitDef { Id = "u9", Name = "Colony Ship", Atk = 0, Def = 50, Hp = 1000, Cargo = 100000, H3Drain = 10, CostE = 150000, CostI = 150000, CostT = 200000, AssemblyTimeSec = 1200, ShipyardReqLvl = 20, Speed = 70f, Desc = "Contains modular life-support reactors to terraform and colonize uninhabited moons." }
		};

		public static readonly List<DistrictDef> Districts = new List<DistrictDef>
		{
			new DistrictDef { Id = 1, Code = "E", Name = "Power Station Grid", Icon = "⚡", ColorHex = "#d946ef", BaseE = 40, BaseI = 60, BaseT = 20, BaseH = 0, Desc = "Generates essential electrical energy required for all mining and factory operations." },
			new DistrictDef { Id = 2, Code = "I", Name = "Iron Extraction Rig", Icon = "⛏️", ColorHex = "#06b6d4", BaseE = 60, BaseI = 20, BaseT = 40, BaseH = 0, Desc = "Extracts dense magnetic ore used for heavy hull construction and infrastructure expansion." },
			new DistrictDef { Id = 3, Code = "T", Name = "Titanium Refinery", Icon = "💎", ColorHex = "#94a3b8", BaseE = 20, BaseI = 40, BaseT = 60, BaseH = 0, Desc = "Smelts titanium alloys required for high-grade shielding and warship armor." },
			new DistrictDef { Id = 4, Code = "H3", Name = "Helium-3 Distillery", Icon = "⛽", ColorHex = "#eab308", BaseE = 120, BaseI = 100, BaseT = 90, BaseH = 0, Desc = "Harvests volatile lunar Helium-3 isotopes. Required to fuel and maintain combat fleets." }
		};

		public static readonly List<FacilityDef> Facilities = new List<FacilityDef>
		{
			new FacilityDef { Id = 5, Code = "cmd", Name = "Industrial Hub Core", Icon = "🏢", ColorHex = "#ffffff", ReqText = "None", BaseE = 1500, BaseI = 2000, BaseT = 800, BaseH = 0, Desc = "Central command spire. Governs structural tech expansion ceilings and sector armor ratings." },
			new FacilityDef { Id = 6, Code = "com", Name = "Commanders Spire", Icon = "🤝", ColorHex = "#22c55e", ReqText = "Core Lvl 11", BaseE = 3000, BaseI = 5000, BaseT = 1500, BaseH = 0, Desc = "Diplomatic link relay. Unlocks global division rankings and commander credentials." },
			new FacilityDef { Id = 7, Code = "flt", Name = "Fleet Station", Icon = "🛰️", ColorHex = "#3b82f6", ReqText = "Core Lvl 4", BaseE = 7500, BaseI = 12000, BaseT = 5000, BaseH = 100, Desc = "Orbital flight operations center. Manages tactical strike group manifests and deployment." },
			new FacilityDef { Id = 8, Code = "rng", Name = "The Deep Radar", Icon = "📡", ColorHex = "#f59e0b", ReqText = "Core Lvl 4", BaseE = 3000, BaseI = 5000, BaseT = 2000, BaseH = 50, Desc = "Long-range sensor array. Scans up to 1,000 moons across the galaxy for hostiles and vacant sectors." },
			new FacilityDef { Id = 9, Code = "mil", Name = "Orbital Shipyard", Icon = "⚔️", ColorHex = "#ef4444", ReqText = "Core Lvl 5", BaseE = 5000, BaseI = 8000, BaseT = 3000, BaseH = 200, Desc = "Heavy aerospace foundry. Assembles combat rovers, missile frigates, dreadnoughts, and colony ships." },
			new FacilityDef { Id = 10, Code = "trd", Name = "Trade Logistics Lab", Icon = "📦", ColorHex = "#06b6d4", ReqText = "Core Lvl 5", BaseE = 12000, BaseI = 18000, BaseT = 8000, BaseH = 500, Desc = "Galactic commerce network. Enables inter-colony supply transfers and alliance economic aid." },
			new FacilityDef { Id = 11, Code = "rsh", Name = "Research Directorate", Icon = "🔬", ColorHex = "#a855f7", ReqText = "Core Lvl 7", BaseE = 15000, BaseI = 20000, BaseT = 15000, BaseH = 1000, Desc = "Advanced science division. Unlocks higher-tier warship blueprints and energy technologies." },
			new FacilityDef { Id = 12, Code = "shd", Name = "Planetary Shield Lab", Icon = "🛡️", ColorHex = "#38bdf8", ReqText = "Core Lvl 7", BaseE = 25000, BaseI = 40000, BaseT = 20000, BaseH = 2000, Desc = "High-yield deflection generator. Mitigates casualty rates during planetary bombardments." },
			new FacilityDef { Id = 13, Code = "mgd", Name = "Galaxy Gold Exchange", Icon = "💰", ColorHex = "#fbbf24", ReqText = "None", BaseE = 0, BaseI = 0, BaseT = 0, BaseH = 0, Desc = "Universal credit bank. Purchase Galaxy Gold to instantly bypass queues and accelerate production." },
			new FacilityDef { Id = 14, Code = "ahq", Name = "Alliance HQ", Icon = "🏛️", ColorHex = "#38bdf8", ReqText = "Core Lvl 4", BaseE = 6000, BaseI = 8000, BaseT = 4000, BaseH = 500, Desc = "Central embassy for Alliance treaty coordination, member reinforcement rallies, and joint planetary wars." },
			new FacilityDef { Id = 15, Code = "silo", Name = "Storage Silos", Icon = "🛢️", ColorHex = "#00f0ff", ReqText = "Core Lvl 1", BaseE = 100, BaseI = 100, BaseT = 50, BaseH = 0, Desc = "High-capacity pressurized containment vaults. Expands global storage limits for Energy, Iron, Titanium, and Helium-3." }
		};

		public static readonly List<ServerDef> Servers = new List<ServerDef>
		{
			new ServerDef { Id = "S1", Name = "GENESIS PRIME", Speed = 1f, Status = "ONLINE", Desc = "Standard Speed - The Pioneer Experience" },
			new ServerDef { Id = "S2", Name = "NEBULA CLUSTER", Speed = 3f, Status = "ONLINE", Desc = "Fast Speed - Accelerated Progression" },
			new ServerDef { Id = "S3", Name = "VOID RUNNERS", Speed = 5f, Status = "LOCKED", Desc = "Rapid Speed - High Frequency Conflict" },
			new ServerDef { Id = "S4", Name = "TITAN OVERDRIVE", Speed = 10f, Status = "LOCKED", Desc = "Super Speed - Extreme Resource Wars" },
			new ServerDef { Id = "S5", Name = "STLLAR STORM", Speed = 15f, Status = "LOCKED", Desc = "Hyper Speed - Advanced Command Protocol" },
			new ServerDef { Id = "S6", Name = "WARZONE ALPHA", Speed = 20f, Status = "LOCKED", Desc = "TOURNAMENT SERVER - TOTAL ANNIHILATION" }
		};
	}
}
