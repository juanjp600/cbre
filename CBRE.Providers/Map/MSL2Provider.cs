﻿using CBRE.Common;
using CBRE.DataStructures.Geometric;
using CBRE.DataStructures.MapObjects;
using CBRE.DataStructures.Transformations;
using CBRE.Extensions;
using CBRE.FileSystem;
using CBRE.Providers.Model;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;

namespace CBRE.Providers.Map {
    public class MSL2Provider : MapProvider {
        protected override DataStructures.MapObjects.Map GetFromFile(string filename, IEnumerable<string> textureDirs, IEnumerable<string> modelDirs) {
            using (var strm = new FileStream(filename, FileMode.Open, FileAccess.Read)) {
                return GetFromStream(strm, textureDirs, modelDirs);
            }
        }

        protected override void SaveToFile(string filename, DataStructures.MapObjects.Map map) {
            using (var strm = new FileStream(filename, FileMode.Create, FileAccess.Write)) {
                SaveToStream(strm, map);
            }
        }

        protected override bool IsValidForFileName(string filename) {
            return filename.EndsWith(".msl", StringComparison.OrdinalIgnoreCase);
        }

        class NormalEq : EqualityComparer<CoordinateF> {
            public override bool Equals(CoordinateF x, CoordinateF y) {
                return x.Dot(y) >= 0.999f;
            }

            public override int GetHashCode(CoordinateF obj) {
                return obj.GetHashCode();
            }
        }

        protected void SkipMemblock(BinaryReader br) {
            UInt32 memblockSize = br.ReadUInt32();
            long startPos = br.BaseStream.Position;

            br.BaseStream.Position = startPos + (long)memblockSize;
        }

        protected void ReadMemblockMesh(BinaryReader br, DataStructures.MapObjects.Map map, List<Face> faces=null) {
            UInt32 memblockSize = br.ReadUInt32();
            long startPos = br.BaseStream.Position;
            Console.WriteLine("memblockSize: " + memblockSize);

            UInt32 dwFVF = br.ReadUInt32();
            UInt32 dwFVFSize = br.ReadUInt32();
            UInt32 dwVertMax = br.ReadUInt32();
            Console.WriteLine($"dwFVF: {dwFVF}; dwFVFSize: {dwFVFSize}; dwVertMax: {dwVertMax}");

            List<CoordinateF> vertexPositions = new List<CoordinateF>();
            List<CoordinateF> vertexNormals = new List<CoordinateF>();
            for (int i=0;i<dwVertMax;i++) {
                float x = br.ReadSingle();
                float z = br.ReadSingle();
                float y = br.ReadSingle();
                vertexPositions.Add(new CoordinateF(x, y, z));
                float nx = br.ReadSingle();
                float nz = br.ReadSingle();
                float ny = br.ReadSingle();
                vertexNormals.Add(new CoordinateF(nx, ny, nz).Normalise());
                for (int j=24;j<dwFVFSize;j+=4) {
                    Console.WriteLine("skipped vertex property " + j + ": " + br.ReadSingle());
                }
            }

            if (faces != null) {
                foreach (var normal in vertexNormals.Distinct(new NormalEq())) {
                    if (normal.LengthSquared() < 0.01f) { continue; }
                    Face newFace = new Face(map.IDGenerator.GetNextFaceID());
                    for (int i = 0; i < vertexPositions.Count; i++) {
                        if (vertexNormals[i].Dot(normal) < 0.999f) { continue; }
                        if (newFace.Vertices.Any(v => (new CoordinateF(v.Location)-vertexPositions[i]).LengthSquared()<0.001f)) { continue; }
                        newFace.Vertices.Add(new Vertex(new Coordinate(vertexPositions[i]), newFace));
                    }

                    newFace.Plane = new Plane(new Coordinate(normal), newFace.Vertices[0].Location);

                    Vertex center = newFace.Vertices[0];
                    newFace.Vertices.RemoveAt(0);

                    newFace.Vertices.Sort((v1, v2) => {
                        float dot = normal.Dot(new CoordinateF((v1.Location - center.Location).Cross(v2.Location - center.Location)));
                        if (dot < 0.0f) { return -1; }
                        else if (dot > 0.0f) { return 1; }
                        return 0;
                    });

                    newFace.Vertices.Insert(0, center);

                    newFace.UpdateBoundingBox();

                    faces.Add(newFace);
                }
            }

            br.BaseStream.Position = startPos + (long)memblockSize;
        }

