using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER TACTICAL FACILITY INSPECTOR MODAL (v7.0 Screen-Centered)
	/// - Dynamically centers on physical screen viewport on open.
	/// - Specialized menus for Shipyards, Refineries, Radars (1,000 moons), and Obelisks.
	/// </summary>
	public partial class BuildingInspectorModal : Control
	{
		[Signal] public delegate void BuildingUpgradedEventHandler(string buildingId, int newLevel);
		[Signal] public delegate void UnitsRecruitedEventHandler(string unitId, int count);

		public string ActiveBuildingId = "";
		public string ActiveBuildingName = "";
		public int ActiveBuildingLevel = 1;
		public Color ActiveBuildingColor = Colors.White;

		private ColorRect _dimBackdrop;
		private Panel _modalChassis;
		private Label _lblHeaderTitle;
		private Label _lblLevelBadge;
		private Button _btnClose;
		private ScrollContainer _scrollContainer;
		private VBoxContainer _contentStack;

		private Dictionary<string, LineEdit> _unitQtyInputs = new Dictionary<string, LineEdit>();
		private LineEdit _radarJumpInput;

		public override void _Ready()
		{
			SetAnchorsPreset(LayoutPreset.FullRect);
			MouseFilter = MouseFilterEnum.Ignore;

			BuildBaseModalFrame();
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
				CustomMinimumSize = new Vector2(520, 540),
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

		public void InspectBuilding(string buildingId, string buildingName, int currentLevel, Color themeColor)
		{
			ActiveBuildingId = buildingId;
			ActiveBuildingName = buildingName;
			ActiveBuildingLevel = currentLevel;
			ActiveBuildingColor = themeColor;

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
				float chassisH = 540;
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

			if (buildingId == "hub_mil" || buildingId == "hub_arm")
			{
				BuildShipyardArmoryMenu();
			}
			else if (buildingId.StartsWith("dist_h3_"))
			{
				BuildHelium3DistilleryMenu();
			}
			else if (buildingId == "hub_rng")
			{
				BuildDeepRadarMenu();
			}
			else if (buildingId == "hub_mgd")
			{
				BuildMoongoldObeliskMenu();
			}
			else if (buildingId == "hub_cmd")
			{
				BuildIndustrialCoreMenu();
			}
			else
			{
				BuildStandardMineMenu();
			}

			Visible = true;
		}

		private void BuildStandardMineMenu()
		{
			bool isH3 = ActiveBuildingId.Contains("h3");
			long currentYield = (long)GameMath.CalcOutput(ActiveBuildingLevel, isH3);
			long nextYield = (long)GameMath.CalcOutput(ActiveBuildingLevel + 1, isH3);

			Panel yieldBox = CreateCardPanel(64);
			VBoxContainer yieldContent = CreateCardContainer(yieldBox);

			Label lblYieldTitle = new Label { Text = "PRODUCTION TELEMETRY OUTPUT:", Modulate = new Color("#94A3B8") };
			lblYieldTitle.AddThemeFontSizeOverride("font_size", 9);

			HBoxContainer yieldValues = new HBoxContainer();
			Label lblCurrent = new Label { Text = $"Current: {currentYield:N0}/h", Modulate = Colors.White, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			lblCurrent.AddThemeFontSizeOverride("font_size", 11);

			Label lblNext = new Label { Text = $"➔ Next Lvl: {nextYield:N0}/h", Modulate = new Color("#22C55E") };
			lblNext.AddThemeFontSizeOverride("font_size", 11);

			yieldValues.AddChild(lblCurrent);
			yieldValues.AddChild(lblNext);
			yieldContent.AddChild(lblYieldTitle);
			yieldContent.AddChild(yieldValues);
			_contentStack.AddChild(yieldBox);

			AddUpgradeCostSection();
		}

		private void BuildIndustrialCoreMenu()
		{
			long currentCap = GameMath.CalcStorageCap(ActiveBuildingLevel);
			long nextCap = GameMath.CalcStorageCap(ActiveBuildingLevel + 1);

			Panel infoBox = CreateCardPanel(70);
			VBoxContainer infoContent = CreateCardContainer(infoBox);

			Label lblTitle = new Label { Text = "CITADEL CORE CAPACITY & SECTOR DEFENSE:", Modulate = new Color("#94A3B8") };
			lblTitle.AddThemeFontSizeOverride("font_size", 9);

			Label lblCap = new Label
			{
				Text = $"Resource Storage Ceiling: {currentCap:N0} units (➔ Next: {nextCap:N0})",
				Modulate = new Color("#00F0FF")
			};
			lblCap.AddThemeFontSizeOverride("font_size", 11);

			double wallMitigation = GameMath.GetWallMitigation(ActiveBuildingLevel);
			Label lblWall = new Label
			{
				Text = $"Perimeter Mitigation: {(wallMitigation * 100):F1}% Protection",
				Modulate = new Color("#D946EF")
			};
			lblWall.AddThemeFontSizeOverride("font_size", 10);

			infoContent.AddChild(lblTitle);
			infoContent.AddChild(lblCap);
			infoContent.AddChild(lblWall);
			_contentStack.AddChild(infoBox);

			AddUpgradeCostSection();
		}

		private void BuildHelium3DistilleryMenu()
		{
			long currentYield = (long)GameMath.CalcOutput(ActiveBuildingLevel, true);
			long nextYield = (long)GameMath.CalcOutput(ActiveBuildingLevel + 1, true);

			Panel monitorBox = CreateCardPanel(75);
			VBoxContainer monitorContent = CreateCardContainer(monitorBox);

			Label lblTitle = new Label { Text = "HELIUM-3 FUEL EXTRACTION MONITOR:", Modulate = new Color("#EAB308") };
			lblTitle.AddThemeFontSizeOverride("font_size", 9);

			HBoxContainer row1 = new HBoxContainer();
			Label lblRateTitle = new Label { Text = "Extractor Output:", Modulate = new Color("#94A3B8"), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			Label lblRateVal = new Label { Text = $"+{currentYield:N0}/h ➔ +{nextYield:N0}/h", Modulate = new Color("#22C55E") };
			lblRateTitle.AddThemeFontSizeOverride("font_size", 10);
			lblRateVal.AddThemeFontSizeOverride("font_size", 10);
			row1.AddChild(lblRateTitle);
			row1.AddChild(lblRateVal);

			monitorContent.AddChild(lblTitle);
			monitorContent.AddChild(row1);
			_contentStack.AddChild(monitorBox);

			AddUpgradeCostSection();
		}

		private void BuildDeepRadarMenu()
		{
			// Calibrated to 1,000 moons maximum
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

			AddUpgradeCostSection();
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
			AddUpgradeCostSection();
		}

		private void BuildMoongoldObeliskMenu()
		{
			Label lblHeader = new Label
			{
				Text = "MOONGOLD CREDIT BUNDLE EXCHANGE:",
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
					Text = $"{pack.Name}\n💰 {pack.Gold:N0} MGOLD",
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
						BaseHUD.Instance.MGold += gAmount;
						BaseHUD.Instance.UpdateHUDDisplay();
						GD.Print($"[EXCHANGE] Added {gAmount} MGold to treasury!");
					}
				};

				row.AddChild(lblName);
				row.AddChild(btnBuy);
				packCard.AddChild(row);
				_contentStack.AddChild(packCard);
			}
		}

		private void AddUpgradeCostSection()
		{
			Label lblReqHeader = new Label { Text = "REQUIRED UPGRADE RESOURCES:", Modulate = new Color("#94A3B8") };
			lblReqHeader.AddThemeFontSizeOverride("font_size", 9);
			_contentStack.AddChild(lblReqHeader);

			bool isHub = ActiveBuildingId.StartsWith("hub_");
			var cost = isHub
				? GameMath.CalcHubCost(ActiveBuildingLevel, 1000, 1000, 500, 0)
				: GameMath.CalcCost(ActiveBuildingLevel, 850, 150, 120, 0);

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

			Button btnUpgrade = new Button
			{
				Text = $"UPGRADE TO LEVEL {ActiveBuildingLevel + 1}",
				CustomMinimumSize = new Vector2(0, 42)
			};
			btnUpgrade.AddThemeFontSizeOverride("font_size", 11);
			btnUpgrade.Pressed += OnUpgradePressed;

			StyleBoxFlat btnStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.13f, 0.77f, 0.36f, 0.85f),
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			btnUpgrade.AddThemeStyleboxOverride("normal", btnStyle);
			_contentStack.AddChild(btnUpgrade);

			Button btnSpeedup = new Button
			{
				Text = "⚡ INSTANT ASSEMBLY (50 MGOLD)",
				CustomMinimumSize = new Vector2(0, 36)
			};
			btnSpeedup.AddThemeFontSizeOverride("font_size", 10);
			btnSpeedup.Modulate = new Color("#FBBF24");
			btnSpeedup.Pressed += OnSpeedupPressed;
			_contentStack.AddChild(btnSpeedup);
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

		private void OnUpgradePressed()
		{
			if (BaseHUD.Instance != null)
			{
				bool isHub = ActiveBuildingId.StartsWith("hub_");
				var cost = isHub
					? GameMath.CalcHubCost(ActiveBuildingLevel, 1000, 1000, 500, 0)
					: GameMath.CalcCost(ActiveBuildingLevel, 850, 150, 120, 0);

				if (BaseHUD.Instance.ResE >= cost.e &&
					BaseHUD.Instance.ResI >= cost.i &&
					BaseHUD.Instance.ResT >= cost.t &&
					BaseHUD.Instance.ResH3 >= cost.h)
				{
					BaseHUD.Instance.ResE -= cost.e;
					BaseHUD.Instance.ResI -= cost.i;
					BaseHUD.Instance.ResT -= cost.t;
					BaseHUD.Instance.ResH3 -= cost.h;
					BaseHUD.Instance.UpdateHUDDisplay();

					int newLevel = ActiveBuildingLevel + 1;
					EmitSignal(SignalName.BuildingUpgraded, ActiveBuildingId, newLevel);
					InspectBuilding(ActiveBuildingId, ActiveBuildingName, newLevel, ActiveBuildingColor);
				}
			}
		}

		private void OnSpeedupPressed()
		{
			if (BaseHUD.Instance != null && BaseHUD.Instance.MGold >= 50)
			{
				BaseHUD.Instance.MGold -= 50;
				BaseHUD.Instance.UpdateHUDDisplay();

				int newLevel = ActiveBuildingLevel + 1;
				EmitSignal(SignalName.BuildingUpgraded, ActiveBuildingId, newLevel);
				InspectBuilding(ActiveBuildingId, ActiveBuildingName, newLevel, ActiveBuildingColor);
			}
		}

		public void CloseInspector()
		{
			Visible = false;
		}
	}
}
