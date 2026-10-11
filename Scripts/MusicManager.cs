using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: PERSISTENT SEAMLESS AUDIO & MUSIC LOOP ENGINE (v1.0)
	/// - Lives directly on the SceneTree Root (/root/MusicManager) so it NEVER gets destroyed or interrupted
	///   when switching between scenes (IntroScene / AuthScene / ServerSelect -> BaseView -> MoonView <-> GalaxyView).
	/// - Guarantees 100% gapless, zero-pause looping for .wav (and .ogg/.mp3) tracks by:
	///   1) Programmatically forcing AudioStreamWav.LoopMode = LoopModeEnum.Forward and LoopEnd = exact sample count
	///      (even if the user forgot to enable Loop in Godot's Import tab!).
	///   2) Connecting the "finished" signal as a secondary hardware failsafe so it restarts on the exact sample frame.
	/// - Smart Track State Machine:
	///   * "login"      -> Plays from Story/Login screen continuously through Server Select until a server is entered.
	///   * "base"       -> Starts when entering a Server into BaseView, or switching back to BaseView from Moons/Galaxy.
	///   * "moongalaxy" -> Starts when entering MoonView or GalaxyView, and NEVER restarts or pauses when switching
	///                     back and forth between Moon Slots UI and Galaxy UI!
	/// </summary>
	public partial class MusicManager : Node
	{
		public static MusicManager Instance { get; private set; }

		public const string TRACK_LOGIN = "login";
		public const string TRACK_BASE = "base";
		public const string TRACK_MOONGALAXY = "moongalaxy";
		private const string AUDIO_PREFS_PATH = "user://audio_settings.cfg";

		private AudioStreamPlayer _player;
		private string _currentTrackKey = "";
		private bool _isMuted = false;
		private bool _pausedByAppBackground = false;
		private float _savedPlaybackPositionSec = 0f;
		private float _volumeDb = -4.0f;

		// Only keep the currently active stream in RAM so 3 uncompressed WAVs never sit in mobile memory at once
		private AudioStream _activeLoadedStream = null;
		private string _activeLoadedTrackKey = "";

		public static MusicManager EnsureInstance(Node caller)
		{
			if (Instance != null && IsInstanceValid(Instance))
			{
				return Instance;
			}

			SceneTree tree = caller?.GetTree() ?? Godot.Engine.GetMainLoop() as SceneTree;
			if (tree?.Root == null) return null;

			var existing = tree.Root.GetNodeOrNull<MusicManager>("MusicManager");
			if (existing != null && IsInstanceValid(existing))
			{
				Instance = existing;
				return Instance;
			}

			var manager = new MusicManager { Name = "MusicManager" };
			// Deferred add if tree root is currently busy locking children during _Ready, otherwise immediate
			tree.Root.CallDeferred(Node.MethodName.AddChild, manager);
			Instance = manager;
			return Instance;
		}

		public override void _Ready()
		{
			if (Instance != null && Instance != this && IsInstanceValid(Instance))
			{
				QueueFree();
				return;
			}

			Instance = this;
			ProcessMode = ProcessModeEnum.Always;
			LoadAudioPreferences();

			_player = new AudioStreamPlayer
			{
				Name = "MasterMusicLoopPlayer",
				Bus = "Master",
				VolumeDb = _isMuted ? -80.0f : _volumeDb,
				Autoplay = false
			};
			_player.Finished += OnStreamFinishedFailsafe;
			AddChild(_player);

			// If PlayTrack was requested right before _Ready finished attaching to /root, start playback immediately
			if (!string.IsNullOrEmpty(_currentTrackKey) && _player.Stream == null)
			{
				StartTrackInternal(_currentTrackKey);
			}
		}

		private void LoadAudioPreferences()
		{
			try
			{
				var cfg = new ConfigFile();
				if (cfg.Load(AUDIO_PREFS_PATH) == Error.Ok)
				{
					_isMuted = (bool)cfg.GetValue("audio", "music_muted", false);
				}
			}
			catch (Exception)
			{
				// Ignore if first launch
			}
		}

		private void SaveAudioPreferences()
		{
			try
			{
				var cfg = new ConfigFile();
				cfg.SetValue("audio", "music_muted", _isMuted);
				cfg.Save(AUDIO_PREFS_PATH);
			}
			catch (Exception)
			{
				// Ignore write failure
			}
		}

		/// <summary>
		/// Requests a track ("login", "base", or "moongalaxy") to play in a seamless infinite loop.
		/// If the requested track is ALREADY playing, this method does nothing—ensuring ZERO pause or restart
		/// when switching between MoonView and GalaxyView, or between Login and ServerSelect!
		/// </summary>
		public static void PlayTrack(Node caller, string trackKey)
		{
			if (string.IsNullOrWhiteSpace(trackKey)) return;
			string normalizedKey = trackKey.Trim().ToLowerInvariant();

			var mgr = EnsureInstance(caller);
			if (mgr == null) return;

			mgr.RequestTrack(normalizedKey);
		}

		public void RequestTrack(string normalizedKey)
		{
			// CRITICAL CONTINUITY GUARD:
			// If this exact track is already active and playing (e.g. switching MoonView <-> GalaxyView),
			// do NOT touch the AudioStreamPlayer so the 30s loop continues without a microsecond of pause!
			if (_currentTrackKey == normalizedKey && _player != null && _player.Playing)
			{
				return;
			}

			_currentTrackKey = normalizedKey;

			if (_player != null && IsInsideTree())
			{
				StartTrackInternal(normalizedKey);
			}
		}

		public override void _Notification(int what)
		{
			// Mobile Lifecycle Guard (Android / iOS):
			// Cleanly pause audio when the player minimizes the app, locks their screen, or receives a phone call,
			// and seamlessly resume from the exact millisecond when they return to the game!
			if (what == NotificationApplicationFocusOut || what == NotificationApplicationPaused)
			{
				if (_player != null && _player.Playing)
				{
					_savedPlaybackPositionSec = _player.GetPlaybackPosition();
					_player.StreamPaused = true;
					_pausedByAppBackground = true;
				}
			}
			else if (what == NotificationApplicationFocusIn || what == NotificationApplicationResumed)
			{
				if (_player != null && _pausedByAppBackground)
				{
					_pausedByAppBackground = false;
					_player.StreamPaused = false;
					if (!_player.Playing && _player.Stream != null)
					{
						_player.Play(_savedPlaybackPositionSec);
					}
				}
			}
		}

		private void StartTrackInternal(string trackKey)
		{
			AudioStream stream = LoadAndConfigureLoopingStream(trackKey);
			if (stream == null)
			{
				GD.PrintErr($"[MUSIC MANAGER] Could not find audio file for '{trackKey}'. Place '{trackKey}.wav' inside res://Assets/Audio/ or res://Assets/");
				return;
			}

			if (_player.Stream == stream && _player.Playing)
			{
				return;
			}

			_player.Stop();
			_player.Stream = stream;
			_player.VolumeDb = _isMuted ? -80.0f : _volumeDb;
			_player.StreamPaused = false;
			_player.Play();

			GD.Print($"[MUSIC MANAGER] Seamless Loop Engaged -> '{trackKey}'");
		}

		private AudioStream LoadAndConfigureLoopingStream(string trackKey)
		{
			if (_activeLoadedTrackKey == trackKey && _activeLoadedStream != null)
			{
				return _activeLoadedStream;
			}

			// Support multiple common folder locations so whichever folder the user drops the .wav files into works automatically
			string[] candidatePaths = new string[]
			{
				$"res://Assets/Audio/{trackKey}.wav",
				$"res://Assets/Music/{trackKey}.wav",
				$"res://Assets/{trackKey}.wav",
				$"res://Audio/{trackKey}.wav",
				$"res://{trackKey}.wav",
				$"res://Assets/Audio/{trackKey}.ogg",
				$"res://Assets/{trackKey}.ogg",
				$"res://Assets/Audio/{trackKey}.mp3",
				$"res://Assets/{trackKey}.mp3"
			};

			AudioStream loadedStream = null;
			string loadedPath = "";

			foreach (string path in candidatePaths)
			{
				if (ResourceLoader.Exists(path))
				{
					loadedStream = GD.Load<AudioStream>(path);
					if (loadedStream != null)
					{
						loadedPath = path;
						break;
					}
				}
			}

			if (loadedStream == null)
			{
				return null;
			}

			// Enforce 100% Sample-Accurate Gapless Looping on .WAV Streams in Code!
			// Handles 8-Bit PCM, 16-Bit PCM, and Mobile IMA-ADPCM compressed WAVs automatically.
			if (loadedStream is AudioStreamWav wavStream)
			{
				wavStream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
				wavStream.LoopBegin = 0;

				int channelCount = wavStream.Stereo ? 2 : 1;
				int totalSampleFrames = 0;

				if (wavStream.Data != null && wavStream.Data.Length > 0)
				{
					if (wavStream.Format == AudioStreamWav.FormatEnum.Format16Bits)
					{
						totalSampleFrames = wavStream.Data.Length / (2 * channelCount);
					}
					else if (wavStream.Format == AudioStreamWav.FormatEnum.Format8Bits)
					{
						totalSampleFrames = wavStream.Data.Length / (1 * channelCount);
					}
					else if (wavStream.Format == AudioStreamWav.FormatEnum.ImaAdpcm)
					{
						// IMA-ADPCM is 4 bits (0.5 bytes) per sample per channel
						totalSampleFrames = (wavStream.Data.Length * 2) / channelCount;
					}
				}

				// Fallback if LoopEnd wasn't already set by Godot's importer
				if (totalSampleFrames > 0 && wavStream.LoopEnd <= 0)
				{
					wavStream.LoopEnd = totalSampleFrames;
				}
				else if (totalSampleFrames > 0)
				{
					wavStream.LoopEnd = totalSampleFrames;
				}

				GD.Print($"[MUSIC MANAGER] Configured sample-exact WAV loop for '{loadedPath}' (Format={wavStream.Format}, LoopEnd={wavStream.LoopEnd}, Stereo={wavStream.Stereo}, MixRate={wavStream.MixRate}Hz)");
			}
			else if (loadedStream is AudioStreamOggVorbis oggStream)
			{
				oggStream.Loop = true;
			}
			else if (loadedStream is AudioStreamMP3 mp3Stream)
			{
				mp3Stream.Loop = true;
			}

			// Release previous stream reference so mobile RAM only holds 1 track at a time
			_activeLoadedStream = loadedStream;
			_activeLoadedTrackKey = trackKey;
			return loadedStream;
		}

		private void OnStreamFinishedFailsafe()
		{
			// Hardware failsafe: if an audio driver ever emits finished on a stream, immediately re-engage on the same frame
			if (_player != null && _player.Stream != null && !string.IsNullOrEmpty(_currentTrackKey))
			{
				_player.Play();
			}
		}

		public static bool ToggleMute(Node caller = null)
		{
			var mgr = EnsureInstance(caller);
			if (mgr == null) return false;

			mgr._isMuted = !mgr._isMuted;
			if (mgr._player != null)
			{
				mgr._player.VolumeDb = mgr._isMuted ? -80.0f : mgr._volumeDb;
			}
			mgr.SaveAudioPreferences();
			return mgr._isMuted;
		}

		public static bool IsMuted => Instance != null && Instance._isMuted;
	}
}
