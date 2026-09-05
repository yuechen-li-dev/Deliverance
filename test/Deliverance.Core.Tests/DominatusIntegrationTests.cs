using Deliverance.Core.Codecs;
using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;
using Deliverance.Dominatus;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;

namespace Deliverance.Core.Tests;

public sealed class DominatusIntegrationTests
{
    [Fact]
    public async Task SaveAndLoadActuations_PreserveCorrelationCheckpointAndExplicitCommit()
    {
        var store = new InMemorySaveStore();
        MessagePackSaveSerializer serializer = new();
        var deliverance = new DeliveranceService(new DeliveranceOptions { Store = store, Serializer = serializer });
        var bridge = new TestBridge(serializer) { Value = 41 };
        var persistence = new DeliverancePersistenceActuator(deliverance, bridge);
        var actuatorHost = new ActuatorHost();
        actuatorHost.Register<SaveSlotActuation>(persistence);
        actuatorHost.Register<LoadSlotActuation>(persistence);
        (AiWorld world, AiAgent agent, AiCtx context) = CreateContext(actuatorHost);

        ActuationDispatchResult save = actuatorHost.Dispatch(context, new SaveSlotActuation("slot"));
        Assert.True(save.Accepted);
        Assert.False(save.Completed);
        Assert.Contains(agent.InFlightActuations, pending => pending.ActuationIdValue == save.Id.Value);
        DominatusCheckpoint checkpoint = DominatusCheckpointBuilder.Capture(world);
        Assert.NotEmpty(checkpoint.Agents.Single().EventCursorBlob);
        await PumpWhenReady(persistence);

        EventCursor saveCursor = default;
        Assert.True(agent.Events.TryConsume<ActuationCompleted>(ref saveCursor, item => item.Id == save.Id, out ActuationCompleted saveCompleted));
        Assert.True(saveCompleted.Ok);
        Assert.DoesNotContain(agent.InFlightActuations, pending => pending.ActuationIdValue == save.Id.Value);

        bridge.Value = 0;
        ActuationDispatchResult load = actuatorHost.Dispatch(context, new LoadSlotActuation("slot"));
        Assert.NotEqual(save.Id, load.Id);
        await PumpWhenReady(persistence);
        Assert.Equal(41, bridge.Value);
        Assert.Equal(1, bridge.CommitCount);

        EventCursor loadCursor = default;
        Assert.True(agent.Events.TryConsume<ActuationCompleted<PersistenceActuationCompletion>>(
            ref loadCursor,
            item => item.Id == load.Id,
            out ActuationCompleted<PersistenceActuationCompletion> loadCompleted));
        Assert.True(loadCompleted.Ok);
        Assert.True(loadCompleted.Payload!.CandidateCommitted);
    }

    [Fact]
    public async Task LoadActuation_PropagatesTypedDeliveranceFailureWithoutCommit()
    {
        var store = new InMemorySaveStore();
        MessagePackSaveSerializer serializer = new();
        var deliverance = new DeliveranceService(new DeliveranceOptions { Store = store, Serializer = serializer });
        var bridge = new TestBridge(serializer) { Value = 1 };
        await deliverance.SaveAsync("slot", bridge.CaptureSave("slot"));
        bridge.DefinitionHash = "changed";
        var persistence = new DeliverancePersistenceActuator(deliverance, bridge);
        var host = new ActuatorHost();
        host.Register<LoadSlotActuation>(persistence);
        (_, AiAgent agent, AiCtx context) = CreateContext(host);

        ActuationDispatchResult load = host.Dispatch(context, new LoadSlotActuation("slot"));
        await PumpWhenReady(persistence);

        EventCursor cursor = default;
        Assert.True(agent.Events.TryConsume<ActuationCompleted>(ref cursor, item => item.Id == load.Id, out ActuationCompleted completed));
        Assert.False(completed.Ok);
        Assert.Contains(nameof(SaveDiagnosticCode.DefinitionMismatch), completed.Error, StringComparison.Ordinal);
        Assert.Equal(0, bridge.CommitCount);
    }

    private static async Task PumpWhenReady(DeliverancePersistenceActuator persistence)
    {
        for (int attempt = 0; attempt < 200 && !persistence.HasPendingCompletion; attempt++)
        {
            await Task.Delay(5);
        }
        Assert.True(persistence.HasPendingCompletion);
        Assert.Equal(1, persistence.PumpCompletions());
    }

    private static (AiWorld World, AiAgent Agent, AiCtx Context) CreateContext(ActuatorHost actuator)
    {
        var graph = new HfsmGraph { Root = "root" };
        graph.Add(new HfsmStateDef { Id = "root", Node = static _ => Idle() });
        var agent = new AiAgent(new HfsmInstance(graph));
        var world = new AiWorld(actuator);
        world.Add(agent);
        var context = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, actuator);
        return (world, agent, context);

        static IEnumerator<AiStep> Idle()
        {
            while (true)
            {
                yield return new WaitSeconds(999);
            }
        }
    }

    private sealed class TestBridge(MessagePackSaveSerializer serializer) : IPersistenceApplicationBridge
    {
        public int Value { get; set; }
        public int CommitCount { get; private set; }
        public string DefinitionHash { get; set; } = "definitions";

        public SaveRequest CaptureSave(string slotId)
        {
            SaveModulePayload module = SaveModulePayload.Create(
                "world", 1, ModuleCriticality.Required, serializer, new NoCompressionCodec(), Value);
            return new SaveRequest(new SaveApplicationMetadata("test", DefinitionHash: DefinitionHash), [module]);
        }

        public IReadOnlyList<SaveModuleDefinition> GetLoadDefinitions(string slotId)
        {
            return [new SaveModuleDefinition("world", 1, ModuleCriticality.Required)];
        }

        public LoadCompatibility GetLoadCompatibility(string slotId)
        {
            return new LoadCompatibility("test", DefinitionHash);
        }

        public void CommitLoadedCandidate(string slotId, LoadedSaveCandidate candidate)
        {
            var serializers = new DefaultSaveSerializerRegistry();
            serializers.Register(serializer);
            Value = candidate.Deserialize<int>("world", serializers);
            CommitCount++;
        }
    }
}
