using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace SuitSaver
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class SuitSaver : BaseUnityPlugin
    {
        private const string modGUID = "Hexnet.lethalcompany.suitsaver";
        private const string modName = "Suit Saver";
        private const string modVersion = "1.0.2";

        private readonly Harmony harmony = new Harmony(modGUID);

        void Awake()
        {
            harmony.PatchAll();

            Debug.Log("[SS]: Suit Saver loaded successfully!");
        }
    }
}

namespace SuitSaver.Patches
{
    internal class Patches
    {
        public static string SavePath = Application.persistentDataPath + "\\suitsaver.txt";

        private static void SaveToFile(string suitName)
        {
            File.WriteAllText(SavePath, suitName);
        }
        private static string LoadFromFile()
        {
            if (File.Exists(SavePath))
            {
                return File.ReadAllText(SavePath);
            }

            return "-1";
        }
        private static UnlockableSuit GetSuitByName(string Name)
        {
            List<UnlockableItem> Unlockables = StartOfRound.Instance.unlockablesList.unlockables;

            foreach (UnlockableSuit unlockable in UnityEngine.Object.FindObjectsOfType<UnlockableSuit>())
            {
                string SuitName = Unlockables[unlockable.suitID].unlockableName;

                if (SuitName == Name)
                {
                    return unlockable;
                }
            }

            return null;
        }
        private static void LoadSuitFromFile()
        {
            string SavedSuit = LoadFromFile();
            PlayerControllerB localplayer = GameNetworkManager.Instance.localPlayerController;

            if (SavedSuit == "-1")
            {
                return;
            }

            UnlockableSuit Suit = GetSuitByName(SavedSuit);

            if (Suit != null)
            {
                UnlockableSuit.SwitchSuitForPlayer(localplayer, Suit.suitID, false);
                Suit.SwitchSuitServerRpc((int)localplayer.playerClientId);

                Debug.Log("[SS]: Successfully loaded saved suit. (" + SavedSuit + ")");
            }
            else
            {
                Debug.Log("[SS]: Failed to load saved suit. Perhaps it's locked? (" + SavedSuit + ")");
            }
        }

        [HarmonyPatch(typeof(StartOfRound))]
        internal class StartPatch
        {
            [HarmonyPatch("ResetShip")]
            [HarmonyPostfix]
            private static void ResetShipPatch()
            {
                Debug.Log("[SS]: Ship has been reset!");
                Debug.Log("[SS]: Reloading suit...");
                LoadSuitFromFile();
            }
        }

        [HarmonyPatch(typeof(UnlockableSuit))]
        internal class SuitPatch
        {
            [HarmonyPatch("SwitchSuitToThis")]
            [HarmonyPostfix]
            private static void EquipSuitPatch()
            {
                PlayerControllerB localplayer = GameNetworkManager.Instance.localPlayerController;
                string SuitName = StartOfRound.Instance.unlockablesList.unlockables[localplayer.currentSuitID].unlockableName;

                SaveToFile(SuitName);

                Debug.Log("[SS]: Successfully saved current suit. (" + SuitName + ")");
            }
        }

        [HarmonyPatch(typeof(PlayerControllerB))]
        internal class EquipPatch
        {
            [HarmonyPatch("ConnectClientToPlayerObject")]
            [HarmonyPostfix]
            private static void LoadSuitPatch(ref PlayerControllerB __instance)
            {
                LoadSuitFromFile();
            }
        }
    }
}