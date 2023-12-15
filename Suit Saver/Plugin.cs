using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
        private const string modVersion = "1.1.2";

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
    [HarmonyPatch]
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

            foreach (UnlockableSuit unlockable in Resources.FindObjectsOfTypeAll<UnlockableSuit>())
            {
                if (unlockable.syncedSuitID.Value >= 0)
                {
                    string SuitName = Unlockables[unlockable.syncedSuitID.Value].unlockableName;

                    if (SuitName == Name)
                    {
                        return unlockable;
                    }
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
                UnlockableSuit.SwitchSuitForPlayer(localplayer, Suit.syncedSuitID.Value, false);
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
            [HarmonyPatch("SwitchSuitClientRpc")]
            [HarmonyPostfix]
            private static void SyncSuit(ref UnlockableSuit __instance, int playerID)
            {

                PlayerControllerB localplayer = GameNetworkManager.Instance.localPlayerController;
                int LocalPlayerID = (int)localplayer.playerClientId;

                if (playerID != LocalPlayerID)
                {
                    UnlockableSuit.SwitchSuitForPlayer(StartOfRound.Instance.allPlayerScripts[playerID], __instance.syncedSuitID.Value);
                }
            }

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
        internal class JoinGamePatch
        {
            [HarmonyPatch("ConnectClientToPlayerObject")]
            [HarmonyPostfix]
            private static void LoadSuitPatch(ref PlayerControllerB __instance)
            {
                GameNetworkManager.Instance.localPlayerController.gameObject.AddComponent<EquipAfterSyncPatch>();
            }
        }

        internal class EquipAfterSyncPatch : MonoBehaviour
        {
            void Start()
            {
                StartCoroutine(LoadSuit());
            }

            IEnumerator LoadSuit()
            {
                Debug.Log("[SS]: Waiting for suits to sync...");

                yield return new WaitForSeconds(1);

                LoadSuitFromFile();
            }
        }
    }
}