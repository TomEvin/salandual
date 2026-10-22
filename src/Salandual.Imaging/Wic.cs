// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Imaging/Wic.cs

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace Salandual.Imaging;

internal static unsafe class Wic
{
    private static readonly Lock _gate = new();
    private static IWICImagingFactory* _factory;

    [ThreadStatic]
    private static bool _comReady;

    private static IWICImagingFactory* Factory
    {
        get
        {
            if (!_comReady)
            {
                PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
                _comReady = true;
            }

            lock (_gate)
            {
                if (_factory is null)
                {
                    var clsid = PInvoke.CLSID_WICImagingFactory2;
                    var iid = IWICImagingFactory.IID_Guid;
                    IWICImagingFactory* f;
                    PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, (void**)&f).ThrowOnFailure();
                    _factory = f;
                }

                return _factory;
            }
        }
    }

    public static DecodedImage DecodeFile(string path, int maxWidth, int maxHeight, bool exifOrientation, bool largestFrame)
    {
        var factory = Factory;
        IWICBitmapDecoder* decoder = null;
        try
        {
            fixed (char* p = path)
            {
                factory->CreateDecoderFromFilename(p, null, GENERIC_ACCESS_RIGHTS.GENERIC_READ, WICDecodeOptions.WICDecodeMetadataCacheOnDemand, &decoder).ThrowOnFailure();
            }

            return Decode(decoder, maxWidth, maxHeight, exifOrientation ? null : 1, largestFrame);
        }
        finally
        {
            Release(decoder);
        }
    }

    public static DecodedImage DecodeBytes(byte[] data, int maxWidth, int maxHeight, int orientation)
    {
        var factory = Factory;
        IWICStream* stream = null;
        IWICBitmapDecoder* decoder = null;
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            factory->CreateStream(&stream).ThrowOnFailure();
            stream->InitializeFromMemory((byte*)pin.AddrOfPinnedObject(), (uint)data.Length).ThrowOnFailure();
            factory->CreateDecoderFromStream((IStream*)stream, null, WICDecodeOptions.WICDecodeMetadataCacheOnDemand, &decoder).ThrowOnFailure();
            return Decode(decoder, maxWidth, maxHeight, orientation, largestFrame: false);
        }
        finally
        {
            Release(decoder);
            Release(stream);
            pin.Free();
        }
    }

    public static DecodedImage Scale(DecodedImage image, int maxWidth, int maxHeight)
    {
        var (w, h) = Fit(image.Width, image.Height, maxWidth, maxHeight);
        if (w == image.Width && h == image.Height)
        {
            return image;
        }

        var factory = Factory;
        IWICBitmap* bitmap = null;
        IWICBitmapScaler* scaler = null;
        try
        {
            var format = PInvoke.GUID_WICPixelFormat32bppPBGRA;
            fixed (byte* p = image.Pixels)
            {
                factory->CreateBitmapFromMemory((uint)image.Width, (uint)image.Height, &format, (uint)image.Width * 4, (uint)(image.Width * image.Height * 4), p, &bitmap).ThrowOnFailure();
            }

            factory->CreateBitmapScaler(&scaler).ThrowOnFailure();
            scaler->Initialize((IWICBitmapSource*)bitmap, (uint)w, (uint)h, WICBitmapInterpolationMode.WICBitmapInterpolationModeFant).ThrowOnFailure();
            return new DecodedImage(w, h, Copy((IWICBitmapSource*)scaler, w, h), image.FullWidth, image.FullHeight, image.Decoder) { IsPreview = image.IsPreview };
        }
        finally
        {
            Release(scaler);
            Release(bitmap);
        }
    }

    internal static (int Width, int Height) Fit(int width, int height, int maxWidth, int maxHeight)
    {
        if (maxWidth <= 0 || maxHeight <= 0)
        {
            maxWidth = maxHeight = ImageDecoder.MaxSide;
        }

        maxWidth = Math.Min(maxWidth, ImageDecoder.MaxSide);
        maxHeight = Math.Min(maxHeight, ImageDecoder.MaxSide);
        if (width <= maxWidth && height <= maxHeight)
        {
            return (width, height);
        }

        var scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static DecodedImage Decode(IWICBitmapDecoder* decoder, int maxWidth, int maxHeight, int? orientation, bool largestFrame)
    {
        var factory = Factory;
        IWICBitmapFrameDecode* frame = null;
        IWICBitmapScaler* scaler = null;
        IWICBitmapFlipRotator* rotator = null;
        IWICFormatConverter* converter = null;
        try
        {
            decoder->GetFrame(largestFrame ? LargestFrame(decoder) : 0, &frame).ThrowOnFailure();
            uint w, h;
            ((IWICBitmapSource*)frame)->GetSize(&w, &h).ThrowOnFailure();
            var turn = orientation ?? ReadOrientation(frame);
            var swapped = turn is >= 5 and <= 8;
            var (fullW, fullH) = swapped ? ((int)h, (int)w) : ((int)w, (int)h);
            var (outW, outH) = Fit(fullW, fullH, maxWidth, maxHeight);
            if ((long)outW * outH > ImageDecoder.MaxPixels)
            {
                throw new ImageDecodeException(Salandual.Core.Resources.Strings.Image_TooBig);
            }

            var source = (IWICBitmapSource*)frame;
            if (outW != fullW || outH != fullH)
            {
                factory->CreateBitmapScaler(&scaler).ThrowOnFailure();
                var (sw, sh) = swapped ? (outH, outW) : (outW, outH);
                scaler->Initialize(source, (uint)sw, (uint)sh, WICBitmapInterpolationMode.WICBitmapInterpolationModeFant).ThrowOnFailure();
                source = (IWICBitmapSource*)scaler;
            }

            if (turn is >= 2 and <= 8)
            {
                factory->CreateBitmapFlipRotator(&rotator).ThrowOnFailure();
                rotator->Initialize(source, Transform(turn)).ThrowOnFailure();
                source = (IWICBitmapSource*)rotator;
            }

            factory->CreateFormatConverter(&converter).ThrowOnFailure();
            var format = PInvoke.GUID_WICPixelFormat32bppPBGRA;
            converter->Initialize(source, &format, WICBitmapDitherType.WICBitmapDitherTypeNone, null, 0, WICBitmapPaletteType.WICBitmapPaletteTypeMedianCut).ThrowOnFailure();
            return new DecodedImage(outW, outH, Copy((IWICBitmapSource*)converter, outW, outH), fullW, fullH, "WIC: " + DecoderName(decoder));
        }
        finally
        {
            Release(converter);
            Release(rotator);
            Release(scaler);
            Release(frame);
        }
    }

    internal static WICBitmapTransformOptions Transform(int orientation) => orientation switch
    {
        2 => WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal,
        3 => WICBitmapTransformOptions.WICBitmapTransformRotate180,
        4 => WICBitmapTransformOptions.WICBitmapTransformFlipVertical,
        5 => WICBitmapTransformOptions.WICBitmapTransformRotate270 | WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal,
        6 => WICBitmapTransformOptions.WICBitmapTransformRotate90,
        7 => WICBitmapTransformOptions.WICBitmapTransformRotate90 | WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal,
        8 => WICBitmapTransformOptions.WICBitmapTransformRotate270,
        _ => WICBitmapTransformOptions.WICBitmapTransformRotate0,
    };

    private static uint LargestFrame(IWICBitmapDecoder* decoder)
    {
        uint count;
        if (decoder->GetFrameCount(&count).Failed || count <= 1)
        {
            return 0;
        }

        uint best = 0;
        long bestArea = -1;
        for (uint i = 0; i < Math.Min(count, 64u); i++)
        {
            IWICBitmapFrameDecode* frame = null;
            try
            {
                uint w, h;
                if (decoder->GetFrame(i, &frame).Succeeded && ((IWICBitmapSource*)frame)->GetSize(&w, &h).Succeeded && (long)w * h > bestArea)
                {
                    (best, bestArea) = (i, (long)w * h);
                }
            }
            finally
            {
                Release(frame);
            }
        }

        return best;
    }

    private static int ReadOrientation(IWICBitmapFrameDecode* frame)
    {
        IWICMetadataQueryReader* reader = null;
        try
        {
            if (frame->GetMetadataQueryReader(&reader).Failed || reader is null)
            {
                return 1;
            }

            PROPVARIANT value = default;
            HRESULT hr;
            fixed (char* name = "System.Photo.Orientation")
            {
                hr = reader->GetMetadataByName(name, &value);
            }

            if (hr.Failed)
            {
                return 1;
            }

            var orientation = value.Anonymous.Anonymous.vt == VARENUM.VT_UI2 ? value.Anonymous.Anonymous.Anonymous.uiVal : 1;
            PInvoke.PropVariantClear(&value);
            return orientation is >= 1 and <= 8 ? orientation : 1;
        }
        finally
        {
            Release(reader);
        }
    }

    private static byte[] Copy(IWICBitmapSource* source, int width, int height)
    {
        var pixels = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
        fixed (byte* p = pixels)
        {
            source->CopyPixels(null, (uint)width * 4, (uint)pixels.Length, p).ThrowOnFailure();
        }

        return pixels;
    }

    private static string DecoderName(IWICBitmapDecoder* decoder)
    {
        IWICBitmapDecoderInfo* info = null;
        try
        {
            if (decoder->GetDecoderInfo(&info).Failed)
            {
                return "?";
            }

            var buffer = stackalloc char[128];
            uint length;
            return ((IWICComponentInfo*)info)->GetFriendlyName(128, new PWSTR(buffer), &length).Succeeded && length > 0 ? new string(buffer, 0, (int)length - 1) : "?";
        }
        finally
        {
            Release(info);
        }
    }

    private static void Release<T>(T* p)
        where T : unmanaged
    {
        if (p is not null)
        {
            ((IUnknown*)p)->Release();
        }
    }
}
