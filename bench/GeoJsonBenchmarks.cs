// Copyright (c) 2021 Atif Aziz.
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable CA1050

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Jacob.Examples.GeoJson;
using JsonElement = System.Text.Json.JsonElement;
using JsonReader = Jacob.JsonReader;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;
using static MoreLinq.Extensions.RepeatExtension;
using System.Collections.Generic;

[MemoryDiagnoser]
public class GeoJsonBenchmarks
{
    const string PointJson = /*lang=json*/ """
        {
            "type": "Point",
            "coordinates": [100.0, 0.0]
        }
        """;

    const string LineStringJson = /*lang=json*/ """
        {
            "type": "LineString",
            "coordinates": [
                [100.0, 0.0],
                [101.0, 1.0]
            ]
        }
        """;

    const string PolygonJson = /*lang=json*/ """
        {
            "type": "Polygon",
            "coordinates": [
                [
                    [100.0, 0.0],
                    [101.0, 0.0],
                    [101.0, 1.0],
                    [100.0, 1.0],
                    [100.0, 0.0]
                ],
                [
                    [100.8, 0.8],
                    [100.8, 0.2],
                    [100.2, 0.2],
                    [100.2, 0.8],
                    [100.8, 0.8]
                ]
            ]
        }
        """;

    const string MultiPointJson = /*lang=json*/ """
        {
            "type": "MultiPoint",
            "coordinates": [
                [100.0, 0.0],
                [101.0, 1.0]
            ]
        }
        """;

    const string MultiLineStringJson = /*lang=json*/ """
        {
            "type": "MultiLineString",
            "coordinates": [
                [
                    [100.0, 0.0],
                    [101.0, 1.0]
                ],
                [
                    [102.0, 2.0],
                    [103.0, 3.0]
                ]
            ]
        }
        """;

    const string MultiPolygonJson = /*lang=json*/ """
        {
            "type": "MultiPolygon",
            "coordinates": [
                [
                    [
                        [102.0, 2.0],
                        [103.0, 2.0],
                        [103.0, 3.0],
                        [102.0, 3.0],
                        [102.0, 2.0]
                    ]
                ],
                [
                    [
                        [100.0, 0.0],
                        [101.0, 0.0],
                        [101.0, 1.0],
                        [100.0, 1.0],
                        [100.0, 0.0]
                    ],
                    [
                        [100.2, 0.2],
                        [100.2, 0.8],
                        [100.8, 0.8],
                        [100.8, 0.2],
                        [100.2, 0.2]
                    ]
                ]
            ]
        }
        """;

    const string GeometryCollectionJson = /*lang=json*/ """
        {
            "type": "GeometryCollection",
            "geometries": [{
                "type": "Point",
                "coordinates": [100.0, 0.0]
            }, {
                "type": "LineString",
                "coordinates": [
                    [101.0, 0.0],
                    [102.0, 1.0]
                ]
            }]
        }
        """;

    public enum SampleSetId
    {
        All,
        MultiPolygon
    }

    static readonly Dictionary<SampleSetId, string[]> Jsons = new()
    {
        [SampleSetId.All] = new[]
        {
            PointJson, LineStringJson, PolygonJson,
            MultiPointJson, MultiLineStringJson,
            MultiPolygonJson, GeometryCollectionJson
        },
        [SampleSetId.MultiPolygon] = new[] { MultiPolygonJson }
    };

    byte[] jsonDataBytes = Array.Empty<byte>();

    [Params(10, 100, 1000, 10000)] public int ObjectCount { get; set; }

    [ParamsAllValues] public SampleSetId SampleSet { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var json = new StringBuilder("[");
        _ = json.Append(string.Join(',', Jsons[SampleSet].Repeat().Take(ObjectCount)));
        _ = json.Append(']');

        this.jsonDataBytes = Encoding.UTF8.GetBytes(json.ToString());
    }

    [Benchmark]
    public Geometry[] JsonReaderBenchmark()
    {
        return JsonReader.Array(GeoJsonReaders.Geometry).Read(this.jsonDataBytes);
    }

    [Benchmark(Baseline = true)]
    public Geometry[] SystemTextJsonBenchmark()
    {
        return SystemTextGeoJsonReader.Read(this.jsonDataBytes);
    }

    static class SystemTextGeoJsonReader
    {
        static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static Geometry[] Read(byte[] json) =>
            JsonSerializer.Deserialize<GeometryJson[]>(json, SerializerOptions)!.Select(ConvertToGeometry).ToArray();

        static Position ConvertToPosition(JsonElement e) =>
            e.GetArrayLength() is var len and (2 or 3)
                ? new Position(e[0].GetDouble(), e[1].GetDouble(), len is 2 ? 0 : e[2].GetDouble())
                : throw new ArgumentException(null, nameof(e));

        static ImmutableArray<Position> ConvertToPositionsArray(JsonElement e) =>
            ImmutableArray.CreateRange(from jsonElement in e.EnumerateArray()
                                       select ConvertToPosition(jsonElement));

        static ImmutableArray<Position> ConvertToLineStringPositions(JsonElement e) =>
            ConvertToPositionsArray(e) is { Length: >= 2 } result
                ? result
                : throw new ArgumentException(null, nameof(e));

        static ImmutableArray<ImmutableArray<Position>>
            ConvertToPolygonPositions(JsonElement e) =>
            ImmutableArray.CreateRange(from jsonElement in e.EnumerateArray()
                                       select ConvertToPositionsArray(jsonElement) is
                                       { Length: >= 4 } result
                                           ? result
                                           : throw new ArgumentException(null, nameof(e))) is
            { Length: >= 1 } result
                ? result
                : throw new ArgumentException(null, nameof(e));

        static Geometry ConvertToGeometry(GeometryJson g) =>
            g.Type switch
            {
                "GeometryCollection" => new GeometryCollection(g.Geometries.Select(ConvertToGeometry)
                                                                .ToImmutableArray()),
                "Point" => new Point(ConvertToPosition(g.Coordinates)),
                "LineString" => new LineString(ConvertToLineStringPositions(g.Coordinates)),
                "MultiPoint" => new MultiPoint(ConvertToPositionsArray(g.Coordinates)),
                "MultiLineString" => new MultiLineString(ImmutableArray.CreateRange(
                    from el in g.Coordinates.EnumerateArray()
                    select ConvertToLineStringPositions(el))),
                "Polygon" => new Polygon(ConvertToPolygonPositions(g.Coordinates)),
                "MultiPolygon" => new MultiPolygon(g.Coordinates.EnumerateArray()
                                                    .Select(ConvertToPolygonPositions)
                                                    .ToImmutableArray()),
                _ => throw new InvalidOperationException($"Type {g.Type} is not supported.")
            };

        record struct GeometryJson(string Type, GeometryJson[] Geometries,
                                   JsonElement Coordinates);
    }
}

