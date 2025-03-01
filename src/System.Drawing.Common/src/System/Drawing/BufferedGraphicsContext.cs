﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using static Interop;

namespace System.Drawing;

/// <summary>
/// The BufferedGraphicsContext class can be used to perform standard double buffer rendering techniques.
/// </summary>
public sealed class BufferedGraphicsContext : IDisposable
{
    private Size _maximumBuffer;
    private Size _bufferSize = Size.Empty;
    private Size _virtualSize;
    private Point _targetLoc;
    private IntPtr _compatDC;
    private IntPtr _dib;
    private IntPtr _oldBitmap;
    private Graphics? _compatGraphics;
    private BufferedGraphics? _buffer;
    private int _busy;
    private bool _invalidateWhenFree;

    private const int BufferFree = 0; // The graphics buffer is free to use.
    private const int BufferBusyPainting = 1; // The graphics buffer is busy being created/painting.
    private const int BufferBusyDisposing = 2; // The graphics buffer is busy disposing.

    /// <summary>
    /// Basic constructor.
    /// </summary>
    public BufferedGraphicsContext()
    {
        // By defualt, the size of our maxbuffer will be 3 x standard button size.
        _maximumBuffer.Width = 75 * 3;
        _maximumBuffer.Height = 32 * 3;
    }

    /// <summary>
    /// Allows you to set the maximum width and height of the buffer that will be retained in memory.
    /// You can allocate a buffer of any size, however any request for a buffer that would have a total
    /// memory footprint larger that the maximum size will be allocated temporarily and then discarded
    /// with the BufferedGraphics is released.
    /// </summary>
    public Size MaximumBuffer
    {
        get => _maximumBuffer;
        set
        {
            if (value.Width <= 0 || value.Height <= 0)
            {
                throw new ArgumentException(SR.Format(SR.InvalidArgumentValue, nameof(MaximumBuffer), value), nameof(value));
            }

            // If we've been asked to decrease the size of the maximum buffer,
            // then invalidate the older & larger buffer.
            if (value.Width * value.Height < _maximumBuffer.Width * _maximumBuffer.Height)
            {
                Invalidate();
            }

            _maximumBuffer = value;
        }
    }

    ~BufferedGraphicsContext() => Dispose(false);

    /// <summary>
    /// Returns a BufferedGraphics that is matched for the specified target Graphics object.
    /// </summary>
    public BufferedGraphics Allocate(Graphics targetGraphics, Rectangle targetRectangle)
    {
        if (ShouldUseTempManager(targetRectangle))
        {
            return AllocBufferInTempManager(targetGraphics, IntPtr.Zero, targetRectangle);
        }

        return AllocBuffer(targetGraphics, IntPtr.Zero, targetRectangle);
    }

    /// <summary>
    /// Returns a BufferedGraphics that is matched for the specified target HDC object.
    /// </summary>
    public BufferedGraphics Allocate(IntPtr targetDC, Rectangle targetRectangle)
    {
        if (ShouldUseTempManager(targetRectangle))
        {
            return AllocBufferInTempManager(null, targetDC, targetRectangle);
        }

        return AllocBuffer(null, targetDC, targetRectangle);
    }

    /// <summary>
    /// Returns a BufferedGraphics that is matched for the specified target HDC object.
    /// </summary>
    private BufferedGraphics AllocBuffer(Graphics? targetGraphics, IntPtr targetDC, Rectangle targetRectangle)
    {
        int oldBusy = Interlocked.CompareExchange(ref _busy, BufferBusyPainting, BufferFree);

        // In the case were we have contention on the buffer - i.e. two threads
        // trying to use the buffer at the same time, we just create a temp
        // buffermanager and have the buffer dispose of it when it is done.
        //
        if (oldBusy != BufferFree)
        {
            return AllocBufferInTempManager(targetGraphics, targetDC, targetRectangle);
        }

        Graphics surface;
        _targetLoc = new Point(targetRectangle.X, targetRectangle.Y);

        try
        {
            if (targetGraphics is not null)
            {
                IntPtr destDc = targetGraphics.GetHdc();
                try
                {
                    surface = CreateBuffer(destDc, targetRectangle.Width, targetRectangle.Height);
                }
                finally
                {
                    targetGraphics.ReleaseHdcInternal(destDc);
                }
            }
            else
            {
                surface = CreateBuffer(targetDC, targetRectangle.Width, targetRectangle.Height);
            }

            _buffer = new BufferedGraphics(surface, this, targetGraphics, targetDC, _targetLoc, _virtualSize);
        }
        catch
        {
            // Free the buffer so it can be disposed.
            _busy = BufferFree;
            throw;
        }

        return _buffer;
    }

