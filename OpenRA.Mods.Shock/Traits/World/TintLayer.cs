#region Copyright & License Information
/*
 * Radioactivity layer by Boolbada of OP Mod.
 * Started off from Resource layer by OpenRA devs but required intensive rewrite...
 * 
 * Copyright 2007-2017 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Drawing;
using OpenRA.Graphics;
using System.Linq;
using OpenRA.Mods.Shock.Graphics;
using OpenRA.Traits;
using Shock.Extensions;

/* Works without base engine modification */

namespace OpenRA.Mods.Shock.Traits
{
	[Desc("Attach this to the world actor. Radioactivity layer, as in RA2 desolator radioactivity. Order of the layers defines the Z sorting.")]

	// You can attach this layer by editing rules/world.yaml
	// I (boolbada) made this layer by cloning resources layer, as resource amount is quite similar to
	// radio activity. I looked at SmudgeLayer too.
	public class TintLayerInfo : ITraitInfo
	{
		[Desc("Z offset of the visualization.")]
		public readonly int ZOffset = -10;

		[Desc("The name of this tint layer, to distinguish between multiples.")]
		public readonly string Name = "tint";

		// Damage dealing is handled by "DamagedByRadioactivity" trait attached at each actor.
		public object Create(ActorInitializer init) { return new TintLayer(init.Self, this); }
	}

	public class TintLayer : IWorldLoaded, ITickRender
	{
		readonly World world;
		public readonly TintLayerInfo Info;

		// In the following, I think dictionary is better than array, as radioactivity has similar affecting area as smudges.

		// true radioactivity values, without considering fog of war.
		readonly Dictionary<CPos, Tint> tiles = new Dictionary<CPos, Tint>();

		// what's visible to the player.
		readonly Dictionary<CPos, Tint> renderedTiles = new Dictionary<CPos, Tint>();

		public TintLayer(Actor self, TintLayerInfo info)
		{
			world = self.World;
			Info = info;
		}

		public void WorldLoaded(World w, WorldRenderer wr) { }

		// tick render, regardless of pause state.
		public void TickRender(WorldRenderer wr, Actor self)
		{
			foreach (var c in tiles)
			{
				if (self.World.FogObscures(c.Key))
					continue;

				if (renderedTiles.ContainsKey(c.Key))
				{
					world.Remove(renderedTiles[c.Key]);
					renderedTiles.Remove(c.Key);
				}

				// synchronize observations with true value.
				if (tiles.ContainsKey(c.Key))
				{
					renderedTiles[c.Key] = new Tint(tiles[c.Key]);
					world.Add(renderedTiles[c.Key]);
				}
			}
		}

		public void TintCell(CPos cell, WorldRenderer wr, Color col, Color col2, int level, int max_level, int slope, int yintercept, int max, int mix)
		{
			// Initialize, on fresh impact.
			if (!tiles.ContainsKey(cell))
			{
				var map = wr.World.Map;
				var tileSet = world.Map.Rules.TileSet;
				var uv = map.CellContaining(world.Map.CenterOfCell(cell)).ToMPos(map);

				if (!map.Height.Contains(uv))
					return;

				var height = (int)map.Height[uv];
				var tile = map.Tiles[uv];
				var ti = tileSet.GetTileInfo(tile);
				var ramp = ti != null ? ti.RampType : 0;

				var corners = map.Grid.CellCorners[ramp];
				var pos = map.CenterOfCell(uv.ToCPos(map));
				var screen = corners.Select(c => wr.Screen3DPxPosition(pos + c)).ToArray();

				tiles[cell] = new Tint(this, screen, world.Map.CenterOfCell(cell), level, col, col2, slope, yintercept, max, mix);
			}
			else
			{
				Color new_col = Color.FromArgb(Math.Max(col.A, tiles[cell].col.A), Blending.BlendRGB(col, tiles[cell].col));
				Color new_col2 = Color.FromArgb(Math.Max(col2.A, tiles[cell].col2.A), Blending.BlendRGB(col2, tiles[cell].col2));

				tiles[cell].col = new_col;
				tiles[cell].col2 = new_col2;
				tiles[cell].MixThreshold = Math.Max(mix, tiles[cell].MixThreshold);
			}
				
		}

	}
}