﻿using BepInEx.Bootstrap;
using System;
using System.Collections.Generic;
using System.Text;

namespace AngryLevelLoader.Managers.BannedMods
{
	public static class FasterPunchSoftBan
	{
		public const string PLUGIN_GUID = "ironfarm.uk.muda";

		public static bool FasterPunchLoaded
		{
			get => Chainloader.PluginInfos.ContainsKey(PLUGIN_GUID);
		}

		public static SoftBanCheckResult Check()
		{
			SoftBanCheckResult result = new SoftBanCheckResult();

			if (FasterPunch.ConfigManager.StandardEnabled.value)
			{
				result.banned = true;
				result.message = "FasterPunch feedbacker is banned";
			}

			if (FasterPunch.ConfigManager.HeavyEnabled.value)
			{
				result.banned = true;
				if (!string.IsNullOrEmpty(result.message))
					result.message += '\n';
				result.message += "FasterPunch knuckleblaster is banned";
			}

			if (FasterPunch.ConfigManager.HookEnabled.value)
			{
				result.banned = true;
				if (!string.IsNullOrEmpty(result.message))
					result.message += '\n';
				result.message += "FasterPunch whiplash is banned";
			}

			return result;
		}
	}
}
