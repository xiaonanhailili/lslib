﻿using System;
using System.Collections.Generic;
using LSLib.Granny.GR2;

#pragma warning disable 0649

namespace LSLib.Granny.Model.VertexFormats
{
    [StructSerialization(MixedMarshal = true)]
    internal class PNGBDT333342_Prototype
    {
        [Serialization(ArraySize = 3)]
        public float[] Position;
        [Serialization(ArraySize = 3)]
        public float[] Normal;
        [Serialization(ArraySize = 3)]
        public float[] Tangent;
        [Serialization(ArraySize = 3)]
        public float[] Binormal;
        [Serialization(ArraySize = 4)]
        public float[] DiffuseColor0;
        [Serialization(ArraySize = 2)]
        public float[] TextureCoordinates0;
    }

    [VertexPrototype(Prototype = typeof(PNGBDT333342_Prototype)),
    VertexDescription(Position = true, Normal = true, Tangent = true, Binormal = true, DiffuseColors = 1, TextureCoordinates = 1)]
    public class PNGBDT333342 : Vertex
    {
        public override List<String> ComponentNames()
        {
            return new List<String> { "Position", "Normal", "Tangent", "Binormal", "DiffuseColor0", "MaxChannel_1" };
        }

        public override void Serialize(WritableSection section)
        {
            WriteVector3(section, Position);
            WriteVector3(section, Normal);
            WriteVector3(section, Tangent);
            WriteVector3(section, Binormal);
            WriteVector4(section, DiffuseColor0);
            WriteVector2(section, TextureCoordinates0);
        }

        public override void Unserialize(GR2Reader reader)
        {
            Position = ReadVector3(reader);
            Normal = ReadVector3(reader);
            Tangent = ReadVector3(reader);
            Binormal = ReadVector3(reader);
            DiffuseColor0 = ReadVector4(reader);
            TextureCoordinates0 = ReadVector2(reader);
        }
    }
}
