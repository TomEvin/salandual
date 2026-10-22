// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Imaging/DecodedImage.cs

namespace Salandual.Imaging;

public sealed class DecodedImage
{
    public DecodedImage(int width, int height, byte[] pixels, int fullWidth, int fullHeight, string decoder)
    {
        if (width <= 0 || height <= 0 || pixels.Length != (long)width * height * 4)
        {
            throw new ArgumentException("The pixels don't match the size.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
        FullWidth = Math.Max(fullWidth, width);
        FullHeight = Math.Max(fullHeight, height);
        Decoder = decoder;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public int FullWidth { get; }

    public int FullHeight { get; }

    public string Decoder { get; }

    public bool IsPreview { get; init; }

    public bool IsReduced => Width < FullWidth || Height < FullHeight;

    public double Density => Math.Min(1, (double)Width / FullWidth);
}

public sealed class ImageDecodeException(string message, Exception? inner = null) : Exception(message, inner);
