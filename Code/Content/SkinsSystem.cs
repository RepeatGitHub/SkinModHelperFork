using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Mono.Cecil.Cil;
using Celeste.Mod.UI;
using System.Xml;
using System.Linq;

using static Celeste.Mod.SkinModHelper.SkinModHelperModule;

namespace Celeste.Mod.SkinModHelper {
    public class SkinsSystem {
        #region

        public static SkinModHelperSettings Settings => (SkinModHelperSettings)Instance._Settings;
        public static SkinModHelperSession Session => (SkinModHelperSession)Instance._Session;

        public static Dictionary<string, string> SpriteSkin_record = new();
        public static Dictionary<string, List<string>> SpriteSkins_records = new();

        public static Dictionary<string, string> PortraitsSkin_record = new();
        public static Dictionary<string, List<string>> PortraitsSkins_records = new();

        public static Dictionary<string, string> OtherSkin_record = new();
        public static Dictionary<string, List<string>> OtherSkins_records = new();

        public static Dictionary<string, SkinModHelperConfig> skinConfigs = new();
        public static Dictionary<string, SkinModHelperConfig> OtherskinConfigs = new();
        public static Dictionary<string, SkinModHelperOldConfig> OtherskinOldConfig = new();

        public static void Load() {
            Everest.Content.OnUpdate += EverestContentUpdateHook;

            On.Celeste.Player.Update += PlayerUpdateHook;

            On.Monocle.SpriteBank.Create += SpriteBankCreateHook;
            On.Monocle.SpriteBank.CreateOn += SpriteBankCreateOnHook;
            On.Monocle.Atlas.GetAtlasSubtextures += GetAtlasSubtexturesHook;
            On.Monocle.Sprite.ctor_Atlas_string += SpriteCtorAtlasStringHook;
        }

        public static void Unload() {
            Everest.Content.OnUpdate -= EverestContentUpdateHook;

            On.Celeste.Player.Update -= PlayerUpdateHook;

            On.Monocle.SpriteBank.Create -= SpriteBankCreateHook;
            On.Monocle.SpriteBank.CreateOn -= SpriteBankCreateOnHook;
            On.Monocle.Atlas.GetAtlasSubtextures -= GetAtlasSubtexturesHook;
            On.Monocle.Sprite.ctor_Atlas_string -= SpriteCtorAtlasStringHook;
        }

        #endregion
        #region

        //When Character_ID appears in the config file, that ID will be automatically added here.
        //In other words, don't add hook for this.
        public static readonly List<string> spritesWithHair = new();

        public static readonly int MAX_DASHES = 32;
        public static readonly int MAX_HAIRLENGTH = 99;

        public static readonly string DEFAULT = "Default";
        public static readonly string ORIGINAL = "Original";
        public static readonly string LockedToPlayer = "LockedToPlayer";

        public static int Player_Skinid_verify;

        public static bool? actualBackpack;
        public static bool backpackOn = true;

        /// <summary> 0-Default, 1-Invert, 2-Off, 3-On </summary>
        public static int backpackSetting = 0;

        public static bool first_build = true;
        public static List<string> FailedXml_record = new();
        public static Dictionary<string, SpriteBank> Xml_records = new();

        /// <summary> Similar to GFX.FxColorGrading, But indexing new color on colorGrade only based the rgb color of the texture source. </summary>
        public static Effect FxColorGrading_SMH;
        #endregion

