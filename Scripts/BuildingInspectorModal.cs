using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER TACTICAL FACILITY INSPECTOR MODAL (v17.1 Unified Matrix & Queue Lock Edition)
	/// - Dynamically centers on physical screen viewport on open.
	/// - Universal Construction Matrix: All buildings display their Core and Sequence requirements in a unified checklist.
	/// - Queue Lock: Queue button is disabled ("DOCK IDLE") unless an upgrade is already actively running.
	/// - Tri-Button System: Start Upgrade (Free), Queue Upgrade (25 GGold), Instant Assembly (50 GGold).
	/// - Premium Boosts: 7-Day and 30-Day production multipliers with Global variants.
	/// - Transaction Safety Net: Intercepts all GGold purchases with a strict confirmation overlay.
	/// </summary>
	public partial class BuildingInspectorModal : Control
	{
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

		public override void _Ready()
		{
			SetAnchorsPreset(LayoutPreset.FullRect);
			MouseFilter = MouseFilterEnum.Ignore;

			BuildBaseModalFrame();
			BuildConfirmOverlay();
			Visible = false;
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
				Color = new Color(0f, 0f, 0f, 0.55f),
				MouseFilter = MouseFilterEnum.Stop
			};
			_dimBackdrop.SetAnchorsPreset(LayoutPreset.FullRect);
			AddChild(_dimBackdrop);

			_modalChassis = new Panel
			{
				Name = "ModalChassis",
				CustomMinimumSize = new Vector2(520, 600),
				MouseFilter = MouseFilterEnum.Stop
			};

			StyleBoxFlat chassisStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.04f, 0.07f, 0.11f, 0.98f),
				BorderColor = new Color("#00F0FF"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8,
				ShadowColor = new Color(0f, 0.94f, 1f, 0.25f),
				ShadowSize = 20
			};
			_modalChassis.AddThemeStyleboxOverride("panel", chassisStyle);
			AddChild(_modalChassis);

			VBoxContainer mainLayout = new VBoxContainer();
			mainLayout.SetAnchorsPreset(LayoutPreset.FullRect);
			mainLayout.OffsetLeft = 18;
			mainLayout.OffsetRight = -18;
			mainLayout.OffsetTop = 16;
			mainLayout.OffsetBottom = -16;
			mainLayout.AddThemeConstantOverride("separation", 10);
			_modalChassis.AddChild(mainLayout);

			HBoxContainer headerRow = new HBoxContainer();
			headerRow.Alignment = BoxContainer.AlignmentMode.Center;

			_lblHeaderTitle = new Label
			{
				Text = "FACILITY INSPECTOR",
				Modulate = Colors.White,
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_lblHeaderTitle.AddThemeFontSizeOverride("font_size", 16);

			_lblLevelBadge = new Label
			{
				Text = "LVL 1",
				Modulate = new Color("#00F0FF")
			};
			_lblLevelBadge.AddThemeFontSizeOverride("font_size", 12);

			_btnClose = new Button
			{
				Text = " ✕ ",
				CustomMinimumSize = new Vector2(32, 32),
				MouseFilter = MouseFilterEnum.Stop
			};
			_btnClose.AddThemeFontSizeOverride("font_size", 14);
			_btnClose.Pressed += CloseInspector;

			headerRow.AddChild(_lblHeaderTitle);
			headerRow.AddChild(_lblLevelBadge);
			headerRow.AddChild(_btnClose);
			mainLayout.AddChild(headerRow);

			mainLayout.AddChild(new HSeparator());

			_scrollContainer = new ScrollContainer
			{
				SizeFlagsVertical = SizeFlags.ExpandFill,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
			};
			mainLayout.AddChild(_scrollContainer);

			_contentStack = new VBoxContainer();
			_contentStack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_contentStack.AddThemeConstantOverride("separation", 12);
			_scrollContainer.AddChild(_contentStack);
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
				CustomMinimumSize = new Vector2(400, 200)
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
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8,
				ShadowColor = new Color(0f, 0f, 0f, 0.5f),
				ShadowSize = 20
			};
			confirmBox.AddThemeStyleboxOverride("panel", boxStyle);
			_confirmOverlay.AddChild(confirmBox);

			VBoxContainer vBox = new VBoxContainer();
			vBox.SetAnchorsPreset(LayoutPreset.FullRect);
			vBox.OffsetLeft = 20;
			vBox.OffsetRight = -20;
			vBox.OffsetTop = 20;
			vBox.OffsetBottom = -20;
			vBox.Alignment = BoxContainer.AlignmentMode.Center;
			vBox.AddThemeConstantOverride("separation", 15);
			confirmBox.AddChild(vBox);

			_lblConfirmTitle = new Label
			{
				Text = "AUTHORIZE TRANSACTION?",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color("#FBBF24")
			};
			_lblConfirmTitle.AddThemeFontSizeOverride("font_size", 14);
			vBox.AddChild(_lblConfirmTitle);

			_lblConfirmMsg = new Label
			{
				Text = "This action will consume Galaxy Gold.",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			_lblConfirmMsg.AddThemeFontSizeOverride("font_size", 11);
			vBox.AddChild(_lblConfirmMsg);

			HBoxContainer btnRow = new HBoxContainer();
			btnRow.Alignment = BoxContainer.AlignmentMode.Center;
			btnRow.AddThemeConstantOverride("separation", 20);

			Button btnCancel = new Button { Text = "CANCEL", CustomMinimumSize = new Vector2(120, 40) };
			btnCancel.Modulate = new Color("#94A3B8");
			btnCancel.Pressed += () => _confirmOverlay.Visible = false;
			btnRow.AddChild(btnCancel);

			Button btnConfirm = new Button { Text = "CONFIRM", CustomMinimumSize = new Vector2(120, 40) };
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
				_lblLevelBadge.Text = $"LVL {currentLevel}";
				_lblLevelBadge.Modulate = themeColor;
			}

			Vector2 vpSize = GetViewportRect().Size;
			if (_modalChassis != null)
			{
				float chassisW = 520;
				float chassisH = 600;
				_modalChassis.Position = new Vector2((vpSize.X - chassisW) * 0.5f, (vpSize.Y - chassisH) * 0.5f);
				_modalChassis.Size = new Vector2(chassisW, chassisH);

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

			if (buildingId == "hub_mil" || buildingId == "hub_arm") BuildShipyardArmoryMenu();
			else if (buildingId == "hub_silo") BuildSiloMenu();
			else if (buildingId == "hub_ahq") BuildAllianceHQMenu();
			else if (buildingId.StartsWith("dist_h3_")) BuildHelium3DistilleryMenu();
			else if (buildingId == "hub_rng") BuildDeepRadarMenu();
			else if (buildingId == "hub_mgd") BuildGalaxyGoldExchangeMenu();
			else if (buildingId == "hub_cmd") BuildIndustrialCoreMenu();
			else if (buildingId.StartsWith("dist_")) BuildStandardSubMineMenu();
			else BuildGenericFacilityMenu();

			if (buildingId != "hub_mgd" && buildingId != "hub_mil" && buildingId != "hub_arm")
			{
				AddBlueprintUpgradeSection(buildingId.StartsWith("dist_"), 30, simLevels, simActiveLevel);
			}
			else if (buildingId == "hub_mil" || buildingId == "hub_arm")
			{
				AddBlueprintUpgradeSection(false, 30, simLevels, simActiveLevel);
			}

			Visible = true;
		}

		private void BuildSiloMenu()
		{
			long currentCap = GameMath.CalcSiloCapacity(ActiveBuildingLevel);
			long nextCap = GameMath.CalcSiloCapacity(ActiveBuildingLevel + 1);
			float vaultProt = GameMath.GetSiloVaultProtection(ActiveBuildingLevel);
			int hp = GameMath.GetStructureHp(ActiveBuildingLevel);
			int pop = GameMath.GetStructurePop("silo", ActiveBuildingLevel);

			Panel infoBox = CreateCardPanel(90);
			VBoxContainer infoContent = CreateCardContainer(infoBox);

			Label lblTitle = new Label { Text = "PRESSURIZED CONTAINMENT VAULT STATUS:", Modulate = new Color("#00F0FF") };
			lblTitle.AddThemeFontSizeOverride("font_size", 9);

			Label lblCap = new Label
			{
				Text = $"Global Capacity: {currentCap:N0} Units (15h Peak Production)\nNext Level: {nextCap:N0} Units (+{(nextCap - currentCap):N0})\nRaid Vault Security: {(vaultProt * 100):F1}% Protected | HP: {hp:N0} | Pop: {pop}",
				Modulate = Colors.White
			};
			lblCap.AddThemeFontSizeOverride("font_size", 10);

			infoContent.AddChild(lblTitle);
			infoContent.AddChild(lblCap);
			_contentStack.AddChild(infoBox);
		}

		private void BuildAllianceHQMenu()
		{
			int hp = GameMath.GetStructureHp(ActiveBuildingLevel);
			int pop = GameMath.GetStructurePop("ahq", ActiveBuildingLevel);

			Panel infoBox = CreateCardPanel(90);
			VBoxContainer infoContent = CreateCardContainer(infoBox);

			Label lblTitle = new Label { Text = "ALLIANCE EMBASSY & STRATEGIC HIGH COMMAND:", Modulate = new Color("#38BDF8") };
			lblTitle.AddThemeFontSizeOverride("font_size", 9);

			Label lblDesc = new Label
			{
				Text = $"HQ Level: {ActiveBuildingLevel} | HP: {hp:N0} | Pop: {pop}\nReinforcement Garrison: {(ActiveBuildingLevel * 25000):N0} Units\nAlliance Rally Speed: +{(ActiveBuildingLevel * 2.5f):F1}%",
				Modulate = Colors.White
			};
			lblDesc.AddThemeFontSizeOverride("font_size", 10);

			infoContent.AddChild(lblTitle);
			infoContent.AddChild(lblDesc);
			_contentStack.AddChild(infoBox);

			Button btnAllianceRally = new Button
			{
				Text = "🛡️ OPEN ALLIANCE DIPLOMATIC EMBASSY",
				CustomMinimumSize = new Vector2(0, 36)
			};
			btnAllianceRally.AddThemeFontSizeOverride("font_size", 10);
			btnAllianceRally.Modulate = new Color("#38BDF8");
			btnAllianceRally.Pressed += () => GD.Print("[ALLIANCE HQ] Diplomatic Interface Initialized.");
			_contentStack.AddChild(btnAllianceRally);
		}

		private void BuildGenericFacilityMenu()
		{
			int hp = GameMath.GetStructureHp(ActiveBuildingLevel);
			int pop = GameMath.GetStructurePop(ActiveBuildingId, ActiveBuildingLevel);

			Panel infoBox = CreateCardPanel(90);
			VBoxContainer infoContent = CreateCardContainer(infoBox);

			Label lblTitle = new Label { Text = $"{ActiveBuildingName.ToUpper()} STATUS:", Modulate = ActiveBuildingColor };
			lblTitle.AddThemeFontSizeOverride("font_size", 9);

			string roleDesc = ActiveBuildingId switch
			{
				"hub_com" => "Diplomatic Link Relay. Unlocks alliance founding, treaties, and global division rankings.",
				"hub_flt" => "Orbital flight operations center. Manages tactical strike group manifests and deployment.",
				"hub_trd" => "Galactic commerce network. Enables inter-colony supply transfers and alliance trade aid.",
				"hub_rsh" => "Advanced science division. Unlocks higher-tier warship blueprints and energy technologies.",
				"hub_shd" => "High-yield deflection generator. Mitigates casualty rates during planetary bombardments.",
				_ => "Specialized colonial facility."
			};

			Label lblDesc = new Label
			{
				Text = $"{roleDesc}\nStructural Health: {hp:N0} HP | Operational Staff: {pop} Officers",
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			lblDesc.AddThemeFontSizeOverride("font_size", 9);

			infoContent.AddChild(lblTitle);
			infoContent.AddChild(lblDesc);
			_contentStack.AddChild(infoBox);
		}

		private void BuildStandardSubMineMenu()
		{
			long currentYield = GameMath.CalcSubMineYield(ActiveBuildingLevel);
			long nextYield = GameMath.CalcSubMineYield(ActiveBuildingLevel + 1);
			int hp = GameMath.GetStructureHp(ActiveBuildingLevel);
			int pop = GameMath.GetStructurePop("mine", ActiveBuildingLevel);

			Panel yieldBox = CreateCardPanel(80);
			VBoxContainer yieldContent = CreateCardContainer(yieldBox);

			Label lblYieldTitle = new Label { Text = "PRODUCTION TELEMETRY OUTPUT:", Modulate = new Color("#94A3B8") };
			lblYieldTitle.AddThemeFontSizeOverride("font_size", 9);

			HBoxContainer yieldValues = new HBoxContainer();
			Label lblCurrent = new Label { Text = $"Current: {currentYield:N0}/h", Modulate = Colors.White, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			lblCurrent.AddThemeFontSizeOverride("font_size", 11);

			Label lblNext = new Label { Text = $"➔ Next: {nextYield:N0}/h", Modulate = new Color("#22C55E") };
			lblNext.AddThemeFontSizeOverride("font_size", 11);

			yieldValues.AddChild(lblCurrent);
			yieldValues.AddChild(lblNext);

			Label lblStats = new Label
			{
				Text = $"Structural Health: {hp:N0} HP | Population Employed: {pop} Engineers",
				Modulate = new Color("#94A3B8")
			};
			lblStats.AddThemeFontSizeOverride("font_size", 9);

			yieldContent.AddChild(lblYieldTitle);
			yieldContent.AddChild(yieldValues);
			yieldContent.AddChild(lblStats);
			_contentStack.AddChild(yieldBox);

			BuildBoostSection(ActiveDistrictCode);
		}

		private void BuildIndustrialCoreMenu()
		{
			double wallMitigation = GameMath.GetWallMitigation(ActiveBuildingLevel);
			int hp = GameMath.GetStructureHp(ActiveBuildingLevel);
			int pop = GameMath.GetStructurePop("cmd", ActiveBuildingLevel);

			Panel infoBox = CreateCardPanel(95);
			VBoxContainer infoContent = CreateCardContainer(infoBox);

			Label lblTitle = new Label { Text = "INDUSTRIAL CORE MASTER SPIRE:", Modulate = new Color("#FFFFFF") };
			lblTitle.AddThemeFontSizeOverride("font_size", 9);

			Label lblSpire = new Label
			{
				Text = $"Core Level: {ActiveBuildingLevel} / 20 | HP: {hp:N0} | Pop: {pop}\nPerimeter Deflection Mitigation: {(wallMitigation * 100):F1}%\nMaster Gatekeeper: Reaching Lvl 20 unlocks Sub-Building Mastery (Lvl 50) & Colony Ships.",
				Modulate = new Color("#CBD5E1")
			};
			lblSpire.AddThemeFontSizeOverride("font_size", 10);

			infoContent.AddChild(lblTitle);
			infoContent.AddChild(lblSpire);
			_contentStack.AddChild(infoBox);
		}

		private void BuildHelium3DistilleryMenu()
		{
			long currentYield = GameMath.CalcSubMineYield(ActiveBuildingLevel);
			long nextYield = GameMath.CalcSubMineYield(ActiveBuildingLevel + 1);
			int hp = GameMath.GetStructureHp(ActiveBuildingLevel);
			int pop = GameMath.GetStructurePop("h3", ActiveBuildingLevel);

			Panel monitorBox = CreateCardPanel(90);
			VBoxContainer monitorContent = CreateCardContainer(monitorBox);

			Label lblTitle = new Label { Text = "HELIUM-3 FUEL EXTRACTION MONITOR:", Modulate = new Color("#EAB308") };
			lblTitle.AddThemeFontSizeOverride("font_size", 9);

			HBoxContainer row1 = new HBoxContainer();
			Label lblRateTitle = new Label { Text = "Distillery Output:", Modulate = new Color("#94A3B8"), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			Label lblRateVal = new Label { Text = $"+{currentYield:N0}/h ➔ +{nextYield:N0}/h", Modulate = new Color("#22C55E") };
			lblRateTitle.AddThemeFontSizeOverride("font_size", 10);
			lblRateVal.AddThemeFontSizeOverride("font_size", 10);
			row1.AddChild(lblRateTitle);
			row1.AddChild(lblRateVal);

			Label lblStarveNotice = new Label
			{
				Text = "Note: If H3 reserves reach 0, fleet attrition will trigger.",
				Modulate = new Color("#F59E0B")
			};
			lblStarveNotice.AddThemeFontSizeOverride("font_size", 8);

			monitorContent.AddChild(lblTitle);
			monitorContent.AddChild(row1);
			monitorContent.AddChild(lblStarveNotice);
			_contentStack.AddChild(monitorBox);

			BuildBoostSection("H3");
		}

		private void BuildBoostSection(string resCode)
		{
			Panel boostBox = CreateCardPanel(110);
			VBoxContainer boostContent = CreateCardContainer(boostBox);

			Label lblTitle = new Label { Text = $"PREMIUM PRODUCTION BOOSTS ({resCode}):", Modulate = new Color("#FBBF24") };
			lblTitle.AddThemeFontSizeOverride("font_size", 9);
			boostContent.AddChild(lblTitle);

			if (BaseHUD.Instance != null && BaseHUD.Instance.ActiveBoosts.TryGetValue(resCode, out var activeBoost))
			{
				if (activeBoost.Expiration > DateTimeOffset.UtcNow)
				{
					TimeSpan remaining = activeBoost.Expiration - DateTimeOffset.UtcNow;
					Label lblActive = new Label 
					{ 
						Text = $"ACTIVE: +{((activeBoost.Multiplier - 1.0f) * 100):F0}% BOOST\nEXPIRES IN: {remaining.Days}d {remaining.Hours}h {remaining.Minutes}m", 
						Modulate = new Color("#22C55E") 
					};
					lblActive.AddThemeFontSizeOverride("font_size", 9);
					boostContent.AddChild(lblActive);
				}
			}

			HBoxContainer btnRow1 = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnRow1.AddThemeConstantOverride("separation", 10);

			Button btnBoost1 = new Button { Text = "+30% (7 DAYS)\n250 GGOLD", CustomMinimumSize = new Vector2(140, 36), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnBoost1.AddThemeFontSizeOverride("font_size", 8);
			btnBoost1.Modulate = new Color("#FBBF24");
			btnBoost1.Pressed += () => OnBuyBoostPressed(resCode, 1.30f, 7, 250);
			btnRow1.AddChild(btnBoost1);

			Button btnBoost2 = new Button { Text = "+75% (7 DAYS)\n500 GGOLD", CustomMinimumSize = new Vector2(140, 36), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnBoost2.AddThemeFontSizeOverride("font_size", 8);
			btnBoost2.Modulate = new Color("#FBBF24");
			btnBoost2.Pressed += () => OnBuyBoostPressed(resCode, 1.75f, 7, 500);
			btnRow1.AddChild(btnBoost2);

			boostContent.AddChild(btnRow1);

			Button btnBoost3 = new Button { Text = "+70% (30 DAYS) - 2150 GGOLD", CustomMinimumSize = new Vector2(0, 32) };
			btnBoost3.AddThemeFontSizeOverride("font_size", 9);
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
			int maxRange = Math.Min(GameMath.TotalMoons, ActiveBuildingLevel * 100);

			Panel radarBox = CreateCardPanel(100);
			VBoxContainer radarContent = CreateCardContainer(radarBox);

			Label lblTitle = new Label { Text = "LONG-RANGE SENSOR STATUS:", Modulate = new Color("#F59E0B") };
			lblTitle.AddThemeFontSizeOverride("font_size", 9);

			Label lblDesc = new Label
			{
				Text = $"Radar Level: {ActiveBuildingLevel} | Scan Horizon: {maxRange:N0} Moons across Galactic Orbit",
				Modulate = Colors.White
			};
			lblDesc.AddThemeFontSizeOverride("font_size", 10);

			HBoxContainer jumpRow = new HBoxContainer();
			_radarJumpInput = new LineEdit
			{
				PlaceholderText = $"Moon # [1-{GameMath.TotalMoons}]",
				CustomMinimumSize = new Vector2(160, 32),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_radarJumpInput.AddThemeFontSizeOverride("font_size", 10);

			Button btnWarpScan = new Button
			{
				Text = "WARP SCAN",
				CustomMinimumSize = new Vector2(100, 32)
			};
			btnWarpScan.AddThemeFontSizeOverride("font_size", 10);
			btnWarpScan.Modulate = new Color("#F59E0B");
			btnWarpScan.Pressed += () =>
			{
				if (int.TryParse(_radarJumpInput.Text, out int moon) && moon >= 1 && moon <= GameMath.TotalMoons)
				{
					GD.Print($"[RADAR] Warping camera scan to Moon #{moon}");
					CloseInspector();
				}
			};

			jumpRow.AddChild(_radarJumpInput);
			jumpRow.AddChild(btnWarpScan);

			radarContent.AddChild(lblTitle);
			radarContent.AddChild(lblDesc);
			radarContent.AddChild(jumpRow);
			_contentStack.AddChild(radarBox);
		}

		private void BuildShipyardArmoryMenu()
		{
			Label lblHeader = new Label
			{
				Text = "RECRUITMENT WORKSHOP MANIFESTS:",
				Modulate = new Color("#EF4444")
			};
			lblHeader.AddThemeFontSizeOverride("font_size", 10);
			_contentStack.AddChild(lblHeader);

			foreach (var unit in GameData.Units)
			{
				bool isUnlocked = ActiveBuildingLevel >= unit.ShipyardReqLvl;

				Panel unitCard = CreateCardPanel(110);
				if (!isUnlocked) unitCard.Modulate = new Color(0.6f, 0.6f, 0.6f, 0.6f);

				VBoxContainer cardContent = CreateCardContainer(unitCard);

				HBoxContainer topRow = new HBoxContainer();
				Label lblUnitName = new Label
				{
					Text = unit.Name.ToUpper(),
					Modulate = isUnlocked ? Colors.White : new Color("#64748B"),
					SizeFlagsHorizontal = SizeFlags.ExpandFill
				};
				lblUnitName.AddThemeFontSizeOverride("font_size", 11);

				Label lblReq = new Label
				{
					Text = $"REQ LVL {unit.ShipyardReqLvl}",
					Modulate = isUnlocked ? new Color("#22C55E") : new Color("#EF4444")
				};
				lblReq.AddThemeFontSizeOverride("font_size", 9);

				topRow.AddChild(lblUnitName);
				topRow.AddChild(lblReq);
				cardContent.AddChild(topRow);

				Label lblStats = new Label
				{
					Text = $"ATK: {unit.Atk} | DEF: {unit.Def} | HP: {unit.Hp} | Cargo: {unit.Cargo:N0} | H3/h: {unit.H3Drain}",
					Modulate = new Color("#94A3B8")
				};
				lblStats.AddThemeFontSizeOverride("font_size", 8);
				cardContent.AddChild(lblStats);

				Label lblCosts = new Label
				{
					Text = $"⚡ {unit.CostE:N0} | ⛏️ {unit.CostI:N0} | 💎 {unit.CostT:N0}",
					Modulate = new Color("#00F0FF")
				};
				lblCosts.AddThemeFontSizeOverride("font_size", 9);
				cardContent.AddChild(lblCosts);

				if (isUnlocked)
				{
					HBoxContainer assembleRow = new HBoxContainer();
					LineEdit inputQty = new LineEdit
					{
						Text = "1",
						PlaceholderText = "Qty",
						CustomMinimumSize = new Vector2(80, 28)
					};
					inputQty.AddThemeFontSizeOverride("font_size", 10);
					_unitQtyInputs[unit.Id] = inputQty;

					Button btnBuild = new Button
					{
						Text = "ASSEMBLE UNITS",
						CustomMinimumSize = new Vector2(140, 28),
						SizeFlagsHorizontal = SizeFlags.ExpandFill
					};
					btnBuild.AddThemeFontSizeOverride("font_size", 9);
					btnBuild.Modulate = new Color("#EF4444");

					string uId = unit.Id;
					btnBuild.Pressed += () => OnRecruitUnit(uId);

					assembleRow.AddChild(inputQty);
					assembleRow.AddChild(btnBuild);
					cardContent.AddChild(assembleRow);
				}
				else
				{
					Label lblLocked = new Label
					{
						Text = $"🔒 UPGRADE SHIPYARD TO LEVEL {unit.ShipyardReqLvl} TO UNLOCK BLUEPRINT",
						Modulate = new Color("#EF4444")
					};
					lblLocked.AddThemeFontSizeOverride("font_size", 8);
					cardContent.AddChild(lblLocked);
				}

				_contentStack.AddChild(unitCard);
			}

			_contentStack.AddChild(new HSeparator());
		}

		private void BuildGalaxyGoldExchangeMenu()
		{
			Label lblHeader = new Label
			{
				Text = "GALAXY GOLD CREDIT BUNDLE EXCHANGE:",
				Modulate = new Color("#FBBF24")
			};
			lblHeader.AddThemeFontSizeOverride("font_size", 10);
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
				Panel packCard = CreateCardPanel(50);
				HBoxContainer row = new HBoxContainer();
				row.SetAnchorsPreset(LayoutPreset.FullRect);
				row.OffsetLeft = 10;
				row.OffsetRight = -10;
				row.OffsetTop = 6;
				row.OffsetBottom = -6;

				Label lblName = new Label
				{
					Text = $"{pack.Name}\n💰 {pack.Gold:N0} GGOLD",
					Modulate = new Color("#FBBF24"),
					SizeFlagsHorizontal = SizeFlags.ExpandFill
				};
				lblName.AddThemeFontSizeOverride("font_size", 9);

				Button btnBuy = new Button
				{
					Text = pack.Price,
					CustomMinimumSize = new Vector2(80, 28)
				};
				btnBuy.AddThemeFontSizeOverride("font_size", 10);
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
			Panel boostBox = CreateCardPanel(110);
			VBoxContainer boostContent = CreateCardContainer(boostBox);

			Label lblTitle = new Label { Text = "GLOBAL EMPIRE BOOSTS (ALL RESOURCES):", Modulate = new Color("#FBBF24") };
			lblTitle.AddThemeFontSizeOverride("font_size", 9);
			boostContent.AddChild(lblTitle);

			HBoxContainer btnRow1 = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnRow1.AddThemeConstantOverride("separation", 10);

			Button btnBoost1 = new Button { Text = "+30% (7 DAYS)\n1000 GGOLD", CustomMinimumSize = new Vector2(140, 36), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnBoost1.AddThemeFontSizeOverride("font_size", 8);
			btnBoost1.Modulate = new Color("#FBBF24");
			btnBoost1.Pressed += () => OnBuyGlobalBoostPressed(1.30f, 7, 1000);
			btnRow1.AddChild(btnBoost1);

			Button btnBoost2 = new Button { Text = "+75% (7 DAYS)\n2000 GGOLD", CustomMinimumSize = new Vector2(140, 36), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			btnBoost2.AddThemeFontSizeOverride("font_size", 8);
			btnBoost2.Modulate = new Color("#FBBF24");
			btnBoost2.Pressed += () => OnBuyGlobalBoostPressed(1.75f, 7, 2000);
			btnRow1.AddChild(btnBoost2);

			boostContent.AddChild(btnRow1);

			Button btnBoost3 = new Button { Text = "+70% (30 DAYS) - 8600 GGOLD", CustomMinimumSize = new Vector2(0, 32) };
			btnBoost3.AddThemeFontSizeOverride("font_size", 9);
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
			int targetLevel = simActiveLevel + 1;

			if (simActiveLevel >= maxLvl)
			{
				Label lblMax = new Label
				{
					Text = $"FACILITY AT MAXIMUM UPGRADE LEVEL ({maxLvl})",
					Modulate = new Color("#22C55E"),
					HorizontalAlignment = HorizontalAlignment.Center
				};
				lblMax.AddThemeFontSizeOverride("font_size", 11);
				_contentStack.AddChild(lblMax);
				return;
			}

			// 1. Generate Universal Construction Matrix
			var prereqs = GameMath.GetFacilityRequirements(ActiveBuildingId, targetLevel, ActiveDistrictCode, ActiveSlotIndex, simLevels);
			bool allReqsMet = true;

			if (prereqs.Count > 0)
			{
				foreach (var r in prereqs) if (!r.IsMet) allReqsMet = false;

				Panel prereqBox = CreateCardPanel(30 + prereqs.Count * 22);
				VBoxContainer prereqContent = CreateCardContainer(prereqBox);

				Label lblPrereqTitle = new Label
				{
					Text = $"CONSTRUCTION MATRIX (LVL {targetLevel} REQUIREMENTS):",
					Modulate = allReqsMet ? new Color("#22C55E") : new Color("#EF4444")
				};
				lblPrereqTitle.AddThemeFontSizeOverride("font_size", 9);
				prereqContent.AddChild(lblPrereqTitle);

				foreach (var req in prereqs)
				{
					HBoxContainer row = new HBoxContainer();
					Label lblStatus = new Label
					{
						Text = req.IsMet ? $"✓ {req.BuildingName} Lvl {req.RequiredLevel} [LVL {req.CurrentLevel}]" : $"🔒 {req.BuildingName} Lvl {req.RequiredLevel} [LVL {req.CurrentLevel}]",
						Modulate = req.IsMet ? new Color("#22C55E") : new Color("#EF4444"),
						SizeFlagsHorizontal = SizeFlags.ExpandFill
					};
					lblStatus.AddThemeFontSizeOverride("font_size", 9);
					row.AddChild(lblStatus);
					prereqContent.AddChild(row);
				}
				_contentStack.AddChild(prereqBox);
			}

			// 2. Evaluate Core Cap Lock
			int simCoreLevel = simLevels.GetValueOrDefault("hub_cmd", CurrentIndustrialCoreLevel);
			bool isCoreCapLocked = ActiveBuildingId != "hub_cmd" && simActiveLevel >= GameMath.GetMaxAllowedSubBuildingLevel(simCoreLevel);

			// 3. Final Lock State
			bool isLocked = isCoreCapLocked || !allReqsMet;
			bool isAlreadyQueued = simActiveLevel > ActiveBuildingLevel;
			bool isEngineBusy = BaseHUD.Instance != null && BaseHUD.Instance.ActiveBuild != null;
			bool isQueueFull = BaseHUD.Instance != null && BaseHUD.Instance.BuildQueue.Count >= 20;

			Label lblReqHeader = new Label { Text = $"REQUIRED RESOURCES FOR LEVEL {targetLevel}:", Modulate = new Color("#94A3B8") };
			lblReqHeader.AddThemeFontSizeOverride("font_size", 9);
			_contentStack.AddChild(lblReqHeader);

			(long e, long i, long t, long h) cost;
			int durationSec;

			if (isSubMine)
			{
				cost = GameMath.CalcSubMineUpgradeCost(ActiveDistrictCode, simActiveLevel);
				durationSec = GameMath.GetSubMineBuildTimeSec(simActiveLevel);
			}
			else if (ActiveBuildingId == "hub_cmd")
			{
				cost = GameMath.CalcIndustrialCoreCost(simActiveLevel);
				durationSec = GameMath.GetIndustrialCoreBuildTimeSec(simActiveLevel);
			}
			else if (ActiveBuildingId == "hub_silo")
			{
				cost = GameMath.CalcStorageSiloCost(simActiveLevel);
				durationSec = GameMath.GetStorageSiloBuildTimeSec(simActiveLevel);
			}
			else
			{
				cost = GameMath.CalcCost(simActiveLevel, 850, 150, 120, 0);
				durationSec = 120;
			}

			GridContainer reqGrid = new GridContainer();
			reqGrid.Columns = 2;
			reqGrid.AddThemeConstantOverride("h_separation", 20);
			reqGrid.AddThemeConstantOverride("v_separation", 6);

			Label lblCostE = new Label { Text = $"⚡ Energy: {cost.e:N0}", Modulate = new Color("#D946EF") };
			Label lblCostI = new Label { Text = $"⛏️ Iron: {cost.i:N0}", Modulate = new Color("#06B6D4") };
			Label lblCostT = new Label { Text = $"💎 Titanium: {cost.t:N0}", Modulate = new Color("#94A3B8") };
			Label lblCostH3 = new Label { Text = $"⛽ Helium-3: {cost.h:N0}", Modulate = new Color("#EAB308") };

			lblCostE.AddThemeFontSizeOverride("font_size", 10);
			lblCostI.AddThemeFontSizeOverride("font_size", 10);
			lblCostT.AddThemeFontSizeOverride("font_size", 10);
			lblCostH3.AddThemeFontSizeOverride("font_size", 10);

			reqGrid.AddChild(lblCostE);
			reqGrid.AddChild(lblCostI);
			reqGrid.AddChild(lblCostT);
			reqGrid.AddChild(lblCostH3);
			_contentStack.AddChild(reqGrid);

			HBoxContainer timeRow = new HBoxContainer();
			Label lblDuration = new Label
			{
				Text = $"⏱️ Construction Duration: {GameMath.FormatTime(durationSec)}",
				Modulate = new Color("#FBBF24"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			lblDuration.AddThemeFontSizeOverride("font_size", 9);
			timeRow.AddChild(lblDuration);
			_contentStack.AddChild(timeRow);

			// 1. START UPGRADE BUTTON (FREE)
			string startText = isEngineBusy ? "ENGINEERING DOCK BUSY" : (isLocked ? "REQUIREMENTS UNMET" : $"START UPGRADE TO LVL {targetLevel}");
			Button btnStart = new Button
			{
				Text = startText,
				CustomMinimumSize = new Vector2(0, 42),
				Disabled = isLocked || isAlreadyQueued || isEngineBusy
			};
			btnStart.AddThemeFontSizeOverride("font_size", 11);
			btnStart.Pressed += () => OnStartUpgradePressed(targetLevel, durationSec, cost);
			ApplyButtonStyle(btnStart, btnStart.Disabled, new Color(0.13f, 0.77f, 0.36f, 0.85f));
			_contentStack.AddChild(btnStart);

			// 2. QUEUE UPGRADE BUTTON (25 GGOLD)
			string queueText;
			if (!isEngineBusy) queueText = "DOCK IDLE (USE START)";
			else if (isQueueFull) queueText = "QUEUE FULL (MAX 20)";
			else if (isLocked) queueText = "REQUIREMENTS UNMET";
			else queueText = $"QUEUE LVL {targetLevel} (25 GGOLD)";

			Button btnQueue = new Button
			{
				Text = queueText,
				CustomMinimumSize = new Vector2(0, 36),
				Disabled = isLocked || isQueueFull || !isEngineBusy
			};
			btnQueue.AddThemeFontSizeOverride("font_size", 10);
			btnQueue.Pressed += () => OnQueueUpgradePressed(targetLevel, durationSec, cost);
			ApplyButtonStyle(btnQueue, btnQueue.Disabled, new Color(0.14f, 0.5f, 0.9f, 0.85f));
			_contentStack.AddChild(btnQueue);

			// 3. INSTANT ASSEMBLY BUTTON (50 GGOLD)
			string instantText = isAlreadyQueued ? "BUILDING IN QUEUE" : (isLocked ? "REQUIREMENTS UNMET" : "⚡ INSTANT ASSEMBLY (50 GGOLD)");
			Button btnInstant = new Button
			{
				Text = instantText,
				CustomMinimumSize = new Vector2(0, 36),
				Disabled = isLocked || isAlreadyQueued
			};
			btnInstant.AddThemeFontSizeOverride("font_size", 10);
			btnInstant.Modulate = btnInstant.Disabled ? new Color(0.5f, 0.5f, 0.5f, 0.5f) : new Color("#FBBF24");
			btnInstant.Pressed += () => OnInstantAssemblyPressed(targetLevel, cost);
			_contentStack.AddChild(btnInstant);
		}

		private void ApplyButtonStyle(Button btn, bool isDisabled, Color activeColor)
		{
			StyleBoxFlat style = new StyleBoxFlat
			{
				BgColor = isDisabled ? new Color(0.18f, 0.22f, 0.28f, 0.7f) : activeColor,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6,
				BorderWidthLeft = isDisabled ? 1 : 0,
				BorderWidthRight = isDisabled ? 1 : 0,
				BorderWidthTop = isDisabled ? 1 : 0,
				BorderWidthBottom = isDisabled ? 1 : 0,
				BorderColor = new Color(0.4f, 0.5f, 0.6f, 0.4f)
			};
			btn.AddThemeStyleboxOverride("normal", style);
			btn.AddThemeStyleboxOverride("disabled", style);
		}

		private void OnStartUpgradePressed(int targetLevel, int durationSec, (long e, long i, long t, long h) cost)
		{
			if (BaseHUD.Instance == null) return;

			if (BaseHUD.Instance.ResE >= cost.e && BaseHUD.Instance.ResI >= cost.i && BaseHUD.Instance.ResT >= cost.t && BaseHUD.Instance.ResH3 >= cost.h)
			{
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

				BaseHUD.Instance.ActiveBuild = item;
				BaseHUD.Instance.UpdateHUDDisplay();
				BaseHUD.Instance.UpdateEngineeringDockUI();
				BaseHUD.Instance.TriggerInstantSave();

				RefreshInspector();
			}
			else
			{
				GD.PrintErr("[INSPECTOR] Insufficient resources to start upgrade.");
			}
		}

		private void OnQueueUpgradePressed(int targetLevel, int durationSec, (long e, long i, long t, long h) cost)
		{
			if (BaseHUD.Instance == null) return;

			if (BaseHUD.Instance.GGold >= 25 && BaseHUD.Instance.ResE >= cost.e && BaseHUD.Instance.ResI >= cost.i && BaseHUD.Instance.ResT >= cost.t && BaseHUD.Instance.ResH3 >= cost.h)
			{
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
			else
			{
				GD.PrintErr("[INSPECTOR] Insufficient resources or GGold to queue upgrade.");
			}
		}

		private void OnInstantAssemblyPressed(int targetLevel, (long e, long i, long t, long h) cost)
		{
			if (BaseHUD.Instance == null) return;

			if (BaseHUD.Instance.GGold >= 50 && BaseHUD.Instance.ResE >= cost.e && BaseHUD.Instance.ResI >= cost.i && BaseHUD.Instance.ResT >= cost.t && BaseHUD.Instance.ResH3 >= cost.h)
			{
				ShowConfirmDialog(
					"AUTHORIZE INSTANT ASSEMBLY?",
					$"This will consume 50 Galaxy Gold to instantly build {ActiveBuildingName} Lvl {targetLevel}.",
					() => 
					{
						BaseHUD.Instance.GGold -= 50;
						BaseHUD.Instance.ResE -= cost.e;
						BaseHUD.Instance.ResI -= cost.i;
						BaseHUD.Instance.ResT -= cost.t;
						BaseHUD.Instance.ResH3 -= cost.h;

						if (ActiveBuildingId == "hub_silo")
						{
							BaseHUD.Instance.StorageSiloLevel = targetLevel;
							BaseHUD.Instance.RecalculateSiloCap();
						}

						if (AllBuildingLevels != null) AllBuildingLevels[ActiveBuildingId] = targetLevel;
						BaseHUD.Instance.BuildingLevels[ActiveBuildingId] = targetLevel;

						BaseHUD.Instance.UpdateHUDDisplay();
						BaseHUD.Instance.TriggerInstantSave();

						EmitSignal(SignalName.BuildingUpgraded, ActiveBuildingId, targetLevel);
						RefreshInspector();
					}
				);
			}
			else
			{
				GD.PrintErr("[INSPECTOR] Insufficient resources or GGold for instant assembly.");
			}
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

		private Panel CreateCardPanel(float height)
		{
			Panel p = new Panel { CustomMinimumSize = new Vector2(0, height) };
			StyleBoxFlat s = new StyleBoxFlat
			{
				BgColor = new Color(0.02f, 0.04f, 0.06f, 0.9f),
				BorderColor = new Color(0.2f, 0.3f, 0.4f, 0.5f),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			p.AddThemeStyleboxOverride("panel", s);
			return p;
		}

		private VBoxContainer CreateCardContainer(Panel parent)
		{
			VBoxContainer box = new VBoxContainer();
			box.SetAnchorsPreset(LayoutPreset.FullRect);
			box.OffsetLeft = 10;
			box.OffsetRight = -10;
			box.OffsetTop = 8;
			box.OffsetBottom = -8;
			box.AddThemeConstantOverride("separation", 4);
			parent.AddChild(box);
			return box;
		}

		private void OnRecruitUnit(string unitId)
		{
			if (_unitQtyInputs.TryGetValue(unitId, out LineEdit input) && int.TryParse(input.Text, out int qty) && qty > 0)
			{
				var unit = GameData.Units.Find(u => u.Id == unitId);
				if (unit != null && BaseHUD.Instance != null)
				{
					long costE = unit.CostE * qty;
					long costI = unit.CostI * qty;
					long costT = unit.CostT * qty;

					if (BaseHUD.Instance.ResE >= costE && BaseHUD.Instance.ResI >= costI && BaseHUD.Instance.ResT >= costT)
					{
						BaseHUD.Instance.ResE -= costE;
						BaseHUD.Instance.ResI -= costI;
						BaseHUD.Instance.ResT -= costT;
						BaseHUD.Instance.UpdateHUDDisplay();

						EmitSignal(SignalName.UnitsRecruited, unitId, qty);
						GD.Print($"[SHIPYARD] Assembled {qty}x {unit.Name}!");
					}
				}
			}
		}

		public void CloseInspector()
		{
			Visible = false;
		}
	}
}
