using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace MetalCasting;

internal static class HeatMath
{
    public const float AmbientTemperature = 20f;
    public const float TemperatureEpsilon = 0.1f;

    public static float ChangeTemperature(float fromTemp, float toTemp, float dt)
    {
        float diff = Math.Abs(fromTemp - toTemp);
        dt += dt * (diff / 28f);

        if (diff < dt || diff < 1f) return toTemp;
        if (fromTemp > toTemp) dt = -dt;
        return fromTemp + dt;
    }

    public static bool HeatSlot(
        IWorldAccessor world,
        ItemSlot slot,
        float sourceTemperature,
        float dt,
        ISlotProvider cookingSlotsProvider = null)
    {
        var stack = slot?.Itemstack;
        if (stack?.Collectible == null) return false;

        float currentTemp = stack.Collectible.GetTemperature(world, stack);
        if (currentTemp >= sourceTemperature) return false;

        float heatDt = (1f + GameMath.Clamp((sourceTemperature - currentTemp) / 30f, 0f, 1.6f)) * dt;
        float meltingPoint = stack.Collectible.GetMeltingPoint(world, cookingSlotsProvider, slot);
        if (meltingPoint > 0f && currentTemp >= meltingPoint)
        {
            heatDt /= 11f;
        }

        float newTemp = ChangeTemperature(currentTemp, sourceTemperature, heatDt);
        newTemp = (newTemp + (stack.StackSize - 1f) * currentTemp) / stack.StackSize;

        int maxTemp = stack.Collectible.GetCombustibleProperties(world, stack, null)?.MaxTemperature ?? 0;
        var attrs = stack.ItemAttributes;
        if (attrs?["maxTemperature"] != null)
        {
            maxTemp = Math.Max(maxTemp, attrs["maxTemperature"].AsInt(0));
        }
        if (maxTemp > 0)
        {
            newTemp = Math.Min(maxTemp, newTemp);
        }

        if (Math.Abs(newTemp - currentTemp) <= TemperatureEpsilon) return false;

        stack.Collectible.SetTemperature(world, stack, newTemp, delayCooldown: true);
        slot.MarkDirty();
        return true;
    }

    public static float GetLowestStackTemperature(IWorldAccessor world, ISlotProvider slotProvider)
    {
        bool hasStack = false;
        float temperature = AmbientTemperature;

        foreach (var slot in slotProvider.Slots)
        {
            var stack = slot.Itemstack;
            if (stack == null) continue;

            float stackTemp = stack.Collectible.GetTemperature(world, stack);
            temperature = hasStack ? Math.Min(temperature, stackTemp) : stackTemp;
            hasStack = true;
        }

        return temperature;
    }
}