        //-----------------------------Build Skins-----------------------------
        #region
        private static void EverestContentUpdateHook(ModAsset oldAsset, ModAsset newAsset) {
            if (newAsset != null && newAsset.PathVirtual.StartsWith("SkinModHelperConfig")) {
                ReloadSettings();
                Logger.Log(LogLevel.Warn, "SkinModHelper", $"If you encounter a loading failure, please just restart the game!");
                Logger.Log(LogLevel.Warn, "SkinModHelper", $"Or add 'SkinModHelperPlus.zip' to mods whitelist (if you have)");
                RefreshSkins(true, false);
            }
        }
        public static void ReloadSettings() {
            spritesWithHair.Clear();
            skinConfigs.Clear();
            OtherskinConfigs.Clear();
            OtherskinOldConfig.Clear();

            Instance.LoadSettings();
            List<int> hashValues = new() { 0, 1, 2, 3, 4, 444482, 444483 };

            foreach (ModContent mod in Everest.Content.Mods) {
                if (mod.Map.TryGetValue("SkinModHelperConfig", out ModAsset configAsset) && configAsset.Type == typeof(AssetTypeYaml)) {

                    #region // Check if config from v0.7 Before
                    if (LoadConfigFile<SkinModHelperOldConfig>(configAsset, out var old_config)) {
                        if (string.IsNullOrEmpty(old_config.SkinId) || old_config.SkinId.EndsWith("_")) {
                            Logger.Log(LogLevel.Warn, "SkinModHelper", $"Invalid skin name '{old_config.SkinId}', will not register.");
                            continue;
                        }
                        SkinModHelperConfig config = new(old_config);

                        if (config.SkinName == DEFAULT || config.SkinName == ORIGINAL || config.SkinName == LockedToPlayer ||
                            OtherskinConfigs.ContainsKey(config.SkinName) || skinConfigs.ContainsKey(config.SkinName)) {
                            Logger.Log(LogLevel.Warn, "SkinModHelper", $"skin name '{config.SkinName}' has been taken.");
                            continue;
                        }

                        Logger.Log(LogLevel.Info, "SkinModHelper", $"Registered old-ver General skin: {config.SkinName}");
                        OtherskinConfigs.Add(config.SkinName, config);
                        OtherskinOldConfig.Add(config.SkinName, old_config);
                        continue;
                    }
                    #endregion

                    if (!LoadConfigFile<List<SkinModHelperConfig>>(configAsset, out var configs) || configs.Count < 1) {
                        continue;
                    }
                    #region // New config
                    foreach (SkinModHelperConfig config in configs) {
                        Regex skinIdRegex = new(@"^[a-zA-Z0-9]+_[a-zA-Z0-9]+$");

                        if (string.IsNullOrEmpty(config.SkinName) || config.SkinName.EndsWith("_")) {
                            Logger.Log(LogLevel.Warn, "SkinModHelper", $"Invalid skin name '{config.SkinName}', will not register.");
                            continue;

                        } else if (config.SkinName == DEFAULT || config.SkinName == ORIGINAL || config.SkinName == LockedToPlayer ||
                            OtherskinConfigs.ContainsKey(config.SkinName) || skinConfigs.ContainsKey(config.SkinName)) {
                            Logger.Log(LogLevel.Warn, "SkinModHelper", $"skin name '{config.SkinName}' has been taken.");
                            continue;
                        }
                        //---------------------GeneralSkin------------------------#
                        if (!string.IsNullOrEmpty(config.OtherSprite_ExPath)) {
                            if (config.OtherSprite_ExPath.EndsWith("/")) { config.OtherSprite_ExPath = config.OtherSprite_ExPath.Remove(config.OtherSprite_ExPath.LastIndexOf("/")); }

                            Logger.Log(config.General_List == false ? LogLevel.Debug : LogLevel.Info, "SkinModHelper", $"Registered new General skin: {config.SkinName}");
                            OtherskinConfigs.Add(config.SkinName, config);
                        }
                        //--------------------------------------------------------#
                        //---------------------PlayerSkin-------------------------
                        if (!string.IsNullOrEmpty(config.Character_ID)) {
                            if (string.IsNullOrEmpty(config.hashSeed)) { config.hashSeed = config.SkinName; }
                            config.hashValues = getHash(config.hashSeed) + 1;

                            //----------------JungleLantern---------------
                            if (config.SkinName.EndsWith("_lantern_NB") || config.SkinName.EndsWith("_lantern")) {
                                config.JungleLanternMode = true;
                                if (config.Silhouette_List || config.Player_List) {
                                    Logger.Log(LogLevel.Warn, "SkinModHelper", $"'{config.SkinName}' this name will affect the gameplay of JungleHelper, it should't appear in the options.");
                                }
                                config.Silhouette_List = false;
                                config.Player_List = false;
                            }
                            //--------------------------------------------#
                            if (!spritesWithHair.Contains(config.Character_ID)) { spritesWithHair.Add(config.Character_ID); }

                            if (!string.IsNullOrEmpty(config.OtherSprite_Path)) {
                                if (config.OtherSprite_Path.EndsWith("/")) { config.OtherSprite_Path = config.OtherSprite_Path.Remove(config.OtherSprite_Path.LastIndexOf("/")); }
                            }
                            if (!hashValues.Contains(config.hashValues)) {
                                hashValues.Add(config.hashValues);

                                string s = "   ";
                                for (int i = config.SkinName.Length; i < 32; s += " ", i++) { }
                                Logger.Log(LogLevel.Info, "SkinModHelper", $"Registered new player skin: {config.SkinName}{s}{config.hashValues}");

                                skinConfigs.Add(config.SkinName, config);
                            } else {
                                Logger.Log(LogLevel.Error, "SkinModHelper", $"Player skin '{config.SkinName}' happened hash value conflict! cannot registered.");
                            }
                        }
                        //--------------------------------------------------------#
                    }
                    #endregion
                }
            }
            first_build = true;
            if (Settings.SelectedPlayerSkin == null) {
                Settings.SelectedPlayerSkin = DEFAULT;
            }
            if (Settings.SelectedSilhouetteSkin == null) {
                Settings.SelectedSilhouetteSkin = DEFAULT;
            }
        }
        private static int getHash(string hash_send) {
            if (hash_send == null) {
                throw new Exception("null hash send");
            }
            int hashValue;

            unchecked {
                int num = 352654597;
                int num_2 = num;

                for (int i = 0; i < hash_send.Length; i += 2) {
                    num = ((num << 5) + num) ^ hash_send[i];
                    if (i == hash_send.Length - 1) { break; }
                    num_2 = ((num_2 << 5) + num_2) ^ hash_send[i + 1];
                }
                hashValue = num + (num_2 * 1566083941);
            }

            if (hashValue < 0) { hashValue += (1 << 31); }
            return hashValue;
        }
        #endregion

