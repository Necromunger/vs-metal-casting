using MetalCasting.BlockEntities;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace MetalCasting;

public class RunnerNetworkManager
{
    private static readonly BlockFacing[] HORIZONTALS =
    {
            BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST
        };

    private ICoreAPI api;
    private readonly Dictionary<long, RunnerNetwork> networks = new();
    private readonly Dictionary<BlockPos, long> runnerToNetwork = new();
    private long nextNetworkId = 1;

    public void Initialize(ICoreAPI api) => this.api = api;

    public RunnerNetwork GetNetwork(BlockPos pos)
    {
        if (runnerToNetwork.TryGetValue(pos, out long id) &&
            networks.TryGetValue(id, out var net)) return net;
        return null;
    }

    public void AddRunner(BlockPos pos)
    {
        var adjacent = new List<long>();
        foreach (var f in HORIZONTALS)
        {
            var n = pos.AddCopy(f);
            if (runnerToNetwork.TryGetValue(n, out long id) && !adjacent.Contains(id))
                adjacent.Add(id);
        }

        if (adjacent.Count == 0)
        {
            long newId = nextNetworkId++;
            var net = new RunnerNetwork(newId);
            net.AddRunner(pos);
            networks[newId] = net;
            runnerToNetwork[pos.Copy()] = newId;
            return;
        }

        if (adjacent.Count == 1)
        {
            long id = adjacent[0];
            networks[id].AddRunner(pos);
            runnerToNetwork[pos.Copy()] = id;
            return;
        }

        // Merge all adjacent networks into the first
        long mainId = adjacent[0];
        var main = networks[mainId];
        main.AddRunner(pos);
        for (int i = 1; i < adjacent.Count; i++)
        {
            long otherId = adjacent[i];
            if (!networks.TryGetValue(otherId, out var other)) continue;
            main.Merge(other);
            foreach (var p in other.Runners) runnerToNetwork[p] = mainId;
            networks.Remove(otherId);
        }
        runnerToNetwork[pos.Copy()] = mainId;
    }

    public void RemoveRunner(BlockPos pos)
    {
        if (!runnerToNetwork.TryGetValue(pos, out long id)) return;
        runnerToNetwork.Remove(pos);
        if (!networks.TryGetValue(id, out var net)) return;

        net.RemoveRunner(pos);
        if (net.Runners.Count == 0)
        {
            networks.Remove(id);
            return;
        }

        // BFS remaining runners to detect split components
        var remaining = new HashSet<BlockPos>(net.Runners);
        var components = new List<HashSet<BlockPos>>();

        while (remaining.Count > 0)
        {
            BlockPos start = null;
            foreach (var p in remaining) { start = p; break; }

            var component = new HashSet<BlockPos>();
            var queue = new Queue<BlockPos>();
            component.Add(start);
            remaining.Remove(start);
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var f in HORIZONTALS)
                {
                    var np = cur.AddCopy(f);
                    if (!remaining.Contains(np)) continue;
                    if (!IsConnected(cur, f)) continue;
                    component.Add(np);
                    remaining.Remove(np);
                    queue.Enqueue(np);
                }
            }
            components.Add(component);
        }

        if (components.Count == 1) return;

        // First component keeps the original network id
        net.Runners.Clear();
        foreach (var p in components[0])
        {
            net.AddRunner(p);
            runnerToNetwork[p] = id;
        }

        // Remaining components get fresh networks
        for (int c = 1; c < components.Count; c++)
        {
            long newId = nextNetworkId++;
            var newNet = new RunnerNetwork(newId);
            networks[newId] = newNet;
            foreach (var p in components[c])
            {
                newNet.AddRunner(p);
                runnerToNetwork[p] = newId;
            }
        }
    }

    private bool IsConnected(BlockPos from, BlockFacing side)
    {
        if (api == null) return true;
        var be = api.World.BlockAccessor.GetBlockEntity(from) as BERunner;
        return be != null && be.IsConnectedToRunner(side);
    }
}