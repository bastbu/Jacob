# Jacob

Jacob provides a succinct JSON combinator API over `Utf8JsonReader` for
deserializing/reading JSON data.

.NET Core 3.0 introduced [`Utf8JsonReader`], a high-performance, low-allocation,
forward-only reader for JSON text encoded in UTF-8. It is a low-level type for
building custom parsers and deserializers, and because it has _low-level_
semantics, its API is not very straightforward to use. It's far more common
instead to see developers create simple classes that mirror the JSON data and
then use [`JsonSerializer`] to deserialize/read the JSON data into instances of
those classes. However, when your application or domain object model is far
richer than the value types available in JSON, you have two possible approaches:

1. Customize the deserialization by writing and registering a [`JsonConverter`].
   This requires a non-trivial amount of coding and understanding, not to
   mention having to work with `Utf8JsonReader` and writing unit tests for each
   converter. Moreover, the converters are not immediately reusable and
   composable (in an ad-hoc manner or otherwise) without considerable effort.

2. Maintain two sets of object models: one that is usually anemic (containing
   very little logic) and for the sole purpose of deserializing from JSON, and
   another that is the rich application/domain model. Then, have logic to
   transform the former into the latter. This requires extra processing as well
   as additional and temporary intermediate represetations of the data in
   memory.

Jacob provides most of the benefits of `Utf8JsonReader`, such as low-allocation,
but without all the ceremony and complexity associated with either of the above
approaches.


## Usage

All of the following examples assume these imports:

```c#
using System;
using System.Collections.Generic;
using System.Text.Json;
using Jacob;
```

Starting simple, here is how to read a JSON number as an `int` in C# (or
`System.Int32`):

```c#
var x = JsonReader.Int32().Read("42");
Console.WriteLine(x);
```

Here is how to read a JSON string as a .NET string:

```c#
var s = JsonReader.String().Read(""" "foobar" """);
Console.WriteLine(s);
```

Deserializing simple types like a string or an integer is pretty straightforward
and can be done with [`JsonSerializer.Deserialize`][deserialize] with equal
ease:

```c#
var x = JsonSerializer.Deserialize<int>("42");
Console.WriteLine(x);

var s = JsonSerializer.Deserialize<string>(""" "foobar" """);
Console.WriteLine(s);
```

However, the next example shows the benefits of a compositional API. It
demonstrates how to read a tuple of string and integer expressed in JSON as
an array of two elements:

```c#
var (key, value) =
    JsonReader.Tuple(JsonReader.String(), JsonReader.Int32())
              .Read("""["foobar", 42]""");
Console.WriteLine($"Key = {key}, Value = {value}");
```

Note how `JsonReader.Tuple` builds on top of the string and integer reader
introduced in previous examples. By supplying those readers as arguments, the
tuple reader then knows how to read each item of the tuple. The tuple reader
also knows to expect only a JSON array of two elements; if more elements are
supplied, as in `["foobar", 42, null]`, then it will produce the following
error:

    Invalid JSON value; JSON array has too many values. See token "Null" at offset 13.

Attempting to do the same with `JsonSerializer.Deserialize` (assuming .NET 6):

```c#
var json = """["foobar", 42]""";
var (key, value) = JsonSerializer.Deserialize<(string, int)>(json);
Console.WriteLine($"Key = {key}, Value = {value}");
```

fails with the error:

    The JSON value could not be converted to System.ValueTuple`2[System.String,System.Int32]. Path: $ | LineNumber: 0 | BytePositionInLine: 1.

and this is where a custom [`JsonConverter`] implementation would be needed that
knows how to deserialize a tuple of a string and an integer from a JSON array.

Once a reader is initialized, it can be reused. In the next example, the reader
is defined outside the loop and then used to read JSON through each iteration:

```c#
var reader = JsonReader.Tuple(JsonReader.String(), JsonReader.Int32());

foreach (var json in new[]
                     {
                         """["foo", 123]""",
                         """["bar", 456]""",
                         """["baz", 789]""",
                     })
{
    var (key, value) = reader.Read(json);
    Console.WriteLine($"Key = {key}, Value = {value}");
}
```

Jacob also enables use of LINQ so you can read a value as one type and project
it to a value of another type that may be closer to what the application might
desire:

```c#
var reader =
    from t in JsonReader.Tuple(JsonReader.String(), JsonReader.Int32())
    select KeyValuePair.Create(t.Item1, t.Item2);

