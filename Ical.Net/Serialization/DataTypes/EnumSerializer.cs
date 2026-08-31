//
// Copyright ical.net project maintainers and contributors.
// Licensed under the MIT license.
//

using System;
using System.IO;
using Ical.Net.DataTypes;

namespace Ical.Net.Serialization.DataTypes;

public class EnumSerializer : EncodableDataTypeSerializer
{
    private readonly Type _mEnumType;

    public EnumSerializer(Type enumType)
    {
        _mEnumType = enumType;
    }

    public EnumSerializer(Type enumType, SerializationContext ctx) : base(ctx)
    {
        _mEnumType = enumType;
    }

    public override Type TargetType => _mEnumType;

    public override string? SerializeToString(object? obj)
    {
        try
        {
            if (SerializationContext.Peek() is ICalendarObject calObject)
            {
                // Encode the value as needed.
                var dt = new EncodableDataType
                {
                    AssociatedObject = calObject
                };
                return Encode(dt, obj?.ToString());
            }
            return obj?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public override object? Deserialize(TextReader tr)
    {
        var value = tr.ReadToEnd();

        if (SerializationContext.Peek() is ICalendarObject obj)
        {
            // Decode the value, if necessary!
            var dt = new EncodableDataType
            {
                AssociatedObject = obj
            };
            value = Decode(dt, value);
        }

        if (value == null)
        {
            return null;
        }

        try
        {
            // Remove "-" characters while parsing Enum values.
            return Enum.Parse(_mEnumType, value.Replace("-", ""), true);
        }
        catch (ArgumentException)
        {
            // The value is not a member of the enum - which happens with real-world .ics files.
            // Fall back to the raw string rather than failing the whole parse.
            //
            // Only ArgumentException is caught: a bare catch here used to turn any failure,
            // including a decoding error, into a silently mistyped property value.
            return value;
        }
    }
}
