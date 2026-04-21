using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace MetalCasting
{
    public class MetalCastingModSystem : ModSystem
    {
        public static MetalCastingModSystem Instance { get; private set; }
        public RunnerNetworkManager NetworkManager { get; private set; }

        public override void Start(ICoreAPI api)
        {
            Instance = this;
            Mod.Logger.Notification("MetalCasting starting: " + api.Side);

            api.RegisterBlockClass("BlockRunner", typeof(BlockRunner));
            api.RegisterBlockEntityClass("BERunner", typeof(BERunner));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            NetworkManager = new RunnerNetworkManager();
            NetworkManager.Initialize(api);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
        }
    }
}
