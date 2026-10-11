using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER TACTICAL FACILITY INSPECTOR MODAL (v17.2 Universal Sync Edition)
	/// - Dynamically centers on physical screen viewport on open.
	/// - Universal Construction Matrix: All buildings display their Core and Sequence requirements in a unified checklist.
	/// - Queue Lock: Queue button is disabled ("DOCK IDLE") unless an upgrade is already actively running.
	/// - Dual-Button System: Start Upgrade (Free) and Queue Upgrade (25 GGold).
	/// - Premium Boosts: 7-Day and 30-Day production multipliers with Global variants.
	/// - Transaction Safety Net: Intercepts all GGold purchases with a strict confirmation overlay.
	/// - Fully synchronized with GameMath Complexity Tiers & Dynamic Floors.
	/// </summary>
	public partial class BuildingInspectorModal : Control
	{
		public static BuildingInspectorModal Instance { get; private set; }

		[Signal] public delegate void BuildingUpgradedEventHandler(string buildingId, int newLevel);
		[Signal] public delegate void UnitsRecruitedEventHandler(string unitId, int count);

		public string ActiveBuildingId = "";
		public string ActiveBuildingName = "";
		public int ActiveBuildingLevel = 1;
		public Color ActiveBuildingColor = Colors.White;
		public string ActiveDistrictCode = "";
		public int ActiveSlotIndex = 0;
		public int[] ActiveDistrictLevels = new int[0];
		public int CurrentIndustrialCoreLevel = 1;
		public Dictionary<string, int> AllBuildingLevels = new Dictionary<string, int>();

		private ColorRect _dimBackdrop;
		private Panel _modalChassis;
		private VBoxContainer _mainLayout;
		private Label _lblHeaderTitle;
		private Label _lblLevelBadge;
		private Button _btnClose;
		private ScrollContainer _scrollContainer;
		private VBoxContainer _contentStack;

		// Confirmation Overlay Nodes
		private ColorRect _confirmOverlay;
		private Label _lblConfirmTitle;
		private Label _lblConfirmMsg;
		private Action _pendingConfirmAction;

		private Dictionary<string, LineEdit> _unitQtyInputs = new Dictionary<string, LineEdit>();
		private LineEdit _radarJumpInput;

		// Live UI Ticker References (Updates every frame while Inspector Modal is open!)
		private VBoxContainer _shipyardLiveBaysBox;
		private readonly Dictionary<string, Label> _shipyardUnitTitleLabels = new Dictionary<string, Label>();
		private readonly Dictionary<string, Label> _shipyardUnitStatusLabels = new Dictionary<string, Label>();
		private Label _armoryLiveSummaryLabel;
		private VBoxContainer _armoryLiveBaysBox;
		private readonly Dictionary<string, Label> _armoryUnitQtyLabels = new Dictionary<string, Label>();
		private readonly Dictionary<string, Label> _fleetTransitLocLabels = new Dictionary<string, Label>();
		private Label _liveUpgradeDurationLabel;
		private int _liveUpgradeBaseDurationSec = 0;
		private Label _liveResearchDirectorateTimerLabel;

		// Live Upgrade Requirements, Per-Resource Deficit & 10-Second Red Alert Glow State
		private PanelContainer _prereqCardPanel;
		private StyleBoxFlat _prereqCardStyle;
		private Label _prereqTitleLabel;
		private PanelContainer _resourceCardPanel;
		private StyleBoxFlat _resourceCardStyle;
		private Label _lblLiveCostE, _lblLiveCostI, _lblLiveCostT, _lblLiveCostH3;
		private (long e, long i, long t, long h) _currentUpgradeCost;
		private int _currentUpgradeTargetLevel = 1;
		private int _currentUpgradeDurationSec = 0;
		private bool _currentAllPrereqsMet = true;
		private List<GameMath.RequirementStatus> _currentPrereqList = new List<GameMath.RequirementStatus>();
		private Button _btnStartUpgrade;
		private Button _btnQueueUpgrade;
		private bool _isActiveBuildingDamaged = false;
		private (long costE, long costI, long costT, long costH3, int damagedHp, double effectiveLevel) _currentRepairMetrics;
		private PanelContainer _repairCardPanel;
		private StyleBoxFlat _repairCardStyle;
		private Label _lblLiveRepairE, _lblLiveRepairI, _lblLiveRepairT, _lblLiveRepairH3;
		private Button _btnRepairBuilding;
		private PanelContainer _upgradeDiagnosticBanner;
		private Label _upgradeDiagnosticLabel;
		private double _prereqRedGlowTimer = 0.0;
		private double _resourceRedGlowTimer = 0.0;
		private float _alertPulseClock = 0.0f;

		public override void _Ready()
		{
			Instance = this;
			SetAnchorsPreset(LayoutPreset.FullRect);
			MouseFilter = MouseFilterEnum.Ignore;

			GetViewport().Connect("size_changed", Callable.From(RecenterModalOnScreen));

			BuildBaseModalFrame();
			BuildConfirmOverlay();
			Visible = false;
		}

		public override void _ExitTree()
		{
			if (Instance == this) Instance = null;
			base._ExitTree();
		}

		public override void _Process(double delta)
		{
			if (!Visible || BaseHUD.Instance == null) return;

			RecenterModalOnScreen();

			// 1. Live Ticker for Orbital Shipyard (hub_mil)
			if (ActiveBuildingId == "hub_mil")
			{
				UpdateOrbitalShipyardLiveTimers();
			}
			// 2. Live Ticker for Garrison Armory (hub_arm)
			else if (ActiveBuildingId == "hub_arm")
			{
				UpdateGarrisonArmoryLiveTimers();
			}
			// 3. Live Ticker for Fleet Station (hub_flt) in-flight missions
			else if (ActiveBuildingId == "hub_flt")
			{
				foreach (var kvp in _fleetTransitLocLabels)
				{
					var lbl = kvp.Value;
					if (!IsInstanceValid(lbl)) continue;
					var fleet = BaseHUD.Instance.SavedFleets.Find(x => x.Id == kvp.Key);
					if (fleet != null && fleet.Status == "IN_TRANSIT")
					{
						lbl.Text = $"{fleet.Protocol} ➔ {fleet.TargetCoord} ({GameMath.FormatTime((int)Math.Ceiling(fleet.EtaSecondsLeft))})";
					}
					else if (fleet != null)
					{
						RefreshInspector();
						break;
					}
				}
			}
			// 3B. Live Ticker for Research Directorate (hub_rsh) active research timer
			else if (ActiveBuildingId == "hub_rsh" && _liveResearchDirectorateTimerLabel != null && IsInstanceValid(_liveResearchDirectorateTimerLabel))
			{
				int endSec = BaseHUD.Instance.BuildingLevels.GetValueOrDefault(GameMath.ResearchDirectorateShieldTimerKey, 0);
				if (endSec > 0)
				{
					long rem = Math.Max(0, endSec - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
					int curRshLvl = Math.Clamp(BaseHUD.Instance.BuildingLevels.GetValueOrDefault(GameMath.ResearchDirectorateShieldKey, 0), 0, 20);
					_liveResearchDirectorateTimerLabel.Text = $"⚙️ RESEARCHING LEVEL {curRshLvl + 1}/20 — TIME REMAINING: {GameMath.FormatTime(rem)}";
					if (rem <= 0) RefreshInspector();
				}
				else
				{
					RefreshInspector();
				}
			}

			// 4. Live Ticker for Active Construction Pipeline Duration at bottom of any building inspector
			if (_liveUpgradeDurationLabel != null && IsInstanceValid(_liveUpgradeDurationLabel))
			{
				double currentTotalPipeline = BaseHUD.Instance.GetTotalPipelineDurationSeconds();
				string pipelineSuffix = currentTotalPipeline > 0
					? $"   ·   Active Queue Total: {GameMath.FormatTime((int)Math.Ceiling(currentTotalPipeline))}"
					: "";
				_liveUpgradeDurationLabel.Text = $"⏱ Construction Duration: {GameMath.FormatTime(_liveUpgradeBaseDurationSec)}{pipelineSuffix}";
			}

			// 5. Live Resource Affordability & 10-Second Pulsing Red Highlight Glow
			UpdateLiveUpgradeAffordabilityAndGlow(delta);
		}

		private void UpdateLiveUpgradeAffordabilityAndGlow(double delta)
		{
			if (BaseHUD.Instance == null) return;

			_alertPulseClock += (float)delta;
			float fastPulse = (Mathf.Sin(_alertPulseClock * 6.5f) + 1.0f) * 0.5f;

			// A. 10-Second Pulsing Red Glow on Construction Matrix (Unmet Building Prerequisites)
			if (_prereqRedGlowTimer > 0.0)
			{
				_prereqRedGlowTimer = Math.Max(0.0, _prereqRedGlowTimer - delta);
				if (_prereqCardStyle != null && IsInstanceValid(_prereqCardPanel))
				{
					Color brightRed = new Color("#EF4444").Lerp(new Color("#FCA5A5"), fastPulse);
					_prereqCardStyle.BorderColor = brightRed;
					_prereqCardStyle.BorderWidthLeft = 2;
					_prereqCardStyle.BorderWidthRight = 2;
					_prereqCardStyle.BorderWidthTop = 2;
					_prereqCardStyle.BorderWidthBottom = 2;
					_prereqCardStyle.BgColor = new Color(0.08f, 0.02f, 0.03f, 0.96f).Lerp(new Color(0.26f, 0.04f, 0.06f, 0.98f), fastPulse);
					_prereqCardStyle.ShadowColor = new Color(0.95f, 0.15f, 0.15f, Mathf.Lerp(0.30f, 0.85f, fastPulse));
					_prereqCardStyle.ShadowSize = (int)Mathf.Lerp(8f, 20f, fastPulse);

					if (_prereqTitleLabel != null && IsInstanceValid(_prereqTitleLabel))
					{
						_prereqTitleLabel.Text = $"⚠️  CONSTRUCTION MATRIX (LVL {_currentUpgradeTargetLevel} REQUIREMENTS UNMET — {(int)Math.Ceiling(_prereqRedGlowTimer)}s):";
						_prereqTitleLabel.Modulate = new Color("#FCA5A5").Lerp(Colors.White, fastPulse);
					}
				}
			}
			else if (_prereqCardStyle != null && IsInstanceValid(_prereqCardPanel))
			{
				_prereqCardStyle.BorderColor = _currentAllPrereqsMet ? new Color(0.13f, 0.77f, 0.36f, 0.65f) : new Color(0.93f, 0.27f, 0.27f, 0.75f);
				_prereqCardStyle.BorderWidthLeft = 1;
				_prereqCardStyle.BorderWidthRight = 1;
				_prereqCardStyle.BorderWidthTop = 1;
				_prereqCardStyle.BorderWidthBottom = 1;
				_prereqCardStyle.BgColor = new Color(0.02f, 0.04f, 0.06f, 0.92f);
				_prereqCardStyle.ShadowSize = 0;

				if (_prereqTitleLabel != null && IsInstanceValid(_prereqTitleLabel))
				{
					_prereqTitleLabel.Text = $"CONSTRUCTION MATRIX (LVL {_currentUpgradeTargetLevel} REQUIREMENTS):";
					_prereqTitleLabel.Modulate = _currentAllPrereqsMet ? new Color("#22C55E") : new Color("#EF4444");
				}
			}

			// B. 10-Second Pulsing Red Glow on Required Resources Box (Missing Resources)
			if (_resourceRedGlowTimer > 0.0)
			{
				_resourceRedGlowTimer = Math.Max(0.0, _resourceRedGlowTimer - delta);
				if (_resourceCardStyle != null && IsInstanceValid(_resourceCardPanel))
				{
					Color brightRed = new Color("#EF4444").Lerp(new Color("#FCA5A5"), fastPulse);
					_resourceCardStyle.BorderColor = brightRed;
					_resourceCardStyle.BorderWidthLeft = 2;
					_resourceCardStyle.BorderWidthRight = 2;
					_resourceCardStyle.BorderWidthTop = 2;
					_resourceCardStyle.BorderWidthBottom = 2;
					_resourceCardStyle.BgColor = new Color(0.08f, 0.02f, 0.03f, 0.96f).Lerp(new Color(0.24f, 0.04f, 0.06f, 0.98f), fastPulse);
					_resourceCardStyle.ShadowColor = new Color(0.95f, 0.15f, 0.15f, Mathf.Lerp(0.30f, 0.85f, fastPulse));
					_resourceCardStyle.ShadowSize = (int)Mathf.Lerp(8f, 18f, fastPulse);
				}
			}
			else if (_resourceCardStyle != null && IsInstanceValid(_resourceCardPanel))
			{
				_resourceCardStyle.BorderColor = new Color(0.2f, 0.3f, 0.4f, 0.55f);
				_resourceCardStyle.BorderWidthLeft = 1;
				_resourceCardStyle.BorderWidthRight = 1;
				_resourceCardStyle.BorderWidthTop = 1;
				_resourceCardStyle.BorderWidthBottom = 1;
				_resourceCardStyle.BgColor = new Color(0.02f, 0.04f, 0.06f, 0.92f);
				_resourceCardStyle.ShadowSize = 0;
			}

			// B2. Live Repair Cost Labels & Repair Button State (When Building Has Structural HP Damage)
			if (_isActiveBuildingDamaged && _lblLiveRepairE != null && IsInstanceValid(_lblLiveRepairE))
			{
				long curE = (long)BaseHUD.Instance.ResE;
				long curI = (long)BaseHUD.Instance.ResI;
				long curT = (long)BaseHUD.Instance.ResT;
				long curH = (long)BaseHUD.Instance.ResH3;

				UpdateSingleCostLabel(_lblLiveRepairE, "⚡ Energy", _currentRepairMetrics.costE, curE);
				UpdateSingleCostLabel(_lblLiveRepairI, "⛏ Iron", _currentRepairMetrics.costI, curI);
				UpdateSingleCostLabel(_lblLiveRepairT, "💎 Titanium", _currentRepairMetrics.costT, curT);
				UpdateSingleCostLabel(_lblLiveRepairH3, "⛽ Helium-3", _currentRepairMetrics.costH3, curH);

				bool canAffordRepair = curE >= _currentRepairMetrics.costE &&
									   curI >= _currentRepairMetrics.costI &&
									   curT >= _currentRepairMetrics.costT &&
									   curH >= _currentRepairMetrics.costH3;

				if (_btnRepairBuilding != null && IsInstanceValid(_btnRepairBuilding))
				{
					string effStr = GameMath.FormatEffectiveLevel(_currentRepairMetrics.effectiveLevel);
					_btnRepairBuilding.Text = canAffordRepair
						? $"🔧 REPAIR +{_currentRepairMetrics.damagedHp:N0} HP NOW (RESTORE LVL {effStr} ➔ LVL {ActiveBuildingLevel})"
						: $"⚠️ INSUFFICIENT RESOURCES TO REPAIR +{_currentRepairMetrics.damagedHp:N0} HP (TAP FOR DETAILS)";
					ApplyButtonStyle(_btnRepairBuilding, !canAffordRepair, new Color("#EA580C"));
				}
			}

			// C. Live Per-Resource Green/Red Indicator Labels & Button Visual States
			if (_lblLiveCostE != null && IsInstanceValid(_lblLiveCostE))
			{
				long curE = (long)BaseHUD.Instance.ResE;
				long curI = (long)BaseHUD.Instance.ResI;
				long curT = (long)BaseHUD.Instance.ResT;
				long curH = (long)BaseHUD.Instance.ResH3;

				UpdateSingleCostLabel(_lblLiveCostE, "⚡ Energy", _currentUpgradeCost.e, curE);
				UpdateSingleCostLabel(_lblLiveCostI, "⛏ Iron", _currentUpgradeCost.i, curI);
				UpdateSingleCostLabel(_lblLiveCostT, "💎 Titanium", _currentUpgradeCost.t, curT);
				UpdateSingleCostLabel(_lblLiveCostH3, "⛽ Helium-3", _currentUpgradeCost.h, curH);

				bool hasResources = curE >= _currentUpgradeCost.e &&
									curI >= _currentUpgradeCost.i &&
									curT >= _currentUpgradeCost.t &&
									curH >= _currentUpgradeCost.h;
				bool hasGGold = BaseHUD.Instance.GGold >= 25;
				bool isEngineBusy = BaseHUD.Instance.ActiveBuild != null;
				bool isQueueFull = BaseHUD.Instance.BuildQueue.Count >= 20;

				if (_btnStartUpgrade != null && IsInstanceValid(_btnStartUpgrade))
				{
					bool canStartNow = !_isActiveBuildingDamaged && _currentAllPrereqsMet && hasResources && !isEngineBusy;
					if (_isActiveBuildingDamaged)
						_btnStartUpgrade.Text = $"🔧 REPAIR REQUIRED BEFORE UPGRADING TO LVL {_currentUpgradeTargetLevel}";
					else if (isEngineBusy)
						_btnStartUpgrade.Text = "⚙ ENGINEERING DOCK BUSY (USE QUEUE BELOW)";
					else if (!_currentAllPrereqsMet)
						_btnStartUpgrade.Text = $"🔒 REQUIREMENTS UNMET FOR LVL {_currentUpgradeTargetLevel} (TAP TO HIGHLIGHT)";
					else if (!hasResources)
						_btnStartUpgrade.Text = $"⚠️ INSUFFICIENT RESOURCES FOR LVL {_currentUpgradeTargetLevel} (TAP FOR DETAILS)";
					else
						_btnStartUpgrade.Text = $"▲ START UPGRADE TO LVL {_currentUpgradeTargetLevel}";

					ApplyButtonStyle(_btnStartUpgrade, !canStartNow, new Color(0.10f, 0.68f, 0.32f, 0.95f));
				}

				if (_btnQueueUpgrade != null && IsInstanceValid(_btnQueueUpgrade))
				{
					bool canQueueNow = !_isActiveBuildingDamaged && _currentAllPrereqsMet && hasResources && hasGGold && isEngineBusy && !isQueueFull;
					if (_isActiveBuildingDamaged)
						_btnQueueUpgrade.Text = $"🔧 REPAIR REQUIRED BEFORE QUEUING LVL {_currentUpgradeTargetLevel}";
					else if (!isEngineBusy)
						_btnQueueUpgrade.Text = "DOCK IDLE (USE START UPGRADE ABOVE)";
					else if (isQueueFull)
						_btnQueueUpgrade.Text = "QUEUE FULL (MAX 20)";
					else if (!_currentAllPrereqsMet)
						_btnQueueUpgrade.Text = $"🔒 REQUIREMENTS UNMET FOR LVL {_currentUpgradeTargetLevel} (TAP TO HIGHLIGHT)";
					else if (!hasResources)
						_btnQueueUpgrade.Text = $"⚠️ INSUFFICIENT RESOURCES FOR LVL {_currentUpgradeTargetLevel} (TAP FOR DETAILS)";
					else if (!hasGGold)
						_btnQueueUpgrade.Text = $"⚠️ NEED 25 GGOLD TO QUEUE LVL {_currentUpgradeTargetLevel} (TAP FOR DETAILS)";
					else
						_btnQueueUpgrade.Text = $"➕ QUEUE LVL {_currentUpgradeTargetLevel} (+{GameMath.FormatTime(_currentUpgradeDurationSec)} · 25 GGOLD)";

					ApplyButtonStyle(_btnQueueUpgrade, !canQueueNow, new Color(0.04f, 0.48f, 0.85f, 0.95f));
				}
			}
		}

		private void UpdateSingleCostLabel(Label lbl, string resName, long requiredAmt, long currentAmt)
		{
			if (lbl == null || !IsInstanceValid(lbl)) return;
			if (requiredAmt <= 0)
			{
				lbl.Text = $"✓ {resName}: 0";
				lbl.Modulate = new Color("#64748B");
				return;
			}

			if (currentAmt >= requiredAmt)
			{
				lbl.Text = $"✓ {resName}: {requiredAmt:N0}";
				lbl.Modulate = new Color("#4ADE80");
			}
			else
			{
				long missing = requiredAmt - currentAmt;
				lbl.Text = $"✕ {resName}: {requiredAmt:N0} (Need +{missing:N0})";
				lbl.Modulate = new Color("#F87171");
			}
		}

		private void UpdateOrbitalShipyardLiveTimers()
		{
			var bays = BaseHUD.Instance?.ActiveUnitBays;
			var inv = BaseHUD.Instance?.UnitInventory;

			if (_shipyardLiveBaysBox != null && IsInstanceValid(_shipyardLiveBaysBox))
			{
				// Rebuild the active bays monitor lines cleanly so timers count down every second
				foreach (Node c in _shipyardLiveBaysBox.GetChildren()) c.QueueFree();

				if (bays == null || bays.Count == 0)
				{
					Label idleLbl = new Label
					{
						Text = "All 9 Unit Assembly Bays are currently IDLE. Queue multiple unit types below to assemble simultaneously!",
						Modulate = new Color("#94A3B8"),
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					};
					idleLbl.AddThemeFontSizeOverride("font_size", 12);
					_shipyardLiveBaysBox.AddChild(idleLbl);
				}
				else
				{
					foreach (var kvp in bays)
					{
						var job = kvp.Value;
						var uDef = GameData.Units.Find(x => x.Id == kvp.Key);
						string uName = uDef != null ? uDef.Name.ToUpper() : kvp.Key.ToUpper();
						double totalSec = Math.Max(0, job.NextUnitTimerSec) + Math.Max(0, job.RemainingCount - 1) * job.SingleUnitDurationSec;

						Label bayLine = new Label
						{
							Text = $"● {uName}: {job.RemainingCount:N0}x Queued  |  Next Unit Drop: {GameMath.FormatTime((int)Math.Ceiling(job.NextUnitTimerSec))}  |  Bay Total: {GameMath.FormatTime((int)Math.Ceiling(totalSec))}",
							Modulate = new Color("#4ADE80")
						};
						bayLine.AddThemeFontSizeOverride("font_size", 13);
						_shipyardLiveBaysBox.AddChild(bayLine);
					}
				}
			}

			foreach (var unit in GameData.Units)
			{
				int garrisonCount = inv != null ? inv.GetValueOrDefault(unit.Id, 0) : 0;
				var activeJob = (bays != null && bays.TryGetValue(unit.Id, out var j)) ? j : null;
				bool isUnlocked = ActiveBuildingLevel >= unit.ShipyardReqLvl;

				if (_shipyardUnitTitleLabels.TryGetValue(unit.Id, out Label titleLbl) && IsInstanceValid(titleLbl))
				{
					titleLbl.Text = $"{unit.Name.ToUpper()}   ·   GARRISON: {garrisonCount:N0}";
				}

				if (_shipyardUnitStatusLabels.TryGetValue(unit.Id, out Label statusLbl) && IsInstanceValid(statusLbl))
				{
					if (activeJob != null && activeJob.RemainingCount > 0)
					{
						double totalBaySec = Math.Max(0, activeJob.NextUnitTimerSec) + Math.Max(0, activeJob.RemainingCount - 1) * activeJob.SingleUnitDurationSec;
						statusLbl.Text = $"⚙️ BUILDING: {activeJob.RemainingCount}x (Next: {GameMath.FormatTime((int)Math.Ceiling(activeJob.NextUnitTimerSec))} · Total: {GameMath.FormatTime((int)Math.Ceiling(totalBaySec))})";
						statusLbl.Modulate = new Color("#FBBF24");
					}
					else
					{
						float srvSpeed = BaseHUD.Instance != null ? Math.Max(1.0f, BaseHUD.Instance.ServerSpeed) : Math.Max(1.0f, SupabaseService.CachedServerSpeed);
						int scaledSec = Math.Max(1, (int)Math.Round(unit.AssemblyTimeSec / srvSpeed));
						statusLbl.Text = $"REQ LVL {unit.ShipyardReqLvl} · ⏱ {scaledSec}s/unit ({srvSpeed:0.#}x)";
						statusLbl.Modulate = isUnlocked ? new Color("#22C55E") : new Color("#EF4444");
					}
				}
			}
		}

		private void UpdateGarrisonArmoryLiveTimers()
		{
			var inv = BaseHUD.Instance?.UnitInventory;
			var bays = BaseHUD.Instance?.ActiveUnitBays;

			long totalUnits = 0, totalAtk = 0, totalDef = 0, totalHp = 0, totalCargo = 0;
			double totalH3Upkeep = 0.0;
			foreach (var u in GameData.Units)
			{
				int count = inv != null ? inv.GetValueOrDefault(u.Id, 0) : 0;
				totalUnits += count;
				totalAtk += (long)u.Atk * count;
				totalDef += (long)u.Def * count;
				totalHp += (long)u.Hp * count;
				totalCargo += (long)u.Cargo * count;
				totalH3Upkeep += (double)u.H3Drain * count;

				if (_armoryUnitQtyLabels.TryGetValue(u.Id, out Label qLbl) && IsInstanceValid(qLbl))
				{
					var activeBay = (bays != null && bays.TryGetValue(u.Id, out var bJob)) ? bJob : null;
					qLbl.Text = activeBay != null
						? $"GARRISON: {count:N0}   (+{activeBay.RemainingCount} BUILDING · {GameMath.FormatTime((int)Math.Ceiling(activeBay.NextUnitTimerSec))})"
						: $"GARRISON: {count:N0} UNITS";
					qLbl.Modulate = count > 0 ? new Color("#4ADE80") : new Color("#64748B");
				}
			}

			if (_armoryLiveSummaryLabel != null && IsInstanceValid(_armoryLiveSummaryLabel))
			{
				_armoryLiveSummaryLabel.Text = $"Total Garrisoned Units: {totalUnits:N0}   |   Combined Cargo Cap: {totalCargo:N0}\nTotal ATK: {totalAtk:N0}   |   Total DEF: {totalDef:N0}   |   Total HP: {totalHp:N0}\nHourly Garrison Fuel Upkeep: -{BaseHUD.FormatH3Rate(totalH3Upkeep, false)} H3/h";
			}

			if (_armoryLiveBaysBox != null && IsInstanceValid(_armoryLiveBaysBox))
			{
				foreach (Node c in _armoryLiveBaysBox.GetChildren()) c.QueueFree();
				if (bays != null && bays.Count > 0)
				{
					foreach (var kvp in bays)
					{
						var u = GameData.Units.Find(x => x.Id == kvp.Key);
						string name = u != null ? u.Name.ToUpper() : kvp.Key.ToUpper();
						Label l = new Label
						{
							Text = $"● {name}: +{kvp.Value.RemainingCount:N0} assembling (Next unit drops in {GameMath.FormatTime((int)Math.Ceiling(kvp.Value.NextUnitTimerSec))})",
							Modulate = new Color("#00F0FF")
						};
						l.AddThemeFontSizeOverride("font_size", 12);
						_armoryLiveBaysBox.AddChild(l);
					}
				}
			}
		}

		private void RecenterModalOnScreen()
		{
			if (_modalChassis == null) return;
			Vector2 vpSize = GetViewportRect().Size;
			if (vpSize.X <= 0 || vpSize.Y <= 0) return;

			// Wide Command Console framing:
			// - Top Cockpit Bar (Y: 0..68) remains permanently visible -> topSafeY = 74
			// - Bottom Navigation Dock (Y: vpSize.Y - 72..vpSize.Y) -> bottomSafeY = vpSize.Y - 78
			// - Left Fleet & Radar Dock and Right Engineering Bay auto-hide while any primary modal is open,
			//   giving the facility inspector up to 92% of viewport width (up to 1120px) for big, crisp mobile text!
			float topSafeY = 74f;
			float bottomSafeY = Math.Max(topSafeY + 340f, vpSize.Y - 78f);
			float maxSafeH = bottomSafeY - topSafeY;

			float desiredMaxW = 1100f;
			float chassisW = Mathf.Min(desiredMaxW, Math.Max(480f, vpSize.X * 0.90f));
			float chassisX = (vpSize.X - chassisW) * 0.5f;

			float chassisH = maxSafeH;
			float chassisY = topSafeY + (maxSafeH - chassisH) * 0.5f;

			_modalChassis.AnchorLeft = 0f;
			_modalChassis.AnchorTop = 0f;
			_modalChassis.AnchorRight = 0f;
			_modalChassis.AnchorBottom = 0f;
			_modalChassis.CustomMinimumSize = new Vector2(chassisW, chassisH);
			_modalChassis.Size = new Vector2(chassisW, chassisH);
			_modalChassis.Position = new Vector2(chassisX, chassisY);

			if (_mainLayout != null)
			{
				float padX = 16f;
				float padY = 12f;
				float innerW = Math.Max(320f, chassisW - padX * 2f);
				float innerH = Math.Max(260f, chassisH - padY * 2f);

				_mainLayout.AnchorLeft = 0f;
				_mainLayout.AnchorTop = 0f;
				_mainLayout.AnchorRight = 0f;
				_mainLayout.AnchorBottom = 0f;
				_mainLayout.GrowHorizontal = GrowDirection.End;
				_mainLayout.GrowVertical = GrowDirection.End;
				_mainLayout.Position = new Vector2(padX, padY);
				_mainLayout.CustomMinimumSize = new Vector2(innerW, innerH);
				_mainLayout.Size = new Vector2(innerW, innerH);
			}
		}

		private void BuildBaseModalFrame()
		{
			foreach (Node child in GetChildren())
			{
				child.QueueFree();
			}

			_dimBackdrop = new ColorRect
			{
				Name = "DimBackdrop",
				Color = new Color(0f, 0f, 0f, 0.65f),
				MouseFilter = MouseFilterEnum.Stop
			};
			_dimBackdrop.SetAnchorsPreset(LayoutPreset.FullRect);
			_dimBackdrop.GuiInput += (InputEvent @event) =>
			{
				if (@event is InputEventMouseButton mb && mb.IsPressed() && mb.ButtonIndex == MouseButton.Left)
				{
					if (_confirmOverlay == null || !_confirmOverlay.Visible)
					{
						CloseInspector();
					}
				}
			};
			AddChild(_dimBackdrop);

			_modalChassis = new Panel
			{
				Name = "ModalChassis",
				CustomMinimumSize = new Vector2(650, 560),
				ClipContents = true,
				MouseFilter = MouseFilterEnum.Stop
			};

			StyleBoxFlat chassisStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.06f, 0.11f, 0.98f),
				BorderColor = new Color("#00F0FF"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 10,
				CornerRadiusTopRight = 10,
				CornerRadiusBottomLeft = 10,
				CornerRadiusBottomRight = 10,
				ShadowColor = new Color(0f, 0.94f, 1f, 0.30f),
				ShadowSize = 20
			};
			_modalChassis.AddThemeStyleboxOverride("panel", chassisStyle);
			AddChild(_modalChassis);

			_mainLayout = new VBoxContainer
			{
				GrowHorizontal = GrowDirection.End,
				GrowVertical = GrowDirection.End,
				ClipContents = true
			};
			_mainLayout.AddThemeConstantOverride("separation", 8);
			_modalChassis.AddChild(_mainLayout);

			HBoxContainer headerRow = new HBoxContainer();
			headerRow.Alignment = BoxContainer.AlignmentMode.Center;
			headerRow.AddThemeConstantOverride("separation", 10);

			_lblHeaderTitle = new Label
			{
				Text = "FACILITY INSPECTOR",
				Modulate = Colors.White,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			_lblHeaderTitle.AddThemeFontSizeOverride("font_size", 20);
			_lblHeaderTitle.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblHeaderTitle.AddThemeConstantOverride("outline_size", 3);

			_lblLevelBadge = new Label
			{
				Text = "LVL 1",
				Modulate = new Color("#00F0FF")
			};
			_lblLevelBadge.AddThemeFontSizeOverride("font_size", 17);
			_lblLevelBadge.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblLevelBadge.AddThemeConstantOverride("outline_size", 2);

			_btnClose = new Button
			{
				Text = " ✕ CLOSE ",
				CustomMinimumSize = new Vector2(96, 38),
				MouseFilter = MouseFilterEnum.Stop
			};
			_btnClose.AddThemeFontSizeOverride("font_size", 15);
			StyleBoxFlat closeStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.25f, 0.06f, 0.08f, 0.92f),
				BorderColor = new Color("#EF4444"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			_btnClose.AddThemeStyleboxOverride("normal", closeStyle);
			_btnClose.Pressed += CloseInspector;

			headerRow.AddChild(_lblHeaderTitle);
			headerRow.AddChild(_lblLevelBadge);
			headerRow.AddChild(_btnClose);
			_mainLayout.AddChild(headerRow);

			_mainLayout.AddChild(new HSeparator());

			_scrollContainer = new ScrollContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
				ClipContents = true
			};
			_mainLayout.AddChild(_scrollContainer);

			_contentStack = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			_contentStack.AddThemeConstantOverride("separation", 8);
			_scrollContainer.AddChild(_contentStack);

			RecenterModalOnScreen();
		}

		private void BuildConfirmOverlay()
		{
			_confirmOverlay = new ColorRect
			{
				Name = "ConfirmOverlay",
				Color = new Color(0f, 0f, 0f, 0.85f),
				Visible = false,
				ZIndex = 100,
				MouseFilter = MouseFilterEnum.Stop
			};
			_confirmOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
			AddChild(_confirmOverlay);

			Panel confirmBox = new Panel
			{
				CustomMinimumSize = new Vector2(480, 240)
			};
			confirmBox.SetAnchorsPreset(LayoutPreset.Center);
			
			StyleBoxFlat boxStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.06f, 0.09f, 0.14f, 1.0f),
				BorderColor = new Color("#FBBF24"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 10,
				CornerRadiusTopRight = 10,
				CornerRadiusBottomLeft = 10,
				CornerRadiusBottomRight = 10,
				ShadowColor = new Color(0f, 0f, 0f, 0.6f),
				ShadowSize = 20
			};
			confirmBox.AddThemeStyleboxOverride("panel", boxStyle);
			_confirmOverlay.AddChild(confirmBox);

			VBoxContainer vBox = new VBoxContainer();
			vBox.SetAnchorsPreset(LayoutPreset.FullRect);
			vBox.OffsetLeft = 24;
			vBox.OffsetRight = -24;
			vBox.OffsetTop = 22;
			vBox.OffsetBottom = -22;
			vBox.Alignment = BoxContainer.AlignmentMode.Center;
			vBox.AddThemeConstantOverride("separation", 16);
			confirmBox.AddChild(vBox);

			_lblConfirmTitle = new Label
			{
				Text = "AUTHORIZE TRANSACTION?",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color("#FBBF24")
			};
			_lblConfirmTitle.AddThemeFontSizeOverride("font_size", 18);
			vBox.AddChild(_lblConfirmTitle);

			_lblConfirmMsg = new Label
			{
				Text = "This action will consume Galaxy Gold.",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			_lblConfirmMsg.AddThemeFontSizeOverride("font_size", 14);
			vBox.AddChild(_lblConfirmMsg);

			HBoxContainer btnRow = new HBoxContainer();
			btnRow.Alignment = BoxContainer.AlignmentMode.Center;
			btnRow.AddThemeConstantOverride("separation", 24);

			Button btnCancel = new Button { Text = "CANCEL", CustomMinimumSize = new Vector2(150, 46) };
			btnCancel.AddThemeFontSizeOverride("font_size", 14);
			btnCancel.Modulate = new Color("#94A3B8");
			btnCancel.Pressed += () => _confirmOverlay.Visible = false;
			btnRow.AddChild(btnCancel);

			Button btnConfirm = new Button { Text = "CONFIRM", CustomMinimumSize = new Vector2(150, 46) };
			btnConfirm.AddThemeFontSizeOverride("font_size", 14);
			btnConfirm.Modulate = new Color("#22C55E");
			btnConfirm.Pressed += () => 
			{
				_confirmOverlay.Visible = false;
				_pendingConfirmAction?.Invoke();
				_pendingConfirmAction = null;
			};
			btnRow.AddChild(btnConfirm);

			vBox.AddChild(btnRow);
		}

		private void ShowConfirmDialog(string title, string message, Action onConfirm)
		{
			_lblConfirmTitle.Text = title;
			_lblConfirmMsg.Text = message;
			_pendingConfirmAction = onConfirm;
			_confirmOverlay.Visible = true;
		}

		public void InspectBuilding(
			string buildingId,
			string buildingName,
			int currentLevel,
			Color themeColor,
			string districtCode = "",
			int slotIndex = 0,
			int[] districtLevels = null,
			int industrialCoreLevel = 1,
			Dictionary<string, int> allBuildingLevels = null)
		{
			ActiveBuildingId = buildingId;
			ActiveBuildingName = buildingName;
			ActiveBuildingLevel = currentLevel;
			ActiveBuildingColor = themeColor;
			ActiveDistrictCode = districtCode;
			ActiveSlotIndex = slotIndex;
			ActiveDistrictLevels = districtLevels ?? new int[0];
			CurrentIndustrialCoreLevel = industrialCoreLevel;
			AllBuildingLevels = allBuildingLevels ?? new Dictionary<string, int>();

			if (_lblHeaderTitle != null) _lblHeaderTitle.Text = buildingName.ToUpper();
			if (_lblLevelBadge != null)
			{
				bool isUpgradable = GameMath.IsBuildingUpgradable(buildingId);
				var hpHeader = GameMath.GetStructureHealthDetails(buildingId, currentLevel, AllBuildingLevels);
				string effLvlStr = GameMath.FormatEffectiveLevel(hpHeader.effectiveLevel);
				_lblLevelBadge.Text = isUpgradable
					? (hpHeader.isDamaged ? $"LVL {effLvlStr} / {currentLevel} (DAMAGED)" : $"LVL {currentLevel} ({hpHeader.maxHp:N0} HP)")
					: "FACILITY";
				_lblLevelBadge.Modulate = hpHeader.isDamaged ? new Color("#EF4444") : themeColor;
			}

			if (_modalChassis != null)
			{
				RecenterModalOnScreen();

				StyleBoxFlat style = new StyleBoxFlat
				{
					BgColor = new Color(0.04f, 0.07f, 0.11f, 0.98f),
					BorderColor = themeColor,
					BorderWidthLeft = 2,
					BorderWidthRight = 2,
					BorderWidthTop = 2,
					BorderWidthBottom = 2,
					CornerRadiusTopLeft = 8,
					CornerRadiusTopRight = 8,
					CornerRadiusBottomLeft = 8,
					CornerRadiusBottomRight = 8,
					ShadowColor = new Color(themeColor.R, themeColor.G, themeColor.B, 0.25f),
					ShadowSize = 20
				};
				_modalChassis.AddThemeStyleboxOverride("panel", style);
			}

			foreach (Node child in _contentStack.GetChildren())
			{
				child.QueueFree();
			}
			_unitQtyInputs.Clear();
			_shipyardLiveBaysBox = null;
			_shipyardUnitTitleLabels.Clear();
			_shipyardUnitStatusLabels.Clear();
			_armoryLiveSummaryLabel = null;
			_armoryLiveBaysBox = null;
			_armoryUnitQtyLabels.Clear();
			_fleetTransitLocLabels.Clear();
			_liveUpgradeDurationLabel = null;
			_liveResearchDirectorateTimerLabel = null;
			_prereqCardPanel = null;
			_prereqCardStyle = null;
			_prereqTitleLabel = null;
			_resourceCardPanel = null;
			_resourceCardStyle = null;
			_lblLiveCostE = _lblLiveCostI = _lblLiveCostT = _lblLiveCostH3 = null;
			_btnStartUpgrade = null;
			_btnQueueUpgrade = null;
			_isActiveBuildingDamaged = false;
			_repairCardPanel = null;
			_repairCardStyle = null;
			_lblLiveRepairE = _lblLiveRepairI = _lblLiveRepairT = _lblLiveRepairH3 = null;
			_btnRepairBuilding = null;
			_upgradeDiagnosticBanner = null;
			_upgradeDiagnosticLabel = null;
			_prereqRedGlowTimer = 0.0;
			_resourceRedGlowTimer = 0.0;

			// Calculate Simulated Future State
			int simActiveLevel = ActiveBuildingLevel;
			Dictionary<string, int> simLevels = new Dictionary<string, int>(AllBuildingLevels);

			if (BaseHUD.Instance != null)
			{
				var fullQueue = new List<GameMath.BuildQueueItem>();
				if (BaseHUD.Instance.ActiveBuild != null) fullQueue.Add(BaseHUD.Instance.ActiveBuild);
				fullQueue.AddRange(BaseHUD.Instance.BuildQueue);

				simLevels = GameMath.GetSimulatedLevels(AllBuildingLevels, fullQueue);
				simActiveLevel = simLevels.GetValueOrDefault(ActiveBuildingId, ActiveBuildingLevel);
			}

			if (buildingId == "hub_mil") BuildOrbitalShipyardMenu();
			else if (buildingId == "hub_arm") BuildGarrisonArmoryMenu();
			else if (buildingId == "hub_flt") BuildFleetStationMenu();
			else if (buildingId == "hub_silo") BuildSiloMenu();
			else if (buildingId == "hub_ahq") BuildAllianceHQMenu();
			else if (buildingId == "hub_shd") BuildPlanetaryShieldMenu();
			else if (buildingId == "hub_rsh") BuildResearchDirectorateMenu();
			else if (buildingId.StartsWith("dist_h3_")) BuildHelium3DistilleryMenu();
			else if (buildingId == "hub_rng") BuildDeepRadarMenu();
			else if (buildingId == "hub_mgd") BuildGalaxyGoldExchangeMenu();
			else if (buildingId == "hub_cmd") BuildIndustrialCoreMenu();
			else if (buildingId.StartsWith("dist_")) BuildStandardSubMineMenu();
			else BuildGenericFacilityMenu();

			if (GameMath.IsBuildingUpgradable(buildingId))
			{
				int maxBuildingLvl = GameMath.GetBuildingMaxLevel(buildingId);
				AddBlueprintUpgradeSection(buildingId.StartsWith("dist_"), maxBuildingLvl, simLevels, simActiveLevel);
			}

			BaseHUD.Instance?.CloseAllModalsExcept("INSPECTOR");
			Visible = true;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();
		}

		private void BuildSiloMenu()
		{
			long currentCap = GameMath.CalcSiloCapacity(ActiveBuildingLevel);
			long nextCap = GameMath.CalcSiloCapacity(ActiveBuildingLevel + 1);
			float vaultProt = GameMath.GetSiloVaultProtection(ActiveBuildingLevel);
			var hpInfo = GameMath.GetStructureHealthDetails(ActiveBuildingId, ActiveBuildingLevel, AllBuildingLevels);
			int pop = GameMath.GetStructurePop("silo", ActiveBuildingLevel);

			PanelContainer infoBox = CreateCardPanel(0);
			VBoxContainer infoContent = CreateCardContainer(infoBox);

			Label lblTitle = new Label { Text = "PRESSURIZED CONTAINMENT VAULT STATUS:", Modulate = new Color("#00F0FF"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			lblTitle.AddThemeFontSizeOverride("font_size", 16);

			Label lblCap = new Label
			{
				Text = $"Global Capacity: {currentCap:N0} Units (15h Peak Production)\nNext Level: {nextCap:N0} Units (+{(nextCap - currentCap):N0})\nRaid Vault Security: {(vaultProt * 100):F1}% Protected | Health: {hpInfo.currentHp:N0}/{hpInfo.maxHp:N0} HP ({hpInfo.healthPercent}%) | Pop: {pop}",
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblCap.AddThemeFontSizeOverride("font_size", 15);

			infoContent.AddChild(lblTitle);
			infoContent.AddChild(lblCap);
			_contentStack.AddChild(infoBox);
		}

		private void BuildAllianceHQMenu()
		{
			var hpInfo = GameMath.GetStructureHealthDetails(ActiveBuildingId, ActiveBuildingLevel, AllBuildingLevels);
			int pop = GameMath.GetStructurePop("ahq", ActiveBuildingLevel);
			int coreLvl = BaseHUD.Instance != null
				? BaseHUD.Instance.BuildingLevels.GetValueOrDefault("hub_cmd", CurrentIndustrialCoreLevel)
				: CurrentIndustrialCoreLevel;
			int memberCap = Math.Clamp(ActiveBuildingLevel, 0, 20);
			int nextMemberCap = Math.Clamp(ActiveBuildingLevel + 1, 1, 20);
			string curAlliance = string.IsNullOrWhiteSpace(SupabaseService.CachedAllianceName) ? "NONE" : SupabaseService.CachedAllianceName.Trim().ToUpper();
			bool inAlliance = (curAlliance != "NONE");

			PanelContainer infoBox = CreateCardPanel(0);
			VBoxContainer infoContent = CreateCardContainer(infoBox);

			Label lblTitle = new Label
			{
				Text = "🛡️  ALLIANCE HQ · SOVEREIGN COALITION & DIPLOMATIC COMMAND:",
				Modulate = new Color("#38BDF8"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblTitle.AddThemeFontSizeOverride("font_size", 16);

			string coreUnlockStatus = coreLvl >= 4
				? $"✓ Industrial Core Level {coreLvl} (Unlock Requirement Met: Core Lvl 4)"
				: $"🔒 Requires Industrial Core Level 4 (Current Core: Lvl {coreLvl})";
			string joinStatus = ActiveBuildingLevel >= 4 && coreLvl >= 4
				? "✓ UNLOCKED (Lvl 4+)"
				: $"🔒 Requires Alliance HQ Lvl 4 (Current: Lvl {ActiveBuildingLevel})";
			string createStatus = ActiveBuildingLevel >= 10 && coreLvl >= 4
				? "✓ UNLOCKED (Lvl 10+ · 10 Starting Member Slots)"
				: $"🔒 Requires Alliance HQ Lvl 10 (Current: Lvl {ActiveBuildingLevel})";

			Label lblDesc = new Label
			{
				Text =
					$"Alliance HQ Level: {ActiveBuildingLevel} / 20   |   Health: {hpInfo.currentHp:N0}/{hpInfo.maxHp:N0} HP ({hpInfo.healthPercent}%)   |   Pop: {pop}\n" +
					$"Member Slot Capacity: {memberCap} / 20 Members (+1 Member Slot per Level · Next Level: {nextMemberCap} Slots)\n" +
					$"• Unlock Requirement: {coreUnlockStatus}\n" +
					$"• Join Alliance: {joinStatus}\n" +
					$"• Create Alliance (10 Starting Members): {createStatus}\n" +
					$"• Current Status: {(inAlliance ? $"ACTIVE MEMBER OF [{curAlliance}] (Alliance Hub Unlocked)" : "UNALIGNED — Open Alliance Hub to Join or Create")}",
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblDesc.AddThemeFontSizeOverride("font_size", 15);

			infoContent.AddChild(lblTitle);
			infoContent.AddChild(lblDesc);
			_contentStack.AddChild(infoBox);

			Button btnAllianceRally = new Button
			{
				Text = inAlliance
					? $"🛡️ OPEN [{curAlliance}] ALLIANCE HUB (MEMBERS · SCIENCES · WAR ROOM · MESSAGING · ATTACKS)"
					: "🛡️ OPEN ALLIANCE HQ COMMAND (JOIN OR CREATE ALLIANCE)",
				CustomMinimumSize = new Vector2(0, 48),
				ClipText = true
			};
			btnAllianceRally.AddThemeFontSizeOverride("font_size", 15);
			btnAllianceRally.Modulate = inAlliance ? new Color("#4ADE80") : new Color("#38BDF8");
			btnAllianceRally.Pressed += () =>
			{
				CloseInspector();
				BaseHUD.Instance?.OpenAllianceHQModal();
			};
			_contentStack.AddChild(btnAllianceRally);
		}

		// =========================================================================
		// PLANETARY SHIELD (hub_shd): DEDICATED TACTICAL TELEMETRY MENU
		// - Lvl 1–3: Tier 2 · Lvl 4–6: Tier 3 · Lvl 7: Tier 4 · Lvl 8–10: Tier 5 (Max Level 10)
		// - Base Defense Power: +450 DEF per Shield Level (up to +4,500 DEF at Lvl 10)
		// - Defender Casualty Mitigation (Capped at 35%):
		//   * Planetary Shield Building (Lvl 1–10): +1% per level (up to 10%)
		//   * Research Directorate First Research (Lvl 1–20): +1% per level (up to 20%)
		//   * Alliance Science (Lvl 1–10): +1% per level (up to 10%)
		// - Laser Cannon Siege Rule: Does not block enemy Laser Cannon structural damage unless Planetary Shield itself is targeted.
		// =========================================================================
		private void BuildPlanetaryShieldMenu()
		{
			var liveLevels = (BaseHUD.Instance != null && BaseHUD.Instance.BuildingLevels != null)
				? BaseHUD.Instance.BuildingLevels
				: AllBuildingLevels;
			var hpInfo = GameMath.GetStructureHealthDetails(ActiveBuildingId, ActiveBuildingLevel, liveLevels);
			int pop = GameMath.GetStructurePop("shd", ActiveBuildingLevel);

			int shdLvl = Math.Clamp(ActiveBuildingLevel, 0, 10);
			int nextShdLvl = Math.Clamp(shdLvl + 1, 1, 10);
			int rshShieldLvl = Math.Clamp(liveLevels.GetValueOrDefault(GameMath.ResearchDirectorateShieldKey, 0), 0, 20);
			int aliShieldLvl = Math.Clamp(
				Math.Max(
					SupabaseService.CachedAllianceShieldScienceLevel,
					liveLevels.GetValueOrDefault($"__ali_lvl_{GameMath.AllianceShieldScienceKey}", 0)
				),
				0, 10
			);

			var mit = GameMath.GetDefenderCasualtyMitigationBreakdown(shdLvl, rshShieldLvl, aliShieldLvl);
			long curDefPower = GameMath.GetPlanetaryShieldDefensePower(shdLvl);
			long nextDefPower = GameMath.GetPlanetaryShieldDefensePower(nextShdLvl);
			float curTier = GameMath.GetBuildingComplexityTier("hub_shd", Math.Max(1, shdLvl));
			float nextTier = GameMath.GetBuildingComplexityTier("hub_shd", nextShdLvl);

			// Card 1: Planetary Shield Generator Live Telemetry
			PanelContainer shieldCard = CreateSlimHiTechCard(new Color("#00F0FF"), new Color(0.05f, 0.12f, 0.22f, 0.98f), 4);
			VBoxContainer shdBox = CreateCardContainer(shieldCard);
			shdBox.AddThemeConstantOverride("separation", 5);

			Label lblTitle = new Label
			{
				Text = "🛡️  PLANETARY SHIELD · TACTICAL DEFLECTION & MITIGATION TELEMETRY:",
				Modulate = new Color("#00F0FF"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblTitle.AddThemeFontSizeOverride("font_size", 16);
			shdBox.AddChild(lblTitle);

			string nextDefText = shdLvl < 10
				? $"➔ Next Level ({nextShdLvl}/10 · Tier {nextTier:0.#}): +{nextDefPower:N0} DEF & +{nextShdLvl}% Building Mitigation"
				: "✓ MAXIMUM PLANETARY SHIELD LEVEL ACHIEVED (LVL 10 / 10)";

			Label lblCoreStats = new Label
			{
				Text =
					$"Shield Generator Level: {GameMath.FormatEffectiveLevel(hpInfo.effectiveLevel)} / 10 (Tier {curTier:0.#})   |   Health: {hpInfo.currentHp:N0} / {hpInfo.maxHp:N0} HP ({hpInfo.healthPercent}%)   |   Officers: {pop}\n" +
					$"• Base Defense Power Bonus: +{curDefPower:N0} DEF (+450 DEF per Shield Level, up to +4,500 DEF at Lvl 10)\n" +
					$"• {nextDefText}\n" +
					$"• Complexity Tier Brackets: Lvl 1–3: Tier 2  ·  Lvl 4–6: Tier 3  ·  Lvl 7: Tier 4  ·  Lvl 8–10: Tier 5",
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblCoreStats.AddThemeFontSizeOverride("font_size", 15);
			shdBox.AddChild(lblCoreStats);
			_contentStack.AddChild(shieldCard);

			// Card 2: 35% Defender Casualty Mitigation Cap Breakdown (Shield + Research Directorate + Alliance Science)
			PanelContainer mitCard = CreateSlimHiTechCard(new Color("#4ADE80"), new Color(0.05f, 0.14f, 0.18f, 0.98f), 4);
			VBoxContainer mitBox = CreateCardContainer(mitCard);
			mitBox.AddThemeConstantOverride("separation", 5);

			Label lblMitHdr = new Label
			{
				Text = $"⚔️  DEFENDER CASUALTY MITIGATION BREAKDOWN: {mit.cappedTotalPct}% / 35% MAX CAP",
				Modulate = new Color("#4ADE80"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblMitHdr.AddThemeFontSizeOverride("font_size", 16);
			mitBox.AddChild(lblMitHdr);

			Label lblMitSources = new Label
			{
				Text =
					$"1. Planetary Shield Building (Lvl {shdLvl}/10): +{mit.shieldBuildingPct}% / 10% (+1% per Shield Level)\n" +
					$"2. Research Directorate — Harmonic Shield Matrix (Lvl {rshShieldLvl}/20): +{mit.researchDirectoratePct}% / 20% (+1% per Research Level)\n" +
					$"3. Alliance Science — Coalition Shield Matrix (Lvl {aliShieldLvl}/10): +{mit.allianceSciencePct}% / 10% (+1% per Alliance Science Level)\n" +
					$"• Combined Active Mitigation: {mit.cappedTotalPct}% (Uncapped Sum: {mit.uncappedTotalPct}% · Hard Cap: 35%)\n" +
					$"• 💥 Laser Cannon Siege Rule: The Planetary Shield reduces defending unit casualties in battle, but does NOT block or absorb enemy Laser Cannon structural damage before it hits buildings (unless the Planetary Shield itself is the building targeted by the Laser Cannons).",
				Modulate = new Color("#CBD5E1"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblMitSources.AddThemeFontSizeOverride("font_size", 14);
			mitBox.AddChild(lblMitSources);

			HBoxContainer quickLinksRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			quickLinksRow.AddThemeConstantOverride("separation", 8);

			int rshBldLvl = liveLevels.GetValueOrDefault("hub_rsh", 0);
			Button btnOpenRsh = MakeHiTechButton(
				$"🔬 RESEARCH DIRECTORATE (+{mit.researchDirectoratePct}%/20%)",
				new Color("#38BDF8"), 0, 40, 14
			);
			btnOpenRsh.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btnOpenRsh.Pressed += () =>
			{
				InspectBuilding(
					"hub_rsh",
					"Research Directorate",
					Math.Max(1, rshBldLvl),
					new Color("#38BDF8"),
					"", 0, null,
					CurrentIndustrialCoreLevel,
					liveLevels
				);
			};
			quickLinksRow.AddChild(btnOpenRsh);

			Button btnOpenAllianceSci = MakeHiTechButton(
				$"🛡️ ALLIANCE SCIENCE (+{mit.allianceSciencePct}%/10%)",
				new Color("#A855F7"), 0, 40, 14
			);
			btnOpenAllianceSci.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btnOpenAllianceSci.Pressed += () =>
			{
				CloseInspector();
				BaseHUD.Instance?.OpenAllianceHQModal();
			};
			quickLinksRow.AddChild(btnOpenAllianceSci);

			mitBox.AddChild(quickLinksRow);
			_contentStack.AddChild(mitCard);
		}

		// =========================================================================
		// RESEARCH DIRECTORATE (hub_rsh): FIRST RESEARCH — HARMONIC SHIELD DEFLECTION MATRIX (LVL 1–20)
		// - Gives +1% Defender Casualty Mitigation to Planetary Shield per Level (Max 20% at Level 20).
		// - Leaves 15% out of the 35% cap for Planetary Shield Building (10%) & Alliance Science (10%).
		// =========================================================================
		private void BuildResearchDirectorateMenu()
		{
			var liveLevels = (BaseHUD.Instance != null && BaseHUD.Instance.BuildingLevels != null)
				? BaseHUD.Instance.BuildingLevels
				: AllBuildingLevels;
			var hpInfo = GameMath.GetStructureHealthDetails(ActiveBuildingId, ActiveBuildingLevel, liveLevels);
			int pop = GameMath.GetStructurePop("rsh", ActiveBuildingLevel);

			int shdBldLvl = Math.Clamp(liveLevels.GetValueOrDefault("hub_shd", 0), 0, 10);
			int curRshTechLvl = Math.Clamp(liveLevels.GetValueOrDefault(GameMath.ResearchDirectorateShieldKey, 0), 0, 20);
			int aliShieldLvl = Math.Clamp(
				Math.Max(
					SupabaseService.CachedAllianceShieldScienceLevel,
					liveLevels.GetValueOrDefault($"__ali_lvl_{GameMath.AllianceShieldScienceKey}", 0)
				),
				0, 10
			);
			var mit = GameMath.GetDefenderCasualtyMitigationBreakdown(shdBldLvl, curRshTechLvl, aliShieldLvl);

			// Card 1: Research Directorate Status Header
			PanelContainer infoCard = CreateSlimHiTechCard(new Color("#38BDF8"), new Color(0.05f, 0.12f, 0.22f, 0.98f), 4);
			VBoxContainer infoBox = CreateCardContainer(infoCard);
			infoBox.AddThemeConstantOverride("separation", 4);

			Label lblTitle = new Label
			{
				Text = "🔬  RESEARCH DIRECTORATE · ADVANCED DEFENSE & WARFARE TECHNOLOGIES:",
				Modulate = new Color("#38BDF8"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblTitle.AddThemeFontSizeOverride("font_size", 16);
			infoBox.AddChild(lblTitle);

			Label lblDesc = new Label
			{
				Text =
					$"Research Directorate Level: {GameMath.FormatEffectiveLevel(hpInfo.effectiveLevel)} / 20   |   Health: {hpInfo.currentHp:N0} / {hpInfo.maxHp:N0} HP ({hpInfo.healthPercent}%)   |   Scientists: {pop}\n" +
					$"Total Planetary Shield Casualty Mitigation: {mit.cappedTotalPct}% / 35% Cap (Shield Building: +{mit.shieldBuildingPct}% · Research Directorate: +{mit.researchDirectoratePct}% · Alliance Science: +{mit.allianceSciencePct}%)",
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblDesc.AddThemeFontSizeOverride("font_size", 15);
			infoBox.AddChild(lblDesc);
			_contentStack.AddChild(infoCard);

			// Card 2: First Integrated Research — Harmonic Shield Deflection Matrix (Levels 1–20)
			PanelContainer techCard = CreateSlimHiTechCard(new Color("#00F0FF"), new Color(0.06f, 0.15f, 0.26f, 0.98f), 4);
			VBoxContainer techBox = CreateCardContainer(techCard);
			techBox.AddThemeConstantOverride("separation", 6);

			HBoxContainer techTopRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			Label lblTechTitle = new Label
			{
				Text = "🛡️  RESEARCH #1: HARMONIC SHIELD DEFLECTION MATRIX",
				Modulate = new Color("#00F0FF"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblTechTitle.AddThemeFontSizeOverride("font_size", 16);

			Label lblTechBadge = new Label
			{
				Text = $"LVL {curRshTechLvl} / 20 (+{curRshTechLvl}% MITIGATION)",
				Modulate = new Color("#4ADE80")
			};
			lblTechBadge.AddThemeFontSizeOverride("font_size", 15);

			techTopRow.AddChild(lblTechTitle);
			techTopRow.AddChild(lblTechBadge);
			techBox.AddChild(techTopRow);

			Label lblTechEffect = new Label
			{
				Text =
					$"• Effect: Adds +1% Defender Casualty Mitigation per Level to your Planetary Shield (Max +20% at Level 20).\n" +
					$"• Cap Balance: At Max Level 20 (+20%), 15% remains out of the 35% Total Cap — fulfilled by Planetary Shield Building (+10% max) and Alliance Science (+10% max).\n" +
					$"• Current Bonus: +{curRshTechLvl}%   ➔   Next Level ({Math.Min(20, curRshTechLvl + 1)}/20): +{Math.Min(20, curRshTechLvl + 1)}% Defender Casualty Mitigation",
				Modulate = new Color("#CBD5E1"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblTechEffect.AddThemeFontSizeOverride("font_size", 14);
			techBox.AddChild(lblTechEffect);

			int activeTimerEndSec = liveLevels.GetValueOrDefault(GameMath.ResearchDirectorateShieldTimerKey, 0);
			long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			bool isResearchingNow = activeTimerEndSec > nowSec;

			if (curRshTechLvl >= 20)
			{
				Label maxLbl = new Label
				{
					Text = "✓ MAXIMUM RESEARCH LEVEL ACHIEVED (LEVEL 20 / 20 — +20% DEFENDER CASUALTY MITIGATION ACTIVE)",
					Modulate = new Color("#4ADE80"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				maxLbl.AddThemeFontSizeOverride("font_size", 15);
				techBox.AddChild(maxLbl);
			}
			else if (isResearchingNow)
			{
				long remSec = Math.Max(1, activeTimerEndSec - nowSec);
				_liveResearchDirectorateTimerLabel = new Label
				{
					Text = $"⚙️ RESEARCHING LEVEL {curRshTechLvl + 1}/20 — TIME REMAINING: {GameMath.FormatTime(remSec)}",
					Modulate = new Color("#FBBF24"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				_liveResearchDirectorateTimerLabel.AddThemeFontSizeOverride("font_size", 15);
				techBox.AddChild(_liveResearchDirectorateTimerLabel);
			}
			else
			{
				float srvSpeed = BaseHUD.Instance != null ? Math.Max(1.0f, BaseHUD.Instance.ServerSpeed) : 1.0f;
				var rshMetrics = GameMath.GetResearchDirectorateShieldMetrics(curRshTechLvl, srvSpeed);
				int nextTechLvl = curRshTechLvl + 1;
				bool hasReqDirLvl = ActiveBuildingLevel >= rshMetrics.reqResearchDirectorateLvl;

				long curE = BaseHUD.Instance != null ? (long)BaseHUD.Instance.ResE : 0;
				long curI = BaseHUD.Instance != null ? (long)BaseHUD.Instance.ResI : 0;
				long curT = BaseHUD.Instance != null ? (long)BaseHUD.Instance.ResT : 0;
				long curH = BaseHUD.Instance != null ? (long)BaseHUD.Instance.ResH3 : 0;
				bool canAffordTech = curE >= rshMetrics.costE &&
									 curI >= rshMetrics.costI &&
									 curT >= rshMetrics.costT &&
									 curH >= rshMetrics.costH3;

				Label lblReqAndCost = new Label
				{
					Text =
						$"Prerequisite: Research Directorate Lvl {rshMetrics.reqResearchDirectorateLvl} (Current: Lvl {ActiveBuildingLevel})   |   ⏱ Research Time: {GameMath.FormatTime(rshMetrics.researchTimeSec)}\n" +
						$"Research Cost:  ⚡ {rshMetrics.costE:N0}   |   ⛏️ {rshMetrics.costI:N0}   |   💎 {rshMetrics.costT:N0}" +
						(rshMetrics.costH3 > 0 ? $"   |   ⛽ {rshMetrics.costH3:N0}" : ""),
					Modulate = (hasReqDirLvl && canAffordTech) ? new Color("#4ADE80") : new Color("#FCA5A5"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblReqAndCost.AddThemeFontSizeOverride("font_size", 14);
				techBox.AddChild(lblReqAndCost);

				bool canStartTech = hasReqDirLvl && canAffordTech;
				string btnTxt = !hasReqDirLvl
					? $"🔒 REQUIRES RESEARCH DIRECTORATE LVL {rshMetrics.reqResearchDirectorateLvl} TO RESEARCH LVL {nextTechLvl}"
					: (!canAffordTech
						? $"⚠️ INSUFFICIENT RESOURCES TO RESEARCH LVL {nextTechLvl}"
						: $"🔬 START RESEARCH LVL {nextTechLvl} (+1% SHIELD MITIGATION · {GameMath.FormatTime(rshMetrics.researchTimeSec)})");

				Button btnStartResearch = MakeHiTechButton(
					btnTxt,
					new Color("#22C55E"),
					0, 44, 15,
					canStartTech,
					!canStartTech
				);
				btnStartResearch.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				btnStartResearch.Pressed += () =>
				{
					if (BaseHUD.Instance == null || !canStartTech) return;
					BaseHUD.Instance.ResE -= rshMetrics.costE;
					BaseHUD.Instance.ResI -= rshMetrics.costI;
					BaseHUD.Instance.ResT -= rshMetrics.costT;
					BaseHUD.Instance.ResH3 -= rshMetrics.costH3;

					long finishUnixSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + rshMetrics.researchTimeSec;
					BaseHUD.Instance.BuildingLevels[GameMath.ResearchDirectorateShieldTimerKey] = (int)Math.Min(int.MaxValue, finishUnixSec);
					BaseHUD.Instance.UpdateHUDDisplay();
					BaseHUD.Instance.TriggerInstantSave();
					RefreshInspector();
				};
				techBox.AddChild(btnStartResearch);
			}

			_contentStack.AddChild(techCard);
		}

		private void BuildGenericFacilityMenu()
		{
			var hpInfo = GameMath.GetStructureHealthDetails(ActiveBuildingId, ActiveBuildingLevel, AllBuildingLevels);
			int pop = GameMath.GetStructurePop(ActiveBuildingId, ActiveBuildingLevel);

			PanelContainer infoBox = CreateCardPanel(0);
			VBoxContainer infoContent = CreateCardContainer(infoBox);

			Label lblTitle = new Label { Text = $"{ActiveBuildingName.ToUpper()} STATUS:", Modulate = ActiveBuildingColor, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			lblTitle.AddThemeFontSizeOverride("font_size", 16);

			string roleDesc = ActiveBuildingId switch
			{
				"hub_com" => "Diplomatic Link Relay. Unlocks alliance founding, treaties, and global division rankings.",
				"hub_flt" => "Fleet Command Center. Build Strike Fleets, load resource cargo, and launch Attacks, Trap Moves, or Allied Reinforcements.",
				"hub_trd" => "Galactic commerce network. Enables inter-colony supply transfers and alliance trade aid.",
				"hub_rsh" => "Advanced science division. Unlocks higher-tier warship blueprints and energy technologies.",
				"hub_shd" => "High-yield deflection generator. Mitigates casualty rates during planetary bombardments.",
				_ => "Specialized colonial facility."
			};

			Label lblDesc = new Label
			{
				Text = $"{roleDesc}\nBuilding Health: {hpInfo.currentHp:N0} / {hpInfo.maxHp:N0} HP ({hpInfo.healthPercent}%)   |   Staff: {pop} Officers",
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblDesc.AddThemeFontSizeOverride("font_size", 15);

			infoContent.AddChild(lblTitle);
			infoContent.AddChild(lblDesc);
			_contentStack.AddChild(infoBox);
		}

		private void BuildStandardSubMineMenu()
		{
			var boosts = BaseHUD.Instance?.ActiveBoosts;
			float srvSpd = BaseHUD.Instance != null ? BaseHUD.Instance.ServerSpeed : 1.0f;
			long currentYield = BaseHUD.GetMineHourlyProduction(ActiveBuildingId, ActiveBuildingLevel, boosts, srvSpd, out long rawCur, out double boostMult);
			long nextYield = BaseHUD.GetMineHourlyProduction(ActiveBuildingId, ActiveBuildingLevel + 1, boosts, srvSpd, out _, out _);
			var hpInfo = GameMath.GetStructureHealthDetails(ActiveBuildingId, ActiveBuildingLevel, AllBuildingLevels);
			int pop = GameMath.GetStructurePop("mine", ActiveBuildingLevel);

			var tel = BaseHUD.Instance != null ? BaseHUD.Instance.GetSmartHangarTelemetry() : default;
			long totalResProd = ActiveDistrictCode == "E" ? tel.totalProdE : (ActiveDistrictCode == "I" ? tel.totalProdI : tel.totalProdT);
			string resLabel = ActiveDistrictCode == "E" ? "Power" : (ActiveDistrictCode == "I" ? "Iron" : "Titanium");

			PanelContainer yieldBox = CreateCardPanel(0);
			VBoxContainer yieldContent = CreateCardContainer(yieldBox);

			Label lblYieldTitle = new Label { Text = "PRODUCTION TELEMETRY OUTPUT:", Modulate = new Color("#94A3B8"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			lblYieldTitle.AddThemeFontSizeOverride("font_size", 16);

			HBoxContainer yieldValues = new HBoxContainer();
			string boostTag = boostMult > 1.001 ? $" (Base {rawCur:N0}/h · +{((boostMult - 1.0) * 100):F0}% Boost)" : "";
			Label lblCurrent = new Label { Text = $"Current: {currentYield:N0}/h{boostTag}", Modulate = Colors.White, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			lblCurrent.AddThemeFontSizeOverride("font_size", 17);

			Label lblNext = new Label { Text = $"➔ Next: {nextYield:N0}/h", Modulate = new Color("#22C55E") };
			lblNext.AddThemeFontSizeOverride("font_size", 17);

			yieldValues.AddChild(lblCurrent);
			yieldValues.AddChild(lblNext);

			Label lblTotalSummary = new Label
			{
				Text = $"Combined {resLabel} Output (Mines A + B + C): +{totalResProd:N0}/h",
				Modulate = new Color("#4ADE80"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblTotalSummary.AddThemeFontSizeOverride("font_size", 15);

			Label lblStats = new Label
			{
				Text = $"Building Health: {hpInfo.currentHp:N0} / {hpInfo.maxHp:N0} HP ({hpInfo.healthPercent}%)   |   Population: {pop} Engineers",
				Modulate = new Color("#CBD5E1"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblStats.AddThemeFontSizeOverride("font_size", 15);

			yieldContent.AddChild(lblYieldTitle);
			yieldContent.AddChild(yieldValues);
			yieldContent.AddChild(lblTotalSummary);
			yieldContent.AddChild(lblStats);
			_contentStack.AddChild(yieldBox);

			BuildBoostSection(ActiveDistrictCode);
		}

		private void BuildIndustrialCoreMenu()
		{
			double wallMitigation = GameMath.GetWallMitigation(ActiveBuildingLevel);
			var hpInfo = GameMath.GetStructureHealthDetails(ActiveBuildingId, ActiveBuildingLevel, AllBuildingLevels);
			int pop = GameMath.GetStructurePop("cmd", ActiveBuildingLevel);

			PanelContainer infoBox = CreateCardPanel(0);
			VBoxContainer infoContent = CreateCardContainer(infoBox);

			Label lblTitle = new Label { Text = "INDUSTRIAL CORE MASTER SPIRE:", Modulate = new Color("#00F0FF"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			lblTitle.AddThemeFontSizeOverride("font_size", 16);

			Label lblSpire = new Label
			{
				Text = $"Core Level: {GameMath.FormatEffectiveLevel(hpInfo.effectiveLevel)} / 20   |   Building Health: {hpInfo.currentHp:N0} / {hpInfo.maxHp:N0} HP ({hpInfo.healthPercent}%)   |   Pop: {pop}\nPerimeter Armor Protection: {(wallMitigation * 100):F1}%   |   Blueprint Rule: 1 Enemy Laser Cannon deals 1 HP damage.",
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblSpire.AddThemeFontSizeOverride("font_size", 15);

			infoContent.AddChild(lblTitle);
			infoContent.AddChild(lblSpire);
			_contentStack.AddChild(infoBox);
		}

		private void BuildHelium3DistilleryMenu()
		{
			var boosts = BaseHUD.Instance?.ActiveBoosts;
			float srvSpd = BaseHUD.Instance != null ? BaseHUD.Instance.ServerSpeed : 1.0f;
			long currentYield = BaseHUD.GetMineHourlyProduction(ActiveBuildingId, ActiveBuildingLevel, boosts, srvSpd, out long rawCur, out double boostMult);
			long nextYield = BaseHUD.GetMineHourlyProduction(ActiveBuildingId, ActiveBuildingLevel + 1, boosts, srvSpd, out _, out _);
			var hpInfo = GameMath.GetStructureHealthDetails(ActiveBuildingId, ActiveBuildingLevel, AllBuildingLevels);
			int pop = GameMath.GetStructurePop("h3", ActiveBuildingLevel);
			var tel = BaseHUD.Instance != null ? BaseHUD.Instance.GetSmartHangarTelemetry() : default;

			PanelContainer monitorBox = CreateCardPanel(0);
			VBoxContainer monitorContent = CreateCardContainer(monitorBox);

			Label lblTitle = new Label { Text = "HELIUM-3 FUEL EXTRACTION MONITOR:", Modulate = new Color("#EAB308"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			lblTitle.AddThemeFontSizeOverride("font_size", 16);

			HBoxContainer row1 = new HBoxContainer();
			string boostTag = boostMult > 1.001 ? $" (Base {rawCur:N0}/h · +{((boostMult - 1.0) * 100):F0}% Boost)" : "";
			Label lblRateTitle = new Label { Text = $"Distillery Output{boostTag}:", Modulate = new Color("#CBD5E1"), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			Label lblRateVal = new Label { Text = $"+{currentYield:N0}/h ➔ +{nextYield:N0}/h", Modulate = new Color("#22C55E") };
			lblRateTitle.AddThemeFontSizeOverride("font_size", 16);
			lblRateVal.AddThemeFontSizeOverride("font_size", 16);
			row1.AddChild(lblRateTitle);
			row1.AddChild(lblRateVal);

			Label lblTotalH3Summary = new Label
			{
				Text = $"Combined H3 Mines (A–E): +{tel.totalProdH3:N0}/h   |   Army Upkeep: -{BaseHUD.FormatH3Rate(tel.totalUnitH3Upkeep, false)}/h   |   Net: {BaseHUD.FormatH3Rate(tel.netH3PerHour)}/h",
				Modulate = tel.netH3PerHour >= 0 ? new Color("#4ADE80") : new Color("#F87171"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblTotalH3Summary.AddThemeFontSizeOverride("font_size", 15);

			Label lblStarveNotice = new Label
			{
				Text = $"Building Health: {hpInfo.currentHp:N0}/{hpInfo.maxHp:N0} HP ({hpInfo.healthPercent}%) | Pop: {pop} | Note: If H3 hits 0, fleet attrition triggers.",
				Modulate = new Color("#F59E0B"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblStarveNotice.AddThemeFontSizeOverride("font_size", 14);

			monitorContent.AddChild(lblTitle);
			monitorContent.AddChild(row1);
			monitorContent.AddChild(lblTotalH3Summary);
			monitorContent.AddChild(lblStarveNotice);
			_contentStack.AddChild(monitorBox);

			BuildBoostSection("H3");
		}

		private void BuildBoostSection(string resCode)
		{
			PanelContainer boostBox = CreateCardPanel(0);
			VBoxContainer boostContent = CreateCardContainer(boostBox);

			Label lblTitle = new Label { Text = $"PREMIUM PRODUCTION BOOSTS ({resCode}):", Modulate = new Color("#FBBF24"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			lblTitle.AddThemeFontSizeOverride("font_size", 16);
			boostContent.AddChild(lblTitle);

			if (BaseHUD.Instance != null && BaseHUD.Instance.ActiveBoosts.TryGetValue(resCode, out var activeBoost))
			{
				if (activeBoost.Expiration > DateTimeOffset.UtcNow)
				{
					TimeSpan remaining = activeBoost.Expiration - DateTimeOffset.UtcNow;
					Label lblActive = new Label 
					{ 
						Text = $"ACTIVE: +{((activeBoost.Multiplier - 1.0f) * 100):F0}% BOOST  (EXPIRES IN: {remaining.Days}d {remaining.Hours}h {remaining.Minutes}m)", 
						Modulate = new Color("#22C55E"),
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					};
					lblActive.AddThemeFontSizeOverride("font_size", 14);
					boostContent.AddChild(lblActive);
				}
			}

			HBoxContainer btnRow1 = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnRow1.AddThemeConstantOverride("separation", 10);

			Button btnBoost1 = new Button { Text = "+30% (7 DAYS) · 250 GGOLD", CustomMinimumSize = new Vector2(180, 46), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnBoost1.AddThemeFontSizeOverride("font_size", 14);
			btnBoost1.Modulate = new Color("#FBBF24");
			btnBoost1.Pressed += () => OnBuyBoostPressed(resCode, 1.30f, 7, 250);
			btnRow1.AddChild(btnBoost1);

			Button btnBoost2 = new Button { Text = "+75% (7 DAYS) · 500 GGOLD", CustomMinimumSize = new Vector2(180, 46), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnBoost2.AddThemeFontSizeOverride("font_size", 14);
			btnBoost2.Modulate = new Color("#FBBF24");
			btnBoost2.Pressed += () => OnBuyBoostPressed(resCode, 1.75f, 7, 500);
			btnRow1.AddChild(btnBoost2);

			boostContent.AddChild(btnRow1);

			Button btnBoost3 = new Button { Text = "+70% (30 DAYS) — 2,150 GGOLD", CustomMinimumSize = new Vector2(0, 46) };
			btnBoost3.AddThemeFontSizeOverride("font_size", 14);
			btnBoost3.Modulate = new Color("#FBBF24");
			btnBoost3.Pressed += () => OnBuyBoostPressed(resCode, 1.70f, 30, 2150);
			boostContent.AddChild(btnBoost3);

			_contentStack.AddChild(boostBox);
		}

		private void OnBuyBoostPressed(string resCode, float multiplier, int days, long cost)
		{
			if (BaseHUD.Instance == null) return;

			if (BaseHUD.Instance.GGold >= cost)
			{
				ShowConfirmDialog(
					"AUTHORIZE BOOST PURCHASE?",
					$"This will consume {cost} Galaxy Gold to apply a +{((multiplier - 1.0f) * 100):F0}% production boost to {resCode} for {days} days.",
					() => 
					{
						BaseHUD.Instance.GGold -= cost;
						BaseHUD.Instance.ActiveBoosts[resCode] = new GameMath.BoostData
						{
							Multiplier = multiplier,
							Expiration = DateTimeOffset.UtcNow.AddDays(days)
						};
						BaseHUD.Instance.UpdateHUDDisplay();
						BaseHUD.Instance.TriggerInstantSave();
						RefreshInspector();
						GD.Print($"[BOOST] Purchased {multiplier}x boost for {resCode} for {days} days.");
					}
				);
			}
			else
			{
				GD.PrintErr("[INSPECTOR] Insufficient GGold for boost.");
			}
		}

		private void BuildDeepRadarMenu()
		{
			int clampedLvl = Math.Clamp(ActiveBuildingLevel, 1, 10);
			int homeMoon = SupabaseService.CachedMoonId > 0 ? SupabaseService.CachedMoonId : 100;
			var (visMoons, visPct, radiusPx) = GameMath.GetRadarVisibilityCircle(homeMoon, clampedLvl);
			int nextLvl = Math.Min(10, clampedLvl + 1);
			var (nextMoons, nextPct, _) = GameMath.GetRadarVisibilityCircle(homeMoon, nextLvl);

			PanelContainer radarBox = CreateCardPanel(0);
			VBoxContainer radarContent = CreateCardContainer(radarBox);

			Label lblTitle = new Label { Text = "📡  DEEP SPACE RADAR · GALAXY VISIBILITY & SECTOR SCANNER:", Modulate = new Color("#F59E0B"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			lblTitle.AddThemeFontSizeOverride("font_size", 16);

			string nextInfo = clampedLvl < 10
				? $"Next Level ({nextLvl}/10): {nextPct:0}% Galaxy Visibility ({nextMoons:N0} / 1,000 Moons inside Radar Circle)"
				: "MAXIMUM RADAR RANGE ACHIEVED (100% Full Galaxy Visibility)";

			Label lblDesc = new Label
			{
				Text = $"Radar Level: {clampedLvl} / 10   |   Galaxy Visibility: {visPct:0}% ({visMoons:N0} / {GameMath.TotalMoons:N0} Moons Revealed)\nRadar Scan Radius: {radiusPx:N0} AU centered on Moon M-{homeMoon}\n{nextInfo}",
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblDesc.AddThemeFontSizeOverride("font_size", 15);

			HBoxContainer jumpRow = new HBoxContainer();
			jumpRow.AddThemeConstantOverride("separation", 10);
			_radarJumpInput = new LineEdit
			{
				PlaceholderText = $"Enter Visible Moon # [1-{GameMath.TotalMoons}]",
				CustomMinimumSize = new Vector2(180, 44),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_radarJumpInput.AddThemeFontSizeOverride("font_size", 15);

			Label jumpFeedback = new Label
			{
				Text = "",
				Modulate = new Color("#EF4444"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			jumpFeedback.AddThemeFontSizeOverride("font_size", 14);

			Button btnWarpScan = new Button
			{
				Text = "🔭 VIEW MOON SLOTS",
				CustomMinimumSize = new Vector2(170, 44)
			};
			btnWarpScan.AddThemeFontSizeOverride("font_size", 14);
			btnWarpScan.Modulate = new Color("#00F0FF");
			btnWarpScan.Pressed += () =>
			{
				if (int.TryParse(_radarJumpInput.Text, out int moon) && moon >= 1 && moon <= GameMath.TotalMoons)
				{
					if (!GameMath.IsMoonVisibleInRadarCircle(homeMoon, moon, clampedLvl))
					{
						jumpFeedback.Text = $"🔒 Moon M-{moon} is outside your {visPct:0}% Radar Circle! Upgrade Deep Space Radar to expand your visibility.";
						return;
					}
					MoonView.SelectedMoonToView = moon;
					CloseInspector();
					string moonTarget = ResourceLoader.Exists("res://Scripts/MoonView.tscn") ? "res://Scripts/MoonView.tscn" : "res://Scenes/MoonView.tscn";
					GetTree().ChangeSceneToFile(moonTarget);
				}
			};

			jumpRow.AddChild(_radarJumpInput);
			jumpRow.AddChild(btnWarpScan);

			radarContent.AddChild(lblTitle);
			radarContent.AddChild(lblDesc);
			radarContent.AddChild(jumpRow);
			radarContent.AddChild(jumpFeedback);
			_contentStack.AddChild(radarBox);

			// Saved Coordinates Watchlist Card inside Deep Space Radar
			BuildWatchlistSectionCard(true);
		}

		// =========================================================================
		// ORBITAL SHIPYARD (hub_mil): SIMULTANEOUS MULTI-BAY UNIT ASSEMBLY
		// Every unit type has its own independent assembly bay and timer!
		// =========================================================================
		private void BuildOrbitalShipyardMenu()
		{
			// 1. Active Simultaneous Unit Assembly Bays Monitor
			PanelContainer activeBaysPanel = CreateCardPanel(0);
			VBoxContainer baysBox = CreateCardContainer(activeBaysPanel);

			Label baysTitle = new Label
			{
				Text = "⚙️  ACTIVE SIMULTANEOUS ASSEMBLY BAYS (INDEPENDENT TIMERS):",
				Modulate = new Color("#FBBF24"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			baysTitle.AddThemeFontSizeOverride("font_size", 16);
			baysBox.AddChild(baysTitle);

			_shipyardLiveBaysBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			_shipyardLiveBaysBox.AddThemeConstantOverride("separation", 4);
			baysBox.AddChild(_shipyardLiveBaysBox);
			_contentStack.AddChild(activeBaysPanel);

			// 2. Unit Blueprints & Simultaneous Assembly Controls
			Label lblHeader = new Label
			{
				Text = "ORBITAL SHIPYARD BLUEPRINT MANIFESTS (DROPS INTO GARRISON ARMORY):",
				Modulate = new Color("#EF4444"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblHeader.AddThemeFontSizeOverride("font_size", 16);
			_contentStack.AddChild(lblHeader);

			var bays = BaseHUD.Instance?.ActiveUnitBays;
			foreach (var unit in GameData.Units)
			{
				bool isUnlocked = ActiveBuildingLevel >= unit.ShipyardReqLvl;
				int garrisonCount = BaseHUD.Instance?.UnitInventory.GetValueOrDefault(unit.Id, 0) ?? 0;
				var activeJob = (bays != null && bays.TryGetValue(unit.Id, out var j)) ? j : null;

				PanelContainer unitCard = CreateCardPanel(0);
				if (!isUnlocked) unitCard.Modulate = new Color(0.6f, 0.6f, 0.6f, 0.6f);

				VBoxContainer cardContent = CreateCardContainer(unitCard);

				HBoxContainer topRow = new HBoxContainer();
				Label lblUnitName = new Label
				{
					Text = $"{unit.Name.ToUpper()}   ·   GARRISON: {garrisonCount:N0}",
					Modulate = isUnlocked ? Colors.White : new Color("#64748B"),
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblUnitName.AddThemeFontSizeOverride("font_size", 16);
				_shipyardUnitTitleLabels[unit.Id] = lblUnitName;

				string statusRight = activeJob != null
					? $"⚙️ BUILDING: {activeJob.RemainingCount}x ({GameMath.FormatTime((int)Math.Ceiling(activeJob.NextUnitTimerSec))})"
					: $"REQ LVL {unit.ShipyardReqLvl} · ⏱ {unit.AssemblyTimeSec}s/unit";
				Label lblReq = new Label
				{
					Text = statusRight,
					Modulate = activeJob != null ? new Color("#FBBF24") : (isUnlocked ? new Color("#22C55E") : new Color("#EF4444"))
				};
				lblReq.AddThemeFontSizeOverride("font_size", 14);
				_shipyardUnitStatusLabels[unit.Id] = lblReq;

				topRow.AddChild(lblUnitName);
				topRow.AddChild(lblReq);
				cardContent.AddChild(topRow);

				Label lblStats = new Label
				{
					Text = $"ATK: {unit.Atk:N0} | DEF: {unit.Def:N0} | HP: {unit.Hp:N0} | Cargo: {unit.Cargo:N0} | Upkeep: {unit.H3Drain} H3/h",
					Modulate = new Color("#E2E8F0"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblStats.AddThemeFontSizeOverride("font_size", 14);
				cardContent.AddChild(lblStats);

				Label lblCosts = new Label
				{
					Text = $"⚡ {unit.CostE:N0}  |  ⛏️ {unit.CostI:N0}  |  💎 {unit.CostT:N0}",
					Modulate = new Color("#00F0FF"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblCosts.AddThemeFontSizeOverride("font_size", 15);
				cardContent.AddChild(lblCosts);

				if (isUnlocked)
				{
					HBoxContainer assembleRow = new HBoxContainer();
					assembleRow.AddThemeConstantOverride("separation", 8);
					LineEdit inputQty = new LineEdit
					{
						Text = "1",
						PlaceholderText = "Qty",
						CustomMinimumSize = new Vector2(92, 42)
					};
					inputQty.AddThemeFontSizeOverride("font_size", 15);
					_unitQtyInputs[unit.Id] = inputQty;

					string uId = unit.Id;

					Button btnBuildCustom = new Button
					{
						Text = "QUEUE BAY",
						CustomMinimumSize = new Vector2(130, 42),
						SizeFlagsHorizontal = SizeFlags.ExpandFill
					};
					btnBuildCustom.AddThemeFontSizeOverride("font_size", 14);
					btnBuildCustom.Modulate = new Color("#EF4444");
					btnBuildCustom.Pressed += () => OnRecruitUnit(uId);
					assembleRow.AddChild(inputQty);
					assembleRow.AddChild(btnBuildCustom);

					foreach (int presetAmt in new int[] { 1, 10, 100 })
					{
						int capAmt = presetAmt;
						Button qBtn = new Button
						{
							Text = $"+{capAmt}",
							CustomMinimumSize = new Vector2(72, 42)
						};
						qBtn.AddThemeFontSizeOverride("font_size", 14);
						qBtn.Modulate = new Color("#4ADE80");
						qBtn.Pressed += () => QueueSimultaneousUnitBay(uId, capAmt);
						assembleRow.AddChild(qBtn);
					}

					cardContent.AddChild(assembleRow);
				}
				else
				{
					Label lblLocked = new Label
					{
						Text = $"🔒 UPGRADE ORBITAL SHIPYARD TO LEVEL {unit.ShipyardReqLvl} TO UNLOCK BLUEPRINT",
						Modulate = new Color("#EF4444"),
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					};
					lblLocked.AddThemeFontSizeOverride("font_size", 14);
					cardContent.AddChild(lblLocked);
				}

				_contentStack.AddChild(unitCard);
			}

			UpdateOrbitalShipyardLiveTimers();
			_contentStack.AddChild(new HSeparator());
		}

		// =========================================================================
		// GARRISON ARMORY (hub_arm): DISPLAYS TOTAL RECRUITED UNITS & ACTIVE BAYS
		// =========================================================================
		private void BuildGarrisonArmoryMenu()
		{
			long totalUnits = 0;
			long totalAtk = 0;
			long totalDef = 0;
			long totalHp = 0;
			long totalCargo = 0;
			double totalH3Upkeep = 0.0;

			var inv = BaseHUD.Instance?.UnitInventory;
			var bays = BaseHUD.Instance?.ActiveUnitBays;

			foreach (var u in GameData.Units)
			{
				int count = inv != null ? inv.GetValueOrDefault(u.Id, 0) : 0;
				totalUnits += count;
				totalAtk += (long)u.Atk * count;
				totalDef += (long)u.Def * count;
				totalHp += (long)u.Hp * count;
				totalCargo += (long)u.Cargo * count;
				totalH3Upkeep += (double)u.H3Drain * count;
			}

			PanelContainer summaryCard = CreateCardPanel(0);
			VBoxContainer sumBox = CreateCardContainer(summaryCard);
			Label sumTitle = new Label { Text = "🛡️  GARRISON ARMORY · SECTOR STANDING FORCES SUMMARY", Modulate = new Color("#4ADE80"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			sumTitle.AddThemeFontSizeOverride("font_size", 16);
			sumBox.AddChild(sumTitle);

			_armoryLiveSummaryLabel = new Label
			{
				Text = $"Total Garrisoned Units: {totalUnits:N0}   |   Combined Cargo Cap: {totalCargo:N0}\nTotal ATK: {totalAtk:N0}   |   Total DEF: {totalDef:N0}   |   Total HP: {totalHp:N0}\nHourly Garrison Fuel Upkeep: -{BaseHUD.FormatH3Rate(totalH3Upkeep, false)} H3/h",
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			_armoryLiveSummaryLabel.AddThemeFontSizeOverride("font_size", 15);
			sumBox.AddChild(_armoryLiveSummaryLabel);
			_contentStack.AddChild(summaryCard);

			// Also show units currently assembling in the Shipyard that are dropping into the Armory
			if (bays != null && bays.Count > 0)
			{
				PanelContainer bayCard = CreateCardPanel(0);
				VBoxContainer bayBox = CreateCardContainer(bayCard);
				Label bHdr = new Label { Text = "⚙️  INCOMING FROM ORBITAL SHIPYARD (ASSEMBLING NOW):", Modulate = new Color("#FBBF24"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
				bHdr.AddThemeFontSizeOverride("font_size", 15);
				bayBox.AddChild(bHdr);

				_armoryLiveBaysBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				_armoryLiveBaysBox.AddThemeConstantOverride("separation", 3);
				bayBox.AddChild(_armoryLiveBaysBox);
				_contentStack.AddChild(bayCard);
			}

			Label listHeader = new Label { Text = "GARRISON UNIT INVENTORY BY CLASS:", Modulate = new Color("#00F0FF"), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			listHeader.AddThemeFontSizeOverride("font_size", 16);
			_contentStack.AddChild(listHeader);

			foreach (var u in GameData.Units)
			{
				int count = inv != null ? inv.GetValueOrDefault(u.Id, 0) : 0;
				var activeBay = (bays != null && bays.TryGetValue(u.Id, out var bJob)) ? bJob : null;

				PanelContainer uPanel = CreateCardPanel(0);
				VBoxContainer uBox = CreateCardContainer(uPanel);

				HBoxContainer row = new HBoxContainer();
				Label nameLbl = new Label
				{
					Text = $"{u.Name.ToUpper()}",
					Modulate = count > 0 ? Colors.White : new Color("#94A3B8"),
					SizeFlagsHorizontal = SizeFlags.ExpandFill
				};
				nameLbl.AddThemeFontSizeOverride("font_size", 16);

				string rightTxt = activeBay != null
					? $"GARRISON: {count:N0}   (+{activeBay.RemainingCount} BUILDING)"
					: $"GARRISON: {count:N0} UNITS";
				Label qtyLbl = new Label
				{
					Text = rightTxt,
					Modulate = count > 0 ? new Color("#4ADE80") : new Color("#64748B")
				};
				qtyLbl.AddThemeFontSizeOverride("font_size", 16);
				_armoryUnitQtyLabels[u.Id] = qtyLbl;

				row.AddChild(nameLbl);
				row.AddChild(qtyLbl);
				uBox.AddChild(row);

				Label subLbl = new Label
				{
					Text = $"ATK: {u.Atk:N0} · DEF: {u.Def:N0} · HP: {u.Hp:N0} · Cargo: {u.Cargo:N0} · Upkeep: {u.H3Drain} H3/h",
					Modulate = new Color("#E2E8F0"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				subLbl.AddThemeFontSizeOverride("font_size", 14);
				uBox.AddChild(subLbl);

				_contentStack.AddChild(uPanel);
			}

			UpdateGarrisonArmoryLiveTimers();
		}

		// =========================================================================
		// FLEET STATION (hub_flt): HIGH-TECH FLEET COMMAND CENTER, WATCHLIST & SLIM FLEET STRIPS
		// - Engineered for managing 100s of simultaneous fleets effortlessly!
		// - Bright, high-contrast neon tactical buttons & slim high-tech cards
		// - Search & filter bar, live fleet counters, Watchlist quick-launcher,
		// - Per-fleet Laser Cannon building target (hidden unless Laser Cannons > 0),
		// - Expandable inline Fleet Editor (add/remove units & resources anytime while Docked)
		// =========================================================================
		private string _fleetTab = "LOCAL"; // LOCAL, STATIONED, TRANSIT, ARCHITECT
		private bool _showFleetWatchlistDrawer = false;
		private string _editingFleetId = "";
		private string _quickDispatchFleetId = "";
		private string _fleetSearchFilter = "";
		private string _fleetEditUnitStep = "1";
		private string _newFleetCallsign = "FLEET ALPHA";
		private int _selectedSiegeBuildingIdx = 0;
		private readonly Dictionary<string, int> _draftFleetUnits = new Dictionary<string, int>();
		private readonly HashSet<string> _expandedFleetDetails = new HashSet<string>();
		private long _draftCargoE = 0, _draftCargoI = 0, _draftCargoT = 0, _draftCargoH3 = 0;
		private Label _fleetFeedbackLabel;

		private Button MakeHiTechButton(string text, Color accent, float minWidth = 0, float minHeight = 30, int fontSize = 11, bool isSelected = false, bool disabled = false)
		{
			Button b = new Button
			{
				Text = text,
				CustomMinimumSize = new Vector2(minWidth, minHeight),
				Disabled = disabled,
				ClipText = (minWidth <= 0),
				TextOverrunBehavior = (minWidth <= 0) ? TextServer.OverrunBehavior.TrimEllipsis : TextServer.OverrunBehavior.NoTrimming,
				MouseFilter = MouseFilterEnum.Stop
			};
			b.AddThemeFontSizeOverride("font_size", fontSize);
			b.AddThemeColorOverride("font_color", disabled ? new Color("#64748B") : Colors.White);
			b.AddThemeColorOverride("font_hover_color", Colors.White);
			b.AddThemeColorOverride("font_pressed_color", Colors.White);
			b.AddThemeColorOverride("font_disabled_color", new Color("#475569"));
			b.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.90f));
			b.AddThemeConstantOverride("outline_size", 2);

			StyleBoxFlat normalStyle = new StyleBoxFlat
			{
				BgColor = disabled
					? new Color(0.06f, 0.09f, 0.14f, 0.72f)
					: (isSelected
						? new Color(
							Mathf.Min(1f, accent.R * 0.48f + 0.08f),
							Mathf.Min(1f, accent.G * 0.48f + 0.10f),
							Mathf.Min(1f, accent.B * 0.48f + 0.14f),
							0.98f)
						: new Color(
							Mathf.Min(1f, accent.R * 0.22f + 0.07f),
							Mathf.Min(1f, accent.G * 0.24f + 0.11f),
							Mathf.Min(1f, accent.B * 0.26f + 0.18f),
							0.96f)),
				BorderColor = disabled
					? new Color(0.25f, 0.32f, 0.42f, 0.55f)
					: (isSelected ? Colors.White : accent),
				BorderWidthLeft = isSelected ? 3 : 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = isSelected ? 3 : 2,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6,
				ShadowColor = disabled ? Colors.Transparent : new Color(accent.R, accent.G, accent.B, isSelected ? 0.42f : 0.22f),
				ShadowSize = disabled ? 0 : (isSelected ? 8 : 4),
				ContentMarginLeft = 7,
				ContentMarginRight = 7,
				ContentMarginTop = 3,
				ContentMarginBottom = 3
			};

			StyleBoxFlat hoverStyle = (StyleBoxFlat)normalStyle.Duplicate();
			hoverStyle.BgColor = new Color(
				Mathf.Min(1f, accent.R * 0.55f + 0.10f),
				Mathf.Min(1f, accent.G * 0.55f + 0.12f),
				Mathf.Min(1f, accent.B * 0.55f + 0.16f),
				1.0f);
			hoverStyle.BorderColor = Colors.White;
			hoverStyle.ShadowColor = new Color(accent.R, accent.G, accent.B, 0.55f);
			hoverStyle.ShadowSize = 8;

			b.AddThemeStyleboxOverride("normal", normalStyle);
			b.AddThemeStyleboxOverride("hover", hoverStyle);
			b.AddThemeStyleboxOverride("pressed", hoverStyle);
			b.AddThemeStyleboxOverride("disabled", normalStyle);
			return b;
		}

		private PanelContainer CreateSlimHiTechCard(Color accentBorder, Color bgTint, int leftAccentWidth = 4)
		{
			PanelContainer p = new PanelContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			StyleBoxFlat s = new StyleBoxFlat
			{
				BgColor = bgTint,
				BorderColor = accentBorder,
				BorderWidthLeft = leftAccentWidth,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6,
				ShadowColor = new Color(accentBorder.R, accentBorder.G, accentBorder.B, 0.14f),
				ShadowSize = 4,
				ContentMarginLeft = 8,
				ContentMarginRight = 8,
				ContentMarginTop = 5,
				ContentMarginBottom = 5
			};
			p.AddThemeStyleboxOverride("panel", s);
			return p;
		}

		private StyleBoxFlat MakeHiTechInputBoxStyle(Color borderAccent)
		{
			return new StyleBoxFlat
			{
				BgColor = new Color(0.08f, 0.15f, 0.26f, 0.98f),
				BorderColor = borderAccent,
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6,
				ContentMarginLeft = 8,
				ContentMarginRight = 8,
				ContentMarginTop = 3,
				ContentMarginBottom = 3
			};
		}

		private void BuildFleetStationMenu()
		{
			var fleets = BaseHUD.Instance?.SavedFleets ?? new List<GameMath.FleetManifest>();
			int dockedCount = 0, stationedCount = 0, transitCount = 0;
			long totalFleetShips = 0, totalFleetAtk = 0;
			foreach (var f in fleets)
			{
				if (f == null || f.Status == "WATCHLIST_DATA") continue;
				if (f.Status == "DOCKED") dockedCount++;
				else if (f.Status == "STATIONED") stationedCount++;
				else if (f.Status == "IN_TRANSIT") transitCount++;

				foreach (var kv in f.Units)
				{
					if (kv.Value <= 0) continue;
					totalFleetShips += kv.Value;
					var u = GameData.Units.Find(x => x.Id == kv.Key);
					if (u != null) totalFleetAtk += (long)u.Atk * kv.Value;
				}
			}

			long armoryUnits = 0;
			if (BaseHUD.Instance?.UnitInventory != null)
			{
				foreach (var kv in BaseHUD.Instance.UnitInventory)
					if (kv.Value > 0) armoryUnits += kv.Value;
			}
			int wlCount = SupabaseService.CachedWatchlist != null ? SupabaseService.CachedWatchlist.Count : 0;

			// 0. Bright Holographic Command Telemetry Header Strip
			PanelContainer telemetryBanner = CreateSlimHiTechCard(new Color("#00F0FF"), new Color(0.07f, 0.15f, 0.27f, 0.98f), 4);
			HBoxContainer teleRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			teleRow.AddThemeConstantOverride("separation", 8);

			Label teleLeft = new Label
			{
				Text = $"🛰️ FLEET COMMAND  ·  FLEETS: {dockedCount + stationedCount + transitCount:N0}  ·  SHIPS: {totalFleetShips:N0}",
				Modulate = Colors.White,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			teleLeft.AddThemeFontSizeOverride("font_size", 14);
			teleRow.AddChild(teleLeft);

			Label teleRight = new Label
			{
				Text = $"⚔️ ATK: {totalFleetAtk:N0}  ·  🛡️ ARMORY: {armoryUnits:N0}",
				Modulate = new Color("#38BDF8"),
				HorizontalAlignment = HorizontalAlignment.Right
			};
			teleRight.AddThemeFontSizeOverride("font_size", 14);
			teleRow.AddChild(teleRight);
			telemetryBanner.AddChild(teleRow);
			_contentStack.AddChild(telemetryBanner);

			// 1. Sleek 4-Column Horizontal Command Bar (Single Slim Row for Maximum Vertical Fleet List Space!)
			GridContainer tabGrid = new GridContainer
			{
				Columns = 4,
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			tabGrid.AddThemeConstantOverride("h_separation", 6);
			tabGrid.AddThemeConstantOverride("v_separation", 6);

			var tabs = new[]
			{
				new { Code = "LOCAL",     Label = $"⚓ AT BASE ({dockedCount})",         Accent = new Color("#00F0FF") },
				new { Code = "STATIONED", Label = $"🛰️ IN ORBIT ({stationedCount})",    Accent = new Color("#38BDF8") },
				new { Code = "TRANSIT",   Label = $"🚀 MISSIONS ({transitCount})",      Accent = new Color("#FBBF24") },
				new { Code = "ARCHITECT", Label = "🛠️ + NEW FLEET",                     Accent = new Color("#4ADE80") }
			};

			foreach (var t in tabs)
			{
				bool isSel = (_fleetTab == t.Code);
				Button b = MakeHiTechButton(t.Label, t.Accent, 0, 38, 13, isSel);
				b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				string capturedCode = t.Code;
				b.Pressed += () =>
				{
					_fleetTab = capturedCode;
					RefreshInspector();
				};
				tabGrid.AddChild(b);
			}
			_contentStack.AddChild(tabGrid);

			// 2. Watchlist Quick-Launcher Bar + High-Contrast Instant Search Filter (Built for 100s of Fleets!)
			if (_fleetTab != "ARCHITECT")
			{
				HBoxContainer commandBar = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				commandBar.AddThemeConstantOverride("separation", 6);

				string wlText = _showFleetWatchlistDrawer
					? $"⭐ HIDE SAVED WATCHLIST ({wlCount})"
					: $"⭐ SAVED TARGET WATCHLIST ({wlCount}) — QUICK LAUNCH";
				Button btnWatchlistToggle = MakeHiTechButton(wlText, new Color("#FBBF24"), 0, 36, 13, _showFleetWatchlistDrawer);
				btnWatchlistToggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				btnWatchlistToggle.Pressed += () =>
				{
					_showFleetWatchlistDrawer = !_showFleetWatchlistDrawer;
					RefreshInspector();
				};
				commandBar.AddChild(btnWatchlistToggle);

				LineEdit searchBox = new LineEdit
				{
					Text = _fleetSearchFilter,
					PlaceholderText = "🔍 Filter callsign / coord...",
					CustomMinimumSize = new Vector2(190, 36)
				};
				searchBox.AddThemeFontSizeOverride("font_size", 13);
				searchBox.AddThemeColorOverride("font_color", Colors.White);
				searchBox.AddThemeColorOverride("font_placeholder_color", new Color("#94A3B8"));
				searchBox.AddThemeStyleboxOverride("normal", MakeHiTechInputBoxStyle(new Color("#38BDF8")));
				searchBox.AddThemeStyleboxOverride("focus", MakeHiTechInputBoxStyle(new Color("#00F0FF")));
				searchBox.TextChanged += (val) =>
				{
					_fleetSearchFilter = val ?? "";
					RefreshInspector();
				};
				commandBar.AddChild(searchBox);

				if (!string.IsNullOrEmpty(_fleetSearchFilter))
				{
					Button btnClearSearch = MakeHiTechButton("✕", new Color("#EF4444"), 30, 30, 11);
					btnClearSearch.Pressed += () =>
					{
						_fleetSearchFilter = "";
						RefreshInspector();
					};
					commandBar.AddChild(btnClearSearch);
				}

				_contentStack.AddChild(commandBar);

				if (_showFleetWatchlistDrawer)
				{
					BuildWatchlistSectionCard(false);
				}
			}

			_fleetFeedbackLabel = new Label
			{
				Text = "",
				Modulate = new Color("#4ADE80"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			_fleetFeedbackLabel.AddThemeFontSizeOverride("font_size", 12);
			_contentStack.AddChild(_fleetFeedbackLabel);

			string filterClean = (_fleetSearchFilter ?? "").Trim().ToUpper();

			if (_fleetTab == "ARCHITECT")
			{
				BuildFleetArchitectSection();
			}
			else if (_fleetTab == "LOCAL")
			{
				int count = 0;
				foreach (var f in fleets)
				{
					if (f == null || f.Status != "DOCKED") continue;
					if (!string.IsNullOrEmpty(filterClean) &&
						!(f.Name ?? "").ToUpper().Contains(filterClean) &&
						!(f.LocationCoord ?? "").ToUpper().Contains(filterClean)) continue;
					count++;
					_contentStack.AddChild(CreateFleetManifestCard(f, count));
				}
				if (count == 0)
				{
					PanelContainer emptyP = CreateSlimHiTechCard(new Color("#00F0FF"), new Color(0.07f, 0.14f, 0.25f, 0.96f));
					VBoxContainer eBox = CreateCardContainer(emptyP);
					Label emptyMsg = new Label
					{
						Text = string.IsNullOrEmpty(filterClean)
							? "No fleets are currently parked at your base.\nClick '🛠️ + NEW FLEET' above to assemble a new strike or transport fleet from your Garrison Armory!"
							: $"No docked fleets match filter '{filterClean}'.",
						Modulate = Colors.White,
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					};
					emptyMsg.AddThemeFontSizeOverride("font_size", 12);
					eBox.AddChild(emptyMsg);
					_contentStack.AddChild(emptyP);
				}
			}
			else if (_fleetTab == "STATIONED")
			{
				int count = 0;
				foreach (var f in fleets)
				{
					if (f == null || f.Status != "STATIONED") continue;
					if (!string.IsNullOrEmpty(filterClean) &&
						!(f.Name ?? "").ToUpper().Contains(filterClean) &&
						!(f.LocationCoord ?? "").ToUpper().Contains(filterClean)) continue;
					count++;
					_contentStack.AddChild(CreateFleetManifestCard(f, count));
				}
				if (count == 0)
				{
					PanelContainer emptyP = CreateSlimHiTechCard(new Color("#38BDF8"), new Color(0.07f, 0.14f, 0.25f, 0.96f));
					VBoxContainer eBox = CreateCardContainer(emptyP);
					eBox.AddChild(new Label
					{
						Text = "No fleets are currently stationed in orbit at another Moon Slot.\nTip: Click any empty moon slot or neutral player base in the Moon Orbit view to move a fleet there as a forward staging base!",
						Modulate = Colors.White,
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					});
					_contentStack.AddChild(emptyP);
				}
			}
			else if (_fleetTab == "TRANSIT")
			{
				int count = 0;
				foreach (var f in fleets)
				{
					if (f == null || f.Status != "IN_TRANSIT") continue;
					if (!string.IsNullOrEmpty(filterClean) &&
						!(f.Name ?? "").ToUpper().Contains(filterClean) &&
						!(f.TargetCoord ?? "").ToUpper().Contains(filterClean)) continue;
					count++;
					_contentStack.AddChild(CreateFleetManifestCard(f, count));
				}
				if (count == 0)
				{
					PanelContainer emptyP = CreateSlimHiTechCard(new Color("#FBBF24"), new Color(0.07f, 0.14f, 0.25f, 0.96f));
					VBoxContainer eBox = CreateCardContainer(emptyP);
					eBox.AddChild(new Label
					{
						Text = "No fleets are currently flying missions in deep space.",
						Modulate = Colors.White,
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					});
					_contentStack.AddChild(emptyP);
				}
			}

			_contentStack.AddChild(new HSeparator());
		}

		private string _watchlistActionTargetKey = "";
		private string _watchlistActionProtocol = "";

		private async void RefreshWatchlistLiveInfoAsync()
		{
			if (SupabaseService.Instance == null || SupabaseService.CachedWatchlist == null || SupabaseService.CachedWatchlist.Count == 0) return;
			var all = await SupabaseService.Instance.FetchAllColoniesAsync();
			bool changed = false;
			foreach (var w in SupabaseService.CachedWatchlist)
			{
				var occ = all.Find(x => x.MoonId == w.MoonId && x.BaseSlot == w.SlotId);
				if (occ != null)
				{
					if (!w.IsOccupied || w.CommanderName != occ.CommanderName || w.AllianceName != occ.AllianceName)
					{
						w.IsOccupied = true;
						w.CommanderName = occ.CommanderName;
						w.BaseName = occ.BaseName;
						w.AllianceName = string.IsNullOrWhiteSpace(occ.AllianceName) ? "NONE" : occ.AllianceName.ToUpper();
						changed = true;
					}
				}
				else if (w.IsOccupied)
				{
					w.IsOccupied = false;
					w.CommanderName = "EMPTY MOON SLOT";
					w.BaseName = $"Open Slot B-{w.SlotId:D2}";
					w.AllianceName = "NONE";
					changed = true;
				}
			}
			if (changed && Visible)
			{
				RefreshInspector();
			}
		}

		private void BuildWatchlistSectionCard(bool isInsideRadar)
		{
			RefreshWatchlistLiveInfoAsync();

			PanelContainer wlCard = CreateSlimHiTechCard(new Color("#FBBF24"), new Color(0.06f, 0.11f, 0.19f, 0.98f), 4);
			VBoxContainer wlBox = CreateCardContainer(wlCard);

			Label wlTitle = new Label
			{
				Text = isInsideRadar
					? "⭐  SAVED TARGET WATCHLIST (DEEP SPACE RADAR TRACKING)"
					: "⭐  SAVED TARGET WATCHLIST — SELECT A SAVED COORDINATE TO LAUNCH",
				Modulate = new Color("#FBBF24"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			wlTitle.AddThemeFontSizeOverride("font_size", 13);
			wlBox.AddChild(wlTitle);

			var list = SupabaseService.CachedWatchlist ?? new List<GameMath.WatchlistEntry>();
			if (list.Count == 0)
			{
				Label emptyLbl = new Label
				{
					Text = "Your Watchlist is currently empty.\nClick on any player's base or any empty moon slot in the Moon Orbit view and tap '☆ SAVE TO WATCHLIST' to bookmark targets here!",
					Modulate = new Color("#E2E8F0"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				emptyLbl.AddThemeFontSizeOverride("font_size", 12);
				wlBox.AddChild(emptyLbl);
				_contentStack.AddChild(wlCard);
				return;
			}

			string myAlliance = (SupabaseService.CachedAllianceName ?? "NONE").Trim().ToUpper();

			foreach (var item in list.ToArray())
			{
				PanelContainer itemPanel = CreateSlimHiTechCard(
					item.IsOccupied ? new Color("#38BDF8") : new Color("#4ADE80"),
					new Color(0.04f, 0.08f, 0.15f, 0.96f),
					3
				);
				VBoxContainer itemBox = CreateCardContainer(itemPanel);
				itemBox.AddThemeConstantOverride("separation", 5);

				string statusTitle = item.IsOccupied
					? $"🎯 M-{item.MoonId}:B-{item.SlotId:D2}  ·  {item.CommanderName.ToUpper()} ({item.BaseName})  ·  [{item.AllianceName}]"
					: $"🌑 M-{item.MoonId}:B-{item.SlotId:D2}  ·  EMPTY MOON SLOT (OPEN)";

				HBoxContainer hdrRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				hdrRow.AddThemeConstantOverride("separation", 6);
				Label itemLbl = new Label
				{
					Text = statusTitle,
					Modulate = Colors.White,
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				itemLbl.AddThemeFontSizeOverride("font_size", 13);
				hdrRow.AddChild(itemLbl);

				int capMoon = item.MoonId;
				int capSlot = item.SlotId;

				Button btnViewMoon = MakeHiTechButton("🪐 VIEW MOON", new Color("#00F0FF"), 96, 30, 11);
				btnViewMoon.Pressed += () =>
				{
					MoonView.SelectedMoonToView = capMoon;
					CloseInspector();
					string moonTarget = ResourceLoader.Exists("res://Scripts/MoonView.tscn") ? "res://Scripts/MoonView.tscn" : "res://Scenes/MoonView.tscn";
					GetTree().ChangeSceneToFile(moonTarget);
				};
				hdrRow.AddChild(btnViewMoon);

				Button btnRemWl = MakeHiTechButton("✕", new Color("#EF4444"), 32, 30, 11);
				btnRemWl.Pressed += () =>
				{
					SupabaseService.RemoveFromWatchlist(capMoon, capSlot);
					BaseHUD.Instance?.TriggerInstantSave();
					RefreshInspector();
				};
				hdrRow.AddChild(btnRemWl);
				itemBox.AddChild(hdrRow);

				// Evaluate Attack / Move / Defend / Drop Off Units rules for this Watchlist entry
				string tAlliance = (item.AllianceName ?? "NONE").Trim().ToUpper();
				bool isMyOwnedBase = IsPlayerOwnedBaseCoordinate(capMoon, capSlot);
				bool sameAlliance = item.IsOccupied && !isMyOwnedBase && myAlliance != "NONE" && tAlliance == myAlliance;
				bool targetNoAlliance = tAlliance == "NONE" || string.IsNullOrEmpty(tAlliance);

				bool canAttack = item.IsOccupied && !isMyOwnedBase && !sameAlliance;
				bool canMove = isMyOwnedBase || !item.IsOccupied || targetNoAlliance;
				bool canDefend = item.IsOccupied && (sameAlliance || isMyOwnedBase);

				HBoxContainer actRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				actRow.AddThemeConstantOverride("separation", 6);
				string itemKey = $"{capMoon}:{capSlot}";

				if (item.IsOccupied && !isMyOwnedBase)
				{
					Button bAtk = MakeHiTechButton(
						canAttack ? "⚔️ ATTACK" : "🔒 ALLY",
						new Color("#EF4444"), 0, 32, 11,
						_watchlistActionTargetKey == itemKey && _watchlistActionProtocol == "RAID",
						!canAttack
					);
					bAtk.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					bAtk.Pressed += () =>
					{
						_watchlistActionTargetKey = itemKey;
						_watchlistActionProtocol = "RAID";
						RefreshInspector();
					};
					actRow.AddChild(bAtk);
				}

				Button bMov = MakeHiTechButton(
					isMyOwnedBase ? "🛰️ SEND / STATION FLEET" : (canMove ? "🛰️ MOVE FLEET" : $"🔒 MOVE ([{tAlliance}])"),
					new Color("#00F0FF"), 0, 32, 11,
					_watchlistActionTargetKey == itemKey && _watchlistActionProtocol == "MOVE",
					!canMove
				);
				bMov.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				bMov.Pressed += () =>
				{
					_watchlistActionTargetKey = itemKey;
					_watchlistActionProtocol = "MOVE";
					RefreshInspector();
				};
				actRow.AddChild(bMov);

				if (isMyOwnedBase)
				{
					Button bDropOff = MakeHiTechButton(
						"🪂 SEND & DROP OFF UNITS",
						new Color("#22C55E"), 0, 32, 11,
						_watchlistActionTargetKey == itemKey && _watchlistActionProtocol == "DROPOFF"
					);
					bDropOff.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					bDropOff.Pressed += () =>
					{
						_watchlistActionTargetKey = itemKey;
						_watchlistActionProtocol = "DROPOFF";
						RefreshInspector();
					};
					actRow.AddChild(bDropOff);
				}
				else if (item.IsOccupied)
				{
					Button bDef = MakeHiTechButton(
						canDefend ? "🛡️ DEFEND ALLY" : "🔒 DEFEND (ALLY ONLY)",
						new Color("#4ADE80"), 0, 32, 11,
						_watchlistActionTargetKey == itemKey && _watchlistActionProtocol == "REINFORCE",
						!canDefend
					);
					bDef.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					bDef.Pressed += () =>
					{
						_watchlistActionTargetKey = itemKey;
						_watchlistActionProtocol = "REINFORCE";
						RefreshInspector();
					};
					actRow.AddChild(bDef);
				}

				itemBox.AddChild(actRow);

				// If player clicked Attack / Move / Defend on this Watchlist item, show available fleets to launch right here!
				if (_watchlistActionTargetKey == itemKey && !string.IsNullOrEmpty(_watchlistActionProtocol))
				{
					var fleets = BaseHUD.Instance?.SavedFleets ?? new List<GameMath.FleetManifest>();
					string activeSrv = SupabaseService.CachedServerId;
					string targetCoordStr = $"{activeSrv}:{capMoon}:{capSlot}";
					int availFleets = 0;

					foreach (var flt in fleets)
					{
						if (flt == null || (flt.Status != "DOCKED" && flt.Status != "STATIONED")) continue;
						if (string.Equals(flt.LocationCoord, targetCoordStr, StringComparison.OrdinalIgnoreCase)) continue;
						// If player clicked "LAUNCH" on a specific fleet card, highlight or filter to that fleet first
						if (!string.IsNullOrEmpty(_quickDispatchFleetId) && flt.Id != _quickDispatchFleetId) continue;
						availFleets++;

						GameMath.TryParseSectorCoordinate(flt.LocationCoord, out int oMoon, out int oSlot);
						float srvSpd = BaseHUD.Instance != null ? Math.Max(1.0f, BaseHUD.Instance.ServerSpeed) : Math.Max(1.0f, SupabaseService.CachedServerSpeed);
						int tSec = GameMath.CalcFleetTravelTimeSeconds(oMoon, oSlot, capMoon, capSlot, flt.Units, srvSpd);

						Color launchCol = _watchlistActionProtocol == "RAID"
							? new Color("#EF4444")
							: (_watchlistActionProtocol == "REINFORCE" ? new Color("#4ADE80") : new Color("#00F0FF"));
						Button btnSendThis = MakeHiTechButton(
							$"🚀 LAUNCH [{flt.Name}] (from M-{oMoon}:B-{oSlot:D2})  ·  ETA: {GameMath.FormatTime(tSec)}",
							launchCol, 0, 34, 12, true
						);
						btnSendThis.SizeFlagsHorizontal = SizeFlags.ExpandFill;
						var capturedFlt = flt;
						string capturedProto = _watchlistActionProtocol;
						btnSendThis.Pressed += () =>
						{
							_watchlistActionTargetKey = "";
							_watchlistActionProtocol = "";
							_quickDispatchFleetId = "";
							DispatchFleetToCoords(capturedFlt, capMoon, capSlot, capturedProto);
						};
						itemBox.AddChild(btnSendThis);
					}

					if (availFleets == 0)
					{
						Label noneLbl = new Label
						{
							Text = "No available fleets ready to launch. Create a fleet in '🛠️ + CREATE NEW FLEET' first!",
							Modulate = new Color("#F59E0B"),
							AutowrapMode = TextServer.AutowrapMode.WordSmart
						};
						noneLbl.AddThemeFontSizeOverride("font_size", 12);
						itemBox.AddChild(noneLbl);
					}
				}

				wlBox.AddChild(itemPanel);
			}

			_contentStack.AddChild(wlCard);
		}

		private PanelContainer CreateFleetManifestCard(GameMath.FleetManifest f, int displayIndex = 1)
		{
			long atk = 0, def = 0, hp = 0, cap = 0;
			int totalShips = 0;
			int laserCannons = f.Units.GetValueOrDefault("u6", 0);
			var unitTags = new List<string>();

			foreach (var kvp in f.Units)
			{
				if (kvp.Value <= 0) continue;
				totalShips += kvp.Value;
				var u = GameData.Units.Find(x => x.Id == kvp.Key);
				if (u != null)
				{
					atk += (long)u.Atk * kvp.Value;
					def += (long)u.Def * kvp.Value;
					hp += (long)u.Hp * kvp.Value;
					cap += (long)u.Cargo * kvp.Value;
					unitTags.Add($"{u.Name} x{kvp.Value:N0}");
				}
			}
			long loadedCargo = f.Cargo.E + f.Cargo.I + f.Cargo.T + f.Cargo.H3;

			Color cardAccent = f.Status == "DOCKED"
				? new Color("#00F0FF")
				: (f.Status == "STATIONED" ? new Color("#4ADE80") : new Color("#FBBF24"));

			PanelContainer card = CreateSlimHiTechCard(cardAccent, new Color(0.07f, 0.14f, 0.25f, 0.98f), 4);
			VBoxContainer box = CreateCardContainer(card);
			box.AddThemeConstantOverride("separation", 4);

			bool isEditingThis = (_editingFleetId == f.Id);
			bool isDetailsOpen = !string.IsNullOrEmpty(f.Id) && _expandedFleetDetails.Contains(f.Id);

			// Row 1 (Ultra-Slim Holographic Command Strip): Callsign + Telemetry + Location/ETA + Quick Action Buttons
			HBoxContainer topRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			topRow.AddThemeConstantOverride("separation", 6);

			Label idxBadge = new Label
			{
				Text = $"#{displayIndex:D2}",
				Modulate = cardAccent
			};
			idxBadge.AddThemeFontSizeOverride("font_size", 14);
			topRow.AddChild(idxBadge);

			Label nameLbl = new Label
			{
				Text = $"🚀 {f.Name.ToUpper()}",
				Modulate = Colors.White
			};
			nameLbl.AddThemeFontSizeOverride("font_size", 15);
			nameLbl.AddThemeColorOverride("font_outline_color", Colors.Black);
			nameLbl.AddThemeConstantOverride("outline_size", 2);
			topRow.AddChild(nameLbl);

			string cargoBadgeText = loadedCargo > 0
				? $"📦 {loadedCargo:N0}/{cap:N0}"
				: $"📦 0/{cap:N0}";
			Label telemetryInline = new Label
			{
				Text = $"  ·  {totalShips:N0} Ships  ·  ⚔️{atk:N0}  🛡️{def:N0}  ❤️{hp:N0}  ·  {cargoBadgeText}",
				Modulate = new Color("#7DD3FC"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true
			};
			telemetryInline.AddThemeFontSizeOverride("font_size", 13);
			topRow.AddChild(telemetryInline);

			string statusBadge = f.Status == "DOCKED" ? "AT BASE" : (f.Status == "STATIONED" ? "IN ORBIT" : "IN FLIGHT");
			string locInfo = f.Status == "IN_TRANSIT"
				? $"{f.Protocol} ➔ {f.TargetCoord} ({GameMath.FormatTime((int)Math.Ceiling(f.EtaSecondsLeft))})"
				: $"📍 {f.LocationCoord} ({statusBadge})";
			Label locLbl = new Label
			{
				Text = locInfo,
				Modulate = f.Status == "IN_TRANSIT" ? new Color("#FDE047") : cardAccent,
				HorizontalAlignment = HorizontalAlignment.Right
			};
			locLbl.AddThemeFontSizeOverride("font_size", 13);
			if (f.Status == "IN_TRANSIT" && !string.IsNullOrEmpty(f.Id))
			{
				_fleetTransitLocLabels[f.Id] = locLbl;
			}
			topRow.AddChild(locLbl);
			box.AddChild(topRow);

			// Row 2 (Sleek High-Contrast Action & Unit Bar): Everything accessible in 1 click without bulky vertical bloat
			HBoxContainer actionRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			actionRow.AddThemeConstantOverride("separation", 5);

			// Toggle compact unit & cargo manifest pill
			string unitSummaryShort = unitTags.Count > 0
				? (unitTags.Count <= 2 ? string.Join(" · ", unitTags) : $"{unitTags[0]} · {unitTags[1]} (+{unitTags.Count - 2} more)")
				: "No Units";
			Button btnToggleUnits = MakeHiTechButton(
				isDetailsOpen ? $"▾ {unitSummaryShort}" : $"▸ {unitSummaryShort}",
				new Color("#38BDF8"), 0, 34, 13, isDetailsOpen
			);
			btnToggleUnits.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			string capturedId = f.Id ?? "";
			btnToggleUnits.Pressed += () =>
			{
				if (_expandedFleetDetails.Contains(capturedId)) _expandedFleetDetails.Remove(capturedId);
				else _expandedFleetDetails.Add(capturedId);
				RefreshInspector();
			};
			actionRow.AddChild(btnToggleUnits);

			if (f.Status == "DOCKED")
			{
				Button btnUseWatchlist = MakeHiTechButton(
					"🎯 LAUNCH",
					new Color("#FBBF24"), 102, 34, 13,
					_showFleetWatchlistDrawer && _quickDispatchFleetId == f.Id
				);
				btnUseWatchlist.Pressed += () =>
				{
					_quickDispatchFleetId = (_quickDispatchFleetId == f.Id && _showFleetWatchlistDrawer) ? "" : f.Id;
					_showFleetWatchlistDrawer = true;
					RefreshInspector();
				};
				actionRow.AddChild(btnUseWatchlist);

				Button btnEditToggle = MakeHiTechButton(
					isEditingThis ? "✓ DONE" : "✏️ EDIT",
					isEditingThis ? new Color("#4ADE80") : new Color("#00F0FF"),
					88, 34, 13, isEditingThis
				);
				btnEditToggle.Pressed += () =>
				{
					_editingFleetId = isEditingThis ? "" : f.Id;
					RefreshInspector();
				};
				actionRow.AddChild(btnEditToggle);

				if (laserCannons > 0)
				{
					string siegeTargetName = GameMath.GetSiegeTargetDisplayName(f.TargetBuildingId);
					Button btnCycleFleetTarget = MakeHiTechButton(
						$"💥 {siegeTargetName.ToUpper()}",
						new Color("#F97316"), 0, 34, 12, true
					);
					btnCycleFleetTarget.Pressed += () =>
					{
						int curIdx = 0;
						for (int i = 0; i < GameMath.SiegeTargetBuildings.Length; i++)
						{
							if (GameMath.SiegeTargetBuildings[i].Id == f.TargetBuildingId)
							{
								curIdx = i;
								break;
							}
						}
						int nextIdx = (curIdx + 1) % GameMath.SiegeTargetBuildings.Length;
						f.TargetBuildingId = GameMath.SiegeTargetBuildings[nextIdx].Id;
						BaseHUD.Instance?.TriggerInstantSave();
						RefreshInspector();
					};
					actionRow.AddChild(btnCycleFleetTarget);
				}

				// Like the Laser Cannon button rule: the DROP OFF UNITS button appears when the fleet registers it is at the player's owned coordinates!
				if (IsFleetAtPlayerOwnedCoordinate(f))
				{
					Button btnDropOffHere = MakeHiTechButton("🪂 DROP OFF UNITS", new Color("#22C55E"), 136, 34, 12, true);
					btnDropOffHere.Pressed += () => DropOffFleetUnitsToOwnedBase(f);
					actionRow.AddChild(btnDropOffHere);
				}

				Button btnDisband = MakeHiTechButton("📦 UNPACK", new Color("#EF4444"), 92, 34, 12);
				btnDisband.Pressed += () => DisbandFleetToGarrison(f);
				actionRow.AddChild(btnDisband);
			}
			else if (f.Status == "STATIONED")
			{
				Button btnForwardMove = MakeHiTechButton(
					"🚀 LAUNCH FROM ORBIT",
					new Color("#4ADE80"), 164, 34, 13,
					_showFleetWatchlistDrawer && _quickDispatchFleetId == f.Id
				);
				btnForwardMove.Pressed += () =>
				{
					_quickDispatchFleetId = (_quickDispatchFleetId == f.Id && _showFleetWatchlistDrawer) ? "" : f.Id;
					_showFleetWatchlistDrawer = true;
					RefreshInspector();
				};
				actionRow.AddChild(btnForwardMove);

				if (laserCannons > 0)
				{
					string siegeTargetName = GameMath.GetSiegeTargetDisplayName(f.TargetBuildingId);
					Button btnCycleFleetTarget = MakeHiTechButton($"💥 {siegeTargetName.ToUpper()}", new Color("#F97316"), 0, 34, 12, true);
					btnCycleFleetTarget.Pressed += () =>
					{
						int curIdx = 0;
						for (int i = 0; i < GameMath.SiegeTargetBuildings.Length; i++)
						{
							if (GameMath.SiegeTargetBuildings[i].Id == f.TargetBuildingId)
							{
								curIdx = i;
								break;
							}
						}
						int nextIdx = (curIdx + 1) % GameMath.SiegeTargetBuildings.Length;
						f.TargetBuildingId = GameMath.SiegeTargetBuildings[nextIdx].Id;
						BaseHUD.Instance?.TriggerInstantSave();
						RefreshInspector();
					};
					actionRow.AddChild(btnCycleFleetTarget);
				}

				// Like the Laser Cannon button rule: the DROP OFF UNITS button appears ONLY when the fleet registers it is at the player's owned coordinates!
				if (IsFleetAtPlayerOwnedCoordinate(f))
				{
					Button btnDropOffStationed = MakeHiTechButton("🪂 DROP OFF UNITS", new Color("#22C55E"), 136, 34, 12, true);
					btnDropOffStationed.Pressed += () => DropOffFleetUnitsToOwnedBase(f);
					actionRow.AddChild(btnDropOffStationed);
				}

				if (loadedCargo > 0)
				{
					Button btnOffload = MakeHiTechButton("📦 DELIVER", new Color("#FBBF24"), 96, 34, 12);
					btnOffload.Pressed += () => OffloadFleetCargo(f);
					actionRow.AddChild(btnOffload);
				}

				Button btnRecall = MakeHiTechButton("↩ RECALL", new Color("#00F0FF"), 92, 34, 12);
				btnRecall.Pressed += () => RecallStationedFleet(f);
				actionRow.AddChild(btnRecall);
			}
			else if (f.Status == "IN_TRANSIT" && f.Protocol != "RETURN")
			{
				Button btnAbort = MakeHiTechButton("✕ CANCEL & RETURN HOME", new Color("#EF4444"), 190, 34, 12);
				btnAbort.Pressed += async () =>
				{
					string prevTargetCmdr = f.TargetCommanderName;
					string fleetId = f.Id;
					long abortNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
					double elapsedSoFar = f.TotalFlightDurationSec > 0
						? Math.Max(10.0, f.TotalFlightDurationSec - f.EtaSecondsLeft)
						: Math.Max(10.0, f.EtaSecondsLeft);
					string activeSrv = SupabaseService.CachedServerId;
					f.Protocol = "RETURN";
					f.LocationCoord = f.TargetCoord;
					f.TargetCoord = $"{activeSrv}:{SupabaseService.CachedMoonId}:{SupabaseService.CachedBaseSlot}";
					f.TotalFlightDurationSec = elapsedSoFar;
					f.DepartureTimeUnixMs = abortNowMs;
					f.EtaSecondsLeft = elapsedSoFar;
					f.ArrivalTimeUnixMs = abortNowMs + (long)(elapsedSoFar * 1000.0);
					SupabaseService.MarkLocalFleetMutated(f);
					BaseHUD.Instance?.TriggerInstantSave();
					MoonView.Instance?.RefreshMoonSlotVisuals();

					if (!string.IsNullOrEmpty(prevTargetCmdr) && SupabaseService.Instance != null)
					{
						await SupabaseService.Instance.SendInboxSignalAsync(
							SupabaseService.CachedCommanderName,
							prevTargetCmdr,
							"INTEL",
							$"CANCEL_FLEET:{fleetId}",
							fleetId
						);
					}
					RefreshInspector();
				};
				actionRow.AddChild(btnAbort);
			}

			box.AddChild(actionRow);

			// Optional Expanded Unit Breakdown & Cargo Readout (when player clicks the unit pill)
			if (isDetailsOpen)
			{
				Label fullCompLbl = new Label
				{
					Text = $"📋 Ships: {string.Join("  ·  ", unitTags)}\n📦 Cargo Hold: ⚡ Power {f.Cargo.E:N0}  |  ⛏️ Iron {f.Cargo.I:N0}  |  💎 Titanium {f.Cargo.T:N0}  |  ⛽ Helium-3 {f.Cargo.H3:N0}  ({loadedCargo:N0} / {cap:N0})",
					Modulate = new Color("#E2E8F0"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				fullCompLbl.AddThemeFontSizeOverride("font_size", 11);
				box.AddChild(fullCompLbl);
			}

			if (isEditingThis && f.Status == "DOCKED")
			{
				box.AddChild(new HSeparator());
				BuildInlineFleetEditor(box, f, cap, loadedCargo);
			}

			return card;
		}

		private void BuildInlineFleetEditor(VBoxContainer parentBox, GameMath.FleetManifest f, long currentMaxCap, long currentLoadedCargo)
		{
			HBoxContainer topEditBar = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			topEditBar.AddThemeConstantOverride("separation", 8);

			Label editHdr = new Label
			{
				Text = $"✏️ EDITING [{f.Name.ToUpper()}] — ADD OR REMOVE UNITS & CARGO",
				Modulate = new Color("#4ADE80"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			editHdr.AddThemeFontSizeOverride("font_size", 12);
			topEditBar.AddChild(editHdr);

			Label qtyStepLbl = new Label { Text = "Step Qty:", Modulate = Colors.White };
			qtyStepLbl.AddThemeFontSizeOverride("font_size", 11);
			topEditBar.AddChild(qtyStepLbl);

			LineEdit stepInput = new LineEdit
			{
				Text = _fleetEditUnitStep,
				PlaceholderText = "Qty",
				CustomMinimumSize = new Vector2(74, 28)
			};
			stepInput.AddThemeFontSizeOverride("font_size", 11);
			stepInput.AddThemeColorOverride("font_color", Colors.White);
			stepInput.AddThemeStyleboxOverride("normal", MakeHiTechInputBoxStyle(new Color("#4ADE80")));
			stepInput.AddThemeStyleboxOverride("focus", MakeHiTechInputBoxStyle(new Color("#00F0FF")));
			stepInput.TextChanged += (val) => _fleetEditUnitStep = val;
			topEditBar.AddChild(stepInput);
			parentBox.AddChild(topEditBar);

			var inv = BaseHUD.Instance?.UnitInventory;
			foreach (var u in GameData.Units)
			{
				int inArmory = inv != null ? inv.GetValueOrDefault(u.Id, 0) : 0;
				int inFleet = f.Units.GetValueOrDefault(u.Id, 0);
				if (inArmory <= 0 && inFleet <= 0) continue;

				HBoxContainer uRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				uRow.AddThemeConstantOverride("separation", 6);

				Label uInfo = new Label
				{
					Text = $"● {u.Name}   (In Fleet: {inFleet:N0}  |  In Armory: {inArmory:N0})",
					Modulate = Colors.White,
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				uInfo.AddThemeFontSizeOverride("font_size", 12);
				uRow.AddChild(uInfo);

				string uId = u.Id;
				Button bAddCustom = MakeHiTechButton("+QTY", new Color("#4ADE80"), 58, 30, 11, false, inArmory < 1);
				bAddCustom.Pressed += () =>
				{
					int req = int.TryParse(_fleetEditUnitStep, out int parsed) && parsed > 0 ? parsed : 1;
					int take = Math.Min(req, inArmory);
					if (take > 0 && inv != null)
					{
						inv[uId] = inArmory - take;
						f.Units[uId] = inFleet + take;
						BaseHUD.Instance?.TriggerInstantSave();
						RefreshInspector();
					}
				};
				uRow.AddChild(bAddCustom);

				Button bAddAll = MakeHiTechButton("+ALL", new Color("#00F0FF"), 54, 30, 11, false, inArmory < 1);
				bAddAll.Pressed += () =>
				{
					if (inArmory > 0 && inv != null)
					{
						inv[uId] = 0;
						f.Units[uId] = inFleet + inArmory;
						BaseHUD.Instance?.TriggerInstantSave();
						RefreshInspector();
					}
				};
				uRow.AddChild(bAddAll);

				Button bRemCustom = MakeHiTechButton("-QTY", new Color("#F59E0B"), 58, 30, 11, false, inFleet < 1);
				bRemCustom.Pressed += () =>
				{
					int req = int.TryParse(_fleetEditUnitStep, out int parsed) && parsed > 0 ? parsed : 1;
					int totalShipsNow = 0;
					foreach (var kv in f.Units) totalShipsNow += kv.Value;
					int maxRem = Math.Min(inFleet, totalShipsNow - 1);
					int giveBack = Math.Min(req, maxRem);
					if (giveBack > 0 && inv != null)
					{
						inv[uId] = inArmory + giveBack;
						f.Units[uId] = inFleet - giveBack;
						if (f.Units[uId] <= 0) f.Units.Remove(uId);
						BaseHUD.Instance?.TriggerInstantSave();
						RefreshInspector();
					}
				};
				uRow.AddChild(bRemCustom);

				parentBox.AddChild(uRow);
			}

			// Cargo Hold Resource Editing (Add or Remove Power, Iron, Titanium, Helium-3)
			Label cargoEditHdr = new Label
			{
				Text = $"📦 CARGO HOLD RESOURCES ({currentLoadedCargo:N0} / {currentMaxCap:N0} Cap):",
				Modulate = new Color("#FBBF24"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			cargoEditHdr.AddThemeFontSizeOverride("font_size", 11);
			parentBox.AddChild(cargoEditHdr);

			GridContainer cGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			cGrid.AddThemeConstantOverride("h_separation", 6);
			cGrid.AddThemeConstantOverride("v_separation", 6);

			long remCap = Math.Max(0, currentMaxCap - currentLoadedCargo);
			AddFleetCargoTransferButtons(cGrid, f, "E", "⚡ Power", remCap);
			AddFleetCargoTransferButtons(cGrid, f, "I", "⛏️ Iron", remCap);
			AddFleetCargoTransferButtons(cGrid, f, "T", "💎 Titanium", remCap);
			AddFleetCargoTransferButtons(cGrid, f, "H3", "⛽ Helium-3", remCap);

			parentBox.AddChild(cGrid);
		}

		private void AddFleetCargoTransferButtons(GridContainer grid, GameMath.FleetManifest f, string resType, string label, long remCap)
		{
			HBoxContainer pair = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			pair.AddThemeConstantOverride("separation", 4);

			Button btnLoad = MakeHiTechButton($"+1K {label}", new Color("#4ADE80"), 0, 30, 11, false, remCap <= 0);
			btnLoad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btnLoad.Pressed += () =>
			{
				if (BaseHUD.Instance == null || remCap <= 0) return;
				long amt = Math.Min(1000L, remCap);
				if (resType == "E" && BaseHUD.Instance.ResE >= amt) { BaseHUD.Instance.ResE -= amt; f.Cargo.E += amt; }
				else if (resType == "I" && BaseHUD.Instance.ResI >= amt) { BaseHUD.Instance.ResI -= amt; f.Cargo.I += amt; }
				else if (resType == "T" && BaseHUD.Instance.ResT >= amt) { BaseHUD.Instance.ResT -= amt; f.Cargo.T += amt; }
				else if (resType == "H3" && BaseHUD.Instance.ResH3 >= amt) { BaseHUD.Instance.ResH3 -= amt; f.Cargo.H3 += amt; }
				BaseHUD.Instance.UpdateHUDDisplay();
				BaseHUD.Instance.TriggerInstantSave();
				RefreshInspector();
			};

			Button btnUnload = MakeHiTechButton($"-1K {label}", new Color("#F59E0B"), 0, 30, 11);
			btnUnload.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btnUnload.Pressed += () =>
			{
				if (BaseHUD.Instance == null) return;
				if (resType == "E" && f.Cargo.E > 0) { long give = Math.Min(1000L, f.Cargo.E); f.Cargo.E -= give; BaseHUD.Instance.ResE = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResE + give); }
				else if (resType == "I" && f.Cargo.I > 0) { long give = Math.Min(1000L, f.Cargo.I); f.Cargo.I -= give; BaseHUD.Instance.ResI = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResI + give); }
				else if (resType == "T" && f.Cargo.T > 0) { long give = Math.Min(1000L, f.Cargo.T); f.Cargo.T -= give; BaseHUD.Instance.ResT = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResT + give); }
				else if (resType == "H3" && f.Cargo.H3 > 0) { long give = Math.Min(1000L, f.Cargo.H3); f.Cargo.H3 -= give; BaseHUD.Instance.ResH3 = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResH3 + give); }
				BaseHUD.Instance.UpdateHUDDisplay();
				BaseHUD.Instance.TriggerInstantSave();
				RefreshInspector();
			};

			pair.AddChild(btnLoad);
			pair.AddChild(btnUnload);
			grid.AddChild(pair);
		}

		private string _draftUnitCustomStep = "25";

		private void BuildFleetArchitectSection()
		{
			long totalCap = 0;
			long totalAtk = 0;
			long totalDef = 0;
			int totalSelectedUnits = 0;
			int draftLaserCannons = _draftFleetUnits.GetValueOrDefault("u6", 0);
			foreach (var kvp in _draftFleetUnits)
			{
				totalSelectedUnits += kvp.Value;
				var u = GameData.Units.Find(x => x.Id == kvp.Key);
				if (u != null)
				{
					totalCap += (long)u.Cargo * kvp.Value;
					totalAtk += (long)u.Atk * kvp.Value;
					totalDef += (long)u.Def * kvp.Value;
				}
			}
			long currentCargo = _draftCargoE + _draftCargoI + _draftCargoT + _draftCargoH3;

			// Card 1: Slim Fleet Identity & Live Telemetry Header
			PanelContainer topCard = CreateSlimHiTechCard(new Color("#4ADE80"), new Color(0.07f, 0.15f, 0.27f, 0.98f), 4);
			VBoxContainer topBox = CreateCardContainer(topCard);
			topBox.AddThemeConstantOverride("separation", 4);

			HBoxContainer nameRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			nameRow.AddThemeConstantOverride("separation", 6);
			Label namePrompt = new Label { Text = "🛠️ CALLSIGN:", Modulate = new Color("#4ADE80") };
			namePrompt.AddThemeFontSizeOverride("font_size", 11);
			LineEdit nameInput = new LineEdit
			{
				Text = _newFleetCallsign,
				PlaceholderText = "Enter Fleet Name...",
				CustomMinimumSize = new Vector2(140, 28),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			nameInput.AddThemeFontSizeOverride("font_size", 11);
			nameInput.AddThemeColorOverride("font_color", Colors.White);
			nameInput.AddThemeColorOverride("font_placeholder_color", new Color("#94A3B8"));
			nameInput.AddThemeStyleboxOverride("normal", MakeHiTechInputBoxStyle(new Color("#4ADE80")));
			nameInput.AddThemeStyleboxOverride("focus", MakeHiTechInputBoxStyle(new Color("#00F0FF")));
			nameInput.TextChanged += (val) => _newFleetCallsign = val.ToUpper();
			nameRow.AddChild(namePrompt);
			nameRow.AddChild(nameInput);

			Button btnQuickSaveTop = MakeHiTechButton("✓ SAVE FLEET", new Color("#4ADE80"), 112, 28, 11, totalSelectedUnits > 0, totalSelectedUnits <= 0);
			btnQuickSaveTop.Pressed += SaveDraftFleetToHangar;
			nameRow.AddChild(btnQuickSaveTop);
			topBox.AddChild(nameRow);

			Label capLbl = new Label
			{
				Text = $"🚀 Ships Selected: {totalSelectedUnits:N0}   ·   ⚔️ ATK: {totalAtk:N0}   ·   🛡️ DEF: {totalDef:N0}   ·   📦 Cargo Hold: {currentCargo:N0} / {totalCap:N0}",
				Modulate = currentCargo > totalCap ? new Color("#EF4444") : new Color("#7DD3FC"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			capLbl.AddThemeFontSizeOverride("font_size", 11);
			topBox.AddChild(capLbl);
			_contentStack.AddChild(topCard);

			// Card 2: Slim High-Tech Unit Selector Rows from Garrison Armory
			PanelContainer shipCard = CreateSlimHiTechCard(new Color("#00F0FF"), new Color(0.06f, 0.13f, 0.24f, 0.98f), 4);
			VBoxContainer shipBox = CreateCardContainer(shipCard);
			shipBox.AddThemeConstantOverride("separation", 4);

			HBoxContainer shipHdrRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			shipHdrRow.AddThemeConstantOverride("separation", 6);
			Label shipHdr = new Label
			{
				Text = "⚔️ ASSIGN UNITS FROM ARMORY",
				Modulate = Colors.White,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			shipHdr.AddThemeFontSizeOverride("font_size", 11);
			shipHdrRow.AddChild(shipHdr);

			Label customStepLbl = new Label { Text = "Qty:", Modulate = new Color("#7DD3FC") };
			customStepLbl.AddThemeFontSizeOverride("font_size", 11);
			shipHdrRow.AddChild(customStepLbl);

			LineEdit customStepInput = new LineEdit
			{
				Text = _draftUnitCustomStep,
				PlaceholderText = "Qty",
				CustomMinimumSize = new Vector2(56, 26)
			};
			customStepInput.AddThemeFontSizeOverride("font_size", 11);
			customStepInput.AddThemeColorOverride("font_color", Colors.White);
			customStepInput.AddThemeStyleboxOverride("normal", MakeHiTechInputBoxStyle(new Color("#38BDF8")));
			customStepInput.AddThemeStyleboxOverride("focus", MakeHiTechInputBoxStyle(new Color("#00F0FF")));
			customStepInput.TextChanged += (val) => _draftUnitCustomStep = val;
			shipHdrRow.AddChild(customStepInput);

			var inv = BaseHUD.Instance?.UnitInventory;
			Button btnAddAllArmory = MakeHiTechButton("+ ALL ARMORY", new Color("#4ADE80"), 108, 26, 10);
			btnAddAllArmory.Pressed += () =>
			{
				if (inv == null) return;
				foreach (var u in GameData.Units)
				{
					int avail = inv.GetValueOrDefault(u.Id, 0);
					if (avail > 0)
					{
						_draftFleetUnits[u.Id] = _draftFleetUnits.GetValueOrDefault(u.Id, 0) + avail;
						inv[u.Id] = 0;
					}
				}
				RefreshInspector();
			};
			shipHdrRow.AddChild(btnAddAllArmory);
			shipBox.AddChild(shipHdrRow);

			int anyUnitsShown = 0;
			foreach (var u in GameData.Units)
			{
				int avail = inv != null ? inv.GetValueOrDefault(u.Id, 0) : 0;
				int inDraft = _draftFleetUnits.GetValueOrDefault(u.Id, 0);
				if (avail <= 0 && inDraft <= 0) continue;
				anyUnitsShown++;

				PanelContainer uStrip = CreateSlimHiTechCard(
					inDraft > 0 ? new Color("#4ADE80") : new Color("#38BDF8"),
					inDraft > 0 ? new Color(0.06f, 0.16f, 0.24f, 0.98f) : new Color(0.05f, 0.11f, 0.20f, 0.96f),
					3
				);
				HBoxContainer uRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				uRow.AddThemeConstantOverride("separation", 4);

				VBoxContainer uTextCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				uTextCol.AddThemeConstantOverride("separation", 0);
				Label uLbl = new Label
				{
					Text = $"{u.Name.ToUpper()}  ·  In Fleet: {inDraft:N0}  ·  Armory: {avail:N0}",
					Modulate = inDraft > 0 ? new Color("#4ADE80") : Colors.White,
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					ClipText = true,
					TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
				};
				uLbl.AddThemeFontSizeOverride("font_size", 11);
				Label uSub = new Label
				{
					Text = $"⚔️ATK {u.Atk} · 🛡️DEF {u.Def} · ❤️HP {u.Hp} · 📦Cargo {u.Cargo}",
					Modulate = new Color("#7DD3FC"),
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					ClipText = true,
					TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
				};
				uSub.AddThemeFontSizeOverride("font_size", 10);
				uTextCol.AddChild(uLbl);
				uTextCol.AddChild(uSub);
				uRow.AddChild(uTextCol);

				string uId = u.Id;
				Button btnAdd1 = MakeHiTechButton("+1", new Color("#00F0FF"), 34, 26, 10, false, avail < 1);
				btnAdd1.Pressed += () => { inv[uId] = avail - 1; _draftFleetUnits[uId] = inDraft + 1; RefreshInspector(); };
				uRow.AddChild(btnAdd1);

				Button btnAdd10 = MakeHiTechButton("+10", new Color("#00F0FF"), 40, 26, 10, false, avail < 1);
				btnAdd10.Pressed += () =>
				{
					int take = Math.Min(10, avail);
					inv[uId] = avail - take;
					_draftFleetUnits[uId] = inDraft + take;
					RefreshInspector();
				};
				uRow.AddChild(btnAdd10);

				Button btnAddStep = MakeHiTechButton("+QTY", new Color("#38BDF8"), 44, 26, 10, false, avail < 1);
				btnAddStep.Pressed += () =>
				{
					int req = int.TryParse(_draftUnitCustomStep, out int p) && p > 0 ? p : 25;
					int take = Math.Min(req, avail);
					inv[uId] = avail - take;
					_draftFleetUnits[uId] = inDraft + take;
					RefreshInspector();
				};
				uRow.AddChild(btnAddStep);

				Button btnAddAll = MakeHiTechButton("ALL", new Color("#4ADE80"), 42, 26, 10, false, avail < 1);
				btnAddAll.Pressed += () => { inv[uId] = 0; _draftFleetUnits[uId] = inDraft + avail; RefreshInspector(); };
				uRow.AddChild(btnAddAll);

				Button btnRem = MakeHiTechButton("CLR", new Color("#EF4444"), 42, 26, 10, false, inDraft < 1);
				btnRem.Pressed += () => { inv[uId] = avail + inDraft; _draftFleetUnits[uId] = 0; RefreshInspector(); };
				uRow.AddChild(btnRem);

				uStrip.AddChild(uRow);
				shipBox.AddChild(uStrip);
			}

			if (anyUnitsShown == 0)
			{
				Label noShipsLbl = new Label
				{
					Text = "Your Garrison Armory currently has 0 units. Build units in the Orbital Shipyard first — as each unit finishes, it drops directly into your Armory!",
					Modulate = new Color("#FBBF24"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				noShipsLbl.AddThemeFontSizeOverride("font_size", 11);
				shipBox.AddChild(noShipsLbl);
			}

			// Hidden until player adds Laser Cannons (u6) to this fleet!
			if (draftLaserCannons > 0)
			{
				shipBox.AddChild(new HSeparator());
				var currentTargetBld = GameMath.SiegeTargetBuildings[_selectedSiegeBuildingIdx];
				Button btnCycleBld = MakeHiTechButton(
					$"💥 LASER CANNON TARGET ({draftLaserCannons:N0}x = -{draftLaserCannons:N0} HP): {currentTargetBld.Name.ToUpper()}  (CLICK TO CHANGE)",
					new Color("#F97316"), 0, 30, 11, true
				);
				btnCycleBld.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				btnCycleBld.Pressed += () =>
				{
					_selectedSiegeBuildingIdx = (_selectedSiegeBuildingIdx + 1) % GameMath.SiegeTargetBuildings.Length;
					RefreshInspector();
				};
				shipBox.AddChild(btnCycleBld);
			}

			_contentStack.AddChild(shipCard);

			// Card 3: Compact Cargo Loader & Save Button
			PanelContainer cargoCard = CreateSlimHiTechCard(new Color("#FBBF24"), new Color(0.07f, 0.14f, 0.25f, 0.98f), 4);
			VBoxContainer cargoBox = CreateCardContainer(cargoCard);
			cargoBox.AddThemeConstantOverride("separation", 5);

			Label cargoHdr = new Label
			{
				Text = $"📦  OPTIONAL CARGO HOLD:  ⚡ {(_draftCargoE):N0}  |  ⛏️ {(_draftCargoI):N0}  |  💎 {(_draftCargoT):N0}  |  ⛽ {(_draftCargoH3):N0}",
				Modulate = new Color("#FBBF24"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			cargoHdr.AddThemeFontSizeOverride("font_size", 11);
			cargoBox.AddChild(cargoHdr);

			GridContainer cargoLoadGrid = new GridContainer
			{
				Columns = 4,
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			cargoLoadGrid.AddThemeConstantOverride("h_separation", 5);
			cargoLoadGrid.AddThemeConstantOverride("v_separation", 5);

			long remDraftCap = Math.Max(0, totalCap - currentCargo);
			Button btnLoadE = MakeHiTechButton("+1K ⚡ Power", new Color("#D946EF"), 0, 28, 10, false, remDraftCap <= 0);
			btnLoadE.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btnLoadE.Pressed += () => LoadDraftResource("E", 1000, remDraftCap);
			cargoLoadGrid.AddChild(btnLoadE);

			Button btnLoadI = MakeHiTechButton("+1K ⛏️ Iron", new Color("#06B6D4"), 0, 28, 10, false, remDraftCap <= 0);
			btnLoadI.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btnLoadI.Pressed += () => LoadDraftResource("I", 1000, remDraftCap);
			cargoLoadGrid.AddChild(btnLoadI);

			Button btnLoadT = MakeHiTechButton("+1K 💎 Titanium", new Color("#94A3B8"), 0, 28, 10, false, remDraftCap <= 0);
			btnLoadT.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btnLoadT.Pressed += () => LoadDraftResource("T", 1000, remDraftCap);
			cargoLoadGrid.AddChild(btnLoadT);

			Button btnLoadH = MakeHiTechButton("+1K ⛽ Helium-3", new Color("#EAB308"), 0, 28, 10, false, remDraftCap <= 0);
			btnLoadH.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btnLoadH.Pressed += () => LoadDraftResource("H3", 1000, remDraftCap);
			cargoLoadGrid.AddChild(btnLoadH);

			cargoBox.AddChild(cargoLoadGrid);

			HBoxContainer bottomCommitRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			bottomCommitRow.AddThemeConstantOverride("separation", 6);

			if (currentCargo > 0)
			{
				Button btnClearCargo = MakeHiTechButton("UNLOAD CARGO", new Color("#F59E0B"), 120, 34, 11);
				btnClearCargo.Pressed += () =>
				{
					if (BaseHUD.Instance != null)
					{
						BaseHUD.Instance.ResE += _draftCargoE;
						BaseHUD.Instance.ResI += _draftCargoI;
						BaseHUD.Instance.ResT += _draftCargoT;
						BaseHUD.Instance.ResH3 += _draftCargoH3;
						_draftCargoE = _draftCargoI = _draftCargoT = _draftCargoH3 = 0;
						BaseHUD.Instance.UpdateHUDDisplay();
						RefreshInspector();
					}
				};
				bottomCommitRow.AddChild(btnClearCargo);
			}

			Button btnSaveFleet = MakeHiTechButton("✓  SAVE NEW FLEET TO HANGAR", new Color("#4ADE80"), 0, 34, 12, totalSelectedUnits > 0, totalSelectedUnits <= 0);
			btnSaveFleet.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btnSaveFleet.Pressed += SaveDraftFleetToHangar;
			bottomCommitRow.AddChild(btnSaveFleet);

			cargoBox.AddChild(bottomCommitRow);
			_contentStack.AddChild(cargoCard);
		}

		private void SaveDraftFleetToHangar()
		{
			int totalU = 0;
			foreach (var kv in _draftFleetUnits) totalU += kv.Value;
			if (totalU <= 0)
			{
				if (_fleetFeedbackLabel != null)
				{
					_fleetFeedbackLabel.Text = "Please add at least 1 unit from your Garrison Armory before saving your fleet.";
					_fleetFeedbackLabel.Modulate = new Color("#EF4444");
				}
				return;
			}

			var selectedTargetBld = GameMath.SiegeTargetBuildings[_selectedSiegeBuildingIdx];
			string activeSrv = SupabaseService.CachedServerId;
			var newManifest = new GameMath.FleetManifest
			{
				Id = $"{activeSrv}_FLT-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
				Name = string.IsNullOrWhiteSpace(_newFleetCallsign) ? "FLEET ALPHA" : _newFleetCallsign.Trim().ToUpper(),
				Units = new Dictionary<string, int>(_draftFleetUnits),
				Cargo = new GameMath.FleetCargoHold { E = _draftCargoE, I = _draftCargoI, T = _draftCargoT, H3 = _draftCargoH3 },
				LocationCoord = $"{activeSrv}:{SupabaseService.CachedMoonId}:{SupabaseService.CachedBaseSlot}",
				TargetBuildingId = selectedTargetBld.Id,
				Status = "DOCKED",
				Protocol = "IDLE",
				LastUpdatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			};
			SupabaseService.MarkLocalFleetMutated(newManifest);

			BaseHUD.Instance?.SavedFleets.Add(newManifest);
			_draftFleetUnits.Clear();
			_draftCargoE = _draftCargoI = _draftCargoT = _draftCargoH3 = 0;
			int nextNum = (BaseHUD.Instance?.SavedFleets.Count ?? 1) + 1;
			_newFleetCallsign = $"FLEET #{nextNum}";
			_fleetTab = "LOCAL";
			BaseHUD.Instance?.TriggerInstantSave();
			RefreshInspector();
		}

		private void LoadDraftResource(string type, long amt, long remainingCap)
		{
			if (BaseHUD.Instance == null || remainingCap <= 0) return;
			long actual = Math.Min(amt, remainingCap);
			if (type == "E" && BaseHUD.Instance.ResE >= actual) { BaseHUD.Instance.ResE -= actual; _draftCargoE += actual; }
			else if (type == "I" && BaseHUD.Instance.ResI >= actual) { BaseHUD.Instance.ResI -= actual; _draftCargoI += actual; }
			else if (type == "T" && BaseHUD.Instance.ResT >= actual) { BaseHUD.Instance.ResT -= actual; _draftCargoT += actual; }
			else if (type == "H3" && BaseHUD.Instance.ResH3 >= actual) { BaseHUD.Instance.ResH3 -= actual; _draftCargoH3 += actual; }
			BaseHUD.Instance.UpdateHUDDisplay();
			RefreshInspector();
		}

		private async void DispatchFleetToCoords(GameMath.FleetManifest f, int tMoon, int tSlot, string protocol)
		{
			if (tMoon < 1 || tMoon > 1000 || tSlot < 1 || tSlot > 20)
			{
				_fleetFeedbackLabel.Text = "Invalid target coordinates.";
				_fleetFeedbackLabel.Modulate = new Color("#EF4444");
				return;
			}

			int radarLvl = BaseHUD.Instance != null
				? Math.Clamp(BaseHUD.Instance.BuildingLevels.GetValueOrDefault("hub_rng", 1), 1, 10)
				: Math.Clamp(SupabaseService.CachedRadarLevel, 1, 10);
			if (!GameMath.IsMoonVisibleInRadarCircle(SupabaseService.CachedMoonId, tMoon, radarLvl))
			{
				_fleetFeedbackLabel.Text = $"🔒 Target Moon M-{tMoon} is outside your Level {radarLvl} Deep Space Radar circle ({radarLvl * 10}% Galaxy Visibility)! Upgrade your Deep Space Radar to reveal and target that sector.";
				_fleetFeedbackLabel.Modulate = new Color("#EF4444");
				return;
			}

			// Check target occupancy & alliance rules across the galaxy
			SupabaseService.GalacticCommanderEntry targetOccupant = null;
			if (SupabaseService.Instance != null)
			{
				var all = await SupabaseService.Instance.FetchAllColoniesAsync();
				targetOccupant = all.Find(x => x.MoonId == tMoon && x.BaseSlot == tSlot);
			}

			bool isOccupied = (targetOccupant != null);
			bool isMyOwnedBase = IsPlayerOwnedBaseCoordinate(tMoon, tSlot) || (targetOccupant != null && targetOccupant.IsMe);
			string targetAlliance = targetOccupant != null ? (targetOccupant.AllianceName ?? "NONE").Trim().ToUpper() : "NONE";
			string myAlliance = (SupabaseService.CachedAllianceName ?? "NONE").Trim().ToUpper();

			if (protocol == "RAID")
			{
				if (isMyOwnedBase)
				{
					_fleetFeedbackLabel.Text = "Cannot Attack: That coordinate is your own base! Use 'MOVE FLEET' or 'SEND & DROP OFF UNITS' instead.";
					_fleetFeedbackLabel.Modulate = new Color("#EF4444");
					return;
				}
				if (!isOccupied)
				{
					_fleetFeedbackLabel.Text = "Cannot Attack: That Moon Slot is empty. Use 'MOVE FLEET' to station your fleet at an open slot.";
					_fleetFeedbackLabel.Modulate = new Color("#EF4444");
					return;
				}
				if (myAlliance != "NONE" && targetAlliance == myAlliance)
				{
					_fleetFeedbackLabel.Text = "Cannot Attack: That player is in your own Alliance!";
					_fleetFeedbackLabel.Modulate = new Color("#EF4444");
					return;
				}
			}
			else if (protocol == "MOVE")
			{
				if (isOccupied && !isMyOwnedBase && targetAlliance != "NONE")
				{
					_fleetFeedbackLabel.Text = $"Blocked: You can only move a fleet to your own bases, an open moon slot, or a player who does not belong to an alliance.";
					_fleetFeedbackLabel.Modulate = new Color("#EF4444");
					return;
				}
			}
			else if (protocol == "DROPOFF")
			{
				if (!isMyOwnedBase)
				{
					_fleetFeedbackLabel.Text = "Blocked: You can only drop off units at your own owned base coordinates!";
					_fleetFeedbackLabel.Modulate = new Color("#EF4444");
					return;
				}
			}
			else if (protocol == "REINFORCE")
			{
				if (!isMyOwnedBase && (!isOccupied || myAlliance == "NONE" || targetAlliance != myAlliance))
				{
					_fleetFeedbackLabel.Text = "Blocked: You can only send defense fleets to your own bases or members of your own Alliance!";
					_fleetFeedbackLabel.Modulate = new Color("#EF4444");
					return;
				}
			}

			GameMath.TryParseSectorCoordinate(f.LocationCoord, out int origMoon, out int origSlot);
			int travelSec = GameMath.CalcFleetTravelTimeSeconds(
				origMoon,
				origSlot,
				tMoon,
				tSlot,
				f.Units,
				BaseHUD.Instance?.ServerSpeed ?? 1.0f
			);

			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			long arrivalMs = nowMs + ((long)travelSec * 1000L);
			string activeSrv = SupabaseService.CachedServerId;

			f.Status = "IN_TRANSIT";
			f.Protocol = protocol;
			f.OriginCommanderName = SupabaseService.CachedCommanderName ?? "COMMANDER";
			f.LocationCoord = $"{activeSrv}:{origMoon}:{origSlot}";
			f.TargetCoord = $"{activeSrv}:{tMoon}:{tSlot}";
			f.TargetCommanderName = targetOccupant?.CommanderName ?? "";
			f.TotalFlightDurationSec = travelSec;
			f.DepartureTimeUnixMs = nowMs;
			f.EtaSecondsLeft = travelSec;
			f.ArrivalTimeUnixMs = arrivalMs;
			SupabaseService.MarkLocalFleetMutated(f);

			BaseHUD.Instance?.TriggerInstantSave();
			MoonView.Instance?.RefreshMoonSlotVisuals();

			if (targetOccupant != null && !isMyOwnedBase && SupabaseService.Instance != null)
			{
				int totalUnits = 0;
				foreach (var kv in f.Units) totalUnits += kv.Value;
				int lasers = f.Units.GetValueOrDefault("u6", 0);
				string bldName = GameMath.GetSiegeTargetDisplayName(f.TargetBuildingId);
				string origin = $"{activeSrv}:{origMoon}:{origSlot}";
				string sigPayload = $"{f.Id}|{SupabaseService.CachedCommanderName}|{origin}|{protocol}|{bldName}|{totalUnits}|{lasers}|{travelSec}|{arrivalMs}";

				await SupabaseService.Instance.SendInboxSignalAsync(
					SupabaseService.CachedCommanderName,
					targetOccupant.CommanderName,
					"INTEL",
					$"INCOMING_FLEET:{f.Id}",
					sigPayload
				);
			}

			_showFleetWatchlistDrawer = false;
			_fleetTab = "TRANSIT";
			BaseHUD.Instance?.UpdateFleetRadarDropdownUI();
			string protoVerb = protocol == "RAID" ? "ATTACKING" : (protocol == "REINFORCE" ? "DEFENDING ALLY AT" : (protocol == "DROPOFF" ? "DROPPING OFF UNITS AT" : "MOVING TO"));
			BaseHUD.Instance?.ShowFleetDockNotification(
				$"🚀 FLEET LAUNCHED: [{f.Name}] is {protoVerb} M-{tMoon}:B-{tSlot:D2} (ETA {GameMath.FormatTime(travelSec)})",
				protocol == "RAID" ? new Color("#EF4444") : (protocol == "REINFORCE" || protocol == "DROPOFF" ? new Color("#4ADE80") : new Color("#00F0FF"))
			);
			RefreshInspector();
		}

		/// <summary>
		/// Checks if the given (moonId, slotId) belongs to the player's owned bases on the current server.
		/// </summary>
		public static bool IsPlayerOwnedBaseCoordinate(int moonId, int slotId)
		{
			if (moonId <= 0 || slotId <= 0) return false;
			int homeMoon = BaseHUD.Instance != null ? BaseHUD.Instance.CurrentMoon : SupabaseService.CachedMoonId;
			int homeSlot = BaseHUD.Instance != null ? BaseHUD.Instance.CurrentBaseSlot : SupabaseService.CachedBaseSlot;
			if (moonId == homeMoon && slotId == homeSlot) return true;

			// Also check any additional colonized/owned bases registered in BuildingLevels or Colony Snapshot
			var blds = BaseHUD.Instance?.BuildingLevels ?? SupabaseService.CachedBuildingLevels;
			if (blds != null && blds.GetValueOrDefault($"__owned_base_{moonId}_{slotId}", 0) == 1)
			{
				return true;
			}

			var snap = SupabaseService.GetCachedColoniesSnapshot();
			if (snap != null)
			{
				foreach (var c in snap)
				{
					if (c != null && c.IsMe && c.MoonId == moonId && c.BaseSlot == slotId)
						return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Like the Laser Cannon button rule: returns true ONLY when the fleet registers its current location coordinate
		/// is at one of the player's owned base coordinates (and the fleet is not in flight).
		/// </summary>
		public static bool IsFleetAtPlayerOwnedCoordinate(GameMath.FleetManifest f)
		{
			if (f == null || f.Status == "IN_TRANSIT" || f.Status == "WATCHLIST_DATA") return false;
			int totalUnits = 0;
			if (f.Units != null)
			{
				foreach (var kv in f.Units) totalUnits += Math.Max(0, kv.Value);
			}
			if (totalUnits <= 0) return false;

			if (!GameMath.TryParseSectorCoordinate(f.LocationCoord, out int fMoon, out int fSlot))
			{
				return f.Status == "DOCKED";
			}
			return IsPlayerOwnedBaseCoordinate(fMoon, fSlot);
		}

		/// <summary>
		/// Drops off all units and resource cargo from the fleet directly into the player's owned base Garrison Armory & Silos at that coordinate.
		/// </summary>
		public static void DropOffFleetUnitsToOwnedBase(GameMath.FleetManifest f, Action onCompleteUiRefresh = null)
		{
			if (BaseHUD.Instance == null || f == null) return;
			if (!IsFleetAtPlayerOwnedCoordinate(f)) return;

			int droppedUnits = 0;
			foreach (var kvp in f.Units)
			{
				if (kvp.Value <= 0) continue;
				droppedUnits += kvp.Value;
				BaseHUD.Instance.UnitInventory[kvp.Key] = BaseHUD.Instance.UnitInventory.GetValueOrDefault(kvp.Key, 0) + kvp.Value;
				if (BaseHUD.Instance.BuildingLevels != null)
				{
					BaseHUD.Instance.BuildingLevels[$"__meta_unit_{kvp.Key}"] = BaseHUD.Instance.UnitInventory[kvp.Key];
				}
			}

			long dropE = f.Cargo?.E ?? 0;
			long dropI = f.Cargo?.I ?? 0;
			long dropT = f.Cargo?.T ?? 0;
			long dropH3 = f.Cargo?.H3 ?? 0;

			BaseHUD.Instance.ResE = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResE + dropE);
			BaseHUD.Instance.ResI = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResI + dropI);
			BaseHUD.Instance.ResT = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResT + dropT);
			BaseHUD.Instance.ResH3 = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResH3 + dropH3);

			string coordLabel = string.IsNullOrWhiteSpace(f.LocationCoord)
				? $"{SupabaseService.CachedServerId}:{BaseHUD.Instance.CurrentMoon}:{BaseHUD.Instance.CurrentBaseSlot}"
				: f.LocationCoord;

			BaseHUD.Instance.SavedFleets.Remove(f);
			SupabaseService.MarkLocalFleetMutated();
			BaseHUD.Instance.UpdateHUDDisplay();
			BaseHUD.Instance.UpdateFleetRadarDropdownUI();
			BaseHUD.Instance.TriggerInstantSave();
			MoonView.Instance?.RefreshMoonSlotVisuals();

			BaseHUD.Instance.ShowFleetDockNotification(
				$"🪂 UNITS DROPPED OFF: [{f.Name}] transferred {droppedUnits:N0} Units to your base at {coordLabel}!",
				new Color("#22C55E")
			);

			onCompleteUiRefresh?.Invoke();
		}

		private void DropOffFleetUnitsToOwnedBase(GameMath.FleetManifest f)
		{
			DropOffFleetUnitsToOwnedBase(f, RefreshInspector);
		}

		private void DisbandFleetToGarrison(GameMath.FleetManifest f)
		{
			if (BaseHUD.Instance == null) return;
			foreach (var kvp in f.Units)
			{
				BaseHUD.Instance.UnitInventory[kvp.Key] = BaseHUD.Instance.UnitInventory.GetValueOrDefault(kvp.Key, 0) + kvp.Value;
			}
			BaseHUD.Instance.ResE = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResE + f.Cargo.E);
			BaseHUD.Instance.ResI = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResI + f.Cargo.I);
			BaseHUD.Instance.ResT = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResT + f.Cargo.T);
			BaseHUD.Instance.ResH3 = Math.Min(BaseHUD.Instance.SiloCap, BaseHUD.Instance.ResH3 + f.Cargo.H3);

			BaseHUD.Instance.SavedFleets.Remove(f);
			SupabaseService.MarkLocalFleetMutated();
			BaseHUD.Instance.UpdateHUDDisplay();
			BaseHUD.Instance.TriggerInstantSave();
			RefreshInspector();
		}

		private async void OffloadFleetCargo(GameMath.FleetManifest f)
		{
			if (BaseHUD.Instance == null) return;
			string targetCoord = f.LocationCoord;
			f.Cargo.E = f.Cargo.I = f.Cargo.T = f.Cargo.H3 = 0;
			SupabaseService.MarkLocalFleetMutated(f);
			BaseHUD.Instance.TriggerInstantSave();

			if (SupabaseService.Instance != null)
			{
				await SupabaseService.Instance.SendInboxSignalAsync(
					SupabaseService.CachedCommanderName,
					SupabaseService.CachedCommanderName,
					"TRADE",
					$"LOGISTICS RECEIPT: CARGO OFFLOADED AT {targetCoord}",
					$"Fleet [{f.Name}] offloaded its resource cargo hold at sector {targetCoord}."
				);
			}
			RefreshInspector();
		}

		private void RecallStationedFleet(GameMath.FleetManifest f)
		{
			string activeSrv = SupabaseService.CachedServerId;
			float srvSpeed = BaseHUD.Instance != null ? Math.Max(1.0f, BaseHUD.Instance.ServerSpeed) : Math.Max(1.0f, SupabaseService.CachedServerSpeed);
			GameMath.TryParseSectorCoordinate(f.LocationCoord, out int origMoon, out int origSlot);
			int recallSec = GameMath.CalcFleetTravelTimeSeconds(origMoon, origSlot, SupabaseService.CachedMoonId, SupabaseService.CachedBaseSlot, f.Units, srvSpeed);
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			f.Status = "IN_TRANSIT";
			f.Protocol = "RETURN";
			f.TargetCoord = $"{activeSrv}:{SupabaseService.CachedMoonId}:{SupabaseService.CachedBaseSlot}";
			f.TotalFlightDurationSec = recallSec;
			f.DepartureTimeUnixMs = nowMs;
			f.EtaSecondsLeft = recallSec;
			f.ArrivalTimeUnixMs = nowMs + ((long)recallSec * 1000L);
			SupabaseService.MarkLocalFleetMutated(f);
			_fleetTab = "TRANSIT";
			BaseHUD.Instance?.TriggerInstantSave();
			MoonView.Instance?.RefreshMoonSlotVisuals();
			RefreshInspector();
		}

		private void BuildGalaxyGoldExchangeMenu()
		{
			Label lblHeader = new Label
			{
				Text = "GALAXY GOLD CREDIT BUNDLE EXCHANGE:",
				Modulate = new Color("#FBBF24"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblHeader.AddThemeFontSizeOverride("font_size", 16);
			_contentStack.AddChild(lblHeader);

			var packs = new[]
			{
				new { Name = "RECON PACK", Gold = 1000, Price = "$0.99" },
				new { Name = "COMMAND PACK", Gold = 5000, Price = "$4.49" },
				new { Name = "EMPIRE PACK", Gold = 11000, Price = "$8.99" },
				new { Name = "OVERLORD PACK", Gold = 60000, Price = "$39.99" }
			};

			foreach (var pack in packs)
			{
				PanelContainer packCard = CreateCardPanel(0);
				HBoxContainer row = new HBoxContainer
				{
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					MouseFilter = MouseFilterEnum.Pass
				};
				row.AddThemeConstantOverride("separation", 12);

				Label lblName = new Label
				{
					Text = $"{pack.Name}\n💰 {pack.Gold:N0} GGOLD",
					Modulate = new Color("#FBBF24"),
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblName.AddThemeFontSizeOverride("font_size", 15);

				Button btnBuy = new Button
				{
					Text = pack.Price,
					CustomMinimumSize = new Vector2(116, 44)
				};
				btnBuy.AddThemeFontSizeOverride("font_size", 15);
				btnBuy.Modulate = new Color("#22C55E");
				int gAmount = pack.Gold;
				btnBuy.Pressed += () =>
				{
					if (BaseHUD.Instance != null)
					{
						BaseHUD.Instance.GGold += gAmount;
						BaseHUD.Instance.UpdateHUDDisplay();
						BaseHUD.Instance.TriggerInstantSave();
						GD.Print($"[EXCHANGE] Added {gAmount} GGold to treasury!");
					}
				};

				row.AddChild(lblName);
				row.AddChild(btnBuy);
				packCard.AddChild(row);
				_contentStack.AddChild(packCard);
			}

			_contentStack.AddChild(new HSeparator());
			BuildGlobalBoostSection();
		}

		private void BuildGlobalBoostSection()
		{
			PanelContainer boostBox = CreateCardPanel(0);
			VBoxContainer boostContent = CreateCardContainer(boostBox);

			Label lblTitle = new Label
			{
				Text = "GLOBAL EMPIRE BOOSTS (ALL RESOURCES):",
				Modulate = new Color("#FBBF24"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblTitle.AddThemeFontSizeOverride("font_size", 13);
			boostContent.AddChild(lblTitle);

			HBoxContainer btnRow1 = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnRow1.AddThemeConstantOverride("separation", 10);

			Button btnBoost1 = new Button { Text = "+30% (7 DAYS) · 1,000 GGOLD", CustomMinimumSize = new Vector2(0, 42), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnBoost1.AddThemeFontSizeOverride("font_size", 12);
			btnBoost1.Modulate = new Color("#FBBF24");
			btnBoost1.Pressed += () => OnBuyGlobalBoostPressed(1.30f, 7, 1000);
			btnRow1.AddChild(btnBoost1);

			Button btnBoost2 = new Button { Text = "+75% (7 DAYS) · 2,000 GGOLD", CustomMinimumSize = new Vector2(0, 42), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnBoost2.AddThemeFontSizeOverride("font_size", 12);
			btnBoost2.Modulate = new Color("#FBBF24");
			btnBoost2.Pressed += () => OnBuyGlobalBoostPressed(1.75f, 7, 2000);
			btnRow1.AddChild(btnBoost2);

			boostContent.AddChild(btnRow1);

			Button btnBoost3 = new Button { Text = "+70% (30 DAYS) — 8,600 GGOLD", CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnBoost3.AddThemeFontSizeOverride("font_size", 12);
			btnBoost3.Modulate = new Color("#FBBF24");
			btnBoost3.Pressed += () => OnBuyGlobalBoostPressed(1.70f, 30, 8600);
			boostContent.AddChild(btnBoost3);

			_contentStack.AddChild(boostBox);
		}

		private void OnBuyGlobalBoostPressed(float multiplier, int days, long cost)
		{
			if (BaseHUD.Instance == null) return;

			if (BaseHUD.Instance.GGold >= cost)
			{
				ShowConfirmDialog(
					"AUTHORIZE GLOBAL BOOST?",
					$"This will consume {cost} Galaxy Gold to apply a +{((multiplier - 1.0f) * 100):F0}% production boost to ALL resources (E, I, T, H3) for {days} days.",
					() => 
					{
						BaseHUD.Instance.GGold -= cost;
						var exp = DateTimeOffset.UtcNow.AddDays(days);
						
						BaseHUD.Instance.ActiveBoosts["E"] = new GameMath.BoostData { Multiplier = multiplier, Expiration = exp };
						BaseHUD.Instance.ActiveBoosts["I"] = new GameMath.BoostData { Multiplier = multiplier, Expiration = exp };
						BaseHUD.Instance.ActiveBoosts["T"] = new GameMath.BoostData { Multiplier = multiplier, Expiration = exp };
						BaseHUD.Instance.ActiveBoosts["H3"] = new GameMath.BoostData { Multiplier = multiplier, Expiration = exp };
						
						BaseHUD.Instance.UpdateHUDDisplay();
						BaseHUD.Instance.TriggerInstantSave();
						RefreshInspector();
						GD.Print($"[BOOST] Purchased GLOBAL {multiplier}x boost for {days} days.");
					}
				);
			}
			else
			{
				GD.PrintErr("[INSPECTOR] Insufficient GGold for global boost.");
			}
		}

		private void AddBlueprintUpgradeSection(bool isSubMine, int maxLvl, Dictionary<string, int> simLevels, int simActiveLevel)
		{
			var liveLevels = (BaseHUD.Instance != null && BaseHUD.Instance.BuildingLevels != null)
				? BaseHUD.Instance.BuildingLevels
				: AllBuildingLevels;
			var hpInfo = GameMath.GetStructureHealthDetails(ActiveBuildingId, ActiveBuildingLevel, liveLevels);
			_isActiveBuildingDamaged = hpInfo.isDamaged;
			_currentRepairMetrics = GameMath.CalcBuildingRepairCost(ActiveBuildingId, ActiveBuildingLevel, liveLevels);

			// 0. UNIVERSAL STRUCTURAL INTEGRITY & REPAIR CARD (Applies across ALL upgradable buildings!)
			// Blueprint Rule:
			// - Each building level adds 100 HP (Max Level 20 = 2,000 HP).
			// - Fractional levels: e.g. 50 HP loss on Level 18 (1,800 HP) drops it to Level 17.5 (1,750 HP).
			// - Repair costs 1% of the specific level's upgrade cost per damaged HP.
			// - Repair is REQUIRED before further upgrades for affected levels!
			if (_isActiveBuildingDamaged)
			{
				_repairCardPanel = CreateCardPanelWithStyle(0, out _repairCardStyle);
				_repairCardStyle.BorderColor = new Color("#EF4444");
				_repairCardStyle.BorderWidthLeft = 2;
				_repairCardStyle.BorderWidthRight = 2;
				_repairCardStyle.BorderWidthTop = 2;
				_repairCardStyle.BorderWidthBottom = 2;
				_repairCardStyle.BgColor = new Color(0.14f, 0.03f, 0.05f, 0.96f);

				VBoxContainer repBox = CreateCardContainer(_repairCardPanel);

				string effLvlText = GameMath.FormatEffectiveLevel(hpInfo.effectiveLevel);
				Label lblRepHeader = new Label
				{
					Text = $"🔧 STRUCTURAL DAMAGE DETECTED — LEVEL {effLvlText} / {ActiveBuildingLevel} ({hpInfo.currentHp:N0} / {hpInfo.maxHp:N0} HP):",
					Modulate = new Color("#FCA5A5"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblRepHeader.AddThemeFontSizeOverride("font_size", 15);
				repBox.AddChild(lblRepHeader);

				Label lblRepExplain = new Label
				{
					Text = $"Damaged HP: -{hpInfo.damagedHp:N0} HP  |  Repair Cost: {hpInfo.damagedHp}% of Level {ActiveBuildingLevel} Upgrade Cost (1% per damaged HP)\n⚠️ Repair is required to restore Level {ActiveBuildingLevel} ({hpInfo.maxHp:N0} HP) before further upgrades can proceed.",
					Modulate = new Color("#FDE047"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblRepExplain.AddThemeFontSizeOverride("font_size", 14);
				repBox.AddChild(lblRepExplain);

				GridContainer repGrid = new GridContainer
				{
					Columns = 2,
					SizeFlagsHorizontal = SizeFlags.ExpandFill
				};
				repGrid.AddThemeConstantOverride("h_separation", 18);
				repGrid.AddThemeConstantOverride("v_separation", 6);

				_lblLiveRepairE = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
				_lblLiveRepairI = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
				_lblLiveRepairT = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
				_lblLiveRepairH3 = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };

				_lblLiveRepairE.AddThemeFontSizeOverride("font_size", 15);
				_lblLiveRepairI.AddThemeFontSizeOverride("font_size", 15);
				_lblLiveRepairT.AddThemeFontSizeOverride("font_size", 15);
				_lblLiveRepairH3.AddThemeFontSizeOverride("font_size", 15);

				repGrid.AddChild(_lblLiveRepairE);
				repGrid.AddChild(_lblLiveRepairI);
				repGrid.AddChild(_lblLiveRepairT);
				repGrid.AddChild(_lblLiveRepairH3);
				repBox.AddChild(repGrid);

				_btnRepairBuilding = new Button
				{
					Text = $"🔧 REPAIR +{hpInfo.damagedHp:N0} HP (RESTORE TO LVL {ActiveBuildingLevel} · {hpInfo.maxHp:N0} HP)",
					CustomMinimumSize = new Vector2(0, 48),
					SizeFlagsHorizontal = SizeFlags.ExpandFill
				};
				_btnRepairBuilding.AddThemeFontSizeOverride("font_size", 15);
				_btnRepairBuilding.Pressed += OnRepairBuildingPressed;
				repBox.AddChild(_btnRepairBuilding);

				_contentStack.AddChild(_repairCardPanel);
			}
			else
			{
				PanelContainer hpStatusCard = CreateCardPanel(0);
				VBoxContainer hpStatusBox = CreateCardContainer(hpStatusCard);
				Label lblHpOk = new Label
				{
					Text = $"🛡️ STRUCTURAL INTEGRITY: {hpInfo.currentHp:N0} / {hpInfo.maxHp:N0} HP (100% · Level {ActiveBuildingLevel} Nominal — +100 HP per Level)",
					Modulate = new Color("#4ADE80"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblHpOk.AddThemeFontSizeOverride("font_size", 14);
				hpStatusBox.AddChild(lblHpOk);
				_contentStack.AddChild(hpStatusCard);
			}

			int targetLevel = simActiveLevel + 1;

			if (simActiveLevel >= maxLvl)
			{
				Label lblMax = new Label
				{
					Text = _isActiveBuildingDamaged
						? $"FACILITY AT MAX LEVEL ({maxLvl}) — REPAIR REQUIRED TO RESTORE {hpInfo.maxHp:N0} HP"
						: $"FACILITY AT MAXIMUM UPGRADE LEVEL ({maxLvl} · {hpInfo.maxHp:N0} HP)",
					Modulate = _isActiveBuildingDamaged ? new Color("#FBBF24") : new Color("#22C55E"),
					HorizontalAlignment = HorizontalAlignment.Center,
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblMax.AddThemeFontSizeOverride("font_size", 16);
				_contentStack.AddChild(lblMax);
				UpdateLiveUpgradeAffordabilityAndGlow(0.0);
				return;
			}

			// 1. Generate Universal Construction Matrix (Includes Industrial Core Cap & All Prerequisites)
			var prereqs = GameMath.GetFacilityRequirements(ActiveBuildingId, targetLevel, ActiveDistrictCode, ActiveSlotIndex, simLevels);
			_currentPrereqList = prereqs;
			bool allReqsMet = true;

			if (prereqs.Count > 0)
			{
				foreach (var r in prereqs) if (!r.IsMet) allReqsMet = false;

				_prereqCardPanel = CreateCardPanelWithStyle(0, out _prereqCardStyle);
				VBoxContainer prereqContent = CreateCardContainer(_prereqCardPanel);

				_prereqTitleLabel = new Label
				{
					Text = $"CONSTRUCTION MATRIX (LVL {targetLevel} REQUIREMENTS):",
					Modulate = allReqsMet ? new Color("#22C55E") : new Color("#EF4444"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				_prereqTitleLabel.AddThemeFontSizeOverride("font_size", 15);
				prereqContent.AddChild(_prereqTitleLabel);

				foreach (var req in prereqs)
				{
					HBoxContainer row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
					string statusStr = req.IsMet
						? $"✓ {req.BuildingName}: Level {req.RequiredLevel} Required (Current / Queued: Lvl {req.CurrentLevel})"
						: $"🔒 {req.BuildingName}: Level {req.RequiredLevel} Required (Current / Queued: Lvl {req.CurrentLevel} — Need +{req.RequiredLevel - req.CurrentLevel} Lvl)";

					Label lblStatus = new Label
					{
						Text = statusStr,
						Modulate = req.IsMet ? new Color("#4ADE80") : new Color("#F87171"),
						SizeFlagsHorizontal = SizeFlags.ExpandFill,
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					};
					lblStatus.AddThemeFontSizeOverride("font_size", 15);
					row.AddChild(lblStatus);
					prereqContent.AddChild(row);
				}
				_contentStack.AddChild(_prereqCardPanel);
			}

			// 2. Evaluate Core Cap Lock & Final Prereq Lock
			int simCoreLevel = simLevels.GetValueOrDefault("hub_cmd", CurrentIndustrialCoreLevel);
			bool isCoreCapLocked = ActiveBuildingId != "hub_cmd" && simActiveLevel >= GameMath.GetMaxAllowedSubBuildingLevel(simCoreLevel);
			bool isLocked = isCoreCapLocked || !allReqsMet;

			// 3. Required Resources Card (With Live Green ✓ / Red Deficit Breakdown & 10-Second Red Alert Glow Support)
			var metrics = GameMath.GetBuildingUpgradeMetrics(ActiveBuildingId, simActiveLevel);
			(long e, long i, long t, long h) cost = (metrics.costE, metrics.costI, metrics.costT, metrics.costH3);
			float activeServerSpeed = BaseHUD.Instance != null ? Math.Max(1.0f, BaseHUD.Instance.ServerSpeed) : Math.Max(1.0f, SupabaseService.CachedServerSpeed);
			int durationSec = Math.Max(5, (int)Math.Round(metrics.buildTimeSec / activeServerSpeed));
			_currentUpgradeCost = cost;
			_currentUpgradeTargetLevel = targetLevel;
			_currentUpgradeDurationSec = durationSec;
			_liveUpgradeBaseDurationSec = durationSec;
			_currentAllPrereqsMet = !isLocked;

			_resourceCardPanel = CreateCardPanelWithStyle(0, out _resourceCardStyle);
			VBoxContainer resContent = CreateCardContainer(_resourceCardPanel);

			Label lblReqHeader = new Label
			{
				Text = $"REQUIRED RESOURCES FOR LEVEL {targetLevel}:",
				Modulate = new Color("#CBD5E1"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblReqHeader.AddThemeFontSizeOverride("font_size", 15);
			resContent.AddChild(lblReqHeader);

			GridContainer reqGrid = new GridContainer
			{
				Columns = 2,
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			reqGrid.AddThemeConstantOverride("h_separation", 18);
			reqGrid.AddThemeConstantOverride("v_separation", 8);

			_lblLiveCostE = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			_lblLiveCostI = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			_lblLiveCostT = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			_lblLiveCostH3 = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };

			_lblLiveCostE.AddThemeFontSizeOverride("font_size", 15);
			_lblLiveCostI.AddThemeFontSizeOverride("font_size", 15);
			_lblLiveCostT.AddThemeFontSizeOverride("font_size", 15);
			_lblLiveCostH3.AddThemeFontSizeOverride("font_size", 15);

			reqGrid.AddChild(_lblLiveCostE);
			reqGrid.AddChild(_lblLiveCostI);
			reqGrid.AddChild(_lblLiveCostT);
			reqGrid.AddChild(_lblLiveCostH3);
			resContent.AddChild(reqGrid);

			double currentTotalPipeline = BaseHUD.Instance != null ? BaseHUD.Instance.GetTotalPipelineDurationSeconds() : 0.0;
			string pipelineSuffix = currentTotalPipeline > 0
				? $"   ·   Active Queue Total: {GameMath.FormatTime((int)currentTotalPipeline)}"
				: "";

			Label lblDuration = new Label
			{
				Text = $"⏱ Construction Duration: {GameMath.FormatTime(durationSec)}{pipelineSuffix}",
				Modulate = new Color("#FBBF24"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblDuration.AddThemeFontSizeOverride("font_size", 15);
			_liveUpgradeDurationLabel = lblDuration;
			resContent.AddChild(lblDuration);

			_contentStack.AddChild(_resourceCardPanel);

			// 4. Diagnostic Alert Banner (Shown for 10 Seconds with Pulsing Red Border when Clicking Upgrade with Missing Requirements or Resources)
			StyleBoxFlat alertStyle;
			_upgradeDiagnosticBanner = CreateCardPanelWithStyle(0, out alertStyle);
			alertStyle.BorderColor = new Color("#EF4444");
			alertStyle.BorderWidthLeft = 2;
			alertStyle.BorderWidthRight = 2;
			alertStyle.BorderWidthTop = 2;
			alertStyle.BorderWidthBottom = 2;
			alertStyle.BgColor = new Color(0.14f, 0.03f, 0.04f, 0.96f);
			_upgradeDiagnosticBanner.Visible = false;
			VBoxContainer alertContent = CreateCardContainer(_upgradeDiagnosticBanner);
			_upgradeDiagnosticLabel = new Label
			{
				Text = "",
				Modulate = new Color("#FCA5A5"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			_upgradeDiagnosticLabel.AddThemeFontSizeOverride("font_size", 14);
			alertContent.AddChild(_upgradeDiagnosticLabel);
			_contentStack.AddChild(_upgradeDiagnosticBanner);

			// 5. Action Buttons (Always Clickable so Tapping While Greyed-Out Triggers Diagnostic Alert & 10-Second Red Highlight Glow!)
			_btnStartUpgrade = new Button
			{
				CustomMinimumSize = new Vector2(0, 52),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_btnStartUpgrade.AddThemeFontSizeOverride("font_size", 16);
			_btnStartUpgrade.Pressed += () => OnStartUpgradePressed(targetLevel, durationSec, cost);
			_contentStack.AddChild(_btnStartUpgrade);

			_btnQueueUpgrade = new Button
			{
				CustomMinimumSize = new Vector2(0, 48),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_btnQueueUpgrade.AddThemeFontSizeOverride("font_size", 15);
			_btnQueueUpgrade.Pressed += () => OnQueueUpgradePressed(targetLevel, durationSec, cost);
			_contentStack.AddChild(_btnQueueUpgrade);

			// Run initial live affordability & button state sync immediately
			UpdateLiveUpgradeAffordabilityAndGlow(0.0);
		}

		private void TriggerUpgradeDiagnosticGlow(bool highlightPrereqs, bool highlightResources, string message)
		{
			if (highlightPrereqs) _prereqRedGlowTimer = 10.0;
			if (highlightResources) _resourceRedGlowTimer = 10.0;

			if (_upgradeDiagnosticBanner != null && IsInstanceValid(_upgradeDiagnosticBanner) &&
				_upgradeDiagnosticLabel != null && IsInstanceValid(_upgradeDiagnosticLabel))
			{
				_upgradeDiagnosticLabel.Text = message;
				_upgradeDiagnosticBanner.Visible = true;
			}
		}

		private string BuildMissingResourcesSummary((long e, long i, long t, long h) cost, bool includeQueueGold)
		{
			if (BaseHUD.Instance == null) return "resources";
			List<string> missing = new List<string>();

			long curE = (long)BaseHUD.Instance.ResE;
			long curI = (long)BaseHUD.Instance.ResI;
			long curT = (long)BaseHUD.Instance.ResT;
			long curH = (long)BaseHUD.Instance.ResH3;

			if (curE < cost.e) missing.Add($"+{(cost.e - curE):N0} Energy");
			if (curI < cost.i) missing.Add($"+{(cost.i - curI):N0} Iron");
			if (curT < cost.t) missing.Add($"+{(cost.t - curT):N0} Titanium");
			if (curH < cost.h) missing.Add($"+{(cost.h - curH):N0} Helium-3");
			if (includeQueueGold && BaseHUD.Instance.GGold < 25)
			{
				missing.Add($"+{25 - BaseHUD.Instance.GGold} Galaxy Gold (25 GGold required to queue)");
			}

			return missing.Count > 0 ? string.Join(", ", missing) : "required resources";
		}

		private void ApplyButtonStyle(Button btn, bool isGreyedOut, Color activeColor)
		{
			StyleBoxFlat style = new StyleBoxFlat
			{
				BgColor = isGreyedOut ? new Color(0.16f, 0.19f, 0.24f, 0.88f) : activeColor,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6,
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				BorderColor = isGreyedOut ? new Color(0.45f, 0.52f, 0.62f, 0.55f) : new Color(1f, 1f, 1f, 0.22f),
				ContentMarginLeft = 10,
				ContentMarginRight = 10,
				ContentMarginTop = 6,
				ContentMarginBottom = 6
			};
			btn.Modulate = isGreyedOut ? new Color(0.82f, 0.85f, 0.90f, 1f) : Colors.White;
			btn.AddThemeStyleboxOverride("normal", style);
			btn.AddThemeStyleboxOverride("hover", style);
			btn.AddThemeStyleboxOverride("pressed", style);
			btn.AddThemeStyleboxOverride("disabled", style);
		}

		private void OnRepairBuildingPressed()
		{
			if (BaseHUD.Instance == null || !_isActiveBuildingDamaged) return;

			var repCost = (_currentRepairMetrics.costE, _currentRepairMetrics.costI, _currentRepairMetrics.costT, _currentRepairMetrics.costH3);
			bool hasResources = BaseHUD.Instance.ResE >= repCost.costE &&
								BaseHUD.Instance.ResI >= repCost.costI &&
								BaseHUD.Instance.ResT >= repCost.costT &&
								BaseHUD.Instance.ResH3 >= repCost.costH3;

			if (!hasResources)
			{
				string missingSummary = BuildMissingResourcesSummary(repCost, includeQueueGold: false);
				TriggerUpgradeDiagnosticGlow(
					highlightPrereqs: false,
					highlightResources: false,
					$"⚠️ INSUFFICIENT RESOURCES FOR STRUCTURAL REPAIR (+{_currentRepairMetrics.damagedHp:N0} HP):\nYou still need {missingSummary} to repair {ActiveBuildingName} back to Level {ActiveBuildingLevel} ({ActiveBuildingLevel * 100:N0} HP)."
				);
				return;
			}

			// Deduct 1% of specific level's upgrade cost per damaged HP and restore building to 100% HP for its current level
			BaseHUD.Instance.ResE -= repCost.costE;
			BaseHUD.Instance.ResI -= repCost.costI;
			BaseHUD.Instance.ResT -= repCost.costT;
			BaseHUD.Instance.ResH3 -= repCost.costH3;

			int fullHp = ActiveBuildingLevel * 100;
			if (BaseHUD.Instance.BuildingLevels != null)
			{
				BaseHUD.Instance.BuildingLevels.Remove($"__hp_{ActiveBuildingId}");
			}
			if (AllBuildingLevels != null)
			{
				AllBuildingLevels.Remove($"__hp_{ActiveBuildingId}");
			}

			BaseHUD.Instance.UpdateHUDDisplay();
			BaseHUD.Instance.TriggerInstantSave();
			EmitSignal(SignalName.BuildingUpgraded, ActiveBuildingId, ActiveBuildingLevel);
			GD.Print($"[REPAIR ENGINE] Repaired {ActiveBuildingName} (+{_currentRepairMetrics.damagedHp} HP -> {fullHp}/{fullHp} HP, Level {ActiveBuildingLevel}).");
			RefreshInspector();
		}

		private void OnStartUpgradePressed(int targetLevel, int durationSec, (long e, long i, long t, long h) cost)
		{
			if (BaseHUD.Instance == null) return;

			// 0. Blueprint Rule: Repair required before further upgrades for affected levels!
			if (_isActiveBuildingDamaged)
			{
				string effStr = GameMath.FormatEffectiveLevel(_currentRepairMetrics.effectiveLevel);
				TriggerUpgradeDiagnosticGlow(
					highlightPrereqs: false,
					highlightResources: false,
					$"🔧 STRUCTURAL REPAIR REQUIRED BEFORE UPGRADING:\n{ActiveBuildingName} took -{_currentRepairMetrics.damagedHp:N0} HP damage and is currently at fractional Level {effStr} / {ActiveBuildingLevel}. Repair it above (costs 1% of Level {ActiveBuildingLevel}'s upgrade cost per damaged HP) before upgrading to Level {targetLevel}!"
				);
				return;
			}

			// 1. Check Building Prerequisites First -> Trigger 10-Second Red Highlight Glow on Construction Matrix!
			if (!_currentAllPrereqsMet)
			{
				List<string> unmetDescriptions = new List<string>();
				foreach (var req in _currentPrereqList)
				{
					if (!req.IsMet)
					{
						unmetDescriptions.Add($"• Upgrade or Queue {req.BuildingName} to Level {req.RequiredLevel} (Currently Level {req.CurrentLevel})");
					}
				}
				string msg = "⚠️ UPGRADE LOCKED — PREREQUISITES NOT MET:\n" + string.Join("\n", unmetDescriptions);
				TriggerUpgradeDiagnosticGlow(highlightPrereqs: true, highlightResources: false, msg);
				return;
			}

			// 2. Check Engineering Bay Busy State
			if (BaseHUD.Instance.ActiveBuild != null)
			{
				TriggerUpgradeDiagnosticGlow(
					highlightPrereqs: false,
					highlightResources: false,
					$"⚠️ ENGINEERING BAY BUSY:\nCurrently constructing {BaseHUD.Instance.ActiveBuild.BuildingName} Lvl {BaseHUD.Instance.ActiveBuild.TargetLevel}. Use the '➕ QUEUE LVL {targetLevel}' button below to add this upgrade to the queue!"
				);
				return;
			}

			// 3. Check Resource Affordability -> Trigger 10-Second Red Highlight Glow on Required Resources Box!
			bool hasResources = BaseHUD.Instance.ResE >= cost.e &&
								BaseHUD.Instance.ResI >= cost.i &&
								BaseHUD.Instance.ResT >= cost.t &&
								BaseHUD.Instance.ResH3 >= cost.h;

			if (!hasResources)
			{
				string missingSummary = BuildMissingResourcesSummary(cost, includeQueueGold: false);
				TriggerUpgradeDiagnosticGlow(
					highlightPrereqs: false,
					highlightResources: true,
					$"⚠️ INSUFFICIENT RESOURCES FOR LEVEL {targetLevel}:\nYou still need {missingSummary} to start this upgrade."
				);
				return;
			}

			// All checks passed -> Deduct resources and start upgrade!
			BaseHUD.Instance.ResE -= cost.e;
			BaseHUD.Instance.ResI -= cost.i;
			BaseHUD.Instance.ResT -= cost.t;
			BaseHUD.Instance.ResH3 -= cost.h;

			var item = new GameMath.BuildQueueItem
			{
				BuildingId = ActiveBuildingId,
				BuildingName = ActiveBuildingName,
				TargetLevel = targetLevel,
				DurationLeft = durationSec,
				CompletionTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ((long)durationSec * 1000L),
				CostE = cost.e,
				CostI = cost.i,
				CostT = cost.t,
				CostH3 = cost.h
			};

			BaseHUD.Instance.ActiveBuild = item;
			BaseHUD.Instance.UpdateHUDDisplay();
			BaseHUD.Instance.UpdateEngineeringDockUI();
			BaseHUD.Instance.TriggerInstantSave();

			RefreshInspector();
		}

		private void OnQueueUpgradePressed(int targetLevel, int durationSec, (long e, long i, long t, long h) cost)
		{
			if (BaseHUD.Instance == null) return;

			// 0. Blueprint Rule: Repair required before further upgrades for affected levels!
			if (_isActiveBuildingDamaged)
			{
				string effStr = GameMath.FormatEffectiveLevel(_currentRepairMetrics.effectiveLevel);
				TriggerUpgradeDiagnosticGlow(
					highlightPrereqs: false,
					highlightResources: false,
					$"🔧 STRUCTURAL REPAIR REQUIRED BEFORE QUEUING:\n{ActiveBuildingName} took -{_currentRepairMetrics.damagedHp:N0} HP damage and is currently at fractional Level {effStr} / {ActiveBuildingLevel}. Repair it above (costs 1% of Level {ActiveBuildingLevel}'s upgrade cost per damaged HP) before queuing Level {targetLevel}!"
				);
				return;
			}

			// 1. Check Building Prerequisites First -> Trigger 10-Second Red Highlight Glow on Construction Matrix!
			if (!_currentAllPrereqsMet)
			{
				List<string> unmetDescriptions = new List<string>();
				foreach (var req in _currentPrereqList)
				{
					if (!req.IsMet)
					{
						unmetDescriptions.Add($"• Upgrade or Queue {req.BuildingName} to Level {req.RequiredLevel} (Currently Level {req.CurrentLevel})");
					}
				}
				string msg = "⚠️ QUEUE LOCKED — PREREQUISITES NOT MET:\n" + string.Join("\n", unmetDescriptions);
				TriggerUpgradeDiagnosticGlow(highlightPrereqs: true, highlightResources: false, msg);
				return;
			}

			// 2. Check if Engineering Bay is Idle
			if (BaseHUD.Instance.ActiveBuild == null)
			{
				TriggerUpgradeDiagnosticGlow(
					highlightPrereqs: false,
					highlightResources: false,
					$"ℹ️ ENGINEERING BAY IS IDLE:\nYou do not need to spend 25 Galaxy Gold to queue right now—click 'START UPGRADE TO LVL {targetLevel}' above for free!"
				);
				return;
			}

			// 3. Check Queue Capacity
			if (BaseHUD.Instance.BuildQueue.Count >= 20)
			{
				TriggerUpgradeDiagnosticGlow(
					highlightPrereqs: false,
					highlightResources: false,
					"⚠️ CONSTRUCTION QUEUE FULL:\nMaximum of 20 queued upgrades reached. Wait for an active upgrade to finish first."
				);
				return;
			}

			// 4. Check Resources & 25 Galaxy Gold Affordability -> Trigger 10-Second Red Highlight Glow on Resources!
			bool hasResourcesAndGold = BaseHUD.Instance.GGold >= 25 &&
									   BaseHUD.Instance.ResE >= cost.e &&
									   BaseHUD.Instance.ResI >= cost.i &&
									   BaseHUD.Instance.ResT >= cost.t &&
									   BaseHUD.Instance.ResH3 >= cost.h;

			if (!hasResourcesAndGold)
			{
				string missingSummary = BuildMissingResourcesSummary(cost, includeQueueGold: true);
				TriggerUpgradeDiagnosticGlow(
					highlightPrereqs: false,
					highlightResources: true,
					$"⚠️ INSUFFICIENT RESOURCES TO QUEUE LEVEL {targetLevel}:\nYou still need {missingSummary} to queue this upgrade."
				);
				return;
			}

			ShowConfirmDialog(
				"AUTHORIZE QUEUE UPGRADE?",
				$"This will consume 25 Galaxy Gold to add {ActiveBuildingName} Lvl {targetLevel} to the Engineering Dock queue.",
				() => 
				{
					BaseHUD.Instance.GGold -= 25;
					BaseHUD.Instance.ResE -= cost.e;
					BaseHUD.Instance.ResI -= cost.i;
					BaseHUD.Instance.ResT -= cost.t;
					BaseHUD.Instance.ResH3 -= cost.h;

					var item = new GameMath.BuildQueueItem
					{
						BuildingId = ActiveBuildingId,
						BuildingName = ActiveBuildingName,
						TargetLevel = targetLevel,
						DurationLeft = durationSec,
						CostE = cost.e,
						CostI = cost.i,
						CostT = cost.t,
						CostH3 = cost.h
					};

					BaseHUD.Instance.BuildQueue.Add(item);
					BaseHUD.Instance.UpdateHUDDisplay();
					BaseHUD.Instance.UpdateEngineeringDockUI();
					BaseHUD.Instance.TriggerInstantSave();

					RefreshInspector();
				}
			);
		}

		private void RefreshInspector()
		{
			InspectBuilding(
				ActiveBuildingId,
				ActiveBuildingName,
				ActiveBuildingLevel,
				ActiveBuildingColor,
				ActiveDistrictCode,
				ActiveSlotIndex,
				ActiveDistrictLevels,
				CurrentIndustrialCoreLevel,
				AllBuildingLevels
			);
		}

		private PanelContainer CreateCardPanel(float minHeight = 0)
		{
			return CreateCardPanelWithStyle(minHeight, out _);
		}

		private PanelContainer CreateCardPanelWithStyle(float minHeight, out StyleBoxFlat styleOut)
		{
			PanelContainer p = new PanelContainer
			{
				CustomMinimumSize = new Vector2(0, minHeight),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			styleOut = new StyleBoxFlat
			{
				BgColor = new Color(0.06f, 0.13f, 0.24f, 0.98f),
				BorderColor = new Color(0.15f, 0.75f, 0.98f, 0.85f),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8,
				ContentMarginLeft = 14,
				ContentMarginRight = 14,
				ContentMarginTop = 10,
				ContentMarginBottom = 10
			};
			p.AddThemeStyleboxOverride("panel", styleOut);
			return p;
		}

		private VBoxContainer CreateCardContainer(PanelContainer parent)
		{
			VBoxContainer box = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			box.AddThemeConstantOverride("separation", 6);
			parent.AddChild(box);
			return box;
		}

		private void OnRecruitUnit(string unitId)
		{
			if (_unitQtyInputs.TryGetValue(unitId, out LineEdit input) && int.TryParse(input.Text, out int qty) && qty > 0)
			{
				QueueSimultaneousUnitBay(unitId, qty);
			}
		}

		private void QueueSimultaneousUnitBay(string unitId, int qty)
		{
			if (qty <= 0 || BaseHUD.Instance == null) return;
			var unit = GameData.Units.Find(u => u.Id == unitId);
			if (unit == null) return;

			long costE = unit.CostE * qty;
			long costI = unit.CostI * qty;
			long costT = unit.CostT * qty;

			if (BaseHUD.Instance.ResE >= costE && BaseHUD.Instance.ResI >= costI && BaseHUD.Instance.ResT >= costT)
			{
				BaseHUD.Instance.ResE -= costE;
				BaseHUD.Instance.ResI -= costI;
				BaseHUD.Instance.ResT -= costT;

				double singleSec = Math.Max(1.0, unit.AssemblyTimeSec / Math.Max(0.1f, BaseHUD.Instance.ServerSpeed));

				if (BaseHUD.Instance.ActiveUnitBays.TryGetValue(unitId, out var existingBay) && existingBay.RemainingCount > 0)
				{
					existingBay.RemainingCount += qty;
					if (existingBay.NextUnitDropUnixMs <= 0)
					{
						existingBay.NextUnitDropUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(existingBay.NextUnitTimerSec * 1000.0);
					}
				}
				else
				{
					BaseHUD.Instance.ActiveUnitBays[unitId] = new GameMath.UnitAssemblyBayJob
					{
						UnitId = unitId,
						RemainingCount = qty,
						SingleUnitDurationSec = singleSec,
						NextUnitTimerSec = singleSec,
						NextUnitDropUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(singleSec * 1000.0)
					};
				}

				BaseHUD.Instance.UpdateHUDDisplay();
				BaseHUD.Instance.TriggerInstantSave();
				EmitSignal(SignalName.UnitsRecruited, unitId, qty);
				GD.Print($"[SHIPYARD] Queued +{qty}x {unit.Name} in Independent Assembly Bay ({unitId})!");
				RefreshInspector();
			}
		}

		public void CloseInspector()
		{
			Visible = false;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();
		}
	}
}
