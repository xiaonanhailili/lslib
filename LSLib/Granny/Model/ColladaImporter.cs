﻿using System;
using System.Collections.Generic;
using System.Linq;
using LSLib.Granny.GR2;
using LSLib.LS;
using OpenTK;

namespace LSLib.Granny.Model
{
    internal class ColladaSource
    {
        public String id;
        public Dictionary<String, List<Single>> FloatParams = new Dictionary<string, List<float>>();
        public Dictionary<String, List<Matrix4>> MatrixParams = new Dictionary<string, List<Matrix4>>();
        public Dictionary<String, List<String>> NameParams = new Dictionary<string, List<string>>();

        public static ColladaSource FromCollada(source src)
        {
            var source = new ColladaSource();
            source.id = src.id;

            var accessor = src.technique_common.accessor;
            // TODO: check src.#ID?

            float_array floats = null;
            Name_array names = null;
            if (src.Item is float_array)
            {
                floats = src.Item as float_array;
                // Workaround for empty arrays being null
                if (floats.Values == null)
                    floats.Values = new double[] { };

                if ((int)floats.count != floats.Values.Length || floats.count < accessor.stride * accessor.count + accessor.offset)
                    throw new ParsingException("Float source data size mismatch. Check source and accessor item counts.");
            }
            else if (src.Item is Name_array)
            {
                names = src.Item as Name_array;
                // Workaround for empty arrays being null
                if (names.Values == null)
                    names.Values = new string[] { };

                if ((int)names.count != names.Values.Length || names.count < accessor.stride * accessor.count + accessor.offset)
                    throw new ParsingException("Name source data size mismatch. Check source and accessor item counts.");
            }
            else
                throw new ParsingException("Unsupported source data format.");

            var paramOffset = 0;
            foreach (var param in accessor.param)
            {
                if (param.name == null)
                    param.name = "default";
                if (param.type == "float" || param.type == "double")
                {
                    var items = new List<Single>((int)accessor.count);
                    var offset = (int)accessor.offset;
                    for (var i = 0; i < (int)accessor.count; i++)
                    {
                        items.Add((float)floats.Values[offset + paramOffset]);
                        offset += (int)accessor.stride;
                    }

                    source.FloatParams.Add(param.name, items);
                }
                else if (param.type == "float4x4")
                {
                    var items = new List<Matrix4>((int)accessor.count);
                    var offset = (int)accessor.offset;
                    for (var i = 0; i < (int)accessor.count; i++)
                    {
                        var itemOff = offset + paramOffset;
                        var mat = new Matrix4(
                            (float)floats.Values[itemOff + 0], (float)floats.Values[itemOff + 1], (float)floats.Values[itemOff + 2], (float)floats.Values[itemOff + 3],
                            (float)floats.Values[itemOff + 4], (float)floats.Values[itemOff + 5], (float)floats.Values[itemOff + 6], (float)floats.Values[itemOff + 7],
                            (float)floats.Values[itemOff + 8], (float)floats.Values[itemOff + 9], (float)floats.Values[itemOff + 10], (float)floats.Values[itemOff + 11],
                            (float)floats.Values[itemOff + 12], (float)floats.Values[itemOff + 13], (float)floats.Values[itemOff + 14], (float)floats.Values[itemOff + 15]
                        );
                        items.Add(mat);
                        offset += (int)accessor.stride;
                    }

                    source.MatrixParams.Add(param.name, items);
                }
                else if (param.type.ToLower() == "name")
                {
                    var items = new List<String>((int)accessor.count);
                    var offset = (int)accessor.offset;
                    for (var i = 0; i < (int)accessor.count; i++)
                    {
                        items.Add(names.Values[offset + paramOffset]);
                        offset += (int)accessor.stride;
                    }

                    source.NameParams.Add(param.name, items);
                }
                else
                    throw new ParsingException("Unsupported accessor param type: " + param.type);

                paramOffset++;
            }

            return source;
        }
    }

    public class ColladaImporter
    {
        [Serialization(Kind = SerializationKind.None)]
        public ExporterOptions Options = new ExporterOptions();

        private bool ZUp = false;

        [Serialization(Kind = SerializationKind.None)]
        public Dictionary<string, Mesh> ColladaGeometries;

        [Serialization(Kind = SerializationKind.None)]
        public HashSet<string> SkinnedMeshes;

