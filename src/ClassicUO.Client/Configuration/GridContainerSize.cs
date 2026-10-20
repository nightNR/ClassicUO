// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.Configuration
{
    public sealed class GridContainerSize
    {
        public int Width { get; set; }
        public int Height { get; set; }

        public GridContainerSize() { }

        public GridContainerSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }
}
