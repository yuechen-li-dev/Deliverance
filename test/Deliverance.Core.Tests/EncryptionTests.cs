using System.Security.Cryptography;
using Deliverance.Core.Codecs;
using Deliverance.Core.Encryption;
using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;

namespace Deliverance.Core.Tests;

public sealed class EncryptionTests
{
    [Fact]
    public async Task AesGcm_RoundTrips_AndUsesUniqueNonce()
    {
        var store = new InMemorySaveStore();
        MessagePackSaveSerializer serializer = new();
        DeliveranceService deliverance = CreateEncrypted(store, serializer, RandomNumberGenerator.GetBytes(32));
        SaveRequest request = Request(serializer);

        await deliverance.SaveAsync("one", request);
        await deliverance.SaveAsync("two", request);
        Assert.NotEqual(store.GetBytes("one"), store.GetBytes("two"));

        LoadedSaveCandidate candidate = await deliverance.LoadAsync(
            "one",
            [new SaveModuleDefinition("world", 1, ModuleCriticality.Required)]);
        Assert.Equal("secret", candidate.Deserialize<string>("world", deliverance.Options.Serializers));
        Assert.Equal(1, (await deliverance.InspectSlotAsync("one")).Chunks.Single().EncryptionId);
    }

    [Fact]
    public async Task WrongKeyAndTampering_AreAuthenticationFailures()
    {
        var store = new InMemorySaveStore();
        MessagePackSaveSerializer serializer = new();
        DeliveranceService writer = CreateEncrypted(store, serializer, RandomNumberGenerator.GetBytes(32));
        await writer.SaveAsync("slot", Request(serializer));
        SaveModuleDefinition definition = new("world", 1, ModuleCriticality.Required);

        DeliveranceService wrongKey = CreateEncrypted(store, serializer, RandomNumberGenerator.GetBytes(32));
        DeliveranceException wrong = await Assert.ThrowsAsync<DeliveranceException>(
            () => wrongKey.LoadAsync("slot", [definition]));
        Assert.Equal(SaveDiagnosticCode.AuthenticationFailed, wrong.Code);

        byte[] bytes = store.GetBytes("slot");
        bytes[^1] ^= 1;
        store.SetBytes("slot", bytes);
        DeliveranceException tampered = await Assert.ThrowsAsync<DeliveranceException>(
            () => writer.LoadAsync("slot", [definition]));
        Assert.Equal(SaveDiagnosticCode.AuthenticationFailed, tampered.Code);
    }

    [Fact]
    public async Task EncryptionRequiresCallerKeyProvider()
    {
        var store = new InMemorySaveStore();
        MessagePackSaveSerializer serializer = new();
        var deliverance = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = serializer,
            DefaultEncryption = new AesGcmEncryptionCodec(),
        });
        deliverance.Options.Codecs.Register(new GzipCodec());

        DeliveranceException exception = await Assert.ThrowsAsync<DeliveranceException>(
            () => deliverance.SaveAsync("slot", Request(serializer)));
        Assert.Equal(SaveDiagnosticCode.EncryptionKeyUnavailable, exception.Code);
    }

    private static DeliveranceService CreateEncrypted(InMemorySaveStore store, MessagePackSaveSerializer serializer, byte[] key)
    {
        var deliverance = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = serializer,
            DefaultEncryption = new AesGcmEncryptionCodec(),
            EncryptionKeyProvider = new FixedKeyProvider(key),
        });
        deliverance.Options.Codecs.Register(new GzipCodec());
        return deliverance;
    }

    private static SaveRequest Request(MessagePackSaveSerializer serializer)
    {
        SaveModulePayload payload = SaveModulePayload.Create(
            "world", 1, ModuleCriticality.Required, serializer, new GzipCodec(), "secret");
        return new SaveRequest(new SaveApplicationMetadata("app"), [payload]);
    }

    private sealed class FixedKeyProvider(byte[] key) : IEncryptionKeyProvider
    {
        public ValueTask<ReadOnlyMemory<byte>> GetKeyAsync(EncryptionKeyContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(key);
        }
    }
}