        private ArtToolInfo ImportArtToolInfo(COLLADA collada)
        {
            ZUp = false;
            var toolInfo = new ArtToolInfo();
            toolInfo.FromArtToolName = "Unknown";
            toolInfo.ArtToolMajorRevision = 0;
            toolInfo.ArtToolMinorRevision = 1;
            toolInfo.ArtToolPointerSize = 32;
            toolInfo.Origin = new float[] { 0, 0, 0 };
            toolInfo.SetYUp();

            if (collada.asset != null)
            {
                if (collada.asset.unit != null)
                {
                    if (collada.asset.unit.name == "meter")
                        toolInfo.UnitsPerMeter = (float)collada.asset.unit.meter;
                    else if (collada.asset.unit.name == "centimeter")
                        toolInfo.UnitsPerMeter = (float)collada.asset.unit.meter * 100;
                    else
                        throw new NotImplementedException("Unsupported asset unit type: " + collada.asset.unit.name);
                }

                if (collada.asset.contributor != null && collada.asset.contributor.Length > 0)
                {
                    var contributor = collada.asset.contributor.First();
                    if (contributor.authoring_tool != null)
                        toolInfo.FromArtToolName = contributor.authoring_tool;
                }

                switch (collada.asset.up_axis)
                {
                    case UpAxisType.X_UP:
                        throw new Exception("X-up not supported yet!");

                    case UpAxisType.Y_UP:
                        toolInfo.SetYUp();
                        break;

                    case UpAxisType.Z_UP:
                        ZUp = true;
                        toolInfo.SetZUp();
                        break;
                }
            }

            return toolInfo;
        }

        private ExporterInfo ImportExporterInfo(COLLADA collada)
        {
            var exporterInfo = new ExporterInfo();
            exporterInfo.ExporterName = String.Format("LSLib GR2 Exporter v{0}", Common.LibraryVersion());
            exporterInfo.ExporterMajorRevision = Common.MajorVersion;
            exporterInfo.ExporterMinorRevision = Common.MinorVersion;
            exporterInfo.ExporterBuildNumber = 0;
            exporterInfo.ExporterCustomization = Common.PatchVersion;
            return exporterInfo;
        }

        private void UpdateUserDefinedProperties(Root root)
        {
            var modelType = Options.ModelType;
            if (modelType == DivinityModelType.Undefined)
            {
                modelType = DivinityHelpers.DetermineModelType(root);
            }

            var userDefinedProperties = DivinityHelpers.ModelTypeToUserDefinedProperties(modelType);
            
            if (root.Meshes != null)
            {
                foreach (var mesh in root.Meshes)
                {
                    if (mesh.ExtendedData == null)
                    {
                        mesh.ExtendedData = new DivinityExtendedData();
                    }

                    mesh.ExtendedData.UserDefinedProperties = userDefinedProperties;
                }
            }

            if (root.Skeletons != null)
            {
                foreach (var skeleton in root.Skeletons)
                {
                    if (skeleton.Bones != null)
                    {
                        foreach (var bone in skeleton.Bones)
                        {
                            if (bone.ExtendedData == null)
                            {
                                bone.ExtendedData = new DivinityExtendedData();
                            }

                            bone.ExtendedData.UserDefinedProperties = userDefinedProperties;
                        }
                    }
                }
            }
        }

        private void FindRootBones(node parent, node node, List<node> rootBones)
        {
            if (node.type == NodeType.JOINT)
            {
                if (parent != null)
                {
                    Utils.Warn(String.Format("Joint {0} is not a top level node; parent transformations will be ignored!", node.name != null ? node.name : "(UNNAMED)"));
                }

                rootBones.Add(node);
            }
            else if (node.type == NodeType.NODE)
            {
                if (node.node1 != null)
                {
                    foreach (var child in node.node1)
                    {
                        FindRootBones(node, child, rootBones);
                    }
                }
            }
        }