        //-----------------------------Sprite Banks / Skin Xmls-----------------------------
        #region
        private static Sprite SpriteBankCreateHook(On.Monocle.SpriteBank.orig_Create orig, SpriteBank self, string id) {
            string newId = id;

            if (self == GFX.SpriteBank) {
                newId = GetSpriteBankIDSkin(id);
            } else if (self == GFX.PortraitsSpriteBank) {
                newId = GetPortraitsBankIDSkin(id);
            }

            if (self.Has(newId)) {
                id = newId;
            }
            return orig(self, id);
        }
        private static Sprite SpriteBankCreateOnHook(On.Monocle.SpriteBank.orig_CreateOn orig, SpriteBank self, Sprite sprite, string id) {
            string newId = id;

            if (self == GFX.SpriteBank) {
                newId = GetSpriteBankIDSkin(id);
            } else if (self == GFX.PortraitsSpriteBank) {
                newId = GetPortraitsBankIDSkin(id);
            }

            if (self.Has(newId)) {
                id = newId;
                if (sprite is PlayerSprite playerSprite) {
                    DynamicData.For(playerSprite).Set("spriteName", id);
                }
            }
            return orig(self, sprite, id);
        }
        #endregion

        #region
        // Combine skin mod XML with a vanilla sprite bank
        private static void CombineSpriteBanks(SpriteBank origBank, string skinId, string xmlPath, bool Enabled) {
            SpriteBank newBank = BuildBank(origBank, skinId, xmlPath);
            if (newBank == null) {
                return;
            }

            // For each overridden sprite, patch it and add it to the original bank with a unique identifier
            foreach (KeyValuePair<string, SpriteData> spriteDataEntry in newBank.SpriteData) {
                string spriteId = spriteDataEntry.Key;
                SpriteData newSpriteData = spriteDataEntry.Value;

                if (origBank.SpriteData.TryGetValue(spriteId, out SpriteData origSpriteData)) {
                    PatchSprite(origSpriteData.Sprite, newSpriteData.Sprite);

                    string newSpriteId = spriteId + skinId;
                    origBank.SpriteData[newSpriteId] = newSpriteData;

                    if (origBank == GFX.SpriteBank && !string.IsNullOrEmpty(skinId)) {

                        // "SpriteSkin_record" initialization
                        SpriteSkin_record[spriteId] = null;

                        // Automatically check if origID has Metadata.
                        MTexture mTexture = origSpriteData.Sprite.Has("idle") ? origSpriteData.Sprite.GetFrame("idle", 0) : origSpriteData.Sprite.Texture;
                        if (new DynamicData(typeof(PlayerSprite)).Get<Dictionary<string, PlayerAnimMetadata>>("FrameMetadata").ContainsKey($"{mTexture}")) {
                            PlayerSprite.CreateFramesMetadata(newSpriteId);
                        }
                    } else if (origBank == GFX.PortraitsSpriteBank && !string.IsNullOrEmpty(skinId)) {

                        // "PortraitsSkin_record" initialization
                        PortraitsSkin_record[spriteId] = null;
                    }
                }
            }
        }
        public static void RecordSpriteBanks_Start() {
            SpriteSkins_records.Clear();
            PortraitsSkins_records.Clear();
            OtherSkins_records.Clear();

            foreach (SkinModHelperConfig config in skinConfigs.Values) {
                if (!string.IsNullOrEmpty(config.OtherSprite_Path)) {

                    string spritesXmlPath = $"Graphics/{config.OtherSprite_Path}/Sprites.xml";
                    string portraitsXmlPath = $"Graphics/{config.OtherSprite_Path}/Portraits.xml";

                    RecordSpriteBanks(GFX.SpriteBank, DEFAULT, spritesXmlPath);
                    RecordSpriteBanks(GFX.PortraitsSpriteBank, DEFAULT, portraitsXmlPath);

                    // This name is not actually used... just for ease of search (why?)
                    string Name = config.SkinName + "_+";
                    RecordOtherSprite(GFX.Game, $"{config.OtherSprite_Path}/death_particle", "death_particle", Name);
                    RecordOtherSprite(GFX.Game, $"{config.OtherSprite_Path}/objects/dreamblock/particles", "dreamblock_particles", Name);
                    RecordOtherSprite(GFX.Game, $"{config.OtherSprite_Path}/particles/feather", "feather_particles", Name);
                    RecordOtherSprite(MTN.Mountain, $"{config.OtherSprite_Path}/marker/runBackpack", "Mountain_marker", Name, true);
                    RecordOtherSprite(GFX.Game, $"{config.OtherSprite_Path}/Gui/hover/highlight", "highlight", Name);
                    RecordOtherSprite(GFX.Game, $"{config.OtherSprite_Path}/Gui/hover/idle", "idle", Name);
                }
            }

            foreach (SkinModHelperConfig config in OtherskinConfigs.Values) {
                if (!string.IsNullOrEmpty(config.OtherSprite_ExPath)) {

                    string spritesXmlPath = $"Graphics/{config.OtherSprite_ExPath}/Sprites.xml";
                    string portraitsXmlPath = $"Graphics/{config.OtherSprite_ExPath}/Portraits.xml";

                    RecordSpriteBanks(GFX.SpriteBank, config.SkinName, spritesXmlPath);
                    RecordSpriteBanks(GFX.PortraitsSpriteBank, config.SkinName, portraitsXmlPath);

                    RecordOtherSprite(GFX.Game, $"{config.OtherSprite_ExPath}/death_particle", "death_particle", config.SkinName);
                    RecordOtherSprite(GFX.Game, $"{config.OtherSprite_ExPath}/objects/dreamblock/particles", "dreamblock_particles", config.SkinName);
                    RecordOtherSprite(GFX.Game, $"{config.OtherSprite_ExPath}/particles/feather", "feather_particles", config.SkinName);
                    RecordOtherSprite(MTN.Mountain, $"{config.OtherSprite_ExPath}/marker/runBackpack", "Mountain_marker", config.SkinName, true);
                    RecordOtherSprite(GFX.Game, $"{config.OtherSprite_Path}/Gui/hover/highlight", "highlight", Name);
                    RecordOtherSprite(GFX.Game, $"{config.OtherSprite_Path}/Gui/hover/idle", "idle", Name);
                }
            }
        }
        public static void RecordOtherSprite(Atlas atlas, string spritePath, string otherSkin, string skinId, bool number_search = false) {
            if ((number_search && atlas.HasAtlasSubtexturesAt(spritePath, 0)) || atlas.Has(spritePath)) {
                RecordSpriteBanks(null, skinId, null, otherSkin);
            }
        }