        protected List<ModelReference> LoadAllModels(IEnumerable<string> modelDirs) {
            List<ModelReference> models = new List<ModelReference>();
            foreach (string dir in modelDirs) {
                string[] files = Directory.GetFiles(dir);
                foreach (string modelPath in files) {
                    NativeFile file = null;
                    if (!string.IsNullOrEmpty(modelPath)) {
                        file = new NativeFile(modelPath);
                    }
                    if (file == null || !ModelProvider.CanLoad(file)) {
                        continue;
                    }

#if !DEBUG
                try
                {
#endif
                    var mr = ModelProvider.CreateModelReference(file);
                    models.Add(mr);
#if !DEBUG
                }
                catch (Exception)
                {
                    // Couldn't load
                    continue;
                }
#endif
                }
            }
            return models;
        }

        protected struct SubmeshTextureInfo {
            public string TextureName;
            public float ScaleU;
            public float ScaleV;
            public float ShiftU;
            public float ShiftV;
            public float Rotation;
        }

        protected class Pair<T1, T2> {
            public T1 Item1 { get; set; }
            public T2 Item2 { get; set; }

            public Pair(T1 item1, T2 item2) {
                Item1 = item1;
                Item2 = item2;
            }
        }

        protected override DataStructures.MapObjects.Map GetFromStream(Stream stream, IEnumerable<string> textureDirs, IEnumerable<string> modelDirs) {
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("");
            var map = new DataStructures.MapObjects.Map();
            map.CordonBounds = new Box(Coordinate.One * -16384, Coordinate.One * 16384);
            BinaryReader br = new BinaryReader(stream);

            List<ModelReference> models = null;

            //header
            bool hasLightmap = Math.Abs(br.ReadSingle()) > 0.01f;
            Console.WriteLine("hasLightmap: " + hasLightmap);
            if (hasLightmap) {
                UInt32 lightmapSize = br.ReadUInt32();
                stream.Position += lightmapSize;
                Console.WriteLine("skipped lightmap: " + stream.Position + " " + lightmapSize);
            }
            int entityCount = (int)br.ReadSingle() - 2;
            Console.WriteLine("entityCount: " + entityCount);
            for (int i=0;i<entityCount;i++) {
                int meshCount = (int)br.ReadSingle();
                Console.WriteLine("**** meshCount: " + meshCount);
                List<long> memblockLocations = new List<long>();
                for (int j=0;j<meshCount;j++) {
                    float unknown1 = br.ReadSingle();
                    Console.WriteLine("unknown1: " + unknown1);
                    memblockLocations.Add(stream.Position);
                    SkipMemblock(br);
                }

                bool isBrush = Math.Abs(br.ReadSingle()) > 0.01f;
                Console.WriteLine("isBrush: " + isBrush + " " + stream.Position.ToString("X2"));
                if (isBrush) {
                    Dictionary<int, List<Face>> faces = new Dictionary<int, List<Face>>();
                    long returnPosition = stream.Position;
                    for (int j = 0; j < meshCount; j++) {
                        stream.Position = memblockLocations[j];
                        faces.Add(j, new List<Face>());
                        ReadMemblockMesh(br, map, faces[j]);
                    }
                    stream.Position = returnPosition;
                    SkipMemblock(br);
                    for (int j = 0; j < 2; j++) {
                        float skip = br.ReadSingle();
                        Console.WriteLine("skip " + j + ": " + skip);
                    }

                    float xTranslate = br.ReadSingle();
                    float zTranslate = br.ReadSingle();
                    float yTranslate = br.ReadSingle();

                    float xScale = br.ReadSingle();
                    float zScale = br.ReadSingle();
                    float yScale = br.ReadSingle();

                    for (int j = 8; j < 25; j++) {
                        float skip = br.ReadSingle();
                        Console.WriteLine("skip " + j + ": " + skip);
                    }
                    List<SubmeshTextureInfo> textures = new List<SubmeshTextureInfo>();
                    for (int j = 0; j < meshCount; j++) {
                        SubmeshTextureInfo submeshTextureInfo = new SubmeshTextureInfo();

                        submeshTextureInfo.TextureName = System.IO.Path.GetFileNameWithoutExtension(br.ReadLine());
                        Console.WriteLine("textureName: " + submeshTextureInfo.TextureName);
                        float flags = br.ReadSingle();
                        bool faceIsHidden = Math.Abs(flags - 1) < 0.01f;
                        bool faceIsLit = Math.Abs(flags - 800) < 0.01f;
                        if (faceIsLit) {
                            br.ReadSingle();
                        }
                        for (int k = 0; k < 4; k++) {
                            float skip2 = br.ReadSingle();
                            Console.WriteLine("skip2 " + k + ": " + skip2);
                        }

                        submeshTextureInfo.ScaleU = br.ReadSingle();
                        submeshTextureInfo.ScaleV = br.ReadSingle();
                        submeshTextureInfo.ShiftU = br.ReadSingle();
                        submeshTextureInfo.ShiftV = br.ReadSingle();
                        submeshTextureInfo.Rotation = br.ReadSingle();

                        if (faceIsHidden) {
                            submeshTextureInfo.TextureName = "tooltextures/remove_face";
                        }
                        textures.Add(submeshTextureInfo);
                    }

                    if (faces.Any()) {
                        Solid newSolid = new Solid(map.IDGenerator.GetNextObjectID());
                        foreach (int key in faces.Keys) {
                            foreach (Face face in faces[key]) {
                                face.Parent = newSolid;
                                newSolid.Faces.Add(face);
                            }
                        }
                        newSolid.Colour = Colour.GetRandomBrushColour();
                        newSolid.UpdateBoundingBox();

                        MapObject parent = map.WorldSpawn;

                        newSolid.SetParent(parent);

                        newSolid.Transform(new UnitScale(Coordinate.One, newSolid.BoundingBox.Center), TransformFlags.None);
                        newSolid.Transform(new UnitScale(new Coordinate(
                            (decimal)xScale / newSolid.BoundingBox.Width,
                            (decimal)yScale / newSolid.BoundingBox.Length,
                            (decimal)zScale / newSolid.BoundingBox.Height), Coordinate.Zero), TransformFlags.None);
                        newSolid.Transform(new UnitTranslate(new Coordinate(
                            (decimal)xTranslate,
                            (decimal)yTranslate,
                            (decimal)zTranslate)), TransformFlags.None);

                        foreach (int key in faces.Keys) {
                            foreach (Face face in faces[key]) {
                                face.Texture.Name = textures[key].TextureName;
                                face.AlignTextureToWorld();
                                face.Texture.XScale = (decimal)textures[key].ScaleU * 0.25m;
                                face.Texture.YScale = (decimal)textures[key].ScaleV * 0.25m;
                                face.Texture.XShift = (decimal)textures[key].ShiftU;
                                face.Texture.YShift = (decimal)textures[key].ShiftV;
                                face.SetTextureRotation((decimal)textures[key].Rotation);
                            }
                        }
                    }
                } else {
                    int entitySubType = (int)br.ReadSingle();
                    Console.WriteLine("entitySubType: " + entitySubType);
                    for (int j = 1; j < 2; j++) {
                        float skip = br.ReadSingle();
                        Console.WriteLine("skip " + j + ": " + skip);
                    }
                    float xTranslate = br.ReadSingle();
                    float zTranslate = br.ReadSingle();
                    float yTranslate = br.ReadSingle();
                    float xScale = br.ReadSingle();
                    float zScale = br.ReadSingle();
                    float yScale = br.ReadSingle();
                    if (Math.Abs(entitySubType-3.0f) < 0.01f) {
                        Console.WriteLine("regular point entity");
                        for (int j = 8; j < 35; j++) {
                            float skip = br.ReadSingle();
                            Console.WriteLine("skip " + j + ": " + skip);
                        }
                        string entityName = br.ReadLine();
                        string entityIcon = br.ReadLine();
                        int propertyCount = (int)br.ReadSingle() + 1;
                        Dictionary<string, string> properties = new Dictionary<string, string>();
                        for (int j = 0; j < propertyCount; j++) {
                            string propertyName = br.ReadLine().ToLowerInvariant();
                            string propertyValue = br.ReadLine();
                            Console.WriteLine(propertyName + ": " + propertyValue);
                            properties.Add(propertyName, propertyValue);
                        }

                        Entity entity = new Entity(map.IDGenerator.GetNextObjectID());
                        Property newProperty = null;
                        switch (entityName.ToLowerInvariant()) {
                            case "pointlight":
                                entity.ClassName = "light";
                                entity.EntityData.Name = "light";
                                entity.Colour = Colour.GetDefaultEntityColour();

                                newProperty = new Property();
                                newProperty.Key = "range";
                                newProperty.Value = properties["range"];
                                entity.EntityData.Properties.Add(newProperty);

                                newProperty = new Property();
                                newProperty.Key = "color";
                                newProperty.Value = properties["color"].Replace(',', ' ').Trim();
                                entity.EntityData.Properties.Add(newProperty);
                                break;
                            case "spotlight":
                                entity.ClassName = "spotlight";
                                entity.EntityData.Name = "spotlight";
                                entity.Colour = Colour.GetDefaultEntityColour();

                                newProperty = new Property();
                                newProperty.Key = "range";
                                newProperty.Value = properties["range"];
                                entity.EntityData.Properties.Add(newProperty);

                                newProperty = new Property();
                                newProperty.Key = "color";
                                newProperty.Value = properties["color"].Replace(',', ' ').Trim();
                                entity.EntityData.Properties.Add(newProperty);

                                newProperty = new Property();
                                newProperty.Key = "innerconeangle";
                                newProperty.Value = "45";
                                if (decimal.TryParse(properties["innerang"], out decimal innerAngle)) {
                                    newProperty.Value = (innerAngle * 0.5m).ToString();
                                }
                                entity.EntityData.Properties.Add(newProperty);

                                newProperty = new Property();
                                newProperty.Key = "outerconeangle";
                                newProperty.Value = "90";
                                if (decimal.TryParse(properties["outerang"], out decimal outerAngle)) {
                                    newProperty.Value = (outerAngle * 0.5m).ToString();
                                }
                                entity.EntityData.Properties.Add(newProperty);

                                newProperty = new Property();
                                newProperty.Key = "angles";
                                newProperty.Value = "0 0 0";
                                string[] dirParts = properties["direction"].Split(',');
                                if (decimal.TryParse(dirParts[0], out decimal dirX) &&
                                    decimal.TryParse(dirParts[1], out decimal dirY) &&
                                    decimal.TryParse(dirParts[2], out decimal dirZ)) {
                                    Coordinate dir = new Coordinate(dirX, dirY, dirZ).Normalise();
                                    decimal pitch = DMath.RadiansToDegrees(DMath.Asin(-dir.Y));
                                    dir.Y = 0;
                                    decimal yaw = 0m;
                                    if (dir.LengthSquared() > 0.01m) {
                                        dir = dir.Normalise();
                                        yaw = DMath.RadiansToDegrees(DMath.Atan2(-dir.X, dir.Z));
                                    }

                                    newProperty.Value = $"{pitch} {yaw} 0";
                                }
                                entity.EntityData.Properties.Add(newProperty);
                                break;
                            default:
                                entity.ClassName = entityName;
                                entity.EntityData.Name = entityName;
                                entity.Colour = Colour.GetDefaultEntityColour();

                                foreach (var key in properties.Keys) {
                                    newProperty = new Property();
                                    newProperty.Key = key;
                                    newProperty.Value = properties[key];
                                    entity.EntityData.Properties.Add(newProperty);
                                }
                                break;
                        }

                        entity.Origin = new Coordinate((decimal)xTranslate, (decimal)yTranslate, (decimal)zTranslate);
                        entity.SetParent(map.WorldSpawn);
                    } else if (Math.Abs(entitySubType-2.0f)<0.01f) {
                        Console.WriteLine("model");
                        if (models == null) {
                            models = LoadAllModels(modelDirs);
                        }
                        ModelReference model = null;
                        Coordinate angles = null;
                        Coordinate scale = null;
                        long returnPosition = stream.Position;
                        for (int j = 0; j < meshCount; j++) {
                            stream.Position = memblockLocations[j];

                            UInt32 memblockSize = br.ReadUInt32();
                            UInt32 dwFVF = br.ReadUInt32();
                            UInt32 dwFVFSize = br.ReadUInt32();
                            UInt32 dwVertMax = br.ReadUInt32();

                            for (int k=0;k<models.Count;k++) {
                                DataStructures.Models.Mesh currMesh = models[k].Model.BodyParts[0].Meshes.Values.First()[0];

                                Console.WriteLine("vertexCount " + j + ": " + dwVertMax + " " + currMesh.Vertices.Count + " " + System.IO.Path.GetFileNameWithoutExtension(models[k].Path));
                                if (dwVertMax == currMesh.Vertices.Count) {
                                    List<Pair<Coordinate, Coordinate>> points = new List<Pair<Coordinate, Coordinate>>();
                                    List<Coordinate> loadedPoints = new List<Coordinate>();
                                    Coordinate loadedCenter = new Coordinate(0, 0, 0);
                                    Coordinate knownCenter = new Coordinate(0, 0, 0);
                                    Coordinate knownBounds = new Coordinate(0, 0, 0);
                                    for (int l = 0; l < dwVertMax; l++) {
                                        float x = br.ReadSingle();
                                        float z = br.ReadSingle();
                                        float y = br.ReadSingle();
                                        Coordinate point = new Coordinate((decimal)x, (decimal)y, (decimal)z);
                                        loadedPoints.Add(point);
                                        loadedCenter += point;
                                        knownCenter += new Coordinate(currMesh.Vertices[l].Location);
                                        knownBounds.X = Math.Max((decimal)currMesh.Vertices[l].Location.X, knownBounds.X);
                                        knownBounds.Y = Math.Max((decimal)currMesh.Vertices[l].Location.Y, knownBounds.Y);
                                        knownBounds.Z = Math.Max((decimal)currMesh.Vertices[l].Location.Z, knownBounds.Z);
                                        for (int m = 12; m < dwFVFSize; m += 4) {
                                            stream.Position += 4;
                                        }

                                        if (points.Count < 3) {
                                            int nativeIndex = (l / 3) * 3 + ((l % 3) + 1) % 3;
                                            Coordinate vertexLoc = new Coordinate(currMesh.Vertices[nativeIndex].Location);
                                            if (!points.Any(p => Math.Abs(p.Item1.Normalise().Dot(vertexLoc.Normalise())) > 0.95m)) {
                                                points.Add(new Pair<Coordinate, Coordinate>(vertexLoc, point));
                                            }
                                        }
                                    }

                                    loadedCenter /= dwVertMax;
                                    knownCenter /= dwVertMax;

                                    knownBounds -= knownCenter;

                                    if (points.Count >= 3) {
                                        model = models[k];

                                        for (int l = 0; l < 3; l++) {
                                            points[l].Item1 -= knownCenter; points[l].Item1 = points[l].Item1.Normalise();
                                            points[l].Item2 -= loadedCenter; points[l].Item2 = points[l].Item2.Normalise();
                                        }

                                        points[2].Item1 = points[0].Item1.Cross(points[1].Item1).Normalise();
                                        points[2].Item2 = points[0].Item2.Cross(points[1].Item2).Normalise();

                                        points[1].Item1 = points[0].Item1.Cross(points[2].Item1).Normalise();
                                        points[1].Item2 = points[0].Item2.Cross(points[2].Item2).Normalise();

                                        decimal dotX0 = Coordinate.UnitX.Dot(points[0].Item1);
                                        decimal dotX1 = Coordinate.UnitX.Dot(points[1].Item1);
                                        decimal dotX2 = Coordinate.UnitX.Dot(points[2].Item1);

                                        decimal dotY0 = Coordinate.UnitY.Dot(points[0].Item1);
                                        decimal dotY1 = Coordinate.UnitY.Dot(points[1].Item1);
                                        decimal dotY2 = Coordinate.UnitY.Dot(points[2].Item1);

                                        decimal dotZ0 = Coordinate.UnitZ.Dot(points[0].Item1);
                                        decimal dotZ1 = Coordinate.UnitZ.Dot(points[1].Item1);
                                        decimal dotZ2 = Coordinate.UnitZ.Dot(points[2].Item1);

                                        Coordinate newX = (dotX0 * points[0].Item2 + dotX1 * points[1].Item2 + dotX2 * points[2].Item2);
                                        Coordinate newY = (dotY0 * points[0].Item2 + dotY1 * points[1].Item2 + dotY2 * points[2].Item2);
                                        Coordinate newZ = (dotZ0 * points[0].Item2 + dotZ1 * points[1].Item2 + dotZ2 * points[2].Item2);

                                        Coordinate unTransformedBounds = new Coordinate(
                                            loadedPoints.Select(p => p.X).Max() - loadedPoints.Select(p => p.X).Min(),
                                            loadedPoints.Select(p => p.Y).Max() - loadedPoints.Select(p => p.Y).Min(),
                                            loadedPoints.Select(p => p.Z).Max() - loadedPoints.Select(p => p.Z).Min());

                                        Coordinate propScale(Coordinate p) {
                                            Coordinate retVal = p.Clone();
                                            retVal.X *= (decimal)xScale / unTransformedBounds.X;
                                            retVal.Y *= (decimal)yScale / unTransformedBounds.Y;
                                            retVal.Z *= (decimal)zScale / unTransformedBounds.Z;
                                            return retVal;
                                        }

                                        Coordinate fuck = propScale(new Coordinate(1, 1, 1));
                                        Coordinate fuck2 = new Coordinate((decimal)xScale, (decimal)yScale, (decimal)zScale);

                                        Coordinate newBounds = new Coordinate(
                                            loadedPoints.Select(p => propScale(p).Dot(newX)).Max() - loadedPoints.Select(p => propScale(p).Dot(newX)).Min(),
                                            loadedPoints.Select(p => propScale(p).Dot(newY)).Max() - loadedPoints.Select(p => propScale(p).Dot(newY)).Min(),
                                            loadedPoints.Select(p => propScale(p).Dot(newZ)).Max() - loadedPoints.Select(p => propScale(p).Dot(newZ)).Min());

                                        Coordinate newBounds2 = new Coordinate(
                                            loadedPoints.Select(p => p.Dot(newX)).Max() - loadedPoints.Select(p => p.Dot(newX)).Min(),
                                            loadedPoints.Select(p => p.Dot(newY)).Max() - loadedPoints.Select(p => p.Dot(newY)).Min(),
                                            loadedPoints.Select(p => p.Dot(newZ)).Max() - loadedPoints.Select(p => p.Dot(newZ)).Min());

                                        scale = new Coordinate(newBounds.X / newBounds2.X, newBounds.Z / newBounds2.Z, newBounds.Y / newBounds2.Y);
                                        /*scale.X /= knownBounds.X;
                                        scale.Y /= knownBounds.Y;
                                        scale.Z /= knownBounds.Z;*/

                                        angles = Entity.ToEuler(newX, newY, newZ);

                                        break;
                                    }
                                }
                            }
                        }
                        stream.Position = returnPosition;
                        for (int j = 8; j < 24; j++) {
                            float skip = br.ReadSingle();
                            Console.WriteLine("skip " + j + ": " + skip);
                        }
                        int materialCount = (int)br.ReadSingle() + 1;
                        for (int j=0;j<materialCount;j++) {
                            string materialName = br.ReadLine();
                            Console.WriteLine("material " + j + ": " + materialName);
                            for (int k = 0; k < 10; k++) {
                                float skip = br.ReadSingle();
                                Console.WriteLine("skip2 " + k + ": " + skip);
                            }
                        }

                        Entity entity = new Entity(map.IDGenerator.GetNextObjectID());
                        entity.ClassName = "model";
                        entity.EntityData.Name = "model";

                        Property newProperty;

                        if (model != null) {
                            newProperty = new Property();
                            newProperty.Key = "file";
                            newProperty.Value = System.IO.Path.GetFileNameWithoutExtension(model.Path);
                            entity.EntityData.Properties.Add(newProperty);

                            if (angles != null) {
                                newProperty = new Property();
                                newProperty.Key = "angles";
                                newProperty.Value = angles.ToDataString();
                                entity.EntityData.Properties.Add(newProperty);
                            }

                            if (scale != null) {
                                newProperty = new Property();
                                newProperty.Key = "scale";
                                newProperty.Value = scale.ToDataString();
                                entity.EntityData.Properties.Add(newProperty);
                            }
                        }

                        entity.Origin = new Coordinate((decimal)xTranslate, (decimal)yTranslate, (decimal)zTranslate);
                        entity.SetParent(map.WorldSpawn);
                    }
                }
            }

            if (models != null) {
                models.ForEach(m => ModelProvider.DeleteModelReference(m));
            }

            return map;
        }

        protected override void SaveToStream(Stream stream, DataStructures.MapObjects.Map map) {
            throw new NotImplementedException("don't save to msl, ew");
        }

        protected override IEnumerable<MapFeature> GetFormatFeatures() {
            return new[]
            {
                MapFeature.Worldspawn,
                MapFeature.Solids,
                MapFeature.Entities
            };
        }
    }
}