        private Mesh ImportMesh(geometry geom, mesh mesh, string vertexFormat)
        {
            var collada = new ColladaMesh();
            bool isSkinned = SkinnedMeshes.Contains(geom.id);
            collada.ImportFromCollada(mesh, vertexFormat, isSkinned, Options);

            var m = new Mesh();
            m.VertexFormat = collada.InternalVertexType;
            m.Name = "Unnamed";

            m.PrimaryVertexData = new VertexData();
            var components = new List<GrannyString>();
            components.Add(new GrannyString("Position"));

            var vertexDesc = Vertex.Description(m.VertexFormat);
            if (vertexDesc.BoneWeights)
            {
                components.Add(new GrannyString("BoneWeights"));
                components.Add(new GrannyString("BoneIndices"));
            }

            if (vertexDesc.Normal)
            {
                components.Add(new GrannyString("Normal"));
            }

            if (vertexDesc.Tangent)
            {
                components.Add(new GrannyString("Tangent"));
            }

            if (vertexDesc.Binormal)
            {
                components.Add(new GrannyString("Binormal"));
            }

            for (int i = 0; i < vertexDesc.DiffuseColors; i++)
            {
                components.Add(new GrannyString("DiffuseColor" + i.ToString()));
            }

            for (int i = 0; i < vertexDesc.TextureCoordinates; i++)
            {
                components.Add(new GrannyString("TextureCoordinate" + i.ToString()));
            }

            m.PrimaryVertexData.VertexComponentNames = components;
            m.PrimaryVertexData.Vertices = collada.ConsolidatedVertices;

            m.PrimaryTopology = new TriTopology();
            m.PrimaryTopology.Indices = collada.ConsolidatedIndices;
            m.PrimaryTopology.Groups = new List<TriTopologyGroup>();
            var triGroup = new TriTopologyGroup();
            triGroup.MaterialIndex = 0;
            triGroup.TriFirst = 0;
            triGroup.TriCount = collada.TriangleCount;
            m.PrimaryTopology.Groups.Add(triGroup);

            m.MaterialBindings = new List<MaterialBinding>();
            m.MaterialBindings.Add(new MaterialBinding());

            // m.BoneBindings; - TODO

            m.OriginalToConsolidatedVertexIndexMap = collada.OriginalToConsolidatedVertexIndexMap;
            Utils.Info(String.Format("Imported {0} mesh ({1} tri groups, {2} tris)", (vertexDesc.BoneWeights ? "skinned" : "rigid"), m.PrimaryTopology.Groups.Count, collada.TriangleCount));

            return m;
        }

        private Mesh ImportMesh(Root root, string name, geometry geom, mesh mesh, string vertexFormat)
        {
            var m = ImportMesh(geom, mesh, vertexFormat);
            m.Name = name;
            root.VertexDatas.Add(m.PrimaryVertexData);
            root.TriTopologies.Add(m.PrimaryTopology);
            root.Meshes.Add(m);
            return m;
        }