var pair = reader.Read("""["foobar", 42]""");
Console.WriteLine(pair);
```

In the above example, a tuple `(string, int)` is converted to a
`KeyValuePair<string, int>`. Again, this demonstrates how readers just compose
with one another. In the same vein, if an array of key-value pairs is what's
needed, then it's just another composition away (this time, with
`JsonReader.Array`):

```c#
var pairReader =
    from t in JsonReader.Tuple(JsonReader.String(), JsonReader.Int32())
    select KeyValuePair.Create(t.Item1, t.Item2);

const string json = """
    [
        ["foo", 123],
        ["bar", 456],
        ["baz", 789]
    ]
    """;

var pairs = JsonReader.Array(pairReader).Read(json);

foreach (var (key, value) in pairs)
    Console.WriteLine($"Key = {key}, Value = {value}");
```

`JsonReader.Either` allows a JSON value to be read in one of two ways. For
example, here a JSON array is read whose elements are either an integer
or a string:

```c#
var reader =
    JsonReader.Either(JsonReader.String().AsObject(),
                      JsonReader.Int32().AsObject());

const string json = """["foo", 123, "bar", 456, "baz", 789]""";

var values = JsonReader.Array(reader).Read(json);

foreach (var value in values)
    Console.WriteLine($"{value} ({value.GetType().Name})");

/* outputs:

foo (String)
123 (Int32)
bar (String)
456 (Int32)
baz (String)
789 (Int32)
*/
```

`Either` expects either reader to return the same type of value, which is why
`AsObject()` is used to convert each read string or integer to the common super
type `object`. `Either` can also be combined with itself to support reading a
JSON value in more than two ways.

Finally, there's `JsonReader.Object` that takes property definitions via
`JsonReader.Property` and a function to combine the read values into the final
object:

```c#
var pairReader =
    JsonReader.Object(
        JsonReader.Property("key", JsonReader.String()),
        JsonReader.Property("value", JsonReader.Int32()),
        KeyValuePair.Create /*
        above is same as:
        (k, v) => KeyValuePair.Create(k, v) */
    );

const string json = """
    [
        { "key"  : "foo", "value": 123   },
        { "value": 456  , "key"  : "bar" },
        { "key"  : "baz", "value": 789   }
    ]
    """;

var pairs = JsonReader.Array(pairReader).Read(json);

foreach (var (key, value) in pairs)
    Console.WriteLine($"Key = {key}, Value = {value}");
```

Once more, `JsonReader.Object` builds on top of readers for each property for
a combined effect of creating an object from the constituent parts.


## Partial JSON Reading

Reading partial JSON is supported by most readers. It enables large JSON text
data to be read and processed in chunks without committing it entirely to
memory.

All readers support `TryRead`, which returns a `JsonReadResult<T>` that either
represents the read value or an error with reading the value. When JSON data
is partially loaded in a buffer such that a reader cannot complete its reading
then the `Incomplete` property of the returned `JsonReadResult<T>` will be
`true`. This is a signal to the caller that it must load the buffer with more
of the JSON source text to resume reading.

All readers can be composed with `JsonReader.Buffer` to load enough
data into the buffer so that none can fail due to partial JSON.
`JsonReader.Buffer` ensures that at least one complete JSON value (be that a
scalar like a string or a structure like an array or an object) is buffered.


## Limitations

- There is no API at this time for writing JSON data.

- When using `JsonReader.Object` with `JsonReader.Property`, a maximum of 16
  properties of a JSON object can be read.


[`Utf8JsonReader`]: https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-use-dom-utf8jsonreader-utf8jsonwriter?pivots=dotnet-6-0#use-utf8jsonreader
[`JsonSerializer`]: https://docs.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer
[`JsonConverter`]: https://docs.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonconverter?view=net-6.0
[x-strict-json]: https://github.com/JamesNK/Newtonsoft.Json/issues/646#issuecomment-356194475
[deserialize]: https://docs.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer.deserialize?view=net-6.0
[Newtonsoft.Json (13.x)]: https://www.nuget.org/packages/Newtonsoft.Json/13.0.1
