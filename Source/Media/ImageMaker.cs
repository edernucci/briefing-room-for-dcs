﻿/*
==========================================================================
This file is part of Briefing Room for DCS World, a mission
generator for DCS World, by @akaAgar (https://github.com/akaAgar/briefing-room-for-dcs)

Briefing Room for DCS World is free software: you can redistribute it
and/or modify it under the terms of the GNU General Public License
as published by the Free Software Foundation, either version 3 of
the License, or (at your option) any later version.

Briefing Room for DCS World is distributed in the hope that it will
be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with Briefing Room for DCS World. If not, see https://www.gnu.org/licenses/
==========================================================================
*/

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace BriefingRoom4DCSWorld.Media
{
    public class ImageMaker : IDisposable
    {
        /// <summary>
        /// Size of images generated by <see cref="ImageMaker"/>. DCS World title images are all 512×512px.
        /// </summary>
        public const int IMAGE_SIZE = 512;

        /// <summary>
        /// Background color to use for all drawn images.
        /// </summary>
        public Color BackgroundColor { get; set; } = Color.Black;

        /// <summary>
        /// Text to draw over all images generated by this <see cref="ImageMaker"/>.
        /// </summary>
        public ImageMakerTextOverlay TextOverlay { get; } = new ImageMakerTextOverlay();

        /// <summary>
        /// Constructor.
        /// </summary>
        public ImageMaker() { }

        /// <summary>
        /// Generates an image from an array of middle-center aligned images with no transformations of offset.
        /// </summary>
        /// <param name="imageFiles">Image files (from <see cref="BRPaths.INCLUDE_JPG"/>, from the bottom to the top</param>
        /// <returns>Bytes of a JPG image</returns>
        public byte[] GetImageBytes(params string[] imageFiles)
        {
            return GetImageBytes((from string f in imageFiles select new ImageMakerLayer(f)).ToArray());
        }

        /// <summary>
        /// Generates an image from an aray of <see cref="ImageMakerLayer"/>.
        /// </summary>
        /// <param name="imageLayers">Image layers, from the bottom to the top</param>
        /// <returns>Bytes of a JPG image</returns>
        public byte[] GetImageBytes(params ImageMakerLayer[] imageLayers)
        {
            Bitmap bitmap = new Bitmap(IMAGE_SIZE, IMAGE_SIZE);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(BackgroundColor);

                // Draw all layers of the image
                foreach (ImageMakerLayer layer in imageLayers)
                {
                    string filePath = $"{BRPaths.INCLUDE_JPG}{layer.FileName}";
                    if (!File.Exists(filePath)) continue; // File doesn't exist, ignore it.

                    using (Image layerImage = Image.FromFile(filePath))
                    {
                        Point position = GetImagePosition(layer.Alignment, layer.OffsetX, layer.OffsetY, layerImage.Size);

                        RotateGraphics(graphics, layer.Rotation);
                        graphics.DrawImage(layerImage,
                                new Rectangle(position, layerImage.Size),
                                new Rectangle(Point.Empty, layerImage.Size),
                                GraphicsUnit.Pixel);
                        RotateGraphics(graphics, -layer.Rotation);
                    }
                }

                // Draw the text overlay
                TextOverlay.Draw(graphics);
            }

            // Coverts the image to a JPG and get all bytes
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Jpeg);
                bytes = ms.ToArray();
            }

            return bytes;
        }

        /// <summary>
        /// Rotates a <see cref="Graphics"/> around its center.
        /// </summary>
        /// <param name="graphics"><see cref="Graphics"/ to rotate</param>
        /// <param name="rotationInDegrees">Rotation in degrees</param>
        private void RotateGraphics(Graphics graphics, int rotationInDegrees)
        {
            if (rotationInDegrees == 0) return;

            graphics.TranslateTransform(IMAGE_SIZE / 2, IMAGE_SIZE / 2);
            graphics.RotateTransform(rotationInDegrees);
            graphics.TranslateTransform(-IMAGE_SIZE / 2, -IMAGE_SIZE / 2);
        }

        /// <summary>
        /// Returns the exact position of an image according to the data in an <see cref="ImageMakerLayer"/>.
        /// </summary>
        /// <param name="alignment">Alignment of the image on the layer</param>
        /// <param name="offsetX">X-offset of the image</param>
        /// <param name="offsetY">Y-offset of the image</param>
        /// <param name="size">Size of the image to draw, in pixels</param>
        /// <returns>Position of the image</returns>
        private Point GetImagePosition(ContentAlignment alignment, int offsetX, int offsetY, Size size)
        {
            int x, y;

            if (alignment.ToString().EndsWith("Left")) x = 0;
            else if (alignment.ToString().EndsWith("Right")) x = IMAGE_SIZE - size.Width;
            else x = (IMAGE_SIZE - size.Width) / 2;

            if (alignment.ToString().StartsWith("Top")) y = 0;
            else if (alignment.ToString().StartsWith("Bottom")) y = IMAGE_SIZE - size.Height;
            else y = (IMAGE_SIZE - size.Height) / 2;

            return new Point(x + offsetX, y + offsetY);
        }

        /// <summary>
        /// <see cref="IDisposable"/> implementation.
        /// </summary>
        public void Dispose() { }
    }
}