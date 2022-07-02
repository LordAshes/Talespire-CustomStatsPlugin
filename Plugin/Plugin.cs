using BepInEx;
using BepInEx.Configuration;
using Bounce.TaleSpire.AssetManagement;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace LordAshes
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(LordAshes.FileAccessPlugin.Guid)]
    [BepInDependency(LordAshes.StatMessaging.Guid)]
    [BepInDependency(RadialUI.RadialUIPlugin.Guid)]
    public partial class CustomStatsPlugin : BaseUnityPlugin
    {
        // Plugin info
        public const string Name = "Custom Stats Plug-In";
        public const string Guid = "org.lordashes.plugins.customstats";
        public const string Version = "2.0.0.0";

        // Configuration
        private static class Internal
        {
            public static bool selectedMiniStatsOpen = false;
            public static Vector2 statMenuCentre = new Vector2(1920 / 2, 1080 / 2);
            public static Dictionary<string, string> selectedMiniStats = new Dictionary<string, string>();

            public static ConfigEntry<KeyboardShortcut> defineModel;
        }

        /// <summary>
        /// Function for initializing plugin
        /// This function is called once by TaleSpire
        /// </summary>
        void Awake()
        {
            // Not required but good idea to log this state for troubleshooting purpose
            UnityEngine.Debug.Log("Custom Stats Plugin: Active.");

            Internal.defineModel =  Config.Bind("Hotkeys", "Apply Model", new KeyboardShortcut(KeyCode.A, KeyCode.RightControl));

            if (Config.Bind("Settings", "Remove Core Stats Menu", true).Value)
            {
                RadialUI.RadialUIPlugin.HideDefaultCharacterMenuItem(CustomStatsPlugin.Guid, "Stats", null);
            }

            RadialUI.RadialUIPlugin.AddCustomButtonOnCharacter(CustomStatsPlugin.Guid, new MapMenu.ItemArgs()
            {
                Action = StatMenuCallback,
                CloseMenuOnActivate = true,
                FadeName = true,
                Icon = FileAccessPlugin.Image.LoadSprite("Stats.png"),
                Title = "Custom Stats",
            }, (a, b) => { return true; });

            Utility.PostOnMainPage(this.GetType());
        }

        private void StatMenuCallback(MapMenuItem arg1, object arg2)
        {
            Debug.Log("Custom Stats Plugin: Radial Stats Menu Callback");
            CreatureBoardAsset radialMenuSource;
            CreaturePresenter.TryGetAsset(new CreatureGuid(RadialUI.RadialUIPlugin.GetLastRadialTargetCreature()), out radialMenuSource);
            Vector3 screenPos = Camera.main.WorldToScreenPoint(radialMenuSource.GetHook(CreatureBoardAsset.HookTransform.HIT).position);
            Internal.statMenuCentre = new Vector2(screenPos.x, 1080-screenPos.y);
            Internal.selectedMiniStats.Clear();
            string json = StatMessaging.ReadInfo(radialMenuSource.CreatureId, CustomStatsPlugin.Guid + ".stats");
            if (json != null && json.Trim() != "")
            {
                Debug.Log("Custom Stats Plugin: Custom Stats JSON = "+json);
                Internal.selectedMiniStats = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            }
            Internal.selectedMiniStatsOpen = true;
            Debug.Log("Custom Stats Plugin: Syncing Core Stats With Custom Stats");
            CampaignSessionManager csm = GameObject.FindObjectOfType<CampaignSessionManager>();
            string[] statNames = (string[])PatchAssistant.GetField(csm, "_statNames");
            for(int statIndex=0; statIndex < statNames.Length; statIndex++)
            { 
                if(Internal.selectedMiniStats.ContainsKey(statNames[statIndex]))
                {
                    Debug.Log("Custom Stats Plugin: Syncing '"+ statNames[statIndex] + "'");
                    CreatureDataV2 data = default(CreatureDataV2);
                    CreatureManager.TryGetCreatureData(radialMenuSource.CreatureId, out data);
                    Internal.selectedMiniStats[statNames[statIndex]] = data.StatByIndex(statIndex).Value + "/" + data.StatByIndex(statIndex).Max;
                }
            }
        }

        void Update()
        {
            if (Utility.isBoardLoaded())
            {
                if(Utility.StrictKeyCheck(Internal.defineModel.Value))
                {
                    SystemMessage.AskForTextInput("Apply Custom Stats", "Custom Stats Model:", "Apply", (modelName) => 
                    {
                        Internal.selectedMiniStats.Clear();
                        string[] modelFiles = FileAccessPlugin.File.Find(modelName + ".model");
                        if (modelFiles.Length>0)
                        {
                            if(modelFiles.Length>1)
                            {
                                Debug.LogWarning("Custom Stats Plugin: Multiple Definitions For '" + modelName + "' ("+modelName+".model) Exist. Using First.");
                            }
                            foreach (string stat in FileAccessPlugin.File.ReadAllText(modelFiles[0]).Split(','))
                            {
                                Internal.selectedMiniStats.Add(stat, "---");
                            }
                            string json = JsonConvert.SerializeObject(Internal.selectedMiniStats);
                            StatMessaging.SetInfo(LocalClient.SelectedCreatureId, CustomStatsPlugin.Guid + ".stats", json);
                        }
                        else
                        {
                            SystemMessage.DisplayInfoText("Custom Stats Plugin: Unable To Find Definition For Model '" + modelName + "'");
                        }
                    }, null, "Cancel", null, "");
                }
            }
        }

        void OnGUI()
        {
            if (Internal.selectedMiniStatsOpen)
            {
                GUIStyle gsLabel = new GUIStyle();
                gsLabel.alignment = TextAnchor.MiddleCenter;
                gsLabel.fontSize = 16;
                gsLabel.normal.textColor = UnityEngine.Color.red;
                GUI.Box(new Rect(Internal.statMenuCentre.x-10, Internal.statMenuCentre.y, 20, 20), Texture2D.blackTexture);
                if (GUI.Button(new Rect(Internal.statMenuCentre.x-10, Internal.statMenuCentre.y, 20, 20), "X", gsLabel))
                {
                    string json = JsonConvert.SerializeObject(Internal.selectedMiniStats);
                    Debug.Log("Custom Stats Plugin: Updating Custom Stats");
                    StatMessaging.SetInfo(new CreatureGuid(RadialUI.RadialUIPlugin.GetLastRadialTargetCreature()), CustomStatsPlugin.Guid + ".stats", json);
                    Debug.Log("Custom Stats Plugin: Updating Core Stats");
                    UpdateCoreStats(new CreatureGuid(RadialUI.RadialUIPlugin.GetLastRadialTargetCreature()), Internal.selectedMiniStats);
                    Internal.selectedMiniStatsOpen = false;
                }
                float degDelta = 360.0f / Internal.selectedMiniStats.Count;
                float dist = 25f + (Internal.selectedMiniStats.Count * 10);
                for (int statIndex = 0; statIndex< Internal.selectedMiniStats.Count; statIndex++)
                {
                    Vector2 pos = dist * Rotate(new Vector2(0, 1), degDelta * statIndex);
                    float offsetY = (degDelta * statIndex >= 90 && degDelta * statIndex <= 270) ? -20 : 20;
                    gsLabel.normal.textColor = UnityEngine.Color.black;
                    GUI.Label(new Rect(Internal.statMenuCentre.x + pos.x - 100, Internal.statMenuCentre.y +  pos.y + offsetY, 200, 20), Internal.selectedMiniStats.ElementAt(statIndex).Key, gsLabel);
                    gsLabel.normal.textColor = UnityEngine.Color.white;
                    GUI.Label(new Rect(Internal.statMenuCentre.x + pos.x+2 - 100, Internal.statMenuCentre.y + pos.y+2 + offsetY, 200, 20), Internal.selectedMiniStats.ElementAt(statIndex).Key, gsLabel);
                    Internal.selectedMiniStats[Internal.selectedMiniStats.ElementAt(statIndex).Key] = GUI.TextField(new Rect(Internal.statMenuCentre.x + pos.x - 30, Internal.statMenuCentre.y + pos.y, 60, 20), Internal.selectedMiniStats.ElementAt(statIndex).Value);
                }
            }
        }

        public void UpdateCoreStats(CreatureGuid cid, Dictionary<string,string> stats)
        {
            string[] statNames = (string[])PatchAssistant.GetField(GameObject.FindObjectOfType<CampaignSessionManager>(), "_statNames");
            foreach (KeyValuePair<string,string> stat in stats)
            {
                bool found = false;
                for (int i = 0; i < statNames.Length; i++)
                {
                    if (stat.Key==statNames[i])
                    {
                        Debug.Log("Custom Stats Plugin: Setting Stat '" + stat.Key + "' At Index " + i+" To "+stat.Value);
                        CreatureStat cStat;
                        if (stat.Value.Split('/').Length < 2)
                        {
                            cStat = new CreatureStat(float.Parse(stat.Value), float.Parse(stat.Value));
                        }
                        else
                        {
                            cStat = new CreatureStat(float.Parse(stat.Value.Split('/')[0]), float.Parse(stat.Value.Split('/')[1]));
                        }
                        CreatureManager.SetCreatureStatByIndex(cid, cStat, i);
                        found = true;
                        break;
                    }
                }
                if (!found) { Debug.Log("Custom Stats Plugin: Stat '" + stat.Key + "' Not Found In Core Stats"); }
            }
        }

        public static Vector2 Rotate(Vector2 v, float deltaDeg)
        {
            return new Vector2(v.x * Mathf.Cos(Mathf.Deg2Rad*deltaDeg) - v.y * Mathf.Sin(Mathf.Deg2Rad*deltaDeg), v.x * Mathf.Sin(Mathf.Deg2Rad*deltaDeg) + v.y * Mathf.Cos(Mathf.Deg2Rad*deltaDeg));
        }
    }
}
