using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: TIER 2 - 20-SLOT LUNAR SPHERE VIEWPORT (v3.0 Production)
	/// - Central 620px diameter Lunar Sphere disc.
	/// - 20 Base Slots arranged in exact orbital surface coordinates (B-01 through B-20).
	/// - Dynamically instantiates BaseHUD on CanvasLayer (Layer 100) so Top Resources & Bottom Nav stay anchored.
	/// - Full camera panning (touchpad, mouse drag, WASD) and mouse wheel zooming.
	/// </summary>
	public partial class MoonView : Node2D
	{
		public static MoonView Instance { get; private set; }

		public int ViewingMoon = 100;
		public string CommanderName = "COMMANDER ALPHA";
		public string AllianceName = "NONE";

		// Camera
		private Camera2D _camera;
		private Vector2 _targetCameraPos = Vector2.Zero;
		private float _targetZoom = 1.0f;
		private bool _isDragging = false;
		private Vector2 _dragStartMousePos;
		private Vector2 _dragStartCameraPos;

		// Layers & Containers
		private Node2D _lunarDiscContainer;
		private CanvasLayer _hudLayer;
		private BaseHUD _hudInstance;

		public struct LunarSlotCoord
		{
			public int Slot;
			public float X;
			public float Y;
		}

		// Exact 20 Base Slot Coordinates from App.js
		private static readonly LunarSlotCoord[] SlotCoords = new LunarSlotCoord[]
		{
			new LunarSlotCoord { Slot = 1,  X = -160, Y = -180 },
			new LunarSlotCoord { Slot = 2,  X = 0,    Y = -230 },
			new LunarSlotCoord { Slot = 3,  X = 160,  Y = -180 },
			new LunarSlotCoord { Slot = 4,  X = -240, Y = -100 },
			new LunarSlotCoord { Slot = 5,  X = -90,  Y = -120 },
			new LunarSlotCoord { Slot = 6,  X = 90,   Y = -120 },
			new LunarSlotCoord { Slot = 7,  X = 240,  Y = -100 },
			new LunarSlotCoord { Slot = 8,  X = 0,    Y = 0    }, // Center Citadel
			new LunarSlotCoord { Slot = 9,  X = -150, Y = -30  },
			new LunarSlotCoord { Slot = 10, X = 150,  Y = -30  },
			new LunarSlotCoord { Slot = 11, X = -250, Y = 50   },
			new LunarSlotCoord { Slot = 12, X = -90,  Y = 70   },
			new LunarSlotCoord { Slot = 13, X = 90,   Y = 70   },
			new LunarSlotCoord { Slot = 14, X = 250,  Y = 50   },
			new LunarSlotCoord { Slot = 15, X = -170, Y = 150  },
			new LunarSlotCoord { Slot = 16, X = 0,    Y = 120  },
			new LunarSlotCoord { Slot = 17, X = 170,  Y = 150  },
			new LunarSlotCoord { Slot = 18, X = -110, Y = 220  },
			new LunarSlotCoord { Slot = 19, X = 0,    Y = 230  },
			new LunarSlotCoord { Slot = 20, X = 110,  Y = 220  }
		};

		public override void _Ready()
		{
			Instance = this;

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

			// 2. Render Deep Space Backdrop
			ColorRect spaceBackdrop = new ColorRect
			{
				Name = "SpaceBackdrop",
				Color = new Color(0.01f, 0.02f, 0.04f, 1.0f),
				Position = new Vector2(-2000, -2000),
				Size = new Vector2(4000, 4000),
				ZIndex = -10
			};
			AddChild(spaceBackdrop);

			// 3. Build Lunar Sphere Disc & 20 Slots
			_lunarDiscContainer = new Node2D { Name = "LunarDiscContainer", ZIndex = 0 };
			AddChild(_lunarDiscContainer);
			BuildLunarSphere();

			// 4. Anchor BaseHUD on CanvasLayer (Layer 100) - Persistent Top Bar & Bottom Nav
			_hudLayer = new CanvasLayer { Name = "HUDLayer", Layer = 100 };
			AddChild(_hudLayer);

			_hudInstance = new BaseHUD();
			_hudLayer.AddChild(_hudInstance);

			GD.Print($"[MOON VIEW] Initialized for Moon #{ViewingMoon}. HUD Layer locked.");
		}

		public override void _Process(double delta)
		{
			float dt = (float)delta;

			if (_camera != null)
			{
				_camera.Position = _camera.Position.Lerp(_targetCameraPos, dt * 14.0f);
				_camera.Zoom = _camera.Zoom.Lerp(new Vector2(_targetZoom, _targetZoom), dt * 14.0f);
			}

			// Smooth Keyboard Navigation (WASD / Arrows)
			Vector2 panDir = Vector2.Zero;
			if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) panDir.Y -= 1.0f;
			if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) panDir.Y += 1.0f;
			if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) panDir.X -= 1.0f;
			if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) panDir.X += 1.0f;

			if (panDir != Vector2.Zero)
			{
				float speed = 850.0f / _targetZoom;
				_targetCameraPos += panDir.Normalized() * speed * dt;
			}
		}

		public override void _Input(InputEvent @event)
		{
			// Zoom Wheel / Two-Finger Touchpad Scroll
			if (@event is InputEventMouseButton mb)
			{
				if (mb.IsPressed())
				{
					if (mb.ButtonIndex == MouseButton.WheelUp)
					{
						_targetZoom = Mathf.Clamp(_targetZoom * 1.12f, 0.45f, 2.2f);
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (mb.ButtonIndex == MouseButton.WheelDown)
					{
						_targetZoom = Mathf.Clamp(_targetZoom * 0.88f, 0.45f, 2.2f);
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						// Do not drag if mouse is over top HUD or bottom navigation bar
						if (mb.Position.Y > 65 && mb.Position.Y < GetViewportRect().Size.Y - 65)
						{
							_isDragging = true;
							_dragStartMousePos = mb.Position;
							_dragStartCameraPos = _targetCameraPos;
						}
					}
				}
				else
				{
					if (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle)
					{
						_isDragging = false;
					}
				}
			}

			if (@event is InputEventMouseMotion mm && _isDragging)
			{
				Vector2 delta = (mm.Position - _dragStartMousePos) / _camera.Zoom.X;
				_targetCameraPos = _dragStartCameraPos - delta;
				GetViewport().SetInputAsHandled();
				return;
			}

			if (@event is InputEventScreenDrag sd)
			{
				Vector2 delta = sd.Relative / _camera.Zoom.X;
				_targetCameraPos -= delta;
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		private void BuildLunarSphere()
		{
			// 1. Central 620px Dark Lunar Disc Polygon
			int circlePoints = 64;
			float radius = 310.0f;
			Vector2[] discPolygon = new Vector2[circlePoints];
			for (int i = 0; i < circlePoints; i++)
			{
				float angle = (i / (float)circlePoints) * Mathf.Tau;
				discPolygon[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
			}

			Polygon2D spherePoly = new Polygon2D
			{
				Polygon = discPolygon,
				Color = new Color(0.04f, 0.07f, 0.12f, 0.95f)
			};
			_lunarDiscContainer.AddChild(spherePoly);

			// Glowing Cyan Perimeter Ring
			Line2D ringOutline = new Line2D
			{
				Width = 2.5f,
				DefaultColor = new Color(0f, 0.94f, 1f, 0.4f),
				Antialiased = true
			};
			for (int i = 0; i < circlePoints; i++) ringOutline.AddPoint(discPolygon[i]);
			ringOutline.AddPoint(discPolygon[0]);
			_lunarDiscContainer.AddChild(ringOutline);

			// Title Label above Moon
			Label moonTitle = new Label
			{
				Text = $"MOON M-{ViewingMoon} (20 SECTORS)",
				HorizontalAlignment = HorizontalAlignment.Center,
				Position = new Vector2(-200, -360),
				Size = new Vector2(400, 30),
				Modulate = new Color(0f, 0.94f, 1f)
			};
			moonTitle.AddThemeFontSizeOverride("font_size", 16);
			_lunarDiscContainer.AddChild(moonTitle);

			// 2. Build 20 Base Slots
			foreach (var coord in SlotCoords)
			{
				int slotNum = coord.Slot;
				bool isMyBase = (ViewingMoon == 100 && slotNum == 1);
				bool isOccupied = isMyBase || (slotNum % 3 == 0); // Mock occupied slots
				Color slotColor = isMyBase ? new Color("#00F0FF") : isOccupied ? new Color("#EF4444") : new Color("#94A3B8");

				Node2D slotAnchor = new Node2D
				{
					Position = new Vector2(coord.X, coord.Y),
					ZIndex = 5
				};

				Button slotBtn = new Button
				{
					Text = $"B-{(slotNum < 10 ? "0" + slotNum : slotNum.ToString())}\n{(isMyBase ? "MY BASE" : isOccupied ? "OUTPOST" : "VACANT")}",
					CustomMinimumSize = new Vector2(80, 52),
					OffsetLeft = -40,
					OffsetRight = 40,
					OffsetTop = -26,
					OffsetBottom = 26,
					MouseFilter = Control.MouseFilterEnum.Stop
				};
				slotBtn.AddThemeFontSizeOverride("font_size", 9);
				slotBtn.Modulate = slotColor;

				StyleBoxFlat slotStyle = new StyleBoxFlat
				{
					BgColor = new Color(0.03f, 0.05f, 0.08f, 0.92f),
					BorderColor = slotColor,
					BorderWidthLeft = 1,
					BorderWidthRight = 1,
					BorderWidthTop = 1,
					BorderWidthBottom = 1,
					CornerRadiusTopLeft = 8,
					CornerRadiusTopRight = 8,
					CornerRadiusBottomLeft = 8,
					CornerRadiusBottomRight = 8
				};
				slotBtn.AddThemeStyleboxOverride("normal", slotStyle);

				int capturedSlot = slotNum;
				slotBtn.Pressed += () => OnSlotPressed(capturedSlot, isMyBase);

				slotAnchor.AddChild(slotBtn);
				_lunarDiscContainer.AddChild(slotAnchor);
			}
		}

		private void OnSlotPressed(int slotNumber, bool isMyBase)
		{
			GD.Print($"[MOON VIEW] Slot B-{slotNumber} clicked. IsMyBase: {isMyBase}");

			if (isMyBase)
			{
				// Transition down to Tier 1: Base Interior
				GetTree().ChangeSceneToFile("res://Scenes/base_view.tscn");
			}
			else
			{
				GD.Print($"[MOON VIEW] Inspecting Sector M-{ViewingMoon}:B-{slotNumber}");
			}
		}
	}
}