        private static void RecordSpriteBanks(SpriteBank origBank, string skinId, string xmlPath, string otherSkin = null) {
            if (otherSkin == null) {
                SpriteBank newBank = BuildBank(origBank, skinId, xmlPath);
                if (newBank == null) {
                    return;
                }

                foreach (KeyValuePair<string, SpriteData> spriteDataEntry in newBank.SpriteData) {
                    string spriteId = spriteDataEntry.Key;
                    if (!string.IsNullOrEmpty(skinId)) {
                        if (origBank == GFX.SpriteBank && origBank.SpriteData.ContainsKey(spriteId)) {

                            if (!SpriteSkins_records.ContainsKey(spriteId)) {
                                SpriteSkins_records.Add(spriteId, new());
                            }
                            if (skinId != DEFAULT && !SpriteSkins_records[spriteId].Contains(skinId)) {
                                SpriteSkins_records[spriteId].Add(skinId);
                            }
                        } else if (origBank == GFX.PortraitsSpriteBank && origBank.SpriteData.ContainsKey(spriteId)) {

                            if (!PortraitsSkins_records.ContainsKey(spriteId)) {
                                PortraitsSkins_records.Add(spriteId, new());
                            }
                            if (skinId != DEFAULT && !PortraitsSkins_records[spriteId].Contains(skinId)) {
                                PortraitsSkins_records[spriteId].Add(skinId);
                            }
                        }
                    }
                }
            } else {
                string spriteId = otherSkin;
                if (!OtherSkins_records.ContainsKey(spriteId)) {
                    OtherSkins_records.Add(spriteId, new());
                }
                if (skinId != DEFAULT && !OtherSkins_records[spriteId].Contains(skinId)) {
                    OtherSkins_records[spriteId].Add(skinId);
                }
            }
        }
        private static SpriteBank BuildBank(SpriteBank origBank, string skinId, string xmlPath) {
            string dir = xmlPath.Remove(xmlPath.LastIndexOf("/"));

            if (Xml_records.TryGetValue(xmlPath, out SpriteBank newBank)) {
                return newBank;

            } else if (FailedXml_record.Contains(dir) || FailedXml_record.Contains(xmlPath)) {
                return null;

            } else if (!AssetExists<AssetTypeDirectory>(dir)) {
                FailedXml_record.Add(dir);
                Logger.Log(LogLevel.Error, "SkinModHelper", $"The xmls directory of '{skinId}' does not exist: {dir}");
                return null;

            } else if (AssetExists<AssetTypeXml>(xmlPath)) {
                try {
                    SpriteBank newBank_2 = new SpriteBank(origBank.Atlas, xmlPath);
                    return Xml_records[xmlPath] = newBank_2;
                } catch (Exception e) {
                    Logger.Log(LogLevel.Error, "SkinModHelper", $"The {xmlPath.Replace(dir + "/", "")} of '{skinId.Replace("_+", "")}' build failed! \n {xmlPath}: {e.Message}");
                }
            }
            FailedXml_record.Add(xmlPath);
            return null;
        }
        #endregion