        private void ImportSkin(Root root, skin skin)
        {
            if (skin.source1[0] != '#')
                throw new ParsingException("Only ID references are supported for skin geometries");

            Mesh mesh = null;
            if (!ColladaGeometries.TryGetValue(skin.source1.Substring(1), out mesh))
                throw new ParsingException("Skin references nonexistent mesh: " + skin.source1);

            if (!Vertex.Description(mesh.VertexFormat).BoneWeights)
            {
                var msg = String.Format("Tried to apply skin to mesh ({0}) with non-skinned vertices ({1})", mesh.Name, mesh.VertexFormat.Name);
                throw new ParsingException(msg);
            }

            var sources = new Dictionary<String, ColladaSource>();
            foreach (var source in skin.source)
            {
                var src = ColladaSource.FromCollada(source);
                sources.Add(src.id, src);
            }

            List<Bone> joints = null;
            List<Matrix4> invBindMatrices = null;
            foreach (var input in skin.joints.input)
            {
                if (input.source[0] != '#')
                    throw new ParsingException("Only ID references are supported for joint input sources");

                ColladaSource inputSource = null;
                if (!sources.TryGetValue(input.source.Substring(1), out inputSource))
                    throw new ParsingException("Joint input source does not exist: " + input.source);

                if (input.semantic == "JOINT")
                {
                    List<string> jointNames = inputSource.NameParams.Values.SingleOrDefault();
                    if (jointNames == null)
                        throw new ParsingException("Joint input source 'JOINT' must contain array of names.");

                    var skeleton = root.Skeletons.Single();
                    joints = new List<Bone>();
                    foreach (var name in jointNames)
                    {
                        Bone bone = null;
                        var lookupName = name.Replace("_x0020_", " ");
                        if (!skeleton.BonesBySID.TryGetValue(lookupName, out bone))
                            throw new ParsingException("Joint name list references nonexistent bone: " + lookupName);

                        joints.Add(bone);
                    }
                }
                else if (input.semantic == "INV_BIND_MATRIX")
                {
                    invBindMatrices = inputSource.MatrixParams.Values.SingleOrDefault();
                    if (invBindMatrices == null)
                        throw new ParsingException("Joint input source 'INV_BIND_MATRIX' must contain a single array of matrices.");
                }
                else
                {
                    throw new ParsingException("Unsupported joint semantic: " + input.semantic);
                }
            }

            if (joints == null)
                throw new ParsingException("Required joint input semantic missing: JOINT");

            if (invBindMatrices == null)
                throw new ParsingException("Required joint input semantic missing: INV_BIND_MATRIX");

            var influenceCounts = ColladaHelpers.StringsToIntegers(skin.vertex_weights.vcount);
            var influences = ColladaHelpers.StringsToIntegers(skin.vertex_weights.v);

            foreach (var count in influenceCounts)
            {
                if (count > 4)
                    throw new ParsingException("GR2 only supports at most 4 vertex influences");
            }

            // TODO
            if (influenceCounts.Count != mesh.OriginalToConsolidatedVertexIndexMap.Count)
                Utils.Warn(String.Format("Vertex influence count ({0}) differs from vertex count ({1})", influenceCounts.Count, mesh.OriginalToConsolidatedVertexIndexMap.Count));

            List<Single> weights = null;

            int jointInputIndex = -1, weightInputIndex = -1;
            foreach (var input in skin.vertex_weights.input)
            {
                if (input.semantic == "JOINT")
                {
                    jointInputIndex = (int)input.offset;
                }
                else if (input.semantic == "WEIGHT")
                {
                    weightInputIndex = (int)input.offset;

                    if (input.source[0] != '#')
                        throw new ParsingException("Only ID references are supported for weight input sources");

                    ColladaSource inputSource = null;
                    if (!sources.TryGetValue(input.source.Substring(1), out inputSource))
                        throw new ParsingException("Weight input source does not exist: " + input.source);

                    if (!inputSource.FloatParams.TryGetValue("WEIGHT", out weights))
                        weights = inputSource.FloatParams.Values.SingleOrDefault();

                    if (weights == null)
                        throw new ParsingException("Weight input source " + input.source + " must have WEIGHT float attribute");
                }
                else
                    throw new ParsingException("Unsupported skin input semantic: " + input.semantic);
            }

            if (jointInputIndex == -1)
                throw new ParsingException("Required vertex weight input semantic missing: JOINT");

            if (weightInputIndex == -1)
                throw new ParsingException("Required vertex weight input semantic missing: WEIGHT");

            // Remove bones that are not actually influenced from the binding list
            var boundBones = new HashSet<Bone>();
            int offset = 0;
            int stride = skin.vertex_weights.input.Length;
            while (offset < influences.Count)
            {
                var jointIndex = influences[offset + jointInputIndex];
                var weightIndex = influences[offset + weightInputIndex];
                var joint = joints[jointIndex];
                var weight = weights[weightIndex];
                if (!boundBones.Contains(joint))
                    boundBones.Add(joint);

                offset += stride;
            }

            if (boundBones.Count > 255)
                throw new ParsingException("GR2 supports at most 255 bound bones per mesh.");

            mesh.BoneBindings = new List<BoneBinding>();
            var boneToIndexMaps = new Dictionary<Bone, int>();
            for (var i = 0; i < joints.Count; i++)
            {
                if (boundBones.Contains(joints[i]))
                {
                    // Collada allows one inverse bind matrix for each skin, however Granny
                    // only has one matrix for one bone, even if said bone is used from multiple meshes.
                    // Hopefully the Collada ones are all equal ...
                    var iwt = invBindMatrices[i];
                    // iwt.Transpose();
                    joints[i].InverseWorldTransform = new float[] {
                        iwt[0, 0], iwt[1, 0], iwt[2, 0], iwt[3, 0],
                        iwt[0, 1], iwt[1, 1], iwt[2, 1], iwt[3, 1],
                        iwt[0, 2], iwt[1, 2], iwt[2, 2], iwt[3, 2],
                        iwt[0, 3], iwt[1, 3], iwt[2, 3], iwt[3, 3]
                    };

                    // Bind all bones that affect vertices to the mesh, so we can reference them
                    // later from the vertexes BoneIndices.
                    var binding = new BoneBinding();
                    binding.BoneName = joints[i].Name;
                    // TODO
                    binding.OBBMin = new float[] { -10, -10, -10 };
                    binding.OBBMax = new float[] { 10, 10, 10 };
                    mesh.BoneBindings.Add(binding);
                    boneToIndexMaps.Add(joints[i], boneToIndexMaps.Count);
                }
            }

            offset = 0;
            for (var vertexIndex = 0; vertexIndex < influenceCounts.Count; vertexIndex++)
            {
                var influenceCount = influenceCounts[vertexIndex];
                for (var i = 0; i < influenceCount; i++)
                {
                    var jointIndex = influences[offset + jointInputIndex];
                    var weightIndex = influences[offset + weightInputIndex];
                    var joint = joints[jointIndex];
                    var weight = weights[weightIndex];
                    foreach (var consolidatedIndex in mesh.OriginalToConsolidatedVertexIndexMap[vertexIndex])
                    {
                        var vertex = mesh.PrimaryVertexData.Vertices[consolidatedIndex];
                        vertex.AddInfluence((byte)boneToIndexMaps[joint], weight);
                    }

                    offset += stride;
                }
            }

            foreach (var vertex in mesh.PrimaryVertexData.Vertices)
            {
                vertex.FinalizeInfluences();
            }

            // Warn if we have vertices that are not influenced by any bone
            int notInfluenced = 0;
            foreach (var vertex in mesh.PrimaryVertexData.Vertices)
            {
                if (vertex.BoneWeights[0] == 0) notInfluenced++;
            }

            if (notInfluenced > 0)
                Utils.Warn(String.Format("{0} vertices are not influenced by any bone", notInfluenced));


            if (skin.bind_shape_matrix != null)
            {
                var bindShapeFloats = skin.bind_shape_matrix.Split(new char[] { ' ' }).Select(s => Single.Parse(s)).ToArray();
                var bindShapeMat = ColladaHelpers.FloatsToMatrix(bindShapeFloats);
                bindShapeMat.Transpose();

                // Deform geometries that were affected by our bind shape matrix
                foreach (var vertex in mesh.PrimaryVertexData.Vertices)
                {
                    vertex.Transform(bindShapeMat);
                }
            }
        }

