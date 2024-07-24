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
        private const string modVersion = "1.2.1";

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
                // Cross matching unlockable items, and current available suits to find the unlockableitem itself.
                // This is done to insure compatibility with TooManySuits.
                if (unlockable.syncedSuitID.Value >= 0)
                {
                    string SuitName = Unlockables[unlockable.syncedSuitID.Value].unlockableName;

                    // Checking whether or not it's our suit.
                    if (SuitName == Name)
                    {
                        // Return the suit upon being found.
                        return unlockable;
                    }
                }
            }

            return null;
        }

        private static bool IsPurchasedSuit(string Name)
        {
            List<UnlockableItem> Unlockables = StartOfRound.Instance.unlockablesList.unlockables;

            // Cross matching unlockable items, and current available suits to find the unlockableitem itself.
            // This is done to insure compatibility with TooManySuits.
            foreach (UnlockableSuit unlockable in Resources.FindObjectsOfTypeAll<UnlockableSuit>())
            {
                if (unlockable.syncedSuitID.Value >= 0)
                {
                    UnlockableItem Suit = Unlockables[unlockable.syncedSuitID.Value];
                    string SuitName = Suit.unlockableName;

                    // Checking whether or not it's our suit.
                    if (SuitName == Name)
                    {
                        // If it's not already unlocked and has been unlocked by the player, then we assume it is a purchased suit.
                        // Sadly, I could not find a better way to do this.
                        return !Suit.alreadyUnlocked && Suit.hasBeenUnlockedByPlayer;
                    }
                }
            }

            return false;
        }

        // This method may be phased out sooner or later.
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
                // Changing our suit on the client without making the equip noise, while also calling the rpc to replicate to other clients.
                UnlockableSuit.SwitchSuitForPlayer(localplayer, Suit.syncedSuitID.Value, false);
                Suit.SwitchSuitServerRpc((int)localplayer.playerClientId);

                Debug.Log("[SS]: Successfully loaded saved suit. (" + SavedSuit + ")");
            }
            else
            {
                Debug.Log("[SS]: Failed to load saved suit. Perhaps it's locked? (" + SavedSuit + ")");
            }
        }

        private static int LoadSuitStartup(string SavedSuit)
        {
            PlayerControllerB localplayer = GameNetworkManager.Instance.localPlayerController;

            if (SavedSuit == "-1")
            {
                return -1;
            }

            UnlockableSuit Suit = GetSuitByName(SavedSuit);

            if (Suit != null)
            {
                // Changing our suit on the client without making the equip noise, while also calling the rpc to replicate to other clients.
                UnlockableSuit.SwitchSuitForPlayer(localplayer, Suit.syncedSuitID.Value, false);
                Suit.SwitchSuitServerRpc((int)localplayer.playerClientId);

                return 1;
            }
            else
            {
                return 0;
            }
            
            // Note: Return values here are used as a way to know whether or not the suit successfully loaded.
        }

        [HarmonyPatch(typeof(StartOfRound))]
        internal class StartPatch
        {
            static bool ReloadSuit = false;

            [HarmonyPatch("playersFiredGameOver")]
            [HarmonyPrefix]
            private static void PurchasedSuitCheck()
            {
                string SavedSuit = LoadFromFile();

                // Making sure we have a suit saved.
                if (SavedSuit != "-1")
                {
                    // If it's not a purchased suit, then we can reload it safely.
                    // Otherwise, relaoding a purchased suit ends up crashing the client.
                    ReloadSuit = !IsPurchasedSuit(SavedSuit);
                }
            }

            [HarmonyPatch("ResetShip")]
            [HarmonyPostfix]
            private static void ResetShipPatch()
            {
                // Reload the suit upon ship reset.
                if (!ReloadSuit)
                {
                    string SavedSuit = LoadFromFile();

                    if (SavedSuit != "-1")
                    {
                        Debug.Log($"[SS]: Could not reload suit upon ship reset. Perhaps it's locked? ({SavedSuit})");
                    }

                    return;
                }

                Debug.Log("[SS]: Ship has been reset!");
                Debug.Log("[SS]: Reloading suit...");
                LoadSuitFromFile();

                // Reset boolean for future use.
                ReloadSuit = false;
            }
        }

        [HarmonyPatch(typeof(UnlockableSuit))]
        internal class SuitPatch
        {
            [HarmonyPatch("SwitchSuitClientRpc")]
            [HarmonyPostfix]
            private static void SyncSuit(ref UnlockableSuit __instance, int playerID)
            {
                // Manually sync the suit whenever someone equips one.
                // This is done for compatibility with TooManySuits.
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
                // Get current suit name, then save to file.
                PlayerControllerB localplayer = GameNetworkManager.Instance.localPlayerController;
                string SuitName = StartOfRound.Instance.unlockablesList.unlockables[localplayer.currentSuitID].unlockableName;

                SaveToFile(SuitName);

                Debug.Log($"[SS]: Successfully saved current suit. ({SuitName})");
            }
        }

        [HarmonyPatch(typeof(PlayerControllerB))]
        internal class JoinGamePatch
        {
            [HarmonyPatch("ConnectClientToPlayerObject")]
            [HarmonyPostfix]
            private static void LoadSuitPatch(ref PlayerControllerB __instance)
            {
                // Load a special sync to prevent cases where the suit does not manage to load.
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
                // Band aid fix, but for the time being it works.
                // Retry equipping the suit upon initial server join 3 times over 3 seconds.
                // If the suit does not load, we let the player know.

                Debug.Log("[SS]: Waiting for suits to sync...");

                string SavedSuit = LoadFromFile();
                int success = -1;

                for (int i = 0; i < 3; i++)
                {
                    success = LoadSuitStartup(SavedSuit);

                    if (success <= 0)
                    {
                        if (success == 0)
                        {
                            Debug.Log($"[SS]: Failed to load saved suit. Perhaps it's locked? ({SavedSuit})");
                        }

                        break;
                    }

                    yield return new WaitForSeconds(1);
                }

                if (success == 1)
                {
                    Debug.Log($"[SS]: Successfully loaded saved suit. ({SavedSuit})");
                }
            }
        }
    }
}