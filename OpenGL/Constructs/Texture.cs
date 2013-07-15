﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenGL
{
    public enum TextureParam : int
    {
        Nearest = 9728,
        Linear = 9729,
        LinearMipmapLinear = 9987,
        LinearMipmapNearest = 9985,
        NearestMipmapLinear = 9986,
        NearestMipmapNearest = 9984
    }

    public class Texture : IDisposable
    {
        #region Propreties
        public string Filename { get; private set; }

        public uint TextureID { get; private set; }

        public Size Size { get; private set; }

        public TextureTarget TextureTarget { get; private set; }
        #endregion

        #region Constructors
        /// <summary>
        /// Create a texture from the supplied filename.
        /// Any files that Bitmap.FromFile can open are supported.
        /// This method also supports dds textures (as long as the file extension is .dds).
        /// </summary>
        /// <param name="Filename">The path to the texture to load.</param>
        public Texture(string Filename)
        {
            if (!File.Exists(Filename))
            {
                throw new FileNotFoundException(string.Format("The file {0} does not exist.", Filename));
            }

            this.Filename = Filename;
            switch (new FileInfo(Filename).Extension.ToLower())
            {
                case ".dds": LoadDDS(Filename);
                    break;
                default: LoadBitmap((Bitmap)Bitmap.FromFile(Filename));//Filename);
                    break;
            }

            Gl.BindTexture(TextureTarget, 0);
        }

        /// <summary>
        /// Create a texture from a supplie bitmap.
        /// </summary>
        /// <param name="BitmapImage">The already decoded bitmap image.</param>
        /// <param name="FlipY">True if the bitmap should be flipped.</param>
        public Texture(Bitmap BitmapImage, bool FlipY = true)
        {
            this.Filename = BitmapImage.GetHashCode().ToString();
            LoadBitmap(BitmapImage, FlipY);
            Gl.BindTexture(TextureTarget, 0);
        }
        
        ~Texture()
        {
            if (TextureID != 0) System.Diagnostics.Debug.Fail(string.Format("Texture {0} was not disposed of properly.", Filename));
        }
        #endregion

        #region Methods
        public void Dispose()
        {
            if (TextureID != 0)
            {
                Gl.DeleteTextures(1, new uint[] { TextureID });
                TextureID = 0;
            }
        }

        private void LoadBitmap(Bitmap BitmapImage, bool FlipY = true)
        {
            /* .net library has methods for converting many image formats so I exploit that by using 
             * .net to convert any filetype to a bitmap.  Then the bitmap is locked into memory so
             * that the garbage collector doesn't touch it, and it is read via OpenGL glTexImage2D. */
            if (FlipY) BitmapImage.RotateFlip(RotateFlipType.RotateNoneFlipY);     // bitmaps read from bottom up, so flip it
            this.Size = BitmapImage.Size;

            // must be Format32bppArgb file format, so convert it if it isn't in that format
            BitmapData bitmapData = BitmapImage.LockBits(new Rectangle(0, 0, BitmapImage.Width, BitmapImage.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            // set the texture target and then generate the texture ID
            this.TextureTarget = (BitmapImage.Height == 1 || BitmapImage.Width == 1) ? TextureTarget.Texture1D : TextureTarget.Texture2D;
            this.TextureID = Gl.GenTexture();

            Gl.PixelStorei(PixelStoreParameter.UnpackAlignment, 1); // set pixel alignment
            Gl.BindTexture(TextureTarget, TextureID);     // bind the texture to memory in OpenGL

            Gl.TexParameteri(TextureTarget, TextureParameterName.GenerateMipmap, 0);
            Gl.TexImage2D(TextureTarget, 0, PixelInternalFormat.Four, BitmapImage.Width, BitmapImage.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bitmapData.Scan0);
            Gl.TexParameteri(TextureTarget, TextureParameterName.TextureMagFilter, (int)TextureParam.Nearest);
            Gl.TexParameteri(TextureTarget, TextureParameterName.TextureMinFilter, (int)TextureParam.Nearest);//(int)TextureParam.Linear);   // linear filter
            BitmapImage.UnlockBits(bitmapData);
            BitmapImage.Dispose();
        }

        /// <summary>
        /// Loads a compressed DDS file into an OpenGL texture.
        /// </summary>
        /// <param name="ResourceFile">The path to the DDS file.</param>
        private void LoadDDS(string ResourceFile)
        {
            using (BinaryReader stream = new BinaryReader(new FileStream(ResourceFile, FileMode.Open)))
            {
                string filecode = new string(stream.ReadChars(4));
                if (filecode != "DDS ")                                 // first 4 chars should be "DDS "
                    throw new Exception("File was not a DDS file format.");

                DDS.DDSURFACEDESC2 imageData = new DDS.DDSURFACEDESC2(stream);  // read the DirectDraw surface descriptor
                this.Size = new Size((int)imageData.Width, (int)imageData.Height);

                if (imageData.LinearSize == 0)
                    throw new Exception("The linear scan line size was zero.");

                bool compressed = true;
                int factor = 0, buffersize = 0, blocksize = 0;
                PixelInternalFormat format;
                switch (imageData.PixelFormat.FourCC)       // check the compression type
                {
                    case "DXT1":    // DXT1 compression ratio is 8:1
                        format = PixelInternalFormat.CompressedRgbaS3tcDxt1Ext;
                        factor = 2;
                        blocksize = 8;
                        break;
                    case "DXT3":    // DXT3 compression ratio is 4:1
                        format = PixelInternalFormat.CompressedRgbaS3tcDxt3Ext;
                        factor = 4;
                        blocksize = 16;
                        break;
                    case "DXT5":    // DXT5 compression ratio is 4:1
                        format = PixelInternalFormat.CompressedRgbaS3tcDxt5Ext;
                        factor = 4;
                        blocksize = 16;
                        break;
                    default:
                        compressed = false;
                        if (imageData.PixelFormat.ABitMask == 0xf000 && imageData.PixelFormat.RBitMask == 0x0f00 &&
                            imageData.PixelFormat.GBitMask == 0x00f0 && imageData.PixelFormat.BBitMask == 0x000f &&
                            imageData.PixelFormat.RGBBitCount == 16) format = PixelInternalFormat.Rgba;
                        throw new Exception(string.Format("File compression \"{0}\" is not supported.", imageData.PixelFormat.FourCC));
                }

                if (imageData.LinearSize != 0) buffersize = (int)((imageData.MipmapCount > 1) ? imageData.LinearSize * factor : imageData.LinearSize);
                else buffersize = (int)(stream.BaseStream.Length - stream.BaseStream.Position);

                // read the pixel data and then pin it to memory so that the garbage collector
                // doesn't shuffle the data around while OpenGL is decompressing it
                byte[] pixels = stream.ReadBytes(buffersize);
                GCHandle pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);

                try
                {
                    this.TextureTarget = (imageData.Height == 1 || imageData.Width == 1) ? TextureTarget.Texture1D : TextureTarget.Texture2D;
                    this.TextureID = Gl.GenTexture();
                    Gl.BindTexture(TextureTarget, TextureID);
                    Gl.TexParameteri(TextureTarget, TextureParameterName.TextureMinFilter, (int)TextureParam.Linear);
                    Gl.TexParameteri(TextureTarget, TextureParameterName.TextureMagFilter, (int)TextureParam.Linear);

                    int nOffset = 0, nWidth = (int)imageData.Width, nHeight = (int)imageData.Height;

                    for (int i = 0; i < (imageData.MipmapCount == 0 ? 1 : imageData.MipmapCount); ++i)
                    {
                        if (nWidth == 0) nWidth = 1;        // smallest mipmap is 1x1 pixels
                        if (nHeight == 0) nHeight = 1;
                        int nSize = 0;

                        if (compressed)
                        {
                            nSize = ((nWidth + 3) / 4) * ((nHeight + 3) / 4) * blocksize;
                            Gl.CompressedTexImage2D(TextureTarget, i, format, nWidth, nHeight, 0, nSize, (IntPtr)(pinned.AddrOfPinnedObject().ToInt64() + nOffset));
                        }
                        else
                        {
                            nSize = nWidth * nHeight * 4 * ((int)imageData.PixelFormat.RGBBitCount / 8);
                            Gl.TexImage2D(TextureTarget, i, format, nWidth, nHeight, 0, PixelFormat.Bgra, PixelType.UnsignedShort4444, (IntPtr)(pinned.AddrOfPinnedObject().ToInt64() + nOffset));
                        }

                        nOffset += nSize;
                        nWidth /= 2;
                        nHeight /= 2;
                    }
                }
                catch (Exception)
                {   // There was some sort of Dll related error, or the target GPU does not support glCompressedTexImage2DARB
                    throw;
                }
                finally
                {
                    pinned.Free();
                }
            }
        }
        #endregion
    }

    internal class DDS
    {
        #region DirectDraw Surface
        /// <summary>The DirectDraw Surface pixel format.</summary>
        public struct DDS_PIXEL_FORMAT
        {
            /// <summary>Size of the DDS_PIXEL_FORMAT structure.</summary>
            public int Size;
            /// <summary>Pixel format flags.</summary>
            public int Flags;
            /// <summary>The FourCC code for compression identification.</summary>
            public string FourCC;
            /// <summary>The number of bits per pixel.</summary>
            public int RGBBitCount;
            /// <summary>Red bit mask.</summary>
            public int RBitMask;
            /// <summary>Green bit mask.</summary>
            public int GBitMask;
            /// <summary>Blue bit mask.</summary>
            public int BBitMask;
            /// <summary>Alpha bit mask.</summary>
            public int ABitMask;

            /// <summary>Reads a DirectDraw Surface pixel format from the current stream.</summary>
            /// <param name="stream">The stream containing the pixel format.</param>
            public DDS_PIXEL_FORMAT(BinaryReader stream)
            {
                Size = stream.ReadInt32();
                Flags = stream.ReadInt32();
                FourCC = new string(stream.ReadChars(4));
                RGBBitCount = stream.ReadInt32();
                RBitMask = stream.ReadInt32();
                GBitMask = stream.ReadInt32();
                BBitMask = stream.ReadInt32();
                ABitMask = stream.ReadInt32();
            }
        }

        /// <summary>The DirectDraw Surface descriptor.</summary>
        public struct DDSURFACEDESC2
        {
            /// <summary>The size of the DDSURFACEDESC2 structure.</summary>
            public int Size;
            /// <summary>Flags to determine which fields are valid.</summary>
            public int Flags;
            /// <summary>The height (in pixels) of the surface.</summary>
            public int Height;
            /// <summary>The width (in pixels) of the surface.</summary>
            public int Width;
            /// <summary>The scan line size of the surface.</summary>
            public int LinearSize;
            /// <summary>The depth (if applicable).</summary>
            public int Depth;
            /// <summary>The number of mip map levels in this surface.</summary>
            public int MipmapCount;
            private int Reserved0;
            private int Reserved1;
            private int Reserved2;
            private int Reserved3;
            private int Reserved4;
            private int Reserved5;
            private int Reserved6;
            private int Reserved7;
            private int Reserved8;
            private int Reserved9;
            private int Reserved10;
            /// <summary>A pixel format describing the surface.</summary>
            public DDS_PIXEL_FORMAT PixelFormat;
            /// <summary>DDS surface flags.</summary>
            public int SurfaceFlags;
            /// <summary>DDS cubemap flags.</summary>
            public int CubemapFlags;
            private int Reserved11;
            private int Reserved12;
            private int Reserved13;

            public bool IsDXT1 { get { return ((PixelFormat.Flags & 0x04) != 0) && (PixelFormat.FourCC == "DXT1"); } }

            public bool IsDXT3 { get { return ((PixelFormat.Flags & 0x04) != 0) && (PixelFormat.FourCC == "DXT3"); } }

            public bool IsDXT5 { get { return ((PixelFormat.Flags & 0x04) != 0) && (PixelFormat.FourCC == "DXT5"); } }

            //public bool IsBGRA8 { get { return 

            /// <summary>Reads a DirectDraw Surface descriptor from the current stream.</summary>
            /// <param name="stream">The stream containing the descriptor.</param>
            public DDSURFACEDESC2(BinaryReader stream)
            {
                Size = stream.ReadInt32();
                Flags = stream.ReadInt32();
                Height = stream.ReadInt32();
                Width = stream.ReadInt32();
                LinearSize = stream.ReadInt32();
                Depth = stream.ReadInt32();
                MipmapCount = stream.ReadInt32();
                Reserved0 = stream.ReadInt32();
                Reserved1 = stream.ReadInt32();
                Reserved2 = stream.ReadInt32();
                Reserved3 = stream.ReadInt32();
                Reserved4 = stream.ReadInt32();
                Reserved5 = stream.ReadInt32();
                Reserved6 = stream.ReadInt32();
                Reserved7 = stream.ReadInt32();
                Reserved8 = stream.ReadInt32();
                Reserved9 = stream.ReadInt32();
                Reserved10 = stream.ReadInt32();
                PixelFormat = new DDS_PIXEL_FORMAT(stream);
                SurfaceFlags = stream.ReadInt32();
                CubemapFlags = stream.ReadInt32();
                Reserved11 = stream.ReadInt32();
                Reserved12 = stream.ReadInt32();
                Reserved13 = stream.ReadInt32();
            }
        }
        #endregion
    }
}