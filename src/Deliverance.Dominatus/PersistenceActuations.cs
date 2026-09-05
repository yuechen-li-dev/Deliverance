using System.Collections.Concurrent;
using Deliverance.Core;
using Deliverance.Core.Modules;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;

namespace Deliverance.Dominatus;

public sealed record SaveSlotActuation(string SlotId) : IActuationCommand;
public sealed record LoadSlotActuation(string SlotId) : IActuationCommand;

public sealed record PersistenceActuationCompletion(string Operation, string SlotId, bool CandidateCommitted);

public interface IPersistenceApplicationBridge
{
    SaveRequest CaptureSave(string slotId);
    IReadOnlyList<SaveModuleDefinition> GetLoadDefinitions(string slotId);
    LoadCompatibility? GetLoadCompatibility(string slotId);
    void CommitLoadedCandidate(string slotId, LoadedSaveCandidate candidate);
}

/// <summary>
/// Capture happens during dispatch under application authority. Serialization and IO run on a worker;
/// load commit and completion publication happen when the application pumps its authoritative thread.
/// </summary>
public sealed class DeliverancePersistenceActuator :
    IActuationHandler<SaveSlotActuation>,
    IActuationHandler<LoadSlotActuation>
{
    private readonly IDeliverance deliverance;
    private readonly IPersistenceApplicationBridge application;
    private readonly ConcurrentQueue<PendingCompletion> completions = new();

    public DeliverancePersistenceActuator(IDeliverance deliverance, IPersistenceApplicationBridge application)
    {
        this.deliverance = deliverance ?? throw new ArgumentNullException(nameof(deliverance));
        this.application = application ?? throw new ArgumentNullException(nameof(application));
    }

    public bool HasPendingCompletion => !completions.IsEmpty;

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, SaveSlotActuation command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SlotId);
        SaveRequest snapshot;
        try
        {
            snapshot = application.CaptureSave(command.SlotId);
        }
        catch (Exception exception)
        {
            return ActuatorHost.HandlerResult.CompletedFailure($"Save capture failed: {exception.Message}");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await deliverance.SaveAsync(command.SlotId, snapshot, ctx.Cancel).ConfigureAwait(false);
                completions.Enqueue(PendingCompletion.Success(ctx.Agent, id, "save", command.SlotId, null));
            }
            catch (Exception exception)
            {
                completions.Enqueue(PendingCompletion.Failure(ctx.Agent, id, "save", command.SlotId, exception));
            }
        }, CancellationToken.None);
        return ActuatorHost.HandlerResult.DeferredAccepted();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, LoadSlotActuation command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SlotId);
        IReadOnlyList<SaveModuleDefinition> definitions = application.GetLoadDefinitions(command.SlotId);
        LoadCompatibility? compatibility = application.GetLoadCompatibility(command.SlotId);
        _ = Task.Run(async () =>
        {
            try
            {
                LoadedSaveCandidate candidate = await deliverance
                    .LoadAsync(command.SlotId, definitions, compatibility, ctx.Cancel)
                    .ConfigureAwait(false);
                completions.Enqueue(PendingCompletion.Success(ctx.Agent, id, "load", command.SlotId, candidate));
            }
            catch (Exception exception)
            {
                completions.Enqueue(PendingCompletion.Failure(ctx.Agent, id, "load", command.SlotId, exception));
            }
        }, CancellationToken.None);
        return ActuatorHost.HandlerResult.DeferredAccepted();
    }

    public int PumpCompletions()
    {
        int count = 0;
        while (completions.TryDequeue(out PendingCompletion? completion))
        {
            bool committed = false;
            string? error = completion.Error;
            if (completion.Candidate is not null && error is null)
            {
                try
                {
                    application.CommitLoadedCandidate(completion.SlotId, completion.Candidate);
                    committed = true;
                }
                catch (Exception exception)
                {
                    error = $"Load candidate commit failed: {exception.Message}";
                }
            }

            bool ok = error is null;
            var payload = new PersistenceActuationCompletion(completion.Operation, completion.SlotId, committed);
            completion.Agent.Events.Publish(new ActuationCompleted(completion.Id, ok, error, payload));
            completion.Agent.Events.Publish(new ActuationCompleted<PersistenceActuationCompletion>(completion.Id, ok, error, payload));
            completion.Agent.InFlightActuations.Remove(new PendingActuation(completion.Id.Value, null));
            count++;
        }
        return count;
    }

    private sealed record PendingCompletion(
        AiAgent Agent,
        ActuationId Id,
        string Operation,
        string SlotId,
        LoadedSaveCandidate? Candidate,
        string? Error)
    {
        public static PendingCompletion Success(
            AiAgent agent,
            ActuationId id,
            string operation,
            string slotId,
            LoadedSaveCandidate? candidate)
        {
            return new PendingCompletion(agent, id, operation, slotId, candidate, null);
        }

        public static PendingCompletion Failure(
            AiAgent agent,
            ActuationId id,
            string operation,
            string slotId,
            Exception exception)
        {
            string detail = exception is DeliveranceException deliveranceException
                ? $"{deliveranceException.Code}: {deliveranceException.Message}"
                : exception.Message;
            return new PendingCompletion(agent, id, operation, slotId, null, detail);
        }
    }
}
