// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Drawing.Tests;

public class BufferedGraphicsContextTests
{
    [Fact]
    public void Ctor_Default()
    {
        using (var context = new BufferedGraphicsContext())
        {
            Assert.Equal(new Size(225, 96), context.MaximumBuffer);
        }
    }

    [Fact]
    public void Allocate_ValidTargetGraphics_Success()
    {
        using (var context = new BufferedGraphicsContext())
        using (var image = new Bitmap(10, 10))
        using (Graphics graphics = Graphics.FromImage(image))
        using (BufferedGraphics bufferedGraphics = context.Allocate(graphics, Rectangle.Empty))
        {
            Assert.NotNull(bufferedGraphics.Graphics);

            context.Invalidate();
        }
    }

    [Fact]
    public void Allocate_SmallRectWithTargetGraphics_Success()
    {
        using (var context = new BufferedGraphicsContext())
        using (var image = new Bitmap(10, 10))
        using (Graphics graphics = Graphics.FromImage(image))
        using (BufferedGraphics bufferedGraphics = context.Allocate(graphics, new Rectangle(0, 0, context.MaximumBuffer.Width - 1, context.MaximumBuffer.Height - 1)))
        {
            Assert.NotNull(bufferedGraphics.Graphics);

            context.Invalidate();
        }
    }

    [Fact]
    public void Allocate_LargeRectWithTargetGraphics_Success()
    {
        using (var context = new BufferedGraphicsContext())
        using (var image = new Bitmap(10, 10))
        using (Graphics graphics = Graphics.FromImage(image))
        using (BufferedGraphics bufferedGraphics = context.Allocate(graphics, new Rectangle(0, 0, context.MaximumBuffer.Width + 1, context.MaximumBuffer.Height + 1)))
        {
            Assert.NotNull(bufferedGraphics.Graphics);

            context.Invalidate();
        }
    }

    [Fact]
    public void Allocate_ValidTargetHdc_Success()
    {
        using (var context = new BufferedGraphicsContext())
        using (var image = new Bitmap(10, 10))
        using (Graphics graphics = Graphics.FromImage(image))
        {
            try
            {
                IntPtr hdc = graphics.GetHdc();
                using (BufferedGraphics bufferedGraphics = context.Allocate(hdc, Rectangle.Empty))
                {
                    Assert.NotNull(bufferedGraphics.Graphics);
                }

                context.Invalidate();
            }
            finally
            {
                graphics.ReleaseHdc();
            }
        }
    }

    [Fact]
    public void Allocate_SmallRectWithTargetHdc_Success()
    {
        using (var context = new BufferedGraphicsContext())
        using (var image = new Bitmap(10, 10))
        using (Graphics graphics = Graphics.FromImage(image))
        {
            try
            {
                IntPtr hdc = graphics.GetHdc();
                using (BufferedGraphics bufferedGraphics = context.Allocate(hdc, new Rectangle(0, 0, context.MaximumBuffer.Width - 1, context.MaximumBuffer.Height - 1)))
                {
                    Assert.NotNull(bufferedGraphics.Graphics);
                }

                context.Invalidate();
            }
            finally
            {
                graphics.ReleaseHdc();
            }
        }
    }

    [Fact]
    public void Allocate_LargeRectWithTargetHdc_Success()
    {
        using (var context = new BufferedGraphicsContext())
        using (var image = new Bitmap(10, 10))
        using (Graphics graphics = Graphics.FromImage(image))
        {
            try
            {
                IntPtr hdc = graphics.GetHdc();
                using (BufferedGraphics bufferedGraphics = context.Allocate(hdc, new Rectangle(0, 0, context.MaximumBuffer.Width + 1, context.MaximumBuffer.Height + 1)))
                {
                    Assert.NotNull(bufferedGraphics.Graphics);
                }

                context.Invalidate();
            }
            finally
            {
                graphics.ReleaseHdc();
            }
        }
    }

    [Fact]
    public void Allocate_InvalidHdc_ThrowsArgumentException()
    {
        using (var context = new BufferedGraphicsContext())
        {
            AssertExtensions.Throws<ArgumentException>(null, () => context.Allocate((IntPtr)(-1), new Rectangle(0, 0, 10, 10)));
        }
    }

