﻿using AngryLevelLoader.Containers;
using AngryLevelLoader.Fields;
using Newtonsoft.Json;
using PluginConfig.API;
using PluginConfig.API.Decorators;
using PluginConfig.API.Fields;
using PluginConfig.API.Functionals;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace AngryLevelLoader.Managers
{
    #region JSON Object Types

    public class LevelInfo
    {
        public class UpdateInfo
        {
            public string Hash { get; set; }
            public string Message { get; set; }
        }

        public string Name { get; set; }
        public string Author { get; set; }
        public string Guid { get; set; }
        public int Size { get; set; }
        public string Hash { get; set; }
        public string ThumbnailHash { get; set; }

        public string ExternalLink { get; set; }
        public List<string> Parts;
        public long LastUpdate { get; set; }
        public List<UpdateInfo> Updates;
    }

    public class LevelCatalog
    {
        public List<LevelInfo> Levels;
    }

    public class ScriptInfo
    {
        public string FileName { get; set; }
        public string Hash { get; set; }
        public int Size { get; set; }
        public List<string> Updates;
    }

    public class ScriptCatalog
    {
        public List<ScriptInfo> Scripts;
    }

    #endregion

    public static class ScriptCatalogLoader
    {
        public static ScriptCatalog scriptCatalog;

        public static bool downloading { get; private set; }
        private static IEnumerator StartDownloadInternal()
        {
            string newHash = "";
            UnityWebRequest hashReq = new UnityWebRequest(OnlineLevelsManager.GetGithubURL(OnlineLevelsManager.Repo.AngryLevels, "ScriptCatalogHash.txt"));
            try
            {
                hashReq.downloadHandler = new DownloadHandlerBuffer();
                yield return hashReq.SendWebRequest();

                if (hashReq.isHttpError || hashReq.isNetworkError)
                {
                    Debug.LogError("Could not download the script catalog hash");
                    yield break;
                }

                newHash = hashReq.downloadHandler.text;
            }
            finally
            {
                hashReq.Dispose();
            }

            string cachedCatalogPath = AngryPaths.ScriptCatalogCachePath;
            if (File.Exists(cachedCatalogPath))
            {
                string catalog = File.ReadAllText(cachedCatalogPath);
                string hash = CryptographyUtils.GetMD5String(catalog);
                if (hash == newHash)
                {
                    Debug.Log("Cached script catalog up to date");
                    scriptCatalog = JsonConvert.DeserializeObject<ScriptCatalog>(catalog);
                    yield break;
                }
            }

            UnityWebRequest updatedCatalogRequest = new UnityWebRequest(OnlineLevelsManager.GetGithubURL(OnlineLevelsManager.Repo.AngryLevels, "ScriptCatalog.json"));
            try
            {
                updatedCatalogRequest.downloadHandler = new DownloadHandlerBuffer();
                yield return updatedCatalogRequest.SendWebRequest();

                if (updatedCatalogRequest.isHttpError || updatedCatalogRequest.isNetworkError)
                {
                    Debug.LogError("Could not download the script catalog");
                    yield break;
                }

                scriptCatalog = JsonConvert.DeserializeObject<ScriptCatalog>(updatedCatalogRequest.downloadHandler.text);
                File.WriteAllText(cachedCatalogPath, updatedCatalogRequest.downloadHandler.text);
                string currentHash = CryptographyUtils.GetMD5String(updatedCatalogRequest.downloadHandler.text);

                if (currentHash != newHash)
                {
                    Debug.LogWarning($"New script catalog hash value does not match online catalog hash value, github page not cached yet (current hash is {currentHash}. online hash is {newHash})");
                }
            }
            finally
            {
                updatedCatalogRequest.Dispose();
            }
        }

        private static IEnumerator DownloadCoroutine()
        {
            if (downloading)
                yield break;

            downloading = true;

            try
            {
                yield return StartDownloadInternal();
            }
            finally
            {
                downloading = false;
            }
        }

        public static void StartDownload()
        {
            if (downloading)
                return;

            OnlineLevelsManager.instance.StartCoroutine(DownloadCoroutine());
        }

        public static bool ScriptExistsInCatalog(string script)
        {
            if (scriptCatalog == null)
                return false;
            return scriptCatalog.Scripts.Where(s => s.FileName == script).Any();
        }

        public static bool TryGetScriptInfo(string script, out ScriptInfo info)
        {
            info = scriptCatalog == null ? null : scriptCatalog.Scripts.Where(s => s.FileName == script).FirstOrDefault();
            return info != null;
        }

    }

    public class OnlineLevelsManager : MonoBehaviour
    {
        public static OnlineLevelsManager instance;
        public static ConfigPanel onlineLevelsPanel;
        public static ConfigDivision onlineLevelContainer;
        public static LoadingCircleField loadingCircle;

        public enum Repo
        {
            AngryLevelLoader,
            AngryLevels
        }

        public static string GetGithubURL(Repo repo, string path)
        {
            string branch = "release";
            if (Plugin.useDevelopmentBranch.value)
                branch = "dev";

            string repoName = "AngryLevels";
            switch (repo)
            {
                case Repo.AngryLevels:
                    repoName = "AngryLevels";
                    break;

                case Repo.AngryLevelLoader:
                    repoName = "AngryLevelLoader";
                    break;
            }

            return $"https://raw.githubusercontent.com/eternalUnion/{repoName}/{branch}/{path}";
        }

        // Filters
        public enum SortFilter
        {
            Name,
            Author,
            LastUpdate,
            ReleaseDate
        }

        public static BoolField showInstalledLevels;
        public static BoolField showUpdateAvailableLevels;
        public static BoolField showNotInstalledLevels;
        public static StringField authorFilter;
        public static EnumField<SortFilter> sortFilter;

        public static void Init()
        {
            if (instance == null)
            {
                instance = new GameObject().AddComponent<OnlineLevelsManager>();
                DontDestroyOnLoad(instance);
            }

            string cachedCatalogPath = AngryPaths.LevelCatalogCachePath;
            if (File.Exists(cachedCatalogPath))
                catalog = JsonConvert.DeserializeObject<LevelCatalog>(File.ReadAllText(cachedCatalogPath));

            var filterPanel = new ConfigPanel(onlineLevelsPanel, "Filters", "online_filters");
            filterPanel.hidden = true;

            new ConfigHeader(filterPanel, "State Filters");
            showInstalledLevels = new BoolField(filterPanel, "Installed", "online_installedLevels", true);
            showNotInstalledLevels = new BoolField(filterPanel, "Not installed", "online_notInstalledLevels", true);
            showUpdateAvailableLevels = new BoolField(filterPanel, "Update available", "online_updateAvailableLevels", true);
            sortFilter = new EnumField<SortFilter>(filterPanel, "Sort type", "sf_o_sortType", SortFilter.LastUpdate);
            sortFilter.onValueChange += (e) =>
            {
                sortFilter.value = e.value;
                SortAll();
            };
            new ConfigHeader(filterPanel, "Variable Filters");
            authorFilter = new StringField(filterPanel, "Author", "sf_o_authorFilter", "", true);

            var toolbar = new ButtonArrayField(onlineLevelsPanel, "online_toolbar", 2, new float[] { 0.5f, 0.5f }, new string[] { "Refresh", "Filters" });
            toolbar.OnClickEventHandler(0).onClick += RefreshAsync;
            toolbar.OnClickEventHandler(1).onClick += () => filterPanel.OpenPanel();

            loadingCircle = new LoadingCircleField(onlineLevelsPanel);
            loadingCircle.hidden = true;
            onlineLevelContainer = new ConfigDivision(onlineLevelsPanel, "p_onlineLevelsDiv");

            LoadThumbnailHashes();
        }

        public static void SortAll()
        {
            int i = 0;
            if (sortFilter.value == SortFilter.Name)
            {
                foreach (var bundle in onlineLevels.Values.OrderBy(b => b.bundleName))
                    bundle.siblingIndex = i++;
            }
            else if (sortFilter.value == SortFilter.Author)
            {
                foreach (var bundle in onlineLevels.Values.OrderBy(b => b.author))
                    bundle.siblingIndex = i++;
            }
            else if (sortFilter.value == SortFilter.LastUpdate)
            {
                foreach (var bundle in onlineLevels.Values.OrderByDescending(b => b.lastUpdate))
                {
                    bundle.siblingIndex = i++;
                }
            }
            else if (sortFilter.value == SortFilter.ReleaseDate)
            {
                for (int k = 0; k < catalog.Levels.Count; k++)
                {
                    if (onlineLevels.TryGetValue(catalog.Levels[k].Guid, out var level))
                        level.siblingIndex = i++;
                }
            }
        }

        private static bool downloadingCatalog = false;
        public static LevelCatalog catalog;
        public static Dictionary<string, OnlineLevelField> onlineLevels = new Dictionary<string, OnlineLevelField>();
        public static Dictionary<string, string> thumbnailHashes = new Dictionary<string, string>();

        public static void LoadThumbnailHashes()
        {
            thumbnailHashes.Clear();

            string thumbnailHashLocation = AngryPaths.ThumbnailCachePath;
            if (File.Exists(thumbnailHashLocation))
            {
                using (StreamReader hashReader = new StreamReader(File.Open(thumbnailHashLocation, FileMode.Open, FileAccess.Read)))
                {
                    while (!hashReader.EndOfStream)
                    {
                        string guid = hashReader.ReadLine();
                        if (hashReader.EndOfStream)
                        {
                            Debug.LogWarning("Invalid end of thumbnail cache hash file");
                            break;
                        }

                        string hash = hashReader.ReadLine();
                        thumbnailHashes[guid] = hash;
                    }
                }
            }
        }

        public static void SaveThumbnailHashes()
        {
            string thumbnailHashLocation = AngryPaths.ThumbnailCachePath;
            IOUtils.TryCreateDirectoryForFile(thumbnailHashLocation);

            using (FileStream fs = File.Open(thumbnailHashLocation, FileMode.OpenOrCreate, FileAccess.Write))
            {
                fs.Seek(0, SeekOrigin.Begin);
                fs.SetLength(0);

                using (StreamWriter sw = new StreamWriter(fs))
                {
                    foreach (var pair in thumbnailHashes)
                    {
                        sw.WriteLine(pair.Key);
                        sw.WriteLine(pair.Value);
                    }
                }
            }
        }

        public static void RefreshAsync()
        {
            ScriptCatalogLoader.StartDownload();

            if (downloadingCatalog)
                return;

            foreach (var field in onlineLevels.Values)
                field.hidden = true;

            onlineLevelContainer.hidden = true;
            loadingCircle.hidden = false;
            instance.StartCoroutine(instance.RefreshCoroutine());
        }

        private IEnumerator RefreshCoroutine()
        {
            if (downloadingCatalog)
                yield break;

            downloadingCatalog = true;

            try
            {
                yield return m_CheckCatalogVersion();
            }
            finally
            {
                downloadingCatalog = false;
                PostCatalogLoad();
            }
        }

        private IEnumerator m_CheckCatalogVersion()
        {
            LevelCatalog prevCatalog = catalog;
            catalog = null;

            string newCatalogHash = "";
            string cachedCatalogPath = AngryPaths.LevelCatalogCachePath;
            IOUtils.TryCreateDirectoryForFile(cachedCatalogPath);

            UnityWebRequest catalogVersionRequest = new UnityWebRequest(GetGithubURL(Repo.AngryLevels, "LevelCatalogHash.txt"));
            catalogVersionRequest.downloadHandler = new DownloadHandlerBuffer();
            yield return catalogVersionRequest.SendWebRequest();

            if (catalogVersionRequest.isNetworkError || catalogVersionRequest.isHttpError)
            {
                Debug.LogError("Could not download catalog version");
                catalog = null;
                yield break;
            }
            else
            {
                newCatalogHash = catalogVersionRequest.downloadHandler.text;
            }

            if (File.Exists(cachedCatalogPath))
            {
                string cachedCatalog = File.ReadAllText(cachedCatalogPath);
                string catalogHash = CryptographyUtils.GetMD5String(cachedCatalog);
                catalog = JsonConvert.DeserializeObject<LevelCatalog>(cachedCatalog);

                if (catalogHash == newCatalogHash)
                {
                    // Wait for script catalog
                    while (ScriptCatalogLoader.downloading)
                    {
                        yield return null;
                    }

                    Debug.Log("Current online level catalog is up to date, loading from cache");
                    yield break;
                }

                catalog = null;
            }

            Debug.Log("Current online level catalog is out of date, downloading from web");
            yield return m_DownloadCatalog(newCatalogHash, prevCatalog);
        }

        private IEnumerator m_DownloadCatalog(string newHash, LevelCatalog prevCatalog)
        {
            catalog = null;

            string catalogPath = AngryPaths.LevelCatalogCachePath;
            IOUtils.TryCreateDirectoryForFile(catalogPath);

            UnityWebRequest catalogRequest = new UnityWebRequest(GetGithubURL(Repo.AngryLevels, "LevelCatalog.json"));
            catalogRequest.downloadHandler = new DownloadHandlerFile(catalogPath);
            yield return catalogRequest.SendWebRequest();

            if (catalogRequest.isNetworkError || catalogRequest.isHttpError)
            {
                Debug.LogError("Could not download catalog");
                yield break;
            }
            else
            {
                while (ScriptCatalogLoader.downloading)
                {
                    yield return null;
                }

                string cachedCatalog = File.ReadAllText(catalogPath);
                string catalogHash = CryptographyUtils.GetMD5String(cachedCatalog);
                catalog = JsonConvert.DeserializeObject<LevelCatalog>(cachedCatalog);

                if (catalogHash != newHash)
                {
                    Debug.LogWarning($"Catalog hash does not match, github did not cache the new catalog yet (current hash is {catalogHash}. online hash is {newHash})");
                }

                if (Plugin.newLevelNotifierToggle.value && prevCatalog != null)
                {
                    List<string> newLevels = catalog.Levels.Where(level => prevCatalog.Levels.Where(l => l.Guid == level.Guid).FirstOrDefault() == null).Select(level => level.Name).ToList();

                    if (newLevels.Count != 0)
                    {
                        if (!string.IsNullOrEmpty(Plugin.newLevelNotifierLevels.value))
                            newLevels.AddRange(Plugin.newLevelNotifierLevels.value.Split('`'));
                        newLevels = newLevels.Distinct().ToList();
                        Plugin.newLevelNotifierLevels.value = string.Join("`", newLevels);

                        Plugin.newLevelNotifier.text = string.Join("\n", newLevels.Where(level => !string.IsNullOrEmpty(level)).Select(name => $"<color=lime>New level: {name}</color>"));
                        Plugin.newLevelNotifier.hidden = false;
                        Plugin.newLevelToggle.value = true;
                    }
                }
                else
                {
                    Plugin.newLevelNotifier.hidden = true;
                }
            }
        }

        public static void UpdateUI()
        {
            foreach (var levelField in onlineLevels.Values)
                levelField.UpdateUI();

            // Insertion sort not working properly for now
            SortAll();
        }

        private static void PostCatalogLoad()
        {
            loadingCircle.hidden = true;
            onlineLevelContainer.hidden = false;
            if (catalog == null)
                return;

            bool dirtyThumbnailCacheHashFile = false;
            foreach (LevelInfo info in catalog.Levels)
            {
                OnlineLevelField field;
                bool justCreated = false;
                if (!onlineLevels.TryGetValue(info.Guid, out field))
                {
                    justCreated = true;
                    field = new OnlineLevelField(onlineLevelContainer, info.Guid);
                    onlineLevels[info.Guid] = field;
                }

                // Update info text
                field.bundleName = info.Name;
                field.author = info.Author;
                field.bundleFileSize = info.Size;
                field.bundleBuildHash = info.Hash;
                field.lastUpdate = info.LastUpdate;

                // Update ui
                field.UpdateUI();

                // Update thumbnail if not cahced or out of date
                string imageCacheDir = AngryPaths.ThumbnailCacheFolderPath;
                IOUtils.TryCreateDirectory(imageCacheDir);
                string imageCachePath = Path.Combine(imageCacheDir, $"{info.Guid}.png");

                bool downloadThumbnail = false;
                bool thumbnailExists = File.Exists(imageCachePath);
                if (!thumbnailExists)
                {
                    downloadThumbnail = true;
                }
                else
                {
                    if (!thumbnailHashes.TryGetValue(info.Guid, out string currentHash))
                    {
                        currentHash = CryptographyUtils.GetMD5String(File.ReadAllBytes(imageCachePath));
                        thumbnailHashes.Add(info.Guid, currentHash);
                    }
                    
                    if (currentHash != info.ThumbnailHash)
                        downloadThumbnail = true;
                }

                if (downloadThumbnail)
                {
                    // Normally download handler can overwrite a file, but
                    // if the download is interrupted before the file can
                    // be downloaded, old file can persist until next
                    // thumbnail update
                    if (thumbnailExists)
                        File.Delete(imageCachePath);
                    dirtyThumbnailCacheHashFile = true;

                    UnityWebRequest req = new UnityWebRequest(GetGithubURL(Repo.AngryLevels, $"Levels/{info.Guid}/thumbnail.png"));
                    req.downloadHandler = new DownloadHandlerFile(imageCachePath);
                    var handle = req.SendWebRequest();

                    handle.completed += (e) =>
                    {
                        try
                        {
                            if (req.isHttpError || req.isNetworkError)
                                return;
                            thumbnailHashes[info.Guid] = CryptographyUtils.GetMD5String(imageCachePath);
                            field.DownloadPreviewImage("file://" + imageCachePath, true);
                        }
                        finally
                        {
                            req.Dispose();
                        }
                    };
                }
                else
                {
                    field.DownloadPreviewImage("file://" + imageCachePath, false);
                }

                // Sort if just created
                if (justCreated)
                    field.UpdateOrder();

                // Show the field if matches the filter
                field.hidden = false;
                if (field.status == OnlineLevelField.OnlineLevelStatus.notInstalled)
                {
                    if (!showNotInstalledLevels.value)
                        field.hidden = true;
                }
                else if (field.status == OnlineLevelField.OnlineLevelStatus.installed)
                {
                    if (!showInstalledLevels.value)
                        field.hidden = true;
                }
                else if (field.status == OnlineLevelField.OnlineLevelStatus.updateAvailable)
                {
                    if (!showUpdateAvailableLevels.value)
                        field.hidden = true;
                }

                if (field.hidden)
                    continue;

                if (!string.IsNullOrEmpty(authorFilter.value) && field.author.ToLower() != authorFilter.value.ToLower())
                    field.hidden = true;
            }

            if (dirtyThumbnailCacheHashFile)
                SaveThumbnailHashes();

            CheckLevelUpdateText();

            SortAll();
        }

        public static void CheckLevelUpdateText()
        {
            if (!Plugin.levelUpdateNotifierToggle.value)
            {
                Plugin.levelUpdateNotifier.hidden = true;
                return;
            }

            Plugin.levelUpdateNotifier.text = "";
            Plugin.levelUpdateNotifier.hidden = true;
            foreach (OnlineLevelField field in onlineLevels.Values)
            {
                if (field.status == OnlineLevelField.OnlineLevelStatus.updateAvailable)
                {
                    if (Plugin.levelUpdateIgnoreCustomBuilds.value)
                    {
                        AngryBundleContainer container = Plugin.GetAngryBundleByGuid(field.bundleGuid);
                        if (container != null)
                        {
                            LevelInfo info = catalog.Levels.Where(level => level.Guid == field.bundleGuid).First();
                            if (info.Updates != null && !info.Updates.Select(u => u.Hash).Contains(container.bundleData.buildHash))
                                continue;
                        }
                    }

                    if (Plugin.levelUpdateNotifier.text != "")
                        Plugin.levelUpdateNotifier.text += '\n';
                    Plugin.levelUpdateNotifier.text += $"<color=cyan>Update available for {field.bundleName}</color>";
                    Plugin.levelUpdateNotifier.hidden = false;
                }
            }
        }
    }
}
