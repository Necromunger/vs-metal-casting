using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using MetalCasting.Blocks;
using MetalCasting.BlockEntities;

namespace MetalCasting
{
    public class MetalCastingModSystem : ModSystem
    {
        public static MetalCastingModSystem Instance { get; private set; }
        public RunnerNetworkManager NetworkManager { get; private set; }

        public override void Start(ICoreAPI api)
        {
            Mod.Logger.Notification("MetalCasting starting: " + api.Side);

            if (api.Side == EnumAppSide.Server)
            {
                Instance = this;
                NetworkManager = new RunnerNetworkManager();
                NetworkManager.Initialize(api);
            }

            api.RegisterBlockClass("BlockRunner", typeof(BlockRunner));
            api.RegisterBlockEntityClass("BERunner", typeof(BERunner));
            api.RegisterBlockClass("BlockSprout", typeof(BlockSprout));
            api.RegisterBlockEntityClass("BESprout", typeof(BESprout));
            api.RegisterBlockClass("BlockTiltingCrucibleFrame", typeof(BlockTiltingCrucibleFrame));
            api.RegisterBlockEntityClass("BETiltingCrucibleFrame", typeof(BETiltingCrucibleFrame));
            api.RegisterBlockClass("BlockCrucibleFurnace", typeof(BlockCrucibleFurnace));
            api.RegisterBlockEntityClass("BECrucibleFurnace", typeof(BECrucibleFurnace));
        }

        public override void StartServerSide(ICoreServerAPI api) { }

        public override void StartClientSide(ICoreClientAPI api)
        {
        }
    }
}
