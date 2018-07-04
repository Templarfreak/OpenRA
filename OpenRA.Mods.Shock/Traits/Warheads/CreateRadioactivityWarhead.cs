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
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Warheads;
using OpenRA.Mods.Shock.Traits;
using OpenRA.Traits;

/* Works without base engine modification */

namespace OpenRA.Mods.Shock.Warheads
{
	public class CreateRadioactivityWarhead : DamageWarhead, IRulesetLoaded<WeaponInfo>
	{
		[Desc("Range between falloff steps, in cells")]
		public readonly int Spread = 1;

		[Desc("Radioactivity level percentage at each range step")]
		public readonly int[] Falloff = { 100, 37, 14, 5, 0 };

		// Since radioactivity level is accumulative, we pre-compute this var from Falloff. (Lookup table)
		int[] falloffDifference;

		[Desc("Ranges at which each Falloff step is defined (in cells). Overrides Spread.")]
		public int[] Range = null;

		[Desc("Radio activity level this weapon puts on the ground. Accumulates over previously contaminated area. (Sievert?)")]
		public int Level = 32; // in RA2, they used 500 for most weapons

		[Desc("Radio activity saturates at this level, by this weapon.")]
		public int MaxLevel = 500;

		[Desc("The name of the layer we want to increase radioactivity level.")]
		public readonly string RadioactivityLayerName = "radioactivity";

		public void RulesetLoaded(Ruleset rules, WeaponInfo info)
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

		public override void DoImpact(WPos pos, Actor firedBy, IEnumerable<int> damageModifiers)
		{
			var world = firedBy.World;

			// Accumulate radiation
			var targetTile = world.Map.CellContaining(pos);
			for (var i = 0; i < Range.Length; i++)
			{
				// Find affected cells, from outer Range down to inner range.
				var affectedCells = world.Map.FindTilesInAnnulus(targetTile, 0, Range[i]);

				// DamagedByRadioactivity trait will make sure that the radioactivity layer whth this name is unique.
				// We omit thorough check here.
				var raLayer = world.WorldActor.TraitsImplementing<RadioactivityLayer>()
					.First(l => l.Info.Name == RadioactivityLayerName);

				foreach (var cell in affectedCells)
					IncreaseRALevel(cell, falloffDifference[i], Falloff[i], raLayer);
			}
		}

		// Increase radiation level of the cell at given pos, considering falloff
		void IncreaseRALevel(CPos pos, int foffDiff, int foff, RadioactivityLayer raLayer)
		{
			// increase RA level of the cell by this amount.
			// Apply fall off to MaxLevel, because if we keep desolator there for very long,
			// all cells get saturated and doesn't look good.
			raLayer.IncreaseLevel(pos, Level * foffDiff / 100, MaxLevel * foff / 100);
		}
	}
}