        //-----------------------------Other Sprite-----------------------------
        #region
        public static void UpdateParticles() {
            FlyFeather.P_Collect.Source = GFX.Game["particles/feather"];
            FlyFeather.P_Boost.Source = GFX.Game["particles/feather"];

            string CustomPath = "particles/feather";

            string SpriteID = "feather_particles";
            if (OtherSkins_records.ContainsKey(SpriteID)) {
                CustomPath = getOtherSkin_ReskinPath(GFX.Game, "particles/feather", SpriteID);
            }

            if (CustomPath != null) {
                FlyFeather.P_Collect.Source = GFX.Game[CustomPath];
                FlyFeather.P_Boost.Source = GFX.Game[CustomPath];
            }
        }
        public static string searchTransform_withBackPack(Atlas atlas, string path) {
            if (atlas == MTN.Mountain) {
                if (path == "marker/runNoBackpack" && (backpackSetting == 3 || backpackSetting == 1)) {
                    path = "marker/runBackpack";
                } else if (path == "marker/runBackpack" && (backpackSetting == 2 || backpackSetting == 1)) {
                    path = "marker/runNoBackpack";
                }
            }
            return path;
        }
        #endregion

        #region
        private static List<MTexture> GetAtlasSubtexturesHook(On.Monocle.Atlas.orig_GetAtlasSubtextures orig, Atlas self, string path) {
            string SpriteID = null;
            bool number_search = false;

            if (self == MTN.Mountain) {
                if (path == "marker/runNoBackpack" || path == "marker/Fall" || path == "marker/runBackpack") {
                    path = searchTransform_withBackPack(self, path);
                    SpriteID = "Mountain_marker";
                    number_search = true;
                }
            }

            if (SpriteID != null && OtherSkins_records.ContainsKey(SpriteID)) {
                RefreshSkinValues_OtherExtra(SpriteID, null, true, false);
                path = getOtherSkin_ReskinPath(self, path, SpriteID, number_search);
            }
            return orig(self, path);
        }
        private static void SpriteCtorAtlasStringHook(On.Monocle.Sprite.orig_ctor_Atlas_string orig, Sprite self, Atlas atlas, string path) {
            string SpriteID = null;
            bool number_search = false;

            if (atlas == MTN.Mountain) {
                if (path == "marker/runNoBackpack" || path == "marker/Fall" || path == "marker/runBackpack") {
                    path = searchTransform_withBackPack(atlas, path);
                    SpriteID = "Mountain_marker";
                    number_search = true;
                }
            }

            if (SpriteID != null && OtherSkins_records.ContainsKey(SpriteID)) {
                RefreshSkinValues_OtherExtra(SpriteID, null, true, false);
                path = getOtherSkin_ReskinPath(atlas, path, SpriteID, number_search);
            }
            orig(self, atlas, path);
        }
        #endregion

