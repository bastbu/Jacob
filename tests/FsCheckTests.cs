// Copyright (c) 2021 Atif Aziz.
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Jacob.Tests;

using FsCheck;
using FsCheck.Xunit;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.Interfaces;
using MoreLinq.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using static FsCheck.TestResult;

interface ISerializableObject
{
    string Serialize();
    IJsonReader<object> GetDeserializer();
    Func<object, bool> ValueComparer();
}

sealed class JsonString : ISerializableObject
{
    public object Value { get; }

    public JsonString(string value) => Value = value;

    public string Serialize() => JsonSerializer.Serialize((string)Value);
    public IJsonReader<object> GetDeserializer() => from s in JsonReader.String()
                                                    select (object)s;

    public Func<object, bool> ValueComparer() => Value.Equals;

    public static implicit operator JsonString(string value) => new(value);

    public override string ToString() => "String";
}

sealed class JsonNumber : ISerializableObject
{
    public object Value { get; }

    public JsonNumber(int value) => Value = value;


    public string Serialize() => JsonSerializer.Serialize((int)Value);
    public IJsonReader<object> GetDeserializer() => from i in JsonReader.Int32()
                                                    select (object)i;
    public static implicit operator JsonNumber(int value) => new(value);

    public Func<object, bool> ValueComparer() => Value.Equals;

    public override string ToString() => "Int32";
}

sealed class JsonArray : ISerializableObject
{
    readonly IReadOnlyCollection<ISerializableObject> value;

    public JsonArray(IReadOnlyCollection<ISerializableObject> value) => this.value = value;

    public string Serialize() =>
        $"[{string.Join(',', from v in this.value select v.Serialize())}]";

    public IJsonReader<object> GetDeserializer() =>
        JsonReader.Array(this.value.FirstOrDefault()?.GetDeserializer() ?? (from i in JsonReader.String().OrNull() select (object)i!),
                         arr => (object)arr);

    public Func<object, bool> ValueComparer() => o =>
    {
        var other = ((IEnumerable<object>)o).ToList();

        if (other.Count != this.value.Count) return false;

        return this.value.Zip(other, (l, r) => l.ValueComparer()(r)).All(b => b);
    };

    public override string ToString() => $"{this.value}";
}

public sealed class FsCheckTests
{
    readonly Gen<ISerializableObject> generator;
#pragma warning disable IDE0052 // Remove unread private members
    readonly ITestOutputHelper testOutputHelper;
#pragma warning restore IDE0052 // Remove unread private members

    public FsCheckTests(ITestOutputHelper testOutputHelper)
    {
        var primitiveGenerators = new List<Gen<ISerializableObject>>
        {
            from s in Arb.Generate<string>()
            where s != null
            select (ISerializableObject)new JsonString(s),
            from i in Arb.Generate<int>()
            select (ISerializableObject)new JsonNumber(i)
        };

        var listGenerator =
            from g in Gen.Sized(s =>
            {
                if (s == 0)
                {
                    return from el in Gen.ListOf(primitiveGenerators.RandomSubset(1).First())
                           select (ISerializableObject)new JsonArray(el);
                }
                else
                {
                    return from el in Gen.ListOf(primitiveGenerators.RandomSubset(1).First())
                           select (ISerializableObject)new JsonArray(el);
                }
            })
            select g;

        // primitiveGenerators.Add(listGenerator);

        this.generator = Gen.OneOf(Gen.OneOf(primitiveGenerators), listGenerator);
        this.testOutputHelper = testOutputHelper;
    }

    [Property(Verbose = true)]
    public Property ParityWithSystemTextJson() => Prop.ForAll(Arb.From(this.generator), arb =>
    {
        var deserializer = arb.GetDeserializer();
        this.testOutputHelper.WriteLine("Serialized value: " + arb.Serialize());
        this.testOutputHelper.WriteLine("Serialized deserialized value: " + JsonSerializer.Serialize(deserializer.Read(arb.Serialize())));
        var equals = arb.ValueComparer()(deserializer.Read(arb.Serialize()));
        return equals;
    });
}
