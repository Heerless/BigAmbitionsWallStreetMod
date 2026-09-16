#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Items;
using UnityEngine;
using WallStreet;

[assembly: RegisterModClass(typeof(WallStreetCityMod))]

namespace WallStreet
{
    /// <summary>
    /// Vanilla skill data arrives from Addressables after initialization, so the Broker
    /// skill is created here rather than in the init pass. Binding it into the recruitment
    /// agencies is what actually makes the job hireable.
    /// </summary>
    [ModEntryOnCityLoad]
    public class WallStreetCityMod : IModBigAmbitions
    {
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            void Log(string message) => context.Logger.Info("[WallStreet] " + message);

            var installed = BrokerSkill.Install(Log);
            Log("Broker skill installed: " + installed);

            if (installed)
            {
                BrokerSkill.VerifyHeadhunterOnly(Log);
                DeskWiring.Apply(Log);
            }

            RegisterCommission(Log);
            FloorAlerts.Install(Log);
            FloorRunner.RepairStatements(Log);

            FloorSave.Bind(SaveGameName(), Log);
            FloorRunner.Start(Log);
            BizManProbe.Run(Log);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Registers the Sales-row product now the item catalogue has loaded. Registering at
        /// initialisation does not survive: the catalogue reloads afterwards and drops it,
        /// which left the income statement throwing on an unknown item name.
        /// </summary>
        private static void RegisterCommission(Action<string> log)
        {
            var item = WallStreetInit.Commission;
            if (item == null) { log("SALES: no commission item to register."); return; }

            if (ItemsGetter.GetByName(item.itemName, true) != null)
            {
                log("SALES: commission item already present.");
                return;
            }

            ItemsGetter.RegisterModItem(item);

            log(ItemsGetter.GetByName(item.itemName, true) != null
                ? "SALES: registered " + item.itemName + "; the statement can resolve it."
                : "SALES: registration did not take - the statement would still throw.");
        }

        /// <summary>
        /// Identifies the save so floors do not bleed between them. characterId is unique
        /// per save and stable across launches - the earlier fallback to a shared key meant
        /// capital committed in one save showed up in another.
        /// </summary>
        private static string SaveGameName()
        {
            try
            {
                var current = SaveGameManager.Current;
                if (current == null) return "shared";

                if (!string.IsNullOrEmpty(current.characterId)) return current.characterId;
            }
            catch { }

            return "shared";
        }

        public Task OnUnloadAsync()
        {
            FloorSave.SaveAll();
            FloorRunner.Stop();
            BizManPanel.Uninstall();
            return Task.CompletedTask;
        }
    }
}