        public void ImportAnimations(IEnumerable<animation> anims, Root root, Skeleton skeleton)
        {
            var animation = new Animation();
            animation.Name = "Default";
            animation.TimeStep = 0.016667f; // 60 FPS
            animation.Oversampling = 1;
            animation.DefaultLoopCount = 1;
            animation.Flags = 1;

            var trackGroup = new TrackGroup();
            trackGroup.Name = skeleton.Name;
            trackGroup.TransformTracks = new List<TransformTrack>();
            trackGroup.InitialPlacement = new Transform();
            trackGroup.AccumulationFlags = 2;
            trackGroup.LoopTranslation = new float[] { 0, 0, 0 };
            foreach (var colladaTrack in anims)
            {
                ImportAnimation(colladaTrack, trackGroup, skeleton);
            }

            if (trackGroup.TransformTracks.Count > 0)
            {
                // Reorder transform tracks in lexicographic order
                // This is needed by Granny; otherwise it'll fail to find animation tracks
                trackGroup.TransformTracks.Sort((t1, t2) => t1.Name.CompareTo(t2.Name));

                animation.Duration =
                    Math.Max(
                        trackGroup.TransformTracks.Max(t => t.OrientationCurve.CurveData.Duration()),
                        Math.Max(
                            trackGroup.TransformTracks.Max(t => t.PositionCurve.CurveData.Duration()),
                            trackGroup.TransformTracks.Max(t => t.ScaleShearCurve.CurveData.Duration())
                        )
                    );
                animation.TrackGroups = new List<TrackGroup> { trackGroup };

                root.TrackGroups.Add(trackGroup);
                root.Animations.Add(animation);
            }
        }

        public void ImportAnimation(animation anim, TrackGroup trackGroup, Skeleton skeleton)
        {
            var childAnims = 0;
            foreach (var item in anim.Items)
            {
                if (item is animation)
                {
                    ImportAnimation(item as animation, trackGroup, skeleton);
                    childAnims++;
                }
            }

            if (childAnims < anim.Items.Length)
            {
                ColladaAnimation collada = new ColladaAnimation();
                if (collada.ImportFromCollada(anim, skeleton))
                {
                    var track = collada.MakeTrack();
                    trackGroup.TransformTracks.Add(track);
                }
            }
        }

