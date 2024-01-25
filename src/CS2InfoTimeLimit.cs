using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;

namespace K4ryuuInfoTimeLimit
{
	public sealed class PluginConfig : BasePluginConfig
	{
		[JsonPropertyName("block-voice-chat")]
		public bool BlockVoiceChat { get; set; } = true;

		[JsonPropertyName("block-voice-after-seconds")]
		public int BlockVoiceChatTime { get; set; } = 5;

		[JsonPropertyName("block-chat")]
		public bool BlockChat { get; set; } = false;

		[JsonPropertyName("block-chat-after-seconds")]
		public int BlockChatTime { get; set; } = 10;

		[JsonPropertyName("immune-permissions")]
		public List<string> ImmunePermissions { get; set; } = new List<string>
		{
			"@myplugin/wont-mute-permission",
			"#myplugin/wont-mute-group",
			"wont-mute-override"
		};

		[JsonPropertyName("chat-notifications")]
		public bool ChatNotifications { get; set; } = true;

		[JsonPropertyName("ConfigVersion")]
		public override int Version { get; set; } = 2;
	}

	[MinimumApiVersion(153)]
	public sealed partial class InfoTimeLimitPlugin : BasePlugin, IPluginConfig<PluginConfig>
	{
		public override string ModuleName => "CS2 InfoTimeLimit";
		public override string ModuleVersion => "1.0.1";
		public override string ModuleAuthor => "K4ryuu";

		public required PluginConfig Config { get; set; } = new PluginConfig();

		public void OnConfigParsed(PluginConfig config)
		{
			if (config.Version < Config.Version)
			{
				base.Logger.LogWarning("Configuration version mismatch (Expected: {0} | Current: {1})", this.Config.Version, config.Version);
			}

			this.Config = config;
		}

		public override void Load(bool hotReload)
		{
			AddCommandListener("say", OnCommandSay);
			AddCommandListener("say_team", OnCommandSay);

			RegisterListener<Listeners.OnMapEnd>(() =>
			{
				List<CCSPlayerController> players = Utilities.GetPlayers();

				foreach (CCSPlayerController target in players)
				{
					if (target is null || !target.IsValid || !target.PlayerPawn.IsValid || target.IsBot || target.IsHLTV)
						continue;

					if (mutedPlayers.Contains(target.Slot))
					{
						target.VoiceFlags = VoiceFlags.Normal;
						mutedPlayers.Remove(target.Slot);
					}

					if (gaggedPlayers.Contains(target.Slot))
					{
						gaggedPlayers.Remove(target.Slot);
					}
				}
			});
		}

		private List<int> mutedPlayers = new List<int>();
		private List<int> gaggedPlayers = new List<int>();

		[GameEventHandler]
		public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
		{
			CCSPlayerController player = @event.Userid;

			if (player is null || !player.IsValid || !player.PlayerPawn.IsValid || player.IsBot || player.IsHLTV || PlayerHaveImmunity(player))
				return HookResult.Continue;

			int playerSlot = player.Slot;

			if (Config.BlockVoiceChat && !player.VoiceFlags.HasFlag(VoiceFlags.Muted))
			{
				AddTimer(Config.BlockVoiceChatTime > 0 ? Config.BlockVoiceChatTime : 0.1f, () =>
				{
					CCSPlayerController player = Utilities.GetPlayerFromSlot(playerSlot);

					if (player is null || !player.IsValid || !player.PlayerPawn.IsValid || player.IsBot || player.IsHLTV)
						return;

					player.VoiceFlags = VoiceFlags.Muted;
					mutedPlayers.Add(playerSlot);

					player.PrintToChat($" {Localizer["phrases.prefix"]} {Localizer["phrases.text-muted"]}");
				}, TimerFlags.STOP_ON_MAPCHANGE);
			}

			if (Config.BlockChat)
			{
				AddTimer(Config.BlockChatTime > 0 ? Config.BlockChatTime : 0.1f, () =>
				{
					CCSPlayerController player = Utilities.GetPlayerFromSlot(playerSlot);

					if (player is null || !player.IsValid || !player.PlayerPawn.IsValid || player.IsBot || player.IsHLTV)
						return;

					gaggedPlayers.Add(playerSlot);

					player.PrintToChat($" {Localizer["phrases.prefix"]} {Localizer["phrases.text-gagged"]}");
				}, TimerFlags.STOP_ON_MAPCHANGE);
			}

			return HookResult.Continue;
		}

		[GameEventHandler]
		public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
		{
			CCSPlayerController player = @event.Userid;

			if (player is null || !player.IsValid || !player.PlayerPawn.IsValid || player.IsBot || player.IsHLTV)
				return HookResult.Continue;

			if (mutedPlayers.Contains(player.Slot) && gaggedPlayers.Contains(player.Slot))
			{
				player.PrintToChat($" {Localizer["phrases.prefix"]} {Localizer["phrases.text-unall"]}");
			}
			else if (mutedPlayers.Contains(player.Slot) && !gaggedPlayers.Contains(player.Slot))
			{
				player.PrintToChat($" {Localizer["phrases.prefix"]} {Localizer["phrases.text-unmuted"]}");
			}
			else if (!mutedPlayers.Contains(player.Slot) && gaggedPlayers.Contains(player.Slot))
			{
				player.PrintToChat($" {Localizer["phrases.prefix"]} {Localizer["phrases.text-ungagged"]}");
			}

			if (mutedPlayers.Contains(player.Slot))
			{
				player.VoiceFlags = VoiceFlags.Normal;
				mutedPlayers.Remove(player.Slot);
			}

			if (gaggedPlayers.Contains(player.Slot))
			{
				gaggedPlayers.Remove(player.Slot);
			}

			return HookResult.Continue;
		}

		private HookResult OnCommandSay(CCSPlayerController? player, CommandInfo info)
		{
			if (player is null || !player.IsValid || !player.PlayerPawn.IsValid || player.IsBot || player.IsHLTV || info.GetArg(1).Length == 0)
				return HookResult.Continue;

			if (gaggedPlayers.Contains(player.Slot))
				return HookResult.Handled;

			return HookResult.Continue;
		}

		public bool PlayerHaveImmunity(CCSPlayerController player)
		{
			bool hasImmunity = false;

			foreach (string checkPermission in Config.ImmunePermissions)
			{
				switch (checkPermission[0])
				{
					case '@':
						if (AdminManager.PlayerHasPermissions(player, checkPermission))
							hasImmunity = true;
						break;
					case '#':
						if (AdminManager.PlayerInGroup(player, checkPermission))
							hasImmunity = true;
						break;
					default:
						if (AdminManager.PlayerHasCommandOverride(player, checkPermission))
							hasImmunity = true;
						break;
				}
			}

			return hasImmunity;
		}
	}
}
