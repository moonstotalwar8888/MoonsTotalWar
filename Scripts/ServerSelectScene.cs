using Godot;
using System;
using System.Collections.Generic;
using MoonsTotalWar.Engine;

namespace MoonsTotalWar.UI
{
	/// <summary>
	/// MOONS TOTAL WAR: MULTIVERSE SERVER SELECTOR
	/// Allows commanders to enter speed-scaled universes or resume existing bases.
	/// </summary>
	public partial class ServerSelectScene : Control
	{
		private SupabaseService _supabase;
		private VBoxContainer _serverListContainer;

		public override void _Ready()
		{
			_supabase = GetNodeOrNull<SupabaseService>("/root/SupabaseService");
			BuildServerSelectUI();
		}

		private void BuildServerSelectUI()
		{
			AnchorsPreset = (int)LayoutPreset.FullRect;

			PanelContainer centerPanel = new PanelContainer();
			centerPanel.CustomMinimumSize = new Vector2(560, 580);
			centerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
			AddChild(centerPanel);

			StyleBoxFlat panelStyle = new StyleBoxFlat();
			panelStyle.BgColor = new Color(0.04f, 0.06f, 0.10f, 0.95f);
			panelStyle.SetCornerRadiusAll(12);
			panelStyle.SetBorderWidthAll(2);
			panelStyle.BorderColor = new Color(0f, 0.94f, 1f, 0.8f);
			centerPanel.AddThemeStyleboxOverride("panel", panelStyle);

			VBoxContainer mainContent = new VBoxContainer();
			mainContent.AddThemeConstantOverride("separation", 15);

			MarginContainer margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 20);
			margin.AddThemeConstantOverride("margin_right", 20);
			margin.AddThemeConstantOverride("margin_top", 20);
			margin.AddThemeConstantOverride("margin_bottom", 20);
			margin.AddChild(mainContent);
			centerPanel.AddChild(margin);

			// Header
			Label header = new Label();
			header.Text = "OPERATIONAL UNIVERSES";
			header.HorizontalAlignment = HorizontalAlignment.Center;
			header.AddThemeFontSizeOverride("font_size", 18);
			header.Modulate = new Color(0f, 0.94f, 1f);
			mainContent.AddChild(header);

			// Scrollable Server List
			ScrollContainer scroll = new ScrollContainer();
			scroll.CustomMinimumSize = new Vector2(0, 420);
			mainContent.AddChild(scroll);

			_serverListContainer = new VBoxContainer();
			_serverListContainer.AddThemeConstantOverride("separation", 10);
			_serverListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			scroll.AddChild(_serverListContainer);

			PopulateServerCards();
		}

		private void PopulateServerCards()
		{
			foreach (var srv in GameData.Servers)
			{
				PanelContainer card = new PanelContainer();
				card.CustomMinimumSize = new Vector2(0, 75);

				StyleBoxFlat cardStyle = new StyleBoxFlat();
				cardStyle.BgColor = new Color(0.08f, 0.12f, 0.18f, 0.90f);
				cardStyle.SetCornerRadiusAll(8);
				cardStyle.SetBorderWidthAll(1);
				cardStyle.BorderColor = srv.Status == "ONLINE" ? new Color(0f, 0.94f, 1f, 0.5f) : new Color(0.3f, 0.3f, 0.3f, 0.5f);
				card.AddThemeStyleboxOverride("panel", cardStyle);

				HBoxContainer row = new HBoxContainer();
				row.Alignment = BoxContainer.AlignmentMode.Begin;

				MarginContainer pad = new MarginContainer();
				pad.AddThemeConstantOverride("margin_left", 15);
				pad.AddThemeConstantOverride("margin_right", 15);
				pad.AddThemeConstantOverride("margin_top", 10);
				pad.AddThemeConstantOverride("margin_bottom", 10);
				pad.AddChild(row);
				card.AddChild(pad);

				// Info (takes expanding space)
				VBoxContainer infoBox = new VBoxContainer();
				infoBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				Label nameLbl = new Label();
				nameLbl.Text = srv.Name;
				nameLbl.AddThemeFontSizeOverride("font_size", 14);
				infoBox.AddChild(nameLbl);

				Label descLbl = new Label();
				descLbl.Text = srv.Desc;
				descLbl.AddThemeFontSizeOverride("font_size", 10);
				descLbl.Modulate = new Color(0.6f, 0.7f, 0.8f);
				infoBox.AddChild(descLbl);

				row.AddChild(infoBox);

				// Action button
				Button enterBtn = new Button();
				enterBtn.Text = srv.Status == "ONLINE" ? $"ENTER ({srv.Speed}X)" : "LOCKED";
				enterBtn.Disabled = srv.Status != "ONLINE";
				enterBtn.CustomMinimumSize = new Vector2(110, 40);
				enterBtn.Pressed += () => OnEnterServer(srv.Id);
				row.AddChild(enterBtn);

				_serverListContainer.AddChild(card);
			}
		}

		private void OnEnterServer(string serverId)
		{
			GD.Print($"[SERVER SELECT] Entering Sector Universe: {serverId}");
			GetTree().ChangeSceneToFile("res://Scenes/base_view.tscn");
		}
	}
}