        //-----------------------------Skins Refresh-----------------------------
        #region
        public static void RefreshSkins(bool Xmls_refresh, bool inGame = true) {
            if (!inGame) {
                Player_Skinid_verify = 0;

                string skinName = GetPlayerSkin();
                if (skinName != null) {
                    Player_Skinid_verify = skinConfigs[skinName].hashValues;
                }
            }

            if (Xmls_refresh == true) {
                LogLevel logLevel = Logger.GetLogLevel("Atlas");
                if (!first_build) { Logger.SetLogLevel("Atlas", LogLevel.Error); }

                first_build = false;
                Xml_records.Clear();
                FailedXml_record.Clear();

                #region
                foreach (string sprite in spritesWithHair) {
                    if (GFX.SpriteBank.SpriteData.ContainsKey(sprite)) {
                        PlayerSprite.CreateFramesMetadata(sprite);
                    } else {
                        throw new Exception($"[SkinModHelper] '{sprite}' does not exist in Graphics/Sprites.xml");
                    }
                }
                bool Enabled = false;
                foreach (SkinModHelperConfig config in OtherskinConfigs.Values) {
                    Enabled = Settings.ExtraXmlList.ContainsKey(config.SkinName) && Settings.ExtraXmlList[config.SkinName];

                    if (!string.IsNullOrEmpty(config.OtherSprite_ExPath)) {
                        string spritesXmlPath = $"Graphics/{config.OtherSprite_ExPath}/Sprites.xml";
                        string portraitsXmlPath = $"Graphics/{config.OtherSprite_ExPath}/Portraits.xml";

                        CombineSpriteBanks(GFX.SpriteBank, config.SkinName, spritesXmlPath, Enabled);
                        CombineSpriteBanks(GFX.PortraitsSpriteBank, config.SkinName, portraitsXmlPath, Enabled);
                    }
                }
                foreach (SkinModHelperConfig config in skinConfigs.Values) {
                    Enabled = Player_Skinid_verify == config.hashValues;

                    if (!string.IsNullOrEmpty(config.OtherSprite_Path)) {
                        string spritesXmlPath = $"Graphics/{config.OtherSprite_Path}/Sprites.xml";
                        string portraitsXmlPath = $"Graphics/{config.OtherSprite_Path}/Portraits.xml";

                        // SaveFilePortraits doesn't seem to like numbers...
                        CombineSpriteBanks(GFX.SpriteBank, $"{config.SkinName}_+", spritesXmlPath, Enabled);
                        CombineSpriteBanks(GFX.PortraitsSpriteBank, $"{config.SkinName}_+", portraitsXmlPath, Enabled);
                    }
                }
                #endregion

                Logger.SetLogLevel("Atlas", LogLevel.Error);
                RecordSpriteBanks_Start();
                Logger.SetLogLevel("Atlas", logLevel);
            }
            RefreshSkinValues(null, inGame);
        }

        private static void PlayerUpdateHook(On.Celeste.Player.orig_Update orig, Player self) {
            orig(self);

            // PandorasBox have an function that can generate the second player entity, we don't want to detect it.
            if (Engine.Scene?.Tracker.GetEntity<Player>() != self) {
                return;
            }

            int player_skinid_verify = 0;
            string SkinName = GetPlayerSkinName((int)self.Sprite.Mode);

            if (SkinName != null) {
                player_skinid_verify = (int)self.Sprite.Mode;
            }

            if (Player_Skinid_verify != player_skinid_verify) {
                Player_Skinid_verify = player_skinid_verify;
                RefreshSkins(false);
            }
        }