    [Fact]
    public void Allocate_NullGraphicsZeroSize_Success()
    {
        using (var context = new BufferedGraphicsContext())
        using (BufferedGraphics graphics = context.Allocate(null, Rectangle.Empty))
        {
            Assert.NotNull(graphics.Graphics);
        }
    }

    [Fact]
    public void Allocate_NullGraphicsNonZeroSize_ThrowsArgumentNullException()
    {
        using (var context = new BufferedGraphicsContext())
        using (var image = new Bitmap(10, 10))
        {
            Assert.Throws<ArgumentNullException>("hdc", () => context.Allocate(null, new Rectangle(0, 0, 10, 10)));
        }
    }

    [Fact]
    public void Allocate_DisposedGraphics_ThrowsArgumentException()
    {
        using (var context = new BufferedGraphicsContext())
        using (var image = new Bitmap(10, 10))
        {
            Graphics graphics = Graphics.FromImage(image);
            graphics.Dispose();

            Rectangle largeRectangle = new Rectangle(0, 0, context.MaximumBuffer.Width + 1, context.MaximumBuffer.Height + 1);
            AssertExtensions.Throws<ArgumentException>(null, () => context.Allocate(graphics, largeRectangle));
            AssertExtensions.Throws<ArgumentException>(null, () => context.Allocate(graphics, Rectangle.Empty));
        }
    }

    [Fact]
    public void Allocate_BusyGraphics_ThrowsInvalidOperationException()
    {
        using (var context = new BufferedGraphicsContext())
        using (var image = new Bitmap(10, 10))
        using (Graphics graphics = Graphics.FromImage(image))
        {
            try
            {
                graphics.GetHdc();

                Rectangle largeRectangle = new Rectangle(0, 0, context.MaximumBuffer.Width + 1, context.MaximumBuffer.Height + 1);
                Assert.Throws<InvalidOperationException>(() => context.Allocate(graphics, largeRectangle));
                Assert.Throws<InvalidOperationException>(() => context.Allocate(graphics, Rectangle.Empty));
            }
            finally
            {
                graphics.ReleaseHdc();
            }
        }
    }

    [Fact]
    public void Invalidate_CallMultipleTimes_Success()
    {
        using (var context = new BufferedGraphicsContext())
        {
            context.Invalidate();
            context.Invalidate();
        }
    }

    [Fact]
    public void MaximumBuffer_SetValid_ReturnsExpected()
    {
        using (var context = new BufferedGraphicsContext())
        {
            context.MaximumBuffer = new Size(10, 10);
            Assert.Equal(new Size(10, 10), context.MaximumBuffer);

            context.MaximumBuffer = new Size(255, 255);
            Assert.Equal(new Size(255, 255), context.MaximumBuffer);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaximumBuffer_SetInvalidWidth_ThrowsArgumentException(int width)
    {
        using (var context = new BufferedGraphicsContext())
        {
            AssertExtensions.Throws<ArgumentException>("value", null, () => context.MaximumBuffer = new Size(width, 1));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaximumBuffer_SetInvalidHeight_ThrowsArgumentException(int height)
    {
        using (var context = new BufferedGraphicsContext())
        {
            AssertExtensions.Throws<ArgumentException>("value", null, () => context.MaximumBuffer = new Size(1, height));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateBufferedGraphicsContext() => new BufferedGraphicsContext();

    [Fact]
    public void Finalize_Invoke_Success()
    {
        // This makes sure than finalization doesn't cause any errors or debug assertions.
        AllocateBufferedGraphicsContext();

        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    [Fact]
    public void Dispose_BusyAndValidated_ThrowsInvalidOperationException()
    {
        using (var context = new BufferedGraphicsContext())
        using (var image = new Bitmap(10, 10))
        using (Graphics graphics = Graphics.FromImage(image))
        {
            using (context.Allocate(graphics, Rectangle.Empty))
            {
                Assert.Throws<InvalidOperationException>(() => context.Dispose());
            }
        }
    }

    [Fact]
    public void Dispose_BusyAndInvalidated_ThrowsInvalidOperationException()
    {
        using (var context = new BufferedGraphicsContext())
        using (var image = new Bitmap(10, 10))
        using (Graphics graphics = Graphics.FromImage(image))
        {
            using (context.Allocate(graphics, Rectangle.Empty))
            {
                context.Invalidate();
                Assert.Throws<InvalidOperationException>(() => context.Dispose());
            }
        }
    }
}
