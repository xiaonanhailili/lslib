﻿using System;
using System.Collections.Generic;
using LSLib.Granny.GR2;

#pragma warning disable 0649

namespace LSLib.Granny.Model.VertexFormats
{
    [StructSerialization(MixedMarshal = true)]
    internal class PWNGB32333_Prototype
    {
        [Serialization(ArraySize = 3)]
        public float[] Position;
        [Serialization(ArraySize = 2, Type = MemberType.NormalUInt8)]
        public byte[] BoneWeights;
        [Serialization(ArraySize = 2)]
        public byte[] BoneIndices;
        [Serialization(ArraySize = 3)]
        public float[] Normal;
        [Serialization(ArraySize = 3)]
        public float[] Tangent;
        [Serialization(ArraySize = 3)]
        public float[] Binormal;
    }

    [VertexPrototype(Prototype = typeof(PWNGB32333_Prototype)),
    VertexDescription(Position = true, BoneWeights = true, BoneIndices = true, Normal = true, Tangent = true, Binormal = true)]
    public class PWNGB32333 : Vertex
    {
        public override List<String> ComponentNames()
        {
            return new List<String> { "Position", "BoneWeights", "BoneIndices", "Normal", "Tangent", "Binormal" };
        }

        public override void Serialize(WritableSection section)
        {
            WriteVector3(section, Position);
            WriteInfluences2(section, BoneWeights);
            WriteInfluences2(section, BoneIndices);
            WriteVector3(section, Normal);
            WriteVector3(section, Tangent);
            WriteVector3(section, Binormal);
        }

        public override void Unserialize(GR2Reader reader)
        {
            Position = ReadVector3(reader);
            BoneWeights = ReadInfluences2(reader);
            BoneIndices = ReadInfluences2(reader);
            Normal = ReadVector3(reader);
            Tangent = ReadVector3(reader);
            Binormal = ReadVector3(reader);
        }
    }
}
