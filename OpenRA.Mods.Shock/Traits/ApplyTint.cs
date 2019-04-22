#region Copyright & License Information
/*
 * Modded by Boolbada of OP Mod.
 * Modded from CreateResourceWarhead by OpenRA devs.
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
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

/* Modifications to World and WorldRenderer necessary. */

namespace OpenRA.Mods.Shock.Traits
{
	public class ApplyTintInfo : ConditionalTraitInfo, IRulesetLoaded
	{
		[Desc("Range between falloff steps, in cells")]
		public readonly int Spread = 1;

		[Desc("Tint level percentage at each range step")]
		public readonly int[] Falloff = { 100, 37, 14, 5, 0 };

		[Desc("The color to tint to.")]
		public readonly Color Color = Color.FromArgb(0, 255, 0); // tint factor (was in RA2) sucks. Modify tint here statically.

		[Desc("Second color to tint, used in conjunction with MixThreshold.")]
		public readonly Color Color2 = Color.Yellow;

		[Desc("Maximum tint allowable in a cell. The cell can actually have more radiation but it will only tint as if it had the maximum " +
			"level.")]
		public readonly int MaxLevel = 500;

		[Desc("The alpha value for displaying tint level for cells with level == 1")]
		public readonly int Darkest = 4;

		[Desc("The alpha value for displaying tint level for cells with level == MaxLevel")]
		public readonly int Brightest = 64;

		[Desc("Color mix threshold. If alpha level goes beyond this threshold, Color2 will be mixed in.")]
		public readonly int MixThreshold = 36;

		[Desc("Mix in the second color?")]
		public readonly bool DoMix = false;

		// Since tint level is accumulative, we pre-compute this var from Falloff. (Lookup table)
		int[] falloffDifference;

		[Desc("Ranges at which each Falloff step is defined (in cells). Overrides Spread.")]
		public int[] Range = null;

		[Desc("Tint level this actor puts on the ground.")]
		public int Level = 32;

		[Desc("The name of the layer we want to increase tint level.")]
		public readonly string TintLayerName = "tint";

		//We need WorldRenderer.
		public WorldRenderer wr = null;

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (Range == null)
				Range = Exts.MakeArray(Falloff.Length, i => i * Spread);
			else
			{
				if (Range.Length != 1 && Range.Length != Falloff.Length)
					throw new YamlException("Number of range values must be 1 or equal to the number of Falloff values.");

				for (var i = 0; i < Range.Length - 1; i++)
					if (Range[i] > Range[i + 1])
						throw new YamlException("Range values must be specified in an increasing order.");
			}

			// Compute FalloffDifference LUT.
			falloffDifference = new int[Falloff.Length];

			for (var i = 0; i < falloffDifference.Length - 1; i++)
			{
				// with Falloff = { 100, 37, 14, 5, 0 }, you get
				// { 63, 23, 9, 5, 0 }
				falloffDifference[i] = Falloff[i] - Falloff[i + 1];
			}

			falloffDifference[falloffDifference.Length - 1] = Falloff.Last();
		}

		public override object Create(ActorInitializer init) { return new ApplyTint(init.Self, this); }
	}

	public class ApplyTint : INotifyCreated
	{
		public readonly ApplyTintInfo Info;
		// Since radioactivity level is accumulative, we pre-compute this var from Falloff. (Lookup table)
		int[] falloffDifference;
		Actor self;

		public readonly int Slope100;
		public readonly int YIntercept100;

		public ApplyTint(Actor self, ApplyTintInfo info)
		{
			Info = info;
			this.self = self;

			// rad level visualization constants...
			Slope100 = 100 * (info.Brightest - info.Darkest) / (info.MaxLevel - 1);
			YIntercept100 = 100 * info.Brightest - (info.MaxLevel * Slope100);
		}

		void INotifyCreated.Created(Actor self)
		{
			RunTint(self.World);
		}

		void RunTint (World w)
		{
			// Compute FalloffDifference LUT.
			falloffDifference = new int[Info.Falloff.Length];

			for (var i = 0; i < falloffDifference.Length - 1; i++)
			{
				// with Falloff = { 100, 37, 14, 5, 0 }, you get
				// { 63, 23, 9, 5, 0 }
				falloffDifference[i] = Info.Falloff[i] - Info.Falloff[i + 1];
			}

			falloffDifference[falloffDifference.Length - 1] = Info.Falloff.Last();

			var world = w;
			var pos = self.CenterPosition;
			var targetTile = world.Map.CellContaining(pos);
			List<CPos> allcells = new List<CPos>();

			for (var i = 0; i < Info.Range.Length; i++)
			{
				// Find affected cells, from outer Range down to inner range.
				var affectedCells = world.Map.FindTilesInAnnulus(targetTile, 0, Info.Range[i]);
				

				// DamagedByRadioactivity trait will make sure that the radioactivity layer whth this name is unique.
				// We omit thorough check here.
				var raLayer = world.WorldActor.TraitsImplementing<TintLayer>()
					.First(l => l.Info.Name == Info.TintLayerName);

				foreach (var cell in affectedCells)
				{
					if (!allcells.Contains(cell))
					{
						SetTintLevels(cell, Info.Color, Info.Color2, falloffDifference[i], Info.Falloff[i], raLayer, Slope100, YIntercept100);
						allcells.Add(cell);
					}
					
				}

			}
		}

		// Increase radiation level of the cell at given pos, considering falloff
		void SetTintLevels(CPos pos, Color col, Color col2, int foffDiff, int foff, TintLayer tLayer, int Slope, int YIntercept)
		{
			int l = Info.Level * foff / 100;
			int m = Info.MaxLevel * foff / 100;
			int level = l.Clamp(0, m); // Saturate the visualization to MaxLevel
			//int alpha = (YIntercept + Slope * level) / 100; // Linear interpolation
			float crunch = (((float)(level) / Info.MaxLevel) * (Info.Brightest));
			int alpha = (int)(crunch);
			alpha = alpha.Clamp((int)(0 + (float)(Info.Darkest)), 255); // Just to be safe.

			Color new_col1 = Color.FromArgb(alpha, col);
			Color new_col2 = Color.FromArgb(alpha, col2);

			var mix  = 0;

			if (!Info.DoMix)
			{
				mix = 1000;
			}

			tLayer.TintCell(pos, new_col1, new_col2, l, m, Slope, YIntercept, 
				Info.MaxLevel, Info.MixThreshold + mix);
		}
	}
}
