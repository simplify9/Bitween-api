using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween.UnitTests;

/// <summary>
/// How many exchanges of a lane run at once on a node. A group's work lane takes its parallel
/// exchanges, else its prefetch, else the default prefetch; a group saved before the setting
/// existed has neither and must still get the prefetch the docs always promised.
/// </summary>
[TestClass]
public class LaneConcurrencyTests
{
    private static WorkGroup Group(ushort? prefetch, ushort? concurrency) => new()
    {
        Options = new WorkGroupOptions
        {
            RabbitMqOptions = new ConsumerSettings { Prefetch = prefetch, Concurrency = concurrency }
        }
    };

    [TestMethod]
    public void A_work_lane_takes_its_parallel_exchanges_then_its_prefetch_then_the_default_prefetch()
    {
        Assert.AreEqual((ushort)3, Group(6, 3).WorkLaneConcurrency(12));
        Assert.AreEqual((ushort)1, Group(6, 1).WorkLaneConcurrency(12));
        Assert.AreEqual((ushort)6, Group(6, null).WorkLaneConcurrency(12));
        Assert.AreEqual((ushort)12, Group(null, null).WorkLaneConcurrency(12));
        Assert.AreEqual((ushort)12, new WorkGroup().WorkLaneConcurrency(12));
    }

    [TestMethod]
    public void The_default_concurrency_is_the_default_prefetch_unless_set()
    {
        Assert.AreEqual((ushort)12, new BitweenOptions().ResolvedBusDefaultConcurrency);
        Assert.AreEqual((ushort)20, new BitweenOptions { BusDefaultQueuePrefetch = 20 }.ResolvedBusDefaultConcurrency);
        Assert.AreEqual((ushort)1, new BitweenOptions { BusDefaultConcurrency = 1 }.ResolvedBusDefaultConcurrency);
    }
}
