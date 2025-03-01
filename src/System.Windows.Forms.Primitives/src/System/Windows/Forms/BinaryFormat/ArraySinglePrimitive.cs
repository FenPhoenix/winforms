﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.BinaryFormat;

/// <summary>
///  Single dimensional array of a primitive type.
/// </summary>
/// <remarks>
///  <para>
///   <see href="https://learn.microsoft.com/openspecs/windows_protocols/ms-nrbf/3a50a305-5f32-48a1-a42a-c34054db310b">
///    [MS-NRBF] 2.4.3.3
///   </see>
///  </para>
/// </remarks>
internal sealed class ArraySinglePrimitive : ArrayRecord, IRecord<ArraySinglePrimitive>
{
    public PrimitiveType PrimitiveType { get; }

    public static RecordType RecordType => RecordType.ArraySinglePrimitive;

    public ArraySinglePrimitive(ArrayInfo arrayInfo, PrimitiveType primitiveType, IReadOnlyList<object> arrayObjects)
        : base(arrayInfo, arrayObjects)
    {
        PrimitiveType = primitiveType;
    }

    static ArraySinglePrimitive IBinaryFormatParseable<ArraySinglePrimitive>.Parse(
        BinaryReader reader,
        RecordMap recordMap)
    {
        ArrayInfo arrayInfo = ArrayInfo.Parse(reader, out Count length);
        PrimitiveType primitiveType = (PrimitiveType)reader.ReadByte();

        ArraySinglePrimitive record = new(
            arrayInfo,
            primitiveType,
            ReadPrimitiveTypes(reader, primitiveType, length));

        recordMap[record.ObjectId] = record;
        return record;
    }

    public override void Write(BinaryWriter writer)
    {
        writer.Write((byte)RecordType);
        ArrayInfo.Write(writer);
        writer.Write((byte)PrimitiveType);
        WritePrimitiveTypes(writer, PrimitiveType, ArrayObjects);
    }
}
