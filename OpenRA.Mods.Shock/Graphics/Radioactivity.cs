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
using System.Linq;
using OpenRA.Effects;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Mods.Shock.Traits;

/*
Works without base engine modification
*/


namespace OpenRA.Mods.Shock.Graphics
{
	class Radioactivity : IRenderable, IFinalizedRenderable, IEffect
	{
		public int Ticks = 0;
		public int Level = 0;

		readonly RadioactivityLayer layer;
		readonly WPos wpos;

		public float3[] screen;

		public int ZOffset
		{
			get
			{
				return layer.Info.ZOffset;
			}
		}

		public Radioactivity(RadioactivityLayer layer, float3[] corners, WPos wpos)
		{
			this.wpos = wpos;
			this.layer = layer;
			screen = corners;
		}

		public Radioactivity(Radioactivity src)
		{
			Ticks = src.Ticks;
			Level = src.Level;
			layer = src.layer;
			wpos = src.wpos;
			screen = src.screen;
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
			int level = this.Level.Clamp(0, layer.Info.MaxLevel); // Saturate the visualization to MaxLevel
			if (level == 0)
				return; // don't visualize 0 cells. They show up before cells get removed.

			float crunch = (((float)(level) / layer.Info.MaxLevel) * layer.Info.Brightest);
			int alpha = (int)(crunch);
			alpha = alpha.Clamp((int)(0 + (float)(layer.Info.Darkest)), 255); // Just to be safe.

			Color newcolor = Color.FromArgb(alpha, layer.Info.Color);
			float3 zoffset = new float3(0, 0, ZOffset);

			Game.Renderer.WorldRgbaColorRenderer.FillTriangle(screen[0] + zoffset, screen[1] + zoffset, screen[2] + zoffset, Color.FromArgb(alpha, layer.Info.Color));
			Game.Renderer.WorldRgbaColorRenderer.FillTriangle(screen[2] + zoffset, screen[3] + zoffset, screen[0] + zoffset, Color.FromArgb(alpha, layer.Info.Color));

			// mix in yellow so that the radion shines brightly, after certain threshold.
			// It is different than tinting the info.color itself and provides nicer look.
			if (alpha > layer.Info.MixThreshold)
			{
				Game.Renderer.WorldRgbaColorRenderer.FillTriangle(screen[0] + zoffset, screen[1] + zoffset, screen[2] + zoffset, Color.FromArgb(16, layer.Info.Color2));
				Game.Renderer.WorldRgbaColorRenderer.FillTriangle(screen[2] + zoffset, screen[3] + zoffset, screen[0] + zoffset, Color.FromArgb(16, layer.Info.Color2));
			}
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }

		public void Tick(World world)
		{
		}

		// Returns true when "dirty" (RA value changed)
		public bool Decay(int updateDelay)
		{
			Ticks--; // count half-life.
			if (Ticks > 0)
				return false;

			/* on each half life...
			 * ra.ticks = info.Halflife; // reset ticks
			 * ra.level /= 2; // simple is best haha...
			 * Looks unnatural and induces "flickers"
			 */
			 

			Ticks = updateDelay; // reset ticks
			int dlevel = layer.K1000 * Level / 1000;
			if (dlevel < 1)
				dlevel = 1; // must decrease by at least 1 so that the contamination disappears eventually.
			Level -= dlevel;

			return true;
		}

		public void IncreaseLevel(int updateDelay, int level, int max_level)
		{
			Ticks = updateDelay;

			/// The code below may look odd but consider that each weapon may have a different max_level.

			var new_level = Level + level;
			if (new_level > max_level)
				new_level = max_level;

			if (Level > new_level)
				return; // the given weapon can't make the cell more radio active. (saturate)

			Level = new_level;
		}

		IEnumerable<IRenderable> IEffect.Render(WorldRenderer r)
		{
			yield return this;
		}
	}
}
