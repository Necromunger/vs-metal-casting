using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace MetalCasting;

internal static class LiquidMetalUtil
{
    public static void PourIntoSink(ILiquidMetalSink sink, ItemStack metal, ref int amount, float temperature)
    {
        if (sink is BlockEntityIngotMold dual && dual.QuantityMolds > 1)
        {
            dual.IsRightSideSelected = false;
            sink.ReceiveLiquidMetal(metal, ref amount, temperature);
            if (amount > 0)
            {
                dual.IsRightSideSelected = true;
                sink.ReceiveLiquidMetal(metal, ref amount, temperature);
            }
        }
        else
        {
            sink.ReceiveLiquidMetal(metal, ref amount, temperature);
        }
        sink.OnPourOver();
    }
}
