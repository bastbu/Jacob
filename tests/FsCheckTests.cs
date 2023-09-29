// Copyright (c) 2021 Atif Aziz.
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Jacob.Tests;

using FsCheck;
using FsCheck.Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

interface ISerializableObject
{
    string Serialize();
    IJsonReader<object> GetDeserializer();
    object Value { get; }
}

sealed class JsonString : ISerializableObject
{
    public object Value { get; }

    public JsonString(string value) => Value = value;

    public string Serialize() => JsonSerializer.Serialize(Value);
    public IJsonReader<object> GetDeserializer() => from s in JsonReader.String()
                                                    select (object)s;

    public static implicit operator JsonString(string value) => new(value);
}

sealed class JsonNumber : ISerializableObject
{
    public object Value { get; }

    public JsonNumber(int value) => Value = value;


    public string Serialize() => JsonSerializer.Serialize(Value);
    public IJsonReader<object> GetDeserializer() => from i in JsonReader.Int32()
                                                    select (object)i;
    public static implicit operator JsonNumber(int value) => new(value);
}

sealed class JsonArray : ISerializableObject
{
    readonly IReadOnlyCollection<ISerializableObject> value;

    public object Value => this.value.Select(v => v.Value).ToList();

    public JsonArray(IReadOnlyCollection<ISerializableObject> value) => this.value = value;

    public string Serialize() => JsonSerializer.Serialize(Value);
    public IJsonReader<object> GetDeserializer() =>
        JsonReader.Array(this.value.FirstOrDefault()?.GetDeserializer() ?? (from i in JsonReader.Int32() select (object)i),
                         arr => (object)arr);
}

public sealed class FsCheckTests
{
    readonly Gen<ISerializableObject> generator;

    public FsCheckTests()
    {
        var primitiveGenerator = Gen.OneOf(new Gen<ISerializableObject>[]
        {
            from s in Arb.Generate<string>()
            select (ISerializableObject)new JsonString(s),
            from i in Arb.Generate<int>()
            select (ISerializableObject)new JsonNumber(i)
        });

        var recursiveGenerator =
            from len in Gen.Choose(0, 10)
            from l in Gen.ListOf(primitiveGenerator)
            select (ISerializableObject)new JsonArray(l);

        this.generator = Gen.OneOf(primitiveGenerator, recursiveGenerator);
    }

    [Fact]
    public void Tests()
    {
        Prop.ForAll(Arb.From(this.generator), arb =>
        {
            var deserializer = arb.GetDeserializer();
            var serialized = arb.Serialize();
            var deserialized = deserializer.Read(serialized);
            var serialized2 = JsonSerializer.Serialize(deserialized);
            return serialized.Equals(serialized2, System.StringComparison.Ordinal);
        }).VerboseCheckThrowOnFailure();
    }

    [Property]
    public Property Success() => Prop.ForAll(Arb.From(this.generator), arb =>
    {
        var deserializer = arb.GetDeserializer();
        var equals = deserializer.Read(arb.Serialize()) == arb;
        return equals;
    });
}
