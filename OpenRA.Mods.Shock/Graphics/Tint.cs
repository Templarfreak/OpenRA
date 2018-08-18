#region Copyright & License Information
/*
 * Radioactivity class by Boolbada of OP Mod
 * This one I made from scratch xD
 * As an OpenRA module, this module follows GPLv3 license:
 * 
 * Copyright 2007-2017 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
 
#endregion

using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OpenRA.Effects;
using OpenRA.Graphics;
using System.Drawing.Imaging;
using OpenRA.Mods.Shock.Traits;

/*
Works without base engine modification
*/


namespace OpenRA.Mods.Shock.Graphics
{
	class Tint : IRenderable, IFinalizedRenderable, IEffect
	{
		public int Ticks = 0;
		public int MixThreshold;

		readonly TintLayer layer;

		readonly WPos wpos;

		public Color col;
		public Color col2;

		public float3[] screen;

		public int ZOffset
		{
			get
			{
				return layer.Info.ZOffset;
			}
		}

		public Tint(TintLayer layer, float3[] corners, WPos wpos, int level, Color col, Color col2, int Slope, int YIntercept, int Max, int Mix)
		{
			this.wpos = wpos;
			this.layer = layer;
			this.col = col;
			this.col2 = col2;
			screen = corners;
			MixThreshold = Mix;
		}

		public Tint(Tint src)
		{
			Ticks = src.Ticks;
			MixThreshold = src.MixThreshold;
			screen = src.screen;
			layer = src.layer;
			wpos = src.wpos;
			col = src.col;
			col2 = src.col2;
		}

		public IRenderable WithPalette(PaletteReference newPalette) { return this; }
		public IRenderable WithZOffset(int newOffset) { return this; }
		public IRenderable OffsetBy(WVec vec) { return this; }
		public IRenderable AsDecoration() { return this; }

		public PaletteReference Palette { get { return null; } }
		public bool IsDecoration { get { return false; } }
		public WPos Pos { get { return wpos; } }

		IFinalizedRenderable IRenderable.PrepareRender(WorldRenderer wr)
		{
			return this;
		}

		public void Render(WorldRenderer wr)
		{
			float3 zoffset = new float3(0, 0, ZOffset);

			Game.Renderer.WorldRgbaColorRenderer.FillTriangle(screen[0] + zoffset, screen[1] + zoffset, screen[2] + zoffset, col);
			Game.Renderer.WorldRgbaColorRenderer.FillTriangle(screen[2] + zoffset, screen[3] + zoffset, screen[0] + zoffset, col);

			// mix in yellow so that the radion shines brightly, after certain threshold.
			// It is different than tinting the info.color itself and provides nicer look.
			if (col.A > MixThreshold)
			{
				Game.Renderer.WorldRgbaColorRenderer.FillTriangle(screen[0] + zoffset, screen[1] + zoffset, screen[2] + zoffset, col2);
				Game.Renderer.WorldRgbaColorRenderer.FillTriangle(screen[2] + zoffset, screen[3] + zoffset, screen[0] + zoffset, col2);
			}
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }

		public void Tick(World world) { }

		IEnumerable<IRenderable> IEffect.Render(WorldRenderer r)
		{
			yield return this;
		}
	}
}