        public Root Import(string inputPath)
        {
            var collada = COLLADA.Load(inputPath);
            var root = new Root();
            root.ArtToolInfo = ImportArtToolInfo(collada);
            root.ExporterInfo = ImportExporterInfo(collada);
            root.FromFileName = inputPath;

            root.Skeletons = new List<Skeleton>();
            root.VertexDatas = new List<VertexData>();
            root.TriTopologies = new List<TriTopology>();
            root.Meshes = new List<Mesh>();
            root.Models = new List<Model>();
            root.TrackGroups = new List<TrackGroup>();
            root.Animations = new List<Animation>();

            ColladaGeometries = new Dictionary<string, Mesh>();
            SkinnedMeshes = new HashSet<string>();

            var collGeometries = new List<geometry>();
            var collSkins = new List<skin>();
            var collAnimations = new List<animation>();
            var rootBones = new List<node>();

            // Import skinning controllers after skeleton and geometry loading has finished, as
            // we reference both of them during skin import
            foreach (var item in collada.Items)
            {
                if (item is library_controllers)
                {
                    var controllers = item as library_controllers;
                    if (controllers.controller != null)
                    {
                        foreach (var controller in controllers.controller)
                        {
                            if (controller.Item is skin)
                            {
                                collSkins.Add(controller.Item as skin);
                                SkinnedMeshes.Add((controller.Item as skin).source1.Substring(1));
                            }
                            else
                            {
                                Utils.Warn(String.Format("Controller {0} is unsupported and will be ignored", controller.Item.GetType().Name));
                            }
                        }
                    }
                }
                else if (item is library_visual_scenes)
                {
                    var scenes = item as library_visual_scenes;
                    if (scenes.visual_scene != null)
                    {
                        foreach (var scene in scenes.visual_scene)
                        {
                            if (scene.node != null)
                            {
                                foreach (var node in scene.node)
                                {
                                    FindRootBones(null, node, rootBones);
                                }
                            }
                        }
                    }
                }
                else if (item is library_geometries)
                {
                    var geometries = item as library_geometries;
                    if (geometries.geometry != null)
                    {
                        foreach (var geometry in geometries.geometry)
                        {
                            if (geometry.Item is mesh)
                            {
                                collGeometries.Add(geometry);
                            }
                            else
                            {
                                Utils.Warn(String.Format("Geometry type {0} is unsupported and will be ignored", geometry.Item.GetType().Name));
                            }
                        }
                    }
                }
                else if (item is library_animations)
                {
                    var animations = item as library_animations;
                    if (animations.animation != null)
                    {
                        collAnimations.AddRange(animations.animation);
                    }
                }
                else
                {
                    Utils.Warn(String.Format("Library {0} is unsupported and will be ignored", item.GetType().Name));
                }
            }

            foreach (var bone in rootBones)
            {
                var skeleton = Skeleton.FromCollada(bone);
                root.Skeletons.Add(skeleton);
            }

            foreach (var geometry in collGeometries)
            {
                string vertexFormat = null;
                // Use the override vertex format, if one was specified
                Options.VertexFormats.TryGetValue(geometry.name, out vertexFormat);
                var mesh = ImportMesh(root, geometry.name, geometry, geometry.Item as mesh, vertexFormat);
                ColladaGeometries.Add(geometry.id, mesh);
            }

            // Import skinning controllers after skeleton and geometry loading has finished, as
            // we reference both of them during skin import
            foreach (var skin in collSkins)
            {
                ImportSkin(root, skin);
            }

            if (collAnimations.Count > 0 && root.Skeletons.Count > 0)
            {
                ImportAnimations(collAnimations, root, root.Skeletons[0]);
            }

            var rootModel = new Model();
            rootModel.Name = "Unnamed"; // TODO
            if (root.Skeletons.Count > 0)
            {
                rootModel.Skeleton = root.Skeletons[0];
                rootModel.Name = rootModel.Skeleton.Bones[0].Name;
            }
            rootModel.InitialPlacement = new Transform();
            rootModel.MeshBindings = new List<MeshBinding>();
            foreach (var mesh in root.Meshes)
            {
                var binding = new MeshBinding();
                binding.Mesh = mesh;
                rootModel.MeshBindings.Add(binding);
            }

            root.Models.Add(rootModel);
            // TODO: make this an option!
            if (root.Skeletons.Count > 0)
                root.Skeletons[0].UpdateInverseWorldTransforms();
            root.ZUp = ZUp;
            root.PostLoad();

            if (Options.WriteUserDefinedProperties)
            {
                this.UpdateUserDefinedProperties(root);
            }

            return root;
        }
    }
}
