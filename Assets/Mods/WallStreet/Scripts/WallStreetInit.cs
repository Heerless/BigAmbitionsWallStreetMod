#nullable enable
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Buildings;
using Helpers;
using UnityEngine;
using WallStreet;

[assembly: RegisterModClass(typeof(WallStreetInit))]

namespace WallStreet
{
    /// <summary>
    /// Registers the Brokerage business type and installs the custom Broker skill.
    /// </summary>
    [ModEntryOnInitializationLoad]
    public class WallStreetInit : IModBigAmbitions
    {
        private const string BundleKey = "AssetBundles/wallstreet.unity3d";
        private const string BrokerageAsset = "Assets/Mods/WallStreet/Brokerage.asset";
        private const string CommissionAsset = "Assets/Mods/WallStreet/Commission.asset";

        public string[] RelativeAssetBundlePaths => new[] { BundleKey };

        /// <summary>The Sales-row product, registered once the city has loaded.</summary>
        public static Item? Commission { get; private set; }

        /// <summary>The Broker skill icon, applied to the cloned skill at city load.</summary>
        public static Sprite? BrokerIcon { get; private set; }

        private BusinessType? _brokerage;
        private Item? _commission;

        public Task OnLoadAsync(ModContext context)
        {
            var bundle = AssetService.GetBundle(context.ModId, BundleKey);
            _brokerage = bundle.LoadAsset<BusinessType>(BrokerageAsset);

            if (_brokerage == null)
            {
                context.Logger.Info("[WallStreet] Brokerage.asset did not load from the bundle.");
            }
            else
            {
                ModdingAPI.RegisterModBusinessType(_brokerage);
                context.Logger.Info("[WallStreet] Registered business type: " + _brokerage.businessTypeName);
            }

            // The income statement calls ItemsGetter.GetByName(name).HasTag(...) with no
            // null check, so an unrecognised Sales line throws and collapses the statement
            // into one "Undefined" row. The commission exists purely to be a name that
            // resolves. Registered at city load, because the item catalogue reloads after
            // initialisation and drops anything added this early.
            _commission = bundle.LoadAsset<Item>(CommissionAsset);
            Commission = _commission;

            BrokerIcon = bundle.LoadAsset<Sprite>("Assets/Mods/WallStreet/SkillIcon-Broker.png");

            context.Logger.Info(_commission == null
                ? "[WallStreet] Commission.asset did not load; Sales will stay empty."
                : "[WallStreet] Commission item loaded, awaiting city load.");

            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (_brokerage != null)
                ModdingAPI.UnregisterModBusinessType(_brokerage);

            if (_commission != null)
                ItemsGetter.UnregisterModItem(_commission.itemName);

            _brokerage = null;
            _commission = null;
            return Task.CompletedTask;
        }
    }
}
