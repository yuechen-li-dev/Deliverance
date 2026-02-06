using Deliverance.Core;
using Deliverance.Core.Serialization;
using Deliverance.Core.Storage;

// Stride namespaces (adjust to your Stride version)
using Stride.Core;

namespace Deliverance.StrideConn;

public static class DeliveranceStrideExtensions
{
    /// <summary>
    /// Registers Deliverance in Stride's service registry.
    /// This is intentionally minimal: storage location + build id are your main knobs.
    /// </summary>
    public static IDeliverance AddDeliverance(
        this IServiceRegistry services,
        string saveRootDirectory,
        string? buildId = null)
    {
        var serializer = new MessagePackSaveSerializer();

        var options = new DeliveranceOptions
        {
            Store = new FileSaveStore(saveRootDirectory),
            Serializer = serializer,
            BuildId = buildId,
        };

        var deliverance = new DeliveranceService(options);

        // Register under both concrete + interface for convenience
        services.AddService(typeof(IDeliverance), deliverance);
        services.AddService(typeof(DeliveranceService), deliverance);

        return deliverance;
    }
}
