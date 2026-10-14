using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NetWiv;
using Xunit;

namespace NetWiv.Generators.Tests;

public sealed class PolymorphicCollectionTests
{
    [Fact]
    public void SourceGeneratedCollectionRootUsesValuesEnvelopeInsteadOfDtoProperties()
    {
        CollectionRoot value = new CollectionVariant { 1, 2 };
        var info = CollectionPolymorphismContext.Default.CollectionRoot;
        Assert.Equal(JsonTypeInfoKind.Enumerable, info.Kind);
        const string wire = """{"$type":"items","$values":[1,2]}""";
        Assert.Equal(wire, JsonSerializer.Serialize(value, info));
        var restored = Assert.IsType<CollectionVariant>(JsonSerializer.Deserialize(wire, info));
        Assert.Equal([1, 2], restored);
        var error = Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(JsonValue.Parse(wire), info, "collection", "value", false));
        Assert.Contains("expected a sequence", error.Message);
    }

    [Fact]
    public void ObjectInterfaceCanSelectACollectionWireContract()
    {
        IObjectCollection value = new ObjectCollectionVariant { 1, 2 };
        var info = CollectionPolymorphismContext.Default.IObjectCollection;
        Assert.Equal(JsonTypeInfoKind.Object, info.Kind);
        Assert.Equal(JsonTypeInfoKind.Enumerable, info.Options.GetTypeInfo(typeof(ObjectCollectionVariant)).Kind);
        const string wire = """{"$type":"items","$values":[1,2]}""";
        Assert.Equal(wire, JsonSerializer.Serialize(value, info));
        Assert.Equal([1, 2], Assert.IsType<ObjectCollectionVariant>(JsonSerializer.Deserialize(wire, info)));
    }

    [Fact]
    public async Task AsyncCollectionsRequireAsyncSerializationAndRejectPolymorphicMetadata()
    {
        // STJ cannot configure discriminator metadata for its async collection converter.
        Assert.Throws<NotSupportedException>(() => CollectionPolymorphismContext.Default.IAsyncCollection);
        IAsyncEnumerable<int> value = AsyncCollectionVariant.Values();
        var info = CollectionPolymorphismContext.Default.AsyncValues;
        Assert.Equal(JsonTypeInfoKind.Enumerable, info.Kind);
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(value, info));
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, value, info);
        Assert.Equal("[1,2]", Encoding.UTF8.GetString(stream.ToArray()));
    }
}

[JsonPolymorphic, JsonDerivedType(typeof(CollectionVariant), "items")]
internal abstract class CollectionRoot : List<int>;
internal sealed class CollectionVariant : CollectionRoot;

[JsonPolymorphic, JsonDerivedType(typeof(ObjectCollectionVariant), "items")]
internal interface IObjectCollection;
internal sealed class ObjectCollectionVariant : List<int>, IObjectCollection;

[JsonPolymorphic, JsonDerivedType(typeof(AsyncCollectionVariant), "items")]
internal interface IAsyncCollection : IAsyncEnumerable<int>;
internal sealed class AsyncCollectionVariant : IAsyncCollection
{
    public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => Values().GetAsyncEnumerator(cancellationToken);
    internal static async IAsyncEnumerable<int> Values()
    {
        yield return 1;
        await Task.Yield();
        yield return 2;
    }
}

[JsonSerializable(typeof(CollectionRoot))]
[JsonSerializable(typeof(IObjectCollection))]
[JsonSerializable(typeof(IAsyncCollection))]
[JsonSerializable(typeof(IAsyncEnumerable<int>), TypeInfoPropertyName = "AsyncValues")]
internal partial class CollectionPolymorphismContext : JsonSerializerContext;
