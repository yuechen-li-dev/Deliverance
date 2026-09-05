using Deliverance.Core.Modules;
using Deliverance.Core.BuiltIns;
using Deliverance.Core.Storage;

namespace Deliverance.Core;

public interface IDeliverance
{
    DeliveranceOptions Options { get; }
    SaveDiagnostics Diagnostics { get; }

    KeyValueStore KV { get; }

    void Register(ISaveModule module);
    bool Unregister(string key);

    Task SaveSlotAsync(string slotId, CancellationToken ct = default);
    Task LoadSlotAsync(string slotId, CancellationToken ct = default);

    Task SaveAsync(string slotId, SaveRequest request, CancellationToken ct = default);
    Task<LoadedSaveCandidate> LoadAsync(
        string slotId,
        IReadOnlyList<SaveModuleDefinition> definitions,
        LoadCompatibility? compatibility = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default);
    Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default);
    Task<bool> SlotExistsAsync(string slotId, CancellationToken ct = default);
    Task DeleteSlotAsync(string slotId, CancellationToken ct = default);
}
