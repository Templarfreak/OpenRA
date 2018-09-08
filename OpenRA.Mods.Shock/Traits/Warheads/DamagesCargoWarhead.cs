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
using OpenRA.Mods.Common;
using OpenRA.Traits;

/* Works without base engine modification */

namespace OpenRA.Mods.Shock.Warheads
{
	public class DamagesCargoWarhead : DamageWarhead, IRulesetLoaded<WeaponInfo>
	{
		[Desc("Range between falloff steps.")]
		public readonly WDist Spread = new WDist(43);

		[Desc("Damage percentage at each range step")]
		public readonly int[] Falloff = { 100, 37, 14, 5, 0 };

		[Desc("Ranges at which each Falloff step is defined. Overrides Spread.")]
		public WDist[] Range = null;

		[Desc("Cargo types to damage.")]
		public readonly HashSet<string> Types = new HashSet<string>();

		void IRulesetLoaded<WeaponInfo>.RulesetLoaded(Ruleset rules, WeaponInfo info)
		{
			if (Range != null)
			{
				if (Range.Length != 1 && Range.Length != Falloff.Length)
					throw new YamlException("Number of range values must be 1 or equal to the number of Falloff values.");

				for (var i = 0; i < Range.Length - 1; i++)
					if (Range[i] > Range[i + 1])
						throw new YamlException("Range values must be specified in an increasing order.");
			}
			else
				Range = Exts.MakeArray(Falloff.Length, i => i * Spread);
		}

		public override void DoImpact(WPos pos, Actor firedBy, IEnumerable<int> damageModifiers)
		{
			var world = firedBy.World;

			var debugVis = world.WorldActor.TraitOrDefault<DebugVisualizations>();
			if (debugVis != null && debugVis.CombatGeometry)
				world.WorldActor.Trait<WarheadDebugOverlay>().AddImpact(pos, Range, DebugOverlayColor);

			var hitActors = world.FindActorsOnCircle(pos, Range[Range.Length - 1]).Where(a => a.TraitsImplementing<Cargo>().Count() > 0);

			foreach (var cargo_unit in hitActors)
			{
				var cargo_traits = cargo_unit.TraitsImplementing<Cargo>();
				
				foreach (var cargo in cargo_traits)
				{
					if (cargo.Info.Types.Any(t => Types.Any(t2 => t == t2)))
					{
						var targets = cargo.cargo;

						foreach (var victim in targets)
						{
							//Being cargo does weird things to actors, so we have to make sure they still exist.
							if (!victim.Disposed)
							{
								// Cannot be damaged without a Health trait
								var healthInfo = victim.Info.TraitInfoOrDefault<HealthInfo>();
								if (healthInfo == null)
									continue;

								// Cannot be damaged without an active HitShape
								var activeShapes = victim.TraitsImplementing<HitShape>().Where(Exts.IsTraitEnabled);
								if (!activeShapes.Any())
									continue;

								var distance = activeShapes.Min(t => t.Info.Type.DistanceFromEdge(pos, cargo_unit));
								var localModifiers = damageModifiers.Append(GetDamageFalloff(distance.Length));

								DoImpact(victim, firedBy, localModifiers);
							}

						}
					}
				}
			}
		}

		int GetDamageFalloff(int distance)
		{
			var inner = Range[0].Length;
			for (var i = 1; i < Range.Length; i++)
			{
				var outer = Range[i].Length;
				if (outer > distance)
					return int2.Lerp(Falloff[i - 1], Falloff[i], distance - inner, outer - inner);

				inner = outer;
			}

			return 0;
		}
	}
}