        #endregion

        //-----------------------------Method-----------------------------
        #region
        /// <summary> 
        /// Copies the animations of origSprite that newSprite missing to newSprite.
        /// </summary>
        public static void PatchSprite(Sprite origSprite, Sprite newSprite) {
            Dictionary<string, Sprite.Animation> newAnims = newSprite.GetAnimations();

            // Shallow copy... sometimes new animations get added mid-update?
            Dictionary<string, Sprite.Animation> oldAnims = new(origSprite.GetAnimations());
            foreach (KeyValuePair<string, Sprite.Animation> animEntry in oldAnims) {
                string origAnimId = animEntry.Key;
                Sprite.Animation origAnim = animEntry.Value;
                if (!newAnims.ContainsKey(origAnimId)) {
                    newAnims[origAnimId] = origAnim;
                }
            }
        }

        public static bool LoadConfigFile<T>(ModAsset skinConfigYaml, out T t) {
            return skinConfigYaml.TryDeserialize(out t);
        }
        public static T searchSkinConfig<T>(string FilePath) {
            foreach (ModContent mod in Everest.Content.Mods) {
                if (mod.Map.TryGetValue(FilePath, out ModAsset configAsset) && configAsset.Type == typeof(AssetTypeYaml)) {
                    return configAsset.Deserialize<T>();
                }
            }
            return default(T);
        }
        public static float GetAlpha(Color c) {
            return c.A == 0 ? 0f : c.A / 255f;
        }
        /// <summary> 
        /// A method similar to Color.Multiply, but ignore alpha value
        /// </summary>
        public static Color ColorBlend(Color c1, object obj) {
            if (obj is Color c2 && c2.A != 0) {
                // Restore c2's brightness when as 100% opacity, and assume its brightness if as c1's opacity.
                c2 = c2 * (255f / c2.A) * GetAlpha(c1);
                return new Color(c1.R * c2.R / 255, c1.G * c2.G / 255, c1.B * c2.B / 255, c1.A);
            } else if (obj is float f) {
                return new Color((int)(c1.R * f), (int)(c1.G * f), (int)(c1.B * f), c1.A);
            }
            return c1;
        }
        /// <returns> 
        /// return false if target's RGB over sample
        /// </returns>
        public static bool ColorSplitter(Color target, Color sample, out Color? value) {
            value = null;
            if (target.A == 0 || sample.A == 0) {
                return false;
            }
            target = target * (255f / target.A);
            sample = sample * (255f / sample.A);
            if (target.R > sample.R || target.G > sample.G || target.B > sample.B) {
                return false;
            } else if (target == sample) {
                value = Color.White;
                return true;
            }
            int R = sample.R == 0 ? 0 : target.R * 255 / sample.R;
            int G = sample.G == 0 ? 0 : target.G * 255 / sample.G;
            int B = sample.B == 0 ? 0 : target.B * 255 / sample.B;
            value = new Color(R, G, B);
            return true;
        }
        #endregion
        #region
        public static string getSkinDefaultValues(SpriteBank selfBank, string SpriteID) {
            string SkinID = null;

            string playerSkinName = GetPlayerSkinName(Player_Skinid_verify) + "_+";
            if (playerSkinName != null) {
                if (selfBank.Has(SpriteID + playerSkinName)) {
                    SkinID = playerSkinName;
                    if (Settings.PlayerSkinGreatestPriority) { return SkinID; }
                }
            }

            if (Settings.FreeCollocations_OffOn) {
                if ((selfBank == GFX.SpriteBank && Settings.FreeCollocations_Sprites.ContainsKey(SpriteID) && Settings.FreeCollocations_Sprites[SpriteID] == LockedToPlayer)
                 || (selfBank == GFX.PortraitsSpriteBank && Settings.FreeCollocations_Portraits.ContainsKey(SpriteID) && Settings.FreeCollocations_Portraits[SpriteID] == LockedToPlayer))
                    return SkinID;
            }

            foreach (SkinModHelperConfig config in GetEnabledGeneralSkins()) {
                if (selfBank.Has(SpriteID + config.SkinName)) {
                    SkinID = config.SkinName;
                }
            }
            return SkinID;
        }
        public static string getOtherSkin_ReskinPath(Atlas atlas, string origPath, string SpriteID, bool number_search = false) {
            string SkinId = GetOtherIDSkin(SpriteID);
            if (SkinId == ORIGINAL) { return origPath; }

            string CustomPath = origPath;
            if (SkinId == DEFAULT || SkinId == LockedToPlayer) {
                string SkinName = GetPlayerSkinName();
                if (SkinName != null) {
                    string spritePath = skinConfigs[SkinName].OtherSprite_Path;
                    if (!string.IsNullOrEmpty(spritePath)) {
                        spritePath = $"{spritePath}/{origPath}";
                        if ((number_search && atlas.HasAtlasSubtexturesAt(spritePath, 0)) || atlas.Has(spritePath)) {
                            CustomPath = spritePath;

                            if (Settings.PlayerSkinGreatestPriority) { return CustomPath; }
                        }
                    }
                }
            }
            if (SkinId == LockedToPlayer) { return CustomPath; }

            if (SkinId == DEFAULT) {
                foreach (SkinModHelperConfig config in GetEnabledGeneralSkins()) {
                    string spritePath = $"{config.OtherSprite_ExPath}/{origPath}";
                    if ((number_search && atlas.HasAtlasSubtexturesAt(spritePath, 0)) || atlas.Has(spritePath)) {
                        CustomPath = spritePath;
                    }
                }
            } else if (GetGeneralSkin(SkinId) != null) {
                string spritePath = $"{OtherskinConfigs[SkinId].OtherSprite_ExPath}/{origPath}";
                if ((number_search && atlas.HasAtlasSubtexturesAt(spritePath, 0)) || atlas.Has(spritePath)) {
                    CustomPath = spritePath;
                }
            }
            return CustomPath;
        }
        #endregion
        #region
        public static string getAnimationRootPath(object type) {
            if (type is PlayerSprite playerSprite) {
                string spriteName = DynamicData.For(playerSprite).Get<string>("spriteName");
                if (spriteName != null && GFX.SpriteBank.SpriteData.ContainsKey(spriteName)) {
                    SpriteData spriteData = GFX.SpriteBank.SpriteData[spriteName];

                    if (!string.IsNullOrEmpty(spriteData.Sources[0].OverridePath)) {
                        return spriteData.Sources[0].OverridePath;
                    } else {
                        return spriteData.Sources[0].Path;
                    }
                }
            } 
            if (type is Sprite sprite) {
                type = $"{(sprite.Has("idle") ? sprite.GetFrame("idle", 0) : sprite.Texture)}";
            } else if (type is Image image) {
                type = $"{image.Texture}";
            } else {
                type = $"{type}";
            }

            if (type is string path && path != null && path.LastIndexOf("/") >= 0) {
                return path.Remove(path.LastIndexOf("/") + 1);
            }
            return "";
        }
        public static string getAnimationRootPath(Sprite sprite, string id) {
            return sprite.Has(id) ? getAnimationRootPath(sprite.GetFrame(id, 0)) : getAnimationRootPath(sprite);
        }
        public static string getAnimationRootPath(object type, out string returnValue) {
            return returnValue = getAnimationRootPath(type);
        }
        #endregion
        #region
        public static FieldInfo GetFieldPlus(Type type, string name) {
            FieldInfo field = null;
            while (field == null && type != null) {
                field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance) ?? type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

                // some mods entities works based on vanilla entities, but mods entity possible don't have theis own field.
                type = type.BaseType;
            }
            return field;
        }
        public static T GetFieldPlus<T>(object obj, string name) {
            FieldInfo field = GetFieldPlus(obj.GetType(), name);
            if (field != null && field.GetValue(obj) is T value) {
                return value;
            }
            return default;
        }

        public static bool AssetExists<T>(string path, Atlas atlas = null) {
            if (atlas != null) {
                path = atlas.DataPath + "/" + path;
            }
            if (path.LastIndexOf(".") >= 0) {
                path = path.Remove(path.LastIndexOf("."));
            }
            return Everest.Content.TryGet<T>(path, out ModAsset asset);
        }
        public static bool SpriteExt_TryPlay(Sprite sprite, string id, bool restart = false) {
            if (sprite.Has(id)) {
                sprite.Play(id, restart);
                return true;
            }
            return false;
        }
        #endregion
    }
}
