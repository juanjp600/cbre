﻿using Sledge.DataStructures.Models;
using Sledge.DataStructures.Geometric;
using Sledge.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;

namespace Sledge.Providers.Model
{
    public class B3DProvider : ModelProvider
    {
        protected override bool IsValidForFile(IFile file)
        {
            return file.Extension.ToLowerInvariant() == "b3d";
        }

        protected static string ReadChunk(BinaryReader reader, DataStructures.Models.Model model, CoordinateF relative = null)
        {
            if (relative == null) relative = CoordinateF.Zero;
            string header = reader.ReadFixedLengthString(Encoding.ASCII,4);
            int size = reader.ReadInt32();
            int initialPos = (int)reader.BaseStream.Position;

            if (header == "NODE")
            {
                string name = reader.ReadNullTerminatedString();
                float posX = -reader.ReadSingle(); float posZ = reader.ReadSingle(); float posY = reader.ReadSingle();
                float scaleX = reader.ReadSingle(); float scaleZ = reader.ReadSingle(); float scaleY = reader.ReadSingle();
                float rotX = reader.ReadSingle(); float rotY = reader.ReadSingle(); float rotZ = reader.ReadSingle(); float rotW = reader.ReadSingle();

                ReadChunk(reader, model, relative+new CoordinateF(posX,posY,posZ));

                reader.ReadBytes(size - ((int)reader.BaseStream.Position - initialPos));
            }
            else if (header == "MESH")
            {
                int brushID = reader.ReadInt32();

                string vertsHeader = reader.ReadFixedLengthString(Encoding.ASCII, 4);
                int vertsSize = reader.ReadInt32();

                int initialVertPos = (int)reader.BaseStream.Position;

                int vertFlags = reader.ReadInt32();
                int tex_coord_sets = reader.ReadInt32();
                int tex_coord_set_size = reader.ReadInt32();

                Mesh mesh = new Mesh(0);
                List<MeshVertex> vertices = new List<MeshVertex>();

                while (reader.BaseStream.Position - initialVertPos < vertsSize)
                {
                    float x = -reader.ReadSingle()+relative.X; float z = reader.ReadSingle() + relative.Z; float y = reader.ReadSingle() + relative.Y;
                    float normalX = 0.0f; float normalY = 1.0f; float normalZ = 0.0f;
                    if ((vertFlags&1) != 0)
                    {
                        normalX = reader.ReadSingle(); normalZ = reader.ReadSingle(); normalY = reader.ReadSingle();
                    }
                    float r; float g; float b; float a;
                    if ((vertFlags&2) != 0)
                    {
                        r = reader.ReadSingle(); g = reader.ReadSingle(); b = reader.ReadSingle(); a = reader.ReadSingle();
                    }

                    float u = 0.0f; float v = 0.0f;
                    if (tex_coord_sets>0)
                    {
                        u = reader.ReadSingle(); v = reader.ReadSingle();
                        for (int j = 0;j < tex_coord_set_size-2;j++)
                        {
                            reader.ReadSingle();
                        }
                        for (int i = 0; i < tex_coord_sets - 1; i++)
                        {
                            for (int j = 0; j < tex_coord_set_size; j++)
                            {
                                reader.ReadSingle();
                            }
                        }
                    }

                    vertices.Add(new MeshVertex(new CoordinateF(x, y, z), new CoordinateF(normalX, normalY, normalZ), model.Bones[0], u, v));
                }

                while (reader.BaseStream.Position - initialPos < size)
                {
                    string trisHeader = reader.ReadFixedLengthString(Encoding.ASCII, 4);
                    int trisSize = reader.ReadInt32();

                    int initialTriPos = (int)reader.BaseStream.Position;

                    int brushID2 = reader.ReadInt32(); //wtf???

                    int[] triInds = new int[3];
                    int indNum = 0;
                    while (reader.BaseStream.Position - initialTriPos < trisSize)
                    {
                        triInds[indNum] = reader.ReadInt32(); indNum = (indNum+1)%3;
                        if (indNum==0)
                        {
                            mesh.Vertices.Add(new MeshVertex(vertices[triInds[0]].Location, vertices[triInds[0]].Normal, vertices[triInds[0]].BoneWeightings, vertices[triInds[0]].TextureU, vertices[triInds[0]].TextureV));
                            mesh.Vertices.Add(new MeshVertex(vertices[triInds[2]].Location, vertices[triInds[2]].Normal, vertices[triInds[2]].BoneWeightings, vertices[triInds[2]].TextureU, vertices[triInds[2]].TextureV));
                            mesh.Vertices.Add(new MeshVertex(vertices[triInds[1]].Location, vertices[triInds[1]].Normal, vertices[triInds[1]].BoneWeightings, vertices[triInds[1]].TextureU, vertices[triInds[1]].TextureV));
                        }
                        
                    }
                }

                model.AddMesh("mesh", 0, mesh);
            }
            else
            {
                reader.ReadBytes(size);
            }

            return header;
        }

        protected override DataStructures.Models.Model LoadFromFile(IFile file)
        {
            DataStructures.Models.Model model = new DataStructures.Models.Model();
            Bone bone = new Bone(0, -1, null, "rootBone", CoordinateF.Zero, CoordinateF.Zero, CoordinateF.One, CoordinateF.One);
            model.Bones.Add(bone);

            FileStream stream = new FileStream(file.FullPathName, FileMode.Open);
            BinaryReader reader = new BinaryReader(stream);

            string header = reader.ReadFixedLengthString(Encoding.ASCII, 4);
            if (header != "BB3D")
            {
                reader.Dispose();
                stream.Dispose();
                return null;
            }

            int fileLength = reader.ReadInt32();

            int version = reader.ReadInt32();

            for (int i=0;i<3;i++)
            {
                if (ReadChunk(reader, model) == "NODE") break;
            }

            reader.Dispose();
            stream.Dispose();


            Bitmap bmp = new Bitmap(64, 64);
            for (int i=0;i<64;i++)
            {
                for (int j=0;j<64;j++)
                {
                    bmp.SetPixel(i, j, Color.DarkGray);
                }
            }
            var tex = new DataStructures.Models.Texture
            {
                Name = "blank",
                Index = 0,
                Width = 64,
                Height = 64,
                Flags = 0,
                Image = bmp
            };
            model.Textures.Add(tex);

            return model;
        }
    }
}