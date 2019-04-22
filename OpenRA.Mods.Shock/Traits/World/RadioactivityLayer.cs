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

using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Mods.Shock.Graphics;
using OpenRA.Traits;
using System.Linq;

/* Works without base engine modification */

namespace OpenRA.Mods.Shock.Traits
{
	[Desc("Attach this to the world actor. Radioactivity layer, as in RA2 desolator radioactivity. Order of the layers defines the Z sorting.")]

	// You can attach this layer by editing rules/world.yaml
	// I (boolbada) made this layer by cloning resources layer, as resource amount is quite similar to
	// radio activity. I looked at SmudgeLayer too.
	public class RadioactivityLayerInfo : ITraitInfo
	{
		[Desc("Color of radio activity")]
		public readonly Color Color = Color.FromArgb(0, 255, 0); // tint factor (was in RA2) sucks. Modify tint here statically.

		[Desc("Second color of radio activity, used in conjunction with MixThreshold.")]
		public readonly Color Color2 = Color.Yellow;

		[Desc("Maximum radiation allowable in a cell.The cell can actually have more radiation but it will only damage as if it had the maximum level.")]
		public readonly int MaxLevel = 500;

		[Desc("Delay in ticks between radiation level decrements. The level updates this often, although the whole lifetime will be as defined by half-life.")]
		public readonly int UpdateDelay = 15;

		[Desc("The alpha value for displaying radioactivity level for cells with level == 1")]
		public readonly int Darkest = 4;

		[Desc("The alpha value for displaying radioactivity level for cells with level == MaxLevel")]
		public readonly int Brightest = 64;

		[Desc("Color mix threshold. If alpha level goes beyond this threshold, Color2 will be mixed in.")]
		public readonly int MixThreshold = 36;

		[Desc("Delay of half life, in ticks")]
		public readonly int Halflife = 150;

		[Desc("Z offset of the visualization.")]
		public readonly int ZOffset = -10;

		[Desc("Damage type this layer does. Users can be creative and have different damage for Plutonium, Uranium, or even Anthrax.")]
		public readonly string Name = "radioactivity";

		// Damage dealing is handled by "DamagedByRadioactivity" trait attached at each actor.
		public object Create(ActorInitializer init) { return new RadioactivityLayer(init.Self, this); }
	}

	public class RadioactivityLayer : IWorldLoaded, INotifyActorDisposing, ITick, ITickRender
	{
		readonly World world;
		public readonly RadioactivityLayerInfo Info;

		// In the following, I think dictionary is better than array, as radioactivity has similar affecting area as smudges.

		// true radioactivity values, without considering fog of war.
		readonly Dictionary<CPos, Radioactivity> tiles = new Dictionary<CPos, Radioactivity>();

		// what's visible to the player.
		readonly Dictionary<CPos, Radioactivity> renderedTiles = new Dictionary<CPos, Radioactivity>();

		// dirty, as in cache dirty bits.
		readonly HashSet<CPos> dirty = new HashSet<CPos>();

		// There's LERP function but the problem is, it is better to reuse these constants than computing
		// related constants (in LERP) every time.
		public readonly int K1000; // half life constant, to be computed at init.
		public readonly int Slope100;
		public readonly int YIntercept100;

		public RadioactivityLayer(Actor self, RadioactivityLayerInfo info)
		{
			world = self.World;
			Info = info;
			K1000 = info.UpdateDelay * 693 / info.Halflife; // (693 is 1000*ln(2) so we must divide by 1000 later on.)

			/*
			 * half life decay follows differential equation d/dt m(t) = -k m(t).
			 * d/dt will be in ticks, ofcourse.
			 */

			// rad level visualization constants...
			Slope100 = 100 * (info.Brightest - info.Darkest) / (info.MaxLevel - 1);
			YIntercept100 = 100 * info.Brightest - (info.MaxLevel * Slope100);
		}

		public void Tick(Actor self)
		{
			var remove = new List<CPos>();

			// Apply half life to each cell.
			foreach (var kv in tiles)
			{
				if (!kv.Value.Decay(Info.UpdateDelay))
					continue;

				// Not radioactive anymore. Remove from this.tiles.
				if (kv.Value.Level <= 0)
					remove.Add(kv.Key);

				dirty.Add(kv.Key);
			}

			// Lets actually remove the entry.
			foreach (var r in remove)
				tiles.Remove(r);
		}

		public void WorldLoaded(World w, WorldRenderer wr) { }

		// tick render, regardless of pause state.
		public void TickRender(WorldRenderer wr, Actor self)
		{
			var remove = new List<CPos>();
			foreach (var c in dirty)
			{
				if (self.World.FogObscures(c))
					continue;

				if (renderedTiles.ContainsKey(c))
				{
					world.Remove(renderedTiles[c]);
					renderedTiles.Remove(c);
				}

				// synchronize observations with true value.
				if (tiles.ContainsKey(c))
				{
					renderedTiles[c] = new Radioactivity(tiles[c]);
					world.Add(renderedTiles[c]);
				}

				remove.Add(c);
			}

			foreach (var r in remove)
				dirty.Remove(r);
		}

		// Gets level, for damage calculation.
		public int GetLevel(CPos cell)
		{
			if (!tiles.ContainsKey(cell))
				return 0;

			// The damage is constrained by MaxLevel!!!!
			var level = tiles[cell].Level;
			if (level > Info.MaxLevel)
				return Info.MaxLevel;
			else
				return level;
		}

		public void IncreaseLevel(CPos cell, WorldRenderer wr, int level, int max_level)
		{
			var map = wr.World.Map;
			var tileSet = wr.World.Map.Rules.TileSet;
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

			// Initialize, on fresh impact.
			if (!tiles.ContainsKey(cell))
				tiles[cell] = new Radioactivity(this, screen, world.Map.CenterOfCell(cell));

			tiles[cell].IncreaseLevel(Info.UpdateDelay, level, max_level);
			dirty.Add(cell);
		}

		bool disposed = false;
		public void Disposing(Actor self)
		{
			if (disposed)
				return;

			// dispose all stuff
			disposed = true;
		}
	}
}
