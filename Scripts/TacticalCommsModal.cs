using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	public class InboxMessageData
	{
		public long id { get; set; }
		public string sender_name { get; set; }
		public string recipient_name { get; set; }
		public string category { get; set; } // PERSONAL, ALLIANCE, BATTLE, TRADE, SYSTEM, INTEL, SENT
		public string title { get; set; }
		public string body { get; set; }
		public bool is_read { get; set; }
		public string created_at { get; set; }
	}

	public class ChatMessageData
	{
		public long id { get; set; }
		public string server_id { get; set; }
		public string commander_name { get; set; }
		public string message { get; set; }
		public string created_at { get; set; }
	}

	/// <summary>
	/// MOONS TOTAL WAR: CLASH OF KINGS STYLE COMMANDER COMMUNICATIONS INBOX, PLAYER CONVERSATION FOLDERS & GLOBAL FREQUENCY CHAT
	/// - Mode 1 (COMMS INBOX):
	///   * PERSONAL TAB ("COMMANDER CHATS"): Groups all sent & received player messages into folders listed by Sender/Commander Name!
	///   * Shows glowing red "🔔 NEW MESSAGE ALERT" badge and unread counter next to any Commander who sent you a new message.
	///   * Clicking a Commander's name opens the full 2-way chat history (sent & received messages) with a stable, persistent bottom reply bar (never duplicates or jumps!).
	///   * Alert badges on category tabs (PERSONAL, ALLIANCE, BATTLE, TRADE, SYSTEM, INTEL).
	/// - Mode 2 (GLOBAL CHAT):
	///   * Opening Global Chat places the LATEST messages at the TOP and older messages flowing down.
	///   * Floating Input Docking Bar on Mobile Keyboard: The main chat screen NEVER shrinks or jumps! Instead, ONLY the input field + send button bar floats right on top of the mobile keyboard while typing!
	/// - Mode 3 (ALLIANCE HQ): Quick link to Alliance Embassy.
	/// </summary>
	public partial class TacticalCommsModal : Control
	{
		private ColorRect _backdrop;
		private Panel _chassis;
		private Label _lblHeader;
		private Label _lblSubHeader;
		private HBoxContainer _topTabBar;
		private ScrollContainer _scroll;
		private VBoxContainer _contentStack;
		private PanelContainer _bottomBarFrame;
		private HBoxContainer _bottomActionBar;

		// Dedicated Floating Keyboard Input Bar (Only the input field + Send button rests on top of the keyboard; the modal never shrinks!)
		private PanelContainer _floatingKeyboardBar;
		private HBoxContainer _floatingKeyboardRow;
		private LineEdit _activeTypingInput;
		private Button _activeTypingSendBtn;

		private string _activePrimaryMode = "INBOX"; // INBOX, CHAT, ALLIANCE
		private string _activeInboxCategory = "PERSONAL"; // PERSONAL, ALLIANCE, BATTLE, TRADE, SYSTEM, INTEL
		private string _activeConversationPartner = ""; // When non-empty inside PERSONAL, shows full 2-way chat history with this Commander!
		private bool _isStartingNewConversation = false;

		// Generation ID prevents overlapping async network calls from ever creating duplicate input boxes or duplicate message cards!
		private int _viewRenderVersion = 0;

		private List<InboxMessageData> _inboxMessages = new List<InboxMessageData>();
		private List<ChatMessageData> _chatMessages = new List<ChatMessageData>();

		private LineEdit _chatInput;
		private LineEdit _threadReplyInput;
		private LineEdit _newConvRecipientInput;
		private LineEdit _newConvBodyInput;
		private LineEdit _joinAllianceInput;
		private LineEdit _foundAllianceInput;
		private LineEdit _allianceWarRoomInput;
		private LineEdit _allianceAnnouncementTitleInput;
		private LineEdit _allianceAnnouncementBodyInput;
		private Label _feedbackLabel;

		// Alliance Hub State (5 Tabs: MEMBERS, SCIENCES, WAR_ROOM, MESSAGING, ATTACKS)
		private string _activeAllianceHubTab = "MEMBERS";
		private List<SupabaseService.GalacticCommanderEntry> _cachedAllianceMembers = new List<SupabaseService.GalacticCommanderEntry>();
		private List<ChatMessageData> _allianceWarRoomMessages = new List<ChatMessageData>();
		private List<InboxMessageData> _allianceAnnouncements = new List<InboxMessageData>();
		private List<SupabaseService.AllianceIncomingAttackAlert> _allianceIncomingAttacks = new List<SupabaseService.AllianceIncomingAttackAlert>();
		private Dictionary<string, SupabaseService.AllianceScienceSharedNode> _allianceSharedSciences = new Dictionary<string, SupabaseService.AllianceScienceSharedNode>();
		private readonly Dictionary<string, Label> _allianceScienceLiveTimerLabels = new Dictionary<string, Label>();
		private double _allianceScienceTickTimer = 0.0;

		private double _chatPollTimer = 0.0;
		private float _lastFloatingKbY = -1f;
		private bool _isInputDockedAboveKeyboard = false;

		private class PlayerConversationFolder
		{
			public string PartnerName;
			public List<InboxMessageData> Messages = new List<InboxMessageData>();
			public int UnreadFromPartnerCount = 0;
			public string LatestPreview = "";
			public string LatestTimestampIso = "";
			public DateTimeOffset LatestSortTime = DateTimeOffset.MinValue;
		}

		public override void _Ready()
		{
			SetAnchorsPreset(LayoutPreset.FullRect);
			MouseFilter = MouseFilterEnum.Ignore;
			ZIndex = 310;

			GetViewport().Connect("size_changed", Callable.From(RecenterModal));
			BuildModalFrame();
			Visible = false;
		}

		public override void _Process(double delta)
		{
			if (!Visible) return;

			// 1. Float ONLY the Input Field + Send Button on top of the Mobile Keyboard (Main Screen NEVER shrinks or jumps!)
			UpdateFloatingKeyboardInputBarPosition();

			// 1B. Live 1-Second Countdown Ticker for Active 1-Hour Alliance Science Upgrades
			if (_activePrimaryMode == "ALLIANCE" && _activeAllianceHubTab == "SCIENCES")
			{
				_allianceScienceTickTimer += delta;
				if (_allianceScienceTickTimer >= 1.0)
				{
					_allianceScienceTickTimer = 0.0;
					long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
					bool anyCompleted = false;
					foreach (var kv in _allianceScienceLiveTimerLabels)
					{
						var lbl = kv.Value;
						if (lbl == null || !IsInstanceValid(lbl)) continue;
						if (_allianceSharedSciences.TryGetValue(kv.Key, out var node) && node.UpgradeEndUnixSec > 0)
						{
							long rem = Math.Max(0, node.UpgradeEndUnixSec - nowSec);
							lbl.Text = $"⏱ 1-HOUR UPGRADE IN PROGRESS (TO LVL {node.CurrentLevel + 1}/{node.MaxLevel}): {GameMath.FormatTime(rem)} REMAINING";
							if (rem <= 0) anyCompleted = true;
						}
					}
					if (anyCompleted)
					{
						string myAll = (SupabaseService.CachedAllianceName ?? "NONE").Trim().ToUpper();
						if (myAll != "NONE") _ = RenderAllianceSciencesTabAsync(myAll, _viewRenderVersion);
					}
				}
			}

			// 2. Real-time polling for Global Chat, Active Player Conversation Thread, or Alliance War Room / Attacks / Sciences
			_chatPollTimer += delta;
			if (_chatPollTimer >= 4.5)
			{
				_chatPollTimer = 0.0;
				if (_activePrimaryMode == "CHAT")
				{
					RefreshGlobalChatAsync();
				}
				else if (_activePrimaryMode == "INBOX" && _activeInboxCategory == "PERSONAL" && !string.IsNullOrEmpty(_activeConversationPartner))
				{
					RefreshActivePlayerThreadQuietlyAsync();
				}
				else if (_activePrimaryMode == "ALLIANCE")
				{
					string myAll = (SupabaseService.CachedAllianceName ?? "NONE").Trim().ToUpper();
					if (myAll != "NONE")
					{
						if (_activeAllianceHubTab == "WAR_ROOM")
						{
							_ = RefreshAllianceWarRoomFlowAsync(_viewRenderVersion, myAll, scrollToTop: false);
						}
						else if (_activeAllianceHubTab == "ATTACKS")
						{
							_ = RefreshAllianceAttacksFlowAsync(_viewRenderVersion, myAll);
						}
					}
				}
			}
		}

		/// <summary>
		/// Keeps the main modal chassis at its full, stable size at all times!
		/// When the mobile virtual keyboard opens while typing in Global Chat or Player Chat,
		/// ONLY the input field + send button bar is lifted to rest directly on top of the keyboard.
		/// </summary>
		private void UpdateFloatingKeyboardInputBarPosition()
		{
			if (_chassis == null || _floatingKeyboardBar == null || _bottomActionBar == null) return;

			Vector2 vp = GetViewportRect().Size;
			int rawKbHeight = DisplayServer.VirtualKeyboardGetHeight();

			bool typingFocused = _activeTypingInput != null && IsInstanceValid(_activeTypingInput) && _activeTypingInput.HasFocus();
			bool isTouchOrMobile = OS.HasFeature("mobile") || OS.HasFeature("android") || OS.HasFeature("ios") ||
								   OS.HasFeature("web_android") || OS.HasFeature("web_ios") || DisplayServer.IsTouchscreenAvailable();

			float kbHeightViewport = 0f;
			if (rawKbHeight > 20)
			{
				Vector2 winSize = DisplayServer.WindowGetSize();
				float scaleY = winSize.Y > 0 ? (vp.Y / winSize.Y) : 1.0f;
				kbHeightViewport = rawKbHeight * scaleY;
			}
			else if (typingFocused && isTouchOrMobile)
			{
				// Fallback for mobile web / Android fullscreen wrappers where VirtualKeyboardGetHeight reports 0
				kbHeightViewport = vp.Y * 0.42f;
			}

			bool shouldFloatInputOnly = (kbHeightViewport > 20f) && _activeTypingInput != null && IsInstanceValid(_activeTypingInput);

			if (shouldFloatInputOnly)
			{
				if (!_isInputDockedAboveKeyboard)
				{
					_isInputDockedAboveKeyboard = true;
					// Reparent ONLY the input box and send button into the floating keyboard bar!
					if (_activeTypingInput.GetParent() != _floatingKeyboardRow)
					{
						_activeTypingInput.GetParent()?.RemoveChild(_activeTypingInput);
						_floatingKeyboardRow.AddChild(_activeTypingInput);
					}
					if (_activeTypingSendBtn != null && IsInstanceValid(_activeTypingSendBtn) && _activeTypingSendBtn.GetParent() != _floatingKeyboardRow)
					{
						_activeTypingSendBtn.GetParent()?.RemoveChild(_activeTypingSendBtn);
						_floatingKeyboardRow.AddChild(_activeTypingSendBtn);
					}
					_floatingKeyboardBar.Visible = true;
					_activeTypingInput.GrabFocus();
					_activeTypingInput.CaretColumn = _activeTypingInput.Text?.Length ?? 0;
				}

				float barW = Mathf.Min(740f, Mathf.Max(340f, vp.X - 20f));
				float barH = 58f;
				float targetY = Mathf.Clamp(vp.Y - kbHeightViewport - barH - 4f, 16f, vp.Y - barH - 8f);

				if (Mathf.Abs(targetY - _lastFloatingKbY) > 1.0f)
				{
					_lastFloatingKbY = targetY;
					_floatingKeyboardBar.CustomMinimumSize = new Vector2(barW, barH);
					_floatingKeyboardBar.Size = new Vector2(barW, barH);
					_floatingKeyboardBar.Position = new Vector2((vp.X - barW) * 0.5f, targetY);
				}
			}
			else if (_isInputDockedAboveKeyboard)
			{
				RestoreInputFromFloatingKeyboardBar();
			}
		}

		private void RestoreInputFromFloatingKeyboardBar()
		{
			_isInputDockedAboveKeyboard = false;
			_lastFloatingKbY = -1f;
			if (_floatingKeyboardBar != null && IsInstanceValid(_floatingKeyboardBar))
			{
				_floatingKeyboardBar.Visible = false;
			}

			if (_bottomActionBar != null && IsInstanceValid(_bottomActionBar))
			{
				if (_activeTypingInput != null && IsInstanceValid(_activeTypingInput) && _activeTypingInput.GetParent() != _bottomActionBar)
				{
					_activeTypingInput.GetParent()?.RemoveChild(_activeTypingInput);
					_bottomActionBar.AddChild(_activeTypingInput);
				}
				if (_activeTypingSendBtn != null && IsInstanceValid(_activeTypingSendBtn) && _activeTypingSendBtn.GetParent() != _bottomActionBar)
				{
					_activeTypingSendBtn.GetParent()?.RemoveChild(_activeTypingSendBtn);
					_bottomActionBar.AddChild(_activeTypingSendBtn);
				}
			}
		}

		private static void ClearContainerImmediate(Node container)
		{
			if (container == null || !IsInstanceValid(container)) return;
			for (int i = container.GetChildCount() - 1; i >= 0; i--)
			{
				Node child = container.GetChild(i);
				container.RemoveChild(child);
				child.QueueFree();
			}
		}

		private void RecenterModal()
		{
			if (_chassis == null) return;
			Vector2 vp = GetViewportRect().Size;
			if (vp.X <= 0 || vp.Y <= 0) return;

			// Wide Command Console framing:
			// - Top Cockpit Bar (Y: 0..68) remains permanently visible -> topSafeY = 74
			// - Bottom Nav Dock (Y: vp.Y - 72..vp.Y) -> bottomSafeY = vp.Y - 78
			// - Side docks auto-hide while any primary modal is open, giving up to 1100px width!
			float topSafeY = 74f;
			float bottomSafeY = Math.Max(topSafeY + 340f, vp.Y - 78f);
			float maxSafeH = bottomSafeY - topSafeY;

			float desiredMaxW = 1100f;
			float w = Mathf.Min(desiredMaxW, Math.Max(480f, vp.X * 0.90f));
			float x = (vp.X - w) * 0.5f;

			float h = maxSafeH;
			float y = topSafeY + (maxSafeH - h) * 0.5f;

			_chassis.CustomMinimumSize = new Vector2(w, h);
			_chassis.Size = new Vector2(w, h);
			_chassis.Position = new Vector2(x, y);
		}

		private void BuildModalFrame()
		{
			ClearContainerImmediate(this);

			_backdrop = new ColorRect
			{
				Color = new Color(0f, 0f, 0f, 0.72f),
				MouseFilter = MouseFilterEnum.Stop
			};
			_backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
			_backdrop.GuiInput += (InputEvent @event) =>
			{
				if (@event is InputEventMouseButton mb && mb.IsPressed() && mb.ButtonIndex == MouseButton.Left)
				{
					CloseModal();
				}
			};
			AddChild(_backdrop);

			_chassis = new Panel
			{
				CustomMinimumSize = new Vector2(920, 620),
				MouseFilter = MouseFilterEnum.Stop
			};
			StyleBoxFlat cStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.05f, 0.11f, 0.21f, 0.99f),
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
				ShadowSize = 22
			};
			_chassis.AddThemeStyleboxOverride("panel", cStyle);
			AddChild(_chassis);

			VBoxContainer rootBox = new VBoxContainer();
			rootBox.SetAnchorsPreset(LayoutPreset.FullRect);
			rootBox.OffsetLeft = 14;
			rootBox.OffsetRight = -14;
			rootBox.OffsetTop = 10;
			rootBox.OffsetBottom = -10;
			rootBox.AddThemeConstantOverride("separation", 8);
			_chassis.AddChild(rootBox);

			HBoxContainer headerRow = new HBoxContainer();
			headerRow.AddThemeConstantOverride("separation", 10);

			VBoxContainer titleCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			titleCol.AddThemeConstantOverride("separation", 2);

			_lblHeader = MakeLabel("📡  COMMANDER TACTICAL COMMS CENTER", 20, new Color("#00F0FF"));
			_lblSubHeader = MakeLabel("CLASH OF COMMANDERS · ENCRYPTED WAR ROOM & PLAYER CHANNELS", 14, new Color("#E2E8F0"));
			titleCol.AddChild(_lblHeader);
			titleCol.AddChild(_lblSubHeader);
			headerRow.AddChild(titleCol);

			Button closeBtn = new Button
			{
				Text = " ✕ CLOSE ",
				CustomMinimumSize = new Vector2(96, 38),
				MouseFilter = MouseFilterEnum.Stop
			};
			closeBtn.AddThemeFontSizeOverride("font_size", 15);
			StyleBoxFlat closeStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.25f, 0.06f, 0.08f, 0.95f),
				BorderColor = new Color("#EF4444")
			};
			closeStyle.SetBorderWidthAll(2);
			closeStyle.SetCornerRadiusAll(8);
			closeBtn.AddThemeStyleboxOverride("normal", closeStyle);
			closeBtn.Pressed += CloseModal;
			headerRow.AddChild(closeBtn);

			rootBox.AddChild(headerRow);

			_topTabBar = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			_topTabBar.AddThemeConstantOverride("separation", 6);
			rootBox.AddChild(_topTabBar);

			rootBox.AddChild(new HSeparator());

			_scroll = new ScrollContainer
			{
				SizeFlagsVertical = SizeFlags.ExpandFill,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
			};
			rootBox.AddChild(_scroll);

			_contentStack = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			_contentStack.AddThemeConstantOverride("separation", 8);
			_scroll.AddChild(_contentStack);

			_bottomBarFrame = new PanelContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(0, 50)
			};
			StyleBoxFlat botStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.02f, 0.05f, 0.10f, 0.96f),
				BorderColor = new Color(0f, 0.94f, 1f, 0.35f)
			};
			botStyle.SetBorderWidthAll(1);
			botStyle.SetCornerRadiusAll(8);
			botStyle.ContentMarginLeft = 8;
			botStyle.ContentMarginRight = 8;
			botStyle.ContentMarginTop = 4;
			botStyle.ContentMarginBottom = 4;
			_bottomBarFrame.AddThemeStyleboxOverride("panel", botStyle);

			_bottomActionBar = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			_bottomActionBar.AddThemeConstantOverride("separation", 8);
			_bottomBarFrame.AddChild(_bottomActionBar);
			rootBox.AddChild(_bottomBarFrame);

			// Dedicated Floating Input Bar that sits directly on top of the mobile keyboard when typing (without shrinking the screen!)
			_floatingKeyboardBar = new PanelContainer
			{
				Visible = false,
				ZIndex = 340,
				MouseFilter = MouseFilterEnum.Stop
			};
			StyleBoxFlat floatStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.02f, 0.06f, 0.12f, 0.99f),
				BorderColor = new Color("#00F0FF"),
				ShadowColor = new Color(0f, 0f, 0f, 0.85f),
				ShadowSize = 16
			};
			floatStyle.SetBorderWidthAll(2);
			floatStyle.SetCornerRadiusAll(10);
			floatStyle.ContentMarginLeft = 10;
			floatStyle.ContentMarginRight = 10;
			floatStyle.ContentMarginTop = 6;
			floatStyle.ContentMarginBottom = 6;
			_floatingKeyboardBar.AddThemeStyleboxOverride("panel", floatStyle);

			_floatingKeyboardRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			_floatingKeyboardRow.AddThemeConstantOverride("separation", 8);
			_floatingKeyboardBar.AddChild(_floatingKeyboardRow);
			AddChild(_floatingKeyboardBar);

			RecenterModal();
		}

		private void ClearView()
		{
			RestoreInputFromFloatingKeyboardBar();

			_activeTypingInput = null;
			_activeTypingSendBtn = null;
			_chatInput = null;
			_threadReplyInput = null;
			_newConvRecipientInput = null;
			_newConvBodyInput = null;
			_joinAllianceInput = null;
			_foundAllianceInput = null;
			_allianceWarRoomInput = null;
			_allianceAnnouncementTitleInput = null;
			_allianceAnnouncementBodyInput = null;

			ClearContainerImmediate(_floatingKeyboardRow);
			ClearContainerImmediate(_topTabBar);
			ClearContainerImmediate(_contentStack);
			ClearContainerImmediate(_bottomActionBar);
		}

		// =========================================================================
		// 1. COMMANDER COMMUNICATIONS INBOX (CLASH OF KINGS GAMING INTERFACE)
		// =========================================================================
		public void OpenPlayerConversationThread(string partnerCommanderName) => OpenPlayerConversation(partnerCommanderName);

		public void OpenPlayerConversation(string partnerCommanderName)
		{
			if (string.IsNullOrWhiteSpace(partnerCommanderName))
			{
				OpenCommsInbox("PERSONAL");
				return;
			}
			_activeConversationPartner = partnerCommanderName.Trim();
			_isStartingNewConversation = false;
			OpenCommsInbox("PERSONAL", keepActivePartner: true);
		}

		public async void OpenCommsInbox(string category = "PERSONAL", bool keepActivePartner = false)
		{
			int myRenderVersion = ++_viewRenderVersion;
			_activePrimaryMode = "INBOX";
			_activeInboxCategory = category;
			if (!keepActivePartner)
			{
				_activeConversationPartner = "";
				_isStartingNewConversation = false;
			}
			ClearView();
			Visible = true;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();
			RecenterModal();

			// Render immediately on Frame 0 using cached messages so opening Messages or Battle Logs has ZERO lag!
			EnsureDefaultSeedMessages();
			RebuildInboxTopTabsAndBadges();
			if (_activeInboxCategory == "PERSONAL")
			{
				if (!string.IsNullOrEmpty(_activeConversationPartner))
				{
					await SetupAndRenderPlayerConversationAsync(_activeConversationPartner, myRenderVersion, scrollToBottom: true);
				}
				else
				{
					RenderCommanderFoldersView();
				}
			}
			else
			{
				await RenderStandardCategoryLogsAsync(_activeInboxCategory, myRenderVersion);
			}

			// Then refresh live messages in background from Supabase
			if (SupabaseService.Instance != null)
			{
				var fetched = await SupabaseService.Instance.FetchInboxMessagesAsync(SupabaseService.CachedCommanderName);
				if (myRenderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "INBOX") return;
				_inboxMessages = fetched;
			}
			if (myRenderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "INBOX") return;

			EnsureDefaultSeedMessages();
			RebuildInboxTopTabsAndBadges();

			if (_activeInboxCategory == "PERSONAL")
			{
				if (!string.IsNullOrEmpty(_activeConversationPartner))
				{
					await PopulatePlayerConversationMessagesOnlyAsync(_activeConversationPartner, myRenderVersion, scrollToBottom: true);
				}
				else
				{
					RenderCommanderFoldersView();
				}
			}
			else
			{
				await RenderStandardCategoryLogsAsync(_activeInboxCategory, myRenderVersion);
			}
		}

		private void RebuildInboxTopTabsAndBadges()
		{
			ClearContainerImmediate(_topTabBar);

			string myCallsign = (SupabaseService.CachedCommanderName ?? "").Trim();
			var unreadByCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
			{
				{ "PERSONAL", 0 }, { "ALLIANCE", 0 }, { "BATTLE", 0 },
				{ "TRADE", 0 },    { "SYSTEM", 0 },   { "INTEL", 0 }
			};

			int totalUnreadAll = 0;
			foreach (var m in _inboxMessages)
			{
				if (m.id <= 0 || m.is_read) continue;
				if (!string.Equals((m.recipient_name ?? "").Trim(), myCallsign, StringComparison.OrdinalIgnoreCase)) continue;

				string title = m.title ?? "";
				if (title.StartsWith("INCOMING_FLEET:", StringComparison.Ordinal) ||
					title.StartsWith("CANCEL_FLEET:", StringComparison.Ordinal) ||
					title.StartsWith("IMPACT_SYNC:", StringComparison.Ordinal) ||
					title.StartsWith("GARRISON_SYNC:", StringComparison.Ordinal))
				{
					continue;
				}

				string c = NormalizeCategory(m.category);
				if (unreadByCategory.ContainsKey(c)) unreadByCategory[c]++;
				totalUnreadAll++;
			}

			// Update HUD Alert Badges
			BaseHUD.Instance?.UpdateInboxAlertBadges(
				totalUnreadAll,
				unreadByCategory["PERSONAL"],
				unreadByCategory["BATTLE"]
			);

			_lblHeader.Text = "📡  COMMANDER WAR ROOM COMMS";
			_lblHeader.Modulate = new Color("#00F0FF");
			_lblSubHeader.Text = $"CALLSIGN: {myCallsign.ToUpper()}  ·  ACTIVE CHANNEL: [{_activeInboxCategory}]  ·  {(totalUnreadAll > 0 ? $"🔔 {totalUnreadAll} NEW ALERT(S)" : "ALL CHANNELS CLEAR")}";

			var categories = new[]
			{
				new { Code = "PERSONAL", Label = "💬 CHATS" },
				new { Code = "BATTLE",   Label = "⚔️ BATTLE" },
				new { Code = "ALLIANCE", Label = "🛡️ ALLIANCE" },
				new { Code = "INTEL",    Label = "📡 INTEL" },
				new { Code = "TRADE",    Label = "📦 TRADE" },
				new { Code = "SYSTEM",   Label = "⚙️ SYSTEM" }
			};

			foreach (var cat in categories)
			{
				bool isSelected = (_activeInboxCategory == cat.Code);
				int catUnread = unreadByCategory.GetValueOrDefault(cat.Code, 0);

				string tabText = catUnread > 0 ? $"{cat.Label} ({catUnread})" : cat.Label;
				Button tabBtn = new Button
				{
					Text = tabText,
					CustomMinimumSize = new Vector2(88, 40),
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					ClipText = true
				};
				tabBtn.AddThemeFontSizeOverride("font_size", 14);

				StyleBoxFlat tabStyle = new StyleBoxFlat
				{
					BgColor = isSelected
						? new Color(0.05f, 0.20f, 0.34f, 0.96f)
						: (catUnread > 0 ? new Color(0.28f, 0.06f, 0.09f, 0.92f) : new Color(0.03f, 0.07f, 0.14f, 0.90f)),
					BorderColor = catUnread > 0
						? new Color("#EF4444")
						: (isSelected ? new Color("#00F0FF") : new Color(0f, 0.94f, 1f, 0.28f))
				};
				tabStyle.SetBorderWidthAll(isSelected || catUnread > 0 ? 2 : 1);
				tabStyle.SetCornerRadiusAll(6);
				tabBtn.AddThemeStyleboxOverride("normal", tabStyle);
				tabBtn.Modulate = catUnread > 0
					? new Color("#FCA5A5")
					: (isSelected ? new Color("#00F0FF") : new Color("#CBD5E1"));

				string cCode = cat.Code;
				tabBtn.Pressed += () => OpenCommsInbox(cCode, keepActivePartner: false);
				_topTabBar.AddChild(tabBtn);
			}
		}

		// =========================================================================
		// 1A. CLASH OF KINGS STYLE: SENDER COMMANDER FOLDERS LIST
		// =========================================================================
		private void RenderCommanderFoldersView()
		{
			RestoreInputFromFloatingKeyboardBar();
			_activeTypingInput = null;
			_activeTypingSendBtn = null;
			_threadReplyInput = null;

			ClearContainerImmediate(_contentStack);
			ClearContainerImmediate(_bottomActionBar);
			_bottomBarFrame.Visible = true;

			string myCallsign = (SupabaseService.CachedCommanderName ?? "").Trim();
			var foldersByPartner = new Dictionary<string, PlayerConversationFolder>(StringComparer.OrdinalIgnoreCase);

			foreach (var msg in _inboxMessages)
			{
				string cat = NormalizeCategory(msg.category);
				if (cat != "PERSONAL") continue;

				string sender = (msg.sender_name ?? "").Trim();
				string recipient = (msg.recipient_name ?? "").Trim();
				if (string.IsNullOrEmpty(sender) && string.IsNullOrEmpty(recipient)) continue;

				bool sentByMe = string.Equals(sender, myCallsign, StringComparison.OrdinalIgnoreCase);
				string partner = sentByMe ? recipient : sender;
				if (string.IsNullOrEmpty(partner)) partner = sender;

				if (!foldersByPartner.TryGetValue(partner, out var folder))
				{
					folder = new PlayerConversationFolder { PartnerName = partner };
					foldersByPartner[partner] = folder;
				}

				folder.Messages.Add(msg);
				if (!sentByMe && !msg.is_read && msg.id > 0)
				{
					folder.UnreadFromPartnerCount++;
				}

				DateTimeOffset msgTime = DateTimeOffset.MinValue;
				if (!string.IsNullOrEmpty(msg.created_at))
				{
					DateTimeOffset.TryParse(msg.created_at, out msgTime);
				}

				if (msgTime >= folder.LatestSortTime || string.IsNullOrEmpty(folder.LatestPreview))
				{
					folder.LatestSortTime = msgTime;
					folder.LatestTimestampIso = msg.created_at;
					string prefix = sentByMe ? "You: " : $"{partner}: ";
					string rawBody = (msg.body ?? "").Replace("\n", " ").Trim();
					if (rawBody.Length > 72) rawBody = rawBody.Substring(0, 69) + "...";
					folder.LatestPreview = prefix + rawBody;
				}
			}

			var sortedFolders = new List<PlayerConversationFolder>(foldersByPartner.Values);
			sortedFolders.Sort((a, b) =>
			{
				if ((a.UnreadFromPartnerCount > 0) != (b.UnreadFromPartnerCount > 0))
				{
					return b.UnreadFromPartnerCount.CompareTo(a.UnreadFromPartnerCount);
				}
				return b.LatestSortTime.CompareTo(a.LatestSortTime);
			});

			// Top Banner Inside Commander Channels
			PanelContainer bannerCard = CreateCard(new Color("#00F0FF"));
			VBoxContainer bannerBox = CreateCardVBox(bannerCard);
			HBoxContainer bannerRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			bannerRow.AddThemeConstantOverride("separation", 10);

			VBoxContainer bannerTextCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			bannerTextCol.AddThemeConstantOverride("separation", 2);
			bannerTextCol.AddChild(MakeLabel("📁  COMMANDER MESSAGE CHANNELS (TAP PLAYER NAME TO OPEN CHAT HISTORY)", 13, new Color("#00F0FF"), true));
			bannerTextCol.AddChild(MakeLabel("Messages from each Commander are grouped inside their Callsign folder. Unread transmissions trigger a red Message Alert.", 11, new Color("#94A3B8"), true));
			bannerRow.AddChild(bannerTextCol);

			Button btnNewMsg = new Button
			{
				Text = _isStartingNewConversation ? "✕ CANCEL NEW" : "➕ MESSAGE COMMANDER",
				CustomMinimumSize = new Vector2(174, 38)
			};
			btnNewMsg.AddThemeFontSizeOverride("font_size", 12);
			btnNewMsg.Modulate = _isStartingNewConversation ? new Color("#EF4444") : new Color("#4ADE80");
			btnNewMsg.Pressed += () =>
			{
				_isStartingNewConversation = !_isStartingNewConversation;
				RenderCommanderFoldersView();
			};
			bannerRow.AddChild(btnNewMsg);
			bannerBox.AddChild(bannerRow);

			if (_isStartingNewConversation)
			{
				bannerBox.AddChild(new HSeparator());
				bannerBox.AddChild(MakeLabel("📡 START NEW COMMANDER CONVERSATION:", 12, new Color("#4ADE80")));
				HBoxContainer newRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				newRow.AddThemeConstantOverride("separation", 8);

				_newConvRecipientInput = new LineEdit
				{
					PlaceholderText = "Commander Name...",
					CustomMinimumSize = new Vector2(165, 40)
				};
				_newConvRecipientInput.AddThemeFontSizeOverride("font_size", 13);

				_newConvBodyInput = new LineEdit
				{
					PlaceholderText = "Type your message...",
					CustomMinimumSize = new Vector2(220, 40),
					SizeFlagsHorizontal = SizeFlags.ExpandFill
				};
				_newConvBodyInput.AddThemeFontSizeOverride("font_size", 13);
				_newConvBodyInput.TextSubmitted += (_) => OnStartNewCommanderConversationSend();

				Button btnSendNew = new Button
				{
					Text = "📡 SEND",
					CustomMinimumSize = new Vector2(105, 40)
				};
				btnSendNew.AddThemeFontSizeOverride("font_size", 13);
				btnSendNew.Modulate = new Color("#4ADE80");
				btnSendNew.Pressed += OnStartNewCommanderConversationSend;

				newRow.AddChild(_newConvRecipientInput);
				newRow.AddChild(_newConvBodyInput);
				newRow.AddChild(btnSendNew);
				bannerBox.AddChild(newRow);
			}

			_contentStack.AddChild(bannerCard);

			if (sortedFolders.Count == 0)
			{
				PanelContainer emptyCard = CreateCard(new Color("#475569"));
				VBoxContainer eBox = CreateCardVBox(emptyCard);
				eBox.AddChild(MakeLabel("📭 NO COMMANDER CONVERSATIONS YET", 15, new Color("#00F0FF")));
				eBox.AddChild(MakeLabel("Click '➕ MESSAGE COMMANDER' above or tap any player's base on a Moon to send them a message and start a conversation folder!", 13, new Color("#CBD5E1"), true));
				_contentStack.AddChild(emptyCard);
			}
			else
			{
				foreach (var folder in sortedFolders)
				{
					bool hasUnread = folder.UnreadFromPartnerCount > 0;
					Color folderBorder = hasUnread ? new Color("#EF4444") : new Color("#00F0FF");

					PanelContainer folderCard = CreateCard(folderBorder);
					VBoxContainer fBox = CreateCardVBox(folderCard);

					HBoxContainer row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
					row.AddThemeConstantOverride("separation", 12);

					// Commander Avatar / Folder Emblem
					PanelContainer emblem = new PanelContainer
					{
						CustomMinimumSize = new Vector2(48, 48),
						SizeFlagsVertical = SizeFlags.ShrinkCenter
					};
					StyleBoxFlat embStyle = new StyleBoxFlat
					{
						BgColor = hasUnread ? new Color(0.35f, 0.06f, 0.09f, 0.96f) : new Color(0.04f, 0.14f, 0.25f, 0.96f),
						BorderColor = folderBorder
					};
					embStyle.SetBorderWidthAll(2);
					embStyle.SetCornerRadiusAll(8);
					emblem.AddThemeStyleboxOverride("panel", embStyle);
					Label embIcon = MakeLabel(hasUnread ? "🔔" : "👨‍🚀", 20, Colors.White);
					embIcon.HorizontalAlignment = HorizontalAlignment.Center;
					embIcon.VerticalAlignment = VerticalAlignment.Center;
					emblem.AddChild(embIcon);
					row.AddChild(emblem);

					// Middle Column: Sender Player Name + Timestamp on Row 1, Alert + Message Preview on Row 2 (Zero horizontal overflow!)
					VBoxContainer midCol = new VBoxContainer
					{
						SizeFlagsHorizontal = SizeFlags.ExpandFill,
						ClipContents = true
					};
					midCol.AddThemeConstantOverride("separation", 4);

					HBoxContainer nameRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
					nameRow.AddThemeConstantOverride("separation", 6);

					string capturedPartner = folder.PartnerName;
					string displayName = folder.PartnerName.ToUpper();
					if (displayName.Length > 18) displayName = displayName.Substring(0, 16) + "..";

					Button btnPlayerName = new Button
					{
						Text = hasUnread
							? $"📁 {displayName}  🔴({folder.UnreadFromPartnerCount} NEW)"
							: $"📁 COMMANDER: {displayName}",
						CustomMinimumSize = new Vector2(120, 28),
						SizeFlagsHorizontal = SizeFlags.ExpandFill,
						Flat = true,
						Alignment = HorizontalAlignment.Left,
						ClipText = true
					};
					btnPlayerName.AddThemeFontSizeOverride("font_size", 14);
					btnPlayerName.Modulate = hasUnread ? new Color("#FDE047") : new Color("#00F0FF");
					btnPlayerName.Pressed += () => SwitchToCommanderThreadImmediate(capturedPartner);
					nameRow.AddChild(btnPlayerName);

					Label timeLbl = MakeLabel($"{FormatTimestamp(folder.LatestTimestampIso)} · {folder.Messages.Count} MSG", 11, new Color("#94A3B8"));
					timeLbl.HorizontalAlignment = HorizontalAlignment.Right;
					nameRow.AddChild(timeLbl);

					midCol.AddChild(nameRow);
					string previewLine = hasUnread ? $"🔔 NEW MESSAGE: {folder.LatestPreview}" : folder.LatestPreview;
					midCol.AddChild(MakeLabel(previewLine, 12, hasUnread ? Colors.White : new Color("#CBD5E1"), true));
					row.AddChild(midCol);

					// Right Action Button: OPEN CHAT (Fixed compact width so it never pushes or overlaps!)
					Button btnOpenChat = new Button
					{
						Text = hasUnread ? $"🔔 READ ({folder.UnreadFromPartnerCount})" : "💬 OPEN ➔",
						CustomMinimumSize = new Vector2(114, 40),
						SizeFlagsVertical = SizeFlags.ShrinkCenter
					};
					btnOpenChat.AddThemeFontSizeOverride("font_size", 12);
					StyleBoxFlat openStyle = new StyleBoxFlat
					{
						BgColor = hasUnread ? new Color(0.45f, 0.08f, 0.12f, 0.96f) : new Color(0.04f, 0.18f, 0.30f, 0.96f),
						BorderColor = hasUnread ? new Color("#EF4444") : new Color("#00F0FF")
					};
					openStyle.SetBorderWidthAll(2);
					openStyle.SetCornerRadiusAll(8);
					btnOpenChat.AddThemeStyleboxOverride("normal", openStyle);
					btnOpenChat.Modulate = hasUnread ? new Color("#FDE047") : new Color("#00F0FF");
					btnOpenChat.Pressed += () => SwitchToCommanderThreadImmediate(capturedPartner);
					row.AddChild(btnOpenChat);

					fBox.AddChild(row);
					_contentStack.AddChild(folderCard);
				}
			}

			// Bottom Action Bar for Folders View (No input box here — only clean navigation buttons!)
			Button btnRefreshInbox = new Button
			{
				Text = "🔄 REFRESH COMMANDER INBOX",
				CustomMinimumSize = new Vector2(220, 40),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			btnRefreshInbox.AddThemeFontSizeOverride("font_size", 13);
			btnRefreshInbox.Modulate = new Color("#00F0FF");
			btnRefreshInbox.Pressed += () => OpenCommsInbox("PERSONAL", keepActivePartner: false);
			_bottomActionBar.AddChild(btnRefreshInbox);
		}

		private async void SwitchToCommanderThreadImmediate(string partnerName)
		{
			_activeConversationPartner = partnerName;
			_isStartingNewConversation = false;
			int myRenderVersion = ++_viewRenderVersion;
			await SetupAndRenderPlayerConversationAsync(partnerName, myRenderVersion, scrollToBottom: true);
		}

		private async void OnStartNewCommanderConversationSend()
		{
			string rec = _newConvRecipientInput?.Text?.Trim() ?? "";
			string body = _newConvBodyInput?.Text?.Trim() ?? "";
			if (string.IsNullOrEmpty(rec) || string.IsNullOrEmpty(body)) return;

			if (SupabaseService.Instance != null)
			{
				await SupabaseService.Instance.SendInboxSignalAsync(
					SupabaseService.CachedCommanderName,
					rec,
					"PERSONAL",
					$"DIRECT COMMS · {SupabaseService.CachedCommanderName.ToUpper()}",
					body
				);
			}

			_isStartingNewConversation = false;
			OpenPlayerConversation(rec);
		}

		// =========================================================================
		// 1B. CLASH OF KINGS STYLE: 2-WAY PLAYER CHAT HISTORY THREAD
		//     (Persistent single reply bar — NEVER duplicates or jumps!)
		// =========================================================================
		private async System.Threading.Tasks.Task SetupAndRenderPlayerConversationAsync(string partnerName, int renderVersion, bool scrollToBottom)
		{
			if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible) return;

			RestoreInputFromFloatingKeyboardBar();
			ClearContainerImmediate(_bottomActionBar);
			_bottomBarFrame.Visible = true;

			// Create EXACTLY ONE reply input box and ONE send button for this conversation!
			_threadReplyInput = new LineEdit
			{
				PlaceholderText = $"Write message to Commander {partnerName}...",
				CustomMinimumSize = new Vector2(300, 42),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_threadReplyInput.AddThemeFontSizeOverride("font_size", 14);
			_threadReplyInput.TextSubmitted += (_) => OnSendThreadReplyPressed();

			Button btnSendReply = new Button
			{
				Text = "📡 SEND",
				CustomMinimumSize = new Vector2(124, 42)
			};
			btnSendReply.AddThemeFontSizeOverride("font_size", 13);
			btnSendReply.Modulate = new Color("#4ADE80");
			btnSendReply.Pressed += OnSendThreadReplyPressed;

			_bottomActionBar.AddChild(_threadReplyInput);
			_bottomActionBar.AddChild(btnSendReply);

			// Register as the active typing input so it floats on top of the mobile keyboard without shrinking the screen!
			_activeTypingInput = _threadReplyInput;
			_activeTypingSendBtn = btnSendReply;

			await PopulatePlayerConversationMessagesOnlyAsync(partnerName, renderVersion, scrollToBottom);
		}

		private async System.Threading.Tasks.Task PopulatePlayerConversationMessagesOnlyAsync(string partnerName, int renderVersion, bool scrollToBottom)
		{
			if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible) return;

			ClearContainerImmediate(_contentStack);

			string myCallsign = (SupabaseService.CachedCommanderName ?? "").Trim();
			var threadMsgs = new List<InboxMessageData>();
			var unreadIdsToMark = new List<long>();

			foreach (var msg in _inboxMessages)
			{
				string cat = NormalizeCategory(msg.category);
				if (cat != "PERSONAL") continue;

				string s = (msg.sender_name ?? "").Trim();
				string r = (msg.recipient_name ?? "").Trim();

				bool isFromPartnerToMe = string.Equals(s, partnerName, StringComparison.OrdinalIgnoreCase) &&
										 string.Equals(r, myCallsign, StringComparison.OrdinalIgnoreCase);
				bool isFromMeToPartner = string.Equals(s, myCallsign, StringComparison.OrdinalIgnoreCase) &&
										 string.Equals(r, partnerName, StringComparison.OrdinalIgnoreCase);

				if (isFromPartnerToMe || isFromMeToPartner)
				{
					threadMsgs.Add(msg);
					if (isFromPartnerToMe && !msg.is_read && msg.id > 0)
					{
						msg.is_read = true;
						unreadIdsToMark.Add(msg.id);
					}
				}
			}

			// Sort thread chronologically (oldest at top -> newest at bottom)
			threadMsgs.Sort((a, b) =>
			{
				DateTimeOffset.TryParse(a.created_at ?? "", out var ta);
				DateTimeOffset.TryParse(b.created_at ?? "", out var tb);
				int cmp = ta.CompareTo(tb);
				return cmp != 0 ? cmp : a.id.CompareTo(b.id);
			});

			// Top Thread Header Bar with Back to Folders Button
			PanelContainer threadHeaderCard = CreateCard(new Color("#00F0FF"));
			VBoxContainer thBox = CreateCardVBox(threadHeaderCard);
			HBoxContainer thRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			thRow.AddThemeConstantOverride("separation", 10);

			Button btnBackFolders = new Button
			{
				Text = "◀ ALL COMMANDERS",
				CustomMinimumSize = new Vector2(158, 36)
			};
			btnBackFolders.AddThemeFontSizeOverride("font_size", 12);
			btnBackFolders.Modulate = new Color("#00F0FF");
			btnBackFolders.Pressed += () =>
			{
				_activeConversationPartner = "";
				_viewRenderVersion++;
				RenderCommanderFoldersView();
			};
			thRow.AddChild(btnBackFolders);

			Label lblPartnerTitle = MakeLabel($"💬  CONVERSATION WITH [{partnerName.ToUpper()}]  ({threadMsgs.Count} MSG)", 14, new Color("#FBBF24"), true);
			lblPartnerTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			thRow.AddChild(lblPartnerTitle);

			thBox.AddChild(thRow);
			_contentStack.AddChild(threadHeaderCard);

			if (threadMsgs.Count == 0)
			{
				PanelContainer emptyThread = CreateCard(new Color("#475569"));
				VBoxContainer etBox = CreateCardVBox(emptyThread);
				etBox.AddChild(MakeLabel($"No messages with Commander {partnerName} yet. Type in the box below to send your first message!", 13, new Color("#CBD5E1"), true));
				_contentStack.AddChild(emptyThread);
			}
			else
			{
				foreach (var m in threadMsgs)
				{
					bool isMe = string.Equals((m.sender_name ?? "").Trim(), myCallsign, StringComparison.OrdinalIgnoreCase);

					HBoxContainer alignRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
					if (isMe)
					{
						Control leftSpacer = new Control { CustomMinimumSize = new Vector2(56, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
						alignRow.AddChild(leftSpacer);
					}

					Color bubbleBorder = isMe ? new Color("#22C55E") : new Color("#38BDF8");
					PanelContainer bubble = new PanelContainer
					{
						SizeFlagsHorizontal = SizeFlags.ExpandFill,
						CustomMinimumSize = new Vector2(250, 0)
					};
					StyleBoxFlat bStyle = new StyleBoxFlat
					{
						BgColor = isMe ? new Color(0.04f, 0.17f, 0.11f, 0.96f) : new Color(0.03f, 0.09f, 0.18f, 0.96f),
						BorderColor = bubbleBorder,
						BorderWidthLeft = isMe ? 1 : 4,
						BorderWidthRight = isMe ? 4 : 1,
						BorderWidthTop = 1,
						BorderWidthBottom = 1,
						CornerRadiusTopLeft = 10,
						CornerRadiusTopRight = 10,
						CornerRadiusBottomLeft = isMe ? 10 : 2,
						CornerRadiusBottomRight = isMe ? 2 : 10
					};
					bubble.AddThemeStyleboxOverride("panel", bStyle);

					VBoxContainer bBox = CreateCardVBox(bubble);
					HBoxContainer metaRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
					metaRow.AddChild(MakeLabel(
						isMe ? $"📤 YOU ({myCallsign.ToUpper()})" : $"📥 COMMANDER {partnerName.ToUpper()}",
						11,
						isMe ? new Color("#4ADE80") : new Color("#38BDF8")
					));
					Label timeLbl = MakeLabel(FormatTimestamp(m.created_at), 11, new Color("#94A3B8"));
					timeLbl.HorizontalAlignment = HorizontalAlignment.Right;
					timeLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					metaRow.AddChild(timeLbl);
					bBox.AddChild(metaRow);

					bBox.AddChild(MakeLabel(m.body ?? "", 14, Colors.White, true));
					alignRow.AddChild(bubble);

					if (!isMe)
					{
						Control rightSpacer = new Control { CustomMinimumSize = new Vector2(56, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
						alignRow.AddChild(rightSpacer);
					}

					_contentStack.AddChild(alignRow);
				}
			}

			// Mark unread messages as read in background without rebuilding the input bar
			if (unreadIdsToMark.Count > 0 && SupabaseService.Instance != null)
			{
				RebuildInboxTopTabsAndBadges();
				_ = SupabaseService.Instance.MarkInboxMessagesReadAsync(unreadIdsToMark);
			}

			if (scrollToBottom)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				if (IsInstanceValid(_scroll) && _scroll.GetVScrollBar() != null)
				{
					_scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
				}
			}
		}

		private async void OnSendThreadReplyPressed()
		{
			if (_threadReplyInput == null || !IsInstanceValid(_threadReplyInput) || string.IsNullOrEmpty(_activeConversationPartner)) return;
			string body = _threadReplyInput.Text.Trim();
			if (string.IsNullOrEmpty(body)) return;

			string partner = _activeConversationPartner;
			int currentVer = _viewRenderVersion;
			_threadReplyInput.Text = "";

			if (SupabaseService.Instance != null)
			{
				await SupabaseService.Instance.SendInboxSignalAsync(
					SupabaseService.CachedCommanderName,
					partner,
					"PERSONAL",
					$"DIRECT COMMS · {SupabaseService.CachedCommanderName.ToUpper()}",
					body
				);
				var latest = await SupabaseService.Instance.FetchInboxMessagesAsync(SupabaseService.CachedCommanderName);
				if (currentVer != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activeConversationPartner != partner) return;
				_inboxMessages = latest;
			}

			if (currentVer == _viewRenderVersion && IsInstanceValid(this) && Visible && _activePrimaryMode == "INBOX" && _activeConversationPartner == partner)
			{
				// Only refresh the message bubbles in _contentStack; NEVER recreate the bottom reply input box!
				await PopulatePlayerConversationMessagesOnlyAsync(partner, currentVer, scrollToBottom: true);
			}
		}

		private async void RefreshActivePlayerThreadQuietlyAsync()
		{
			if (SupabaseService.Instance == null || string.IsNullOrEmpty(_activeConversationPartner)) return;
			int currentVer = _viewRenderVersion;
			string partner = _activeConversationPartner;

			var latest = await SupabaseService.Instance.FetchInboxMessagesAsync(SupabaseService.CachedCommanderName);
			if (currentVer != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "INBOX" || _activeConversationPartner != partner) return;

			if (latest.Count != _inboxMessages.Count)
			{
				_inboxMessages = latest;
				// Only update the message list — the input box stays untouched!
				await PopulatePlayerConversationMessagesOnlyAsync(partner, currentVer, scrollToBottom: true);
			}
		}

		// =========================================================================
		// 1C. TACTICAL LOG CHANNELS (BATTLE, ALLIANCE, INTEL, TRADE, SYSTEM)
		// =========================================================================
		private async System.Threading.Tasks.Task RenderStandardCategoryLogsAsync(string category, int renderVersion)
		{
			if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible) return;

			RestoreInputFromFloatingKeyboardBar();
			_activeTypingInput = null;
			_activeTypingSendBtn = null;

			ClearContainerImmediate(_contentStack);
			ClearContainerImmediate(_bottomActionBar);
			_bottomBarFrame.Visible = false;

			string myCallsign = (SupabaseService.CachedCommanderName ?? "").Trim();
			var unreadIdsToMark = new List<long>();
			int shownCount = 0;

			foreach (var msg in _inboxMessages)
			{
				string title = msg.title ?? "";
				if (title.StartsWith("INCOMING_FLEET:", StringComparison.Ordinal) ||
					title.StartsWith("CANCEL_FLEET:", StringComparison.Ordinal) ||
					title.StartsWith("IMPACT_SYNC:", StringComparison.Ordinal) ||
					title.StartsWith("GARRISON_SYNC:", StringComparison.Ordinal))
				{
					continue;
				}

				string msgCat = NormalizeCategory(msg.category);
				if (msgCat != category) continue;

				// Strict Recipient Filter for BATTLE, ALLIANCE & INTEL logs:
				// Attackers ONLY receive Attack Reports and Defenders ONLY receive Defense Reports!
				if ((category == "BATTLE" || category == "ALLIANCE" || category == "INTEL") && msg.id > 0 &&
					!string.Equals((msg.recipient_name ?? "").Trim(), myCallsign, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (category == "BATTLE" &&
					title.Contains("DEFENSE REPORT", StringComparison.OrdinalIgnoreCase) &&
					string.Equals((msg.sender_name ?? "").Trim(), myCallsign, StringComparison.OrdinalIgnoreCase) &&
					!title.Contains($"({myCallsign})", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				shownCount++;

				if (!msg.is_read && msg.id > 0 && string.Equals((msg.recipient_name ?? "").Trim(), myCallsign, StringComparison.OrdinalIgnoreCase))
				{
					msg.is_read = true;
					unreadIdsToMark.Add(msg.id);
				}

				Color borderCol = category switch
				{
					"BATTLE" => new Color("#EF4444"),
					"TRADE" => new Color("#FBBF24"),
					"ALLIANCE" => new Color("#4ADE80"),
					"INTEL" => new Color("#A855F7"),
					"SYSTEM" => new Color("#38BDF8"),
					_ => new Color("#00F0FF")
				};

				PanelContainer msgCard = CreateCard(borderCol);
				VBoxContainer box = CreateCardVBox(msgCard);

				HBoxContainer topRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				topRow.AddChild(MakeLabel($"FROM: {(msg.sender_name ?? "CENTRAL COMMAND").ToUpper()}", 12, new Color("#FBBF24")));
				Label timeLbl = MakeLabel(FormatTimestamp(msg.created_at), 11, new Color("#94A3B8"));
				timeLbl.HorizontalAlignment = HorizontalAlignment.Right;
				timeLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				topRow.AddChild(timeLbl);
				box.AddChild(topRow);

				box.AddChild(MakeLabel((msg.title ?? "ENCRYPTED SIGNAL").ToUpper(), 15, Colors.White, true));
				box.AddChild(MakeLabel(msg.body ?? "", 13, new Color("#CBD5E1"), true));

				HBoxContainer actRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
				actRow.AddThemeConstantOverride("separation", 10);

				string senderTarget = (msg.sender_name ?? "").Trim();
				if (!string.IsNullOrEmpty(senderTarget) &&
					!string.Equals(senderTarget, "CENTRAL COMMAND", StringComparison.OrdinalIgnoreCase) &&
					!string.Equals(senderTarget, "ORBITAL COMBAT TELEMETRY", StringComparison.OrdinalIgnoreCase) &&
					!string.Equals(senderTarget, "DEEP SPACE RADAR", StringComparison.OrdinalIgnoreCase) &&
					!string.Equals(senderTarget, myCallsign, StringComparison.OrdinalIgnoreCase))
				{
					Button chatBtn = new Button
					{
						Text = $"💬 CHAT WITH {senderTarget.ToUpper()}",
						CustomMinimumSize = new Vector2(150, 34)
					};
					chatBtn.AddThemeFontSizeOverride("font_size", 11);
					chatBtn.Modulate = new Color("#00F0FF");
					chatBtn.Pressed += () => OpenPlayerConversation(senderTarget);
					actRow.AddChild(chatBtn);
				}

				if (msg.id > 0)
				{
					Button delBtn = new Button
					{
						Text = "🗑 PURGE",
						CustomMinimumSize = new Vector2(96, 34)
					};
					delBtn.AddThemeFontSizeOverride("font_size", 12);
					delBtn.Modulate = new Color("#EF4444");
					long capturedId = msg.id;
					delBtn.Pressed += async () =>
					{
						if (SupabaseService.Instance != null)
						{
							await SupabaseService.Instance.DeleteInboxMessageAsync(capturedId);
							OpenCommsInbox(_activeInboxCategory);
						}
					};
					actRow.AddChild(delBtn);
				}

				box.AddChild(actRow);
				_contentStack.AddChild(msgCard);
			}

			if (unreadIdsToMark.Count > 0 && SupabaseService.Instance != null)
			{
				RebuildInboxTopTabsAndBadges();
				await SupabaseService.Instance.MarkInboxMessagesReadAsync(unreadIdsToMark);
			}

			if (shownCount == 0)
			{
				PanelContainer emptyCard = CreateCard(new Color("#475569"));
				VBoxContainer eBox = CreateCardVBox(emptyCard);
				eBox.AddChild(MakeLabel($"[ NO ENCRYPTED SIGNALS IN {category} ARCHIVE ]", 14, new Color("#94A3B8")));
				_contentStack.AddChild(emptyCard);
			}
		}

		private static string NormalizeCategory(string rawCategory)
		{
			string c = (rawCategory ?? "PERSONAL").Trim().ToUpper();
			if (c == "PRIVATE" || c == "SENT") return "PERSONAL";
			if (c == "LOGISTICS") return "TRADE";
			return c;
		}

		private void EnsureDefaultSeedMessages()
		{
			if (!_inboxMessages.Exists(m => NormalizeCategory(m.category) == "SYSTEM"))
			{
				_inboxMessages.Add(new InboxMessageData
				{
					id = -1,
					sender_name = "CENTRAL COMMAND",
					recipient_name = SupabaseService.CachedCommanderName,
					category = "SYSTEM",
					is_read = true,
					title = "WELCOME TO THE SINGULARITY — SECTOR INITIALIZED",
					body = $"Commander {SupabaseService.CachedCommanderName}, your colony at Moon M-{SupabaseService.CachedMoonId} : Slot B-{SupabaseService.CachedBaseSlot:D2} is online. Expand your Helium-3 Distilleries and Orbital Shipyard before the 6-month Aether-Reaper cycle arrives.",
					created_at = DateTimeOffset.UtcNow.ToString("o")
				});
			}

			if (!_inboxMessages.Exists(m => NormalizeCategory(m.category) == "INTEL"))
			{
				_inboxMessages.Add(new InboxMessageData
				{
					id = -2,
					sender_name = "DEEP SPACE RADAR",
					recipient_name = SupabaseService.CachedCommanderName,
					category = "INTEL",
					is_read = true,
					title = "LONG-RANGE ORBITAL TELEMETRY BRIEFING",
					body = "Tactical Note: Moving a fleet to a vacant Moon Slot or an unaligned Commander's base stations your fleet in orbit as a Trap/Ambush defense without surrendering unit ownership. Reinforcing is restricted to Alliance members.",
					created_at = DateTimeOffset.UtcNow.ToString("o")
				});
			}
		}

		// =========================================================================
		// 2. GLOBAL SERVER FREQUENCY (LATEST MESSAGES AT TOP, FLOATING KEYBOARD INPUT BAR)
		// =========================================================================
		public async void OpenGlobalServerChat()
		{
			int myRenderVersion = ++_viewRenderVersion;
			_activePrimaryMode = "CHAT";
			_chatPollTimer = 0.0;
			ClearView();
			Visible = true;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();
			_bottomBarFrame.Visible = true;
			RecenterModal();

			DateTime now = DateTime.UtcNow;
			int moonsYear = now.Year + 122;
			var srvDef = GameData.GetServerDef(SupabaseService.CachedServerId);
			_lblHeader.Text = $"💬  {srvDef.Name} ({srvDef.Id} · {srvDef.Speed:0.#}X) · GLOBAL FREQUENCY CHAT";
			_lblHeader.Modulate = new Color("#4ADE80");
			_lblSubHeader.Text = $"LATEST MESSAGES AT TOP · OLDER FLOWING DOWN  ||  STARDATE {now.Day:D2}/{now.Month:D2}/{moonsYear}";

			Label orderBadge = MakeLabel("📌 FEED ORDER: NEWEST MESSAGES AT TOP ➔ OLDER BELOW", 12, new Color("#00F0FF"), true);
			orderBadge.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			orderBadge.VerticalAlignment = VerticalAlignment.Center;
			_topTabBar.AddChild(orderBadge);

			Button btnRefresh = new Button
			{
				Text = "🔄 REFRESH CHAT",
				CustomMinimumSize = new Vector2(148, 36)
			};
			btnRefresh.AddThemeFontSizeOverride("font_size", 12);
			btnRefresh.Modulate = new Color("#00F0FF");
			btnRefresh.Pressed += RefreshGlobalChatAsync;
			_topTabBar.AddChild(btnRefresh);

			// Create EXACTLY ONE Global Chat Input Field + Send Button
			_chatInput = new LineEdit
			{
				PlaceholderText = $"Transmit message to {srvDef.Name} ({srvDef.Id})...",
				CustomMinimumSize = new Vector2(300, 42),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_chatInput.AddThemeFontSizeOverride("font_size", 14);
			_chatInput.TextSubmitted += (_) => OnSendChatPressed();

			Button sendBtn = new Button
			{
				Text = "📡 SEND",
				CustomMinimumSize = new Vector2(118, 42)
			};
			sendBtn.AddThemeFontSizeOverride("font_size", 14);
			sendBtn.Modulate = new Color("#00F0FF");
			sendBtn.Pressed += OnSendChatPressed;

			_bottomActionBar.AddChild(_chatInput);
			_bottomActionBar.AddChild(sendBtn);

			// Register as the active typing input so ONLY this input bar floats on top of the mobile keyboard!
			_activeTypingInput = _chatInput;
			_activeTypingSendBtn = sendBtn;

			await RefreshGlobalChatFlow(myRenderVersion, scrollToTopForLatest: true);
		}

		private async void RefreshGlobalChatAsync()
		{
			await RefreshGlobalChatFlow(_viewRenderVersion, scrollToTopForLatest: false);
		}

		private async System.Threading.Tasks.Task RefreshGlobalChatFlow(int renderVersion, bool scrollToTopForLatest)
		{
			if (renderVersion != _viewRenderVersion || !Visible || _activePrimaryMode != "CHAT") return;

			if (SupabaseService.Instance != null)
			{
				var fetched = await SupabaseService.Instance.FetchGlobalChatAsync(SupabaseService.CachedServerId);
				if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "CHAT") return;
				_chatMessages = fetched;
			}
			if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "CHAT") return;

			// Guarantee Latest Messages are at the TOP and Older Messages flow down!
			_chatMessages.Sort((a, b) =>
			{
				DateTimeOffset.TryParse(a.created_at ?? "", out var ta);
				DateTimeOffset.TryParse(b.created_at ?? "", out var tb);
				int cmp = tb.CompareTo(ta);
				return cmp != 0 ? cmp : b.id.CompareTo(a.id);
			});

			ClearContainerImmediate(_contentStack);

			if (_chatMessages.Count == 0)
			{
				PanelContainer welcomePod = CreateCard(new Color("#00F0FF"));
				VBoxContainer wBox = CreateCardVBox(welcomePod);
				wBox.AddChild(MakeLabel("SYSTEM RELAY · [STARDATE 2148]", 12, new Color("#00F0FF")));
				wBox.AddChild(MakeLabel($"Global Frequency Channel ({SupabaseService.CachedServerId}) open. Transmit a message below to communicate with all Commanders on this server!", 13, Colors.White, true));
				_contentStack.AddChild(welcomePod);
				return;
			}

			for (int i = 0; i < _chatMessages.Count; i++)
			{
				var m = _chatMessages[i];
				bool isMe = string.Equals(m.commander_name, SupabaseService.CachedCommanderName, StringComparison.OrdinalIgnoreCase);
				bool isNewestTop = (i == 0);
				Color podColor = isMe ? new Color("#22C55E") : (isNewestTop ? new Color("#00F0FF") : new Color("#FBBF24"));

				PanelContainer pod = CreateCard(podColor);
				VBoxContainer pBox = CreateCardVBox(pod);

				HBoxContainer top = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				top.AddThemeConstantOverride("separation", 8);

				string senderTag = $"👨‍🚀 {(m.commander_name ?? "UNKNOWN").ToUpper()}{(isMe ? " (YOU)" : "")}";
				if (isNewestTop) senderTag = "🆕 LATEST · " + senderTag;
				top.AddChild(MakeLabel(senderTag, 12, podColor));

				if (!isMe && !string.IsNullOrWhiteSpace(m.commander_name))
				{
					Button btnDm = new Button
					{
						Text = "📩 DM",
						CustomMinimumSize = new Vector2(58, 24)
					};
					btnDm.AddThemeFontSizeOverride("font_size", 10);
					btnDm.Modulate = new Color("#00F0FF");
					string targetCmdr = m.commander_name;
					btnDm.Pressed += () => OpenPlayerConversation(targetCmdr);
					top.AddChild(btnDm);
				}

				Label tLbl = MakeLabel(FormatTimestamp(m.created_at), 11, new Color("#94A3B8"));
				tLbl.HorizontalAlignment = HorizontalAlignment.Right;
				tLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				top.AddChild(tLbl);
				pBox.AddChild(top);

				pBox.AddChild(MakeLabel(m.message ?? "", 14, Colors.White, true));
				_contentStack.AddChild(pod);
			}

			if (scrollToTopForLatest)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				if (IsInstanceValid(_scroll))
				{
					_scroll.ScrollVertical = 0; // Top = Latest messages!
				}
			}
		}

		private async void OnSendChatPressed()
		{
			if (_chatInput == null || !IsInstanceValid(_chatInput)) return;
			string txt = _chatInput.Text.Trim();
			if (string.IsNullOrEmpty(txt)) return;

			int currentVer = _viewRenderVersion;
			_chatInput.Text = "";
			if (SupabaseService.Instance != null)
			{
				await SupabaseService.Instance.SendGlobalChatAsync(SupabaseService.CachedCommanderName, txt, SupabaseService.CachedServerId);
			}
			await RefreshGlobalChatFlow(currentVer, scrollToTopForLatest: true);
		}

		// =========================================================================
		// 3. ALLIANCE SYSTEM BLUEPRINT & 5-TAB ALLIANCE HUB
		//    - Unlock Requirement: Industrial Core Level 4.
		//    - Alliance HQ Level 4: Unlocks "Join Alliance".
		//    - Alliance HQ Level 10: Unlocks "Create Alliance" with 10 starting members.
		//    - Levels: 20 upgradable levels, each adding +1 member slot (Max Capacity: 20 members).
		//    - Initial State (Unaligned): Displays "Join" and "Create" options if requirements are met.
		//    - Post-Action State (In Alliance): Replaces Join/Create options with the 5-Tab Alliance Hub
		//      (Members, Sciences, War Room, Messaging, Attacks), restricting further join/create actions!
		// =========================================================================
		public void OpenAllianceHQInterface() => OpenAllianceHQInterface(_activeAllianceHubTab);

		public async void OpenAllianceHQInterface(string hubTab)
		{
			int myRenderVersion = ++_viewRenderVersion;
			_activePrimaryMode = "ALLIANCE";
			if (!string.IsNullOrWhiteSpace(hubTab)) _activeAllianceHubTab = hubTab.Trim().ToUpper();
			ClearView();
			Visible = true;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();
			_bottomBarFrame.Visible = false;
			RecenterModal();

			int coreLvl = BaseHUD.Instance != null
				? BaseHUD.Instance.BuildingLevels.GetValueOrDefault("hub_cmd", 1)
				: SupabaseService.CachedBuildingLevels.GetValueOrDefault("hub_cmd", 1);
			int ahqLvl = BaseHUD.Instance != null
				? BaseHUD.Instance.BuildingLevels.GetValueOrDefault("hub_ahq", 0)
				: SupabaseService.CachedBuildingLevels.GetValueOrDefault("hub_ahq", 0);

			string myAlliance = string.IsNullOrWhiteSpace(SupabaseService.CachedAllianceName)
				? "NONE"
				: SupabaseService.CachedAllianceName.Trim().ToUpper();
			bool isAligned = (myAlliance != "NONE");

			// Calculate member slot capacity: Each Alliance HQ level adds +1 member slot (Lvl 10 = 10 starting members, Max Lvl 20 = 20 members)
			int myHqMemberCap = Math.Clamp(ahqLvl, 0, 20);

			if (!isAligned)
			{
				// =====================================================================
				// INITIAL STATE (UNALIGNED): DISPLAY JOIN & CREATE OPTIONS
				// =====================================================================
				_lblHeader.Text = "🛡️  ALLIANCE HQ · COALITION REGISTRY";
				_lblHeader.Modulate = new Color("#38BDF8");
				_lblSubHeader.Text = $"INDUSTRIAL CORE: LVL {coreLvl} (REQ LVL 4)   ·   ALLIANCE HQ: LVL {ahqLvl} / 20 ({myHqMemberCap}/20 MEMBER SLOTS)";

				// Top Blueprint Status & Capacity Overview Card
				bool coreUnlocked = (coreLvl >= 4);
				bool canJoinAlliance = coreUnlocked && (ahqLvl >= 4);
				bool canCreateAlliance = coreUnlocked && (ahqLvl >= 10);

				PanelContainer overviewCard = CreateCard(coreUnlocked ? new Color("#38BDF8") : new Color("#EF4444"));
				VBoxContainer ovBox = CreateCardVBox(overviewCard);
				ovBox.AddChild(MakeLabel("📐  ALLIANCE HQ BLUEPRINT & CAPACITY SPECIFICATIONS", 13, new Color("#38BDF8"), true));
				ovBox.AddChild(MakeLabel(
					$"• Unlock Requirement: Industrial Core Level 4  {(coreUnlocked ? $"[✓ MET: CORE LVL {coreLvl}]" : $"[🔒 LOCKED: CURRENT CORE LVL {coreLvl}]")}\n" +
					$"• Alliance HQ Level: {ahqLvl} / 20  (+1 Member Slot per Level · Max Capacity: 20 Members)\n" +
					$"• Level 4 Unlock: Join Existing Alliance  {(canJoinAlliance ? "[✓ UNLOCKED]" : $"[🔒 REQ ALLIANCE HQ LVL 4 · CURRENT LVL {ahqLvl}]")}\n" +
					$"• Level 10 Unlock: Create New Alliance (10 Starting Members)  {(canCreateAlliance ? "[✓ UNLOCKED]" : $"[🔒 REQ ALLIANCE HQ LVL 10 · CURRENT LVL {ahqLvl}]")}",
					12, new Color("#CBD5E1"), true));
				_contentStack.AddChild(overviewCard);

				// Feedback Banner for Join / Create Validation
				_feedbackLabel = MakeLabel("", 12, new Color("#FBBF24"), true);
				_contentStack.AddChild(_feedbackLabel);

				// OPTION 1: JOIN EXISTING ALLIANCE (Requires Industrial Core Lvl 4 & Alliance HQ Lvl 4+)
				PanelContainer joinCard = CreateCard(canJoinAlliance ? new Color("#22C55E") : new Color("#475569"));
				VBoxContainer jBox = CreateCardVBox(joinCard);
				jBox.AddChild(MakeLabel(
					canJoinAlliance
						? "🤝  JOIN EXISTING ALLIANCE [UNLOCKED — ALLIANCE HQ LVL 4+]"
						: $"🔒  JOIN EXISTING ALLIANCE [REQUIRES INDUSTRIAL CORE LVL 4 & ALLIANCE HQ LVL 4 · CURRENT: CORE {coreLvl}, HQ {ahqLvl}]",
					13,
					canJoinAlliance ? new Color("#4ADE80") : new Color("#94A3B8"),
					true
				));
				jBox.AddChild(MakeLabel(
					"Enter an existing Alliance Tag on this server to join their coalition. Once joined, this screen transforms into the 5-Tab Alliance Hub.",
					12, new Color("#CBD5E1"), true));

				HBoxContainer joinRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				joinRow.AddThemeConstantOverride("separation", 8);
				_joinAllianceInput = new LineEdit
				{
					PlaceholderText = canJoinAlliance ? "Enter Alliance Tag (e.g. STARBORN, TITAN)..." : "Upgrade Core to Lvl 4 & Alliance HQ to Lvl 4 to Join...",
					CustomMinimumSize = new Vector2(200, 40),
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					Editable = canJoinAlliance
				};
				_joinAllianceInput.AddThemeFontSizeOverride("font_size", 13);
				joinRow.AddChild(_joinAllianceInput);

				Button btnJoin = new Button
				{
					Text = canJoinAlliance ? "🤝 JOIN ALLIANCE" : "🔒 LOCKED (HQ LVL 4)",
					CustomMinimumSize = new Vector2(158, 40),
					Disabled = !canJoinAlliance
				};
				btnJoin.AddThemeFontSizeOverride("font_size", 12);
				btnJoin.Modulate = canJoinAlliance ? new Color("#4ADE80") : new Color("#64748B");
				btnJoin.Pressed += () => OnJoinAlliancePressedAsync(myRenderVersion);
				joinRow.AddChild(btnJoin);
				jBox.AddChild(joinRow);
				_contentStack.AddChild(joinCard);

				// OPTION 2: CREATE NEW ALLIANCE (Requires Industrial Core Lvl 4 & Alliance HQ Lvl 10+ -> Starts with 10 Members, +1 per level up to 20!)
				PanelContainer createCard = CreateCard(canCreateAlliance ? new Color("#FBBF24") : new Color("#475569"));
				VBoxContainer cBox = CreateCardVBox(createCard);
				int startingSlots = Math.Clamp(Math.Max(10, ahqLvl), 10, 20);
				cBox.AddChild(MakeLabel(
					canCreateAlliance
						? $"👑  CREATE NEW ALLIANCE [UNLOCKED — {startingSlots} STARTING MEMBER SLOTS]"
						: $"🔒  CREATE NEW ALLIANCE [REQUIRES INDUSTRIAL CORE LVL 4 & ALLIANCE HQ LVL 10 · CURRENT: CORE {coreLvl}, HQ {ahqLvl}]",
					13,
					canCreateAlliance ? new Color("#FBBF24") : new Color("#94A3B8"),
					true
				));
				cBox.AddChild(MakeLabel(
					"Found a new sovereign Alliance at Level 10 with 10 starting member slots. Each Alliance HQ upgrade up to Level 20 adds +1 member slot (Max Capacity: 20 Members).",
					12, new Color("#CBD5E1"), true));

				HBoxContainer createRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				createRow.AddThemeConstantOverride("separation", 8);
				_foundAllianceInput = new LineEdit
				{
					PlaceholderText = canCreateAlliance ? "New Alliance Tag (3-12 chars)..." : "Upgrade Alliance HQ to Level 10 to Create Alliance...",
					CustomMinimumSize = new Vector2(200, 40),
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					Editable = canCreateAlliance
				};
				_foundAllianceInput.AddThemeFontSizeOverride("font_size", 13);
				createRow.AddChild(_foundAllianceInput);

				Button btnCreate = new Button
				{
					Text = canCreateAlliance ? "👑 CREATE ALLIANCE" : "🔒 LOCKED (HQ LVL 10)",
					CustomMinimumSize = new Vector2(168, 40),
					Disabled = !canCreateAlliance
				};
				btnCreate.AddThemeFontSizeOverride("font_size", 12);
				btnCreate.Modulate = canCreateAlliance ? new Color("#FBBF24") : new Color("#64748B");
				btnCreate.Pressed += () => OnCreateAlliancePressedAsync(myRenderVersion);
				createRow.AddChild(btnCreate);
				cBox.AddChild(createRow);
				_contentStack.AddChild(createCard);

				// Live Server Directory of Existing Alliances (So players at Lvl 4+ can see & 1-click join active alliances!)
				if (SupabaseService.Instance != null)
				{
					var allColonies = await SupabaseService.Instance.FetchAllColoniesAsync();
					if (myRenderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "ALLIANCE") return;

					var allianceGroups = new Dictionary<string, (int count, int maxCap, long totalPop)>(StringComparer.OrdinalIgnoreCase);
					foreach (var col in allColonies)
					{
						string tag = (col.AllianceName ?? "NONE").Trim().ToUpper();
						if (tag == "NONE" || string.IsNullOrEmpty(tag)) continue;
						int memberHqCap = Math.Clamp(Math.Max(10, col.AllianceHQLevel), 10, 20);
						if (!allianceGroups.TryGetValue(tag, out var info))
						{
							info = (0, memberHqCap, 0);
						}
						info.count++;
						info.maxCap = Math.Max(info.maxCap, memberHqCap);
						info.totalPop += col.Population;
						allianceGroups[tag] = info;
					}

					if (allianceGroups.Count > 0)
					{
						PanelContainer dirCard = CreateCard(new Color("#00F0FF"));
						VBoxContainer dirBox = CreateCardVBox(dirCard);
						dirBox.AddChild(MakeLabel("🌌  ACTIVE SERVER ALLIANCES (TAP TO JOIN IF HQ LVL 4+):", 12, new Color("#00F0FF"), true));

						foreach (var kv in allianceGroups)
						{
							string aTag = kv.Key;
							int curMembers = kv.Value.count;
							int capMembers = Math.Clamp(kv.Value.maxCap, 10, 20);
							bool isFull = curMembers >= capMembers;

							HBoxContainer aRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
							aRow.AddThemeConstantOverride("separation", 8);
							Label aLbl = MakeLabel(
								$"🛡️ [{aTag}]   ·   Members: {curMembers} / {capMembers}   ·   Total Pop: {kv.Value.totalPop:N0}",
								12, Colors.White, true
							);
							aLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
							aRow.AddChild(aLbl);

							Button btnQuickJoin = new Button
							{
								Text = isFull ? "FULL" : (canJoinAlliance ? "JOIN ➔" : "REQ HQ 4"),
								CustomMinimumSize = new Vector2(96, 30),
								Disabled = !canJoinAlliance || isFull
							};
							btnQuickJoin.AddThemeFontSizeOverride("font_size", 11);
							btnQuickJoin.Modulate = (!canJoinAlliance || isFull) ? new Color("#64748B") : new Color("#4ADE80");
							string capturedTag = aTag;
							btnQuickJoin.Pressed += () =>
							{
								if (_joinAllianceInput != null) _joinAllianceInput.Text = capturedTag;
								OnJoinAlliancePressedAsync(myRenderVersion);
							};
							aRow.AddChild(btnQuickJoin);
							dirBox.AddChild(aRow);
						}
						_contentStack.AddChild(dirCard);
					}
				}
				return;
			}

			// =========================================================================
			// POST-ACTION STATE (ALIGNED): 5-TAB ALLIANCE HUB
			// Replaces Join/Create options with the Alliance Hub, restricting further Join/Create!
			// Tabs: 1. MEMBERS | 2. SCIENCES | 3. WAR ROOM | 4. MESSAGING | 5. ATTACKS
			// =========================================================================
			await RenderAlignedAllianceHubAsync(myAlliance, ahqLvl, myRenderVersion);
		}

		private async void OnJoinAlliancePressedAsync(int renderVersion)
		{
			if (_joinAllianceInput == null) return;
			string tag = _joinAllianceInput.Text.Trim().ToUpper();
			if (tag.Length < 2 || tag == "NONE")
			{
				if (_feedbackLabel != null)
				{
					_feedbackLabel.Text = "⚠️ Please enter a valid Alliance Tag (2-12 characters).";
					_feedbackLabel.Modulate = new Color("#EF4444");
				}
				return;
			}

			// Check capacity of target alliance on this server
			if (SupabaseService.Instance != null)
			{
				var allColonies = await SupabaseService.Instance.FetchAllColoniesAsync();
				int memberCount = 0;
				int maxCap = 10;
				foreach (var c in allColonies)
				{
					if (string.Equals((c.AllianceName ?? "").Trim(), tag, StringComparison.OrdinalIgnoreCase))
					{
						memberCount++;
						maxCap = Math.Max(maxCap, Math.Clamp(c.AllianceHQLevel, 10, 20));
					}
				}

				if (memberCount >= Math.Min(20, maxCap))
				{
					if (_feedbackLabel != null && IsInstanceValid(_feedbackLabel))
					{
						_feedbackLabel.Text = $"⚠️ Alliance [{tag}] is at maximum member capacity ({memberCount}/{Math.Min(20, maxCap)}). Their Founder must upgrade Alliance HQ (+1 slot/level up to 20)!";
						_feedbackLabel.Modulate = new Color("#EF4444");
					}
					return;
				}
			}

			SupabaseService.CachedAllianceName = tag;
			if (BaseHUD.Instance != null)
			{
				// Role 0 = Member when joining an existing alliance
				if (!BaseHUD.Instance.BuildingLevels.ContainsKey("__meta_alliance_role"))
				{
					BaseHUD.Instance.BuildingLevels["__meta_alliance_role"] = 0;
				}
				BaseHUD.Instance.TriggerInstantSave();
			}
			_activeAllianceHubTab = "MEMBERS";
			OpenAllianceHQInterface("MEMBERS");
		}

		private async void OnCreateAlliancePressedAsync(int renderVersion)
		{
			if (_foundAllianceInput == null) return;
			string tag = _foundAllianceInput.Text.Trim().ToUpper();
			if (tag.Length < 3 || tag.Length > 12 || tag == "NONE")
			{
				if (_feedbackLabel != null)
				{
					_feedbackLabel.Text = "⚠️ Alliance Tag must be between 3 and 12 characters.";
					_feedbackLabel.Modulate = new Color("#EF4444");
				}
				return;
			}

			if (SupabaseService.Instance != null)
			{
				var allColonies = await SupabaseService.Instance.FetchAllColoniesAsync();
				if (allColonies.Exists(c => string.Equals((c.AllianceName ?? "").Trim(), tag, StringComparison.OrdinalIgnoreCase)))
				{
					if (_feedbackLabel != null && IsInstanceValid(_feedbackLabel))
					{
						_feedbackLabel.Text = $"⚠️ Alliance [{tag}] already exists on this server! Use 'Join Alliance' instead or choose a unique tag.";
						_feedbackLabel.Modulate = new Color("#EF4444");
					}
					return;
				}
			}

			SupabaseService.CachedAllianceName = tag;
			if (BaseHUD.Instance != null)
			{
				// Role 4 = Supreme Founder when creating an alliance
				BaseHUD.Instance.BuildingLevels["__meta_alliance_role"] = 4;
				BaseHUD.Instance.TriggerInstantSave();
			}
			_activeAllianceHubTab = "MEMBERS";
			OpenAllianceHQInterface("MEMBERS");
		}

		private async System.Threading.Tasks.Task RenderAlignedAllianceHubAsync(string allianceTag, int myAhqLvl, int renderVersion)
		{
			if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible) return;

			// Fetch live roster and incoming attacks IN PARALLEL using cached colony rows for instant zero-lag Alliance Hub opening!
			if (SupabaseService.Instance != null)
			{
				var coloniesTask = SupabaseService.Instance.FetchAllColoniesAsync();
				var attacksTask = SupabaseService.Instance.FetchAllianceIncomingAttacksAsync(allianceTag);
				await System.Threading.Tasks.Task.WhenAll(coloniesTask, attacksTask);

				if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "ALLIANCE") return;

				var allColonies = coloniesTask.Result;
				_cachedAllianceMembers.Clear();
				foreach (var c in allColonies)
				{
					if (string.Equals((c.AllianceName ?? "").Trim(), allianceTag, StringComparison.OrdinalIgnoreCase))
					{
						_cachedAllianceMembers.Add(c);
					}
				}
				_cachedAllianceMembers.Sort((a, b) =>
				{
					int rCmp = b.AllianceRoleCode.CompareTo(a.AllianceRoleCode);
					return rCmp != 0 ? rCmp : b.Population.CompareTo(a.Population);
				});

				_allianceIncomingAttacks = attacksTask.Result;
			}

			// Determine Alliance Max Member Capacity:
			// Starts at 10 members at Level 10 creation, and scales up +1 per highest Alliance HQ level in the coalition up to Max 20!
			int highestAhqInAlliance = Math.Max(10, myAhqLvl);
			foreach (var m in _cachedAllianceMembers)
			{
				if (m.AllianceHQLevel > highestAhqInAlliance) highestAhqInAlliance = m.AllianceHQLevel;
			}
			int allianceMaxCapacity = Math.Clamp(highestAhqInAlliance, 10, 20);
			int currentMemberCount = Math.Max(1, _cachedAllianceMembers.Count);
			int incomingAttackCount = _allianceIncomingAttacks.Count;

			_lblHeader.Text = $"🛡️  ALLIANCE HUB — [{allianceTag}]";
			_lblHeader.Modulate = new Color("#00F0FF");
			_lblSubHeader.Text = $"MEMBERS: {currentMemberCount} / {allianceMaxCapacity} (MAX 20)   ·   HQ LVL: {myAhqLvl}/20   ·   {(incomingAttackCount > 0 ? $"🚨 {incomingAttackCount} INCOMING ATTACK(S)!" : "SECTOR SECURE")}";

			// Build the 5 Blueprint Alliance Hub Tabs:
			// 1. Members | 2. Sciences | 3. War Room | 4. Messaging | 5. Attacks
			ClearContainerImmediate(_topTabBar);
			var hubTabs = new[]
			{
				new { Code = "MEMBERS",   Label = $"👥 MEMBERS ({currentMemberCount}/{allianceMaxCapacity})" },
				new { Code = "SCIENCES",  Label = "🔬 SCIENCES" },
				new { Code = "WAR_ROOM",  Label = "💬 WAR ROOM" },
				new { Code = "MESSAGING", Label = "📢 MESSAGING" },
				new { Code = "ATTACKS",   Label = incomingAttackCount > 0 ? $"🚨 ATTACKS ({incomingAttackCount})" : "🛡️ ATTACKS" }
			};

			foreach (var tab in hubTabs)
			{
				bool isSelected = (_activeAllianceHubTab == tab.Code);
				bool isAttackAlert = (tab.Code == "ATTACKS" && incomingAttackCount > 0);

				Button tBtn = new Button
				{
					Text = tab.Label,
					CustomMinimumSize = new Vector2(96, 40),
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					ClipText = true
				};
				tBtn.AddThemeFontSizeOverride("font_size", 14);

				StyleBoxFlat tStyle = new StyleBoxFlat
				{
					BgColor = isSelected
						? new Color(0.05f, 0.22f, 0.36f, 0.98f)
						: (isAttackAlert ? new Color(0.32f, 0.06f, 0.09f, 0.95f) : new Color(0.03f, 0.07f, 0.14f, 0.92f)),
					BorderColor = isAttackAlert
						? new Color("#EF4444")
						: (isSelected ? new Color("#00F0FF") : new Color(0f, 0.94f, 1f, 0.28f))
				};
				tStyle.SetBorderWidthAll(isSelected || isAttackAlert ? 2 : 1);
				tStyle.SetCornerRadiusAll(6);
				tBtn.AddThemeStyleboxOverride("normal", tStyle);
				tBtn.Modulate = isAttackAlert
					? new Color("#FCA5A5")
					: (isSelected ? new Color("#00F0FF") : new Color("#CBD5E1"));

				string capturedTab = tab.Code;
				tBtn.Pressed += () => OpenAllianceHQInterface(capturedTab);
				_topTabBar.AddChild(tBtn);
			}

			if (_activeAllianceHubTab == "MEMBERS")
			{
				RenderAllianceMembersTab(allianceTag, currentMemberCount, allianceMaxCapacity, myAhqLvl);
			}
			else if (_activeAllianceHubTab == "SCIENCES")
			{
				await RenderAllianceSciencesTabAsync(allianceTag, renderVersion);
			}
			else if (_activeAllianceHubTab == "WAR_ROOM")
			{
				await SetupAndRenderAllianceWarRoomTabAsync(allianceTag, renderVersion);
			}
			else if (_activeAllianceHubTab == "MESSAGING")
			{
				await RenderAllianceMessagingTabAsync(allianceTag, renderVersion);
			}
			else if (_activeAllianceHubTab == "ATTACKS")
			{
				RenderAllianceAttacksTab(allianceTag);
			}
		}

		// =========================================================================
		// HUB TAB 1: MEMBERS (ROSTER, POPULATION LEVELS & ASSIGNED OFFICER POSITIONS)
		// =========================================================================
		private void RenderAllianceMembersTab(string allianceTag, int currentMembers, int maxCapacity, int myAhqLvl)
		{
			RestoreInputFromFloatingKeyboardBar();
			_activeTypingInput = null;
			_activeTypingSendBtn = null;
			ClearContainerImmediate(_contentStack);
			ClearContainerImmediate(_bottomActionBar);
			_bottomBarFrame.Visible = true;

			long totalAlliancePop = 0;
			foreach (var m in _cachedAllianceMembers) totalAlliancePop += m.Population;

			int myRoleCode = BaseHUD.Instance != null
				? BaseHUD.Instance.BuildingLevels.GetValueOrDefault("__meta_alliance_role", 0)
				: 0;
			string myRoleTitle = GetOfficerPositionTitle(myRoleCode);

			// Coalition Summary Banner
			PanelContainer summaryCard = CreateCard(new Color("#00F0FF"));
			VBoxContainer sumBox = CreateCardVBox(summaryCard);
			sumBox.AddChild(MakeLabel(
				$"🛡️  [{allianceTag}] SOVEREIGN ROSTER   ·   CAPACITY: {currentMembers} / {maxCapacity} MEMBERS (MAX 20)",
				13, new Color("#00F0FF"), true
			));
			sumBox.AddChild(MakeLabel(
				$"Combined Coalition Population: {totalAlliancePop:N0}   |   Your Officer Position: {myRoleTitle}\n" +
				$"Blueprint Rule: Alliance HQ has 20 upgradable levels, each adding +1 member slot up to 20 Max Members.",
				11, new Color("#CBD5E1"), true
			));
			_contentStack.AddChild(summaryCard);

			// Officer Position Selector for Local Commander
			PanelContainer roleAssignCard = CreateCard(new Color("#FBBF24"));
			VBoxContainer roleBox = CreateCardVBox(roleAssignCard);
			roleBox.AddChild(MakeLabel("🎖️  ASSIGN OFFICER POSITION (COALITION COMMAND HIERARCHY):", 12, new Color("#FBBF24"), true));

			HBoxContainer roleBtnsRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			roleBtnsRow.AddThemeConstantOverride("separation", 6);
			var roles = new[]
			{
				new { Code = 4, Title = "👑 LEADER" },
				new { Code = 3, Title = "⚔️ GENERAL" },
				new { Code = 2, Title = "🔬 SCIENTIST" },
				new { Code = 1, Title = "📡 DIPLOMAT" },
				new { Code = 0, Title = "🛡️ MEMBER" }
			};

			foreach (var r in roles)
			{
				bool isMyRole = (myRoleCode == r.Code);
				Button rBtn = new Button
				{
					Text = r.Title,
					CustomMinimumSize = new Vector2(80, 32),
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					ClipText = true
				};
				rBtn.AddThemeFontSizeOverride("font_size", 10);
				rBtn.Modulate = isMyRole ? new Color("#FDE047") : new Color("#94A3B8");
				int capCode = r.Code;
				rBtn.Pressed += () =>
				{
					if (BaseHUD.Instance != null)
					{
						BaseHUD.Instance.BuildingLevels["__meta_alliance_role"] = capCode;
						BaseHUD.Instance.TriggerInstantSave();
					}
					OpenAllianceHQInterface("MEMBERS");
				};
				roleBtnsRow.AddChild(rBtn);
			}
			roleBox.AddChild(roleBtnsRow);
			_contentStack.AddChild(roleAssignCard);

			// Member Roster Cards (Showing Callsign, Assigned Officer Position, Population Level, Core Level, Coordinates & Quick Actions)
			for (int i = 0; i < _cachedAllianceMembers.Count; i++)
			{
				var mem = _cachedAllianceMembers[i];
				int roleCode = mem.IsMe ? myRoleCode : mem.AllianceRoleCode;
				if (!mem.IsMe && i == 0 && roleCode == 0) roleCode = 4; // Highest ranked member defaults to Leader if unassigned
				string officerTitle = GetOfficerPositionTitle(roleCode);
				Color cardBorder = mem.IsMe ? new Color("#22C55E") : (roleCode >= 3 ? new Color("#FBBF24") : new Color("#38BDF8"));

				PanelContainer memCard = CreateCard(cardBorder);
				VBoxContainer mBox = CreateCardVBox(memCard);

				HBoxContainer topRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				topRow.AddThemeConstantOverride("separation", 8);

				Label nameLbl = MakeLabel(
					$"#{i + 1}  {mem.CommanderName.ToUpper()}{(mem.IsMe ? " (YOU)" : "")}   ·   [{officerTitle}]",
					13,
					mem.IsMe ? new Color("#4ADE80") : new Color("#FBBF24"),
					true
				);
				nameLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				topRow.AddChild(nameLbl);

				Label coordLbl = MakeLabel($"🪐 M-{mem.MoonId}:B-{mem.BaseSlot:D2}", 12, new Color("#00F0FF"));
				topRow.AddChild(coordLbl);
				mBox.AddChild(topRow);

				HBoxContainer statsRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				statsRow.AddThemeConstantOverride("separation", 8);

				Label popLbl = MakeLabel(
					$"👥 Population: {mem.Population:N0}   |   🏛️ Core Lvl: {mem.IndustrialCoreLevel}   |   🛡️ Alliance HQ Lvl: {Math.Max(1, mem.AllianceHQLevel)}",
					12,
					new Color("#CBD5E1"),
					true
				);
				popLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				statsRow.AddChild(popLbl);

				if (!mem.IsMe)
				{
					string targetCmdr = mem.CommanderName;
					int targetMoon = mem.MoonId;

					Button btnMsg = new Button
					{
						Text = "💬 MESSAGE",
						CustomMinimumSize = new Vector2(92, 30)
					};
					btnMsg.AddThemeFontSizeOverride("font_size", 10);
					btnMsg.Modulate = new Color("#00F0FF");
					btnMsg.Pressed += () => OpenPlayerConversation(targetCmdr);
					statsRow.AddChild(btnMsg);

					Button btnReinforce = new Button
					{
						Text = "🛡️ REINFORCE",
						CustomMinimumSize = new Vector2(102, 30)
					};
					btnReinforce.AddThemeFontSizeOverride("font_size", 10);
					btnReinforce.Modulate = new Color("#4ADE80");
					btnReinforce.Pressed += () =>
					{
						MoonView.SelectedMoonToView = targetMoon;
						CloseModal();
						string moonTarget = ResourceLoader.Exists("res://Scripts/MoonView.tscn") ? "res://Scripts/MoonView.tscn" : "res://Scenes/MoonView.tscn";
						GetTree().ChangeSceneToFile(moonTarget);
					};
					statsRow.AddChild(btnReinforce);
				}

				mBox.AddChild(statsRow);
				_contentStack.AddChild(memCard);
			}

			// Bottom Action Bar: Refresh Roster & Leave Alliance Option
			Button btnRefreshRoster = new Button
			{
				Text = "🔄 REFRESH ROSTER",
				CustomMinimumSize = new Vector2(160, 40),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			btnRefreshRoster.AddThemeFontSizeOverride("font_size", 12);
			btnRefreshRoster.Modulate = new Color("#00F0FF");
			btnRefreshRoster.Pressed += () => OpenAllianceHQInterface("MEMBERS");
			_bottomActionBar.AddChild(btnRefreshRoster);

			Button btnLeaveAlliance = new Button
			{
				Text = "🚪 LEAVE ALLIANCE",
				CustomMinimumSize = new Vector2(160, 40)
			};
			btnLeaveAlliance.AddThemeFontSizeOverride("font_size", 12);
			btnLeaveAlliance.Modulate = new Color("#EF4444");
			btnLeaveAlliance.Pressed += () =>
			{
				SupabaseService.CachedAllianceName = "NONE";
				if (BaseHUD.Instance != null)
				{
					BaseHUD.Instance.BuildingLevels.Remove("__meta_alliance_role");
					BaseHUD.Instance.TriggerInstantSave();
				}
				OpenAllianceHQInterface("MEMBERS");
			};
			_bottomActionBar.AddChild(btnLeaveAlliance);
		}

		private static string GetOfficerPositionTitle(int roleCode)
		{
			return roleCode switch
			{
				4 => "👑 SUPREME LEADER (R5)",
				3 => "⚔️ WAR GENERAL (R4)",
				2 => "🔬 CHIEF SCIENTIST (R3)",
				1 => "📡 DIPLOMAT (R2)",
				_ => "🛡️ COALITION MEMBER (R1)"
			};
		}

		// =========================================================================
		// HUB TAB 2: SCIENCES (COALITION RESEARCH TREE, SHARED CONTRIBUTIONS & 1-HOUR OFFICER UPGRADES)
		// - Featured Blueprint Science #1: PLANETARY SHIELD DEFLECTION MATRIX (Levels 1-10, +1% Defender Casualty Mitigation per Level = +10% Max)
		// - Completes the 35% Defender Casualty Mitigation Cap:
		//   * Planetary Shield Building (Lvl 1-10): +10%
		//   * Research Directorate First Research (Lvl 1-20): +20%
		//   * Alliance Science (Lvl 1-10): +10% (Capped at 35% total)
		// - All Alliance Members can contribute resources together toward the expensive coalition pool!
		// - Once 100% funded, ONLY the Alliance Leader (R5) or Assigned Officers (R2/R3/R4) may press UPGRADE, which takes 1 Hour (3,600s)!
		// =========================================================================
		private async System.Threading.Tasks.Task RenderAllianceSciencesTabAsync(string allianceTag, int renderVersion)
		{
			if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible) return;

			RestoreInputFromFloatingKeyboardBar();
			_activeTypingInput = null;
			_activeTypingSendBtn = null;
			_allianceScienceLiveTimerLabels.Clear();
			ClearContainerImmediate(_contentStack);
			ClearContainerImmediate(_bottomActionBar);
			_bottomBarFrame.Visible = true;

			if (SupabaseService.Instance != null)
			{
				var fetchedSciences = await SupabaseService.Instance.FetchAllianceSharedSciencesAsync(allianceTag);
				if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "ALLIANCE" || _activeAllianceHubTab != "SCIENCES") return;
				_allianceSharedSciences = fetchedSciences;
			}

			int myRoleCode = BaseHUD.Instance != null
				? BaseHUD.Instance.BuildingLevels.GetValueOrDefault("__meta_alliance_role", 0)
				: 0;
			// If local player is the highest ranked / founder in the roster and role is 0, treat as Leader (4)
			if (myRoleCode == 0 && _cachedAllianceMembers.Count > 0 && _cachedAllianceMembers[0].IsMe)
			{
				myRoleCode = 4;
			}
			bool isLeaderOrOfficer = (myRoleCode >= 1); // R5 Leader (4), R4 General (3), R3 Scientist (2), R2 Diplomat (1)
			string myRoleTitle = GetOfficerPositionTitle(myRoleCode);

			int myShdBldLvl = BaseHUD.Instance != null ? Math.Clamp(BaseHUD.Instance.BuildingLevels.GetValueOrDefault("hub_shd", 0), 0, 10) : 0;
			int myRshShieldLvl = BaseHUD.Instance != null ? Math.Clamp(BaseHUD.Instance.BuildingLevels.GetValueOrDefault(GameMath.ResearchDirectorateShieldKey, 0), 0, 20) : 0;
			int curAllianceShieldLvl = _allianceSharedSciences.TryGetValue(GameMath.AllianceShieldScienceKey, out var shdNode) ? shdNode.CurrentLevel : 0;
			var mit = GameMath.GetDefenderCasualtyMitigationBreakdown(myShdBldLvl, myRshShieldLvl, curAllianceShieldLvl);

			// Header Banner Explaining Coalition Contribution & 1-Hour Officer Upgrade Rule
			PanelContainer introCard = CreateCard(new Color("#A855F7"));
			VBoxContainer introBox = CreateCardVBox(introCard);
			introBox.AddChild(MakeLabel(
				$"🔬  [{allianceTag}] COALITION RESEARCH TREE   ·   YOUR RANK: {myRoleTitle}",
				13, new Color("#C084FC"), true
			));
			introBox.AddChild(MakeLabel(
				$"• Coalition Contribution Rule: All alliance members contribute resources together to fill each expensive Science level's pool.\n" +
				$"• Command Authorization Rule: Once a Science pool reaches 100%, ONLY the Alliance Leader or Assigned Officers may press UPGRADE (⏱ 1 Hour / 3,600s Upgrade Duration).\n" +
				$"• Active Defender Casualty Mitigation: {mit.cappedTotalPct}% / 35% Cap (Planetary Shield Lvl {myShdBldLvl}: +{mit.shieldBuildingPct}%  ·  Research Directorate Lvl {myRshShieldLvl}: +{mit.researchDirectoratePct}%  ·  Alliance Science Lvl {curAllianceShieldLvl}: +{mit.allianceSciencePct}%)",
				11, new Color("#CBD5E1"), true
			));
			_contentStack.AddChild(introCard);

			var scienceDefs = new[]
			{
				new
				{
					Key = GameMath.AllianceShieldScienceKey,
					Name = "🛡️ PLANETARY SHIELD CASUALTY MITIGATION MATRIX (PRIMARY BLUEPRINT SCIENCE)",
					MaxLvl = 10,
					BonusPerLvl = "+1% Planetary Shield Defender Casualty Mitigation per Level (+10% Max toward 35% Cap)",
					Accent = new Color("#00F0FF")
				},
				new
				{
					Key = "sci_rally",
					Name = "🚀 WARP VECTOR SYNCHRONIZATION",
					MaxLvl = 20,
					BonusPerLvl = "+2.5% Alliance Fleet & Reinforcement Speed per Level",
					Accent = new Color("#38BDF8")
				},
				new
				{
					Key = "sci_weapons",
					Name = "⚔️ COALITION PLASMA TARGETING",
					MaxLvl = 20,
					BonusPerLvl = "+2.0% Fleet Attack Power in Sector Raids per Level",
					Accent = new Color("#EF4444")
				},
				new
				{
					Key = "sci_shields",
					Name = "🛡️ ORBITAL GARRISON PHALANX",
					MaxLvl = 20,
					BonusPerLvl = "+2.5% Garrison & Allied Defense Rating per Level",
					Accent = new Color("#4ADE80")
				},
				new
				{
					Key = "sci_econ",
					Name = "⚡ QUANTUM EXTRACTION LOGISTICS",
					MaxLvl = 20,
					BonusPerLvl = "+3.0% Resource Yield & Trade Aid Capacity per Level",
					Accent = new Color("#FBBF24")
				},
				new
				{
					Key = "sci_siege",
					Name = "💥 STRUCTURAL REPAIR NANITES",
					MaxLvl = 20,
					BonusPerLvl = "-2.0% Building Repair Resource Cost per Level",
					Accent = new Color("#C084FC")
				}
			};

			foreach (var def in scienceDefs)
			{
				if (!_allianceSharedSciences.TryGetValue(def.Key, out var node))
				{
					var reqs = GameMath.GetAllianceSciencePoolRequirement(def.Key, 0);
					node = new SupabaseService.AllianceScienceSharedNode
					{
						ScienceKey = def.Key,
						CurrentLevel = 0,
						MaxLevel = def.MaxLvl,
						RequiredE = reqs.reqE,
						RequiredI = reqs.reqI,
						RequiredT = reqs.reqT,
						RequiredH3 = reqs.reqH3
					};
				}

				bool isMax = node.CurrentLevel >= node.MaxLevel;
				bool isPrimaryShieldSci = (def.Key == GameMath.AllianceShieldScienceKey);

				PanelContainer sCard = CreateCard(def.Accent);
				VBoxContainer sBox = CreateCardVBox(sCard);

				HBoxContainer topRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				topRow.AddThemeConstantOverride("separation", 8);

				Label titleLbl = MakeLabel(
					$"{def.Name}   ·   LEVEL {node.CurrentLevel} / {node.MaxLevel}",
					13,
					def.Accent,
					true
				);
				titleLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				topRow.AddChild(titleLbl);

				string statusBadgeText = isMax
					? "✓ MAX LEVEL"
					: (node.IsUpgradingNow
						? "⚙️ UPGRADING (1H)"
						: (node.IsFullyFunded ? "⚡ 100% FUNDED — READY!" : "📦 COLLECTING CONTRIBUTIONS"));
				Label badgeLbl = MakeLabel(
					statusBadgeText,
					11,
					isMax || node.IsFullyFunded ? new Color("#4ADE80") : (node.IsUpgradingNow ? new Color("#FBBF24") : new Color("#7DD3FC"))
				);
				topRow.AddChild(badgeLbl);
				sBox.AddChild(topRow);

				string effectText = isPrimaryShieldSci
					? $"Effect: {def.BonusPerLvl}   |   Current Alliance Bonus: +{node.CurrentLevel}% / +10% Max"
					: $"Effect: {def.BonusPerLvl}";
				sBox.AddChild(MakeLabel(effectText, 12, Colors.White, true));

				if (isMax)
				{
					sBox.AddChild(MakeLabel(
						$"✓ MAXIMUM COALITION SCIENCE LEVEL ACHIEVED (LEVEL {node.MaxLevel} / {node.MaxLevel})",
						12, new Color("#4ADE80"), true
					));
				}
				else if (node.IsUpgradingNow)
				{
					Label timerLbl = MakeLabel(
						$"⏱ 1-HOUR UPGRADE IN PROGRESS (TO LVL {node.CurrentLevel + 1}/{node.MaxLevel}): {GameMath.FormatTime(node.RemainingUpgradeSeconds)} REMAINING",
						13, new Color("#FBBF24"), true
					);
					_allianceScienceLiveTimerLabels[def.Key] = timerLbl;
					sBox.AddChild(timerLbl);
				}
				else
				{
					// Coalition Resource Pool Progress Line
					long totalReq = Math.Max(1L, node.RequiredE + node.RequiredI + node.RequiredT + node.RequiredH3);
					long totalDon = node.ContributedE + node.ContributedI + node.ContributedT + node.ContributedH3;
					int pctFunded = (int)Math.Clamp(Math.Round((totalDon / (double)totalReq) * 100.0), 0, 100);

					sBox.AddChild(MakeLabel(
						$"Coalition Pool for Level {node.CurrentLevel + 1} ({pctFunded}% Funded — ⏱ 1h 00m Upgrade Time):\n" +
						$"⚡ Power: {node.ContributedE:N0} / {node.RequiredE:N0}   |   ⛏️ Iron: {node.ContributedI:N0} / {node.RequiredI:N0}   |   💎 Titanium: {node.ContributedT:N0} / {node.RequiredT:N0}   |   ⛽ H3: {node.ContributedH3:N0} / {node.RequiredH3:N0}",
						11,
						node.IsFullyFunded ? new Color("#4ADE80") : new Color("#CBD5E1"),
						true
					));

					HBoxContainer actRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
					actRow.AddThemeConstantOverride("separation", 8);

					string capKey = def.Key;
					int capCurLvl = node.CurrentLevel;

					// Member Contribution Buttons (+10% Pool Slice or +25% Pool Slice)
					if (!node.IsFullyFunded)
					{
						Button btnDonate10 = new Button
						{
							Text = "🤝 CONTRIBUTE 10% TO POOL",
							CustomMinimumSize = new Vector2(170, 34),
							SizeFlagsHorizontal = SizeFlags.ExpandFill,
							ClipText = true
						};
						btnDonate10.AddThemeFontSizeOverride("font_size", 11);
						btnDonate10.Modulate = new Color("#38BDF8");
						btnDonate10.Pressed += () =>
						{
							if (SupabaseService.ContributeToAllianceSciencePool(capKey, capCurLvl, 0.10))
							{
								_ = RenderAllianceSciencesTabAsync(allianceTag, _viewRenderVersion);
							}
						};
						actRow.AddChild(btnDonate10);

						Button btnDonate25 = new Button
						{
							Text = "🤝 CONTRIBUTE 25% TO POOL",
							CustomMinimumSize = new Vector2(170, 34),
							SizeFlagsHorizontal = SizeFlags.ExpandFill,
							ClipText = true
						};
						btnDonate25.AddThemeFontSizeOverride("font_size", 11);
						btnDonate25.Modulate = new Color("#00F0FF");
						btnDonate25.Pressed += () =>
						{
							if (SupabaseService.ContributeToAllianceSciencePool(capKey, capCurLvl, 0.25))
							{
								_ = RenderAllianceSciencesTabAsync(allianceTag, _viewRenderVersion);
							}
						};
						actRow.AddChild(btnDonate25);
					}

					// Leader / Assigned Officer Upgrade Button (Requires 100% Pool + Leader/Officer Rank, starts 1-Hour Upgrade!)
					bool canPressUpgrade = node.IsFullyFunded && isLeaderOrOfficer;
					string upgBtnText = !node.IsFullyFunded
						? $"🔒 POOL AT {pctFunded}% (NEEDS 100%)"
						: (!isLeaderOrOfficer
							? "🔒 LEADER / OFFICER ONLY"
							: $"⚡ START 1-HOUR UPGRADE TO LVL {capCurLvl + 1}");

					Button btnOfficerUpgrade = new Button
					{
						Text = upgBtnText,
						CustomMinimumSize = new Vector2(210, 34),
						SizeFlagsHorizontal = SizeFlags.ExpandFill,
						Disabled = !canPressUpgrade,
						ClipText = true
					};
					btnOfficerUpgrade.AddThemeFontSizeOverride("font_size", 11);
					btnOfficerUpgrade.Modulate = canPressUpgrade ? new Color("#4ADE80") : new Color("#94A3B8");
					btnOfficerUpgrade.Pressed += () =>
					{
						if (!canPressUpgrade) return;
						if (SupabaseService.StartAllianceScienceOneHourUpgrade(capKey, capCurLvl))
						{
							_ = RenderAllianceSciencesTabAsync(allianceTag, _viewRenderVersion);
						}
					};
					actRow.AddChild(btnOfficerUpgrade);

					sBox.AddChild(actRow);
				}

				_contentStack.AddChild(sCard);
			}

			Button btnRefreshSciences = new Button
			{
				Text = "🔄 SYNC ALLIANCE MEMBER CONTRIBUTIONS & TIMERS",
				CustomMinimumSize = new Vector2(220, 40),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			btnRefreshSciences.AddThemeFontSizeOverride("font_size", 12);
			btnRefreshSciences.Modulate = new Color("#C084FC");
			btnRefreshSciences.Pressed += () => _ = RenderAllianceSciencesTabAsync(allianceTag, _viewRenderVersion);
			_bottomActionBar.AddChild(btnRefreshSciences);
		}

		// =========================================================================
		// HUB TAB 3: WAR ROOM (ALLIANCE-WIDE CHAT — ISOLATED TO YOUR ALLIANCE)
		// =========================================================================
		private async System.Threading.Tasks.Task SetupAndRenderAllianceWarRoomTabAsync(string allianceTag, int renderVersion)
		{
			if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible) return;

			RestoreInputFromFloatingKeyboardBar();
			ClearContainerImmediate(_bottomActionBar);
			_bottomBarFrame.Visible = true;

			_allianceWarRoomInput = new LineEdit
			{
				PlaceholderText = $"Transmit encrypted War Room message to [{allianceTag}] members...",
				CustomMinimumSize = new Vector2(280, 42),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_allianceWarRoomInput.AddThemeFontSizeOverride("font_size", 14);
			_allianceWarRoomInput.TextSubmitted += (_) => OnSendAllianceWarRoomChatPressed(allianceTag);

			Button btnSendWarChat = new Button
			{
				Text = "📡 SEND",
				CustomMinimumSize = new Vector2(118, 42)
			};
			btnSendWarChat.AddThemeFontSizeOverride("font_size", 13);
			btnSendWarChat.Modulate = new Color("#4ADE80");
			btnSendWarChat.Pressed += () => OnSendAllianceWarRoomChatPressed(allianceTag);

			_bottomActionBar.AddChild(_allianceWarRoomInput);
			_bottomActionBar.AddChild(btnSendWarChat);

			// Float only the input bar on top of the mobile keyboard when typing!
			_activeTypingInput = _allianceWarRoomInput;
			_activeTypingSendBtn = btnSendWarChat;

			await RefreshAllianceWarRoomFlowAsync(renderVersion, allianceTag, scrollToTop: true);
		}

		private async System.Threading.Tasks.Task RefreshAllianceWarRoomFlowAsync(int renderVersion, string allianceTag, bool scrollToTop)
		{
			if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "ALLIANCE" || _activeAllianceHubTab != "WAR_ROOM") return;

			string warRoomChannelId = $"{SupabaseService.CachedServerId}_ALLY_{allianceTag}";
			if (SupabaseService.Instance != null)
			{
				var fetched = await SupabaseService.Instance.FetchGlobalChatAsync(warRoomChannelId);
				if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "ALLIANCE" || _activeAllianceHubTab != "WAR_ROOM") return;
				_allianceWarRoomMessages = fetched;
			}

			_allianceWarRoomMessages.Sort((a, b) =>
			{
				DateTimeOffset.TryParse(a.created_at ?? "", out var ta);
				DateTimeOffset.TryParse(b.created_at ?? "", out var tb);
				int cmp = tb.CompareTo(ta);
				return cmp != 0 ? cmp : b.id.CompareTo(a.id);
			});

			ClearContainerImmediate(_contentStack);

			PanelContainer hdrCard = CreateCard(new Color("#4ADE80"));
			VBoxContainer hdrBox = CreateCardVBox(hdrCard);
			hdrBox.AddChild(MakeLabel($"💬  [{allianceTag}] ENCRYPTED ALLIANCE WAR ROOM CHAT (LATEST AT TOP)", 13, new Color("#4ADE80"), true));
			hdrBox.AddChild(MakeLabel("Private real-time tactical frequency visible strictly to members of your Alliance.", 11, new Color("#94A3B8"), true));
			_contentStack.AddChild(hdrCard);

			if (_allianceWarRoomMessages.Count == 0)
			{
				PanelContainer emptyCard = CreateCard(new Color("#475569"));
				VBoxContainer eBox = CreateCardVBox(emptyCard);
				eBox.AddChild(MakeLabel($"No transmissions in the [{allianceTag}] War Room yet. Type below to rally your coalition!", 13, new Color("#CBD5E1"), true));
				_contentStack.AddChild(emptyCard);
				return;
			}

			for (int i = 0; i < _allianceWarRoomMessages.Count; i++)
			{
				var m = _allianceWarRoomMessages[i];
				bool isMe = string.Equals(m.commander_name, SupabaseService.CachedCommanderName, StringComparison.OrdinalIgnoreCase);
				Color podCol = isMe ? new Color("#22C55E") : new Color("#38BDF8");

				PanelContainer pod = CreateCard(podCol);
				VBoxContainer pBox = CreateCardVBox(pod);

				HBoxContainer top = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				top.AddChild(MakeLabel($"🛡️ {(m.commander_name ?? "ALLY").ToUpper()}{(isMe ? " (YOU)" : "")}", 12, podCol));
				Label tLbl = MakeLabel(FormatTimestamp(m.created_at), 11, new Color("#94A3B8"));
				tLbl.HorizontalAlignment = HorizontalAlignment.Right;
				tLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				top.AddChild(tLbl);
				pBox.AddChild(top);

				pBox.AddChild(MakeLabel(m.message ?? "", 14, Colors.White, true));
				_contentStack.AddChild(pod);
			}

			if (scrollToTop)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				if (IsInstanceValid(_scroll)) _scroll.ScrollVertical = 0;
			}
		}

		private async void OnSendAllianceWarRoomChatPressed(string allianceTag)
		{
			if (_allianceWarRoomInput == null || !IsInstanceValid(_allianceWarRoomInput)) return;
			string txt = _allianceWarRoomInput.Text.Trim();
			if (string.IsNullOrEmpty(txt)) return;

			int currentVer = _viewRenderVersion;
			_allianceWarRoomInput.Text = "";
			string warRoomChannelId = $"{SupabaseService.CachedServerId}_ALLY_{allianceTag}";
			if (SupabaseService.Instance != null)
			{
				await SupabaseService.Instance.SendGlobalChatAsync(SupabaseService.CachedCommanderName, txt, warRoomChannelId);
			}
			await RefreshAllianceWarRoomFlowAsync(currentVer, allianceTag, scrollToTop: true);
		}

		// =========================================================================
		// HUB TAB 4: MESSAGING (GROUP ANNOUNCEMENTS & ALLIANCE BROADCASTS)
		// =========================================================================
		private async System.Threading.Tasks.Task RenderAllianceMessagingTabAsync(string allianceTag, int renderVersion)
		{
			if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible) return;

			RestoreInputFromFloatingKeyboardBar();
			ClearContainerImmediate(_contentStack);
			ClearContainerImmediate(_bottomActionBar);
			_bottomBarFrame.Visible = true;

			// Compose Group Announcement Card
			PanelContainer composeCard = CreateCard(new Color("#FBBF24"));
			VBoxContainer cBox = CreateCardVBox(composeCard);
			cBox.AddChild(MakeLabel($"📢  BROADCAST GROUP ANNOUNCEMENT TO ALL [{allianceTag}] MEMBERS", 13, new Color("#FBBF24"), true));
			cBox.AddChild(MakeLabel(
				"Group announcements are delivered directly to the Alliance Inbox of every member in your coalition AND pinned to the Alliance Bulletin Board below.",
				11, new Color("#CBD5E1"), true
			));

			_allianceAnnouncementTitleInput = new LineEdit
			{
				PlaceholderText = "Announcement Subject / Operation Order (e.g. RALLY AT MOON M-105)...",
				CustomMinimumSize = new Vector2(0, 38),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_allianceAnnouncementTitleInput.AddThemeFontSizeOverride("font_size", 13);
			cBox.AddChild(_allianceAnnouncementTitleInput);
			_contentStack.AddChild(composeCard);

			// Bottom Input Bar for Announcement Message Body + Broadcast Button (Floats above keyboard on mobile!)
			_allianceAnnouncementBodyInput = new LineEdit
			{
				PlaceholderText = $"Type group announcement message for all [{allianceTag}] members...",
				CustomMinimumSize = new Vector2(260, 42),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_allianceAnnouncementBodyInput.AddThemeFontSizeOverride("font_size", 13);
			_allianceAnnouncementBodyInput.TextSubmitted += (_) => OnBroadcastAllianceAnnouncementPressed(allianceTag);

			Button btnBroadcast = new Button
			{
				Text = "📢 BROADCAST",
				CustomMinimumSize = new Vector2(138, 42)
			};
			btnBroadcast.AddThemeFontSizeOverride("font_size", 12);
			btnBroadcast.Modulate = new Color("#FBBF24");
			btnBroadcast.Pressed += () => OnBroadcastAllianceAnnouncementPressed(allianceTag);

			_bottomActionBar.AddChild(_allianceAnnouncementBodyInput);
			_bottomActionBar.AddChild(btnBroadcast);

			_activeTypingInput = _allianceAnnouncementBodyInput;
			_activeTypingSendBtn = btnBroadcast;

			// Fetch & Display Past Alliance Announcements
			string bulletinChannelId = $"{SupabaseService.CachedServerId}_ANN_{allianceTag}";
			var announcements = new List<ChatMessageData>();
			if (SupabaseService.Instance != null)
			{
				announcements = await SupabaseService.Instance.FetchGlobalChatAsync(bulletinChannelId);
				if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "ALLIANCE" || _activeAllianceHubTab != "MESSAGING") return;
			}

			if (announcements.Count == 0)
			{
				PanelContainer emptyCard = CreateCard(new Color("#475569"));
				VBoxContainer eBox = CreateCardVBox(emptyCard);
				eBox.AddChild(MakeLabel($"No group announcements posted for [{allianceTag}] yet.", 13, new Color("#94A3B8"), true));
				_contentStack.AddChild(emptyCard);
			}
			else
			{
				foreach (var ann in announcements)
				{
					PanelContainer aCard = CreateCard(new Color("#FBBF24"));
					VBoxContainer aBox = CreateCardVBox(aCard);

					HBoxContainer top = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
					top.AddChild(MakeLabel($"📢 COMMAND BULLETIN · BY {(ann.commander_name ?? "OFFICER").ToUpper()}", 12, new Color("#FBBF24")));
					Label tLbl = MakeLabel(FormatTimestamp(ann.created_at), 11, new Color("#94A3B8"));
					tLbl.HorizontalAlignment = HorizontalAlignment.Right;
					tLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					top.AddChild(tLbl);
					aBox.AddChild(top);

					aBox.AddChild(MakeLabel(ann.message ?? "", 13, Colors.White, true));
					_contentStack.AddChild(aCard);
				}
			}
		}

		private async void OnBroadcastAllianceAnnouncementPressed(string allianceTag)
		{
			if (_allianceAnnouncementBodyInput == null || !IsInstanceValid(_allianceAnnouncementBodyInput)) return;
			string body = _allianceAnnouncementBodyInput.Text.Trim();
			if (string.IsNullOrEmpty(body)) return;

			string subj = _allianceAnnouncementTitleInput != null && IsInstanceValid(_allianceAnnouncementTitleInput) && !string.IsNullOrWhiteSpace(_allianceAnnouncementTitleInput.Text)
				? _allianceAnnouncementTitleInput.Text.Trim().ToUpper()
				: $"[{allianceTag}] COALITION ANNOUNCEMENT";

			_allianceAnnouncementBodyInput.Text = "";
			if (_allianceAnnouncementTitleInput != null && IsInstanceValid(_allianceAnnouncementTitleInput))
				_allianceAnnouncementTitleInput.Text = "";

			int currentVer = _viewRenderVersion;
			string bulletinChannelId = $"{SupabaseService.CachedServerId}_ANN_{allianceTag}";

			if (SupabaseService.Instance != null)
			{
				// 1. Save to the Alliance Bulletin Feed
				await SupabaseService.Instance.SendGlobalChatAsync(
					SupabaseService.CachedCommanderName,
					$"[{subj}]\n{body}",
					bulletinChannelId
				);

				// 2. Deliver directly to every Alliance member's ALLIANCE inbox tab so they get a red alert badge!
				foreach (var mem in _cachedAllianceMembers)
				{
					if (mem == null || string.IsNullOrWhiteSpace(mem.CommanderName)) continue;
					await SupabaseService.Instance.SendInboxSignalAsync(
						SupabaseService.CachedCommanderName,
						mem.CommanderName,
						"ALLIANCE",
						$"📢 [{allianceTag}] {subj}",
						body
					);
				}
			}

			if (currentVer == _viewRenderVersion && IsInstanceValid(this) && Visible && _activePrimaryMode == "ALLIANCE" && _activeAllianceHubTab == "MESSAGING")
			{
				await RenderAllianceMessagingTabAsync(allianceTag, currentVer);
			}
		}

		// =========================================================================
		// HUB TAB 5: ATTACKS (INCOMING ATTACKS MONITOR AGAINST ALLIANCE MEMBERS)
		// =========================================================================
		private void RenderAllianceAttacksTab(string allianceTag)
		{
			RestoreInputFromFloatingKeyboardBar();
			_activeTypingInput = null;
			_activeTypingSendBtn = null;
			ClearContainerImmediate(_contentStack);
			ClearContainerImmediate(_bottomActionBar);
			_bottomBarFrame.Visible = true;

			int atkCount = _allianceIncomingAttacks.Count;
			PanelContainer headerCard = CreateCard(atkCount > 0 ? new Color("#EF4444") : new Color("#4ADE80"));
			VBoxContainer hBox = CreateCardVBox(headerCard);
			hBox.AddChild(MakeLabel(
				atkCount > 0
					? $"🚨  COALITION DEFENSE RADAR — {atkCount} INCOMING HOSTILE ATTACK(S) DETECTED!"
					: $"🛡️  COALITION DEFENSE RADAR — NO INCOMING ATTACKS AGAINST [{allianceTag}] MEMBERS",
				13,
				atkCount > 0 ? new Color("#FCA5A5") : new Color("#4ADE80"),
				true
			));
			hBox.AddChild(MakeLabel(
				"Monitors all active hostile Raid fleets targeting any member of your Alliance across the galaxy, detailing Attacker Callsign, Origin Coordinates, Target Member Coordinates, Fleet Size, and ETA.",
				11, new Color("#CBD5E1"), true
			));
			_contentStack.AddChild(headerCard);

			if (atkCount == 0)
			{
				PanelContainer clearCard = CreateCard(new Color("#38BDF8"));
				VBoxContainer cBox = CreateCardVBox(clearCard);
				cBox.AddChild(MakeLabel("✓ ALL ALLIANCE SECTORS SECURE", 14, new Color("#4ADE80")));
				cBox.AddChild(MakeLabel(
					"No hostile fleets are currently locked onto any member of your Alliance. If an enemy launches a Raid against any coalition member, their attacker callsign and coordinates will appear here immediately.",
					12, new Color("#CBD5E1"), true
				));
				_contentStack.AddChild(clearCard);
			}
			else
			{
				foreach (var atk in _allianceIncomingAttacks)
				{
					PanelContainer atkCard = CreateCard(new Color("#EF4444"));
					VBoxContainer aBox = CreateCardVBox(atkCard);

					HBoxContainer row1 = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
					row1.AddThemeConstantOverride("separation", 8);
					Label lblAlert = MakeLabel(
						$"🚨 UNDER ATTACK: {atk.DefenderName} ({atk.DefenderCoord})   ⬅   ATTACKER: {atk.AttackerName} ({atk.AttackerOriginCoord})",
						13, new Color("#FCA5A5"), true
					);
					lblAlert.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					row1.AddChild(lblAlert);

					Label lblEta = MakeLabel($"⏱ IMPACT ETA: {GameMath.FormatTime((int)Math.Ceiling(atk.EtaSecondsLeft))}", 12, new Color("#FDE047"));
					row1.AddChild(lblEta);
					aBox.AddChild(row1);

					string laserInfo = atk.LaserCannonCount > 0
						? $"💥 {atk.LaserCannonCount:N0} Laser Cannons (-{atk.LaserCannonCount:N0} HP ➔ {atk.TargetBuildingName})"
						: "No Laser Cannons (0 Building HP Damage)";
					aBox.AddChild(MakeLabel(
						$"• Attacker: {atk.AttackerName} [{atk.AttackerAlliance}]  |  Launched From: {atk.AttackerOriginCoord}\n" +
						$"• Defending Member: {atk.DefenderName} ({atk.DefenderBaseName})  |  Target Sector: {atk.DefenderCoord}\n" +
						$"• Hostile Fleet Strength: {atk.TotalUnits:N0} Warships  |  Siege Telemetry: {laserInfo}",
						12, Colors.White, true
					));

					HBoxContainer btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
					btnRow.AddThemeConstantOverride("separation", 8);

					int defMoon = atk.DefenderMoonId;
					GameMath.TryParseSectorCoordinate(atk.AttackerOriginCoord, out int atkMoon, out _);

					if (atkMoon >= 1 && atkMoon <= 1000)
					{
						Button btnViewAttacker = new Button
						{
							Text = $"🎯 VIEW ATTACKER ({atk.AttackerOriginCoord})",
							CustomMinimumSize = new Vector2(180, 34)
						};
						btnViewAttacker.AddThemeFontSizeOverride("font_size", 11);
						btnViewAttacker.Modulate = new Color("#EF4444");
						btnViewAttacker.Pressed += () =>
						{
							MoonView.SelectedMoonToView = atkMoon;
							CloseModal();
							string moonTarget = ResourceLoader.Exists("res://Scripts/MoonView.tscn") ? "res://Scripts/MoonView.tscn" : "res://Scenes/MoonView.tscn";
							GetTree().ChangeSceneToFile(moonTarget);
						};
						btnRow.AddChild(btnViewAttacker);
					}

					Button btnReinforceAlly = new Button
					{
						Text = $"🛡️ REINFORCE ALLY ({atk.DefenderCoord})",
						CustomMinimumSize = new Vector2(180, 34)
					};
					btnReinforceAlly.AddThemeFontSizeOverride("font_size", 11);
					btnReinforceAlly.Modulate = new Color("#4ADE80");
					btnReinforceAlly.Pressed += () =>
					{
						MoonView.SelectedMoonToView = defMoon;
						CloseModal();
						string moonTarget = ResourceLoader.Exists("res://Scripts/MoonView.tscn") ? "res://Scripts/MoonView.tscn" : "res://Scenes/MoonView.tscn";
						GetTree().ChangeSceneToFile(moonTarget);
					};
					btnRow.AddChild(btnReinforceAlly);

					aBox.AddChild(btnRow);
					_contentStack.AddChild(atkCard);
				}
			}

			Button btnScanRadar = new Button
			{
				Text = "🔄 SCAN COALITION SECTORS FOR INCOMING ATTACKS",
				CustomMinimumSize = new Vector2(240, 40),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			btnScanRadar.AddThemeFontSizeOverride("font_size", 12);
			btnScanRadar.Modulate = new Color("#00F0FF");
			btnScanRadar.Pressed += () => OpenAllianceHQInterface("ATTACKS");
			_bottomActionBar.AddChild(btnScanRadar);
		}

		private async System.Threading.Tasks.Task RefreshAllianceAttacksFlowAsync(int renderVersion, string allianceTag)
		{
			if (SupabaseService.Instance == null || renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible) return;
			var latestAtks = await SupabaseService.Instance.FetchAllianceIncomingAttacksAsync(allianceTag);
			if (renderVersion != _viewRenderVersion || !IsInstanceValid(this) || !Visible || _activePrimaryMode != "ALLIANCE" || _activeAllianceHubTab != "ATTACKS") return;
			_allianceIncomingAttacks = latestAtks;
			RenderAllianceAttacksTab(allianceTag);
		}

		private PanelContainer CreateCard(Color borderColor)
		{
			PanelContainer p = new PanelContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			StyleBoxFlat s = new StyleBoxFlat
			{
				BgColor = new Color(0.06f, 0.13f, 0.24f, 0.98f), // Bright Fleet Station High-Visibility Card Backdrop
				BorderColor = new Color(borderColor.R, borderColor.G, borderColor.B, 0.92f),
				BorderWidthLeft = 4,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8
			};
			p.AddThemeStyleboxOverride("panel", s);
			return p;
		}

		private VBoxContainer CreateCardVBox(PanelContainer card)
		{
			MarginContainer m = new MarginContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			m.AddThemeConstantOverride("margin_left", 14);
			m.AddThemeConstantOverride("margin_right", 14);
			m.AddThemeConstantOverride("margin_top", 10);
			m.AddThemeConstantOverride("margin_bottom", 10);

			VBoxContainer v = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			v.AddThemeConstantOverride("separation", 6);
			m.AddChild(v);
			card.AddChild(m);
			return v;
		}

		private Label MakeLabel(string text, int fontSize, Color color, bool wrap = false)
		{
			Label l = new Label
			{
				Text = text,
				Modulate = color,
				AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
				SizeFlagsHorizontal = wrap ? SizeFlags.ExpandFill : SizeFlags.Fill,
				MouseFilter = MouseFilterEnum.Ignore
			};
			int boostedSize = fontSize <= 14 ? fontSize + 3 : (fontSize <= 18 ? fontSize + 2 : fontSize);
			l.AddThemeFontSizeOverride("font_size", boostedSize);
			l.AddThemeColorOverride("font_outline_color", Colors.Black);
			l.AddThemeConstantOverride("outline_size", 2);
			return l;
		}

		private static string FormatTimestamp(string iso)
		{
			if (string.IsNullOrEmpty(iso)) return "RECENT";
			if (DateTimeOffset.TryParse(iso, out var dto))
			{
				return dto.ToLocalTime().ToString("dd/MM HH:mm");
			}
			return "RECENT";
		}

		public void CloseModal()
		{
			_viewRenderVersion++;
			RestoreInputFromFloatingKeyboardBar();
			Visible = false;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();
			MoonView.Instance?.NotifyExternalStateUpdated(refreshCloudOccupants: true);
			GalaxyView.Instance?.QueueRedraw();
		}
	}
}
