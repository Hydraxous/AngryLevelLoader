﻿using BepInEx;
using HarmonyLib;
using PluginConfig.API;
using PluginConfig.API.Decorators;
using PluginConfig.API.Fields;
using PluginConfig.API.Functionals;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.Audio;
using RudeLevelScript;
using PluginConfig;
using BepInEx.Bootstrap;
using AngryLevelLoader.Containers;
using AngryLevelLoader.Managers;
using AngryLevelLoader.DataTypes;
using AngryLevelLoader.Fields;
using PluginConfiguratorComponents;
using System.Text;
using AngryLevelLoader.Managers.ServerManager;
using UnityEngine.UI;
using AngryUiComponents;
using Unity.Audio;
using BepInEx.Logging;
using AngryLevelLoader.Managers.BannedMods;

namespace AngryLevelLoader
{
    public class SpaceField : CustomConfigField
    {
        public SpaceField(ConfigPanel parentPanel, float space) : base(parentPanel, 60, space)
        {

        }
    }

	[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
	[BepInDependency(PluginConfiguratorController.PLUGIN_GUID, "1.8.0")]
	[BepInDependency(Ultrapain.Plugin.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.heaven.orhell", BepInDependency.DependencyFlags.SoftDependency)]
	// Soft ban dependencies
	[BepInDependency(BannedModsManager.HYDRA_LIB_GUID, BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(DualWieldPunchesSoftBan.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(UltraTweakerSoftBan.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(MovementPlusSoftBan.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(UltraCoinsSoftBan.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(UltraFunGunsSoftBan.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(FasterPunchSoftBan.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(AtlasWeapons.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
	public class Plugin : BaseUnityPlugin
	{
		public const bool devMode = true;

        public const string PLUGIN_NAME = "AngryLevelLoader";
        public const string PLUGIN_GUID = "com.eternalUnion.angryLevelLoader";
        public const string PLUGIN_VERSION = "2.5.1";

		public static string workingDir;
		// This is the path addressable remote load path uses
		// {AngryLevelLoader.Plugin.tempFolderPath}\\{guid}
		public static string tempFolderPath;
		public static string dataPath;
        public static string levelsPath;

		// This is the path angry addressables use
		public static string angryCatalogPath;

        public static Plugin instance;
		public static ManualLogSource logger;
		
		public static PluginConfigurator internalConfig;
		public static StringField lastVersion;
		public static BoolField ignoreUpdates;
		public static StringField configDataPath;

		public static bool ultrapainLoaded = false;
		public static bool heavenOrHellLoaded = false;

		public static Dictionary<string, RudeLevelData> idDictionary = new Dictionary<string, RudeLevelData>();
		public static Dictionary<string, AngryBundleContainer> angryBundles = new Dictionary<string, AngryBundleContainer>();

		// System which tracks when a bundle was played last in unix time
		public static Dictionary<string, long> lastPlayed = new Dictionary<string, long>();
		public static void LoadLastPlayedMap()
		{
			lastPlayed.Clear();

			string path = AngryPaths.LastPlayedMapPath;
			if (!File.Exists(path))
				return;

			using (StreamReader reader = new StreamReader(File.Open(path, FileMode.Open, FileAccess.Read)))
			{
				while (!reader.EndOfStream)
				{
					string key = reader.ReadLine();
					if (reader.EndOfStream)
					{
						logger.LogWarning("Invalid end of last played map file");
						break;
					}

					string value = reader.ReadLine();
					if (long.TryParse(value, out long seconds))
					{
						lastPlayed[key] = seconds;
					}
					else
					{
						logger.LogInfo($"Invalid last played time '{value}'");
					}
				}
			}
		}

		public static void UpdateLastPlayed(AngryBundleContainer bundle)
		{
			string guid = bundle.bundleData.bundleGuid;
			if (guid.Length != 32)
				return;

			if (bundleSortingMode.value == BundleSorting.LastPlayed)
				bundle.rootPanel.siblingIndex = 0;
			long secondsNow = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
			lastPlayed[guid] = secondsNow;

			string path = AngryPaths.LastPlayedMapPath;
            IOUtils.TryCreateDirectoryForFile(path);
            using (StreamWriter writer = new StreamWriter(File.Open(path, FileMode.OpenOrCreate, FileAccess.Write)))
			{
				writer.BaseStream.Seek(0, SeekOrigin.Begin);
				writer.BaseStream.SetLength(0);
				foreach (var pair in lastPlayed)
				{
					writer.WriteLine(pair.Key);
					writer.WriteLine(pair.Value.ToString());
				}
			}
		}

		public static AngryBundleContainer GetAngryBundleByGuid(string guid)
		{
			return angryBundles.Values.Where(bundle => bundle.bundleData.bundleGuid == guid).FirstOrDefault();
		}

		public static LevelContainer GetLevel(string id)
		{
			foreach (AngryBundleContainer container in angryBundles.Values)
			{
				foreach (LevelContainer level in container.levels.Values)
				{
					if (level.field.data.uniqueIdentifier == id)
						return level;
				}
			}

			return null;
		}

		public static void ProcessPath(string path)
		{
            if (AngryFileUtils.TryGetAngryBundleData(path, out AngryBundleData data, out Exception error))
			{
                if (angryBundles.TryGetValue(data.bundleGuid, out AngryBundleContainer bundle))
                {
					// Duplicate file check
					if (File.Exists(bundle.pathToAngryBundle) && !IOUtils.PathEquals(path, bundle.pathToAngryBundle))
					{
						logger.LogError($"Duplicate angry files. Original: {Path.GetFileName(bundle.pathToAngryBundle)}. Duplicate: {Path.GetFileName(path)}");

						if (!string.IsNullOrEmpty(errorText.text))
							errorText.text += '\n';
						errorText.text += $"<color=red>Error loading {Path.GetFileName(path)}</color> Duplicate file, original is {Path.GetFileName(bundle.pathToAngryBundle)}";

						return;
					}

					bool newFile = !IOUtils.PathEquals(bundle.pathToAngryBundle, path);
                    bundle.pathToAngryBundle = path;
                    bundle.rootPanel.interactable = true;
                    bundle.rootPanel.hidden = false;

                    if (newFile)
                        bundle.UpdateScenes(false, false);

                    return;
                }

                AngryBundleContainer newBundle = new AngryBundleContainer(path, data);
                angryBundles[data.bundleGuid] = newBundle;
                newBundle.UpdateOrder();

                try
                {
                    // If rank score is not cached (invalid value) do not lazy load and calculate rank data
                    if (newBundle.finalRankScore.value < 0)
                    {
						logger.LogWarning("Final rank score for the bundle not cached, skipping lazy reload");
                        newBundle.UpdateScenes(false, false);
                    }
                    else
                    {
                        newBundle.UpdateScenes(false, true);
                    }
                }
                catch (Exception e)
                {
					logger.LogWarning($"Exception thrown while loading level bundle: {e}");
                    if (!string.IsNullOrEmpty(errorText.text))
                        errorText.text += '\n';
                    errorText.text += $"<color=red>Error loading {Path.GetFileNameWithoutExtension(path)}</color>. Check the logs for more information";
                }
            }
			else
			{
                if (AngryFileUtils.IsV1LegacyFile(path))
                {
                    if (!string.IsNullOrEmpty(errorText.text))
                        errorText.text += '\n';
                    errorText.text += $"<color=yellow>{Path.GetFileName(path)} is a V1 legacy file. Support for legacy files were dropped after 2.5.0</color>";
                }
                else
                {
					logger.LogError($"Could not load the bundle at {path}\n{error}");

                    if (!string.IsNullOrEmpty(errorText.text))
                        errorText.text += '\n';
                    errorText.text += $"<color=yellow>Failed to load {Path.GetFileNameWithoutExtension(path)}</color>";
                }

                return;
            }
        }

		// This does NOT reload the files, only
		// loads newly added angry levels
		public static void ScanForLevels()
        {
            errorText.text = "";
            if (!Directory.Exists(levelsPath))
            {
				logger.LogWarning("Could not find the Levels folder at " + levelsPath);
				errorText.text = "<color=red>Error: </color>Levels folder not found";
				return;
            }

			foreach (string path in Directory.GetFiles(levelsPath))
			{
				ProcessPath(path);
			}

			OnlineLevelsManager.UpdateUI();
		}

		public static void SortBundles()
		{
			int i = 0;
			if (bundleSortingMode.value == BundleSorting.Alphabetically)
			{
				foreach (var bundle in angryBundles.Values.OrderBy(b => b.bundleData.bundleName))
					bundle.rootPanel.siblingIndex = i++;
			}
			else if (bundleSortingMode.value == BundleSorting.Author)
			{
				foreach (var bundle in angryBundles.Values.OrderBy(b => b.bundleData.bundleAuthor))
					bundle.rootPanel.siblingIndex = i++;
			}
			else if (bundleSortingMode.value == BundleSorting.LastPlayed)
			{
				foreach (var bundle in angryBundles.Values.OrderByDescending((b) => {
					if (lastPlayed.TryGetValue(b.bundleData.bundleGuid, out long time))
						return time;
					return 0;
				}))
				{
					bundle.rootPanel.siblingIndex = i++;
				}
			}
		}

		public static void UpdateAllUI()
		{
			foreach (AngryBundleContainer angryBundle in  angryBundles.Values)
			{
				if (angryBundle.finalRankScore.value < 0)
					angryBundle.UpdateScenes(false, false);
				else
					angryBundle.UpdateFinalRankUI();

                foreach (LevelContainer level in angryBundle.levels.Values)
				{
					level.UpdateUI();
				}
			}
		}

        public static bool LoadEssentialScripts()
        {
			bool loaded = true;

			var res = ScriptManager.AttemptLoadScriptWithCertificate("AngryLoaderAPI.dll");
			if (res == ScriptManager.LoadScriptResult.NotFound)
			{
				logger.LogError("Required script AngryLoaderAPI.dll not found");
				loaded = false;
			}
			else
			{
				ScriptManager.ForceLoadScript("AngryLoaderAPI.dll");
			}

			res = ScriptManager.AttemptLoadScriptWithCertificate("RudeLevelScripts.dll");
			if (res == ScriptManager.LoadScriptResult.NotFound)
			{
				logger.LogError("Required script RudeLevelScripts.dll not found");
				loaded = false;
			}
			else
			{
				ScriptManager.ForceLoadScript("RudeLevelScripts.dll");
			}

			return loaded;
		}

		// Defaults to violent
        public static int selectedDifficulty = 3;
		private static List<string> difficultyList = new List<string> { "HARMLESS", "LENIENT", "STANDARD", "VIOLENT" };
		public static StringListField difficultySelect;
		
		public static Harmony harmony;

		#region Config Fields
		// Main panel
		public static PluginConfigurator config;
		public static ConfigHeader levelUpdateNotifier;
		public static ConfigHeader newLevelNotifier;
		public static StringField newLevelNotifierLevels;
		public static BoolField newLevelToggle;
        public static ConfigHeader errorText;
		public static ConfigDivision bundleDivision;

		// Settings panel
		public static ButtonField changelog;
		public static KeyCodeField reloadFileKeybind;
		public enum CustomLevelButtonPosition
		{
			Top,
			Bottom,
			Disabled
		}
		public static EnumField<CustomLevelButtonPosition> customLevelButtonPosition;
		public static ColorField customLevelButtonFrameColor;
		public static ColorField customLevelButtonBackgroundColor;
		public static ColorField customLevelButtonTextColor;
		public static BoolField refreshCatalogOnBoot;
		public static BoolField checkForUpdates;
		public static BoolField levelUpdateNotifierToggle;
		public static BoolField levelUpdateIgnoreCustomBuilds;
		public static BoolField newLevelNotifierToggle;
		public static List<string> scriptCertificateIgnore = new List<string>();
		public static StringMultilineField scriptCertificateIgnoreField;
		public static BoolField useDevelopmentBranch;
		public static BoolField scriptUpdateIgnoreCustom;
		public enum BundleSorting
		{
			Alphabetically,
			Author,
			LastPlayed
		}
		public static EnumField<BundleSorting> bundleSortingMode;
		
		// Developer panel

		#endregion

		// Set every fields' interactable field to false
		// Used by move data process to force a restart
		private static void DisableAllConfig()
		{
			Stack<ConfigField> toProcess = new Stack<ConfigField>(config.rootPanel.GetAllFields());

			while (toProcess.Count != 0)
			{
				ConfigField field = toProcess.Pop();

                if (field is ConfigPanel concretePanel)
				{
					foreach (var subField in concretePanel.GetAllFields())
						toProcess.Push(subField);
				}

				field.interactable = false;
			}
		}

		// Delayed refresh online catalog on boot
		private static void RefreshCatalogOnMainMenu(Scene newScene, LoadSceneMode mode)
		{
			if (SceneHelper.CurrentScene != "Main Menu")
				return;

			if (refreshCatalogOnBoot.value)
				OnlineLevelsManager.RefreshAsync();

			SceneManager.sceneLoaded -= RefreshCatalogOnMainMenu;
		}

		// Is ultrapain difficulty enabled?
		private static bool GetUltrapainDifficultySet()
		{
			return Ultrapain.Plugin.ultrapainDifficulty;
		}

		// Is Heaven or Hell difficulty enabled?
		private static bool GetHeavenOrHellDifficultySet()
		{
			return MyCoolMod.Plugin.isHeavenOrHell;
		}

		// Create the shortcut in chapters menu
		private const string CUSTOM_LEVEL_BUTTON_ASSET_PATH = "AngryLevelLoader/UI/CustomLevels.prefab";
		private static AngryCustomLevelButtonComponent currentCustomLevelButton;
		private static RectTransform bossRushButton;
		private static void CreateCustomLevelButtonOnMainMenu()
		{
			GameObject canvasObj = SceneManager.GetActiveScene().GetRootGameObjects().Where(obj => obj.name == "Canvas").FirstOrDefault();
			if (canvasObj == null)
			{
				logger.LogWarning("Angry tried to create main menu buttons, but root canvas was not found!");
				return;
			}

			Transform chapterSelect = canvasObj.transform.Find("Chapter Select");
			if (chapterSelect != null)
			{
				GameObject customLevelButtonObj = Addressables.InstantiateAsync(CUSTOM_LEVEL_BUTTON_ASSET_PATH, chapterSelect).WaitForCompletion();
				Transform bossRush = chapterSelect.Find("Boss Rush Button");
				if (bossRush != null)
					bossRushButton = bossRush.gameObject.GetComponent<RectTransform>();
				currentCustomLevelButton = customLevelButtonObj.GetComponent<AngryCustomLevelButtonComponent>();

				currentCustomLevelButton.button.onClick = new Button.ButtonClickedEvent();
				currentCustomLevelButton.button.onClick.AddListener(() =>
				{
					// Disable act selection panel
					chapterSelect.gameObject.SetActive(false);

					// Open the options menu
					Transform optionsMenu = canvasObj.transform.Find("OptionsMenu");
					if (optionsMenu == null)
					{
						logger.LogError("Angry tried to find the options menu but failed!");
						chapterSelect.gameObject.SetActive(true);
						return;
					}
					optionsMenu.gameObject.SetActive(true);

					// Open plugin config panel
					Transform pluginConfigButton = optionsMenu.transform.Find("PluginConfiguratorButton(Clone)");
					if (pluginConfigButton == null)
						pluginConfigButton = optionsMenu.transform.Find("PluginConfiguratorButton");

					if (pluginConfigButton == null)
					{
						logger.LogError("Angry tried to find the plugin configurator button but failed!");
						return;
					}

					// Click the plugin config button and open the main panel of angry
					pluginConfigButton.gameObject.GetComponent<Button>().onClick.Invoke();
					if (PluginConfiguratorController.activePanel != null)
						PluginConfiguratorController.activePanel.SetActive(false);
					PluginConfiguratorController.mainPanel.gameObject.SetActive(false);
					config.rootPanel.OpenPanelInternally(false);
					config.rootPanel.currentPanel.rect.normalizedPosition = new Vector2(0, 1);

					// Set the difficulty based on the previously selected act
					int difficulty = PrefsManager.Instance.GetInt("difficulty", 3);
					switch (difficulty)
					{
						// Stock difficulties
						case 0:
						case 1:
						case 2:
						case 3:
							logger.LogInfo($"Angry setting difficulty to {difficultyList[difficulty]}");
							difficultySelect.valueIndex = difficulty;
							break;

						// Possibly ultrapain
						case 5:
							if (ultrapainLoaded)
							{
								if (GetUltrapainDifficultySet())
								{
									difficultySelect.valueIndex = difficultyList.IndexOf("ULTRAPAIN");
								}
								else
								{
									logger.LogWarning("Difficulty was set to UKMD, but angry does not support it. Setting to violent");
									difficultySelect.valueIndex = 3;
								}
							}
							break;

						// Possibly Heaven or Hell, or invalid difficulty
						default:
							if (heavenOrHellLoaded)
							{
								if (GetHeavenOrHellDifficultySet())
								{
									difficultySelect.valueIndex = difficultyList.IndexOf("HEAVEN OR HELL");
								}
								else
								{
									logger.LogWarning("Unknown difficulty, defaulting to violent");
									difficultySelect.valueIndex = 3;
								}
							}
							break;
					}
				});

				customLevelButtonPosition.TriggerPostValueChangeEvent();
				customLevelButtonFrameColor.TriggerPostValueChangeEvent();
				customLevelButtonBackgroundColor.TriggerPostValueChangeEvent();
				customLevelButtonTextColor.TriggerPostValueChangeEvent();
			}
			else
			{
				logger.LogWarning("Angry tried to find chapter select menu, but root canvas was not found!");
			}
		}

		// Create the angry canvas
		private const string ANGRY_UI_PANEL_ASSET_PATH = "AngryLevelLoader/UI/AngryUIPanel.prefab";
		public static AngryUIPanelComponent currentPanel;
		private static void CreateAngryUI()
		{
			if (currentPanel != null)
				return;

			GameObject canvasObj = SceneManager.GetActiveScene().GetRootGameObjects().Where(obj => obj.name == "Canvas").FirstOrDefault();
			if (canvasObj == null)
			{
				logger.LogWarning("Angry tried to create main menu buttons, but root canvas was not found!");
				return;
			}

			GameObject panelObj = Addressables.InstantiateAsync(ANGRY_UI_PANEL_ASSET_PATH, canvasObj.transform).WaitForCompletion();
			currentPanel = panelObj.GetComponent<AngryUIPanelComponent>();

			currentPanel.reloadBundlePrompt.MakeTransparent(true);
		}

		internal static FileSystemWatcher watcher;
		private static void InitializeFileWatcher()
		{
			if (watcher != null)
				return;

			watcher = new FileSystemWatcher(levelsPath);
			watcher.SynchronizingObject = CrossThreadInvoker.Instance;
			watcher.Changed += (sender, e) =>
			{
				// Notify the bundle that the file is outdated

				string fullPath = e.FullPath;
				foreach (var bundle in angryBundles.Values)
				{
					if (IOUtils.PathEquals(fullPath, bundle.pathToAngryBundle))
					{
						logger.LogWarning($"Bundle {fullPath} was updated, container notified");
						bundle.FileChanged();
						return;
					}
				}
			};
			watcher.Renamed += (sender, e) =>
			{
				// Try to find if a bundle owns the file, then update its file path

				string fullPath = e.FullPath;
				foreach (var bundle in angryBundles.Values)
				{
					if (IOUtils.PathEquals(fullPath, bundle.pathToAngryBundle))
					{
						logger.LogWarning($"Bundle {fullPath} was renamed, path updated");
						bundle.pathToAngryBundle = fullPath;
						return;
					}
				}
			};
			watcher.Deleted += (sender, e) =>
			{
				// Try to find if a bundle owns the file, then unlink it

				string fullPath = e.FullPath;
				foreach (var bundle in angryBundles.Values)
				{
					if (IOUtils.PathEquals(fullPath, bundle.pathToAngryBundle))
					{
						logger.LogWarning($"Bundle {fullPath} was deleted, unlinked");
						bundle.pathToAngryBundle = "";
						return;
					}
				}
			};
			watcher.Created += (sender, e) =>
			{
				// Try to find a bundle matching the file's guid

				string fullPath = e.FullPath;
				if (!AngryFileUtils.TryGetAngryBundleData(fullPath, out AngryBundleData data, out Exception exp))
					return;

				if (angryBundles.TryGetValue(data.bundleGuid, out AngryBundleContainer bundle))
				{
					if (bundle.bundleData.bundleGuid == data.bundleGuid && !File.Exists(bundle.pathToAngryBundle))
					{
						logger.LogWarning($"Bundle {fullPath} was just added, and a container with the same guid had no file linked. Linked, container notified");
						bundle.pathToAngryBundle = fullPath;
						bundle.FileChanged();
						return;
					}
				}
			};

			watcher.Filter = "*";

			watcher.IncludeSubdirectories = false;
			watcher.EnableRaisingEvents = true;
		}

		private static void InitializeConfig()
		{
			if (config != null)
				return;

			config = PluginConfigurator.Create("Angry Level Loader", PLUGIN_GUID);
			config.postPresetChangeEvent += (b, a) => UpdateAllUI();
			config.SetIconWithURL("file://" + Path.Combine(workingDir, "plugin-icon.png"));
			newLevelToggle = new BoolField(config.rootPanel, "", "v_newLevelToggle", false);
			newLevelToggle.hidden = true;
			config.rootPanel.onPannelOpenEvent += (external) =>
			{
				if (newLevelToggle.value)
				{
					newLevelNotifier.text = string.Join("\n", newLevelNotifierLevels.value.Split('`').Where(level => !string.IsNullOrEmpty(level)).Select(name => $"<color=lime>New level: {name}</color>"));
					newLevelNotifier.hidden = false;
					newLevelNotifierLevels.value = "";
				}
				newLevelToggle.value = false;
			};

			newLevelNotifier = new ConfigHeader(config.rootPanel, "<color=lime>New levels are available!</color>", 16);
			newLevelNotifier.hidden = true;
			levelUpdateNotifier = new ConfigHeader(config.rootPanel, "<color=lime>Level updates available!</color>", 16);
			levelUpdateNotifier.hidden = true;
			OnlineLevelsManager.onlineLevelsPanel = new ConfigPanel(internalConfig.rootPanel, "Online Levels", "b_onlineLevels", ConfigPanel.PanelFieldType.StandardWithIcon);
			new ConfigBridge(OnlineLevelsManager.onlineLevelsPanel, config.rootPanel);
			OnlineLevelsManager.onlineLevelsPanel.SetIconWithURL("file://" + Path.Combine(workingDir, "online-icon.png"));
			OnlineLevelsManager.onlineLevelsPanel.onPannelOpenEvent += (e) =>
			{
				newLevelNotifier.hidden = true;
			};
			OnlineLevelsManager.Init();

			difficultySelect = new StringListField(internalConfig.rootPanel, "Difficulty", "difficultySelect", difficultyList.ToArray(), "VIOLENT");
			new ConfigBridge(difficultySelect, config.rootPanel);
			difficultySelect.onValueChange += (e) =>
			{
				selectedDifficulty = Array.IndexOf(difficultyList.ToArray(), e.value);
				if (selectedDifficulty == -1)
				{
					logger.LogWarning("Invalid difficulty, setting to violent");
					selectedDifficulty = 3;
					e.value = "VIOLENT";
				}
				else
				{
					if (e.value == "ULTRAPAIN")
						selectedDifficulty = 4;
					else if (e.value == "HEAVEN OR HELL")
						selectedDifficulty = 5;
				}
			};
			difficultySelect.TriggerValueChangeEvent();

			ConfigPanel settingsPanel = new ConfigPanel(internalConfig.rootPanel, "Settings", "p_settings", ConfigPanel.PanelFieldType.Standard);
			new ConfigBridge(settingsPanel, config.rootPanel);
			settingsPanel.hidden = true;

			// Settings panel
			ButtonField openLevels = new ButtonField(settingsPanel, "Open Levels Folder", "openLevelsButton");
			openLevels.onClick += () => Application.OpenURL(levelsPath);
			changelog = new ButtonField(settingsPanel, "Changelog", "changelogButton");
			changelog.onClick += () =>
			{
				changelog.interactable = false;
				_ = PluginUpdateHandler.CheckPluginUpdate();
			};

			reloadFileKeybind = new KeyCodeField(settingsPanel, "Reload File", "f_reloadFile", KeyCode.None);
			reloadFileKeybind.onValueChange += (e) =>
			{
				if (e.value == KeyCode.Mouse0 || e.value == KeyCode.Mouse1 || e.value == KeyCode.Mouse2)
					e.canceled = true;
			};

			customLevelButtonPosition = new EnumField<CustomLevelButtonPosition>(settingsPanel, "Custom level button position", "s_customLevelButtonPosition", CustomLevelButtonPosition.Bottom);
			customLevelButtonPosition.postValueChangeEvent += (pos) =>
			{
				if (currentCustomLevelButton == null)
					return;

				currentCustomLevelButton.gameObject.SetActive(true);
				switch (pos)
				{
					case CustomLevelButtonPosition.Disabled:
						currentCustomLevelButton.gameObject.SetActive(false);
						break;

					case CustomLevelButtonPosition.Bottom:
						currentCustomLevelButton.transform.localPosition = new Vector3(currentCustomLevelButton.transform.localPosition.x, -303, currentCustomLevelButton.transform.localPosition.z);
						break;

					case CustomLevelButtonPosition.Top:
						currentCustomLevelButton.transform.localPosition = new Vector3(currentCustomLevelButton.transform.localPosition.x, 192, currentCustomLevelButton.transform.localPosition.z);
						break;
				}

				if (bossRushButton != null)
				{
					if (pos == CustomLevelButtonPosition.Bottom)
					{
						currentCustomLevelButton.rect.sizeDelta = new Vector2((380f - 5) / 2, 50);
						currentCustomLevelButton.transform.localPosition = new Vector3((380f + 5) / -4, currentCustomLevelButton.transform.localPosition.y, currentCustomLevelButton.transform.localPosition.z);

						bossRushButton.sizeDelta = new Vector2((380f - 5) / 2, 50);
						bossRushButton.transform.localPosition = new Vector3((380f + 5) / 4, -303, 0);
					}
					else
					{
						currentCustomLevelButton.rect.sizeDelta = new Vector2(380, 50);
						currentCustomLevelButton.transform.localPosition = new Vector3(0, currentCustomLevelButton.transform.localPosition.y, currentCustomLevelButton.transform.localPosition.z);

						bossRushButton.sizeDelta = new Vector2(380, 50);
						bossRushButton.transform.localPosition = new Vector3(0, -303, 0);
					}
				}
			};

			customLevelButtonFrameColor = new ColorField(settingsPanel, "Custom level button frame color", "s_customLevelButtonFrameColor", Color.white);
			customLevelButtonFrameColor.postValueChangeEvent += (clr) =>
			{
				if (currentCustomLevelButton == null)
					return;

				ColorBlock block = new ColorBlock();
				block.colorMultiplier = 1f;
				block.fadeDuration = 0.1f;
				block.normalColor = clr;
				block.selectedColor = clr * 0.8f;
				block.highlightedColor = clr * 0.8f;
				block.pressedColor = clr * 0.5f;
				block.disabledColor = Color.gray;

				currentCustomLevelButton.button.colors = block;
			};

			customLevelButtonBackgroundColor = new ColorField(settingsPanel, "Custom level button background color", "s_customLevelButtonBgColor", Color.black);
			customLevelButtonBackgroundColor.postValueChangeEvent += (clr) =>
			{
				if (currentCustomLevelButton == null)
					return;

				currentCustomLevelButton.background.color = clr;
			};

			customLevelButtonTextColor = new ColorField(settingsPanel, "Custom level button text color", "s_customLevelButtonTextColor", Color.white);
			customLevelButtonTextColor.postValueChangeEvent += (clr) =>
			{
				if (currentCustomLevelButton == null)
					return;

				currentCustomLevelButton.text.color = clr;
			};

			bundleSortingMode = new EnumField<BundleSorting>(settingsPanel, "Bundle sorting", "s_bundleSortingMode", BundleSorting.LastPlayed);
			bundleSortingMode.onValueChange += (e) =>
			{
				bundleSortingMode.value = e.value;
				SortBundles();
			};

			new ConfigHeader(settingsPanel, "Online");
			new ConfigHeader(settingsPanel, "Online level catalog and thumbnails are cached, if there are no updates only 64 bytes of data is downloaded per refresh", 12, TextAnchor.UpperLeft);
			refreshCatalogOnBoot = new BoolField(settingsPanel, "Refresh online catalog on boot", "s_refreshCatalogBoot", true);
			checkForUpdates = new BoolField(settingsPanel, "Check for updates on boot", "s_checkForUpdates", true);
			useDevelopmentBranch = new BoolField(settingsPanel, "Use development chanel", "s_useDevChannel", false);
			if (!devMode)
			{
				useDevelopmentBranch.hidden = true;
				useDevelopmentBranch.value = false;
			}
			levelUpdateNotifierToggle = new BoolField(settingsPanel, "Notify on level updates", "s_levelUpdateNofify", true);
			levelUpdateNotifierToggle.onValueChange += (e) =>
			{
				levelUpdateNotifierToggle.value = e.value;
				OnlineLevelsManager.CheckLevelUpdateText();
			};
			levelUpdateIgnoreCustomBuilds = new BoolField(settingsPanel, "Ignore updates for custom build", "s_levelUpdateIgnoreCustomBuilds", false);
			levelUpdateIgnoreCustomBuilds.onValueChange += (e) =>
			{
				levelUpdateIgnoreCustomBuilds.value = e.value;
				OnlineLevelsManager.CheckLevelUpdateText();
			};
			newLevelNotifierLevels = new StringField(settingsPanel, "h_New levels", "s_newLevelNotifierLevels", "", true);
			newLevelNotifierLevels.hidden = true;
			newLevelNotifierToggle = new BoolField(settingsPanel, "Notify on new level release", "s_newLevelNotiftToggle", true);
			newLevelNotifierToggle.onValueChange += (e) =>
			{
				newLevelNotifierToggle.value = e.value;
				if (!e.value)
					newLevelNotifier.hidden = true;
			};
			new ConfigHeader(settingsPanel, "Scripts");
			scriptUpdateIgnoreCustom = new BoolField(settingsPanel, "Ignore updates for custom builds", "s_scriptUpdateIgnoreCustom", false);
			scriptCertificateIgnoreField = new StringMultilineField(settingsPanel, "Certificate ignore", "s_scriptCertificateIgnore", "", true);
			scriptCertificateIgnore = scriptCertificateIgnoreField.value.Split('\n').ToList();

			new SpaceField(settingsPanel, 5);
			new ConfigHeader(settingsPanel, "Danger Zone") { textColor = Color.red };
			StringField dataPathInput = new StringField(settingsPanel, "Data Path", "s_dataPathInput", dataPath, false, false);
			ButtonField changeDataPath = new ButtonField(settingsPanel, "Move Data", "s_changeDataPath");
			ConfigHeader dataInfo = new ConfigHeader(settingsPanel, "<color=red>RESTART REQUIRED</color>", 18);
			dataInfo.hidden = true;
			changeDataPath.onClick += () =>
			{
				string newPath = dataPathInput.value;
				if (newPath == configDataPath.value)
					return;

				if (!Directory.Exists(newPath))
				{
					dataInfo.text = "<color=red>Could not find the directory</color>";
					dataInfo.hidden = false;
					return;
				}

				string newLevelsFolder = Path.Combine(newPath, "Levels");
				IOUtils.TryCreateDirectory(newLevelsFolder);
				foreach (string levelFile in Directory.GetFiles(levelsPath))
				{
					File.Copy(levelFile, Path.Combine(newLevelsFolder, Path.GetFileName(levelFile)), true);
					File.Delete(levelFile);
				}
				Directory.Delete(levelsPath, true);
				levelsPath = newLevelsFolder;

				string newLevelsUnpackedFolder = Path.Combine(newPath, "LevelsUnpacked");
				IOUtils.TryCreateDirectory(newLevelsUnpackedFolder);
				foreach (string unpackedLevelFolder in Directory.GetDirectories(tempFolderPath))
				{
					string dest = Path.Combine(newLevelsUnpackedFolder, Path.GetFileName(unpackedLevelFolder));
					if (Directory.Exists(dest))
						Directory.Delete(dest, true);

					IOUtils.DirectoryCopy(unpackedLevelFolder, dest, true, true);
				}
				Directory.Delete(tempFolderPath, true);
				tempFolderPath = newLevelsUnpackedFolder;

				dataInfo.text = "<color=red>RESTART REQUIRED</color>";
				dataInfo.hidden = false;
				configDataPath.value = newPath;

				DisableAllConfig();
			};

			ButtonArrayField settingsAndReload = new ButtonArrayField(config.rootPanel, "settingsAndReload", 2, new float[] { 0.5f, 0.5f }, new string[] { "Settings", "Scan For Levels" });
			settingsAndReload.OnClickEventHandler(0).onClick += () =>
			{
				settingsPanel.OpenPanel();
			};
			settingsAndReload.OnClickEventHandler(1).onClick += () =>
			{
				ScanForLevels();
			};

			// Developer panel
			ConfigPanel devPanel = new ConfigPanel(config.rootPanel, "Developer Panel", "devPanel", ConfigPanel.PanelFieldType.BigButton);
			if (!devMode)
				devPanel.hidden = true;

			new ConfigHeader(devPanel, "Angry Server Interface");
			ConfigHeader output = new ConfigHeader(devPanel, "Output", 18, TextAnchor.MiddleLeft);
			ConfigDivision devDiv = new ConfigDivision(devPanel, "devDiv");
			ButtonField addAllBundles = new ButtonField(devDiv, "Add All Bundles", "addAllBundles");
			addAllBundles.onClick += async () =>
			{
				devDiv.interactable = false;

				try
				{
					if (OnlineLevelsManager.catalog == null)
					{
						output.text = "Catalog is not loaded";
						return;
					}

					output.text = "<color=grey>Fetching existing bundles...</color>";

					AngryVotes.GetAllVotesResult existingBundles = await AngryVotes.GetAllVotesTask();
					if (existingBundles.networkError)
					{
						output.text += "\nNetwork error, check connection";
						return;
					}
					if (existingBundles.httpError)
					{
						output.text += "\nHttp error, check server";
						return;
					}
					if (existingBundles.status != AngryVotes.GetAllVotesStatus.GET_ALL_VOTES_OK)
					{
						output.text += $"\nStatus error: {existingBundles.message}:{existingBundles.status}";
						return;
					}

					output.text += "\n<color=grey>Adding all bundles...</color>";

					foreach (var bundle in OnlineLevelsManager.catalog.Levels.Where(level => !existingBundles.response.bundles.Keys.Where(existingBundle => existingBundle == level.Guid).Any()))
					{
						output.text += $"\n<color=grey>command: add_bundle {bundle.Guid}</color>";
						AngryAdmin.CommandResult res = await AngryAdmin.SendCommand($"add_bundle {bundle.Guid}");

						if (res.completedSuccessfully && res.status == AngryAdmin.CommandStatus.OK)
						{
							output.text += $"\n{res.response.result}";
						}
						else if (res.networkError)
						{
							output.text += $"\n<color=red>NETWORK ERROR</color> Check conntection";
						}
						else if (res.httpError)
						{
							output.text += $"\n<color=red>HTTP ERROR</color> Check server";
						}
						else
						{
							if (res.response != null)
								output.text += $"\n<color=red>ERROR: </color>{res.message}:{res.status}";
							else
								output.text += $"\n<color=red>ERROR: </color>Encountered unknown error. Status: " + res.status;
						}
					}

					output.text += $"\n<color=lime>done</color>";
				}
				finally
				{
					devDiv.interactable = true;
				}
			};

			errorText = new ConfigHeader(config.rootPanel, "", 16, TextAnchor.UpperLeft); ;

			new ConfigHeader(config.rootPanel, "Level Bundles");
			bundleDivision = new ConfigDivision(config.rootPanel, "div_bundles");
		}

		private void Awake()
		{
			// Plugin startup logic
			instance = this;
			logger = Logger;

			BannedModsManager.Init();

			// Initialize internal config
			internalConfig = PluginConfigurator.Create("Angry Level Loader (INTERNAL)" ,PLUGIN_GUID + "_internal");
			internalConfig.hidden = true;
			internalConfig.interactable = false;
			internalConfig.presetButtonHidden = true;
			internalConfig.presetButtonInteractable = false;

            lastVersion = new StringField(internalConfig.rootPanel, "lastPluginVersion", "lastPluginVersion", "", true);
            ignoreUpdates = new BoolField(internalConfig.rootPanel, "ignoreUpdate", "ignoreUpdate", false);
			configDataPath = new StringField(internalConfig.rootPanel, "dataPath", "dataPath", Path.Combine(IOUtils.AppData, "AngryLevelLoader"));

			// Setup variable dependent paths
            workingDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			dataPath = configDataPath.value;
			IOUtils.TryCreateDirectory(dataPath);
			levelsPath = Path.Combine(dataPath, "Levels");
            IOUtils.TryCreateDirectory(levelsPath);
            tempFolderPath = Path.Combine(dataPath, "LevelsUnpacked");
            IOUtils.TryCreateDirectory(tempFolderPath);

			AngryPaths.TryCreateAllPaths();

			// To detect angry file changes in the levels folder
			CrossThreadInvoker.Init();
			InitializeFileWatcher();
			
			// Load the loader's assets
			Addressables.InitializeAsync().WaitForCompletion();
			angryCatalogPath = Path.Combine(workingDir, "Assets");
			Addressables.LoadContentCatalogAsync(Path.Combine(angryCatalogPath, "catalog.json"), true).WaitForCompletion();
			AssetManager.Init();

			// These scripts are common among all the levels
            if (!LoadEssentialScripts())
			{
				logger.LogError("Disabling AngryLevelLoader because one or more of its dependencies have failed to load");
				enabled = false;
				return;
			}

			// Tracks when each bundle was last played in unix time
			LoadLastPlayedMap();

			harmony = new Harmony(PLUGIN_GUID);
            harmony.PatchAll();

			SceneManager.sceneLoaded += (scene, mode) =>
			{
				if (mode == LoadSceneMode.Additive)
					return;

                if (AngrySceneManager.isInCustomLevel)
				{
					Logger.LogInfo("Running post scene load event");
					AngrySceneManager.PostSceneLoad();

					Logger.LogInfo("Creating UI panel");
					CreateAngryUI();

					Logger.LogInfo("Checking bundle file status");
					AngrySceneManager.currentBundleContainer.CheckReloadPrompt();
				}
				else if (SceneHelper.CurrentScene == "Main Menu")
				{
					CreateCustomLevelButtonOnMainMenu();
				}
			};
			// Delay the catalog reload on boot until the main menu since steam must be initialized for the ticket request
			SceneManager.sceneLoaded += RefreshCatalogOnMainMenu;

			// See if custom difficulties are loaded. BepInEx soft dependency forces them to be loaded first
			if (Chainloader.PluginInfos.ContainsKey(Ultrapain.Plugin.PLUGIN_GUID))
			{
				ultrapainLoaded = true;
				difficultyList.Add("ULTRAPAIN");
			}
			if (Chainloader.PluginInfos.ContainsKey("com.heaven.orhell"))
			{
				heavenOrHellLoaded = true;
				difficultyList.Add("HEAVEN OR HELL");
			}

			InitializeConfig();

			// TODO: Investigate further on this issue:
			//
			// if I don't do that, when I load an addressable scene (custom level)
			// it results in whatever this is. I guess it doesn't load the dependencies
			// but I am not too sure. Same thing happens when I load trough asset bundles
			// instead and everything is white unless I load a prefab which creates a chain
			// reaction of texture, material, shader dependency loads. Though it MIGHT be incorrect,
			// and I am not sure of the actual origin of the issue (because when I check the loaded
			// bundles every addressable bundle is already in the memory like what?)
			Addressables.LoadAssetAsync<GameObject>("Assets/Prefabs/Attacks and Projectiles/Projectile Decorative.prefab");

			// Migrate from legacy versions, and check for a new version from web
			PluginUpdateHandler.Check();

            ScanForLevels();

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

		float lastPress = 0;
		private void OnGUI()
		{
			if (reloadFileKeybind.value == KeyCode.None)
				return;

			if (!AngrySceneManager.isInCustomLevel)
				return;

			Event current = Event.current;
			KeyCode keyCode = KeyCode.None;
			if (current.keyCode == KeyCode.Escape)
			{
				return;
			}
			if (current.isKey || current.isMouse || current.button > 2 || current.shift)
			{
				if (current.isKey)
				{
					keyCode = current.keyCode;
				}
				else if (Input.GetKey(KeyCode.LeftShift))
				{
					keyCode = KeyCode.LeftShift;
				}
				else if (Input.GetKey(KeyCode.RightShift))
				{
					keyCode = KeyCode.RightShift;
				}
				else if (current.button <= 6)
				{
					keyCode = KeyCode.Mouse0 + current.button;
				}
			}
			else if (Input.GetKey(KeyCode.Mouse3) || Input.GetKey(KeyCode.Mouse4) || Input.GetKey(KeyCode.Mouse5) || Input.GetKey(KeyCode.Mouse6))
			{
				keyCode = KeyCode.Mouse3;
				if (Input.GetKey(KeyCode.Mouse4))
				{
					keyCode = KeyCode.Mouse4;
				}
				else if (Input.GetKey(KeyCode.Mouse5))
				{
					keyCode = KeyCode.Mouse5;
				}
				else if (Input.GetKey(KeyCode.Mouse6))
				{
					keyCode = KeyCode.Mouse6;
				}
			}
			else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
			{
				keyCode = KeyCode.LeftShift;
				if (Input.GetKey(KeyCode.RightShift))
				{
					keyCode = KeyCode.RightShift;
				}
			}
			
			if (keyCode == reloadFileKeybind.value)
			{
				if (Time.time - lastPress < 3)
					return;

				lastPress = Time.time;

				if (NotificationPanel.CurrentNotificationCount() == 0)
					ReloadFileKeyPressed();
			}
		}
	
		private void ReloadFileKeyPressed()
		{
			if (AngrySceneManager.currentBundleContainer != null)
				AngrySceneManager.currentBundleContainer.UpdateScenes(false, false);
		}
	}

    public static class RudeLevelInterface
    {
		public static char INCOMPLETE_LEVEL_CHAR = '-';
		public static char GetLevelRank(string levelId)
        {
			LevelContainer level = Plugin.GetLevel(levelId);
			if (level == null)
				return INCOMPLETE_LEVEL_CHAR;
			return level.finalRank.value[0];
		}
	
        public static bool GetLevelChallenge(string levelId)
		{
			LevelContainer level = Plugin.GetLevel(levelId);
			if (level == null)
				return false;
			return level.challenge.value;
		}

		public static bool GetLevelSecret(string levelId, int secretIndex)
		{
			if (secretIndex < 0)
				return false;

			LevelContainer level = Plugin.GetLevel(levelId);
			if (level == null)
				return false;

			level.AssureSecretsSize();
			if (secretIndex >= level.field.data.secretCount)
				return false;
			return level.secrets.value[secretIndex] == 'T';
		}

        public static string GetCurrentLevelId()
        {
            return AngrySceneManager.isInCustomLevel ? AngrySceneManager.currentLevelData.uniqueIdentifier : "";
        }
    }

	public static class RudeBundleInterface
	{
		public static bool BundleExists(string bundleGuid)
		{
			return Plugin.angryBundles.Values.Where(bundle => bundle.bundleData.bundleGuid == bundleGuid).FirstOrDefault() != null;
		}

		public static string GetBundleBuildHash(string bundleGuid)
		{
			var bundle = Plugin.angryBundles.Values.Where(bundle => bundle.bundleData.bundleGuid == bundleGuid).FirstOrDefault();
			return bundle == null ? "" : bundle.bundleData.buildHash;
		}
    }
}