    /// <summary>
    /// Returns a BufferedGraphics that is matched for the specified target HDC object.
    /// </summary>
    private static BufferedGraphics AllocBufferInTempManager(Graphics? targetGraphics, IntPtr targetDC, Rectangle targetRectangle)
    {
        BufferedGraphicsContext? tempContext = null;
        BufferedGraphics? tempBuffer = null;

        try
        {
            tempContext = new BufferedGraphicsContext();
            tempBuffer = tempContext.AllocBuffer(targetGraphics, targetDC, targetRectangle);
            tempBuffer.DisposeContext = true;
        }
        finally
        {
            if (tempContext is not null && tempBuffer is not { DisposeContext: true })
            {
                tempContext.Dispose();
            }
        }

        return tempBuffer;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// This routine allows us to control the point were we start using throw away
    /// managers for painting. Since the buffer manager stays around (by default)
    /// for the life of the app, we don't want to consume too much memory
    /// in the buffer. However, re-allocating the buffer for small things (like
    /// buttons, labels, etc) will hit us on runtime performance.
    /// </summary>
    private bool ShouldUseTempManager(Rectangle targetBounds)
    {
        return (targetBounds.Width * targetBounds.Height) > (MaximumBuffer.Width * MaximumBuffer.Height);
    }

    /// <summary>
    /// Fills in the fields of a BITMAPINFO so that we can create a bitmap
    /// that matches the format of the display.
    ///
    /// This is done by creating a compatible bitmap and calling GetDIBits
    /// to return the color masks. This is done with two calls. The first
    /// call passes in biBitCount = 0 to GetDIBits which will fill in the
    /// base BITMAPINFOHEADER data. The second call to GetDIBits (passing
    /// in the BITMAPINFO filled in by the first call) will return the color
    /// table or bitmasks, as appropriate.
    /// </summary>
    /// <returns>True if successful, false otherwise.</returns>
    private unsafe bool FillBitmapInfo(IntPtr hdc, IntPtr hpal, ref Gdi32.BITMAPINFO_FLAT pbmi)
    {
        IntPtr hbm = IntPtr.Zero;
        bool bRet = false;
        try
        {
            // Create a dummy bitmap from which we can query color format info
            // about the device surface.
            hbm = Gdi32.CreateCompatibleBitmap(new HandleRef(null, hdc), 1, 1);

            if (hbm == IntPtr.Zero)
            {
                throw new OutOfMemoryException(SR.GraphicsBufferQueryFail);
            }

            pbmi.bmiHeader_biSize = sizeof(NativeMethods.BITMAPINFOHEADER);

            // Call first time to fill in BITMAPINFO header.
            Gdi32.GetDIBits(
                new HandleRef(null, hdc),
                new HandleRef(null, hbm),
                0,
                0,
                IntPtr.Zero,
                ref pbmi,
                NativeMethods.DIB_RGB_COLORS);

            if (pbmi.bmiHeader_biBitCount <= 8)
            {
                bRet = FillColorTable(hdc, hpal, ref pbmi);
            }
            else
            {
                if (pbmi.bmiHeader_biCompression == NativeMethods.BI_BITFIELDS)
                {
                    // Call a second time to get the color masks.
                    Gdi32.GetDIBits(
                        new HandleRef(null, hdc),
                        new HandleRef(null, hbm),
                        0,
                        pbmi.bmiHeader_biHeight,
                        IntPtr.Zero,
                        ref pbmi,
                        NativeMethods.DIB_RGB_COLORS);
                }

                bRet = true;
            }
        }
        finally
        {
            if (hbm != IntPtr.Zero)
            {
                Gdi32.DeleteObject(hbm);
            }
        }

        return bRet;
    }

    /// <summary>
    /// Initialize the color table of the BITMAPINFO pointed to by pbmi. Colors
    /// are set to the current system palette.
    ///
    /// Note: call only valid for displays of 8bpp or less.
    /// </summary>
    /// <returns>True is successful, false otherwise.</returns>
    private unsafe bool FillColorTable(IntPtr hdc, IntPtr hpal, ref Gdi32.BITMAPINFO_FLAT pbmi)
    {
        byte[] aj = new byte[sizeof(NativeMethods.PALETTEENTRY) * 256];

        fixed (byte* pcolors = pbmi.bmiColors)
        {
            fixed (byte* ppal = aj)
            {
                NativeMethods.RGBQUAD* prgb = (NativeMethods.RGBQUAD*)pcolors;
                NativeMethods.PALETTEENTRY* lppe = (NativeMethods.PALETTEENTRY*)ppal;

                int cColors = 1 << pbmi.bmiHeader_biBitCount;
                if (cColors <= 256)
                {
                    // Note: we don't support 4bpp displays.
                    uint palRet;
                    IntPtr palHalftone = IntPtr.Zero;
                    if (hpal == IntPtr.Zero)
                    {
                        palHalftone = Graphics.GetHalftonePalette();
                        palRet = Gdi32.GetPaletteEntries(new HandleRef(null, palHalftone), 0, cColors, aj);
                    }
                    else
                    {
                        palRet = Gdi32.GetPaletteEntries(new HandleRef(null, hpal), 0, cColors, aj);
                    }

                    if (palRet != 0)
                    {
                        for (int i = 0; i < cColors; i++)
                        {
                            prgb[i].rgbRed = lppe[i].peRed;
                            prgb[i].rgbGreen = lppe[i].peGreen;
                            prgb[i].rgbBlue = lppe[i].peBlue;
                            prgb[i].rgbReserved = 0;
                        }

                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a Graphics object representing a buffer.
    /// </summary>
    private Graphics CreateBuffer(IntPtr src, int width, int height)
    {
        // Create the compat DC.
        _busy = BufferBusyDisposing;
        DisposeDC();
        _busy = BufferBusyPainting;
        _compatDC = Gdi32.CreateCompatibleDC(src);

        // Recreate the bitmap if necessary.
        if (width > _bufferSize.Width || height > _bufferSize.Height)
        {
            int optWidth = Math.Max(width, _bufferSize.Width);
            int optHeight = Math.Max(height, _bufferSize.Height);

            _busy = BufferBusyDisposing;
            DisposeBitmap();
            _busy = BufferBusyPainting;

            IntPtr pvbits = IntPtr.Zero;
            _dib = CreateCompatibleDIB(src, IntPtr.Zero, optWidth, optHeight, ref pvbits);
            _bufferSize = new Size(optWidth, optHeight);
        }

        // Select the bitmap.
        _oldBitmap = Kernel32.SelectObject(new HandleRef(this, _compatDC), new HandleRef(this, _dib));

        // Create compat graphics.
        _compatGraphics = Graphics.FromHdcInternal(_compatDC);
        _compatGraphics.TranslateTransform(-_targetLoc.X, -_targetLoc.Y);
        _virtualSize = new Size(width, height);

        return _compatGraphics;
    }

    /// <summary>
    /// Create a DIB section with an optimal format w.r.t. the specified hdc.
    ///
    /// If DIB &lt;= 8bpp, then the DIB color table is initialized based on the
    /// specified palette. If the palette handle is NULL, then the system
    /// palette is used.
    ///
    /// Note: The hdc must be a direct DC (not an info or memory DC).
    ///
    /// Note: On palettized displays, if the system palette changes the
    ///       UpdateDIBColorTable function should be called to maintain
    ///       the identity palette mapping between the DIB and the display.
    /// </summary>
    /// <returns>A valid bitmap handle if successful, IntPtr.Zero otherwise.</returns>
    private IntPtr CreateCompatibleDIB(IntPtr hdc, IntPtr hpal, int ulWidth, int ulHeight, ref IntPtr ppvBits)
    {
        if (hdc == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(hdc));
        }

        IntPtr hbmRet = IntPtr.Zero;
        Gdi32.BITMAPINFO_FLAT pbmi = default;

        // Validate hdc.
        Gdi32.ObjectType objType = Gdi32.GetObjectType(hdc);
        switch (objType)
        {
            case Gdi32.ObjectType.OBJ_DC:
            case Gdi32.ObjectType.OBJ_METADC:
            case Gdi32.ObjectType.OBJ_MEMDC:
            case Gdi32.ObjectType.OBJ_ENHMETADC:
                break;
            default:
                throw new ArgumentException(SR.DCTypeInvalid);
        }

        if (FillBitmapInfo(hdc, hpal, ref pbmi))
        {
            // Change bitmap size to match specified dimensions.
            pbmi.bmiHeader_biWidth = ulWidth;
            pbmi.bmiHeader_biHeight = ulHeight;
            if (pbmi.bmiHeader_biCompression == NativeMethods.BI_RGB)
            {
                pbmi.bmiHeader_biSizeImage = 0;
            }
            else
            {
                if (pbmi.bmiHeader_biBitCount == 16)
                {
                    pbmi.bmiHeader_biSizeImage = ulWidth * ulHeight * 2;
                }
                else if (pbmi.bmiHeader_biBitCount == 32)
                {
                    pbmi.bmiHeader_biSizeImage = ulWidth * ulHeight * 4;
                }
                else
                {
                    pbmi.bmiHeader_biSizeImage = 0;
                }
            }

            pbmi.bmiHeader_biClrUsed = 0;
            pbmi.bmiHeader_biClrImportant = 0;

            // Create the DIB section. Let Win32 allocate the memory and return
            // a pointer to the bitmap surface.
            hbmRet = Gdi32.CreateDIBSection(new HandleRef(null, hdc), ref pbmi, NativeMethods.DIB_RGB_COLORS, ref ppvBits, IntPtr.Zero, 0);
            Win32Exception? ex = null;
            if (hbmRet == IntPtr.Zero)
            {
                ex = new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (ex is not null)
            {
                throw ex;
            }
        }

        return hbmRet;
    }

    /// <summary>
    /// Disposes the DC, but leaves the bitmap alone.
    /// </summary>
    private void DisposeDC()
    {
        if (_oldBitmap != IntPtr.Zero && _compatDC != IntPtr.Zero)
        {
            Kernel32.SelectObject(new HandleRef(this, _compatDC), new HandleRef(this, _oldBitmap));
            _oldBitmap = IntPtr.Zero;
        }

        if (_compatDC != IntPtr.Zero)
        {
            Gdi32.DeleteDC(new HandleRef(this, _compatDC));
            _compatDC = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Disposes the bitmap, will ASSERT if bitmap is being used (checks oldbitmap). if ASSERTed, call DisposeDC() first.
    /// </summary>
    private void DisposeBitmap()
    {
        if (_dib != IntPtr.Zero)
        {
            Debug.Assert(_oldBitmap == IntPtr.Zero);

            Gdi32.DeleteObject(new HandleRef(this, _dib));
            _dib = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Disposes of the Graphics buffer.
    /// </summary>
    private void Dispose(bool disposing)
    {
        int oldBusy = Interlocked.CompareExchange(ref _busy, BufferBusyDisposing, BufferFree);

        if (disposing)
        {
            if (oldBusy == BufferBusyPainting)
            {
                throw new InvalidOperationException(SR.GraphicsBufferCurrentlyBusy);
            }

            if (_compatGraphics is not null)
            {
                _compatGraphics.Dispose();
                _compatGraphics = null;
            }
        }

        DisposeDC();
        DisposeBitmap();

        if (_buffer is not null)
        {
            _buffer.Dispose();
            _buffer = null;
        }

        _bufferSize = Size.Empty;
        _virtualSize = Size.Empty;

        _busy = BufferFree;
    }

    /// <summary>
    /// Invalidates the cached graphics buffer.
    /// </summary>
    public void Invalidate()
    {
        int oldBusy = Interlocked.CompareExchange(ref _busy, BufferBusyDisposing, BufferFree);

        // If we're not busy with our buffer, lets clean it up now
        if (oldBusy == BufferFree)
        {
            Dispose();
            _busy = BufferFree;
        }
        else
        {
            // This will indicate to free the buffer as soon as it becomes non-busy.
            _invalidateWhenFree = true;
        }
    }

    /// <summary>
    /// Returns a Graphics object representing a buffer.
    /// </summary>
    internal void ReleaseBuffer()
    {
        _buffer = null;
        if (_invalidateWhenFree)
        {
            // Clears everything including the bitmap.
            _busy = BufferBusyDisposing;
            Dispose();
        }
        else
        {
            // Otherwise, just dispose the DC. A new one will be created next time.
            _busy = BufferBusyDisposing;

            // Only clears out the DC.
            DisposeDC();
        }

        _busy = BufferFree;
    }
}
