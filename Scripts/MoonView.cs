using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: TIER 2 - 20-SLOT LUNAR SPHERE VIEWPORT (v4.0 Circular Crater Nodes, Orbital Border & Smart Fleet Vectors)
	/// - Spacious 1,040px diameter Lunar Sphere disc with crater surface detail and orbital perimeter barrier.
	/// - 20 Circular Base Slot Nodes (B-01 through B-20) arranged with generous spacing.
	/// - Enforces Deep Space Radar circular visibility when cycling Moons.
	/// - Real-Time Color-Coded Fleet Flight Lines & Ship Icons:
	///   * Same-Moon flights travel directly between the two circular Base Slots.
	///   * Inter-Moon flights exit/enter the Lunar Sphere along the exact Galaxy angle toward/from the other Moon!
	///   * Strictly displays ONLY fleets heading TO or FROM the player's own bases, 100% synced with flight time & speed!
	/// </summary>
	public partial class MoonView : Node2D
	{
		public static MoonView Instance { get; private set; }
		public static int SelectedMoonToView = 100;

		public int ViewingMoon = 100;
		public string CommanderName = "COMMANDER ALPHA";
		public string AllianceName = "NONE";

		// Camera & Orbital Border Clamping
		private Camera2D _camera;
		private Vector2 _targetCameraPos = Vector2.Zero;
		private float _targetZoom = 0.86f;
		private const float MinMoonZoom = 0.52f;
		private const float MaxMoonZoom = 1.85f;
		private const float LunarTerrainBorderHalfSize = 1150f;
		private const float LunarSphereRadius = 520f;
		private const float OrbitalEdgeWaypointRadius = 820f;

		private bool _isDragging = false;
		private Vector2 _dragStartMousePos;
		private Vector2 _dragStartCameraPos;
		private float _dragAccumulatedDistance = 0.0f;
		private readonly Dictionary<int, Vector2> _activeTouches = new Dictionary<int, Vector2>();
		private bool _wasMultiTouchGesture = false;

		// Layers & Containers
		private Node2D _lunarDiscContainer;
		private FleetVectorOverlayNode _fleetOverlayNode;
		private CanvasLayer _hudLayer;
		private BaseHUD _hudInstance;
		private PanelContainer _pinnedRadarNavConsole;
		private Label _moonTitleLabel;
		private Label _radarWarningLabel;

		private partial class FleetVectorOverlayNode : Node2D
		{
			public MoonView OwnerView;
			public override void _Draw()
			{
				OwnerView?.DrawLiveMoonFleetVectorsOnTop(this);
			}
		}

		private readonly Dictionary<int, Button> _slotButtons = new Dictionary<int, Button>();
		private readonly Dictionary<int, StyleBoxFlat> _slotStyles = new Dictionary<int, StyleBoxFlat>();
		private readonly Dictionary<int, Polygon2D> _slotCraterDiscs = new Dictionary<int, Polygon2D>();
		private readonly Dictionary<int, Label> _slotSubLabels = new Dictionary<int, Label>();
		private readonly Dictionary<int, SupabaseService.GalacticCommanderEntry> _slotOccupants = new Dictionary<int, SupabaseService.GalacticCommanderEntry>();

		private float _pulseAnimTime = 0f;
		private float _slotVisualRefreshTimer = 0f;
		private float _cloudOccupantsRefreshTimer = 0f;
		private int _moonRefreshRequestId = 0;

		public override void _Ready()
		{
			Instance = this;
			RenderingServer.SetDefaultClearColor(new Color(0.01f, 0.02f, 0.05f, 1.0f));

			int homeMoon = SupabaseService.CachedMoonId > 0 ? SupabaseService.CachedMoonId : 100;
			int radarLvl = Math.Clamp(
				Math.Max(SupabaseService.CachedRadarLevel, SupabaseService.CachedBuildingLevels.GetValueOrDefault("hub_rng", 1)),
				1,
				10
			);
			ViewingMoon = SelectedMoonToView > 0 ? SelectedMoonToView : homeMoon;

			// If the requested moon is outside the player's Radar Circle, snap to Home Moon
			if (!GameMath.IsMoonVisibleInRadarCircle(homeMoon, ViewingMoon, radarLvl))
			{
				ViewingMoon = homeMoon;
				SelectedMoonToView = homeMoon;
			}

			// 1. Camera Initialization
			_camera = GetNodeOrNull<Camera2D>("Camera2D");
			if (_camera == null)
			{
				_camera = new Camera2D { Name = "Camera2D" };
				AddChild(_camera);
			}

			_camera.Position = Vector2.Zero;
			_targetCameraPos = Vector2.Zero;
			_camera.Zoom = new Vector2(_targetZoom, _targetZoom);
			_camera.MakeCurrent();

			// 2. Render Deep Space Backdrop & Lunar Orbital Perimeter Border
			BuildLunarTerrainAndBorder();

			// 3. Build Spacious 1,040px Lunar Sphere Disc & 20 Circular Crater Base Slots
			_lunarDiscContainer = new Node2D { Name = "LunarDiscContainer", ZIndex = 0 };
			AddChild(_lunarDiscContainer);
			BuildLunarSphere();

			// 3B. Top-Level Fleet Trajectory Vector Overlay (ZIndex = 80: Renders ABOVE the Moon Disc & Base Slots so lines are never hidden!)
			_fleetOverlayNode = new FleetVectorOverlayNode
			{
				Name = "FleetVectorOverlayNode",
				ZIndex = 80,
				OwnerView = this
			};
			AddChild(_fleetOverlayNode);

			// 4. Anchor BaseHUD on CanvasLayer (Layer 100) - Persistent Top Bar & Bottom Nav
			_hudLayer = new CanvasLayer { Name = "HUDLayer", Layer = 100 };
			AddChild(_hudLayer);

			_hudInstance = new BaseHUD();
			_hudInstance.CameraZoomRequested += (mult) =>
			{
				_targetZoom = Mathf.Clamp(_targetZoom * mult, MinMoonZoom, MaxMoonZoom);
				ClampCameraToLunarBorder();
			};
			_hudInstance.CameraResetRequested += () =>
			{
				_targetZoom = 0.86f;
				_targetCameraPos = Vector2.Zero;
				ClampCameraToLunarBorder();
			};
			_hudInstance.ColonyDataLoaded += () =>
			{
				NotifyExternalStateUpdated(refreshCloudOccupants: true);
			};
			_hudLayer.AddChild(_hudInstance);

			// 4B. Pin Deep Radar Navigation Console to the Top Hangar (CanvasLayer 100), aligned with Left Fleet Dock & Right Engineering Bay!
			BuildPinnedTopHangarRadarNav();
			GetViewport().Connect("size_changed", Callable.From(LayoutPinnedTopHangarRadarNav));

			RefreshMoonOccupantsFromCloud();

			// Engage or seamlessly continue 'moongalaxy.wav' loop (zero pause or restart when switching Moon <-> Galaxy)
			MusicManager.PlayTrack(this, MusicManager.TRACK_MOONGALAXY);

			GD.Print($"[MOON VIEW] Initialized Circular 20-Slot Lunar Sphere for Moon #{ViewingMoon}.");
		}

		public override void _ExitTree()
		{
			if (Instance == this) Instance = null;
			base._ExitTree();
		}

		private void BuildLunarTerrainAndBorder()
		{
			float border = LunarTerrainBorderHalfSize;

			ColorRect spaceBackdrop = new ColorRect
			{
				Name = "SpaceBackdrop",
				Color = new Color(0.01f, 0.02f, 0.05f, 1.0f),
				Position = new Vector2(-border - 1500f, -border - 1500f),
				Size = new Vector2((border + 1500f) * 2f, (border + 1500f) * 2f),
				ZIndex = -20,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			AddChild(spaceBackdrop);

			Node2D terrainLayer = new Node2D { Name = "LunarTerrainLayer", ZIndex = -10 };
			AddChild(terrainLayer);

			// Orbital Radar Grid Rings around the Moon
			int pts = 96;
			foreach (float r in new float[] { 680f, OrbitalEdgeWaypointRadius, 980f })
			{
				Line2D orbitRing = new Line2D
				{
					Width = (Math.Abs(r - OrbitalEdgeWaypointRadius) < 5f) ? 2.2f : 1.2f,
					DefaultColor = (Math.Abs(r - OrbitalEdgeWaypointRadius) < 5f)
						? new Color(0.0f, 0.94f, 1.0f, 0.26f)
						: new Color(0.0f, 0.75f, 1.0f, 0.10f),
					Closed = true,
					Antialiased = true
				};
				for (int i = 0; i < pts; i++)
				{
					float a = (i / (float)pts) * Mathf.Tau;
					orbitRing.AddPoint(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
				}
				terrainLayer.AddChild(orbitRing);
			}

			// Hard Lunar Sector Containment Border Box so player cannot scroll out of the UI
			Line2D outerBorder = new Line2D
			{
				Width = 8.0f,
				DefaultColor = new Color(0.0f, 0.94f, 1.0f, 0.35f),
				Closed = true,
				Antialiased = true
			};
			outerBorder.AddPoint(new Vector2(-border, -border));
			outerBorder.AddPoint(new Vector2(border, -border));
			outerBorder.AddPoint(new Vector2(border, border));
			outerBorder.AddPoint(new Vector2(-border, border));
			terrainLayer.AddChild(outerBorder);

			Label warpHorizonLbl = new Label
			{
				Text = "🛰️ HIGH LUNAR ORBITAL JUMP RING (INTER-MOON FLEET TRANSIT CORRIDOR)",
				HorizontalAlignment = HorizontalAlignment.Center,
				Position = new Vector2(-340f, -OrbitalEdgeWaypointRadius - 28f),
				Size = new Vector2(680f, 24f),
				Modulate = new Color(0.0f, 0.94f, 1.0f, 0.65f)
			};
			warpHorizonLbl.AddThemeFontSizeOverride("font_size", 12);
			terrainLayer.AddChild(warpHorizonLbl);
		}

		private void ClampCameraToLunarBorder()
		{
			float border = LunarTerrainBorderHalfSize;
			Vector2 vpSize = GetViewportRect().Size;
			if (vpSize.X <= 1f || vpSize.Y <= 1f) vpSize = new Vector2(1280f, 720f);

			float halfWorldW = (vpSize.X * 0.5f) / Math.Max(0.25f, _targetZoom);
			float halfWorldH = (vpSize.Y * 0.5f) / Math.Max(0.25f, _targetZoom);

			float maxX = Math.Max(0f, border - halfWorldW + 180f);
			float maxY = Math.Max(0f, border - halfWorldH + 180f);

			_targetCameraPos.X = Mathf.Clamp(_targetCameraPos.X, -maxX, maxX);
			_targetCameraPos.Y = Mathf.Clamp(_targetCameraPos.Y, -maxY, maxY);
		}

		private static Vector2[] BuildCirclePolygon(float radius, int segments, Vector2 centerOffset)
		{
			Vector2[] pts = new Vector2[segments];
			for (int i = 0; i < segments; i++)
			{
				float a = (i / (float)segments) * Mathf.Tau;
				pts[i] = centerOffset + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
			}
			return pts;
		}

		private void BuildLunarSphere()
		{
			int circlePoints = 96;
			float radius = LunarSphereRadius; // 520px radius = 1,040px diameter!

			// 1. Outer Lunar Atmosphere Glow
			Polygon2D atmoHalo = new Polygon2D
			{
				Polygon = BuildCirclePolygon(radius + 26f, circlePoints, Vector2.Zero),
				Color = new Color(0.0f, 0.94f, 1.0f, 0.08f)
			};
			_lunarDiscContainer.AddChild(atmoHalo);

			// 2. Central 1,040px Lunar Sphere Surface Disc
			Vector2[] discPolygon = BuildCirclePolygon(radius, circlePoints, Vector2.Zero);
			Polygon2D spherePoly = new Polygon2D
			{
				Polygon = discPolygon,
				Color = new Color(0.04f, 0.08f, 0.14f, 0.96f)
			};
			_lunarDiscContainer.AddChild(spherePoly);

			// Decorative Lunar Surface Mare & Craters
			var mareDefs = new (Vector2 offset, float r)[]
			{
				(new Vector2(-180f, -110f), 115f),
				(new Vector2(165f, -75f), 95f),
				(new Vector2(-75f, 175f), 125f),
				(new Vector2(195f, 165f), 85f),
				(new Vector2(0f, -10f), 78f)
			};
			foreach (var m in mareDefs)
			{
				Polygon2D mare = new Polygon2D
				{
					Polygon = BuildCirclePolygon(m.r, 28, m.offset),
					Color = new Color(0.02f, 0.05f, 0.09f, 0.55f)
				};
				_lunarDiscContainer.AddChild(mare);
			}

			// Glowing Cyan Lunar Perimeter Ring
			Line2D ringOutline = new Line2D
			{
				Width = 3.5f,
				DefaultColor = new Color(0f, 0.94f, 1f, 0.55f),
				Closed = true,
				Antialiased = true
			};
			for (int i = 0; i < circlePoints; i++) ringOutline.AddPoint(discPolygon[i]);
			_lunarDiscContainer.AddChild(ringOutline);

			// 4. Build 20 Circular Lunar Crater Base Slot Nodes (84x84 circular pads + commander subtitle)
			_slotButtons.Clear();
			_slotStyles.Clear();
			_slotCraterDiscs.Clear();
			_slotSubLabels.Clear();

			Vector2[] slotDiscPoly = BuildCirclePolygon(38f, 24, new Vector2(42f, 42f));

			foreach (var coord in GameMath.LunarSlotCoords)
			{
				int slotNum = coord.Slot;
				bool isMyBase = (ViewingMoon == SupabaseService.CachedMoonId && slotNum == SupabaseService.CachedBaseSlot);
				Color slotColor = isMyBase ? new Color("#00F0FF") : new Color("#94A3B8");

				Control slotHolder = new Control
				{
					Position = new Vector2(coord.X - 42f, coord.Y - 42f),
					Size = new Vector2(84f, 84f),
					CustomMinimumSize = new Vector2(84f, 84f),
					ZIndex = 5,
					MouseFilter = Control.MouseFilterEnum.Ignore
				};

				Polygon2D craterDisc = new Polygon2D
				{
					Polygon = slotDiscPoly,
					Color = isMyBase ? new Color(0.03f, 0.28f, 0.42f, 0.96f) : new Color(0.05f, 0.09f, 0.15f, 0.94f)
				};
				slotHolder.AddChild(craterDisc);
				_slotCraterDiscs[slotNum] = craterDisc;

				// Circular Button (84x84 with 42px Corner Radius = Perfect Circle!)
				Button slotBtn = new Button
				{
					Text = $"B-{slotNum:D2}\n{(isMyBase ? "MY BASE" : "VACANT")}",
					Position = Vector2.Zero,
					Size = new Vector2(84f, 84f),
					CustomMinimumSize = new Vector2(84f, 84f),
					MouseFilter = Control.MouseFilterEnum.Pass
				};
				slotBtn.AddThemeFontSizeOverride("font_size", 11);
				slotBtn.AddThemeColorOverride("font_outline_color", Colors.Black);
				slotBtn.AddThemeConstantOverride("outline_size", 3);
				slotBtn.Modulate = slotColor;

				StyleBoxFlat slotStyle = new StyleBoxFlat
				{
					BgColor = new Color(0.02f, 0.05f, 0.10f, 0.35f),
					BorderColor = slotColor,
					BorderWidthLeft = isMyBase ? 3 : 2,
					BorderWidthRight = isMyBase ? 3 : 2,
					BorderWidthTop = isMyBase ? 3 : 2,
					BorderWidthBottom = isMyBase ? 3 : 2,
					CornerRadiusTopLeft = 42,
					CornerRadiusTopRight = 42,
					CornerRadiusBottomLeft = 42,
					CornerRadiusBottomRight = 42
				};
				slotBtn.AddThemeStyleboxOverride("normal", slotStyle);
				slotBtn.AddThemeStyleboxOverride("hover", slotStyle);
				slotBtn.AddThemeStyleboxOverride("pressed", slotStyle);
				slotHolder.AddChild(slotBtn);

				Label subLbl = new Label
				{
					Text = isMyBase ? "★ MY CITADEL" : "OPEN CRATER",
					HorizontalAlignment = HorizontalAlignment.Center,
					Position = new Vector2(-28f, 86f),
					Size = new Vector2(140f, 20f),
					Modulate = slotColor
				};
				subLbl.AddThemeFontSizeOverride("font_size", 10);
				subLbl.AddThemeColorOverride("font_outline_color", Colors.Black);
				subLbl.AddThemeConstantOverride("outline_size", 2);
				slotHolder.AddChild(subLbl);

				_slotButtons[slotNum] = slotBtn;
				_slotStyles[slotNum] = slotStyle;
				_slotSubLabels[slotNum] = subLbl;

				int capturedSlot = slotNum;
				slotBtn.Pressed += () => OnSlotPressed(capturedSlot);

				_lunarDiscContainer.AddChild(slotHolder);
			}
		}

		private void BuildPinnedTopHangarRadarNav()
		{
			_pinnedRadarNavConsole = new PanelContainer
			{
				Name = "PinnedRadarNavConsole",
				MouseFilter = Control.MouseFilterEnum.Stop
			};

			StyleBoxFlat navStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.05f, 0.09f, 0.95f),
				BorderColor = new Color("#00F0FF"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8,
				ContentMarginLeft = 10,
				ContentMarginRight = 10,
				ContentMarginTop = 6,
				ContentMarginBottom = 6,
				ShadowColor = new Color(0f, 0f, 0f, 0.65f),
				ShadowSize = 8
			};
			_pinnedRadarNavConsole.AddThemeStyleboxOverride("panel", navStyle);
			_hudLayer.AddChild(_pinnedRadarNavConsole);

			VBoxContainer vBox = new VBoxContainer
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				MouseFilter = Control.MouseFilterEnum.Pass
			};
			vBox.AddThemeConstantOverride("separation", 4);
			_pinnedRadarNavConsole.AddChild(vBox);

			HBoxContainer topRow = new HBoxContainer
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				Alignment = BoxContainer.AlignmentMode.Center,
				MouseFilter = Control.MouseFilterEnum.Pass
			};
			topRow.AddThemeConstantOverride("separation", 8);

			StyleBoxFlat btnStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.06f, 0.12f, 0.22f, 0.96f),
				BorderColor = new Color("#00F0FF"),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6,
				ContentMarginLeft = 8,
				ContentMarginRight = 8,
				ContentMarginTop = 4,
				ContentMarginBottom = 4
			};

			Button prevMoonBtn = new Button
			{
				Text = "◀ PREV MOON",
				CustomMinimumSize = new Vector2(114, 32),
				MouseFilter = Control.MouseFilterEnum.Stop
			};
			prevMoonBtn.AddThemeFontSizeOverride("font_size", 11);
			prevMoonBtn.Modulate = new Color("#00F0FF");
			prevMoonBtn.AddThemeStyleboxOverride("normal", btnStyle);
			prevMoonBtn.AddThemeStyleboxOverride("hover", btnStyle);
			prevMoonBtn.AddThemeStyleboxOverride("pressed", btnStyle);
			prevMoonBtn.Pressed += () => StepToAdjacentVisibleMoon(-1);
			topRow.AddChild(prevMoonBtn);

			_moonTitleLabel = new Label
			{
				Text = $"🪐 MOON M-{ViewingMoon}",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
				Modulate = new Color("#00F0FF")
			};
			_moonTitleLabel.AddThemeFontSizeOverride("font_size", 14);
			_moonTitleLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
			_moonTitleLabel.AddThemeConstantOverride("outline_size", 2);
			topRow.AddChild(_moonTitleLabel);

			Button nextMoonBtn = new Button
			{
				Text = "NEXT MOON ▶",
				CustomMinimumSize = new Vector2(114, 32),
				MouseFilter = Control.MouseFilterEnum.Stop
			};
			nextMoonBtn.AddThemeFontSizeOverride("font_size", 11);
			nextMoonBtn.Modulate = new Color("#00F0FF");
			nextMoonBtn.AddThemeStyleboxOverride("normal", btnStyle);
			nextMoonBtn.AddThemeStyleboxOverride("hover", btnStyle);
			nextMoonBtn.AddThemeStyleboxOverride("pressed", btnStyle);
			nextMoonBtn.Pressed += () => StepToAdjacentVisibleMoon(1);
			topRow.AddChild(nextMoonBtn);

			vBox.AddChild(topRow);

			_radarWarningLabel = new Label
			{
				Text = "",
				HorizontalAlignment = HorizontalAlignment.Center,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
				Modulate = new Color("#FBBF24")
			};
			_radarWarningLabel.AddThemeFontSizeOverride("font_size", 11);
			_radarWarningLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
			_radarWarningLabel.AddThemeConstantOverride("outline_size", 2);
			vBox.AddChild(_radarWarningLabel);

			LayoutPinnedTopHangarRadarNav();
		}

		public void SetRadarNavConsoleVisible(bool visible)
		{
			if (_pinnedRadarNavConsole != null && IsInstanceValid(_pinnedRadarNavConsole))
			{
				_pinnedRadarNavConsole.Visible = visible;
			}
		}

		private void LayoutPinnedTopHangarRadarNav()
		{
			if (_pinnedRadarNavConsole == null || !IsInstanceValid(_pinnedRadarNavConsole)) return;
			Vector2 vpSize = GetViewportRect().Size;
			if (vpSize.X <= 1f || vpSize.Y <= 1f) vpSize = new Vector2(1280f, 720f);

			// Left Fleet & Radar Dock occupies X: 12..348 (Y: 76..178)
			// Right Engineering Bay occupies X: vpSize.X - 328..vpSize.X - 12 (Y: 76..178)
			float leftSafeX = 356f;
			float rightSafeX = vpSize.X - 336f;
			float corridorW = rightSafeX - leftSafeX;

			if (corridorW >= 380f)
			{
				float navW = Mathf.Min(560f, corridorW);
				float navX = leftSafeX + (corridorW - navW) * 0.5f;
				_pinnedRadarNavConsole.Position = new Vector2(navX, 76f);
				_pinnedRadarNavConsole.CustomMinimumSize = new Vector2(navW, 66f);
				_pinnedRadarNavConsole.Size = new Vector2(navW, 66f);
			}
			else
			{
				// Narrow screen fallback: pin cleanly right below the top docks so it never overlaps!
				float navW = Mathf.Min(520f, Math.Max(340f, vpSize.X - 24f));
				float navX = (vpSize.X - navW) * 0.5f;
				_pinnedRadarNavConsole.Position = new Vector2(navX, 182f);
				_pinnedRadarNavConsole.CustomMinimumSize = new Vector2(navW, 64f);
				_pinnedRadarNavConsole.Size = new Vector2(navW, 64f);
			}
		}

		private void StepToAdjacentVisibleMoon(int stepDir)
		{
			int homeMoon = SupabaseService.CachedMoonId > 0 ? SupabaseService.CachedMoonId : 100;
			int radarLvl = _hudInstance != null && _hudInstance.BuildingLevels != null
				? Math.Clamp(_hudInstance.BuildingLevels.GetValueOrDefault("hub_rng", 1), 1, 10)
				: Math.Clamp(SupabaseService.CachedRadarLevel, 1, 10);

			int candidate = ViewingMoon;
			for (int tries = 0; tries < GameMath.TotalMoons; tries++)
			{
				candidate += stepDir;
				if (candidate < 1) candidate = GameMath.TotalMoons;
				if (candidate > GameMath.TotalMoons) candidate = 1;

				if (GameMath.IsMoonVisibleInRadarCircle(homeMoon, candidate, radarLvl))
				{
					ChangeViewingMoon(candidate);
					return;
				}
			}
		}

		private async void RefreshMoonOccupantsFromCloud(bool forceRefresh = false)
		{
			int myRequestId = ++_moonRefreshRequestId;
			int requestedMoon = ViewingMoon;

			// Immediately seed slot occupants from the latest cached colonies snapshot + local player state so bases never disappear while an HTTP refresh is in flight!
			var cachedSnapshot = SupabaseService.GetCachedColoniesSnapshot();
			if (cachedSnapshot != null && cachedSnapshot.Count > 0)
			{
				foreach (var entry in cachedSnapshot)
				{
					if (entry != null && entry.MoonId == ViewingMoon && entry.BaseSlot >= 1 && entry.BaseSlot <= 20)
					{
						_slotOccupants[entry.BaseSlot] = entry;
					}
				}
			}

			EnsureLocalPlayerSlotSeededIfOnHomeMoon();
			RefreshMoonSlotVisuals();

			List<SupabaseService.GalacticCommanderEntry> allColonies = null;
			if (SupabaseService.Instance != null)
			{
				allColonies = await SupabaseService.Instance.FetchAllColoniesAsync(forceRefresh);
			}

			// Ignore out-of-order HTTP responses if the player already stepped to another Moon!
			if (myRequestId != _moonRefreshRequestId || ViewingMoon != requestedMoon || !IsInstanceValid(this))
				return;

			// Only overwrite _slotOccupants if FetchAllColoniesAsync returned a valid non-empty list of colonies
			if (allColonies != null && allColonies.Count > 0)
			{
				_slotOccupants.Clear();
				foreach (var entry in allColonies)
				{
					if (entry != null && entry.MoonId == ViewingMoon && entry.BaseSlot >= 1 && entry.BaseSlot <= 20)
					{
						_slotOccupants[entry.BaseSlot] = entry;
					}
				}
			}

			EnsureLocalPlayerSlotSeededIfOnHomeMoon();
			RefreshMoonSlotVisuals();
		}

		private void EnsureLocalPlayerSlotSeededIfOnHomeMoon()
		{
			int myMoon = SupabaseService.CachedMoonId > 0 ? SupabaseService.CachedMoonId : 100;
			int mySlot = SupabaseService.CachedBaseSlot >= 1 && SupabaseService.CachedBaseSlot <= 20 ? SupabaseService.CachedBaseSlot : 1;
			if (ViewingMoon != myMoon) return;

			if (_slotOccupants.TryGetValue(mySlot, out var existing) && existing != null)
			{
				if (existing.IsMe || (SupabaseService.Instance != null && !string.IsNullOrEmpty(SupabaseService.Instance.UserId) && string.Equals(existing.UserId, SupabaseService.Instance.UserId, StringComparison.OrdinalIgnoreCase)))
				{
					existing.CommanderName = SupabaseService.CachedCommanderName.ToUpper();
					existing.BaseName = SupabaseService.CachedBaseName;
					existing.AllianceName = SupabaseService.CachedAllianceName;
					existing.IsMe = true;
				}
				return;
			}

			_slotOccupants[mySlot] = new SupabaseService.GalacticCommanderEntry
			{
				UserId = SupabaseService.Instance?.UserId ?? "LOCAL_COMMANDER",
				CommanderName = SupabaseService.CachedCommanderName.ToUpper(),
				BaseName = SupabaseService.CachedBaseName,
				AllianceName = SupabaseService.CachedAllianceName,
				MoonId = myMoon,
				BaseSlot = mySlot,
				IndustrialCoreLevel = BaseHUD.Instance != null ? BaseHUD.Instance.BuildingLevels.GetValueOrDefault("hub_cmd", 1) : SupabaseService.CachedBuildingLevels.GetValueOrDefault("hub_cmd", 1),
				Population = BaseHUD.Instance != null ? SupabaseService.CalculateColonyPopulation(BaseHUD.Instance.BuildingLevels) : 700,
				IsMe = true
			};
		}

		/// <summary>
		/// Synchronizes all 20 Lunar Slot buttons with both Cloud Colony Occupants AND Live Stationed / Moving Fleets!
		/// Guarantees that empty moon slots with stationed fleets or outbound/inbound fleet movements register and display their status accurately.
		/// </summary>
		public void RefreshMoonSlotVisuals()
		{
			if (!IsInstanceValid(this)) return;

			var cachedSnapshot = SupabaseService.GetCachedColoniesSnapshot();
			if (cachedSnapshot != null && cachedSnapshot.Count > 0)
			{
				foreach (var entry in cachedSnapshot)
				{
					if (entry != null && entry.MoonId == ViewingMoon && entry.BaseSlot >= 1 && entry.BaseSlot <= 20)
					{
						if (!_slotOccupants.ContainsKey(entry.BaseSlot))
						{
							_slotOccupants[entry.BaseSlot] = entry;
						}
					}
				}
			}

			EnsureLocalPlayerSlotSeededIfOnHomeMoon();
			int occupiedCount = _slotOccupants.Count;
			int homeMoon = SupabaseService.CachedMoonId > 0 ? SupabaseService.CachedMoonId : 100;
			int radarLvl = _hudInstance != null && _hudInstance.BuildingLevels != null
				? Math.Clamp(_hudInstance.BuildingLevels.GetValueOrDefault("hub_rng", 1), 1, 10)
				: Math.Clamp(SupabaseService.CachedRadarLevel, 1, 10);

			// Scan player's live fleets on this Moon (Stationed in orbit, Launching FROM a slot on this Moon, or Heading TO a slot on this Moon)
			var stationedPerSlot = new Dictionary<int, List<GameMath.FleetManifest>>();
			var outboundFromSlot = new Dictionary<int, List<GameMath.FleetManifest>>();
			var inboundToSlot = new Dictionary<int, List<GameMath.FleetManifest>>();
			int totalStationedOnMoon = 0;

			var myFleets = _hudInstance?.SavedFleets ?? SupabaseService.CachedSavedFleets;
			if (myFleets != null)
			{
				foreach (var f in myFleets)
				{
					if (f == null || f.Status == "WATCHLIST_DATA") continue;

					if (f.Status == "STATIONED")
					{
						if (GameMath.TryParseSectorCoordinate(f.LocationCoord, out int sMoon, out int sSlot) && sMoon == ViewingMoon && sSlot >= 1 && sSlot <= 20)
						{
							if (!stationedPerSlot.ContainsKey(sSlot)) stationedPerSlot[sSlot] = new List<GameMath.FleetManifest>();
							stationedPerSlot[sSlot].Add(f);
							totalStationedOnMoon++;
						}
					}
					else if (f.Status == "IN_TRANSIT")
					{
						if (GameMath.TryParseSectorCoordinate(f.LocationCoord, out int oMoon, out int oSlot) && oMoon == ViewingMoon && oSlot >= 1 && oSlot <= 20)
						{
							if (!outboundFromSlot.ContainsKey(oSlot)) outboundFromSlot[oSlot] = new List<GameMath.FleetManifest>();
							outboundFromSlot[oSlot].Add(f);
						}
						if (GameMath.TryParseSectorCoordinate(f.TargetCoord, out int dMoon, out int dSlot) && dMoon == ViewingMoon && dSlot >= 1 && dSlot <= 20)
						{
							if (!inboundToSlot.ContainsKey(dSlot)) inboundToSlot[dSlot] = new List<GameMath.FleetManifest>();
							inboundToSlot[dSlot].Add(f);
						}
					}
				}
			}

			if (_moonTitleLabel != null)
			{
				string fleetSuffix = totalStationedOnMoon > 0 ? $"   ·   🛰️ {totalStationedOnMoon} FLEET(S) STATIONED" : "";
				_moonTitleLabel.Text = $"🪐  MOON M-{ViewingMoon}   ·   {occupiedCount}/20 SECTORS CLAIMED{fleetSuffix}";
			}
			if (_radarWarningLabel != null)
			{
				_radarWarningLabel.Text = $"📡 Deep Space Radar Lvl {radarLvl}/10 ({radarLvl * 10}% Galaxy Circle Active around Home Moon M-{homeMoon})";
			}

			for (int slotNum = 1; slotNum <= 20; slotNum++)
			{
				if (!_slotButtons.TryGetValue(slotNum, out Button btn) ||
					!_slotStyles.TryGetValue(slotNum, out StyleBoxFlat style) ||
					!_slotCraterDiscs.TryGetValue(slotNum, out Polygon2D disc) ||
					!_slotSubLabels.TryGetValue(slotNum, out Label subLbl))
					continue;

				stationedPerSlot.TryGetValue(slotNum, out var slotStationed);
				outboundFromSlot.TryGetValue(slotNum, out var slotOutbound);
				inboundToSlot.TryGetValue(slotNum, out var slotInbound);

				int statCount = slotStationed != null ? slotStationed.Count : 0;
				int outCount = slotOutbound != null ? slotOutbound.Count : 0;
				int inCount = slotInbound != null ? slotInbound.Count : 0;

				if (_slotOccupants.TryGetValue(slotNum, out var occ))
				{
					bool isMe = occ.IsMe;
					Color col = isMe ? new Color("#00F0FF") : new Color("#F59E0B");
					string shortCmd = occ.CommanderName.Length > 12 ? occ.CommanderName.Substring(0, 12) : occ.CommanderName;
					string rawBaseName = isMe ? SupabaseService.CachedBaseName : (occ.BaseName ?? "");
					bool hasCustomBaseName = !string.IsNullOrWhiteSpace(rawBaseName) &&
											 !string.Equals(rawBaseName.Trim(), "Alpha Outpost", StringComparison.OrdinalIgnoreCase) &&
											 !rawBaseName.Trim().EndsWith(" Outpost", StringComparison.OrdinalIgnoreCase);
					string shortBase = rawBaseName.Trim().Length > 12 ? rawBaseName.Trim().Substring(0, 12) : rawBaseName.Trim();
					btn.Text = $"B-{slotNum:D2}\n{(isMe ? (hasCustomBaseName ? shortBase.ToUpper() : "MY BASE") : $"LVL {occ.IndustrialCoreLevel}")}";
					btn.Modulate = col;
					style.BorderColor = statCount > 0 ? new Color("#4ADE80") : col;
					style.BorderWidthLeft = style.BorderWidthRight = style.BorderWidthTop = style.BorderWidthBottom = 3;
					disc.Color = isMe ? new Color(0.03f, 0.32f, 0.48f, 0.96f) : new Color(0.28f, 0.14f, 0.04f, 0.96f);

					if (statCount > 0 && !isMe)
					{
						subLbl.Text = $"🛰️ {statCount} FLT @ {shortCmd}";
						subLbl.Modulate = new Color("#4ADE80");
					}
					else if (inCount > 0 && !isMe)
					{
						subLbl.Text = $"🚀 {inCount} INBOUND ➔ {shortCmd}";
						subLbl.Modulate = new Color("#EF4444");
					}
					else
					{
						subLbl.Text = isMe ? $"★ {shortCmd}" : (hasCustomBaseName ? $"⚔ {shortCmd} · {shortBase}" : $"⚔ {shortCmd}");
						subLbl.Modulate = col;
					}
				}
				else if (statCount > 0)
				{
					// Empty Moon Slot with 1 or more of the player's Fleets STATIONED in orbit!
					Color guardCol = new Color("#4ADE80");
					btn.Text = $"B-{slotNum:D2}\n🛰️ {statCount} FLEET{(statCount > 1 ? "S" : "")}";
					btn.Modulate = guardCol;
					style.BorderColor = guardCol;
					style.BorderWidthLeft = style.BorderWidthRight = style.BorderWidthTop = style.BorderWidthBottom = 3;
					disc.Color = new Color(0.03f, 0.26f, 0.18f, 0.96f);
					string firstFleetName = slotStationed[0].Name ?? "FLEET";
					if (firstFleetName.Length > 12) firstFleetName = firstFleetName.Substring(0, 12);
					subLbl.Text = statCount == 1 ? $"🛰️ {firstFleetName}" : $"🛰️ {statCount} FLEETS ORBITING";
					subLbl.Modulate = guardCol;
				}
				else if (outCount > 0)
				{
					// Empty Moon Slot from which a previously stationed fleet is currently launching/moving out!
					Color launchCol = new Color("#38BDF8");
					btn.Text = $"B-{slotNum:D2}\n🚀 MOVING";
					btn.Modulate = launchCol;
					style.BorderColor = launchCol;
					style.BorderWidthLeft = style.BorderWidthRight = style.BorderWidthTop = style.BorderWidthBottom = 3;
					disc.Color = new Color(0.04f, 0.18f, 0.32f, 0.95f);
					var fOut = slotOutbound[0];
					subLbl.Text = $"🚀 ➔ {fOut.TargetCoord}";
					subLbl.Modulate = launchCol;
				}
				else if (inCount > 0)
				{
					// Empty Moon Slot with a player fleet currently flying toward it!
					Color incomingCol = new Color("#00F0FF");
					btn.Text = $"B-{slotNum:D2}\n🛰️ EN ROUTE";
					btn.Modulate = incomingCol;
					style.BorderColor = incomingCol;
					style.BorderWidthLeft = style.BorderWidthRight = style.BorderWidthTop = style.BorderWidthBottom = 2;
					disc.Color = new Color(0.04f, 0.16f, 0.28f, 0.94f);
					var fIn = slotInbound[0];
					subLbl.Text = $"🛰️ ETA {GameMath.FormatTime((int)Math.Ceiling(fIn.EtaSecondsLeft))}";
					subLbl.Modulate = incomingCol;
				}
				else
				{
					Color vacantCol = new Color("#94A3B8");
					btn.Text = $"B-{slotNum:D2}\nVACANT";
					btn.Modulate = vacantCol;
					style.BorderColor = new Color(0.35f, 0.48f, 0.62f, 0.70f);
					style.BorderWidthLeft = style.BorderWidthRight = style.BorderWidthTop = style.BorderWidthBottom = 2;
					disc.Color = new Color(0.05f, 0.09f, 0.15f, 0.92f);
					subLbl.Text = "OPEN SECTOR";
					subLbl.Modulate = new Color("#64748B");
				}
			}
		}

		/// <summary>
		/// Renders real-time color-coded Fleet Trajectory Lines & moving Ship Icons ABOVE the Lunar Sphere & Base Slots!
		/// - Zero per-frame node allocations for butter-smooth mobile performance.
		/// - Only renders fleets heading TO or FROM the player's own bases.
		/// </summary>
		public void DrawLiveMoonFleetVectorsOnTop(CanvasItem canvas)
		{
			Font font = ThemeDB.FallbackFont;
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			int myMoon = SupabaseService.CachedMoonId > 0 ? SupabaseService.CachedMoonId : 100;
			int mySlot = SupabaseService.CachedBaseSlot > 0 ? SupabaseService.CachedBaseSlot : 1;

			// 1. Outbound & Returning Player Fleets
			var myFleets = _hudInstance?.SavedFleets ?? SupabaseService.CachedSavedFleets;
			if (myFleets != null)
			{
				foreach (var f in myFleets)
				{
					if (f.Status != "IN_TRANSIT") continue;
					GameMath.TryParseSectorCoordinate(f.LocationCoord, out int origMoon, out int origSlot);
					GameMath.TryParseSectorCoordinate(f.TargetCoord, out int destMoon, out int destSlot);

					if (ViewingMoon != origMoon && ViewingMoon != destMoon) continue;

					float progress = GalaxyView.ComputeFlightProgress01(f.DepartureTimeUnixMs, f.ArrivalTimeUnixMs, f.TotalFlightDurationSec, f.EtaSecondsLeft, nowMs);
					Color lineColor = GalaxyView.GetFleetProtocolColor(f.Protocol, isHostileIncoming: false);
					string friendlyProto = f.Protocol == "RAID" ? "ATTACK" : (f.Protocol == "REINFORCE" ? "DEFEND" : f.Protocol);

					if (origMoon == destMoon && ViewingMoon == origMoon)
					{
						Vector2 startPos = GameMath.GetLunarSlotPosition(origSlot);
						Vector2 endPos = GameMath.GetLunarSlotPosition(destSlot);
						DrawMoonFleetTrajectory(
							canvas,
							startPos,
							endPos,
							progress,
							lineColor,
							$"🚀 {f.Name} ({friendlyProto} ➔ B-{destSlot:D2}) · {GameMath.FormatTime((int)Math.Ceiling(f.EtaSecondsLeft))}",
							font
						);
					}
					else if (ViewingMoon == origMoon)
					{
						Vector2 startPos = GameMath.GetLunarSlotPosition(origSlot);
						Vector2 galaxyDir = GameMath.GetGalaxyDirectionBetweenMoons(origMoon, destMoon);
						Vector2 orbitalExitPos = galaxyDir * OrbitalEdgeWaypointRadius;

						DrawMoonFleetTrajectory(
							canvas,
							startPos,
							orbitalExitPos,
							progress,
							lineColor,
							$"🚀 {f.Name} ({friendlyProto}) ➔ M-{destMoon}:B-{destSlot:D2} · {GameMath.FormatTime((int)Math.Ceiling(f.EtaSecondsLeft))}",
							font
						);
					}
					else if (ViewingMoon == destMoon)
					{
						Vector2 galaxyApproachDir = GameMath.GetGalaxyDirectionBetweenMoons(destMoon, origMoon);
						Vector2 orbitalEntryPos = galaxyApproachDir * OrbitalEdgeWaypointRadius;
						Vector2 endPos = GameMath.GetLunarSlotPosition(destSlot);

						DrawMoonFleetTrajectory(
							canvas,
							orbitalEntryPos,
							endPos,
							progress,
							lineColor,
							$"🚀 {f.Name} ({friendlyProto} from M-{origMoon}) ➔ B-{destSlot:D2} · {GameMath.FormatTime((int)Math.Ceiling(f.EtaSecondsLeft))}",
							font
						);
					}
				}
			}

			// 2. Incoming Fleets Heading TO the Player's Base
			if (SupabaseService.CachedIncomingSignals != null)
			{
				foreach (var sig in SupabaseService.CachedIncomingSignals)
				{
					GameMath.TryParseSectorCoordinate(sig.OriginCoord, out int origMoon, out int origSlot);
					if (ViewingMoon != origMoon && ViewingMoon != myMoon) continue;

					float progress = GalaxyView.ComputeFlightProgress01(sig.DepartureTimeUnixMs, sig.ArrivalTimeUnixMs, sig.TotalDurationSec, sig.EtaSecondsLeft, nowMs);
					bool isHostile = (sig.Protocol ?? "RAID").ToUpper() == "RAID";
					Color lineColor = GalaxyView.GetFleetProtocolColor(sig.Protocol, isHostileIncoming: isHostile);
					string friendlyProto = isHostile ? "ATTACK" : (sig.Protocol == "REINFORCE" ? "DEFEND" : "MOVE");

					if (origMoon == myMoon && ViewingMoon == myMoon)
					{
						Vector2 startPos = GameMath.GetLunarSlotPosition(origSlot);
						Vector2 endPos = GameMath.GetLunarSlotPosition(mySlot);
						DrawMoonFleetTrajectory(
							canvas,
							startPos,
							endPos,
							progress,
							lineColor,
							$"⚠️ INCOMING {friendlyProto} ({sig.SenderName}) ➔ B-{mySlot:D2} · {GameMath.FormatTime((int)Math.Ceiling(sig.EtaSecondsLeft))}",
							font
						);
					}
					else if (ViewingMoon == origMoon)
					{
						Vector2 startPos = GameMath.GetLunarSlotPosition(origSlot);
						Vector2 galaxyDir = GameMath.GetGalaxyDirectionBetweenMoons(origMoon, myMoon);
						Vector2 orbitalExitPos = galaxyDir * OrbitalEdgeWaypointRadius;
						DrawMoonFleetTrajectory(
							canvas,
							startPos,
							orbitalExitPos,
							progress,
							lineColor,
							$"⚠️ {sig.SenderName} ({friendlyProto}) ➔ M-{myMoon}:B-{mySlot:D2} · {GameMath.FormatTime((int)Math.Ceiling(sig.EtaSecondsLeft))}",
							font
						);
					}
					else if (ViewingMoon == myMoon)
					{
						Vector2 galaxyApproachDir = GameMath.GetGalaxyDirectionBetweenMoons(myMoon, origMoon);
						Vector2 orbitalEntryPos = galaxyApproachDir * OrbitalEdgeWaypointRadius;
						Vector2 endPos = GameMath.GetLunarSlotPosition(mySlot);
						DrawMoonFleetTrajectory(
							canvas,
							orbitalEntryPos,
							endPos,
							progress,
							lineColor,
							$"⚠️ INCOMING {friendlyProto} ({sig.SenderName} from M-{origMoon}) · {GameMath.FormatTime((int)Math.Ceiling(sig.EtaSecondsLeft))}",
							font
						);
					}
				}
			}
		}

		private void DrawMoonFleetTrajectory(CanvasItem canvas, Vector2 startPos, Vector2 endPos, float progress01, Color color, string labelText, Font font)
		{
			Vector2 shipPos = startPos.Lerp(endPos, progress01);

			// 1. Dark contrast backing & bright outer glow so the line stands out clearly over the Lunar Disc!
			canvas.DrawLine(startPos, endPos, new Color(0f, 0f, 0f, 0.72f), 8.0f, true);
			canvas.DrawLine(startPos, endPos, new Color(color.R, color.G, color.B, 0.28f), 6.0f, true);

			// 2. Crisp vibrant traveled path + glowing remaining path ahead
			canvas.DrawLine(startPos, shipPos, new Color(color.R, color.G, color.B, 1.0f), 4.2f, true);
			canvas.DrawLine(shipPos, endPos, new Color(color.R, color.G, color.B, 0.65f), 3.0f, true);

			// 3. Origin & Target endpoint beacon rings
			canvas.DrawCircle(startPos, 6.5f, new Color(color.R, color.G, color.B, 0.90f));
			canvas.DrawArc(endPos, 11.0f, 0f, Mathf.Tau, 24, new Color(color.R, color.G, color.B, 0.95f), 2.5f, true);

			// 4. Moving Warship Chevron Marker with dark outline
			Vector2 dir = (endPos - startPos).Normalized();
			Vector2 perp = new Vector2(-dir.Y, dir.X);
			Vector2[] chevronBg = new Vector2[]
			{
				shipPos + dir * 20f,
				shipPos - dir * 14f + perp * 13f,
				shipPos - dir * 6f,
				shipPos - dir * 14f - perp * 13f
			};
			canvas.DrawColoredPolygon(chevronBg, new Color(0f, 0f, 0f, 0.85f));

			Vector2[] chevron = new Vector2[]
			{
				shipPos + dir * 17f,
				shipPos - dir * 11f + perp * 10.5f,
				shipPos - dir * 5f,
				shipPos - dir * 11f - perp * 10.5f
			};
			canvas.DrawColoredPolygon(chevron, color);

			float pulseRadius = 17f + MathF.Sin(_pulseAnimTime * 5f) * 3.5f;
			canvas.DrawArc(shipPos, pulseRadius, 0f, Mathf.Tau, 24, new Color(color.R, color.G, color.B, 0.92f), 2.4f, true);

			if (font != null)
			{
				Vector2 textPos = shipPos + new Vector2(20f, -12f);
				float pillW = Math.Max(160f, labelText.Length * 7.2f + 18f);
				Rect2 pillRect = new Rect2(textPos.X - 6f, textPos.Y - 15f, pillW, 22f);
				canvas.DrawRect(pillRect, new Color(0.01f, 0.03f, 0.07f, 0.90f), true);
				canvas.DrawRect(pillRect, new Color(color.R, color.G, color.B, 0.80f), false, 1.5f);
				canvas.DrawString(font, textPos, labelText, HorizontalAlignment.Left, -1f, 12, Colors.White);
			}
		}

		public override void _Process(double delta)
		{
			float dt = (float)delta;
			_pulseAnimTime += dt;
			_slotVisualRefreshTimer += dt;
			_cloudOccupantsRefreshTimer += dt;
			if (_slotVisualRefreshTimer >= 1.0f)
			{
				_slotVisualRefreshTimer = 0f;
				RefreshMoonSlotVisuals();
			}
			if (_cloudOccupantsRefreshTimer >= 8.0f)
			{
				_cloudOccupantsRefreshTimer = 0f;
				RefreshMoonOccupantsFromCloud(forceRefresh: false);
			}

			// Smooth Keyboard Navigation (WASD / Arrows) — Disabled while any modal (Bio / Comms / Profile) is open!
			bool isModalOpen = _hudInstance != null && _hudInstance.IsAnyOverlayModalOpen();
			Vector2 panDir = Vector2.Zero;
			if (!isModalOpen)
			{
				if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) panDir.Y -= 1.0f;
				if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) panDir.Y += 1.0f;
				if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) panDir.X -= 1.0f;
				if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) panDir.X += 1.0f;
			}

			if (panDir != Vector2.Zero)
			{
				float speed = 900.0f / _targetZoom;
				_targetCameraPos += panDir.Normalized() * speed * dt;
				ClampCameraToLunarBorder();
			}

			if (_camera != null)
			{
				ClampCameraToLunarBorder();
				float lerpWeight = 1.0f - Mathf.Exp(-14.0f * dt);
				_camera.Position = _camera.Position.Lerp(_targetCameraPos, lerpWeight);
				_camera.Zoom = _camera.Zoom.Lerp(new Vector2(_targetZoom, _targetZoom), lerpWeight);
			}

			_fleetOverlayNode?.QueueRedraw();
		}

		public override void _UnhandledInput(InputEvent @event)
		{
			// CRITICAL FIX: When any overlay modal (Player Bio, Empty Slot, Comms Inbox, Global Chat, Profile) is open,
			// completely lock background Moon camera panning & zooming so scrolling inside the Bio never moves the Moon terrain!
			if (_hudInstance != null && _hudInstance.IsAnyOverlayModalOpen())
			{
				_isDragging = false;
				_dragAccumulatedDistance = 0.0f;
				_activeTouches.Clear();
				_wasMultiTouchGesture = false;
				return;
			}

			// Also ignore drags started on the pinned Top Hangar Radar Navigation console or Side Docks
			if (@event is InputEventMouseButton checkMb && checkMb.IsPressed())
			{
				if (_pinnedRadarNavConsole != null && IsInstanceValid(_pinnedRadarNavConsole) &&
					_pinnedRadarNavConsole.GetGlobalRect().HasPoint(checkMb.Position))
				{
					return;
				}
			}

			// 0. Laptop Trackpad Two-Finger Pan & Pinch-Magnify Gestures
			if (@event is InputEventMagnifyGesture mag)
			{
				float factor = Mathf.Clamp(mag.Factor, 0.80f, 1.25f);
				_targetZoom = Mathf.Clamp(_targetZoom * factor, MinMoonZoom, MaxMoonZoom);
				ClampCameraToLunarBorder();
				if (_isDragging)
				{
					_dragStartMousePos = GetViewport().GetMousePosition();
					_dragStartCameraPos = _targetCameraPos;
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			if (@event is InputEventPanGesture panGesture)
			{
				float effZoom = Math.Max(0.25f, _targetZoom);
				_targetCameraPos += panGesture.Delta * (28.0f / effZoom);
				ClampCameraToLunarBorder();
				if (_isDragging)
				{
					_dragStartMousePos = GetViewport().GetMousePosition();
					_dragStartCameraPos = _targetCameraPos;
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			// 1. Multi-Touch Tracking for Mobile Pinch-to-Zoom & Smooth 2->1 Finger Re-Anchoring
			if (@event is InputEventScreenTouch st)
			{
				if (st.Pressed)
				{
					_activeTouches[st.Index] = st.Position;
					if (_activeTouches.Count >= 2)
					{
						_wasMultiTouchGesture = true;
						_isDragging = false;
					}
					else if (_activeTouches.Count == 1)
					{
						_wasMultiTouchGesture = false;
					}
				}
				else
				{
					_activeTouches.Remove(st.Index);
					if (_activeTouches.Count == 1)
					{
						foreach (var kvp in _activeTouches)
						{
							_dragStartMousePos = kvp.Value;
							_dragStartCameraPos = _targetCameraPos;
							break;
						}
					}
				}
			}

			if (@event is InputEventScreenDrag sd && _activeTouches.Count >= 2)
			{
				if (_activeTouches.ContainsKey(sd.Index))
				{
					var keys = new List<int>(_activeTouches.Keys);
					Vector2 p0Before = _activeTouches[keys[0]];
					Vector2 p1Before = _activeTouches[keys[1]];
					float prevDist = p0Before.DistanceTo(p1Before);
					Vector2 prevMid = (p0Before + p1Before) * 0.5f;

					_activeTouches[sd.Index] = sd.Position;

					Vector2 p0After = _activeTouches[keys[0]];
					Vector2 p1After = _activeTouches[keys[1]];
					float newDist = p0After.DistanceTo(p1After);
					Vector2 newMid = (p0After + p1After) * 0.5f;

					if (prevDist > 12.0f && newDist > 12.0f)
					{
						float ratio = Mathf.Clamp(newDist / prevDist, 0.80f, 1.25f);
						_targetZoom = Mathf.Clamp(_targetZoom * ratio, MinMoonZoom, MaxMoonZoom);
					}

					float effZoom = Math.Max(0.25f, _targetZoom);
					_targetCameraPos -= (newMid - prevMid) / effZoom;
					ClampCameraToLunarBorder();
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			// 2. Zoom Wheel / Single-Finger & Mouse Pan + Direct World-Space Slot Tap Detection
			if (@event is InputEventMouseButton mb)
			{
				if (mb.IsPressed())
				{
					if (mb.ButtonIndex == MouseButton.WheelUp)
					{
						float step = mb.Factor > 0.01f ? Mathf.Lerp(1.0f, 1.12f, Mathf.Clamp(mb.Factor, 0.15f, 1.5f)) : 1.12f;
						_targetZoom = Mathf.Clamp(_targetZoom * step, MinMoonZoom, MaxMoonZoom);
						ClampCameraToLunarBorder();
						if (_isDragging)
						{
							_dragStartMousePos = mb.Position;
							_dragStartCameraPos = _targetCameraPos;
						}
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (mb.ButtonIndex == MouseButton.WheelDown)
					{
						float step = mb.Factor > 0.01f ? Mathf.Lerp(1.0f, 0.88f, Mathf.Clamp(mb.Factor, 0.15f, 1.5f)) : 0.88f;
						_targetZoom = Mathf.Clamp(_targetZoom * step, MinMoonZoom, MaxMoonZoom);
						ClampCameraToLunarBorder();
						if (_isDragging)
						{
							_dragStartMousePos = mb.Position;
							_dragStartCameraPos = _targetCameraPos;
						}
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						_dragAccumulatedDistance = 0.0f;
						if (_activeTouches.Count <= 1)
						{
							_wasMultiTouchGesture = false;
						}
						if (mb.Position.Y > 60 && mb.Position.Y < GetViewportRect().Size.Y - 62 && _activeTouches.Count <= 1)
						{
							_isDragging = true;
							_dragStartMousePos = mb.Position;
							_dragStartCameraPos = _targetCameraPos;
						}
					}
				}
				else
				{
					if (mb.ButtonIndex == MouseButton.Left)
					{
						bool wasCleanTap = !_wasMultiTouchGesture && _dragAccumulatedDistance <= 18.0f;
						_isDragging = false;
						_dragAccumulatedDistance = 0.0f;
						if (_activeTouches.Count == 0)
						{
							_wasMultiTouchGesture = false;
						}

						if (wasCleanTap && mb.Position.Y > 68 && mb.Position.Y < GetViewportRect().Size.Y - 72)
						{
							// Direct World-Space Circular Slot Hit-Test (immune to Control focus/hover state across scene switches)
							Vector2 worldClickPos = GetGlobalMousePosition();
							int bestSlot = -1;
							float bestDist = 48.0f; // 42px visual radius + 6px touch comfort pad
							foreach (var coord in GameMath.LunarSlotCoords)
							{
								float d = worldClickPos.DistanceTo(new Vector2(coord.X, coord.Y));
								if (d <= bestDist)
								{
									bestDist = d;
									bestSlot = coord.Slot;
								}
							}

							if (bestSlot >= 1)
							{
								OnSlotPressed(bestSlot);
								GetViewport().SetInputAsHandled();
								return;
							}
						}
					}
					else if (mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						_isDragging = false;
						_dragAccumulatedDistance = 0.0f;
					}
				}
			}

			if (@event is InputEventMouseMotion mm && _isDragging && _activeTouches.Count <= 1 && !_wasMultiTouchGesture)
			{
				_dragAccumulatedDistance += mm.Relative.Length();
				if (_dragAccumulatedDistance <= 2.5f)
				{
					_dragStartMousePos = mm.Position;
					return;
				}
				float effZoom = Math.Max(0.25f, _targetZoom);
				Vector2 stepDelta = (mm.Position - _dragStartMousePos) / effZoom;
				_dragStartMousePos = mm.Position;
				_targetCameraPos -= stepDelta;
				ClampCameraToLunarBorder();
				_dragStartCameraPos = _targetCameraPos;
				return;
			}
		}

		private void ChangeViewingMoon(int nextMoon)
		{
			if (ViewingMoon != nextMoon)
			{
				_slotOccupants.Clear();
			}
			ViewingMoon = nextMoon;
			SelectedMoonToView = nextMoon;
			RefreshMoonOccupantsFromCloud();
		}

		public void NotifyExternalStateUpdated(bool refreshCloudOccupants = false)
		{
			if (!IsInstanceValid(this)) return;
			EnsureLocalPlayerSlotSeededIfOnHomeMoon();
			RefreshMoonSlotVisuals();
			_fleetOverlayNode?.QueueRedraw();
			if (refreshCloudOccupants)
			{
				RefreshMoonOccupantsFromCloud(forceRefresh: false);
			}
		}

		private ulong _lastSlotPressTickMs = 0;

		private void OnSlotPressed(int slotNumber)
		{
			if (_wasMultiTouchGesture || _dragAccumulatedDistance > 14.0f) return;

			// Prevent duplicate firing if both GUI Button.Pressed and World-Space Tap fire on the same click
			ulong nowTick = Time.GetTicksMsec();
			if (nowTick - _lastSlotPressTickMs < 180) return;
			_lastSlotPressTickMs = nowTick;
			_isDragging = false;
			_dragAccumulatedDistance = 0.0f;

			// Ensure _slotOccupants is hydrated from the latest session snapshot before checking slotNumber
			if (!_slotOccupants.ContainsKey(slotNumber))
			{
				var cachedSnapshot = SupabaseService.GetCachedColoniesSnapshot();
				if (cachedSnapshot != null)
				{
					foreach (var entry in cachedSnapshot)
					{
						if (entry != null && entry.MoonId == ViewingMoon && entry.BaseSlot >= 1 && entry.BaseSlot <= 20)
						{
							_slotOccupants[entry.BaseSlot] = entry;
						}
					}
				}
				EnsureLocalPlayerSlotSeededIfOnHomeMoon();
			}

			var activeHud = (_hudInstance != null && IsInstanceValid(_hudInstance)) ? _hudInstance : BaseHUD.Instance;

			if (_slotOccupants.TryGetValue(slotNumber, out var occupant) && occupant != null)
			{
				GD.Print($"[MOON VIEW] Occupied Slot B-{slotNumber:D2} clicked ({occupant.CommanderName}). IsMe: {occupant.IsMe}");
				if (occupant.IsMe)
				{
					string target = ResourceLoader.Exists("res://Scripts/base_view.tscn") ? "res://Scripts/base_view.tscn" : "res://Scenes/base_view.tscn";
					GetTree().ChangeSceneToFile(target);
				}
				else
				{
					activeHud?.OpenPlayerBioModal(occupant);
				}
			}
			else
			{
				GD.Print($"[MOON VIEW] Empty Slot M-{ViewingMoon}:B-{slotNumber:D2} clicked — opening Empty Moon Slot Command Modal.");
				activeHud?.OpenEmptyMoonSlotModal(ViewingMoon, slotNumber);
			}
		}
	}
}
