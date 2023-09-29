// Copyright (c) 2021 Atif Aziz.
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Jacob.Tests;

using FsCheck;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

interface ISerializableObject<T> where T : ISerializableObject<T>, IEquatable<T>
{
    string Serialize(object value);
    static abstract IJsonReader<T> GetDeserializer();
}

sealed class JsonString : ISerializableObject<JsonString>, IEquatable<JsonString>
{
    string Value { get; }

    public JsonString(string value) => Value = value;

    public override bool Equals(object? obj) => Equals(obj as JsonString);
    public bool Equals(JsonString? other) => Value.Equals(other?.Value, StringComparison.Ordinal);
    public override int GetHashCode() => HashCode.Combine(Value);


    public string Serialize(object value) => JsonSerializer.Serialize(value);
    public static IJsonReader<JsonString> GetDeserializer() => from s in JsonReader.String()
                                                               select new JsonString(s);
}

sealed class JsonNumber : ISerializableObject<JsonNumber>, IEquatable<JsonNumber>
{
    int Value { get; }

    public JsonNumber(int value) => Value = value;

    public override bool Equals(object? obj) => Equals(obj as JsonNumber);
    public bool Equals(JsonNumber? other) => Value.Equals(other?.Value);
    public override int GetHashCode() => HashCode.Combine(Value);


    public string Serialize(object value) => JsonSerializer.Serialize(value);
    public static IJsonReader<JsonNumber> GetDeserializer() => from i in JsonReader.Int32()
                                                               select new JsonNumber(i);
}

sealed class JsonArray<T> : ISerializableObject<JsonArray<T>>, IEquatable<JsonArray<T>>
    where T : ISerializableObject<T>, IEquatable<T>
{
    IReadOnlyCollection<T> Value { get; }

    public JsonArray(IReadOnlyCollection<T> value) => Value = value;

    public override bool Equals(object? obj) => Equals(obj as JsonArray<T>);
    public bool Equals(JsonArray<T>? other) => other is { } someOther && Value.SequenceEqual(someOther.Value);
    public override int GetHashCode() => HashCode.Combine(Value);


    public string Serialize(object value) => JsonSerializer.Serialize(value);
    public static IJsonReader<JsonArray<T>> GetDeserializer() => JsonReader.Array(T.GetDeserializer(), arr => new JsonArray<T>(arr));
}

public sealed class FsCheckTests
{
    private Gen<ISerializableObject<object>> generator;

    public FsCheckTests()
    {
        var primitiveGenerator = Gen.OneOf<ISerializableObject<object>>(new[]
        {
            from s in Arb.Generate<string>()
            select (ISerializableObject<object>)new JsonString(s),
            from i in Arb.Generate<int>()
            select (ISerializableObject<object>)new JsonNumber(i)
        });

        var recursiveGenerator =
            from r in Gen.OneOf<object>(new[]
            {
                Arb.Generate<>
            });

        this.generator = Gen.OneOf<ISerializableObject<object>>(primitiveGenerator, recursiveGenerator);
    }
}
